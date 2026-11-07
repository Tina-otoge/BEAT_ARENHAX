using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BeatArenhax.DTX
{
    public sealed class DtxChip
    {
        public int Measure;
        public double Frac;      // position inside the measure, 0..1
        public int Channel;      // hex channel number
        public int Value;        // base36 chip id (or BPM ref / raw value)
    }

    public sealed class DtxWav
    {
        public string File;
        public int Volume = 100; // 0..100
        public int Pan;          // -100..100
    }

    public sealed class DtxDirective
    {
        public string Key;
        public string Value;
    }

    /// <summary>Parsed DTX (DTXMania) chart for drums, audio and stage videos.</summary>
    public sealed class DtxChart
    {
        private static readonly HashSet<string> KnownDirectives = new(StringComparer.OrdinalIgnoreCase)
        {
            "ARTIST", "BASEBPM", "BANNER", "COMMENT", "DLEVEL", "DIFFICULTY", "GENRE", "LNTYPE", "LNOBJ",
            "MAKER", "PATH_WAV", "PLAYLEVEL", "PREIMAGE", "PREMOVIE", "PREVIEW", "STAGEFILE", "SUBARTIST",
            "SUBTITLE", "TITLE",
        };

        public string FilePath;
        public string Directory;
        public string Title = "";
        public string Artist = "";
        public string Preview;
        public string PreImage;
        public double DLevel;
        public double Bpm = 120;
        public double BaseBpm;
        public string PathWav = "";
        public readonly Dictionary<int, DtxWav> Wavs = [];
        public readonly Dictionary<int, string> Videos = [];
        public readonly Dictionary<int, double> BpmDefs = [];
        public readonly Dictionary<int, double> StopDefs = [];
        public readonly Dictionary<int, double> MeasureLength = [];
        public readonly Dictionary<string, string> VisualAssets = new(StringComparer.OrdinalIgnoreCase);
        public readonly List<DtxDirective> Directives = [];
        public readonly List<DtxChip> Chips = [];
        public readonly List<string> Warnings = [];
        public int MaxMeasure;

        static DtxChart()
        {
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
        }

        public static string ReadText(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            if (LooksLikeUtf8(bytes)) return Encoding.UTF8.GetString(bytes);
            try { return Encoding.GetEncoding(932).GetString(bytes); }
            catch { return Encoding.Default.GetString(bytes); }
        }

        private static bool LooksLikeUtf8(byte[] b)
        {
            bool anyMulti = false;
            for (int i = 0; i < b.Length; i++)
            {
                byte c = b[i];
                if (c < 0x80) continue;
                int n = c is >= 0xF0 and < 0xF8 ? 3 : c >= 0xE0 ? 2 : c is >= 0xC2 and < 0xE0 ? 1 : -1;
                if (n < 0 || i + n >= b.Length) return false;
                for (int k = 1; k <= n; k++) if ((b[i + k] & 0xC0) != 0x80) return false;
                i += n; anyMulti = true;
            }
            return anyMulti;
        }

        public static int Base36(string s, int pos)
        {
            int v = 0;
            for (int i = 0; i < 2; i++)
            {
                char c = char.ToUpperInvariant(s[pos + i]);
                int d = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : -1;
                if (d < 0) return -1;
                v = (v * 36) + d;
            }
            return v;
        }

        private static int Hex2(string s, int pos)
        {
            int v = 0;
            for (int i = 0; i < 2; i++)
            {
                char c = char.ToUpperInvariant(s[pos + i]);
                int d = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'F' ? c - 'A' + 10 : -1;
                if (d < 0) return -1;
                v = (v * 16) + d;
            }
            return v;
        }

        private static int ParseMeasure(string s)
        {
            // DTXMania allows the first digit to be base36 (A00 = 1000).
            char c = char.ToUpperInvariant(s[0]);
            int hi = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : -1;
            return hi < 0 || !char.IsDigit(s[1]) || !char.IsDigit(s[2]) ? -1 : (hi * 100) + ((s[1] - '0') * 10) + (s[2] - '0');
        }

        private static double ParseDouble(string s, double def)
        {
            s = s.Trim();
            int end = 0;
            while (end < s.Length && (char.IsDigit(s[end]) || s[end] == '.' || s[end] == '-' || s[end] == '+')) end++;
            return double.TryParse(s[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : def;
        }

        /// <summary>Parses only the header (fast) when headerOnly is true.</summary>
        public static DtxChart Load(string path, bool headerOnly = false, Random random = null)
        {
            DtxChart chart = new() { FilePath = path, Directory = Path.GetDirectoryName(path) };
            string text = ReadText(path);
            string[] lines = text.Split('\n');
            DtxConditionals conditionals = new();
            Random randomSource = random ?? (headerOnly ? new Random(1) : Random.Shared);
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim().TrimEnd('\r');
                if (line.Length < 2 || line[0] != '#') continue;

                int sep = -1;
                for (int i = 1; i < line.Length; i++)
                {
                    char c = line[i];
                    if (c is ':' or ' ' or '\t') { sep = i; break; }
                }
                string key = (sep < 0 ? line[1..] : line[1..sep]).ToUpperInvariant();
                string value = sep < 0 ? "" : line[(sep + 1)..].Trim().TrimStart(':').Trim();
                chart.Directives.Add(new DtxDirective { Key = key, Value = value });
                if (conditionals.Handle(key, value, randomSource, chart.Warnings)) continue;
                if (!conditionals.Active) continue;

                // Data line: mmmCC
                if (key.Length == 5 && ParseMeasure(key) >= 0 && Hex2(key, 3) >= 0)
                {
                    if (headerOnly) continue;
                    int measure = ParseMeasure(key);
                    int channel = Hex2(key, 3);
                    chart.ParseData(measure, channel, value);
                    continue;
                }

                switch (key)
                {
                    case "TITLE": chart.Title = value; continue;
                    case "ARTIST": chart.Artist = value; continue;
                    case "PREVIEW": chart.Preview = StripComment(value); continue;
                    case "PREIMAGE": chart.PreImage = StripComment(value); continue;
                    case "DLEVEL":
                    case "PLAYLEVEL":
                        chart.DLevel = ParseDouble(StripComment(value), 0); continue;
                    case "BPM": chart.Bpm = ParseDouble(StripComment(value), 120); continue;
                    case "BASEBPM": chart.BaseBpm = ParseDouble(StripComment(value), 0); continue;
                    case "PATH_WAV": chart.PathWav = StripComment(value); continue;
                    default:
                        break;
                }
                if (headerOnly) continue;

                if ((key.Length == 5 && key.StartsWith("AVI")) || (key.Length == 7 && key.StartsWith("VIDEO")))
                {
                    int id = Base36(key, key.Length - 2);
                    if (id > 0)
                    {
                        chart.Videos[id] = StripComment(value);
                        chart.VisualAssets[key] = StripComment(value);
                    }
                }
                else if (key.Length == 5 && (key.StartsWith("BMP") || key.StartsWith("BGA")))
                {
                    int id = Base36(key, 3);
                    if (id > 0) chart.VisualAssets[key] = StripComment(value);
                }
                else if (key.Length == 6 && key.StartsWith("TEXT"))
                {
                    int id = Base36(key, 4);
                    if (id > 0) chart.VisualAssets[key] = StripComment(value);
                }
                else if (key.Length == 5 && key.StartsWith("WAV"))
                {
                    int id = Base36(key, 3);
                    if (id > 0) chart.GetWav(id).File = StripComment(value);
                }
                else if (key.Length == 8 && (key.StartsWith("VOLUME") || key.StartsWith("WAVVOL")))
                {
                    int id = Base36(key, 6);
                    if (id > 0) chart.GetWav(id).Volume = (int)Math.Clamp(ParseDouble(StripComment(value), 100), 0, 100);
                }
                else if (key.Length == 8 && key.StartsWith("WAVPAN"))
                {
                    int id = Base36(key, 6);
                    if (id > 0) chart.GetWav(id).Pan = (int)Math.Clamp(ParseDouble(StripComment(value), 0), -100, 100);
                }
                else if (key.Length == 5 && key.StartsWith("PAN"))
                {
                    int id = Base36(key, 3);
                    if (id > 0) chart.GetWav(id).Pan = (int)Math.Clamp(ParseDouble(StripComment(value), 0), -100, 100);
                }
                else if (key.Length == 5 && key.StartsWith("BPM"))
                {
                    int id = Base36(key, 3);
                    if (id > 0) chart.BpmDefs[id] = ParseDouble(StripComment(value), 120);
                }
                else if (key.Length == 6 && key.StartsWith("STOP"))
                {
                    int id = Base36(key, 4);
                    if (id > 0) chart.StopDefs[id] = ParseDouble(StripComment(value), 0);
                }
                else if (!KnownDirectives.Contains(key))
                {
                    chart.Warnings.Add("Unsupported DTX directive #" + key + "; directive preserved but not interpreted");
                }
            }
            conditionals.Complete(chart.Warnings);
            // Old charts use a 1-10 scale.
            if (chart.DLevel is > 0 and <= 10)
                chart.DLevel *= 10;
            // Three-digit DTX levels use hundredths; keep the internal 0-100 scale.
            else if (chart.DLevel is >= 100 and < 1000)
                chart.DLevel /= 10;
            return chart;
        }

        private DtxWav GetWav(int id)
        {
            if (!Wavs.TryGetValue(id, out DtxWav w)) Wavs[id] = w = new DtxWav();
            return w;
        }

        private static string StripComment(string value)
        {
            int comment = value.IndexOf(';');
            return (comment >= 0 ? value[..comment] : value).Trim();
        }

        private void ParseData(int measure, int channel, string value)
        {
            if (measure > MaxMeasure) MaxMeasure = measure;
            if (channel == 0x02)
            {
                double len = ParseDouble(value, 1);
                if (len > 0) MeasureLength[measure] = len;
                return;
            }
            StringBuilder data = new(value.Length);
            foreach (char c in value)
            {
                if (c == ';') break;
                if (c == '_' || char.IsWhiteSpace(c)) continue;
                _ = data.Append(c);
            }
            string d = data.ToString();
            int count = d.Length / 2;
            if (count == 0) return;
            for (int i = 0; i < count; i++)
            {
                int v = channel == 0x03 ? Hex2(d, i * 2) : Base36(d, i * 2);
                if (v <= 0) continue;
                Chips.Add(new DtxChip { Measure = measure, Frac = (double)i / count, Channel = channel, Value = v });
            }
        }

        public string ResolveWavPath(int wavId) => !Wavs.TryGetValue(wavId, out DtxWav w) || string.IsNullOrWhiteSpace(w.File) ? null : ResolveFile(w.File, true);

        public string ResolveFile(string file, bool isWav)
        {
            if (string.IsNullOrWhiteSpace(file)) return null;
            file = file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            List<string> candidates = [];
            if (isWav && !string.IsNullOrEmpty(PathWav))
                candidates.Add(Path.Combine(Directory, PathWav, file));
            candidates.Add(Path.Combine(Directory, file));
            foreach (string candidate in candidates)
            {
                try
                {
                    if (File.Exists(candidate)) return candidate;
                    if (!isWav) continue;
                    // Very common in DTX packs: chart says .wav but file was converted to .ogg (or vice versa).
                    foreach (string extension in new[] { ".ogg", ".wav", ".mp3", ".xa" })
                    {
                        string alternative = Path.ChangeExtension(candidate, extension);
                        if (!File.Exists(alternative)) continue;
                        return alternative;
                    }
                }
                catch { }
            }
            return null;
        }
    }
}
