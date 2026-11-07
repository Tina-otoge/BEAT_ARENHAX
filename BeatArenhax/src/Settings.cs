using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace BeatArenhax
{
    public enum ChartDifficultyCut
    {
        Easiest,
        Hardest,
    }

    public enum SpectatorCamera
    {
        VR,
        ThirdPerson,
    }

    public sealed class Settings
    {
        public string SongPath = "DTXFiles";
        public bool LoadVideos = true;
        public float CenterVideoDim = 0.75f;
        public bool DecimalNotation = true;
        public bool DisplayFolderName = true;
        public bool DisplayFolderPicker = true;
        // Vanilla song template to use as a base, affects stage environment (penlights,
        // colors, animations, band members, and other stage elements)
        // Empty = random
        public string TemplateSong = "";
        public int FirstSongId = 5000;
        public ChartDifficultyCut DifficultiesCut = ChartDifficultyCut.Easiest;
        public int MinNoteGapMs = 0;
        public int BeginnerMinNoteGapMs = 0;
        public bool BeginnerOnlyEightNotes = false;
        public bool RideOnRightCymbal = true;
        public int MinimumTimeBeforeFirstNoteMs = 2500;
        public PedalBinding LeftPedal = PedalBinding.Parse("", "", "");
        public PedalBinding RightPedal = PedalBinding.Parse("", "", "");
        public float MasterVolume = 1.0f;
        public float BgmVolume = 1.0f;
        public float DrumVolume = 1.0f;
        public int AudioLatencyMs = 40;
        public int AudioOffsetMs = 0;
        public bool ForceQuitOnSignal = true;
        public bool QuitButton = true;
        public float GameplayPanelOpacity = 1.0f;
        public float StoolHeight = -0.1f;
        public float StoolOffsetX = 0f;
        public float StoolOffsetZ = -0.3f;
        public bool AutoOfflineMode = true;
        public bool SkipTitleScreen = true;
        public bool SkipCautionScreen = false;
        public bool SkipPostGameGesture = false;
        public bool SkipPreGameGesture = false;
        public bool GrabWithoutWait = true;
        public bool GrabWithoutVibration = true;
        public bool MoveResultMenuToTheSide = true;
        public bool ShowDifficultyOnResultScreen = true;
        public bool DisplayFastSlow = true;
        public bool DisplayFastSlowOnPerfects = false;
        public string VirtualScreen = "";
        public float VirtualScreenScale = 1f;
        public float VirtualScreenOffsetY = 0f;
        public float VirtualScreenDistanceScale = 1f;
        public float VirtualScreenBackgroundOpacity = 0.9f;
        public bool VirtualScreenShowAuto = true;
        public bool VirtualScreenGrayAuto = true;
        public bool UnlockAllAvatarParts = true;
        public bool UnlockCampaignRewards = false;
        public bool HideButtonsUnavailableOffline = true;
        public string PlayerName = "";
        public SpectatorCamera SpectatorCamera = SpectatorCamera.ThirdPerson;
        public bool SpectatorThirdPersonKeyboardMouseControl = true;
        public bool SpectatorCinematicGameplay = false;
        public CinematicShot[] SpectatorCinematicCameraTypes = CinematicCameraDirector.ParseTypes("");
        public bool SpectatorSongInfo = true;
        public bool SpectatorCustomTitle = true;
        public bool SpectatorShowVirtualScreen = false;
        public int SpectatorWidth = 1024;
        public int SpectatorHeight = 768;
        public bool ShowStats = true;
        public int SongWheelColumns = 5;
        public float SongWheelRadiusScale = 1.0f;
        public float SongWheelDistanceScale = 1.2f;
        public bool SongWheelForwardOnly = true;
        public float SongWheelForwardArc = 120f;
        public bool SongWheelRememberPosition = true;
        public bool SongWheelPlayPreviewByTouching = true;
        public int SongWheelTouchSelectTimeMs = 600;
        public bool LazySongWheel = true;
        public bool BackgroundJacketDecoding = true;
        public bool PlayerInfoOnlyPlayedCustomSongs = true;
        public bool PlayerInfoHideCustomSongs = false;

        internal static readonly string DefaultIni = string.Join(Environment.NewLine,
        [
            "; Generated using version " + BuildInfo.Version + ", if you are running a different version, read the changelog to review any change in the config file format",
            "",
            "[CustomSongs]",
            "; Custom songs folder, recursively scanned for DTX charts",
            "Path=DTXFiles",
            "; Replaces stage animations with DTX videos",
            "LoadVideos=1",
            "; Makes the center video less bright",
            "CenterVideoDim=0.75",
            "; Replaces level display for custom chart to support decimal notation",
            "DecimalNotation=1",
            "; Displays the DTX folder name from which each chart is from at multiple locations in the UI",
            "DisplayFolderName=1",
            "; Hold a collection tile for 300 ms to filter using the vanilla picker; swipe vertically to scroll.",
            "DisplayFolderPicker=1",
            "; Vanilla song template to use as a base, affects stage environment (penlights, colors,",
            "; animations, band members, and other stage elements)",
            "; Empty = random",
            "; DTX videos can replace the stage animations.",
            "TemplateSong=",
            "; First music id used for custom songs, to avoid collisions with originals.",
            "FirstSongId=5000",
            "",
            "; We support up to 4 difficulties for now, use this to select between the bottom",
            "; 4 or the top 4",
            "DifficultiesCut=easiest",
            "; Values = easiest, hardest",
            "; Minimum gap between notes on the same pad",
            "MinNoteGapMs=0",
            "; Same but only for Beginner charts",
            "BeginnerMinNoteGapMs=0",
            "; Keep only eighth-note grid hits in every Beginner chart; other hits play automatically.",
            "BeginnerOnlyEightNotes=false",
            "; Ride cymbal goes to the right cymbal pad (false = played automatically)",
            "RideOnRightCymbal=true",
            "",
            "MinimumTimeBeforeFirstNoteMs=2500",
            "",
            "[Pedals]",
            "; Add pedal support for custom songs; empty bindings play notes automatically.",
            "; Valid keys: a, b, c, ..., z, 0, 1, ..., 9, space, enter, arrows, F1-F12, modifiers.",
            "LeftPedalKey=",
            "; Valid buttons: 1...20 or A, B, X, Y, LB, RB, Back, Start, LS, RS.",
            "LeftPedalButton=",
            "; Valid MIDI notes: 0-127 on any channel/device; positive-velocity Note On.",
            "LeftPedalMIDI=",
            "RightPedalKey=",
            "RightPedalButton=",
            "RightPedalMIDI=",
            "",
            "[Audio]",
            "MasterVolume=1",
            "BgmVolume=1",
            "DrumVolume=1",
            "; WASAPI buffer. Lower = more responsive hit sounds, too low = crackles.",
            "AudioLatencyMs=40",
            "; Positive values make the notes arrive later relative to the music.",
            "AudioOffsetMs=0",
            "",
            "[Game]",
            "; Changes the opacity of the background panel during gameplay, the one under the",
            "; judges and combo count. Turn it to 0 for better view of background videos.",
            "GameplayPanelOpacity=1",
            "; In case the drum stool is not properly aligned with your butt",
            "; Negative value = lower position",
            "StoolHeight=-0.1",
            "; Positive X = left, negative = right; negative Z = behind you, positive = in front.",
            "StoolOffsetX=0",
            "StoolOffsetZ=-0.30",
            "; Force close the game when a close signal is received",
            "ForceQuitOnSignal=1",
            "; Adds a quit button in the main menu",
            "QuitButton=1",
            "; Skips the popup that asks you to choose between rebooting or going offline at",
            "; startup since the servers are down.",
            "AutoOfflineMode=1",
            "; Skips the KONAMI / BEAT ARENA logo intro at startup",
            "SkipTitleScreen=1",
            "; Skip the caution screen (controller / sitting-down warnings)",
            "; NOT RECOMMENDED: This drops you into the lobby before objects finish initializing; they flash as they load. Jarring in VR.",
            "SkipCautionScreen=0",
            "; Skip the 'make a gesture to each member and start playing' wait before a song.",
            "SkipPreGameGesture=0",
            "; Skip the 'send your impression to the members' countdown after a song and go straight to the score.",
            "SkipPostGameGesture=0",
            "; Makes you able to grab the song panel immediately, without having to wait for",
            "; the usual hover activation animation",
            "GrabWithoutWait=1",
            "; Disable vibration on this very same panel",
            "GrabWithoutVibration=1",
            "; The quit/retry/save menu that appears when a song ends blocks the view of your",
            "; score result, this patch moves it to the side",
            "MoveResultMenuToTheSide=1",
            "; Add the difficulty name and level to the result screen",
            "ShowDifficultyOnResultScreen=1",
            "; Add a timing indicator (FAST / SLOW) above the drums",
            "DisplayFastSlow=1",
            "; Also show the timing indicator on Perfect hits",
            "DisplayFastSlowOnPerfects=0",
            "; Used to be provided by the server",
            "UnlockAllAvatarParts=1",
            "; These ones you can get just by playing the game normally",
            "UnlockCampaignRewards=0",
            "; Hide all buttons that only work online (\"Members search\", \"Following\", ...)",
            "HideButtonsUnavailableOffline=1",
            "; Name shown in game (score boards, replays). Leave empty to use the game's own name.",
            "PlayerName=",
            "",
            "",
            "[VirtualScreen]",
            "; When enabled, replaces the 3D note with a flat screen, simulating traditional",
            "; DTXMania gameplay. Needs a DTXMania skin to work.",
            "Path=",
            "; Scale the screen",
            "Scale=1",
            "; Positive = moves the screen up, negative = moves it down",
            "OffsetY=0",
            "; Bigger number = further away from the player",
            "DistanceScale=1",
            "; Lanes background opacity",
            "BackgroundOpacity=0.9",
            "; Displays automatic notes (pedals if you didn't bind them)",
            "ShowAuto=1",
            "; Displays automatic notes in black and white",
            "GrayAuto=1",
            "",
            "[Spectator]",
            "; You can customize the desktop window here. These settings do not affect the",
            "; VR headset view.",
            "; Places the camera behind the player",
            "ThirdPerson=1",
            "; Adds keyboard/mouse controls for the ThirdPerson camera (mouse drag and WASD/ZQSD/arrows).",
            "ThirdPersonKeyboardMouseControl=1",
            "; Randomly moves around the stage during gameplay, using presets",
            "CinematicGameplay=0",
            "; Allowed values = Standard, ArcUp, ArcDown, High, Low, BackLeft, BackRight,",
            "; PlayerFirstPerson, NpcFirstPerson.",
            "CinematicCameraTypes=",
            "; Show the virtual screen (see section above) in the spectator window.",
            "; Otherwise, spectators will see the 3D notes, even if the player is using the DTXMania virtual screen.",
            "ShowVirtualScreen=0",
            "; Change the desktop spectator window size (default 1024 x 768 pixels).",
            "Width=1024",
            "Height=768",
            "; Adds an overlay with the current song and current judges",
            "SongInfo=1",
            "; Set the desktop window title to BEAT ARENA, or include the current song while it is playing.",
            "CustomTitle=1",
            "",
            "[SongSelect]",
            "; Show song levels and grades on jackets",
            "ShowStats=1",
            "; Lay each row on an arc in front of you instead of all around you (rows no",
            "; longer spin left/right).",
            "SongWheelForwardOnly=1",
            "; Width of the arc when using Forward mode. 360 = full circle = vanilla.",
            "SongWheelForwardArc=120",
            "; Reopen the song wheel where you left it instead of at the first row",
            "SongWheelRememberPosition=1",
            "; Songs per row, when in forward mode, a lower number is better.",
            "; Vanilla = 9.",
            "; When using forward mode, 5 is recommended.",
            "SongWheelColumns=5",
            "; Expands the circle on which the songs are arranged",
            "; Bigger value = song wheel spawns further away from the player",
            "SongWheelRadiusScale=1",
            "; Bigger value = song wheel spawns further away from the player",
            "SongWheelDistanceScale=1.2",
            "; Preview songs by touching their jacket, instead of having to select them.",
            "SongWheelPlayPreviewByTouching=1",
            "; How long a song's plate must be touched before the song is selected.",
            "; Longer values makes it easier to trigger PreviewByTouching without",
            "; accidentally selecting the song.",
            "SongWheelTouchSelectTimeMs=600",
            "; SongWheelTouchSelectTimeMs=300",
            "",
            "[Performance]",
            "; Spawn song wheel items as inactive, mark them active only when shown.",
            "; On a 1200+ library, sped up loading from 32s to 1s.",
            "LazySongWheel=1",
            "; Decode PNG jackets on background threads at startup",
            "; On a 1200+ library, sped up loading from 7s to 1s.",
            "BackgroundJacketDecoding=1",
            "; In the Player Info menu, you can see a list of all songs in the game and your",
            "; score on it. This option hides custom songs you haven't played yet from this list.",
            "; The load time for 1200 entries is 20s",
            "PlayerInfoOnlyPlayedCustomSongs=1",
            "; Hide all custom songs from the Player Info menu.",
            "PlayerInfoHideCustomSongs=0",
            "",
        ]) + Environment.NewLine;

        internal static void SaveModMenuValues(string path, IReadOnlyList<(string Section, string Key, string Value)> values)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("An ini path is required.", nameof(path));
            if (values == null) throw new ArgumentNullException(nameof(values));
            if (!File.Exists(path)) throw new FileNotFoundException("The settings ini was not found.", path);

            string original = File.ReadAllText(path);
            string newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n"
                : original.Contains('\r') ? "\r" : "\n";
            bool hasFinalNewline = original.EndsWith("\n", StringComparison.Ordinal)
                || original.EndsWith("\r", StringComparison.Ordinal);
            List<string> lines = [];
            using (StringReader reader = new(original))
            {
                string line;
                while ((line = reader.ReadLine()) != null) lines.Add(line);
            }

            foreach ((string section, string key, string value) in values)
            {
                if (string.IsNullOrWhiteSpace(section) || string.IsNullOrWhiteSpace(key)
                    || value == null || value.Contains('\r') || value.Contains('\n'))
                    throw new ArgumentException("Invalid mod setting entry.");

                int sectionStart = FindSection(lines, section);
                if (sectionStart < 0)
                {
                    if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1])) lines.Add("");
                    lines.Add($"[{section}]");
                    sectionStart = lines.Count - 1;
                }

                int sectionEnd = sectionStart + 1;
                while (sectionEnd < lines.Count && !IsSectionHeader(lines[sectionEnd])) sectionEnd++;
                int found = 0;
                for (int i = sectionStart + 1; i < sectionEnd; i++)
                {
                    string line = lines[i];
                    int equals = line.IndexOf('=');
                    if (equals <= 0 || !line[..equals].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) continue;
                    lines[i] = ReplaceIniValue(line, equals, value);
                    found++;
                }

                if (found == 0)
                {
                    int insertAt = sectionEnd;
                    while (insertAt > sectionStart + 1 && string.IsNullOrWhiteSpace(lines[insertAt - 1])) insertAt--;
                    lines.Insert(insertAt, $"{key}={value}");
                }
            }

            string updated = string.Join(newline, lines);
            if (hasFinalNewline) updated += newline;
            string fullPath = Path.GetFullPath(path);
            string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(temporaryPath, updated, new UTF8Encoding(false));
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }

            static int FindSection(List<string> source, string section)
            {
                string header = $"[{section}]";
                for (int i = 0; i < source.Count; i++)
                    if (source[i].Trim().Equals(header, StringComparison.OrdinalIgnoreCase)) return i;
                return -1;
            }

            static bool IsSectionHeader(string line)
            {
                string trimmed = line.Trim();
                return trimmed.Length >= 3 && trimmed[0] == '[' && trimmed[^1] == ']';
            }

            static string ReplaceIniValue(string line, int equals, string value)
            {
                string tail = line[(equals + 1)..];
                int commentIndex = tail.IndexOfAny(new[] { ';', '#' });
                string valueAndSpacing = commentIndex >= 0 ? tail[..commentIndex] : tail;
                int leading = valueAndSpacing.Length - valueAndSpacing.TrimStart().Length;
                int trailing = valueAndSpacing.Length;
                while (trailing > leading && char.IsWhiteSpace(valueAndSpacing[trailing - 1])) trailing--;
                string suffix = valueAndSpacing[trailing..] + (commentIndex >= 0 ? tail[commentIndex..] : "");
                return line[..(equals + 1)] + valueAndSpacing[..leading] + value + suffix;
            }
        }

        public static Settings LoadOrCreate(string path)
        {
            Settings s = new();
            if (!File.Exists(path))
            {
                File.WriteAllText(path, DefaultIni, new UTF8Encoding(false));
                return s;
            }
            IConfiguration configuration = new ConfigurationBuilder()
                .AddIniFile(path, optional: false, reloadOnChange: false)
                .Build();
            string Get(string section, string key, string defaultValue)
            {
                string value = configuration[$"{section}:{key}"];
                return !string.IsNullOrEmpty(value) ? value : defaultValue;
            }

            int GetI(string section, string key, int defaultValue, string legacyKey = "")
            {
                string fallback = legacyKey.Length == 0 ? "" : Get(section, legacyKey, "");
                return int.TryParse(Get(section, key, fallback), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : defaultValue;
            }

            float GetF(string section, string key, float defaultValue)
            {
                return float.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    ? value
                    : defaultValue;
            }

            bool GetB(string section, string key, bool defaultValue, string legacyKey = "")
            {
                string fallback = legacyKey.Length == 0 ? "" : Get(section, legacyKey, "");
                string value = Get(section, key, fallback).ToLowerInvariant();
                return value is "1" or "true" or "yes" or "on"
                    || (value is not "0" and not "false" and not "no" and not "off" && defaultValue);
            }
            ChartDifficultyCut GetDifficultyCut(string section, string key, ChartDifficultyCut defaultValue)
            {
                return Get(section, key, defaultValue.ToString()).ToUpperInvariant() switch
                {
                    "EASIEST" => ChartDifficultyCut.Easiest,
                    "HARDEST" => ChartDifficultyCut.Hardest,
                    _ => throw new FormatException($"Invalid {key} in [{section}] in '{path}': expected easiest or hardest."),
                };
            }
            s.SongPath = Get("CustomSongs", "Path", s.SongPath).Trim('"');
            s.LoadVideos = GetB("CustomSongs", "LoadVideos", s.LoadVideos);
            float GetUnitFloat(string section, string key, float fallback)
            {
                string text = configuration[$"{section}:{key}"];
                if (text == null) return fallback;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    throw new FormatException($"Invalid {key} in [{section}] in '{path}': expected a finite number from 0 to 1.");
                return Math.Clamp(value, 0f, 1f);
            }
            s.CenterVideoDim = GetUnitFloat("CustomSongs", "CenterVideoDim", s.CenterVideoDim);
            s.GameplayPanelOpacity = GetUnitFloat("Game", "GameplayPanelOpacity", s.GameplayPanelOpacity);
            s.DecimalNotation = GetB("CustomSongs", "DecimalNotation", s.DecimalNotation);
            s.DisplayFolderName = GetB("CustomSongs", "DisplayFolderName", s.DisplayFolderName);
            s.DisplayFolderPicker = GetB("CustomSongs", "DisplayFolderPicker", s.DisplayFolderPicker);
            s.TemplateSong = (configuration["CustomSongs:TemplateSong"] ?? s.TemplateSong).Trim();
            s.FirstSongId = Math.Clamp(GetI("CustomSongs", "FirstSongId", s.FirstSongId), 100, 7000);
            s.DifficultiesCut = GetDifficultyCut("CustomSongs", "DifficultiesCut", s.DifficultiesCut);
            s.MinNoteGapMs = Math.Clamp(GetI("CustomSongs", "MinNoteGapMs", s.MinNoteGapMs), 0, 1000);
            s.BeginnerMinNoteGapMs = Math.Clamp(GetI("CustomSongs", "BeginnerMinNoteGapMs",
                s.BeginnerMinNoteGapMs, "CasualMinNoteGapMs"), 0, 2000);
            s.BeginnerOnlyEightNotes = GetB("CustomSongs", "BeginnerOnlyEightNotes",
                s.BeginnerOnlyEightNotes, "CasualOnlyEighthNotes");
            s.RideOnRightCymbal = GetB("CustomSongs", "RideOnRightCymbal", s.RideOnRightCymbal);
            s.MinimumTimeBeforeFirstNoteMs = Math.Clamp(GetI("CustomSongs", "MinimumTimeBeforeFirstNoteMs", s.MinimumTimeBeforeFirstNoteMs), 0, 10000);
            try
            {
                s.LeftPedal = PedalBinding.Parse(Get("Pedals", "LeftPedalKey", ""),
                    Get("Pedals", "LeftPedalButton", ""), Get("Pedals", "LeftPedalMIDI", ""));
                s.RightPedal = PedalBinding.Parse(Get("Pedals", "RightPedalKey", ""),
                    Get("Pedals", "RightPedalButton", ""), Get("Pedals", "RightPedalMIDI", ""));
            }
            catch (FormatException e)
            {
                throw new FormatException($"Invalid [Pedals] binding in '{path}': {e.Message}", e);
            }
            s.MasterVolume = Math.Clamp(GetF("Audio", "MasterVolume", s.MasterVolume), 0f, 4f);
            s.BgmVolume = Math.Clamp(GetF("Audio", "BgmVolume", s.BgmVolume), 0f, 4f);
            s.DrumVolume = Math.Clamp(GetF("Audio", "DrumVolume", s.DrumVolume), 0f, 4f);
            s.AudioLatencyMs = Math.Clamp(GetI("Audio", "AudioLatencyMs", s.AudioLatencyMs), 10, 300);
            s.AudioOffsetMs = Math.Clamp(GetI("Audio", "AudioOffsetMs", s.AudioOffsetMs), -500, 500);
            s.ForceQuitOnSignal = GetB("Game", "ForceQuitOnSignal", s.ForceQuitOnSignal);
            s.QuitButton = GetB("Game", "QuitButton", s.QuitButton);
            float GetStoolOffset(string key, float fallback)
            {
                string text = configuration[$"Game:{key}"];
                if (text == null) return fallback;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    throw new FormatException($"Invalid {key} in [Game] in '{path}': expected a finite number from -0.5 to 0.5 metres.");
                return Math.Clamp(value, -0.5f, 0.5f);
            }
            s.StoolHeight = GetStoolOffset("StoolHeight", s.StoolHeight);
            s.StoolOffsetX = GetStoolOffset("StoolOffsetX", s.StoolOffsetX);
            s.StoolOffsetZ = GetStoolOffset("StoolOffsetZ", s.StoolOffsetZ);
            s.AutoOfflineMode = GetB("Game", "AutoOfflineMode", s.AutoOfflineMode);
            s.SkipTitleScreen = GetB("Game", "SkipTitleScreen", s.SkipTitleScreen);
            s.SkipCautionScreen = GetB("Game", "SkipCautionScreen", s.SkipCautionScreen);
            s.SkipPostGameGesture = GetB("Game", "SkipPostGameGesture", s.SkipPostGameGesture);
            s.SkipPreGameGesture = GetB("Game", "SkipPreGameGesture", s.SkipPreGameGesture);
            s.GrabWithoutWait = GetB("Game", "GrabWithoutWait", s.GrabWithoutWait);
            s.GrabWithoutVibration = GetB("Game", "GrabWithoutVibration", s.GrabWithoutVibration);
            s.MoveResultMenuToTheSide = GetB("Game", "MoveResultMenuToTheSide", s.MoveResultMenuToTheSide);
            s.ShowDifficultyOnResultScreen = GetB("Game", "ShowDifficultyOnResultScreen", s.ShowDifficultyOnResultScreen);
            s.DisplayFastSlow = GetB("Game", "DisplayFastSlow", s.DisplayFastSlow);
            s.DisplayFastSlowOnPerfects = GetB("Game", "DisplayFastSlowOnPerfects", s.DisplayFastSlowOnPerfects);
            s.VirtualScreen = Get("VirtualScreen", "Path", s.VirtualScreen).Trim().Trim('"');
            float GetScreenFloat(string key, float fallback, float minimum, float maximum)
            {
                string text = configuration[$"VirtualScreen:{key}"];
                if (text == null) return fallback;
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value))
                    throw new FormatException($"Invalid {key} in [VirtualScreen] in '{path}': expected a finite number from {minimum} to {maximum}.");
                return Math.Clamp(value, minimum, maximum);
            }
            s.VirtualScreenScale = GetScreenFloat("Scale", s.VirtualScreenScale, 0.25f, 4f);
            s.VirtualScreenOffsetY = GetScreenFloat("OffsetY", s.VirtualScreenOffsetY, -5f, 5f);
            s.VirtualScreenDistanceScale = GetScreenFloat("DistanceScale", s.VirtualScreenDistanceScale, 0.25f, 4f);
            s.VirtualScreenBackgroundOpacity = GetUnitFloat("VirtualScreen", "BackgroundOpacity", s.VirtualScreenBackgroundOpacity);
            s.VirtualScreenShowAuto = GetB("VirtualScreen", "ShowAuto", s.VirtualScreenShowAuto);
            s.VirtualScreenGrayAuto = GetB("VirtualScreen", "GrayAuto", s.VirtualScreenGrayAuto);
            s.UnlockAllAvatarParts = GetB("Game", "UnlockAllAvatarParts", s.UnlockAllAvatarParts);
            s.UnlockCampaignRewards = GetB("Game", "UnlockCampaignRewards", s.UnlockCampaignRewards);
            s.HideButtonsUnavailableOffline = GetB("Game", "HideButtonsUnavailableOffline", s.HideButtonsUnavailableOffline);
            s.PlayerName = configuration["Game:PlayerName"] ?? s.PlayerName;
            if (configuration["Spectator:ThirdPerson"] != null)
            {
                s.SpectatorCamera = GetB("Spectator", "ThirdPerson",
                    s.SpectatorCamera == SpectatorCamera.ThirdPerson)
                    ? SpectatorCamera.ThirdPerson
                    : SpectatorCamera.VR;
            }
            else if (configuration["Spectator:Camera"] != null)
            {
                s.SpectatorCamera = Get("Spectator", "Camera", s.SpectatorCamera.ToString()).ToUpperInvariant() switch
                {
                    "VR" => SpectatorCamera.VR,
                    "THIRDPERSON" => SpectatorCamera.ThirdPerson,
                    _ => throw new FormatException($"Invalid legacy Camera in [Spectator] in '{path}': expected VR or ThirdPerson."),
                };
            }
            s.SpectatorThirdPersonKeyboardMouseControl = GetB("Spectator", "ThirdPersonKeyboardMouseControl", s.SpectatorThirdPersonKeyboardMouseControl);
            s.SpectatorCinematicGameplay = GetB("Spectator", "CinematicGameplay", s.SpectatorCinematicGameplay);
            s.SpectatorCinematicCameraTypes = CinematicCameraDirector.ParseTypes(configuration["Spectator:CinematicCameraTypes"] ?? "");
            s.SpectatorSongInfo = GetB("Spectator", "SongInfo", s.SpectatorSongInfo);
            s.SpectatorCustomTitle = GetB("Spectator", "CustomTitle", s.SpectatorCustomTitle);
            s.SpectatorShowVirtualScreen = GetB("Spectator", "ShowVirtualScreen", s.SpectatorShowVirtualScreen);
            s.SpectatorWidth = Math.Clamp(GetI("Spectator", "Width", s.SpectatorWidth), 320, 7680);
            s.SpectatorHeight = Math.Clamp(GetI("Spectator", "Height", s.SpectatorHeight), 240, 4320);
            s.ShowStats = GetB("SongSelect", "ShowStats", s.ShowStats);
            s.SongWheelColumns = Math.Clamp(GetI("SongSelect", "SongWheelColumns", s.SongWheelColumns), 4, 60);
            s.SongWheelRadiusScale = Math.Clamp(GetF("SongSelect", "SongWheelRadiusScale", s.SongWheelRadiusScale), 0.5f, 2f);
            s.SongWheelDistanceScale = Math.Clamp(GetF("SongSelect", "SongWheelDistanceScale", s.SongWheelDistanceScale), 0.5f, 2f / s.SongWheelRadiusScale);
            s.SongWheelForwardOnly = GetB("SongSelect", "SongWheelForwardOnly", s.SongWheelForwardOnly);
            s.SongWheelForwardArc = Math.Clamp(GetF("SongSelect", "SongWheelForwardArc", s.SongWheelForwardArc), 30f, 360f);
            s.SongWheelRememberPosition = GetB("SongSelect", "SongWheelRememberPosition", s.SongWheelRememberPosition);
            s.SongWheelPlayPreviewByTouching = GetB("SongSelect", "SongWheelPlayPreviewByTouching", s.SongWheelPlayPreviewByTouching);
            s.SongWheelTouchSelectTimeMs = Math.Clamp(GetI("SongSelect", "SongWheelTouchSelectTimeMs", s.SongWheelTouchSelectTimeMs), 300, 5000);
            s.LazySongWheel = GetB("Performance", "LazySongWheel", s.LazySongWheel);
            s.BackgroundJacketDecoding = GetB("Performance", "BackgroundJacketDecoding", s.BackgroundJacketDecoding);
            s.PlayerInfoOnlyPlayedCustomSongs = GetB("Performance", "PlayerInfoOnlyPlayedCustomSongs", s.PlayerInfoOnlyPlayedCustomSongs);
            s.PlayerInfoHideCustomSongs = GetB("Performance", "PlayerInfoHideCustomSongs", s.PlayerInfoHideCustomSongs);
            return s;
        }

    }
}
