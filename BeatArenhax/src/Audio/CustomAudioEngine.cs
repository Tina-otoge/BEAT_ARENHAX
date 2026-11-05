using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BeatArenhax.CustomSongs;
using BeatArenhax.DTX;

namespace BeatArenhax.Audio
{
    /// <summary>Owns the mixer, decodes DTX audio and plays the currently armed chart.</summary>
    public sealed class CustomAudioEngine : IDisposable
    {
        private readonly Settings _s;
        private readonly Action<string> _log;
        private readonly Dictionary<string, AudioBuffer> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, double> _durations = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();
        private readonly object _warningLock = new();
        private readonly HashSet<string> _loggedWarnings = new(StringComparer.OrdinalIgnoreCase);
        private Task _prefetchTask = Task.CompletedTask;
        private readonly object _prefetchLock = new();
        private int _previewGen;

        public AudioMixer Mixer { get; }
        public CustomSong ArmedSong { get; private set; }
        public ConvertedChart ArmedChart { get; private set; }
        private Dictionary<int, (AudioBuffer buf, float vol, float pan)> _armedWavs = [];

        public CustomAudioEngine(Settings s, Action<string> log)
        {
            _s = s;
            _log = log;
            Mixer = new AudioMixer(s.AudioLatencyMs) { MasterVolume = s.MasterVolume, OffsetMs = s.AudioOffsetMs };
            if (Mixer.Error != null) _log("Audio: " + Mixer.Error);
            if (Mixer.IsAvailable) _log($"Audio output ready: {Mixer.SampleRate} Hz, latency {s.AudioLatencyMs} ms");
        }

        public void Dispose() => Mixer.Dispose();

        public double DurationMs(string path)
        {
            lock (_durations)
            {
                if (_durations.TryGetValue(path, out double duration)) return duration;
            }
            double v = AudioDecoder.GetDurationMs(path);
            lock (_durations) _durations[path] = v;
            return v;
        }

        public ConvertedSong GetConverted(CustomSong song)
        {
            ConvertedSong converted = song.GetConverted(_s, DurationMs);
            foreach (ConvertedChart chart in new[] { converted.Casual, converted.Advanced })
            {
                foreach (string warning in chart.Warnings)
                {
                    string key = song.RelativeFolder + "\n" + warning;
                    lock (_warningLock)
                    {
                        if (!_loggedWarnings.Add(key)) continue;
                    }
                    _log("DTX " + song.Title + ": " + warning);
                }
            }
            return converted;
        }

