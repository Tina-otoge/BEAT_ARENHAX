using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BeatArenhax.CustomSongs;

namespace BeatArenhax.Images
{
    // Jackets decoded at full resolution on worker threads after the song scan, so the main thread only has to
    // upload pixels instead of decoding a thousand PNGs while the song select scene loads. Nothing is written
    // to disk. Images the managed decoder can't read (JPEG...) are left to Unity's decoder.
    internal static class JacketPreloader
    {
        internal sealed class Pixels
        {
            internal int Width, Height;
            internal byte[] Rgba; // bottom row first, as Texture2D.LoadRawTextureData expects
        }

        private static readonly ConcurrentDictionary<int, Pixels> Ready = new();
        private static readonly ConcurrentDictionary<int, byte> Unsupported = new();
        // Songs the main thread already asked for; the workers must not decode or keep them afterwards.
        private static readonly ConcurrentDictionary<int, byte> Taken = new();

        internal static void Start(IReadOnlyCollection<CustomSong> songs) =>
            _ = Task.Run(() =>
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    int decoded = 0;
                    _ = Parallel.ForEach(songs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, song =>
                    {
                        if (song.PreImagePath == null || Taken.ContainsKey(song.Id) || Ready.ContainsKey(song.Id) || Unsupported.ContainsKey(song.Id)) return;
                        Pixels pixels = Decode(song);
                        if (pixels == null) { Unsupported[song.Id] = 0; return; }
                        if (!Ready.TryAdd(song.Id, pixels)) return;
                        // The main thread may have needed it in the meantime; it then decoded it itself.
                        if (Taken.ContainsKey(song.Id)) { _ = Ready.TryRemove(song.Id, out _); return; }
                        _ = Interlocked.Increment(ref decoded);
                    });
                    Plugin.L.LogInfo($"Jackets: {decoded} decoded in the background, {Unsupported.Count} left for Unity ({sw.ElapsedMilliseconds} ms)");
                }
                catch (Exception e) { Plugin.L.LogWarning("Jacket preloading failed: " + e.Message); }
            });
        // Decoded pixels (removed from the preloader), or null when Unity's decoder has to be used.
        internal static Pixels Take(CustomSong song)
        {
            Taken[song.Id] = 0;
            if (Ready.TryRemove(song.Id, out Pixels pixels)) return pixels;
            if (Unsupported.ContainsKey(song.Id)) return null;
            pixels = Decode(song);
            if (pixels == null) Unsupported[song.Id] = 0;
            return pixels;
        }

        private static Pixels Decode(CustomSong song)
        {
            byte[] file;
            try { file = File.ReadAllBytes(song.PreImagePath); }
            catch { return null; }
            return DecodePixels(file);
        }

        internal static Pixels DecodePixels(byte[] file)
        {
            byte[] rgba = PngDecoder.Decode(file, out int width, out int height);
            if (rgba == null) return null;
            int stride = width * 4;
            byte[] flipped = new byte[rgba.Length];
            for (int y = 0; y < height; y++) Buffer.BlockCopy(rgba, y * stride, flipped, (height - 1 - y) * stride, stride);
            return new Pixels { Width = width, Height = height, Rgba = flipped };
        }
    }
}
