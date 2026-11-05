using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BeatArenhax.Audio
{
    public struct SongEvent
    {
        public long Frame;
        public AudioBuffer Buffer;
        public float GainL, GainR;
        public int PolyKey;
    }

    /// <summary>
    /// Small software mixer on a shared-mode WASAPI stream. Plays a "song" (a list of scheduled sounds that
    /// follows a pausable/seekable timeline) plus one-shot sounds (hit sounds) and a looping preview.
    /// </summary>
    public sealed class AudioMixer : ISampleProvider, IDisposable
    {
        public const int PolyphonyPerSound = AudioVoiceRenderer.PolyphonyPerSound;

        private readonly ConcurrentQueue<Action> _cmds = new();
        private readonly object _clockLock = new();
        private readonly AudioVoiceRenderer _voiceRenderer;
        private WasapiOut _out;
        private readonly int _outChannels;
        private float[] _mix = [];

        // song state (audio thread)
        private List<SongEvent> _events;
        private int _nextEvent;
        private bool _songPlaying, _songPaused;
        private float _songVolume = 1f;

        // clock (guarded by _clockLock)
        private long _rendered;
        private long _songPos;
        private bool _clockSongActive;
        private int _songGeneration;

        public int SampleRate { get; }
        public WaveFormat WaveFormat { get; }
        public bool IsAvailable => _out != null;
        public float MasterVolume = 1f;
        public float OneShotVolume = 1f;
        public int OffsetMs;
        public string Error;

        public AudioMixer(int latencyMs)
        {
            int rate = 48000, ch = 2;
            try
            {
                using MMDeviceEnumerator en = new();
                using MMDevice dev = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                WaveFormat mf = dev.AudioClient.MixFormat;
                rate = mf.SampleRate;
                ch = Math.Max(2, mf.Channels);
            }
            catch (Exception e) { Error = "Mix format query failed: " + e.Message; }
            SampleRate = rate;
            _outChannels = ch;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, ch);
            _voiceRenderer = new AudioVoiceRenderer(rate);
            try
            {
                _out = new WasapiOut(AudioClientShareMode.Shared, true, latencyMs);
                _out.Init(this);
                _out.Play();
            }
            catch (Exception e)
            {
                Error = "WASAPI init failed: " + e;
                try { _out?.Dispose(); } catch { }
                _out = null;
            }
        }

        public void Dispose()
        {
            try { _out?.Stop(); _out?.Dispose(); } catch { }
            _out = null;
        }

        public static (float l, float r) PanGains(float volume, float pan)
        {
            pan = Math.Clamp(pan, -1f, 1f);
            double a = (pan + 1) * Math.PI / 4;
            return ((float)(volume * Math.Cos(a) * Math.Sqrt(2)), (float)(volume * Math.Sin(a) * Math.Sqrt(2)));
        }

        // ---------------- public API (any thread) ----------------

        public void PlayOneShot(AudioBuffer buf, float volume, float pan, int polyKey)
        {
            if (buf == null || buf.Frames == 0) return;
            (float l, float r) = PanGains(volume, pan);
            _cmds.Enqueue(() => _voiceRenderer.Add(buf, l, r, polyKey));
        }

        public void StartSong(List<SongEvent> events, float volume)
        {
            lock (_clockLock) { _songPos = 0; _clockSongActive = true; _songGeneration++; }
            _cmds.Enqueue(() =>
            {
                _voiceRenderer.RemoveSongVoices();
                _events = events ?? [];
                _nextEvent = 0;
                _songPlaying = true;
                _songPaused = false;
                _songVolume = volume;
                lock (_clockLock) _songPos = 0;
            });
        }

        public void StopSong()
        {
            lock (_clockLock) { _clockSongActive = false; }
            _cmds.Enqueue(() =>
            {
                _songPlaying = false;
                _events = null;
                _voiceRenderer.FadeSongVoices(30f);
            });
        }

        public void PauseSong()
        {
            double now = SongTimeMs;
            lock (_clockLock) { SongTimeMs = now; _frozen = true; }
            _cmds.Enqueue(() => { if (_songPlaying) _songPaused = true; });
        }

        public void ResumeSong() => _cmds.Enqueue(() => { _songPaused = false; lock (_clockLock) _frozen = false; });

        private bool _frozen;

        public void SetSongVolume(float v) => _cmds.Enqueue(() => _songVolume = v);

        public void SeekSong(double ms)
        {
            long target = Math.Max(0, (long)(ms * SampleRate / 1000.0));
            _cmds.Enqueue(() =>
            {
                if (_events == null) return;
                _voiceRenderer.RemoveSongVoices();
                lock (_clockLock) _songPos = target;
                _nextEvent = 0;
                while (_nextEvent < _events.Count && _events[_nextEvent].Frame <= target)
                {
                    SongEvent e = _events[_nextEvent++];
                    long offset = target - e.Frame;
                    if (offset >= e.Buffer.Frames) continue;
                    _voiceRenderer.Add(e.Buffer, e.GainL, e.GainR, e.PolyKey, isSong: true, position: (int)offset);
                }
            });
        }

        public bool SongActive { get { lock (_clockLock) return _clockSongActive; } }
        public int SongGeneration { get { lock (_clockLock) return _songGeneration; } }

        /// <summary>Song position that is currently audible, in ms.</summary>
        public double SongTimeMs
        {
            get
            {
                long played = DevicePositionFrames();
                lock (_clockLock)
                {
                    if (!_clockSongActive) return 0;
                    if (_frozen) return field;
                    long latency = played >= 0 ? Math.Max(0, _rendered - played) : 0;
                    double ms = ((_songPos - latency) * 1000.0 / SampleRate) - OffsetMs;
                    return ms < 0 ? 0 : ms;
                }
            }

            private set;
        }

        public void PlayPreview(AudioBuffer buf, float volume) => _cmds.Enqueue(() =>
                                                                           {
                                                                               _voiceRenderer.FadePreviewVoices(200f);
                                                                               if (buf == null || buf.Frames == 0) return;
                                                                               _voiceRenderer.Add(buf, volume, volume, -1, loop: true, isPreview: true,
                                                                                   fade: 0f, fadeStep: 1f / (SampleRate * 0.3f));
                                                                           });

        public void StopPreview() => _cmds.Enqueue(() => _voiceRenderer.FadePreviewVoices(200f));

        // ---------------- audio thread ----------------

        private long DevicePositionFrames()
        {
            try
            {
                WasapiOut o = _out;
                return o == null ? -1 : o.GetPosition() / (4L * _outChannels);
            }
            catch { return -1; }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            while (_cmds.TryDequeue(out Action c))
            {
                try { c(); } catch { }
            }
            int frames = count / _outChannels;
            if (_mix.Length < frames * 2) _mix = new float[frames * 2];
            Array.Clear(_mix, 0, frames * 2);

            bool songRunning = _songPlaying && !_songPaused;
            long songPos;
            lock (_clockLock) songPos = _songPos;

            if (songRunning && _events != null)
            {
                long end = songPos + frames;
                while (_nextEvent < _events.Count && _events[_nextEvent].Frame < end)
                {
                    SongEvent e = _events[_nextEvent++];
                    long delay = e.Frame - songPos;
                    int pos = 0;
                    if (delay < 0) { pos = (int)Math.Min(int.MaxValue, -delay); delay = 0; }
                    if (pos >= e.Buffer.Frames) continue;
                    _voiceRenderer.Add(e.Buffer, e.GainL, e.GainR, e.PolyKey,
                        isSong: true, position: pos, delay: delay);
                }
            }

            _voiceRenderer.Render(_mix, frames, songRunning, _songVolume, OneShotVolume);

            float mv = MasterVolume;
            for (int fi = 0; fi < frames; fi++)
            {
                float l = _mix[fi * 2] * mv, r = _mix[(fi * 2) + 1] * mv;
                if (l > 1f) l = 1f; else if (l < -1f) l = -1f;
                if (r > 1f) r = 1f; else if (r < -1f) r = -1f;
                int o = offset + (fi * _outChannels);
                buffer[o] = l; buffer[o + 1] = r;
                for (int c = 2; c < _outChannels; c++) buffer[o + c] = 0;
            }
            lock (_clockLock)
            {
                _rendered += frames;
                if (songRunning) _songPos += frames;
            }
            return frames * _outChannels;
        }
    }
}