        private AudioBuffer GetBuffer(string path)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(path, out AudioBuffer cachedBuffer)) return cachedBuffer;
            }
            AudioBuffer buf;
            try { buf = AudioDecoder.Decode(path, Mixer.SampleRate); }
            catch (Exception e) { _log("Cannot decode " + path + ": " + e.Message); buf = AudioBuffer.Empty; }
            lock (_lock) _cache[path] = buf;
            return buf;
        }

        /// <summary>Converts a song and decodes its sounds in the background.</summary>
        public void Prefetch(CustomSong song)
        {
            ConvertedSong converted = GetConverted(song);
            PrefetchAudio(song, converted, null);
        }

        private void PrefetchAudio(CustomSong song, ConvertedSong converted, ConvertedChart selectedChart)
        {
            lock (_lock)
            {
                string keep = song.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    + Path.DirectorySeparatorChar;
                List<string> stalePaths = [.. _cache.Keys.Where(path => !path.StartsWith(keep, StringComparison.OrdinalIgnoreCase))];
                foreach (string path in stalePaths) _ = _cache.Remove(path);
            }

            ConvertedChart[] charts = selectedChart == null
                ? [converted.Casual, converted.Advanced]
                : [selectedChart];
            HashSet<string> paths = new(StringComparer.OrdinalIgnoreCase);
            foreach (ConvertedChart chart in charts)
            {
                foreach (int wavId in chart.UsedWavIds)
                {
                    string path = chart.Source.ResolveWavPath(wavId);
                    if (path == null) continue;
                    _ = paths.Add(path);
                }
            }

            lock (_prefetchLock)
            {
                Task previous = _prefetchTask;
                _prefetchTask = Task.Run(() =>
                {
                    try { previous.Wait(); } catch { }
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    try
                    {
                        _ = Parallel.ForEach(paths,
                            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) },
                            path => GetBuffer(path));
                        _log($"Prepared '{song.Title}': {paths.Count} sounds, {stopwatch.ElapsedMilliseconds} ms");
                    }
                    catch (Exception e)
                    {
                        _log("Prefetch failed for " + song.Folder + ": " + e);
                    }
                });
            }
        }

        /// <summary>Selects the chart whose sounds are used by <see cref="PlayChip"/> and <see cref="StartSong"/>.</summary>
        public ConvertedChart Arm(CustomSong song, int difficulty)
        {
            ConvertedSong converted = GetConverted(song);
            ConvertedChart chart = converted.Get(difficulty);
            if (ArmedSong == song && ArmedChart == chart) return chart;
            ArmedSong = song;
            ArmedChart = chart;
            _armedWavs = [];
            PrefetchAudio(song, converted, chart);
            return chart;
        }

        public void ReleaseArmedChart()
        {
            ArmedSong?.ReleaseConverted();
            ArmedSong = null;
            ArmedChart = null;
            _armedWavs.Clear();
        }

        private (AudioBuffer buf, float vol, float pan) GetWav(int wavId)
        {
            if (_armedWavs.TryGetValue(wavId, out (AudioBuffer buf, float vol, float pan) wav)) return wav;
            ConvertedChart chart = ArmedChart;
            if (chart == null) return (null, 0, 0);
            string path = chart.Source.ResolveWavPath(wavId);
            _ = chart.Source.Wavs.TryGetValue(wavId, out DtxWav definition);
            wav = (path != null ? GetBuffer(path) : AudioBuffer.Empty,
                (definition?.Volume ?? 100) / 100f, (definition?.Pan ?? 0) / 100f);
            _armedWavs[wavId] = wav;
            return wav;
        }

        public bool StartSong(float volume)
        {
            ConvertedChart chart = ArmedChart;
            if (chart == null) { Mixer.StartSong(null, 0); return false; }
            Task prefetchTask;
            lock (_prefetchLock) prefetchTask = _prefetchTask;
            if (!prefetchTask.IsCompleted)
            {
                _log("Waiting for audio decoding...");
                _ = prefetchTask.Wait(TimeSpan.FromSeconds(60));
            }
            List<SongEvent> events = new(chart.Schedule.Count);
            foreach (ScheduledSound sound in chart.Schedule)
            {
                (AudioBuffer buf, float vol, float pan) = GetWav(sound.WavId);
                if (buf == null || buf.Frames == 0) continue;
                float scheduledVolume = vol * (sound.FromNote ? _s.DrumVolume : _s.BgmVolume);
                (float left, float right) = AudioMixer.PanGains(scheduledVolume, pan);
                events.Add(new SongEvent { Frame = (long)(sound.TimeMs * Mixer.SampleRate / 1000.0), Buffer = buf, GainL = left, GainR = right, PolyKey = sound.WavId });
            }
            Mixer.StartSong(events, volume);
            return true;
        }

        public void PlayChip(int wavId, float volume)
        {
            (AudioBuffer buf, float vol, float pan) = GetWav(wavId);
            if (buf == null) return;
            Mixer.PlayOneShot(buf, vol * volume * _s.DrumVolume, pan, wavId);
        }

        public void PlayPreview(CustomSong song, float volume)
        {
            int gen = Interlocked.Increment(ref _previewGen);
            _ = Task.Run(() =>
            {
                try
                {
                    AudioBuffer buf = null;
                    if (song.PreviewPath != null)
                        buf = AudioDecoder.Decode(song.PreviewPath, Mixer.SampleRate, 0, 30);
                    else
                    {
                        DtxChart chart = DtxChart.Load(song.AdvancedChart.Path);
                        string bgm = chart.Chips.Where(chip => chip.Channel == 0x01)
                            .OrderBy(chip => chip.Measure)
                            .ThenBy(chip => chip.Frac)
                            .Select(chip => chart.ResolveWavPath(chip.Value))
                            .FirstOrDefault(audioPath => audioPath != null);
                        if (bgm != null)
                        {
                            double len = DurationMs(bgm) / 1000.0;
                            buf = AudioDecoder.Decode(bgm, Mixer.SampleRate, Math.Max(0, len * 0.35), 20);
                        }
                    }
                    if (gen == Volatile.Read(ref _previewGen) && buf != null)
                        Mixer.PlayPreview(buf, volume * _s.BgmVolume);
                }
                catch (Exception e) { _log("Preview failed: " + e.Message); }
            });
        }

        public void StopPreview()
        {
            _ = Interlocked.Increment(ref _previewGen);
            Mixer.StopPreview();
        }
    }
}
