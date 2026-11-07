using System;
using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace BeatArenhax.Audio
{
    internal sealed class AdpcmSampleProvider : ISampleProvider, IDisposable
    {
        private static readonly int[] Adaptation = [230, 230, 230, 230, 307, 409, 512, 614, 768, 614, 512, 409, 307, 230, 230, 230];
        private readonly WaveFileReader _reader;
        private readonly int _blockFrames;
        private readonly long _frames;
        private readonly short[] _coefficients;
        private readonly byte[] _encoded;
        private readonly float[] _decoded;
        private int _blockOffset;
        private int _blockSamples;
        private long _position;

        public WaveFormat WaveFormat { get; }
        public TimeSpan TotalTime => TimeSpan.FromSeconds((double)_frames / WaveFormat.SampleRate);

        public AdpcmSampleProvider(WaveFileReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            WaveFormat format = reader.WaveFormat;
            if (format.Encoding != WaveFormatEncoding.Adpcm || format.BitsPerSample != 4
                || format.Channels is not (1 or 2) || format.SampleRate <= 0
                || format.BlockAlign < 7 * format.Channels
                || format is not WaveFormatExtraData extra || extra.ExtraSize < 8)
                throw new InvalidDataException("Invalid Microsoft ADPCM format");
            _blockFrames = BinaryPrimitives.ReadUInt16LittleEndian(extra.ExtraData);
            int coefficientCount = BinaryPrimitives.ReadUInt16LittleEndian(extra.ExtraData.AsSpan(2));
            if (_blockFrames != 2 + (format.BlockAlign - 7 * format.Channels) * 2 / format.Channels
                || coefficientCount == 0 || 4 + coefficientCount * 4 > extra.ExtraSize)
                throw new InvalidDataException("Invalid Microsoft ADPCM block size or coefficient table");
            _coefficients = new short[coefficientCount * 2];
            for (int i = 0; i < _coefficients.Length; i++)
                _coefficients[i] = BinaryPrimitives.ReadInt16LittleEndian(extra.ExtraData.AsSpan(4 + i * 2));
            long partialBytes = reader.Length % format.BlockAlign;
            if (reader.Length == 0 || (partialBytes > 0 && partialBytes < 7 * format.Channels))
                throw new InvalidDataException("Truncated Microsoft ADPCM block header");
            long capacity = reader.Length / format.BlockAlign * _blockFrames;
            if (partialBytes > 0) capacity += 2 + (partialBytes - 7 * format.Channels) * 2 / format.Channels;
            _frames = capacity;
            foreach (RiffChunk chunk in reader.ExtraChunks)
            {
                if (chunk.IdentifierAsString != "fact") continue;
                if (chunk.Length < 4) throw new InvalidDataException("Truncated ADPCM fact chunk");
                uint frames = BinaryPrimitives.ReadUInt32LittleEndian(reader.GetChunkData(chunk));
                if (frames == 0 || frames > capacity || capacity - frames >= _blockFrames)
                    throw new InvalidDataException("Invalid ADPCM fact sample count");
                _frames = frames;
                break;
            }
            _reader = reader;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
            _encoded = new byte[format.BlockAlign];
            _decoded = new float[_blockFrames * format.Channels];
        }

        private void ReadBlock()
        {
            int expected = (int)Math.Min(_encoded.Length, _reader.Length - _reader.Position);
            int read = _reader.Read(_encoded, 0, _encoded.Length);
            if (read != expected || read < 7 * WaveFormat.Channels)
                throw new EndOfStreamException("Truncated Microsoft ADPCM block");
            int channels = WaveFormat.Channels;
            Span<int> predictors = stackalloc int[2];
            Span<int> deltas = stackalloc int[2];
            Span<int> previous = stackalloc int[2];
            Span<int> older = stackalloc int[2];
            for (int channel = 0; channel < channels; channel++)
            {
                predictors[channel] = _encoded[channel];
                if (predictors[channel] >= _coefficients.Length / 2)
                    throw new InvalidDataException("Invalid Microsoft ADPCM predictor");
                deltas[channel] = BinaryPrimitives.ReadUInt16LittleEndian(_encoded.AsSpan(channels + channel * 2));
                if (deltas[channel] < 16) throw new InvalidDataException("Invalid Microsoft ADPCM delta");
                previous[channel] = BinaryPrimitives.ReadInt16LittleEndian(_encoded.AsSpan(channels * 3 + channel * 2));
                older[channel] = BinaryPrimitives.ReadInt16LittleEndian(_encoded.AsSpan(channels * 5 + channel * 2));
                _decoded[channel] = older[channel] / 32768f;
                _decoded[channels + channel] = previous[channel] / 32768f;
            }
            int output = channels * 2;
            for (int index = channels * 7; index < read; index++)
            for (int half = 0; half < 2; half++)
            {
                int channel = channels == 1 ? 0 : half;
                int nibble = half == 0 ? _encoded[index] >> 4 : _encoded[index] & 15;
                int signed = nibble < 8 ? nibble : nibble - 16;
                int predictor = predictors[channel] * 2;
                long prediction = ((long)previous[channel] * _coefficients[predictor]
                    + (long)older[channel] * _coefficients[predictor + 1]) / 256;
                int sample = (int)Math.Clamp(prediction + (long)signed * deltas[channel], short.MinValue, short.MaxValue);
                _decoded[output++] = sample / 32768f;
                older[channel] = previous[channel];
                previous[channel] = sample;
                long delta = Math.Max(16, (long)Adaptation[nibble] * deltas[channel] / 256);
                if (delta > int.MaxValue) throw new InvalidDataException("Microsoft ADPCM delta overflow");
                deltas[channel] = (int)delta;
            }
            _blockSamples = output;
            _blockOffset = 0;
        }

        public int Read(float[] buffer, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            if (offset < 0 || count < 0 || offset > buffer.Length - count)
                throw new ArgumentOutOfRangeException(nameof(count));
            count -= count % WaveFormat.Channels;
            int remaining = (int)Math.Min(count, _frames * WaveFormat.Channels - _position);
            int filled = 0;
            while (filled < remaining)
            {
                if (_blockOffset == _blockSamples) ReadBlock();
                int n = Math.Min(remaining - filled, _blockSamples - _blockOffset);
                Array.Copy(_decoded, _blockOffset, buffer, offset + filled, n);
                _blockOffset += n;
                _position += n;
                filled += n;
            }
            return filled;
        }

        public void Seek(TimeSpan time)
        {
            long frame = (long)Math.Round(Math.Clamp(time.TotalSeconds * WaveFormat.SampleRate, 0, _frames));
            long block = frame / _blockFrames;
            _reader.Position = block * _encoded.Length;
            _position = block * _blockFrames * WaveFormat.Channels;
            _blockSamples = _blockOffset = 0;
            if (frame % _blockFrames == 0) return;
            ReadBlock();
            _blockOffset = (int)(frame % _blockFrames) * WaveFormat.Channels;
            _position = frame * WaveFormat.Channels;
        }

        public void Dispose() => _reader.Dispose();
    }
}
