using System;
using System.IO;
using System.Linq;
using BeatArenhax.Audio;
using NAudio.Wave;

internal static class AudioDecoderTests
{
    public static int VerifyFolder(string folder)
    {
        if (File.Exists(folder))
        {
            using WaveFileReader reader = new(folder);
            int sampleRate = reader.WaveFormat.SampleRate;
            AudioBuffer native = AudioDecoder.Decode(folder, sampleRate);
            AudioBuffer mixed = AudioDecoder.Decode(folder, 48000);
            if (native.Frames == 0 || mixed.Frames == 0 || !mixed.Data.All(float.IsFinite)
                || !mixed.Data.Any(value => value != 0)
                || Math.Abs(native.Frames * 1000.0 / sampleRate - AudioDecoder.GetDurationMs(folder)) > 0.001)
                throw new InvalidOperationException("WAV sample verification failed: " + folder);
            Console.WriteLine($"{folder}: {native.Frames} frames at {sampleRate} Hz, "
                + $"{mixed.Frames} mixer frames, peak {mixed.Data.Max(value => Math.Abs(value)):0.0000}");
            return 0;
        }
        string[] paths = Directory.GetFiles(folder, "*.xa");
        if (paths.Length == 0) throw new InvalidOperationException("No XA samples found in " + folder);
        foreach (string path in paths)
        {
            using XaSampleProvider provider = new(path);
            int rate = provider.WaveFormat.SampleRate;
            AudioBuffer native = AudioDecoder.Decode(path, rate);
            AudioBuffer mixed = AudioDecoder.Decode(path, 48000);
            if (native.Frames == 0 || mixed.Frames == 0 || !mixed.Data.All(float.IsFinite)
                || !mixed.Data.Any(value => value != 0)
                || Math.Abs(native.Frames * 1000.0 / rate - AudioDecoder.GetDurationMs(path)) > 0.001)
                throw new InvalidOperationException("XA sample verification failed: " + path);
            Console.WriteLine($"{Path.GetFileName(path)}: {native.Frames} frames at {rate} Hz, "
                + $"{mixed.Frames} mixer frames, peak {mixed.Data.Max(value => Math.Abs(value)):0.0000}");
        }
        Console.WriteLine($"Verified {paths.Length} XA samples");
        return 0;
    }

