using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace BeatArenhax.Images
{
    // Minimal PNG decoder to RGBA32 (top row first), so jackets can be converted off the main thread.
    // Returns null for anything unusual (interlaced, corrupt); callers fall back to Unity's decoder.
    internal static class PngDecoder
    {
        private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        internal static byte[] Decode(byte[] file, out int width, out int height)
        {
            width = height = 0;
            try { return DecodeCore(file, out width, out height); }
            catch { return null; }
        }

        private static byte[] DecodeCore(byte[] file, out int width, out int height)
        {
            width = height = 0;
            if (file.Length < 33 || !file.AsSpan(0, 8).SequenceEqual(Signature)) return null;
            int bitDepth = 0, colorType = 0;
            byte[] palette = null, paletteAlpha = null;
            using MemoryStream idat = new();
            int pos = 8;
            while (pos + 8 <= file.Length)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(pos));
                string type = System.Text.Encoding.ASCII.GetString(file, pos + 4, 4);
                int data = pos + 8;
                if (length < 0 || data + length > file.Length) return null;
                switch (type)
                {
                    case "IHDR":
                        width = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(data));
                        height = BinaryPrimitives.ReadInt32BigEndian(file.AsSpan(data + 4));
                        bitDepth = file[data + 8];
                        colorType = file[data + 9];
                        if (file[data + 12] != 0) return null; // interlaced
                        break;
                    case "PLTE": palette = file.AsSpan(data, length).ToArray(); break;
                    case "tRNS": paletteAlpha = file.AsSpan(data, length).ToArray(); break;
                    case "IDAT": idat.Write(file, data, length); break;
                    case "IEND": pos = file.Length; continue;
                    default: break;
                }
                pos = data + length + 4;
            }
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192) return null;
            int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
            if (channels == 0 || bitDepth is not (1 or 2 or 4 or 8 or 16)) return null;
            if (colorType == 3 && palette == null) return null;

            int bitsPerPixel = channels * bitDepth;
            int stride = ((width * bitsPerPixel) + 7) / 8;
            int bpp = Math.Max(1, bitsPerPixel / 8);
            byte[] raw = new byte[(stride + 1) * height];
            idat.Position = 0;
            using (ZLibStream z = new(idat, CompressionMode.Decompress))
            {
                int read = 0;
                while (read < raw.Length)
                {
                    int n = z.Read(raw, read, raw.Length - read);
                    if (n <= 0) return null;
                    read += n;
                }
            }

            byte[] prev = new byte[stride];
            byte[] line = new byte[stride];
            byte[] rgba = new byte[width * height * 4];
            for (int y = 0; y < height; y++)
            {
                int src = y * (stride + 1);
                byte filter = raw[src];
                Buffer.BlockCopy(raw, src + 1, line, 0, stride);
                Unfilter(filter, line, prev, bpp);
                WriteRow(line, rgba, y * width * 4, width, colorType, bitDepth, palette, paletteAlpha);
                (prev, line) = (line, prev);
            }
            return rgba;
        }

        private static void Unfilter(byte filter, byte[] line, byte[] prev, int bpp)
        {
            int n = line.Length;
            switch (filter)
            {
                case 0: break;
                case 1: for (int i = bpp; i < n; i++) line[i] += line[i - bpp]; break;
                case 2: for (int i = 0; i < n; i++) line[i] += prev[i]; break;
                case 3:
                    for (int i = 0; i < n; i++) line[i] += (byte)(((i >= bpp ? line[i - bpp] : 0) + prev[i]) >> 1);
                    break;
                case 4:
                    for (int i = 0; i < n; i++)
                    {
                        int a = i >= bpp ? line[i - bpp] : 0, b = prev[i], c = i >= bpp ? prev[i - bpp] : 0;
                        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
                        line[i] += (byte)(pa <= pb && pa <= pc ? a : pb <= pc ? b : c);
                    }
                    break;
                default: throw new InvalidDataException("bad filter");
            }
        }

        private static void WriteRow(byte[] line, byte[] rgba, int dst, int width, int colorType, int bitDepth, byte[] palette, byte[] paletteAlpha)
        {
            for (int x = 0; x < width; x++, dst += 4)
            {
                byte r, g, b, a = 255;
                switch (colorType)
                {
                    case 0:
                        r = g = b = Sample(line, x, 0, 1, bitDepth);
                        break;
                    case 2:
                        r = Sample(line, x, 0, 3, bitDepth); g = Sample(line, x, 1, 3, bitDepth); b = Sample(line, x, 2, 3, bitDepth);
                        break;
                    case 3:
                        int index = PackedIndex(line, x, bitDepth);
                        if ((index * 3) + 2 >= palette.Length) { r = g = b = 0; break; }
                        r = palette[index * 3]; g = palette[(index * 3) + 1]; b = palette[(index * 3) + 2];
                        if (paletteAlpha != null && index < paletteAlpha.Length) a = paletteAlpha[index];
                        break;
                    case 4:
                        r = g = b = Sample(line, x, 0, 2, bitDepth); a = Sample(line, x, 1, 2, bitDepth);
                        break;
                    default:
                        r = Sample(line, x, 0, 4, bitDepth); g = Sample(line, x, 1, 4, bitDepth); b = Sample(line, x, 2, 4, bitDepth); a = Sample(line, x, 3, 4, bitDepth);
                        break;
                }
                rgba[dst] = r; rgba[dst + 1] = g; rgba[dst + 2] = b; rgba[dst + 3] = a;
            }
        }

        // 8/16-bit samples (16-bit keeps the high byte); 1/2/4-bit grayscale is scaled up to 0-255.
        private static byte Sample(byte[] line, int x, int channel, int channels, int bitDepth)
        {
            if (bitDepth == 8) return line[(x * channels) + channel];
            if (bitDepth == 16) return line[((x * channels) + channel) * 2];
            int max = (1 << bitDepth) - 1;
            return (byte)(PackedIndex(line, x, bitDepth) * 255 / max);
        }

        private static int PackedIndex(byte[] line, int x, int bitDepth)
        {
            if (bitDepth == 8) return line[x];
            int bit = x * bitDepth;
            return (line[bit >> 3] >> (8 - bitDepth - (bit & 7))) & ((1 << bitDepth) - 1);
        }
    }
}
