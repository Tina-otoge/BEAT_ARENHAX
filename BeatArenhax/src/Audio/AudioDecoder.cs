using System;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NVorbis;

namespace BeatArenhax.Audio
{
    /// <summary>Decoded audio: interleaved stereo float at the mixer sample rate.</summary>
    public sealed class AudioBuffer
    {
        public float[] Data;
        public int Frames;
        public static readonly AudioBuffer Empty = new() { Data = [], Frames = 0 };
    }

    public static class AudioDecoder
    {
        private sealed class VorbisSampleProvider : ISampleProvider, IDisposable
        {
            private readonly VorbisReader _r;
            public VorbisSampleProvider(string path)
            {
                _r = new VorbisReader(path);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_r.SampleRate, _r.Channels);
            }
            public WaveFormat WaveFormat { get; }
            public TimeSpan TotalTime => _r.TotalTime;
            public void Seek(TimeSpan t) => _r.TimePosition = t;

            public int Read(float[] buffer, int offset, int count)
            {
                count -= count % _r.Channels;
                return _r.ReadSamples(buffer, offset, count);
            }
            public void Dispose() => _r.Dispose();
        }

        private sealed class Mp3SampleProvider : ISampleProvider, IDisposable
        {
            private readonly NLayer.MpegFile _f;
            public Mp3SampleProvider(string path)
            {
                _f = new NLayer.MpegFile(path);
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(_f.SampleRate, _f.Channels);
            }
            public WaveFormat WaveFormat { get; }
            public TimeSpan TotalTime => _f.Duration;
            public void Seek(TimeSpan t) => _f.Time = t;

            public int Read(float[] buffer, int offset, int count)
            {
                count -= count % _f.Channels;
                return _f.ReadSamples(buffer, offset, count);
            }
            public void Dispose() => _f.Dispose();
        }

        private static (ISampleProvider provider, IDisposable owner, TimeSpan total) Open(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == ".ogg")
            {
                VorbisSampleProvider v = new(path);
                return (v, v, v.TotalTime);
            }
            if (ext == ".mp3")
            {
                Mp3SampleProvider m = new(path);
                return (m, m, m.TotalTime);
            }
            if (ext == ".xa")
            {
                using FileStream probe = File.OpenRead(path);
                using BinaryReader reader = new(probe);
                if (probe.Length < 4 || reader.ReadUInt32() != 0x46464952)
                {
                    XaSampleProvider xa = new(path);
                    return (xa, xa, xa.TotalTime);
                }
            }
            // .wav (and anything else that is a RIFF wave)
            WaveFileReader w = new(path);
            try
            {
                if (w.WaveFormat.Encoding == WaveFormatEncoding.Adpcm)
                {
                    AdpcmSampleProvider adpcm = new(w);
                    return (adpcm, adpcm, adpcm.TotalTime);
                }
                if (w.WaveFormat.Encoding is not WaveFormatEncoding.Pcm and not WaveFormatEncoding.IeeeFloat
                    and not WaveFormatEncoding.Extensible)
                    throw new NotSupportedException("Unsupported wave encoding " + w.WaveFormat.Encoding);
                return (w.ToSampleProvider(), w, w.TotalTime);
            }
            catch
            {
                w.Dispose();
                throw;
            }
        }

        public static double GetDurationMs(string path)
        {
            try
            {
                (ISampleProvider provider, IDisposable owner, TimeSpan total) = Open(path);
                owner.Dispose();
                return total.TotalMilliseconds;
            }
            catch
            {
                return 0;
            }
        }

        public static AudioBuffer Decode(string path, int targetRate, double startSeconds = 0, double maxSeconds = double.MaxValue)
        {
            (ISampleProvider source, IDisposable owner, TimeSpan total) = Open(path);
            try
            {
                if (startSeconds > 0)
                {
                    if (owner is VorbisSampleProvider v) v.Seek(TimeSpan.FromSeconds(startSeconds));
                    else if (owner is Mp3SampleProvider mp) mp.Seek(TimeSpan.FromSeconds(startSeconds));
                    else if (owner is XaSampleProvider xa) xa.Seek(TimeSpan.FromSeconds(startSeconds));
                    else if (owner is AdpcmSampleProvider adpcm) adpcm.Seek(TimeSpan.FromSeconds(startSeconds));
                    else if (owner is WaveStream ws) ws.CurrentTime = TimeSpan.FromSeconds(Math.Min(startSeconds, ws.TotalTime.TotalSeconds));
                }
                ISampleProvider p = source;
                if (p.WaveFormat.Channels == 1) p = new MonoToStereoSampleProvider(p);
                else if (p.WaveFormat.Channels > 2) p = new FirstTwoChannels(p);
                if (p.WaveFormat.SampleRate != targetRate) p = new WdlResamplingSampleProvider(p, targetRate);

                long maxFrames = maxSeconds >= double.MaxValue / 2 ? long.MaxValue : (long)(maxSeconds * targetRate);
                int estimate = (int)Math.Min(int.MaxValue / 4, Math.Max(4096, (total.TotalSeconds - startSeconds + 0.5) * targetRate * 2));
                if (maxFrames != long.MaxValue) estimate = (int)Math.Min(estimate, (maxFrames * 2) + 4096);
                float[] data = new float[estimate];
                int filled = 0;
                float[] tmp = new float[16384];
                while (true)
                {
                    int want = tmp.Length;
                    if (maxFrames != long.MaxValue) want = (int)Math.Min(want, (maxFrames * 2) - filled);
                    if (want <= 0) break;
                    int n = p.Read(tmp, 0, want);
                    if (n <= 0) break;
                    if (filled + n > data.Length) Array.Resize(ref data, Math.Max(data.Length * 2, filled + n));
                    Array.Copy(tmp, 0, data, filled, n);
                    filled += n;
                }
                filled -= filled % 2;
                if (filled != data.Length) Array.Resize(ref data, filled);
                return new AudioBuffer { Data = data, Frames = filled / 2 };
            }
            finally
            {
                owner.Dispose();
            }
        }

        private sealed class FirstTwoChannels(ISampleProvider src) : ISampleProvider
        {
            private readonly ISampleProvider _src = src;
            private float[] _buf = [];
            private readonly int _ch = src.WaveFormat.Channels;

            public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(src.WaveFormat.SampleRate, 2);
            public int Read(float[] buffer, int offset, int count)
            {
                int frames = count / 2;
                if (_buf.Length < frames * _ch) _buf = new float[frames * _ch];
                int n = _src.Read(_buf, 0, frames * _ch) / _ch;
                for (int i = 0; i < n; i++)
                {
                    buffer[offset + (i * 2)] = _buf[i * _ch];
                    buffer[offset + (i * 2) + 1] = _buf[(i * _ch) + 1];
                }
                return n * 2;
            }
        }
    }
}
