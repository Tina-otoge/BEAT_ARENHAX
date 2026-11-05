using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using BeatArenhax;
using BeatArenhax.Audio;
using BeatArenhax.CustomSongs;
using BeatArenhax.DTX;

namespace BeatArenhax
{
    internal static class BuildInfo
    {
        public const string Version = "test";
    }
}

internal class Program
{
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "selftest") return RunSelfTest();

        string root = args.Length > 0 ? args[0] : @"G:\Games\PC\DTXFiles\DTXFiles.APPROVEDDTX";
        string outDir = args.Length > 1 ? args[1] : Path.Combine(Path.GetTempPath(), "dtxtest");
        string mode = args.Length > 2 ? args[2] : "convert";
        _ = Directory.CreateDirectory(outDir);
        Settings settings = new();
        SongLibrary library = new();
        Stopwatch stopwatch = Stopwatch.StartNew();
        library.Scan(root, settings, Path.Combine(outDir, "ids.txt"));
        Console.WriteLine($"Scanned {library.Songs.Count} songs in {stopwatch.ElapsedMilliseconds} ms, {library.Warnings.Count} warnings");
        foreach (string warning in library.Warnings.Take(20)) Console.WriteLine("  WARN " + warning);

        if (mode == "play")
        {
            using CustomAudioEngine engine = new(settings, Console.WriteLine);
            CustomSong song = library.Songs.First(item => item.Folder.Contains(args.Length > 3 ? args[3] : "Ave Mujica"));
            engine.Prefetch(song);
            _ = engine.Arm(song, 1);
            _ = engine.StartSong(1f);
            Stopwatch start = Stopwatch.StartNew();
            double last = -1;
            for (int i = 0; i < 40; i++)
            {
                Thread.Sleep(50);
                double songTime = engine.Mixer.SongTimeMs;
                Console.WriteLine($"wall {start.ElapsedMilliseconds,6} song {songTime,8:0.0} delta {(last < 0 ? 0 : songTime - last),6:0.0}");
                last = songTime;
                if (i % 5 == 0) engine.PlayChip(song.GetConverted(settings, engine.DurationMs).Advanced.Notes.First(note => note.Playable).WavId, 1f);
            }
            engine.Mixer.PauseSong();
            Thread.Sleep(300);
            Console.WriteLine("paused at " + engine.Mixer.SongTimeMs);
            Thread.Sleep(300);
            Console.WriteLine("paused still " + engine.Mixer.SongTimeMs);
            engine.Mixer.ResumeSong();
            Thread.Sleep(300);
            Console.WriteLine("resumed " + engine.Mixer.SongTimeMs);
            engine.Mixer.SeekSong(10000);
            Thread.Sleep(200);
            Console.WriteLine("after seek(10000)+200ms " + engine.Mixer.SongTimeMs);
            engine.Mixer.StopSong();
            return 0;
        }

        int ok = 0, fail = 0, idx = 0;
        long totalPlayable = 0, totalAuto = 0;
        Dictionary<string, double> durationCache = [];
        double duration(string path)
        {
            lock (durationCache)
            {
                if (durationCache.TryGetValue(path, out double cachedDuration)) return cachedDuration;
            }
            double decodedDuration = AudioDecoder.GetDurationMs(path);
            lock (durationCache) durationCache[path] = decodedDuration;
            return decodedDuration;
        }
        foreach (CustomSong song in library.Songs)
        {
            try
            {
                ConvertedSong conv = song.GetConverted(settings, duration);
                ok++;
                foreach (string warning in conv.Casual.Warnings.Concat(conv.Advanced.Warnings).Distinct())
                    Console.WriteLine("WARN " + song.Folder + ": " + warning);
                totalPlayable += conv.Advanced.PlayableCount;
                totalAuto += conv.Advanced.AutoCount;
                if (idx < 3 || song.Folder.Contains("Ave Mujica - Ave Mujica"))
                {
                    string baseName = Path.Combine(outDir, "song" + idx);
                    File.WriteAllBytes(baseName + "_dr1.mid", conv.Advanced.Midi);
                    File.WriteAllBytes(baseName + "_dr0.mid", conv.Casual.Midi);
                    File.WriteAllText(baseName + "_param.json", conv.ParamJson);
                    Console.WriteLine($"[{song.Id}] {song.Title} / {song.Artist}  Advanced L{song.AdvancedLevel} ({song.AdvancedChart.DLevel}) {conv.Advanced.PlayableCount}+{conv.Advanced.AutoCount}auto, " +
                        $"Casual L{song.CasualLevel} dtx {conv.Casual.PlayableCount}+{conv.Casual.AutoCount}auto, len {conv.Advanced.LengthMs / 1000:0.0}s, sched {conv.Advanced.Schedule.Count} -> {baseName}");
                }
                idx++;
                song.ReleaseConverted();
            }
            catch (Exception e)
            {
                fail++;
                Console.WriteLine("FAIL " + song.Folder + ": " + e);
            }
        }
        Console.WriteLine($"Converted ok={ok} fail={fail} in {stopwatch.ElapsedMilliseconds} ms; avg playable {(ok > 0 ? totalPlayable / ok : 0)}, avg auto {(ok > 0 ? totalAuto / ok : 0)}");
        return fail == 0 ? 0 : 1;
    }

    private static int RunSelfTest()
    {
        foreach (GameLane lane in new[] { GameLane.None, GameLane.BD, GameLane.LP, (GameLane)(-1) })
        {
            if (ChartNoteSimplifier.Priority(new ChartNote { Lane = lane }) == 0) continue;
            throw new InvalidOperationException("Automatic-lane priority self-test failed: " + lane);
        }
        string path = Path.Combine(Path.GetTempPath(), "beatarenhax-dtx-selftest-" + Guid.NewGuid().ToString("N") + ".dtx");
        string settingsPath = Path.Combine(Path.GetTempPath(), "beatarenhax-settings-selftest-" + Guid.NewGuid().ToString("N") + ".ini");
        string text = string.Join("\n",
        [
            "#RANDOM 3",
            "#IF 1",
            "#TITLE First",
            "#00004:01",
            "#ELSEIF 2",
            "#IF 2",
            "#TITLE Second",
            "#00004:02",
            "#ELSE",
            "#TITLE Wrong nested branch",
            "#ENDIF",
            "#ELSE",
            "#TITLE Third",
            "#00004:03",
            "#ENDIF",
            "#ENDRANDOM",
            "#STOP01 192",
            "#00009:0001",
            "#WAV01 hit.wav",
            "#00112:01",
            "#BMP01 first.png",
            "#BMP02 second.png",
            "#BMP03 third.png",
        ]);
        try
        {
            File.WriteAllText(path, text);
            DtxChart chart = DtxChart.Load(path, random: new FixedRandom(2));
            DtxChart firstBranch = DtxChart.Load(path, random: new FixedRandom(1));
            DtxChart thirdBranch = DtxChart.Load(path, random: new FixedRandom(3));
            ChartTimeline timeline = ChartTimeline.Build(chart, 0, 1);
            int measureEndTick = timeline.MeasureTick(0, 1);
            double measureEndMs = timeline.TickToMs(measureEndTick);
            ChartConverter converter = new(new Settings(), file => 0);
            ConvertedChart converted = converter.Prepare(chart, false);
            Dictionary<int, int> noteMap = GameParamWriter.AssignNotes([converted.Notes]);
            ChartConverter.Finish(converted, noteMap);
            if (chart.Title != "Second" || firstBranch.Title != "First" || thirdBranch.Title != "Third"
                || chart.Chips.Count != 3 || chart.Chips[0].Value != 2
                || chart.VisualAssets.Count != 3 || Math.Abs(measureEndMs - 2500) > 1)
            {
                Console.Error.WriteLine($"DTX self-test failed: title='{chart.Title}', chips={chart.Chips.Count}, value={(chart.Chips.Count > 0 ? chart.Chips[0].Value : 0)}, visual assets={chart.VisualAssets.Count}, measure end={measureEndMs:0.0}ms");
                return 1;
            }
            if (converted.PlayableCount != 1 || converted.Midi == null || converted.Midi[0] != (byte)'M'
                || !converted.Warnings.Any(warning => warning.Contains("preserved but not rendered")))
            {
                Console.Error.WriteLine("DTX conversion self-test failed");
                return 1;
            }

            string defaultSettings = Settings.DefaultIni;
            if (!defaultSettings.Contains("ForceQuitOnSignal=1"))
            {
                Console.Error.WriteLine("ForceQuitOnSignal default self-test failed");
                return 1;
            }
            File.WriteAllText(settingsPath, defaultSettings.Replace("ForceQuitOnSignal=1", "ForceQuitOnSignal=0"));
            if (Settings.LoadOrCreate(settingsPath).ForceQuitOnSignal)
            {
                Console.Error.WriteLine("ForceQuitOnSignal opt-out self-test failed");
                return 1;
            }

            RunDifficultySelfTest(settingsPath);
            Console.WriteLine("DTX parser/conversion/settings self-test passed");
            return 0;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static void RunDifficultySelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings defaults = Settings.LoadOrCreate(settingsPath);
        if (defaults.DifficultiesCut != ChartDifficultyCut.Easiest
            || File.ReadAllText(settingsPath) != Settings.DefaultIni)
            throw new InvalidOperationException("Difficulty defaults/INI creation self-test failed");

        foreach (Settings settings in new[] { new Settings(), defaults, Settings.LoadOrCreate(settingsPath) })
        {
            if (settings.MinNoteGapMs != 0 || settings.CasualMinNoteGapMs != 0 || settings.CasualOnlyEighthNotes)
                throw new InvalidOperationException("Note filtering defaults self-test failed");
        }
        File.WriteAllText(settingsPath, "[Charts]\nMinNoteGapMs=99\n");
        Settings missingSettings = Settings.LoadOrCreate(settingsPath);
        if (missingSettings.MinNoteGapMs != 0 || missingSettings.CasualMinNoteGapMs != 0 || missingSettings.CasualOnlyEighthNotes)
            throw new InvalidOperationException("Settings in unrelated sections should be ignored");

        File.WriteAllText(settingsPath, "[SongSelect]\nSongWheelDistanceScale=1.75\n");
        if (Settings.LoadOrCreate(settingsPath).SongWheelDistanceScale != 1.75f
            || defaults.SongWheelDistanceScale != 1.5f)
            throw new InvalidOperationException("Song wheel distance scale parsing/default self-test failed");

        foreach ((int Value, int Expected) in new[] { (300, 300), (750, 750), (100, 300), (6000, 5000) })
        {
            File.WriteAllText(settingsPath, $"[SongSelect]\nSongWheelTouchSelectTimeMs={Value}\n");
            Settings loaded = Settings.LoadOrCreate(settingsPath);
            if (loaded.SongWheelTouchSelectTimeMs != Expected
                || defaults.SongWheelTouchSelectTimeMs != 600
                || Math.Abs((loaded.SongWheelTouchSelectTimeMs / 1000f) - (Expected / 1000.0)) > 0.000001)
                throw new InvalidOperationException("Song wheel touch time parsing/units self-test failed");
        }

        foreach (ChartDifficultyCut cut in Enum.GetValues<ChartDifficultyCut>())
        {
            File.WriteAllText(settingsPath, $"[CustomSongs]\nDifficultiesCut={cut.ToString().ToLowerInvariant()}\n"
                + "MinNoteGapMs=45\nCasualOnlyEighthNotes=true\nCasualMinNoteGapMs=234\n"
                + "[Charts]\nDifficultiesCut=invalid\nMinNoteGapMs=99\n"
                + "[Audio]\nMasterVolume=2\nMinNoteGapMs=88\n"
                + "[Game]\nPlayerName=SectionTest\n");
            Settings loaded = Settings.LoadOrCreate(settingsPath);
            if (loaded.DifficultiesCut != cut
                || loaded.MinNoteGapMs != 45 || loaded.CasualMinNoteGapMs != 234 || !loaded.CasualOnlyEighthNotes
                || loaded.MasterVolume != 2 || loaded.PlayerName != "SectionTest")
                throw new InvalidOperationException("Difficulty INI parsing self-test failed: " + cut);
        }
        foreach (string value in new[] { "invalid", "1", "BSC" })
        {
            File.WriteAllText(settingsPath, "[CustomSongs]\nDifficultiesCut=" + value + "\n");
            try
            {
                _ = Settings.LoadOrCreate(settingsPath);
                throw new InvalidOperationException("Invalid difficulties cut was accepted: " + value);
            }
            catch (FormatException) { }
        }

        string folder = Path.Combine(Path.GetTempPath(), "beatarenhax-difficulty-selftest-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(folder);
        try
        {
            for (int index = 1; index <= 4; index++)
                File.WriteAllText(Path.Combine(folder, $"chart{index}.dtx"),
                    $"#TITLE Difficulty test\n#DLEVEL {index}\n#BPM 120\n"
                    + "#WAV01 hit.wav\n#00012:01010000000000000000000000000000\n");

            (int[] Available, ChartDifficultyCut Cut, int ExpectedCasual, int ExpectedAdvanced, bool ReverseDLevels)[] cases =
            [
                (new[] { 1, 2, 3, 4 }, ChartDifficultyCut.Easiest, 1, 2, false),
                (new[] { 1, 2, 3, 4 }, ChartDifficultyCut.Hardest, 3, 4, false),
                (new[] { 1, 3, 4 }, ChartDifficultyCut.Easiest, 1, 3, false),
                (new[] { 1, 3, 4 }, ChartDifficultyCut.Hardest, 3, 4, false),
                (new[] { 1, 2 }, ChartDifficultyCut.Easiest, 1, 2, false),
                (new[] { 1, 2 }, ChartDifficultyCut.Hardest, 1, 2, false),
                (new[] { 3, 4 }, ChartDifficultyCut.Easiest, 3, 4, false),
                (new[] { 3, 4 }, ChartDifficultyCut.Hardest, 3, 4, false),
                (new[] { 1 }, ChartDifficultyCut.Easiest, 1, 1, false),
                (new[] { 1 }, ChartDifficultyCut.Hardest, 1, 1, false),
                (new[] { 1, 2, 3, 4 }, ChartDifficultyCut.Easiest, 4, 3, true),
                (new[] { 1, 2, 3, 4 }, ChartDifficultyCut.Hardest, 2, 1, true),
            ];
            foreach ((int[] Available, ChartDifficultyCut Cut, int ExpectedCasual, int ExpectedAdvanced, bool ReverseDLevels) in cases)
            {
                File.WriteAllText(Path.Combine(folder, "set.def"), "#TITLE Difficulty test\n"
                    + string.Join("\n", Available.AsEnumerable().Reverse().Select(index => $"#L{index}FILE: chart{index}.dtx")));
                if (ReverseDLevels)
                {
                    for (int index = 1; index <= 4; index++)
                        File.WriteAllText(Path.Combine(folder, $"chart{index}.dtx"),
                            $"#TITLE Difficulty test\n#DLEVEL {5 - index}\n#BPM 120\n"
                            + "#WAV01 hit.wav\n#00012:01010000000000000000000000000000\n");
                }
                Settings settings = new() { DifficultiesCut = Cut };
                SongLibrary library = new();
                library.Scan(folder, settings, Path.Combine(folder, "ids.txt"));
                if (library.Warnings.Count != 0 || library.Songs.Count != 1)
                    throw new InvalidOperationException("Difficulty fixture scan failed: " + string.Join("; ", library.Warnings));
                CustomSong song = library.Songs[0];
                if (song.CasualChart.SetIndex != ExpectedCasual || song.AdvancedChart.SetIndex != ExpectedAdvanced)
                    throw new InvalidOperationException($"Difficulty selection failed: available={string.Join(",", Available)}, "
                        + $"cut={Cut}; got {song.CasualChart.SetIndex}/{song.AdvancedChart.SetIndex} "
                        + $"with levels {string.Join(",", song.Charts.Select(chart => $"{chart.SetIndex}:{chart.DLevel}"))}");

                ConvertedSong converted = song.GetConverted(settings, file => 0);
                if (converted.Get(0) != converted.Casual || converted.Get(1) != converted.Advanced
                    || converted.Casual.Source.FilePath != song.CasualChart.Path
                    || converted.Advanced.Source.FilePath != song.AdvancedChart.Path
                    || converted.Advanced.PlayableCount != 2
                    || converted.Casual.PlayableCount != 2)
                    throw new InvalidOperationException($"Casual/Advanced conversion self-test failed: cut={Cut}, "
                        + $"charts={song.CasualChart.SetIndex}/{song.AdvancedChart.SetIndex}, "
                        + $"converted={converted.Casual.PlayableCount}/{converted.Advanced.PlayableCount}");
                song.ReleaseConverted();
                settings.MinNoteGapMs = 234;
                ConvertedSong filtered = song.GetConverted(settings, file => 0);
                if (filtered.Casual.PlayableCount != 1 || filtered.Advanced.PlayableCount != 1)
                    throw new InvalidOperationException("Shared note filtering self-test failed");
                song.ReleaseConverted();
                settings.MinNoteGapMs = 0;
                foreach ((bool Eighths, int Gap) in new[] { (true, 0), (false, 234), (true, 234) })
                {
                    settings.CasualOnlyEighthNotes = Eighths;
                    settings.CasualMinNoteGapMs = Gap;
                    ConvertedSong casualFiltered = song.GetConverted(settings, file => 0);
                    if (casualFiltered.Casual.PlayableCount != 1 || casualFiltered.Advanced.PlayableCount != 2
                        || casualFiltered.Casual.AutoCount != converted.Casual.AutoCount + 1
                        || !casualFiltered.Advanced.Midi.SequenceEqual(converted.Advanced.Midi)
                        || !casualFiltered.Advanced.Schedule.SequenceEqual(converted.Advanced.Schedule))
                        throw new InvalidOperationException($"Casual-only filtering self-test failed: cut={Cut}, eighths={Eighths}, gap={Gap}");
                    song.ReleaseConverted();
                }
                settings.CasualOnlyEighthNotes = false;
                settings.CasualMinNoteGapMs = 1;
                settings.MinNoteGapMs = 234;
                ConvertedSong combined = song.GetConverted(settings, file => 0);
                if (combined.Casual.PlayableCount != 1 || combined.Advanced.PlayableCount != 1)
                    throw new InvalidOperationException("Casual gap must not override a larger global gap");
                song.ReleaseConverted();
            }

            File.WriteAllText(Path.Combine(folder, "chart1.dtx"),
                "#TITLE Single chart test\n#DLEVEL 80\n#BPM 120\n"
                + "#WAV01 hit.wav\n#00012:01010000000000000000000000000000\n"
                + "#00011:01010000000000000000000000000000\n");
            File.WriteAllText(Path.Combine(folder, "set.def"), "#TITLE Single chart test\n#L1FILE: chart1.dtx\n");
            foreach (ChartDifficultyCut cut in Enum.GetValues<ChartDifficultyCut>())
            {
                File.WriteAllText(settingsPath, $"[CustomSongs]\nDifficultiesCut={cut.ToString().ToLowerInvariant()}\n");
                Settings settings = Settings.LoadOrCreate(settingsPath);
                SongLibrary library = new();
                library.Scan(folder, settings, Path.Combine(folder, "ids.txt"));
                CustomSong song = library.Songs.Single();
                ConvertedSong converted = song.GetConverted(settings, file => 0);
                if (song.CasualChart != song.AdvancedChart || song.CasualLevel != song.AdvancedLevel
                    || song.CasualLevel != ChartConverter.ToGameLevel(80)
                    || converted.Casual.PlayableCount != 4 || converted.Advanced.PlayableCount != 4
                    || !converted.Casual.Midi.SequenceEqual(converted.Advanced.Midi)
                    || !converted.Casual.Schedule.SequenceEqual(converted.Advanced.Schedule)
                    || converted.Casual.GameLevel != converted.Advanced.GameLevel)
                    throw new InvalidOperationException("Single-chart equivalence self-test failed: " + cut);
            }

            for (int index = 1; index <= 4; index++)
                File.WriteAllText(Path.Combine(folder, $"chart{index}.dtx"),
                    $"#TITLE Difficulty test\n#DLEVEL {index}\n#BPM 120\n"
                    + "#WAV01 hit.wav\n#00012:01010000000000000000000000000000\n");
            File.Delete(Path.Combine(folder, "set.def"));
            SongLibrary standalone = new();
            standalone.Scan(folder, defaults, Path.Combine(folder, "ids.txt"));
            if (standalone.Songs.Count != 1 || standalone.Songs[0].CasualChart.SetIndex != 1
                || standalone.Songs[0].AdvancedChart.SetIndex != 2)
                throw new InvalidOperationException("Standalone DTX difficulty self-test failed");
            Console.WriteLine($"Difficulty settings/selection/conversion self-test passed ({cases.Length} chart combinations)");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    private sealed class FixedRandom(int value) : Random
    {
        private readonly int _value = value;

        public override int Next(int minValue, int maxValue) => Math.Clamp(_value, minValue, maxValue - 1);
    }
}