    public static void Run()
    {
        AdpcmDecoderTests.Run();
        string path = Path.Combine(Path.GetTempPath(), "beatarenhax-xa-" + Guid.NewGuid().ToString("N") + ".xa");
        try
        {
            foreach (int bits in new[] { 4, 6, 8 })
            foreach (int channels in new[] { 1, 2 })
            {
                byte[] fixture = Fixture(bits, channels);
                File.WriteAllBytes(path, fixture);
                using XaSampleProvider provider = new(path);
                float[] data = new float[61 * channels];
                int filled = 0;
                while (filled < data.Length)
                {
                    int read = provider.Read(data, filled, Math.Min(7 * channels, data.Length - filled));
                    if (read == 0) throw new InvalidOperationException("XA ended early");
                    filled += read;
                }
                if (provider.Read(new float[channels], 0, channels) != 0)
                    throw new InvalidOperationException("XA padding was exposed");
                for (int frame = 0; frame < 61; frame++)
                for (int channel = 0; channel < channels; channel++)
                {
                    float expected = (frame % 2 == 0 ? 1 : -1) * (channel == 0 ? 1 : 2) / (float)(1 << (bits - 1));
                    if (data[frame * channels + channel] != expected)
                        throw new InvalidOperationException("XA residual/channel decoding failed");
                }
                provider.Seek(TimeSpan.FromSeconds(35.0 / 32000));
                float[] tail = new float[26 * channels];
                if (provider.Read(tail, 0, tail.Length) != tail.Length || !tail.SequenceEqual(data.Skip(35 * channels)))
                    throw new InvalidOperationException("XA seek failed");
                if (Math.Abs(AudioDecoder.GetDurationMs(path) - 61.0 / 32) > 0.0001)
                    throw new InvalidOperationException("XA duration failed");
                AudioBuffer stereo = AudioDecoder.Decode(path, 32000);
                if (stereo.Frames != 61 || stereo.Data[0] != data[0]
                    || stereo.Data[1] != data[channels == 1 ? 0 : 1])
                    throw new InvalidOperationException("XA stereo conversion failed");
                AudioBuffer slice = AudioDecoder.Decode(path, 32000, 35.0 / 32000, 10.0 / 32000);
                if (slice.Frames != 10 || slice.Data[0] != data[35 * channels])
                    throw new InvalidOperationException("XA partial decode failed");
                AudioBuffer resampled = AudioDecoder.Decode(path, 48000);
                if (resampled.Frames == 0 || !resampled.Data.All(float.IsFinite)
                    || !resampled.Data.Any(value => value != 0))
                    throw new InvalidOperationException("XA resampling failed");
            }

            byte[] predicted = Fixture(8, 2);
            predicted[20] = 1;
            predicted[24] = 255;
            predicted[25] = 255;
            Array.Clear(predicted, 32, predicted.Length - 32);
            foreach (int start in new[] { 32, 65, 98, 131 }) predicted[start] = 0x40;
            File.WriteAllBytes(path, predicted);
            AudioBuffer prediction = AudioDecoder.Decode(path, 32000);
            int previous = 1, older = 0;
            for (int frame = 0; frame < 61; frame++)
            {
                int sample = Math.Clamp((previous * 488 - older * 240) / 256, short.MinValue, short.MaxValue);
                if (prediction.Data[frame * 2] != sample / 32768f
                    || prediction.Data[frame * 2 + 1] != -sample / 32768f)
                    throw new InvalidOperationException("XA history/prediction rounding failed");
                older = previous;
                previous = sample;
            }
            byte[] valid = Fixture(8, 1);
            Reject(path, valid[..20]);
            Reject(path, valid[..^1]);
            foreach (int index in new[] { 0, 4, 8, 12, 14, 15, 32 })
            {
                byte[] invalid = (byte[])valid.Clone();
                if (index == 32) invalid[index] = 0x50;
                else if (index == 8) invalid[index] = 65;
                else invalid[index] = 0;
                if (index == 12) invalid[13] = 0;
                Reject(path, invalid);
            }
            using (WaveFileWriter wave = new(path, new WaveFormat(32000, 16, 1)))
                wave.Write([0, 32], 0, 2);
            if (AudioDecoder.Decode(path, 32000).Data[0] != 0.25f)
                throw new InvalidOperationException("RIFF audio with XA extension regressed");
            Console.WriteLine("XA 4/6/8-bit mono/stereo, prediction, seeking, resampling and invalid-data self-tests passed");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static byte[] Fixture(int bits, int channels)
    {
        int blockBytes = bits * 4 + 1;
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write(0x3144574B);
        writer.Write(blockBytes * channels * 2);
        writer.Write(61);
        writer.Write((ushort)32000);
        writer.Write((byte)bits);
        writer.Write((byte)channels);
        writer.Write(new byte[16]);
        for (int block = 0; block < 2; block++)
        for (int channel = 0; channel < channels; channel++)
        {
            byte[] encoded = new byte[blockBytes];
            for (int frame = 0; frame < 32; frame++)
            {
                int value = (frame % 2 == 0 ? 1 : -1) * (channel + 1) & ((1 << bits) - 1);
                for (int bit = 0; bit < bits; bit++)
                {
                    int index = frame * bits + bit;
                    encoded[1 + index / 8] |= (byte)(((value >> (bits - 1 - bit)) & 1) << (7 - index % 8));
                }
            }
            writer.Write(encoded);
        }
        return stream.ToArray();
    }

    private static void Reject(string path, byte[] data)
    {
        File.WriteAllBytes(path, data);
        try { _ = AudioDecoder.Decode(path, 32000); }
        catch (InvalidDataException) { return; }
        catch (EndOfStreamException) { return; }
        throw new InvalidOperationException("Invalid XA data was accepted");
    }
}
