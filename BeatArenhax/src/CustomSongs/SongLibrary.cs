using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BeatArenhax.DTX;

namespace BeatArenhax.CustomSongs
{
    public sealed class CustomSongChart
    {
        public string Path;
        public string Label;
        public double DLevel;
        public int SetIndex;

        public string GetDecimalLevelText() => (DLevel / 10).ToString("0.00", CultureInfo.InvariantCulture);

        public string GetDifficultyTag(bool advanced)
        {
            foreach (string token in (Label ?? "").ToUpperInvariant().Split([' ', '_', '-', '.', '(', ')', '[', ']']))
            {
                string tag = token switch
                {
                    "BSC" or "BASIC" or "BEGINNER" => "BSC",
                    "ADV" or "ADVANCED" => "ADV",
                    "EXT" or "EXTREME" => "EXT",
                    "MAS" or "MASTER" => "MAS",
                    "INS" or "INSANE" => "INS",
                    _ => null,
                };
                if (tag != null) return tag;
            }
            return advanced ? "ADV" : "BSC";
        }
    }

    public sealed class ConvertedSong
    {
        public ConvertedChart Casual;
        public ConvertedChart Advanced;
        public string ParamJson;
        public Dictionary<int, int> WavToNote;

        public ConvertedChart Get(int difficulty) => difficulty == 0 ? Casual : Advanced;
    }

    public sealed class ConvertedCustomChart
    {
        public CustomSongChart Definition { get; init; }
        public ConvertedChart Chart { get; init; }
        public string ParamJson { get; init; }
        public Dictionary<int, int> WavToNote { get; init; }
    }

    public sealed class FolderInfo
    {
        public string Name;
        public string Directory;
        public string PreImagePath;
        public readonly List<int> SongIds = [];
    }

    public sealed class CustomSong
    {
        public int Id;
        public string Folder;
        public string RelativeFolder;
        public string DisplayFolderName;
        public string Title;
        public string Artist;
        public List<CustomSongChart> Charts = [];
        public CustomSongChart CasualChart;
        public CustomSongChart AdvancedChart;
        public int CasualLevel;
        public int AdvancedLevel;
        public string PreviewPath;
        public string PreImagePath;

        private readonly object _lock = new();
        private ConvertedSong _converted;
        private DtxChart _casualDtx, _advancedDtx;
        private readonly Dictionary<(int SetIndex, bool Casual), ConvertedCustomChart> _convertedCharts = [];

        private DtxChart LoadDtxNoLock(bool advanced) => advanced
            ? (_advancedDtx ??= DtxChart.Load(AdvancedChart.Path))
            : (_casualDtx ??= DtxChart.Load(CasualChart.Path));

        public ConvertedSong GetConverted(Settings settings, Func<string, double> durationMs)
        {
            lock (_lock)
            {
                if (_converted != null) return _converted;
                ChartConverter conv = new(settings, durationMs);
                DtxChart adv = LoadDtxNoLock(true);
                DtxChart casual = CasualChart.Path == AdvancedChart.Path ? adv : LoadDtxNoLock(false);
                ConvertedChart a = conv.Prepare(adv, false);
                ConvertedChart c = conv.Prepare(casual, true);
                Dictionary<int, int> map = GameParamWriter.AssignNotes([a.Notes, c.Notes]);
                ChartConverter.Finish(a, map);
                ChartConverter.Finish(c, map);
                _converted = new ConvertedSong
                {
                    Advanced = a,
                    Casual = c,
                    WavToNote = map,
                    ParamJson = GameParamWriter.BuildJson(map, a.Notes),
                };
                return _converted;
            }
        }

        public ConvertedCustomChart GetConvertedChart(Settings settings, Func<string, double> durationMs, int visibleIndex)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(durationMs);
            lock (_lock)
            {
                IReadOnlyList<CustomSongChart> visible = CustomChartSelection.Select(Charts, settings.DifficultiesCut);
                if (visibleIndex < 0 || visibleIndex >= visible.Count)
                    throw new ArgumentOutOfRangeException(nameof(visibleIndex), "No custom chart exists at this position.");
                CustomSongChart definition = visible[visibleIndex];
                bool casual = visibleIndex == 0;
                var key = (definition.SetIndex, casual);
                if (_convertedCharts.TryGetValue(key, out ConvertedCustomChart cached)) return cached;

                ChartConverter converter = new(settings, durationMs);
                ConvertedChart chart = converter.Prepare(DtxChart.Load(definition.Path), casual);
                Dictionary<int, int> map = GameParamWriter.AssignNotes([chart.Notes]);
                ChartConverter.Finish(chart, map);
                ConvertedCustomChart result = new()
                {
                    Definition = definition,
                    Chart = chart,
                    WavToNote = map,
                    ParamJson = GameParamWriter.BuildJson(map, chart.Notes),
                };
                _convertedCharts.Add(key, result);
                return result;
            }
        }

