using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        if (args.Length == 2 && args[0] == "audio-check") return AudioDecoderTests.VerifyFolder(args[1]);

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

        if (mode == "folders")
        {
            foreach (CustomSong song in library.Songs)
                Console.WriteLine($"{song.RelativeFolder}\t{song.DisplayFolderName}");
            return library.Warnings.Count == 0 ? 0 : 1;
        }

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

    private static void RunReplayLoopSelfTest()
    {
        int period = ReplayLoopTiming.Period(999);
        if (period != 1000 || ReplayLoopTiming.Period(0) != 0
            || ReplayLoopTiming.Period(int.MaxValue) != 0
            || ReplayLoopTiming.Wrap(-10, period) != -10
            || ReplayLoopTiming.Wrap(999, period) != 999
            || ReplayLoopTiming.Wrap(1000, period) != 0
            || ReplayLoopTiming.Wrap(2500, period) != 500
            || ReplayLoopTiming.Wrap(2500, 0) != 2500
            || ReplayLoopTiming.NeedsCursorReset(998, 999, period)
            || !ReplayLoopTiming.NeedsCursorReset(999, 1000, period)
            || ReplayLoopTiming.NeedsCursorReset(1000, 1001, period)
            || !ReplayLoopTiming.NeedsCursorReset(1001, 3500, period)
            || !ReplayLoopTiming.NeedsCursorReset(1900, 1500, period))
            throw new InvalidOperationException("Partner replay loop timing self-test failed");
    }

    private static int RunSelfTest()
    {
        RunReplayLoopSelfTest();
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
            RunModSettingsIniSaveSelfTest();

            RunDifficultySelfTest(settingsPath);
            RunCustomChartSelectionSelfTest();
            RunCustomScoreStoreSelfTest();
            RunDecimalNotationSelfTest(settingsPath, path);
            RunStoolSettingsSelfTest(settingsPath);
            RunGrabSettingsSelfTest(settingsPath);
            RunQuitButtonSettingsSelfTest(settingsPath);
            RunResultMenuSettingsSelfTest(settingsPath);
            RunResultDifficultySettingsSelfTest(settingsPath);
            RunFastSlowSettingsSelfTest(settingsPath);
            RunVirtualScreenSelfTest(settingsPath);
            RunPedalSelfTest(settingsPath);
            RunSpectatorSettingsSelfTest(settingsPath);
            RunSpectatorCameraControlSelfTest();
            RunCinematicCameraSelfTest();
            RunShowStatsSettingsSelfTest(settingsPath);
            RunFolderNameSelfTest(settingsPath);
            RunVideoSelfTest(settingsPath);
            RunTemplateSelfTest(settingsPath);
            AudioDecoderTests.Run();
            Console.WriteLine("DTX parser/conversion/settings self-test passed");
            return 0;
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(settingsPath)) File.Delete(settingsPath);
        }
    }

    private static void RunTemplateSelfTest(string settingsPath)
    {
        File.WriteAllText(settingsPath, "[CustomSongs]\nTemplateSong=\n");
        if (Settings.LoadOrCreate(settingsPath).TemplateSong != "")
            throw new InvalidOperationException("Empty template setting was not preserved.");
        File.WriteAllText(settingsPath, "[CustomSongs]\n");
        if (Settings.LoadOrCreate(settingsPath).TemplateSong != "")
            throw new InvalidOperationException("Missing template setting changed its default.");
        File.WriteAllText(settingsPath, "[CustomSongs]\nTemplateSong=custom\n");
        if (Settings.LoadOrCreate(settingsPath).TemplateSong != "custom")
            throw new InvalidOperationException("Fixed template setting was not preserved.");
        if (TemplateSelection.ChooseIndex(3, count => count - 1) != 2
            || !TemplateSelection.Compatible(47, "fuego", 1, 4, 1, 4)
            || TemplateSelection.Compatible(8000, "tutorial", 1, 4, 1, 4)
            || TemplateSelection.Compatible(47, "", 1, 4, 1, 4)
            || TemplateSelection.Compatible(47, "other", 1, 3, 1, 4)
            || TemplateSelection.Compatible(47, "other", 2, 4, 1, 4))
            throw new InvalidOperationException("Template eligibility self-test failed.");
        try { _ = TemplateSelection.ChooseIndex(0, _ => 0); throw new Exception("Empty template list accepted."); }
        catch (InvalidOperationException) { }
    }

    private static void RunPedalSelfTest(string settingsPath)
    {
        File.WriteAllText(settingsPath, Settings.DefaultIni);
        Settings defaults = Settings.LoadOrCreate(settingsPath);
        if (defaults.LeftPedal.Enabled || defaults.RightPedal.Enabled)
            throw new Exception("Pedals must default to automatic");
        foreach (string key in new[] { "LeftPedalKey", "LeftPedalButton", "LeftPedalMIDI", "RightPedalKey", "RightPedalButton", "RightPedalMIDI" })
            if (!Settings.DefaultIni.Contains(key + "=")) throw new Exception("Missing pedal default: " + key);
        File.WriteAllText(settingsPath, "[Pedals]\nLeftPedalKey=left\nLeftPedalButton=A\nLeftPedalMIDI=0\nRightPedalKey=R\nRightPedalButton=20\nRightPedalMIDI=127\n");
        Settings bound = Settings.LoadOrCreate(settingsPath);
        if (bound.LeftPedal.Key != 276 || bound.LeftPedal.Button != 0 || bound.LeftPedal.MIDI != 0
            || bound.RightPedal.Key != 'r' || bound.RightPedal.Button != 19 || bound.RightPedal.MIDI != 127)
            throw new Exception("Pedal binding parsing failed");
        foreach ((string name, int code) in new[] { ("a", 97), ("Left Arrow", 276), ("left ctrl", 306), ("space", 32), ("F15", 296), ("keypad0", 256), ("-", 45), ("caps lock", 301), ("keypad enter", 271), ("print screen", 316) })
            if (PedalBinding.Parse(name, "", "").Key != code) throw new Exception("Pedal keyboard alias failed: " + name);
        string[] buttons = ["A", "B", "X", "Y", "LB", "RB", "Back", "Start", "LS", "RS"];
        for (int i = 0; i < buttons.Length; i++)
            if (PedalBinding.Parse("", buttons[i], "").Button != i || PedalBinding.Parse("", (i + 1).ToString(), "").Button != i)
                throw new Exception("Pedal button alias failed: " + buttons[i]);
        foreach ((string key, string value) in new[]
        {
            ("LeftPedalKey", "notakey"), ("LeftPedalButton", "0"), ("LeftPedalButton", "21"),
            ("LeftPedalButton", "-1"), ("RightPedalMIDI", "-1"), ("RightPedalMIDI", "128"), ("RightPedalMIDI", "10:36"),
        })
        {
            File.WriteAllText(settingsPath, $"[Pedals]\n{key}={value}\n");
            try { _ = Settings.LoadOrCreate(settingsPath); throw new Exception("Invalid pedal binding accepted: " + value); }
            catch (FormatException) { }
        }
        if (!PedalMidiInput.IsHit(0x012490, out int firstNote) || firstNote != 36)
            throw new Exception("MIDI Note On failed");
        for (uint channel = 0; channel < 16; channel++)
            if (!PedalMidiInput.IsHit(0x7F2490 | channel, out int note) || note != 36
                || PedalMidiInput.IsHit(0x002490 | channel, out _)
                || PedalMidiInput.IsHit(0x7F2480 | channel, out _)
                || PedalMidiInput.IsHit(0x7F24B0 | channel, out _))
                throw new Exception("MIDI channel/velocity filtering failed");

        DtxChart chart = new() { Directory = Path.GetTempPath(), MaxMeasure = 1 };
        int[] channels = [0x11, 0x12, 0x13, 0x1B, 0x1C];
        for (int i = 0; i < channels.Length; i++)
        {
            chart.Wavs[i + 1] = new DtxWav { File = "pedal-selftest-missing.wav" };
            chart.Chips.Add(new DtxChip { Measure = 1, Frac = i == 4 ? 0.5 : 0, Channel = channels[i], Value = i + 1 });
        }
        ConvertedChart automatic = new ChartConverter(new Settings(), _ => 0).Prepare(chart, false);
        if (automatic.Notes.Count(note => note.Playable) != 2 || automatic.Schedule.Count(sound => sound.FromNote) != 3)
            throw new Exception("Unbound pedal audio changed");
        ConvertedChart enabled = new ChartConverter(bound, _ => 0).Prepare(chart, false);
        if (enabled.Notes.Count(note => note.Playable) != 5 || enabled.Schedule.Any(sound => sound.FromNote)
            || enabled.Notes.Single(note => note.Channel == 0x1C).Lane != GameLane.LP)
            throw new Exception("Bound pedal conversion failed");
        Dictionary<int, int> map = GameParamWriter.AssignNotes([enabled.Notes]);
        ChartConverter.Finish(enabled, map);
        if (enabled.PlayableCount != 5 || enabled.AutoCount != 0 || enabled.Midi == null)
            throw new Exception("Pedals missing from MIDI / scoring count");
        PedalNoteMap pedalMap = new(enabled, map);
        foreach (ChartNote note in enabled.Notes.Where(note => note.Playable && note.Lane is GameLane.BD or GameLane.LP))
            if (!pedalMap.TryTake(map[note.WavId], out int key)
                || key != (note.Lane == GameLane.LP ? VirtualScreenLayout.LeftPedalKey : VirtualScreenLayout.RightPedalKey))
                throw new Exception("Native pedal lane restoration failed");
        if (pedalMap.Remaining != 0 || pedalMap.TryTake(999, out _))
            throw new Exception("Native pedal mapping consumption failed");
        ConvertedChart sharedSound = new()
        {
            Notes =
            [
                new() { Tick = 0, WavId = 1, Lane = GameLane.LP, Playable = true },
                new() { Tick = 120, WavId = 1, Lane = GameLane.BD, Playable = true },
                new() { Tick = 120, WavId = 1, Lane = GameLane.LP, Playable = true },
            ],
        };
        PedalNoteMap sharedMap = new(sharedSound, new Dictionary<int, int> { [1] = 36 });
        foreach (int expected in new[] { VirtualScreenLayout.LeftPedalKey, VirtualScreenLayout.RightPedalKey, VirtualScreenLayout.LeftPedalKey })
            if (!sharedMap.TryTake(36, out int key) || key != expected)
                throw new Exception("Shared-sample / simultaneous foot mapping failed");
        if (sharedMap.TryTake(36, out _) || sharedMap.Remaining != 0)
            throw new Exception("Pedal map reused a consumed note");
        bound.RightPedal = PedalBinding.Parse("", "", "");
        ConvertedChart leftOnly = new ChartConverter(bound, _ => 0).Prepare(chart, false);
        if (leftOnly.Notes.Single(note => note.Channel == 0x13).Playable
            || leftOnly.Notes.Count(note => note.Playable) != 4)
            throw new Exception("Independent pedal enablement failed");
        bound.BeginnerOnlyEightNotes = true;
        bound.BeginnerMinNoteGapMs = 2000;
        chart.Chips.Add(new DtxChip { Measure = 1, Frac = 0.03125, Channel = 0x1B, Value = 4 });
        ConvertedChart casual = new ChartConverter(bound, _ => 0).Prepare(chart, true);
        ConvertedChart advanced = new ChartConverter(bound, _ => 0).Prepare(chart, false);
        if (casual.Notes.Count(note => note.Playable && note.Lane == GameLane.LP) != 1
            || advanced.Notes.Count(note => note.Playable && note.Lane == GameLane.LP) != 3
            || casual.Schedule.Count(sound => sound.FromNote) != 3)
            throw new Exception("Pedal Casual filtering / automatic sounds failed");
    }

    private static void RunVideoSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        string defaults = File.ReadAllText(settingsPath);
        if (!defaults.Contains("GameplayPanelOpacity=1") || !defaults.Contains("CenterVideoDim=0.75")
            || defaults.Contains("CenterScreenOpacity"))
            throw new InvalidOperationException("New ini files must use separate panel opacity and video dimming keys.");
        if (!new Settings().LoadVideos || !created.LoadVideos
            || !Settings.LoadOrCreate(settingsPath).LoadVideos || !File.ReadAllText(settingsPath).Contains("LoadVideos=1"))
            throw new InvalidOperationException("LoadVideos must be enabled by default.");
        foreach ((string value, bool expected) in new[] { ("0", false), ("false", false), ("1", true), ("true", true) })
        {
            string ini = "[CustomSongs]\nLoadVideos=" + value + "\nTemplateSong=custom\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).LoadVideos != expected || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("LoadVideos must parse without rewriting existing settings.");
        }
        File.WriteAllText(settingsPath, "[CustomSongs]\n");
        if (!Settings.LoadOrCreate(settingsPath).LoadVideos)
            throw new InvalidOperationException("A missing LoadVideos key must use the enabled default.");
        if (created.GameplayPanelOpacity != 1f || Settings.LoadOrCreate(settingsPath).GameplayPanelOpacity != 1f
            || new Settings().CenterVideoDim != 0.75f
            || created.CenterVideoDim != 0.75f || Settings.LoadOrCreate(settingsPath).CenterVideoDim != 0.75f)
            throw new InvalidOperationException("Panel opacity and video dimming defaults are incorrect.");
        foreach ((string value, float expected) in new[] { ("0", 0f), ("0.25", 0.25f), ("1", 1f), ("-1", 0f), ("2", 1f) })
        {
            string ini = "[CustomSongs]\nCenterVideoDim=" + value + "\n[Game]\nGameplayPanelOpacity=" + value + "\n";
            File.WriteAllText(settingsPath, ini);
            Settings loaded = Settings.LoadOrCreate(settingsPath);
            if (loaded.GameplayPanelOpacity != expected || loaded.CenterVideoDim != expected || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Panel opacity and video dimming must clamp to 0-1 without rewriting ini files.");
        }
        foreach (string key in new[] { "[CustomSongs]\nCenterVideoDim", "[Game]\nGameplayPanelOpacity" })
        foreach (string invalid in new[] { "NaN", "Infinity", "-Infinity", "invalid", "" })
        {
            File.WriteAllText(settingsPath, key + "=" + invalid + "\n");
            try
            {
                _ = Settings.LoadOrCreate(settingsPath);
                throw new InvalidOperationException("Invalid opacity/dimming must be rejected.");
            }
            catch (FormatException) { }
        }
        foreach (string value in new[] { "0.25", "invalid" })
        {
            string obsolete = "[CustomSongs]\nCenterScreenOpacity=" + value + "\n";
            File.WriteAllText(settingsPath, obsolete);
            if (Settings.LoadOrCreate(settingsPath).GameplayPanelOpacity != 1f || File.ReadAllText(settingsPath) != obsolete)
                throw new InvalidOperationException("Obsolete center opacity must be ignored without rewriting the ini.");
        }
        File.WriteAllText(settingsPath, "[CustomSongs]\nCenterScreenOpacity=invalid\n[Game]\nGameplayPanelOpacity=0\n");
        if (Settings.LoadOrCreate(settingsPath).GameplayPanelOpacity != 0f)
            throw new InvalidOperationException("An obsolete invalid key must not override a valid new panel setting.");

        string folder = Path.Combine(Path.GetTempPath(), "beatarenhax-video-selftest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string chartPath = Path.Combine(folder, "test.dtx");
        string firstPath = Path.Combine(folder, "bg.mp4");
        string secondPath = Path.Combine(folder, "second.avi");
        try
        {
            File.WriteAllBytes(firstPath, []);
            File.WriteAllBytes(secondPath, []);
            File.WriteAllText(chartPath, string.Join("\n",
            [
                "#BPM:120",
                "#BPM01:240",
                "#STOP01:192",
                "#PATH_WAV:audio",
                "#AVIZZ:bg.mp4 ; comment",
                "#AVI01:second.avi",
                "#VIDEOAZ:bg.mp4",
                "#AVI02:missing.mp4",
                "#AVI00:ignored.mp4",
                "#RANDOM 2",
                "#IF 1",
                "#AVI01:wrong.mp4",
                "#ENDIF",
                "#ENDRANDOM",
                "#00012:01",
                "#00054:ZZ",
                "#00008:0001",
                "#00109:0001",
                "#00154:0001",
                "#0025A:AZ",
                "#00354:02",
                "#00454:03",
                "#00554:ZZ",
            ]));
            DtxChart source = DtxChart.Load(chartPath, random: new FixedRandom(2));
            if (source.Videos.Count != 4 || source.Videos[DtxChart.Base36("ZZ", 0)] != "bg.mp4"
                || source.Videos[1] != "second.avi" || source.ResolveFile(source.Videos[1], false) != secondPath)
                throw new InvalidOperationException("AVI IDs, VIDEO aliases, comments and conditional definitions must be respected.");
            ChartConverter converter = new(new Settings(), _ => 0);
            ConvertedChart advanced = converter.Prepare(source, false);
            ConvertedChart casual = converter.Prepare(source, true);
            ChartTimeline raw = ChartTimeline.Build(source, 0, 5);
            double leadIn = advanced.Notes[0].TimeMs;
            double[] expected = [leadIn, leadIn + raw.TickToMs(raw.MeasureTick(1, 0.5)),
                leadIn + raw.TickToMs(raw.MeasureTick(2, 0)), leadIn + raw.TickToMs(raw.MeasureTick(3, 0)),
                leadIn + raw.TickToMs(raw.MeasureTick(4, 0)), leadIn + raw.TickToMs(raw.MeasureTick(5, 0))];
            if (advanced.Videos.Count != 6 || casual.Videos.Count != 6 || leadIn < 2500)
                throw new InvalidOperationException("All video triggers must survive conversion in both difficulties with the chart lead-in.");
            for (int i = 0; i < expected.Length; i++)
                if (Math.Abs(advanced.Videos[i].TimeMs - expected[i]) > 0.001 || advanced.Videos[i].TimeMs != casual.Videos[i].TimeMs)
                    throw new InvalidOperationException("Video timing must include BPM changes, measure positions and STOPs.");
            if (advanced.Videos[0].Path != firstPath || advanced.Videos[1].Path != secondPath
                || advanced.Videos[2].Path != firstPath || advanced.Videos[3].Path != null || advanced.Videos[4].Path != null
                || advanced.Videos[5].Path != firstPath || advanced.Warnings.Count(w => w.Contains("DTX video")) != 2)
                throw new InvalidOperationException("Missing/undefined videos must be explicit fallback events; later triggers must remain playable.");
            ConvertedChart disabled = new ChartConverter(new Settings { LoadVideos = false }, _ => 0).Prepare(source, false);
            if (disabled.Videos.Count != 0 || disabled.Warnings.Any(w => w.Contains("DTX video"))
                || disabled.LengthMs != advanced.LengthMs || disabled.Notes.Count != advanced.Notes.Count
                || disabled.Schedule.Count != advanced.Schedule.Count)
                throw new InvalidOperationException("Disabling videos must leave notes, audio and song length unchanged.");
            Console.WriteLine("DTX AVI definitions, timed video triggers and LoadVideos settings self-test passed");
        }
        finally
        {
            foreach (string file in new[] { chartPath, firstPath, secondPath })
                if (File.Exists(file)) File.Delete(file);
            Directory.Delete(folder);
        }
    }

    private static void RunCustomChartSelectionSelfTest()
    {
        CustomSongChart[] charts = [.. Enumerable.Range(1, 5)
            .Select(index => new CustomSongChart { SetIndex = index, DLevel = index })];
        foreach ((ChartDifficultyCut cut, int[] expected) in new[]
        {
            (ChartDifficultyCut.Easiest, new[] { 1, 2, 3, 4 }),
            (ChartDifficultyCut.Hardest, new[] { 2, 3, 4, 5 }),
        })
        {
            int[] actual = [.. CustomChartSelection.Select(charts.Reverse(), cut).Select(chart => chart.SetIndex)];
            if (!actual.SequenceEqual(expected)) throw new InvalidOperationException("Four-chart selection failed: " + cut);
        }
        int[] sparse = [.. CustomChartSelection.Select([charts[3], charts[0]], ChartDifficultyCut.Hardest)
            .Select(chart => chart.SetIndex)];
        if (!sparse.SequenceEqual(new[] { 1, 4 })) throw new InvalidOperationException("Sparse chart selection failed.");
        for (int count = 0; count <= 4; count++)
        {
            foreach (ChartDifficultyCut cut in Enum.GetValues<ChartDifficultyCut>())
            {
                int[] actual = [.. CustomChartSelection.Select(charts.Take(count), cut).Select(chart => chart.SetIndex)];
                if (!actual.SequenceEqual(Enumerable.Range(1, count)))
                    throw new InvalidOperationException("Small chart-set selection failed: " + count + ", " + cut);
            }
        }
        foreach ((string label, string expected) in new[]
        {
            ("casual", "#33B5FF"), ("beginner", "#33FF33"), ("basic", "#33FF33"), ("BSC", "#33FF33"),
            ("Advanced", "#FF9900"), ("adv", "#FF9900"), ("EXTREME", "#FF3333"), ("ext", "#FF3333"),
            ("master", "#CC66FF"), ("MAS", "#CC66FF"), ("MST", "#CC66FF"), ("song_EXT", "#FF3333"),
            ("extreme2", (string)null), ("unknown", (string)null),
        })
            if (CustomChartSelection.RecognizedColor(label) != expected)
                throw new InvalidOperationException("Chart label color failed: " + label);
        Console.WriteLine("Four-chart selection and label color self-test passed");
    }

    private static void RunCustomScoreStoreSelfTest()
    {
        string file = Path.Combine(Path.GetTempPath(), "beatarenhax-scores-selftest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            CustomScoreKey first = new(5000, 1, 1);
            CustomScoreKey second = new(5000, 1, 4);
            CustomScoreKey instrument = new(5000, 0, 1);
            CustomScoreStore store = new(file);
            int[] judges = [1, 2, 3, 4, 5];
            if (!store.Update(first, 200, judges) || store.Update(first, 100, [9, 9, 9, 9, 9])
                || !store.Update(second, 300, judges) || !store.Update(instrument, 400, judges))
                throw new InvalidOperationException("Custom score update failed.");
            judges[0] = 99;
            CustomScoreStore loaded = new(file);
            CustomScore record = loaded.Get(first);
            if (record.PlayCount != 2 || record.Score != 200 || record.Judges[0] != 1
                || loaded.Get(second).Score != 300 || loaded.Get(instrument).Score != 400
                || loaded.GetSongScores(5000).Count != 3 || loaded.Get(new CustomScoreKey(5001, 1, 1)) != null)
                throw new InvalidOperationException("Custom score round-trip or slot isolation failed.");
            record.Judges[0] = 99;
            if (loaded.Get(first).Judges[0] != 1) throw new InvalidOperationException("Custom score leaked a mutable reference.");
            if (!loaded.Update(first, 250, [2, 3, 4, 5, 6])
                || new CustomScoreStore(file).Get(first).Score != 250)
                throw new InvalidOperationException("Custom score atomic replacement failed.");
            try { loaded.Update(first, 100, [1, 2]); throw new InvalidOperationException("Invalid judges were accepted."); }
            catch (ArgumentException) { }
            if (new CustomScoreStore(file).Get(first).PlayCount != 3)
                throw new InvalidOperationException("Rejected score modified the file.");
            File.WriteAllText(file, "{\"Version\":2,\"Scores\":[]}");
            try { _ = new CustomScoreStore(file); throw new InvalidOperationException("Unknown score version was accepted."); }
            catch (InvalidDataException) { }
            if (File.ReadAllText(file) != "{\"Version\":2,\"Scores\":[]}")
                throw new InvalidOperationException("Invalid score file was modified.");
            Console.WriteLine("Custom score persistence self-test passed");
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }

    private static void RunVirtualScreenSelfTest(string settingsPath)
    {
        Settings defaults = new();
        if (defaults.VirtualScreenOffsetY != 0f || defaults.VirtualScreenDistanceScale != 1f
            || !Settings.DefaultIni.Contains("OffsetY=0") || !Settings.DefaultIni.Contains("DistanceScale=1"))
            throw new Exception("Virtual screen placement defaults self-test failed");
        foreach ((string offset, string distance, float expectedOffset, float expectedDistance) in new[]
        {
            ("0.3", "2", 0.3f, 2f), ("-0.4", "0.5", -0.4f, 0.5f),
            ("-6", "0", -5f, 0.25f), ("6", "5", 5f, 4f),
        })
        {
            string placementIni = $"[VirtualScreen]\nOffsetY={offset}\nDistanceScale={distance}\n";
            File.WriteAllText(settingsPath, placementIni);
            Settings settings = Settings.LoadOrCreate(settingsPath);
            if (settings.VirtualScreenOffsetY != expectedOffset || settings.VirtualScreenDistanceScale != expectedDistance
                || File.ReadAllText(settingsPath) != placementIni)
                throw new Exception("Virtual screen placement settings self-test failed");
        }
        foreach (float scale in new[] { 0.25f, 0.5f, 1f, 2f, 3f, 4f })
        {
            foreach (float offset in new[] { -5f, 0f, 0.3f, 5f })
            {
                float height = VirtualScreenLayout.Height * VirtualScreenLayout.WorldUnitsPerPixel * scale;
                float center = VirtualScreenLayout.CenterHeight(scale, offset);
                if (MathF.Abs(center - height * 0.5f - (-0.15f + offset)) > 0.00001f)
                    throw new Exception("Virtual screen bottom must stay fixed when scaling");
            }
        }
        if (MathF.Abs(VirtualScreenLayout.CenterHeight(1f, 0f) - 0.65f) > 0.00001f
            || VirtualScreenLayout.BaseDistance != 1.5f)
            throw new Exception("Virtual screen default placement changed");
        if (!defaults.VirtualScreenShowAuto || !defaults.VirtualScreenGrayAuto
            || !Settings.DefaultIni.Contains("ShowAuto=1") || !Settings.DefaultIni.Contains("GrayAuto=1"))
            throw new Exception("Virtual screen automatic-note defaults self-test failed");
        foreach (string value in new[] { "0", "false", "1", "true" })
        {
            bool expected = value is "1" or "true";
            File.WriteAllText(settingsPath, $"[VirtualScreen]\nShowAuto={value}\nGrayAuto={value}\n");
            Settings settings = Settings.LoadOrCreate(settingsPath);
            if (settings.VirtualScreenShowAuto != expected || settings.VirtualScreenGrayAuto != expected)
                throw new Exception("Virtual screen automatic-note settings self-test failed");
        }
        if (defaults.VirtualScreen != "" || defaults.VirtualScreenScale != 1f || defaults.VirtualScreenBackgroundOpacity != 0.9f
            || !Settings.DefaultIni.Contains("[VirtualScreen]")
            || !Settings.DefaultIni.Contains("Scale=1")
            || !Settings.DefaultIni.Contains("BackgroundOpacity=0.9"))
            throw new Exception("Virtual screen default self-test failed");
        string ini = "[VirtualScreen]\nPath=\"G:\\Games\\PC\\DTXMania\\skins\\GALAXY WAVE\"\nScale=1.5\nBackgroundOpacity=0.25\n";
        File.WriteAllText(settingsPath, ini);
        Settings configured = Settings.LoadOrCreate(settingsPath);
        if (configured.VirtualScreen != "G:\\Games\\PC\\DTXMania\\skins\\GALAXY WAVE"
            || configured.VirtualScreenScale != 1.5f || configured.VirtualScreenBackgroundOpacity != 0.25f
            || File.ReadAllText(settingsPath) != ini)
            throw new Exception("Virtual screen settings self-test failed");
        File.WriteAllText(settingsPath, "[Game]\n");
        Settings missing = Settings.LoadOrCreate(settingsPath);
        if (missing.VirtualScreenOffsetY != 0f || missing.VirtualScreenDistanceScale != 1f)
            throw new Exception("Virtual screen placement missing-key self-test failed");
        if (!missing.VirtualScreenShowAuto || !missing.VirtualScreenGrayAuto)
            throw new Exception("Virtual screen automatic-note missing-key self-test failed");
        if (missing.VirtualScreen != "" || missing.VirtualScreenScale != 1f || missing.VirtualScreenBackgroundOpacity != 0.9f)
            throw new Exception("Virtual screen missing-key self-test failed");
        File.WriteAllText(settingsPath, "[Game]\nVirtualScreen=old\nVirtualScreenScale=2\nVirtualScreenBackgroundOpacity=0.5\n");
        Settings legacy = Settings.LoadOrCreate(settingsPath);
        if (legacy.VirtualScreen != "" || legacy.VirtualScreenScale != 1f || legacy.VirtualScreenBackgroundOpacity != 0.9f)
            throw new Exception("Obsolete virtual screen keys must be ignored.");
        foreach ((string scale, string opacity, float expectedScale, float expectedOpacity) in new[]
        {
            ("0", "-1", 0.25f, 0f), ("8", "2", 4f, 1f), ("0.25", "0", 0.25f, 0f), ("4", "1", 4f, 1f),
        })
        {
            File.WriteAllText(settingsPath, $"[VirtualScreen]\nScale={scale}\nBackgroundOpacity={opacity}\n");
            Settings settings = Settings.LoadOrCreate(settingsPath);
            if (settings.VirtualScreenScale != expectedScale || settings.VirtualScreenBackgroundOpacity != expectedOpacity)
                throw new Exception("Virtual screen settings range self-test failed");
        }
        foreach (string key in new[] { "Scale", "BackgroundOpacity", "OffsetY", "DistanceScale" })
        {
            foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "not-a-number" })
            {
                File.WriteAllText(settingsPath, $"[VirtualScreen]\n{key}={value}\n");
                try { _ = Settings.LoadOrCreate(settingsPath); throw new Exception(key + " accepted " + value); }
                catch (FormatException) { }
            }
        }
        int[] columns = [1, 3, 4, 6, 7, 8, 9, 0];
        foreach ((int channel, int key) in new[] { (0x13, 9), (0x1B, 8), (0x1C, 8), (0x12, 1), (0x19, 6), (0x01, -1) })
        {
            ChartNote automatic = new() { Channel = channel, Playable = false };
            if (VirtualScreenLayout.AutomaticKey(automatic) != key)
                throw new Exception("Virtual screen automatic-note mapping self-test failed");
            automatic.Playable = true;
            if (VirtualScreenLayout.AutomaticKey(automatic) != -1)
                throw new Exception("Virtual screen automatic notes duplicated a playable note");
        }
        if (VirtualScreenLayout.GrayPixel(255, 0, 0) != 76
            || VirtualScreenLayout.GrayPixel(0, 255, 0) != 150
            || VirtualScreenLayout.GrayPixel(0, 0, 255) != 29
            || VirtualScreenLayout.GrayPixel(0, 0, 0) != 0
            || VirtualScreenLayout.GrayPixel(255, 255, 255) != 255)
            throw new Exception("Virtual screen grayscale self-test failed");
        foreach (float angle in new[] { 0f, 0.5f, 1.5707964f, 3.1415927f, -1.5707964f })
        {
            float rightX = MathF.Cos(angle), rightZ = MathF.Sin(angle);
            (float x, float z) = VirtualScreenLayout.Forward(rightX * 0.4f, rightZ * 0.4f);
            if (MathF.Abs(x + rightZ) > 0.0001f || MathF.Abs(z - rightX) > 0.0001f
                || MathF.Abs(x * rightX + z * rightZ) > 0.0001f)
                throw new Exception("Virtual screen drum-relative direction self-test failed");
        }
        foreach ((float x, float z) in new[] { (0f, 0f), (float.NaN, 0f), (0f, float.PositiveInfinity) })
        {
            try { _ = VirtualScreenLayout.Forward(x, z); throw new Exception("Invalid drum direction accepted."); }
            catch (InvalidOperationException) { }
        }
        if (Math.Abs(VirtualScreenLayout.Width * VirtualScreenLayout.WorldUnitsPerPixel - 1.4f) > 0.0001f
            || Math.Abs(VirtualScreenLayout.Height * VirtualScreenLayout.WorldUnitsPerPixel - 1.6f) > 0.0001f
            || Math.Abs(VirtualScreenLayout.Width * VirtualScreenLayout.WorldUnitsPerPixel * 0.5f - 0.7f) > 0.0001f
            || Math.Abs(VirtualScreenLayout.Height * VirtualScreenLayout.WorldUnitsPerPixel * 0.5f - 0.8f) > 0.0001f)
            throw new Exception("Virtual screen physical scale self-test failed");
        if (VirtualScreenLayout.LaneCount != 10 || VirtualScreenLayout.LeftPedalColumn != 2 || VirtualScreenLayout.BassDrumColumn != 5)
            throw new Exception("Virtual screen foot lanes self-test failed");
        if (VirtualScreenLayout.LeftPedalChipX != 663 || VirtualScreenLayout.LeftPedalChipWidth != 55)
            throw new Exception("Virtual screen left-pedal sprite self-test failed");
        int[] fireRows = [1, 2, 4, 5, 6, 7, 9, 0, 8, 3];
        for (int key = 0; key < fireRows.Length; key++)
            if (VirtualScreenLayout.FireRow(key) != fireRows[key])
                throw new Exception("Virtual screen fire row mapping self-test failed");
        if (!VirtualScreenLayout.ShowFeedback(8, false, true, true)
            || !VirtualScreenLayout.ShowFeedback(9, false, true, true)
            || !VirtualScreenLayout.ShowFeedback(1, false, true, true)
            || !VirtualScreenLayout.ShowFeedback(0, false, false, false)
            || VirtualScreenLayout.ShowFeedback(0, false, true, false)
            || VirtualScreenLayout.ShowFeedback(8, true, true, true)
            || VirtualScreenLayout.ShowFeedback(9, true, false, true)
            || VirtualScreenLayout.ShowFeedback(-1, false, true, true))
            throw new Exception("Virtual screen feedback filtering self-test failed");
        if (VirtualScreenLayout.FireRow(-1) != -1 || VirtualScreenLayout.FireRow(10) != -1
            || VirtualScreenLayout.EffectFrame(-1, 50, 12) != -1
            || VirtualScreenLayout.EffectFrame(0, 50, 12) != 0
            || VirtualScreenLayout.EffectFrame(599, 50, 12) != 11
            || VirtualScreenLayout.EffectFrame(600, 50, 12) != -1
            || VirtualScreenLayout.EffectFrame(335, 14, 24) != 23
            || VirtualScreenLayout.EffectFrame(336, 14, 24) != -1)
            throw new Exception("Virtual screen feedback animation self-test failed");
        byte r = 0, g = 0, b = 0, a = 255;
        VirtualScreenLayout.FirePixel(ref r, ref g, ref b, ref a);
        if (a != 0) throw new Exception("Virtual screen fire black transparency self-test failed");
        r = 128; g = 64; b = 0; a = 128;
        VirtualScreenLayout.FirePixel(ref r, ref g, ref b, ref a);
        if (r != 255 || g != 128 || b != 0 || a != 64)
            throw new Exception("Virtual screen fire color and alpha self-test failed");
        for (int key = 0; key < VirtualScreenLayout.PlayableLaneCount; key++)
            if (VirtualScreenLayout.Column(key) != columns[key])
                throw new Exception("Virtual screen lane mapping self-test failed");
        if (VirtualScreenLayout.Column(-1) != -1 || VirtualScreenLayout.Column(10) != -1
            || VirtualScreenLayout.Column(VirtualScreenLayout.LeftPedalKey) != VirtualScreenLayout.LeftPedalColumn
            || VirtualScreenLayout.Column(VirtualScreenLayout.RightPedalKey) != VirtualScreenLayout.BassDrumColumn
            || VirtualScreenLayout.NoteY(1000, 1000) != VirtualScreenLayout.JudgeY
            || VirtualScreenLayout.NoteY(2800, 1000) != VirtualScreenLayout.Height)
            throw new Exception("Virtual screen timing self-test failed");
    }

    private static void RunShowStatsSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().ShowStats || !created.ShowStats || !Settings.LoadOrCreate(settingsPath).ShowStats
            || !File.ReadAllText(settingsPath).Contains("ShowStats=1"))
            throw new InvalidOperationException("ShowStats defaults must be enabled.");
        foreach ((string value, bool expected) in new[] { ("0", false), ("1", true), ("false", false), ("true", true) })
        {
            string ini = "[SongSelect]\nShowStats=" + value + "\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).ShowStats != expected || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("ShowStats parsing must preserve existing settings: " + value);
        }
        File.WriteAllText(settingsPath, "[SongSelect]\n");
        if (!Settings.LoadOrCreate(settingsPath).ShowStats)
            throw new InvalidOperationException("Missing ShowStats must use the default.");
        string stats = SongWheelStatsText.FormatLine("5.20", "AA", Colors.BeginnerDifficulty, "#FFFFFF") + "\n"
            + SongWheelStatsText.FormatLine("10.00", "--", Colors.AdvancedDifficulty, "#FFFFFF");
        if (stats != "<color=#33FF33>5.20</color> <color=#FFFFFF>AA</color>\n"
            + "<color=#FF9900>10.00</color> <color=#FFFFFF>--</color>")
            throw new InvalidOperationException("Song wheel stats must color only the levels and omit difficulty prefixes.");
    }

    private static void RunDecimalNotationSelfTest(string settingsPath, string chartPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().DecimalNotation || !created.DecimalNotation
            || !Settings.LoadOrCreate(settingsPath).DecimalNotation
            || !File.ReadAllText(settingsPath).Contains("DecimalNotation=1"))
            throw new InvalidOperationException("DecimalNotation defaults must be enabled.");
        foreach ((string value, bool expected) in new[] { ("0", false), ("1", true), ("false", false), ("true", true) })
        {
            string ini = "[CustomSongs]\nDecimalNotation=" + value + "\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).DecimalNotation != expected || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("DecimalNotation parsing must preserve existing settings: " + value);
        }
        File.WriteAllText(settingsPath, "[CustomSongs]\n");
        if (!Settings.LoadOrCreate(settingsPath).DecimalNotation)
            throw new InvalidOperationException("Missing DecimalNotation must use the default.");

        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            foreach ((string source, string expected) in new[] { ("52", "5.20"), ("28", "2.80"), ("84", "8.40"),
                ("99", "9.90"), ("5.2", "5.20"), ("78.5", "7.85"), ("0", "0.00"), ("10", "10.00"),
                ("100", "1.00"), ("425", "4.25"), ("520", "5.20"), ("999", "9.99"), ("425 ; comment", "4.25") })
            {
                File.WriteAllText(chartPath, "#TITLE Decimal test\n#DLEVEL: " + source + "\n#BPM 120\n");
                foreach (bool headerOnly in new[] { false, true })
                {
                    DtxChart parsed = DtxChart.Load(chartPath, headerOnly);
                    CustomSongChart chart = new() { DLevel = parsed.DLevel };
                    if (chart.GetDecimalLevelText() != expected)
                        throw new InvalidOperationException("Decimal level mismatch for " + source + ": " + chart.GetDecimalLevelText());
                    double normalized = double.Parse(expected, CultureInfo.InvariantCulture) * 10;
                    if (Math.Abs(parsed.DLevel - normalized) > 0.000001
                        || ChartConverter.ToGameLevel(parsed.DLevel) != ChartConverter.ToGameLevel(normalized))
                        throw new InvalidOperationException("Normalized level mismatch for " + source);
                }
            }
        }
        finally { CultureInfo.CurrentCulture = previousCulture; }
    }

    private static void RunGrabSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().GrabWithoutWait || !created.GrabWithoutWait
            || !Settings.LoadOrCreate(settingsPath).GrabWithoutWait
            || !new Settings().GrabWithoutVibration || !created.GrabWithoutVibration
            || !Settings.LoadOrCreate(settingsPath).GrabWithoutVibration
            || !File.ReadAllText(settingsPath).Contains("GrabWithoutWait=1")
            || !File.ReadAllText(settingsPath).Contains("GrabWithoutVibration=1"))
            throw new InvalidOperationException("Grab settings defaults should be enabled");

        foreach (string wait in new[] { "0", "1", "false", "true" })
        foreach (string vibration in new[] { "0", "1", "false", "true" })
        {
            string ini = $"[Game]\nGrabWithoutWait={wait}\nGrabWithoutVibration={vibration}\n";
            File.WriteAllText(settingsPath, ini);
            Settings settings = Settings.LoadOrCreate(settingsPath);
            if (settings.GrabWithoutWait != (wait is "1" or "true")
                || settings.GrabWithoutVibration != (vibration is "1" or "true")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException($"Grab settings parsing/preservation failed for {wait}/{vibration}");
        }

        const string unrelated = "[SongSelect]\nGrabWithoutWait=0\nGrabWithoutVibration=0\n";
        File.WriteAllText(settingsPath, unrelated);
        Settings missing = Settings.LoadOrCreate(settingsPath);
        if (!missing.GrabWithoutWait || !missing.GrabWithoutVibration || File.ReadAllText(settingsPath) != unrelated)
            throw new InvalidOperationException("Missing Game grab settings should use defaults without rewriting the ini");
    }

    private static void RunStoolSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        foreach (Settings defaults in new[] { new Settings(), created, Settings.LoadOrCreate(settingsPath) })
            if (defaults.StoolHeight != -0.1f || defaults.StoolOffsetX != 0f || defaults.StoolOffsetZ != -0.3f)
                throw new InvalidOperationException("Vanilla stool default offsets must be X=0, Y=-0.1, Z=-0.3 metres.");
        foreach (string entry in new[] { "StoolHeight=-0.1", "StoolOffsetX=0", "StoolOffsetZ=-0.30" })
            if (!File.ReadAllText(settingsPath).Contains(entry))
                throw new InvalidOperationException("Missing stool INI default: " + entry);

        foreach (string key in new[] { "StoolHeight", "StoolOffsetX", "StoolOffsetZ" })
        {
            foreach ((string value, float expected) in new[] { ("0", 0f), ("-0.1", -0.1f), ("0.25", 0.25f), ("-2", -0.5f), ("2", 0.5f) })
            {
                string ini = "[Game]\n" + key + "=" + value + "\n";
                File.WriteAllText(settingsPath, ini);
                Settings loaded = Settings.LoadOrCreate(settingsPath);
                float actual = key == "StoolHeight" ? loaded.StoolHeight : key == "StoolOffsetX" ? loaded.StoolOffsetX : loaded.StoolOffsetZ;
                if (actual != expected || File.ReadAllText(settingsPath) != ini)
                    throw new InvalidOperationException(key + " parsing/clamping must preserve existing settings: " + value);
            }

            foreach (string value in new[] { "NaN", "Infinity", "-Infinity", "invalid" })
            {
                File.WriteAllText(settingsPath, "[Game]\n" + key + "=" + value + "\n");
                try
                {
                    _ = Settings.LoadOrCreate(settingsPath);
                    throw new InvalidOperationException("Invalid " + key + " was accepted: " + value);
                }
                catch (FormatException) { }
            }
        }
        File.WriteAllText(settingsPath, "[Game]\nStoolHeight=0\nStoolOffsetX=0.2\nStoolOffsetZ=-0.30\n");
        Settings combined = Settings.LoadOrCreate(settingsPath);
        if (combined.StoolHeight != 0f || combined.StoolOffsetX != 0.2f || combined.StoolOffsetZ != -0.3f)
            throw new InvalidOperationException("Horizontal stool offsets must be independent of height.");
        const string unrelated = "[Game]\nStool=0\nStoolHeigt=0.4\n[SongSelect]\nStoolHeight=0\nStoolOffsetX=0.2\nStoolOffsetZ=0.4\n";
        File.WriteAllText(settingsPath, unrelated);
        Settings missing = Settings.LoadOrCreate(settingsPath);
        if (missing.StoolHeight != -0.1f || missing.StoolOffsetX != 0f || missing.StoolOffsetZ != -0.3f || File.ReadAllText(settingsPath) != unrelated)
            throw new InvalidOperationException("Missing Game.StoolHeight must default without using removed keys or rewriting the ini.");
    }

    private static void RunSpectatorSettingsSelfTest(string settingsPath)
    {
        foreach ((string label, string tag) in new[] {
            ("BASIC", "BSC"), ("advanced", "ADV"), ("EXTREME", "EXT"),
            ("MASTER", "MAS"), ("INSANE", "INS"), ("song_ext", "EXT"),
            ("BSC", "BSC"), ("ADV", "ADV"), ("EXT", "EXT"), ("MAS", "MAS"), ("INS", "INS"),
        })
        {
            CustomSongChart chart = new() { Label = label };
            if (chart.GetDifficultyTag(false) != tag || chart.GetDifficultyTag(true) != tag)
                throw new InvalidOperationException("Spectator chart tag self-test failed: " + label);
        }
        foreach (string label in new[] { null, "", "L3", "chart" })
        {
            CustomSongChart chart = new() { Label = label };
            if (chart.GetDifficultyTag(false) != "BSC" || chart.GetDifficultyTag(true) != "ADV")
                throw new InvalidOperationException("Spectator unlabeled chart tag self-test failed.");
        }
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        foreach (Settings settings in new[] { new Settings(), created, Settings.LoadOrCreate(settingsPath) })
            if (settings.SpectatorCamera != SpectatorCamera.ThirdPerson || !settings.SpectatorSongInfo
                || !settings.SpectatorCustomTitle || !settings.SpectatorThirdPersonKeyboardMouseControl
                || settings.SpectatorShowVirtualScreen || settings.SpectatorCinematicGameplay
                || settings.SpectatorWidth != 1024 || settings.SpectatorHeight != 768)
                throw new InvalidOperationException("Spectator defaults self-test failed.");
        string defaults = File.ReadAllText(settingsPath);
        if (!defaults.Contains("[Spectator]") || !defaults.Contains("ThirdPerson=1") || defaults.Contains("Camera=") || !defaults.Contains("SongInfo=1")
            || !defaults.Contains("CustomTitle=1") || !defaults.Contains("ShowVirtualScreen=0") || !defaults.Contains("CinematicGameplay=0")
            || !defaults.Contains("CinematicCameraTypes=") || !defaults.Contains("ThirdPersonKeyboardMouseControl=1")
            || !defaults.Contains("Width=1024") || !defaults.Contains("Height=768"))
            throw new InvalidOperationException("Spectator default ini self-test failed.");
        foreach (string value in new[] { "0", "1", "false", "true" })
        foreach (string info in new[] { "0", "1", "false", "true" })
        {
            string ini = $"[Spectator]\nThirdPerson={value}\nSongInfo={info}\n";
            File.WriteAllText(settingsPath, ini);
            Settings settings = Settings.LoadOrCreate(settingsPath);
            SpectatorCamera expected = value is "1" or "true" ? SpectatorCamera.ThirdPerson : SpectatorCamera.VR;
            if (settings.SpectatorCamera != expected
                || settings.SpectatorSongInfo != (info is "1" or "true") || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Spectator third-person parsing self-test failed.");
        }
        foreach ((string legacy, SpectatorCamera expected) in new[]
        {
            ("VR", SpectatorCamera.VR),
            ("vr", SpectatorCamera.VR),
            ("ThirdPerson", SpectatorCamera.ThirdPerson),
            ("thirdperson", SpectatorCamera.ThirdPerson),
        })
        {
            string ini = $"[Spectator]\nCamera={legacy}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).SpectatorCamera != expected || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Legacy spectator camera migration self-test failed.");
        }
        const string precedenceIni = "[Spectator]\nThirdPerson=0\nCamera=ThirdPerson\n";
        File.WriteAllText(settingsPath, precedenceIni);
        if (Settings.LoadOrCreate(settingsPath).SpectatorCamera != SpectatorCamera.VR)
            throw new InvalidOperationException("Spectator third-person key precedence self-test failed.");
        string missingIni = "[Game]\nCamera=VR\nSongInfo=0\n";
        File.WriteAllText(settingsPath, missingIni);
        Settings missing = Settings.LoadOrCreate(settingsPath);
        if (missing.SpectatorCamera != SpectatorCamera.ThirdPerson || !missing.SpectatorSongInfo
            || !missing.SpectatorCustomTitle || !missing.SpectatorThirdPersonKeyboardMouseControl
            || missing.SpectatorShowVirtualScreen || missing.SpectatorCinematicGameplay
            || missing.SpectatorWidth != 1024 || missing.SpectatorHeight != 768
            || File.ReadAllText(settingsPath) != missingIni)
            throw new InvalidOperationException("Spectator missing keys self-test failed.");
        foreach (string value in new[] { "0", "1", "false", "true" })
        {
            string controlIni = $"[Spectator]\nThirdPersonKeyboardMouseControl={value}\n";
            File.WriteAllText(settingsPath, controlIni);
            Settings controlSettings = Settings.LoadOrCreate(settingsPath);
            if (controlSettings.SpectatorThirdPersonKeyboardMouseControl != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != controlIni)
                throw new InvalidOperationException("Spectator keyboard/mouse parsing self-test failed.");
        }
        foreach (string value in new[] { "0", "1", "false", "true" })
        {
            string cinematicIni = $"[Spectator]\nCinematicGameplay={value}\n";
            File.WriteAllText(settingsPath, cinematicIni);
            Settings cinematicSettings = Settings.LoadOrCreate(settingsPath);
            if (cinematicSettings.SpectatorCinematicGameplay != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != cinematicIni)
                throw new InvalidOperationException("Spectator cinematic parsing self-test failed.");
        }
        foreach (string value in new[] { "", "  ", "Standard,High, arcup,high", "PlayerFirstPerson", "NpcFirstPerson" })
        {
            string ini = $"[Spectator]\nCinematicCameraTypes={value}\n";
            File.WriteAllText(settingsPath, ini);
            CinematicShot[] types = Settings.LoadOrCreate(settingsPath).SpectatorCinematicCameraTypes;
            CinematicShot[] expected = CinematicCameraDirector.ParseTypes(value);
            if (!types.SequenceEqual(expected) || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Cinematic camera type parsing failed.");
        }
        foreach (string value in new[] { "unknown", "1", "High,", ",High", "High,,Low" })
        {
            File.WriteAllText(settingsPath, $"[Spectator]\nCinematicCameraTypes={value}\n");
            try { Settings.LoadOrCreate(settingsPath); }
            catch (FormatException) { continue; }
            throw new InvalidOperationException("Invalid cinematic camera type was accepted: " + value);
        }
        foreach (string value in new[] { "0", "1", "false", "true" })
        {
            string screenIni = $"[Spectator]\nShowVirtualScreen={value}\n";
            File.WriteAllText(settingsPath, screenIni);
            Settings screenSettings = Settings.LoadOrCreate(settingsPath);
            if (screenSettings.SpectatorShowVirtualScreen != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != screenIni)
                throw new InvalidOperationException("Spectator virtual-screen parsing self-test failed.");
        }
        foreach (string value in new[] { "0", "1", "false", "true" })
        {
            string titleIni = $"[Spectator]\nCustomTitle={value}\n";
            File.WriteAllText(settingsPath, titleIni);
            Settings titleSettings = Settings.LoadOrCreate(settingsPath);
            if (titleSettings.SpectatorCustomTitle != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != titleIni)
                throw new InvalidOperationException("Spectator custom-title parsing self-test failed.");
        }
        const string resolutionIni = "[Spectator]\nWidth=1920\nHeight=1080\n";
        File.WriteAllText(settingsPath, resolutionIni);
        Settings resolution = Settings.LoadOrCreate(settingsPath);
        if (resolution.SpectatorWidth != 1920 || resolution.SpectatorHeight != 1080
            || File.ReadAllText(settingsPath) != resolutionIni)
            throw new InvalidOperationException("Spectator resolution parsing self-test failed.");
        File.WriteAllText(settingsPath, "[Spectator]\nWidth=1\nHeight=99999\n");
        Settings clamped = Settings.LoadOrCreate(settingsPath);
        if (clamped.SpectatorWidth != 320 || clamped.SpectatorHeight != 4320)
            throw new InvalidOperationException("Spectator resolution range self-test failed.");
        foreach (string invalid in new[] { "FirstPerson", "ThirdPersonCinematic", "2" })
        {
            File.WriteAllText(settingsPath, $"[Spectator]\nCamera={invalid}\n");
            try { Settings.LoadOrCreate(settingsPath); }
            catch (FormatException) { continue; }
            throw new InvalidOperationException("Invalid legacy spectator camera was accepted.");
        }
    }

    private static void RunSpectatorCameraControlSelfTest()
    {
        SpectatorCameraControl control = new();
        static bool Near(float actual, float expected) => Math.Abs(actual - expected) < 0.0001f;
        control.Update(0, 1, 0.5f, 0, 0, false, false, false);
        if (!Near(control.X, 0) || !Near(control.Z, 1))
            throw new InvalidOperationException("Spectator forward movement failed.");
        control.Update(0, 0, 0, 450, 25, true, true, false);
        if (!Near(control.Yaw, 90) || !Near(control.Pitch, -5) || !Near(control.Y, -0.125f))
            throw new InvalidOperationException("Spectator mouse controls failed.");
        control.Update(0, 0, 0, 0, -50, false, true, false);
        if (!Near(control.Y, 0.125f))
            throw new InvalidOperationException("Spectator downward drag failed.");
        control.Update(0, 1, 0.5f, 0, 0, false, false, false);
        if (!Near(control.X, 1) || !Near(control.Z, 1))
            throw new InvalidOperationException("Spectator heading-relative movement failed.");
        control.Reset();
        control.Update(1, 1, 0.5f, 0, 0, false, false, false);
        if (!Near(MathF.Sqrt(control.X * control.X + control.Z * control.Z), 1))
            throw new InvalidOperationException("Spectator diagonal movement speed failed.");
        control.Update(0, 0, 0, 0, 10000, true, false, false);
        if (!Near(control.Pitch + SpectatorCameraControl.DefaultPitch, -89))
            throw new InvalidOperationException("Spectator pitch clamp failed.");
        control.Update(1, 1, 1, 20, 20, true, true, true);
        if (!Near(control.X, 0) || !Near(control.Y, 0) || !Near(control.Z, 0)
            || !Near(control.Yaw, 0) || !Near(control.Pitch, 0))
            throw new InvalidOperationException("Spectator camera reset failed.");
    }

    private static void RunCinematicCameraSelfTest()
    {
        CinematicCameraDirector director = new(new Random(42));
        HashSet<CinematicShot> seen = [];
        float start = 10f;
        director.Update(true, start, true);
        for (int i = 0; i < 300; i++)
        {
            CinematicShot expected = director.Shot;
            if ((expected == CinematicShot.Standard) != (i % 2 == 0))
                throw new InvalidOperationException("Cinematic camera did not alternate with the standard view.");
            seen.Add(expected);
            float duration = director.Duration;
            float minimum = CinematicCameraDirector.IsMoving(expected) ? 6f : 3f;
            if (duration < minimum || duration > 15f)
                throw new InvalidOperationException("Cinematic camera delay outside the shot's allowed range.");
            director.Update(true, start + duration * 0.5f, true);
            if (director.Shot != expected || Math.Abs(director.Progress - 0.5f) > 0.001f)
                throw new InvalidOperationException("Cinematic camera shot progress is incorrect.");
            director.Update(true, start + duration - 0.001f, true);
            if (director.Shot != expected)
                throw new InvalidOperationException("Cinematic camera switched too early.");
            start += duration + 0.001f;
            director.Update(true, start, true);
            if (director.Shot == expected || director.Progress != 0f)
                throw new InvalidOperationException("Cinematic camera did not switch at its deadline.");
        }
        if (seen.Count != 9)
            throw new InvalidOperationException("Cinematic camera did not select all shot types.");
        director.Update(false, start);
        if (director.Shot != CinematicShot.Standard)
            throw new InvalidOperationException("Cinematic camera did not restore the standard view.");
        director.Update(true, start + 100f);
        if (director.Shot != CinematicShot.Standard || director.Progress != 0f)
            throw new InvalidOperationException("Cinematic camera did not restart on the standard view.");
        for (int i = 0; i < 200; i++)
        {
            start += 16f;
            director.Update(true, start + 100f, false);
            if (director.Shot == CinematicShot.NpcFirstPerson)
                throw new InvalidOperationException("Cinematic camera selected an unavailable NPC.");
        }
        foreach (CinematicShot[] allowed in new[] {
            new[] { CinematicShot.High },
            new[] { CinematicShot.ArcUp, CinematicShot.Low },
            new[] { CinematicShot.Standard, CinematicShot.PlayerFirstPerson },
        })
        {
            director.Update(false, start);
            HashSet<CinematicShot> selected = [];
            for (int i = 0; i < 100; i++)
            {
                start += 16f;
                director.Update(true, start, true, allowed);
                if (!allowed.Contains(director.Shot))
                    throw new InvalidOperationException("Cinematic director selected a disallowed shot.");
                selected.Add(director.Shot);
            }
            if (selected.Count != allowed.Length)
                throw new InvalidOperationException("Cinematic director did not select all allowed shots.");
        }
        director.Update(false, start);
        CinematicShot[] npcOnly = [CinematicShot.NpcFirstPerson];
        director.Update(true, start, false, npcOnly);
        if (director.Shot != CinematicShot.Standard)
            throw new InvalidOperationException("Unavailable NPC did not use the safe spectator fallback.");
        director.Update(true, start + 1, true, npcOnly);
        if (director.Shot != CinematicShot.NpcFirstPerson)
            throw new InvalidOperationException("Available NPC shot was not resumed.");
        director.Update(true, start + 2, false, npcOnly);
        if (director.Shot != CinematicShot.Standard)
            throw new InvalidOperationException("Lost NPC did not restore the spectator fallback.");
    }

    private static void RunQuitButtonSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().QuitButton || !created.QuitButton
            || !Settings.LoadOrCreate(settingsPath).QuitButton
            || !File.ReadAllText(settingsPath).Contains("QuitButton=1"))
            throw new InvalidOperationException("Quit button defaults/INI creation self-test failed");
        foreach (string value in new[] { "1", "true", "yes", "on", "0", "false", "no", "off" })
        {
            string ini = $"[Game]\nQuitButton={value}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).QuitButton != (value is "1" or "true" or "yes" or "on")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Quit button parsing/INI preservation self-test failed");
        }
        File.WriteAllText(settingsPath, "[SongSelect]\nQuitButton=0\n");
        if (!Settings.LoadOrCreate(settingsPath).QuitButton)
            throw new InvalidOperationException("QuitButton must be in [Game]");
    }

    private static void RunFastSlowSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        foreach (Settings settings in new[] { new Settings(), created, Settings.LoadOrCreate(settingsPath) })
            if (!settings.DisplayFastSlow || settings.DisplayFastSlowOnPerfects)
                throw new InvalidOperationException("FAST/SLOW defaults self-test failed");
        string defaults = File.ReadAllText(settingsPath);
        if (!defaults.Contains("DisplayFastSlow=1") || !defaults.Contains("DisplayFastSlowOnPerfects=0"))
            throw new InvalidOperationException("FAST/SLOW INI creation self-test failed");

        foreach (string display in new[] { "1", "true", "0", "false" })
        foreach (string perfects in new[] { "1", "true", "0", "false" })
        {
            string ini = $"[Game]\nDisplayFastSlow={display}\nDisplayFastSlowOnPerfects={perfects}\n";
            File.WriteAllText(settingsPath, ini);
            Settings settings = Settings.LoadOrCreate(settingsPath);
            if (settings.DisplayFastSlow != (display is "1" or "true")
                || settings.DisplayFastSlowOnPerfects != (perfects is "1" or "true")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("FAST/SLOW parsing/INI preservation self-test failed");
        }
        File.WriteAllText(settingsPath, "[SongSelect]\nDisplayFastSlow=0\nDisplayFastSlowOnPerfects=1\n");
        Settings missing = Settings.LoadOrCreate(settingsPath);
        if (!missing.DisplayFastSlow || missing.DisplayFastSlowOnPerfects)
            throw new InvalidOperationException("FAST/SLOW settings must be in [Game]");
    }

    private static void RunResultMenuSettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().MoveResultMenuToTheSide || !created.MoveResultMenuToTheSide
            || !Settings.LoadOrCreate(settingsPath).MoveResultMenuToTheSide
            || !File.ReadAllText(settingsPath).Contains("MoveResultMenuToTheSide=1")
            || File.ReadAllText(settingsPath).Contains("DisplayResultMenuOnSide="))
            throw new InvalidOperationException("Result menu side offset defaults/INI creation self-test failed");

        foreach (string value in new[] { "1", "true", "yes", "on", "0", "false", "no", "off" })
        {
            string ini = $"[Game]\nMoveResultMenuToTheSide={value}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).MoveResultMenuToTheSide != (value is "1" or "true" or "yes" or "on")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException($"Result menu side offset parsing/INI preservation self-test failed: {value}");
        }

        foreach (string value in new[] { "0", "invalid" })
        {
            string ini = $"[Game]\nDisplayResultMenuOnSide={value}\n";
            File.WriteAllText(settingsPath, ini);
            if (!Settings.LoadOrCreate(settingsPath).MoveResultMenuToTheSide || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Obsolete result menu setting must be ignored.");
        }
        foreach ((string current, string legacy) in new[] { ("0", "1"), ("1", "0") })
        {
            string ini = $"[Game]\nMoveResultMenuToTheSide={current}\nDisplayResultMenuOnSide={legacy}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).MoveResultMenuToTheSide != (current == "1")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Current result menu setting should take precedence over the legacy key");
        }

        const string unrelated = "[SongSelect]\nMoveResultMenuToTheSide=0\nDisplayResultMenuOnSide=0\n";
        File.WriteAllText(settingsPath, unrelated);
        if (!Settings.LoadOrCreate(settingsPath).MoveResultMenuToTheSide || File.ReadAllText(settingsPath) != unrelated)
            throw new InvalidOperationException("Missing Game.MoveResultMenuToTheSide should use the default");
    }

    private static void RunResultDifficultySettingsSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().ShowDifficultyOnResultScreen || !created.ShowDifficultyOnResultScreen
            || !Settings.LoadOrCreate(settingsPath).ShowDifficultyOnResultScreen
            || !File.ReadAllText(settingsPath).Contains("ShowDifficultyOnResultScreen=1"))
            throw new InvalidOperationException("Result difficulty defaults/INI creation self-test failed");

        foreach (string value in new[] { "1", "true", "0", "false" })
        {
            string ini = $"[Game]\nShowDifficultyOnResultScreen={value}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).ShowDifficultyOnResultScreen != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Result difficulty parsing/INI preservation self-test failed: " + value);
        }

        const string unrelated = "[SongSelect]\nShowDifficultyOnResultScreen=0\n";
        File.WriteAllText(settingsPath, unrelated);
        if (!Settings.LoadOrCreate(settingsPath).ShowDifficultyOnResultScreen || File.ReadAllText(settingsPath) != unrelated)
            throw new InvalidOperationException("Missing Game.ShowDifficultyOnResultScreen should use the default");
    }

    private static void RunFolderNameSelfTest(string settingsPath)
    {
        File.Delete(settingsPath);
        Settings created = Settings.LoadOrCreate(settingsPath);
        if (!new Settings().DisplayFolderName || !created.DisplayFolderName
            || !Settings.LoadOrCreate(settingsPath).DisplayFolderName
            || !File.ReadAllText(settingsPath).Contains("DisplayFolderName=1"))
            throw new InvalidOperationException("Folder caption defaults/INI creation self-test failed");
        foreach (string value in new[] { "1", "true", "0", "false" })
        {
            string ini = $"[CustomSongs]\nDisplayFolderName={value}\n";
            File.WriteAllText(settingsPath, ini);
            if (Settings.LoadOrCreate(settingsPath).DisplayFolderName != (value is "1" or "true")
                || File.ReadAllText(settingsPath) != ini)
                throw new InvalidOperationException("Folder caption parsing/INI preservation self-test failed: " + value);
        }
        File.WriteAllText(settingsPath, "[SongSelect]\nDisplayFolderName=0\n");
        if (!Settings.LoadOrCreate(settingsPath).DisplayFolderName)
            throw new InvalidOperationException("Missing CustomSongs.DisplayFolderName should use the default");

        string fixture = Path.Combine(Path.GetTempPath(), "beatarenhax-folder-selftest-" + Guid.NewGuid().ToString("N"));
        string root = Path.Combine(fixture, "DTXFiles");
        string approved = Path.Combine(root, "DTXFiles.APPROVEDDTX");
        string numbered = Path.Combine(approved, "DTXFiles.101-150");
        string galaxy = Path.Combine(root, "GITADORA GALAXY WAVE");
        string crimson = Path.Combine(numbered, "Crimson Fighter");
        string arcadia = Path.Combine(galaxy, "Arcadia");
        string ids = Path.Combine(fixture, "ids.txt");
        try
        {
            foreach (string folder in new[] { crimson, arcadia })
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(Path.Combine(folder, "chart.dtx"),
                    "#TITLE Test song\n#ARTIST Original artist\n#DLEVEL 50\n#BPM 120\n");
            }
            File.WriteAllText(Path.Combine(galaxy, "BoX.DeF"), "#TITLEsuffix: Wrong\n#title: BOX collection\n",
                new System.Text.UTF8Encoding(true));
            SongLibrary library = new();
            Settings settings = new();
            void Check(string scanRoot, params string[] expected)
            {
                library.Scan(scanRoot, settings, ids);
                string[] names = library.Songs.Select(song => song.DisplayFolderName).OrderBy(name => name).ToArray();
                if (library.Warnings.Count != 0 || !names.SequenceEqual(expected.OrderBy(name => name))
                    || library.Songs.Any(song => song.Title != "Test song" || song.Artist != "Original artist"))
                    throw new InvalidOperationException("Folder resolution self-test failed: "
                        + string.Join(", ", names) + "; " + string.Join("; ", library.Warnings));
            }
            Check(root, "APPROVEDDTX", "BOX collection");
            string savedIds = File.ReadAllText(ids);
            Check(root, "APPROVEDDTX", "BOX collection");
            if (File.ReadAllText(ids) != savedIds)
                throw new InvalidOperationException("Folder captions must not change stable IDs");
            Check(approved, "APPROVEDDTX");
            File.Delete(Path.Combine(galaxy, "BoX.DeF"));
            Check(root, "APPROVEDDTX", "GITADORA GALAXY WAVE");
            File.WriteAllText(Path.Combine(approved, "box.def"), "#TITLE: Parent BOX\n");
            Check(root, "Parent BOX", "GITADORA GALAXY WAVE");
            File.WriteAllText(Path.Combine(numbered, "box.def"), "#TITLE Nearest BOX\n");
            Check(root, "Nearest BOX", "GITADORA GALAXY WAVE");
            Check(crimson, "Crimson Fighter");
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#TITLE:   \n");
            library.Scan(root, settings, ids);
            if (!library.Warnings.Any(warning => warning.Contains("No non-empty #TITLE"))
                || library.Songs.Single(song => song.Folder == arcadia).DisplayFolderName != "GITADORA GALAXY WAVE")
                throw new InvalidOperationException("Empty BOX title must warn and use the folder name");
            settings.DisplayFolderName = false;
            settings.DisplayFolderPicker = false;
            library.Warnings.Clear();
            Check(root, null, null);
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#TITLE: Galaxy\n#PREIMAGE: tile.png\n");
            File.WriteAllBytes(Path.Combine(galaxy, "tile.png"), [1, 2, 3]);
            File.WriteAllBytes(Path.Combine(galaxy, "pre.png"), [4, 5, 6]);
            settings.DisplayFolderPicker = true;
            library.Scan(root, settings, ids);
            FolderInfo galaxyFolder = library.Folders.Single(folder => folder.Name == "Galaxy");
            if (galaxyFolder.PreImagePath != Path.Combine(galaxy, "tile.png") || galaxyFolder.SongIds.Count != 1
                || library.Songs.Any(song => song.DisplayFolderName != null))
                throw new InvalidOperationException("Folder panel metadata must work with captions disabled and prefer BOX artwork.");
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#TITLE: Galaxy\n");
            library.Scan(root, settings, ids);
            galaxyFolder = library.Folders.Single(folder => folder.Name == "Galaxy");
            if (galaxyFolder.PreImagePath != Path.Combine(galaxy, "pre.png"))
                throw new InvalidOperationException("Folder pre.png fallback failed.");
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#TITLE: Galaxy\n#PREIMAGE: missing.png\n");
            library.Warnings.Clear();
            library.Scan(root, settings, ids);
            if (!library.Warnings.Any(warning => warning.Contains("missing.png"))
                || library.Folders.Single(folder => folder.Name == "Galaxy").PreImagePath != Path.Combine(galaxy, "pre.png"))
                throw new InvalidOperationException("Missing BOX artwork must warn and fall back to pre.png.");
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#PREIMAGE: tile.png\n");
            library.Scan(root, settings, ids);
            if (library.Folders.Single(folder => folder.Name == "GITADORA GALAXY WAVE").PreImagePath != Path.Combine(galaxy, "tile.png"))
                throw new InvalidOperationException("BOX artwork must not depend on a TITLE entry.");
            File.WriteAllText(Path.Combine(galaxy, "box.def"), "#TITLE: Galaxy\n");
            string other = Path.Combine(galaxy, "Second song");
            Directory.CreateDirectory(other);
            File.Copy(Path.Combine(arcadia, "chart.dtx"), Path.Combine(other, "chart.dtx"));
            library.Scan(root, settings, ids);
            galaxyFolder = library.Folders.Single(folder => folder.Name == "Galaxy");
            if (galaxyFolder.SongIds.Count != 2 || galaxyFolder.SongIds.Distinct().Count() != 2)
                throw new InvalidOperationException("Collection grouping must include every accepted song ID once.");
            int customId = library.Songs.Single(song => song.Folder == arcadia).Id;
            foreach ((FolderSelectionKind kind, bool custom, bool original) in new[]
                { (FolderSelectionKind.All, true, true), (FolderSelectionKind.Custom, true, false),
                    (FolderSelectionKind.Original, false, true), (FolderSelectionKind.Folder, true, false) })
            {
                FolderSelection selection = new() { Kind = kind, Folder = galaxyFolder };
                if (selection.Matches(customId, library) != custom || selection.Matches(1, library) != original)
                    throw new InvalidOperationException("Folder selection scope failed: " + kind);
            }
            FolderSelection collision = new() { Kind = FolderSelectionKind.Folder,
                Folder = new FolderInfo { Name = "ALL", SongIds = { customId } } };
            if (!collision.Matches(customId, library) || collision.Matches(1, library))
                throw new InvalidOperationException("A collection named ALL must not become the special ALL filter.");
            string dadaFolder = Path.Combine(galaxy, "Dadadadadadadadadada");
            if (!SongSearch.Matches("dada", "打打打打打打打打打打", "Artist", dadaFolder)
                || !SongSearch.Matches("  TEST  ", "Test song", "Artist", arcadia)
                || !SongSearch.Matches("artist", "Title", "Original Artist", arcadia)
                || !SongSearch.Matches("arcadia", "Title", "Artist", arcadia + Path.DirectorySeparatorChar)
                || SongSearch.Matches("galaxy", "Title", "Artist", arcadia)
                || SongSearch.Matches("missing", "Title", "Artist", null)
                || !SongSearch.Matches("  ", null, null, null)
                || !SongSearch.Matches("", null, null, null))
                throw new InvalidOperationException("Title, artist and song-directory search self-test failed.");
            if (!new FolderSelection { Kind = FolderSelectionKind.Custom }.Matches(customId, library)
                || !SongSearch.Matches("arcadia", "Test song", "Original artist", arcadia)
                || SongSearch.Matches("arcadia", "Test song", "Original artist", crimson))
                throw new InvalidOperationException("Search must combine with the selected folder.");
            Directory.CreateDirectory(dadaFolder);
            File.WriteAllText(Path.Combine(dadaFolder, "set.def"), "#TITLE: 打打打打打打打打打打\n#L1FILE: chart.dtx\n");
            File.WriteAllText(Path.Combine(dadaFolder, "chart.dtx"), "#TITLE 打打打打打打打打打打\n#ARTIST Artist\n#DLEVEL 50\n#BPM 120\n");
            library.Scan(root, settings, ids);
            CustomSong dada = library.Songs.Single(song => song.Folder == dadaFolder);
            if (dada.Title != "打打打打打打打打打打" || !SongSearch.Matches("dada", dada.Title, dada.Artist, dada.Folder))
                throw new InvalidOperationException("Set title must not hide a matching song-directory name.");
            if (FolderGrid.Columns != 3 || FolderGrid.Height != FolderGrid.Pitch * 4 + FolderGrid.TileSize / 2
                || FolderGrid.MaxScroll(12) != 0f || FolderGrid.MaxScroll(15) != 50f
                || FolderGrid.Hit(1, FolderGrid.Height - 1, 0, 15) != 12
                || FolderGrid.Hit(101, 1, 0, 15) != -1 || FolderGrid.Hit(1, FolderGrid.Height, 0, 15) != -1)
                throw new InvalidOperationException("Three-column, 4.5-row viewport/clipping self-test failed.");
            if (SongSearchKeyboardLayout.Scale != 2f * 0.8f
                || SongSearchKeyboardLayout.HeadOffsetY != -0.30f
                || SongSearchKeyboardLayout.HeadOffsetZ != 0.48f
                || SongSearchKeyboardLayout.Scale * SongSearchKeyboardLayout.Width < 1.6f * FolderGrid.Width
                || SongSearchKeyboardLayout.Scale * SongSearchKeyboardLayout.Height < 1.6f * FolderGrid.Height
                || SongSearchKeyboardLayout.Width <= SongSearchKeyboardLayout.Height
                || SongSearchKeyboardLayout.Scale * SongSearchKeyboardLayout.KeyWidth < 1.6f * 29f
                || SongSearchKeyboardLayout.Scale * SongSearchKeyboardLayout.KeyHeight < 1.6f * 38f
                || 9f * SongSearchKeyboardLayout.KeyPitch + SongSearchKeyboardLayout.KeyWidth > SongSearchKeyboardLayout.Width
                || SongSearchKeyboardLayout.FirstRowY + SongSearchKeyboardLayout.KeyHeight * 0.5f > SongSearchKeyboardLayout.Height * 0.5f
                || -210f - SongSearchKeyboardLayout.KeyHeight * 0.5f < -SongSearchKeyboardLayout.Height * 0.5f
                || SongSearchKeyboardLayout.LauncherSize <= 84f)
                throw new InvalidOperationException("Search keyboard size, position, landscape layout, or key bounds failed.");
            FolderTouch touch = new();
            if (!touch.Start(10f, 10f, 0f, 15) || touch.Move(10f, 11f, 15) != 0f || touch.Dragged || touch.Release() != 0)
                throw new InvalidOperationException("Folder tap must select its starting tile.");
            if (!touch.Start(10f, 120f, 0f, 15) || touch.Move(10f, 20f, 15) != 50f || touch.Release() != -1)
                throw new InvalidOperationException("Folder drag must scroll to its limit without selecting.");
            if (!touch.Start(10f, 120f, 50f, 15) || touch.Move(10f, 220f, 15) != 0f || touch.Release() != -1)
                throw new InvalidOperationException("Folder downward drag must restore the first row.");
            if (!touch.Start(10f, 120f, 0f, 15) || touch.Move(10f + FolderGrid.DragThreshold, 120f, 15) != 0f
                || !touch.Dragged || touch.Release() != -1
                || FolderGrid.Hit(230f, 470f, 0f, 13) != -1
                || FolderGrid.Hit(1f, 481f, FolderGrid.MaxScroll(18), 18) != 15)
                throw new InvalidOperationException("Folder drag threshold, unused cell, or final scrolled row hit failed.");
            if (!touch.Start(10f, 120f, 0f, 15) || touch.Dragged
                || touch.Move(10f + FolderGrid.DragThreshold - 1f, 120f, 15) != 0f || touch.Dragged)
                throw new InvalidOperationException("Small hand movement must not cancel the native dwell timer.");
            File.Delete(settingsPath);
            if (!Settings.LoadOrCreate(settingsPath).DisplayFolderPicker
                || !File.ReadAllText(settingsPath).Contains("DisplayFolderPicker=1"))
                throw new InvalidOperationException("Folder panel default creation failed.");
            foreach (string value in new[] { "0", "1", "false", "true" })
            {
                string ini = "[CustomSongs]\nDisplayFolderPicker=" + value + "\nDisplayFolderName=0\n";
                File.WriteAllText(settingsPath, ini);
                if (Settings.LoadOrCreate(settingsPath).DisplayFolderPicker != (value is "1" or "true")
                    || File.ReadAllText(settingsPath) != ini)
                    throw new InvalidOperationException("Folder panel settings must preserve existing INI values.");
            }
            string legacyIni = "[CustomSongs]\nDisplayFolderPanel=0\n";
            File.WriteAllText(settingsPath, legacyIni);
            if (!Settings.LoadOrCreate(settingsPath).DisplayFolderPicker
                || File.ReadAllText(settingsPath) != legacyIni)
                throw new InvalidOperationException("Legacy DisplayFolderPanel must remain ignored without migration.");
        }
        finally
        {
            if (Directory.Exists(fixture)) Directory.Delete(fixture, recursive: true);
        }
    }

    private static void RunModSettingsIniSaveSelfTest()
    {
        string path = Path.Combine(Path.GetTempPath(), "beatarenhax-mod-menu-selftest-" + Guid.NewGuid().ToString("N") + ".ini");
        const string original = "; preserve this comment\n[CustomSongs]\n; keep the path setting\nPath=D:\\Songs\nLoadVideos=1\n\n[Game]\nPlayerName=Tester\n[Other]\nKeep=unchanged\n";
        try
        {
            File.WriteAllText(path, original);
            Settings.SaveModMenuValues(path,
            [
                ("CustomSongs", "LoadVideos", "0"),
                ("CustomSongs", "CenterVideoDim", "0.25"),
                ("Game", "ForceQuitOnSignal", "0"),
            ]);

            string updated = File.ReadAllText(path);
            Settings parsed = Settings.LoadOrCreate(path);
            if (!updated.Contains("; preserve this comment") || !updated.Contains("; keep the path setting")
                || !updated.Contains("Path=D:\\Songs") || !updated.Contains("[Other]\nKeep=unchanged")
                || parsed.LoadVideos || Math.Abs(parsed.CenterVideoDim - 0.25f) > 0.0001f
                || parsed.ForceQuitOnSignal || parsed.PlayerName != "Tester")
                throw new InvalidOperationException("Mod settings ini save self-test failed");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
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
            if (settings.MinNoteGapMs != 0 || settings.BeginnerMinNoteGapMs != 0 || settings.BeginnerOnlyEightNotes)
                throw new InvalidOperationException("Note filtering defaults self-test failed");
        }
        File.WriteAllText(settingsPath, "[Charts]\nMinNoteGapMs=99\n");
        Settings missingSettings = Settings.LoadOrCreate(settingsPath);
        if (missingSettings.MinNoteGapMs != 0 || missingSettings.BeginnerMinNoteGapMs != 0 || missingSettings.BeginnerOnlyEightNotes)
            throw new InvalidOperationException("Settings in unrelated sections should be ignored");

        File.WriteAllText(settingsPath, "[SongSelect]\nSongWheelDistanceScale=1.75\n");
        if (Settings.LoadOrCreate(settingsPath).SongWheelDistanceScale != 1.75f
            || defaults.SongWheelDistanceScale != 1.2f)
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
                + "MinNoteGapMs=45\nBeginnerOnlyEightNotes=true\nBeginnerMinNoteGapMs=234\n"
                + "[Charts]\nDifficultiesCut=invalid\nMinNoteGapMs=99\n"
                + "[Audio]\nMasterVolume=2\nMinNoteGapMs=88\n"
                + "[Game]\nPlayerName=SectionTest\n");
            Settings loaded = Settings.LoadOrCreate(settingsPath);
            if (loaded.DifficultiesCut != cut
                || loaded.MinNoteGapMs != 45 || loaded.BeginnerMinNoteGapMs != 234 || !loaded.BeginnerOnlyEightNotes
                || loaded.MasterVolume != 2 || loaded.PlayerName != "SectionTest")
                throw new InvalidOperationException("Difficulty INI parsing self-test failed: " + cut);
        }
        File.WriteAllText(settingsPath, "[CustomSongs]\nCasualOnlyEighthNotes=true\nCasualMinNoteGapMs=321\n");
        Settings legacySettings = Settings.LoadOrCreate(settingsPath);
        if (legacySettings.BeginnerMinNoteGapMs != 321 || !legacySettings.BeginnerOnlyEightNotes)
            throw new InvalidOperationException("Legacy beginner setting migration self-test failed");
        File.WriteAllText(settingsPath, "[CustomSongs]\nBeginnerOnlyEightNotes=false\nBeginnerMinNoteGapMs=123\n"
            + "CasualOnlyEighthNotes=true\nCasualMinNoteGapMs=321\n");
        Settings renamedSettings = Settings.LoadOrCreate(settingsPath);
        if (renamedSettings.BeginnerMinNoteGapMs != 123 || renamedSettings.BeginnerOnlyEightNotes)
            throw new InvalidOperationException("Renamed beginner settings should take precedence over legacy keys");
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
                    settings.BeginnerOnlyEightNotes = Eighths;
                    settings.BeginnerMinNoteGapMs = Gap;
                    ConvertedSong casualFiltered = song.GetConverted(settings, file => 0);
                    if (casualFiltered.Casual.PlayableCount != 1 || casualFiltered.Advanced.PlayableCount != 2
                        || casualFiltered.Casual.AutoCount != converted.Casual.AutoCount + 1
                        || !casualFiltered.Advanced.Midi.SequenceEqual(converted.Advanced.Midi)
                        || !casualFiltered.Advanced.Schedule.SequenceEqual(converted.Advanced.Schedule))
                        throw new InvalidOperationException($"Casual-only filtering self-test failed: cut={Cut}, eighths={Eighths}, gap={Gap}");
                    song.ReleaseConverted();
                }
                settings.BeginnerOnlyEightNotes = false;
                settings.BeginnerMinNoteGapMs = 1;
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
            File.WriteAllText(Path.Combine(folder, "chart5.dtx"),
                "#TITLE Difficulty test\n#DLEVEL 5\n#BPM 120\n"
                + "#WAV01 hit.wav\n#00012:01010000000000000000000000000000\n");
            File.WriteAllText(Path.Combine(folder, "set.def"), "#TITLE Difficulty test\n"
                + string.Join("\n", Enumerable.Range(1, 5).Reverse()
                    .Select(index => $"#L{index}LABEL: Label {index}\n#L{index}FILE: chart{index}.dtx")));
            foreach ((ChartDifficultyCut cut, int[] expected) in new[]
            {
                (ChartDifficultyCut.Easiest, new[] { 1, 2, 3, 4 }),
                (ChartDifficultyCut.Hardest, new[] { 2, 3, 4, 5 }),
            })
            {
                SongLibrary fiveChartLibrary = new();
                fiveChartLibrary.Scan(folder, new Settings { DifficultiesCut = cut }, Path.Combine(folder, "ids.txt"));
                CustomSong set = fiveChartLibrary.Songs.Single();
                var selected = CustomChartSelection.Select(set.Charts, cut);
                if (!selected.Select(chart => chart.SetIndex).SequenceEqual(expected)
                    || selected.Any(chart => chart.Label != "Label " + chart.SetIndex))
                    throw new InvalidOperationException("Five-chart set.def selection failed: " + cut);
                Settings conversionSettings = new() { DifficultiesCut = cut, BeginnerMinNoteGapMs = 234 };
                for (int i = 0; i < selected.Count; i++)
                {
                    ConvertedCustomChart converted = set.GetConvertedChart(conversionSettings, file => 0, i);
                    if (converted.Definition.SetIndex != expected[i]
                        || converted.Chart.Source.FilePath != selected[i].Path
                        || converted.Chart.PlayableCount != (i == 0 ? 1 : 2)
                        || converted.Chart.Midi == null
                        || !converted.ParamJson.Contains(ChartConverter.ChipLabelPrefix + "1")
                        || converted.WavToNote.Count != 1
                        || !ReferenceEquals(converted, set.GetConvertedChart(conversionSettings, file => 0, i)))
                        throw new InvalidOperationException($"Four-chart conversion failed: {cut}, index {i}");
                }
                try
                {
                    _ = set.GetConvertedChart(conversionSettings, file => 0, selected.Count);
                    throw new InvalidOperationException("Unavailable chart was converted.");
                }
                catch (ArgumentOutOfRangeException) { }
                if (cut == ChartDifficultyCut.Easiest)
                {
                    ConvertedCustomChart advanced = set.GetConvertedChart(conversionSettings, file => 0, 1);
                    conversionSettings.DifficultiesCut = ChartDifficultyCut.Hardest;
                    ConvertedCustomChart casual = set.GetConvertedChart(conversionSettings, file => 0, 0);
                    if (advanced.Definition.SetIndex != 2 || casual.Definition.SetIndex != 2
                        || advanced.Chart.PlayableCount != 2 || casual.Chart.PlayableCount != 1
                        || casual.Chart.AutoCount != advanced.Chart.AutoCount + 1
                        || ReferenceEquals(advanced, casual))
                        throw new InvalidOperationException("First-chart filtering must follow the selected cut.");
                    conversionSettings.DifficultiesCut = cut;
                }
                set.ReleaseConverted();
                if (set.GetConvertedChart(conversionSettings, file => 0, 0).Chart.PlayableCount != 1)
                    throw new InvalidOperationException("Four-chart conversion after release failed.");
            }
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
