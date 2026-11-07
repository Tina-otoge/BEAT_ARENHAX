using System;
using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace BeatArenhax.Audio
{
    internal sealed class XaSampleProvider : ISampleProvider, IDisposable
    {
        private const int HeaderSize = 32;
        private const int BlockFrames = 32;
        private static readonly int[] PredictorFirst = [0, 240, 460, 392, 488];
        private static readonly int[] PredictorSecond = [0, 0, -208, -220, -240];
        private readonly FileStream _stream;
        private readonly int _bits;
        private readonly int _frames;
        private readonly int[] _initialHistory;
        private readonly int[] _history;
        private readonly byte[] _encoded;
        private readonly float[] _decoded;
        private int _blockOffset;
        private long _position;

        public WaveFormat WaveFormat { get; }
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)_frames / WaveFormat.SampleRate);

        public XaSampleProvider(string path)
        {
            _stream = File.OpenRead(path);
            try
            {
                using BinaryReader reader = new(_stream, System.Text.Encoding.ASCII, leaveOpen: true);
                byte[] header = reader.ReadBytes(HeaderSize);
                if (header.Length != HeaderSize) throw new InvalidDataException("Truncated XA header");
                if (BinaryPrimitives.ReadUInt32LittleEndian(header) != 0x3144574B)
                    throw new InvalidDataException("Unsupported XA header (expected KWD1)");
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
                uint frames = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
                int rate = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(12));
                _bits = header[14];
                int channels = header[15];
                if (_bits is not (4 or 6 or 8) || channels is not (1 or 2) || rate == 0)
                    throw new InvalidDataException("Invalid XA sample format");
                int blockSize = (1 + _bits * 4) * channels;
                long capacity = (long)length / blockSize * BlockFrames;
                if (length == 0 || length % blockSize != 0 || frames == 0
                    || frames > int.MaxValue / channels || frames > capacity || capacity - frames >= BlockFrames)
                    throw new InvalidDataException("Invalid XA block/sample count");
                if (_stream.Length < HeaderSize + (long)length)
                    throw new InvalidDataException("Truncated XA sample data");
                _frames = (int)frames;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
                _initialHistory = new int[channels * 2];
                for (int i = 0; i < _initialHistory.Length; i++)
                    _initialHistory[i] = BinaryPrimitives.ReadInt16LittleEndian(header.AsSpan(20 + i * 2));
                _history = (int[])_initialHistory.Clone();
                _encoded = new byte[blockSize];
                _decoded = new float[BlockFrames * channels];
                _blockOffset = _decoded.Length;
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        private void ReadBlock()
        {
            int read = 0;
            while (read < _encoded.Length)
            {
                int n = _stream.Read(_encoded, read, _encoded.Length - read);
                if (n == 0) throw new EndOfStreamException("Truncated XA block");
                read += n;
            }
            int channels = WaveFormat.Channels;
            int channelBytes = _encoded.Length / channels;
            for (int channel = 0; channel < channels; channel++)
            {
                int start = channel * channelBytes;
                int predictor = _encoded[start] >> 4;
                int shift = _encoded[start] & 15;
                if (predictor >= PredictorFirst.Length)
                    throw new InvalidDataException("Invalid XA predictor " + predictor);
                int previous = _history[channel * 2];
                int older = _history[channel * 2 + 1];
                // XA residuals are MSB-first; prediction divides toward zero, not an arithmetic shift.
                for (int frame = 0; frame < BlockFrames; frame++)
                {
                    int residual = 0;
                    for (int bit = 0; bit < _bits; bit++)
                    {
                        int index = frame * _bits + bit;
                        residual = (residual << 1) | ((_encoded[start + 1 + index / 8] >> (7 - index % 8)) & 1);
                    }
                    int signed = (short)(residual << (16 - _bits));
                    int prediction = (previous * PredictorFirst[predictor] + older * PredictorSecond[predictor]) / 256;
                    int sample = Math.Clamp((signed >> shift) + prediction, short.MinValue, short.MaxValue);
                    _decoded[frame * channels + channel] = sample / 32768f;
                    older = previous;
                    previous = sample;
                }
                _history[channel * 2] = previous;
                _history[channel * 2 + 1] = older;
            }
            _blockOffset = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            count -= count % WaveFormat.Channels;
            int remaining = (int)Math.Min(count, (long)_frames * WaveFormat.Channels - _position);
            int filled = 0;
            while (filled < remaining)
            {
                if (_blockOffset == _decoded.Length) ReadBlock();
                int n = Math.Min(remaining - filled, _decoded.Length - _blockOffset);
                Array.Copy(_decoded, _blockOffset, buffer, offset + filled, n);
                _blockOffset += n;
                _position += n;
                filled += n;
            }
            return filled;
        }

        public void Seek(TimeSpan time)
        {
            long target = (long)Math.Round(Math.Clamp(time.TotalSeconds * WaveFormat.SampleRate, 0, _frames)) * WaveFormat.Channels;
            _stream.Position = HeaderSize;
            Array.Copy(_initialHistory, _history, _history.Length);
            _position = 0;
            _blockOffset = _decoded.Length;
            float[] discard = new float[_decoded.Length];
            while (_position < target)
                _ = Read(discard, 0, (int)Math.Min(discard.Length, target - _position));
        }

        public void Dispose() => _stream.Dispose();
    }
}
