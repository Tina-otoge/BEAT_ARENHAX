using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using BeatArenhax.Audio;
using NAudio.Wave;

internal static class AdpcmDecoderTests
{
    public static void Run()
    {
        string path = Path.Combine(Path.GetTempPath(), "beatarenhax-adpcm-" + Guid.NewGuid().ToString("N") + ".wav");
        try
        {
            foreach (int channels in new[] { 1, 2 })
            foreach (bool fact in new[] { false, true })
            {
                byte[] fixture = Fixture(channels, fact);
                File.WriteAllBytes(path, fixture);
                short[] first = channels == 1
                    ? [1000, 2000, 2016, 2000, 2112, 1808]
                    : [1000, -1000, 2000, -2000, 2016, -2016, 2128, -2144, 2128, -2144, 2128, -2144];
                short[] last = channels == 1 ? [1000, 2000, 2016, 2000] : [1000, -1000, 2000, -2000, 2016, -2016];
                float[] expected = first.Concat(last.Take(last.Length - (fact ? channels : 0)))
                    .Select(value => value / 32768f).ToArray();
                using AdpcmSampleProvider provider = new(new WaveFileReader(path));
                float[] actual = new float[expected.Length];
                int filled = 0;
                while (filled < actual.Length)
                {
                    int read = provider.Read(actual, filled, Math.Min(channels * 3, actual.Length - filled));
                    if (read == 0) throw new InvalidOperationException("ADPCM ended early");
                    filled += read;
                }
                if (!actual.SequenceEqual(expected) || provider.Read(new float[channels], 0, channels) != 0)
                    throw new InvalidOperationException("ADPCM sample values, nibble order or partial block failed");
                int frames = expected.Length / channels;
                if (Math.Abs(AudioDecoder.GetDurationMs(path) - frames * 1000.0 / 32000) > 0.0001)
                    throw new InvalidOperationException("ADPCM duration/fact count failed");
                for (int frame = 0; frame <= frames; frame++)
                {
                    provider.Seek(TimeSpan.FromSeconds(frame / 32000.0));
                    float[] tail = new float[expected.Length - frame * channels];
                    if (provider.Read(tail, 0, tail.Length) != tail.Length
                        || !tail.SequenceEqual(expected.Skip(frame * channels)))
                        throw new InvalidOperationException("ADPCM seek failed at " + frame);
                }
                AudioBuffer native = AudioDecoder.Decode(path, 32000);
                if (native.Frames != frames || native.Data[0] != expected[0]
                    || native.Data[1] != expected[channels == 1 ? 0 : 1])
                    throw new InvalidOperationException("ADPCM stereo conversion failed");
                AudioBuffer slice = AudioDecoder.Decode(path, 32000, 7.0 / 32000, 1.0 / 32000);
                if (slice.Frames != 1 || slice.Data[0] != expected[7 * channels])
                    throw new InvalidOperationException("ADPCM partial decoding failed");
                AudioBuffer resampled = AudioDecoder.Decode(path, 48000);
                if (resampled.Frames == 0 || !resampled.Data.All(float.IsFinite)
                    || !resampled.Data.Any(value => value != 0))
                    throw new InvalidOperationException("ADPCM resampling failed");
            }
            byte[] valid = Fixture(1, false);
            int dataStart = 54;
            foreach (int offset in new[] { 22, 32, 34, 38, 40, dataStart, dataStart + 1 })
            {
                byte[] invalid = (byte[])valid.Clone();
                invalid[offset] = offset == dataStart ? (byte)1 : (byte)0;
                if (offset == 32) invalid[offset] = 8;
                Reject(path, invalid);
            }
            Reject(path, valid[..^2]);
            byte[] invalidFact = Fixture(2, true);
            BinaryPrimitives.WriteUInt32LittleEndian(invalidFact.AsSpan(54), 1000);
            Reject(path, invalidFact);

            byte[] rounding = Fixture(1, false);
            BinaryPrimitives.WriteInt16LittleEndian(rounding.AsSpan(42), 255);
            BinaryPrimitives.WriteInt16LittleEndian(rounding.AsSpan(dataStart + 3), -2000);
            rounding[dataStart + 7] = 0;
            File.WriteAllBytes(path, rounding);
            if (AudioDecoder.Decode(path, 32000).Data[4] != -1992 / 32768f)
                throw new InvalidOperationException("ADPCM signed prediction rounding failed");
            byte[] clipped = Fixture(1, false);
            BinaryPrimitives.WriteInt16LittleEndian(clipped.AsSpan(dataStart + 3), 32760);
            clipped[dataStart + 7] = 0x70;
            File.WriteAllBytes(path, clipped);
            if (AudioDecoder.Decode(path, 32000).Data[4] != 32767 / 32768f)
                throw new InvalidOperationException("ADPCM clipping failed");
            Console.WriteLine("Microsoft ADPCM mono/stereo, custom coefficients, partial blocks, fact counts, seeking and invalid-data self-tests passed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Fixture(int channels, bool fact)
    {
        int blockBytes = channels == 1 ? 9 : 18;
        int partialBytes = channels * 7 + 1;
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write("RIFF"u8);
        writer.Write(0);
        writer.Write("WAVEfmt "u8);
        writer.Write(26);
        writer.Write((ushort)2);
        writer.Write((ushort)channels);
        writer.Write(32000);
        writer.Write(32000 * blockBytes / 6);
        writer.Write((ushort)blockBytes);
        writer.Write((ushort)4);
        writer.Write((ushort)8);
        writer.Write((ushort)6);
        writer.Write((ushort)1);
        writer.Write((short)256);
        writer.Write((short)0);
        if (fact)
        {
            writer.Write("fact"u8);
            writer.Write(4);
            writer.Write(channels == 1 ? 9 : 8);
        }
        writer.Write("data"u8);
        writer.Write(blockBytes + partialBytes);
        foreach (int length in new[] { blockBytes, partialBytes })
        {
            writer.Write(new byte[channels]);
            for (int channel = 0; channel < channels; channel++) writer.Write((ushort)16);
            for (int channel = 0; channel < channels; channel++) writer.Write((short)(channel == 0 ? 2000 : -2000));
            for (int channel = 0; channel < channels; channel++) writer.Write((short)(channel == 0 ? 1000 : -1000));
            writer.Write((byte)0x1F);
            if (length == blockBytes)
            {
                writer.Write((byte)0x78);
                if (channels == 2) writer.Write((ushort)0);
            }
        }
        if (stream.Length % 2 != 0) writer.Write((byte)0);
        stream.Position = 4;
        writer.Write((int)stream.Length - 8);
        return stream.ToArray();
    }

    private static void Reject(string path, byte[] data)
    {
        File.WriteAllBytes(path, data);
        try { _ = AudioDecoder.Decode(path, 32000); }
        catch (InvalidDataException) { return; }
        catch (EndOfStreamException) { return; }
        throw new InvalidOperationException("Invalid ADPCM data was accepted");
    }
}