        public void ReleaseConverted()
        {
            lock (_lock) { _converted = null; _casualDtx = null; _advancedDtx = null; _convertedCharts.Clear(); }
        }
    }

    public sealed class SongLibrary
    {
        public const int MaxId = 7999;
        public readonly List<CustomSong> Songs = [];
        public readonly Dictionary<int, CustomSong> ById = [];
        public readonly List<FolderInfo> Folders = [];
        public readonly List<string> Warnings = [];
        private readonly Dictionary<string, FolderInfo> _folderNames = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FolderInfo> _foldersByName = new(StringComparer.OrdinalIgnoreCase);

        public static string ReadSetDefValue(string line, string key)
        {
            if (!line.StartsWith("#" + key, StringComparison.OrdinalIgnoreCase)) return null;
            string v = line[(key.Length + 1)..];
            return v.Length > 0 && v[0] != ' ' && v[0] != '\t' && v[0] != ':' ? null : v.TrimStart(':', ' ', '\t').Trim();
        }

        public void Scan(string root, Settings settings, string idFile)
        {
            Songs.Clear(); ById.Clear();
            _folderNames.Clear();
            Folders.Clear(); _foldersByName.Clear();
            if (!Directory.Exists(root))
            {
                Warnings.Add("Song folder does not exist: " + root);
                return;
            }

            Dictionary<string, int> ids = LoadIds(idFile);
            HashSet<int> usedIds = [.. ids.Values];
            bool idsChanged = false;

            SortedSet<string> dirs = new(StringComparer.OrdinalIgnoreCase);
            foreach (string file in SafeEnumerate(root, "*.dtx")) _ = dirs.Add(Path.GetDirectoryName(file));
            foreach (string file in SafeEnumerate(root, "set.def")) _ = dirs.Add(Path.GetDirectoryName(file));

            foreach (string dir in dirs)
            {
                try
                {
                    CustomSong song = LoadSongFolder(root, dir, settings);
                    if (song == null) continue;
                    string key = song.RelativeFolder.ToLowerInvariant();
                    if (!ids.TryGetValue(key, out int id))
                    {
                        id = settings.FirstSongId;
                        while (usedIds.Contains(id) && id <= MaxId) id++;
                        if (id > MaxId) { Warnings.Add("Too many custom songs, skipping " + dir); continue; }
                        ids[key] = id; _ = usedIds.Add(id); idsChanged = true;
                    }
                    song.Id = id;
                    Songs.Add(song);
                    ById[id] = song;
                    if (settings.DisplayFolderName || settings.DisplayFolderPicker)
                        AddToFolder(GetFolder(root, song.Folder), song);
                }
                catch (Exception e)
                {
                    Warnings.Add("Failed to read " + dir + ": " + e.Message);
                }
            }
            if (idsChanged) SaveIds(idFile, ids);
            Folders.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }

        private void AddToFolder(FolderInfo source, CustomSong song)
        {
            if (!_foldersByName.TryGetValue(source.Name, out FolderInfo folder))
            {
                folder = source;
                _foldersByName[folder.Name] = folder;
                Folders.Add(folder);
            }
            else if (folder.PreImagePath == null && source.PreImagePath != null)
                folder.PreImagePath = source.PreImagePath;
            folder.SongIds.Add(song.Id);
        }

        private static IEnumerable<string> SafeEnumerate(string root, string pattern)
        {
            Stack<string> stack = new();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string directory = stack.Pop();
                string[] files = [], subs = [];
                try { files = Directory.GetFiles(directory, pattern); } catch { }
                try { subs = Directory.GetDirectories(directory); } catch { }
                foreach (string file in files) yield return file;
                foreach (string subdirectory in subs) stack.Push(subdirectory);
            }
        }

        private CustomSong LoadSongFolder(string root, string dir, Settings settings)
        {
            CustomSong song = new() { Folder = dir, RelativeFolder = Path.GetRelativePath(root, dir) };
            string setDef = Directory.GetFiles(dir).FirstOrDefault(f => Path.GetFileName(f).Equals("set.def", StringComparison.OrdinalIgnoreCase));
            string setTitle = null;
            if (setDef != null)
            {
                Dictionary<int, string> labels = [];
                Dictionary<int, string> files = [];
                foreach (string raw in DtxChart.ReadText(setDef).Split('\n'))
                {
                    string line = raw.Trim();
                    string title = ReadSetDefValue(line, "TITLE");
                    if (title != null) { setTitle ??= title; continue; }
                    for (int i = 1; i <= 5; i++)
                    {
                        string chartFile = ReadSetDefValue(line, "L" + i + "FILE");
                        if (chartFile != null) files[i] = chartFile;
                        string label = ReadSetDefValue(line, "L" + i + "LABEL");
                        if (label != null) labels[i] = label;
                    }
                }
                foreach (KeyValuePair<int, string> entry in files)
                {
                    string chartPath = Path.Combine(dir, entry.Value.Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(chartPath)) continue;
                    string label = labels.TryGetValue(entry.Key, out string chartLabel) ? chartLabel : "L" + entry.Key;
                    song.Charts.Add(new CustomSongChart { Path = chartPath, SetIndex = entry.Key, Label = label });
                }
            }
            if (song.Charts.Count == 0)
            {
                int idx = 0;
                foreach (string chartPath in Directory.GetFiles(dir, "*.dtx").OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                    song.Charts.Add(new CustomSongChart { Path = chartPath, SetIndex = ++idx, Label = Path.GetFileNameWithoutExtension(chartPath) });
            }
            if (song.Charts.Count == 0) return null;

            DtxChart firstHeader = null;
            foreach (CustomSongChart chart in song.Charts)
            {
                DtxChart header = DtxChart.Load(chart.Path, headerOnly: true);
                chart.DLevel = header.DLevel;
                if (firstHeader == null || (string.IsNullOrEmpty(firstHeader.Title) && !string.IsNullOrEmpty(header.Title))) firstHeader = header;
                if (song.PreviewPath == null && !string.IsNullOrEmpty(header.Preview)) song.PreviewPath = header.ResolveFile(header.Preview, true);
                if (song.PreImagePath == null && !string.IsNullOrEmpty(header.PreImage)) song.PreImagePath = header.ResolveFile(header.PreImage, false);
                if (string.IsNullOrEmpty(song.Artist) && !string.IsNullOrEmpty(header.Artist)) song.Artist = header.Artist;
            }
            song.Charts = [.. song.Charts.OrderBy(c => c.DLevel).ThenBy(c => c.SetIndex)];
            song.Title = !string.IsNullOrWhiteSpace(setTitle) ? setTitle
                : !string.IsNullOrWhiteSpace(firstHeader?.Title) ? firstHeader.Title
                : Path.GetFileName(dir);
            song.Artist ??= "";
            if (settings.DisplayFolderName) song.DisplayFolderName = GetFolder(root, dir).Name;

            song.CasualChart = Pick(song.Charts, settings.DifficultiesCut, false);
            song.AdvancedChart = Pick(song.Charts, settings.DifficultiesCut, true);
            song.AdvancedLevel = ChartConverter.ToGameLevel(song.AdvancedChart.DLevel);
            song.CasualLevel = Math.Min(song.AdvancedLevel, ChartConverter.ToGameLevel(song.CasualChart.DLevel));
            return song;
        }

        private FolderInfo GetFolder(string root, string songFolder)
        {
            DirectoryInfo scanRoot = new(Path.GetFullPath(root));
            DirectoryInfo songDirectory = new(Path.GetFullPath(songFolder));
            DirectoryInfo parent = songDirectory.Parent;
            string cacheKey = songDirectory.FullName.Equals(scanRoot.FullName, StringComparison.OrdinalIgnoreCase)
                ? songDirectory.FullName : parent?.FullName ?? songDirectory.FullName;
            if (_folderNames.TryGetValue(cacheKey, out FolderInfo cached)) return cached;

            string name = null;
            string boxImage = null;
            DirectoryInfo namedGroup = null;
            DirectoryInfo group = songDirectory;
            if (songDirectory.FullName.Equals(scanRoot.FullName, StringComparison.OrdinalIgnoreCase))
                group = scanRoot;
            else
            {
                for (DirectoryInfo ancestor = parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    bool atRoot = ancestor.FullName.Equals(scanRoot.FullName, StringComparison.OrdinalIgnoreCase);
                    if (name == null)
                    {
                        try
                        {
                            string box = Directory.GetFiles(ancestor.FullName)
                                .FirstOrDefault(file => Path.GetFileName(file).Equals("box.def", StringComparison.OrdinalIgnoreCase));
                            if (box != null)
                            {
                                string[] lines = DtxChart.ReadText(box).Split('\n');
                                name = lines.Select(line => ReadSetDefValue(line.Trim(), "TITLE"))
                                    .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title));
                                if (name != null)
                                {
                                    namedGroup = ancestor;
                                    boxImage = lines.Select(line => ReadSetDefValue(line.Trim(), "PREIMAGE"))
                                        .FirstOrDefault(image => !string.IsNullOrWhiteSpace(image));
                                }
                                if (name == null) Warnings.Add("No non-empty #TITLE in " + box + "; using the folder name.");
                            }
                        }
                        catch (IOException e) { Warnings.Add("Reading folder metadata in " + ancestor.FullName + ": " + e.Message); }
                        catch (UnauthorizedAccessException e) { Warnings.Add("Reading folder metadata in " + ancestor.FullName + ": " + e.Message); }
                    }
                    if (atRoot) break;
                    group = ancestor;
                }
                if (scanRoot.Name.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase)) group = scanRoot;
            }
            name ??= group.Name.StartsWith("DTXFiles.", StringComparison.OrdinalIgnoreCase)
                ? group.Name["DTXFiles.".Length..] : group.Name;
            string directory = (namedGroup ?? group).FullName;
            FolderInfo result = new() { Name = name, Directory = directory };
            try
            {
                if (namedGroup == null)
                {
                    string box = Directory.GetFiles(directory)
                        .FirstOrDefault(file => Path.GetFileName(file).Equals("box.def", StringComparison.OrdinalIgnoreCase));
                    if (box != null) boxImage = DtxChart.ReadText(box).Split('\n')
                        .Select(line => ReadSetDefValue(line.Trim(), "PREIMAGE"))
                        .FirstOrDefault(image => !string.IsNullOrWhiteSpace(image));
                }
                string image = !string.IsNullOrWhiteSpace(boxImage)
                    ? Path.GetFullPath(Path.Combine(directory, boxImage.Trim('"').Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)))
                    : null;
                if (image != null && File.Exists(image)) result.PreImagePath = image;
                else
                {
                    if (image != null) Warnings.Add("Folder image does not exist: " + image + "; trying pre.png.");
                    result.PreImagePath = Directory.GetFiles(directory)
                        .FirstOrDefault(file => Path.GetFileName(file).Equals("pre.png", StringComparison.OrdinalIgnoreCase));
                }
            }
            catch (IOException e) { Warnings.Add("Reading folder image in " + directory + ": " + e.Message); }
            catch (UnauthorizedAccessException e) { Warnings.Add("Reading folder image in " + directory + ": " + e.Message); }
            _folderNames[cacheKey] = result;
            return result;
        }

        private static CustomSongChart Pick(List<CustomSongChart> charts, ChartDifficultyCut cut, bool advanced)
        {
            List<CustomSongChart> ordered = [.. charts.OrderBy(chart => chart.DLevel).ThenBy(chart => chart.SetIndex)];
            int chartIndex = cut == ChartDifficultyCut.Easiest
                ? (advanced ? Math.Min(1, ordered.Count - 1) : 0)
                : (advanced ? ordered.Count - 1 : Math.Max(0, ordered.Count - 2));
            return ordered[chartIndex];
        }

        private static Dictionary<string, int> LoadIds(string file)
        {
            Dictionary<string, int> d = [];
            if (!File.Exists(file)) return d;
            foreach (string line in File.ReadAllLines(file, Encoding.UTF8))
            {
                int tab = line.IndexOf('\t');
                if (tab <= 0) continue;
                if (!int.TryParse(line[..tab], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) continue;
                d[line[(tab + 1)..].Trim().ToLowerInvariant()] = id;
            }
            return d;
        }

        private static void SaveIds(string file, Dictionary<string, int> ids)
        {
            StringBuilder sb = new();
            _ = sb.AppendLine("; music id <TAB> song folder (relative to the song path). Keeps scores attached to the right song.");
            foreach (KeyValuePair<string, int> entry in ids.OrderBy(item => item.Value))
                _ = sb.Append(entry.Value.ToString(CultureInfo.InvariantCulture)).Append('\t').AppendLine(entry.Key);
            File.WriteAllText(file, sb.ToString(), new UTF8Encoding(false));
        }
    }
}
