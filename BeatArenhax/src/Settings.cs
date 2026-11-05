using System;
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

    public sealed class Settings
    {
        public string SongPath = "./DTXFiles";
        public string TemplateSong = "fuego";
        public int FirstSongId = 5000;
        public ChartDifficultyCut DifficultiesCut = ChartDifficultyCut.Easiest;
        public int MaxSimultaneousHits = 2;
        public int MinNoteGapMs = 0;
        public int CasualMinNoteGapMs = 0;
        public bool CasualOnlyEighthNotes = false;
        public bool RideOnRightCymbal = true;
        public int MinimumTimeBeforeFirstNoteMs = 2500;
        public float MasterVolume = 1.0f;
        public float BgmVolume = 1.0f;
        public float DrumVolume = 1.0f;
        public int AudioLatencyMs = 40;
        public int AudioOffsetMs = 0;
        public bool ForceQuitOnSignal = true;
        public bool AutoOfflineMode = true;
        public bool SkipTitleScreen = true;
        public bool SkipCautionScreen = false;
        public bool SkipPostGameGesture = false;
        public bool SkipPreGameGesture = false;
        public bool UnlockAllAvatarParts = true;
        public bool UnlockCampaignRewards = false;
        public bool HideButtonsUnavailableOffline = true;
        public string PlayerName = "Player";
        public int SongWheelColumns = 5;
        public float SongWheelRadiusScale = 1.0f;
        public float SongWheelDistanceScale = 1.5f;
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
            "; BEAT ARENHAX " + BuildInfo.Version + ", if you are running a different version, read the changelog to review any change in the config file format",
            "",
            "[CustomSongs]",
            "; Custom songs folder, recursively scanned for DTX charts.",
            "Path=./DTXFiles",
            "; Vanilla song to use as a template, affects stage, band animations and menus.",
            "TemplateSong=fuego",
            "; First music id used for custom songs. IDs are remembered in custom_songs_ids.txt so scores stay attached. 8000+ are tutorial songs, so should be avoided.",
            "FirstSongId=5000",
            "",
            "; Choose the two easiest or hardest available DTX charts, ordered by DLEVEL.",
            "; If fewer than two charts exist, both charts will be the same.",
            "DifficultiesCut=easiest",
            "; DifficultiesCut=hardest",
            "; Max notes that have to be hit at the same time (you only have two sticks).",
            "MaxSimultaneousHits=2",
            "; Notes on the same pad closer than this are played automatically instead.",
            "MinNoteGapMs=0",
            "; Minimum same-pad gap for every Casual chart; the larger of this and MinNoteGapMs is used.",
            "CasualMinNoteGapMs=0",
            "; Keep only eighth-note grid hits in every Casual chart; other hits play automatically.",
            "CasualOnlyEighthNotes=false",
            "; Ride cymbal goes to the right cymbal pad (false = played automatically)",
            "RideOnRightCymbal=true",
            "",
            "MinimumTimeBeforeFirstNoteMs=2500",
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
            "; Force process exit if the game blocks an external quit request.",
            "ForceQuitOnSignal=1",
            "; The online service is gone: skip the 'reboot / offline' question at start and go straight to offline mode.",
            "AutoOfflineMode=1",
            "; Skip the KONAMI / BEAT ARENA logo intro at startup (it also waits until you look at the logo).",
            "SkipTitleScreen=1",
            "; Skip the caution screen (controller / sitting-down warnings)",
            "; NOT RECOMMENDED: This will drop you in the lobby instantly, before objects are properly initialized. You will see flashing objects as they load. Jarring in VR.",
            "SkipCautionScreen=0",
            "; Skip the 'make a gesture to each member and start playing' wait before a song.",
            "SkipPreGameGesture=0",
            "; Skip the 'send your impression to the members' countdown after a song and go straight to the score.",
            "SkipPostGameGesture=0",
            "; Unlock every avatar part, like the online service did (its 'unlock all parts' flag never arrives offline).",
            "UnlockAllAvatarParts=1",
            "; Also unlock all campaign rewards (feelings/stamps and reward parts) without clearing campaigns.",
            "UnlockCampaignRewards=0",
            "; Hide buttons that only work online (\"Members search\" on the song panel, \"Following\" in the left-stick menu).",
            "HideButtonsUnavailableOffline=1",
            "; Name shown in game (score boards, replays). Leave empty to use the game's own name.",
            "PlayerName=Player",
            "; PlayerName=",
            "",
            "[SongSelect]",
            "; Display songs in front of you instead of all around",
            "; This disables left/right scrolling, only up/down remains",
            "SongWheelForwardOnly=1",
            "; Width of the arc in degrees when using ForwardOnly",
            "; 180 degrees is a semicircle, 360 degrees is a full circle",
            "SongWheelForwardArc=120",
            "; Reopen the song wheel where you left it instead of at the first row",
            "SongWheelRememberPosition=1",
            "; Songs per row of the song wheel",
            "; Game default is 9",
            "; When using SongWheelForwardOnly, 5 is recommended",
            "SongWheelColumns=5",
            "; SongWheelColumns=9",
            "; Song wheel size multiplier (1 = vanilla, 0.7 m, max = 2)",
            "; SongWheelRadiusScale x SongWheelDistanceScale is capped at 2 (plates within 1.4 m)",
            "SongWheelRadiusScale=1",
            "; Distance multiplier of song wheel from your head",
            "SongWheelDistanceScale=1.5",
            "; Play song previews by briefly touching its jacket, instead of having to select the song",
            "SongWheelPlayPreviewByTouching=1",
            "; Change the time needed to select a song, otherwise PlayPreviewByTouching is very hard",
            "; to trigger without accidentally selecting the song",
            "; Game default = 300 ms",
            "SongWheelTouchSelectTimeMs=600",
            "; SongWheelTouchSelectTimeMs=300",
            "",
            "[Performance]",
            "; Load song wheel objects when they come into view instead of on scene load",
            "; For 1200 songs, improved load times from 32s to 1s",
            "LazySongWheel=1",
            "; Decode PNG jackets on background threads at startup",
            "; For 1200 songs, improved load times from 7.7s to 1s",
            "BackgroundJacketDecoding=1",
            "; In the Player Info menu, you can see a list of all songs in the game and your",
            "; score on it. This option hides custom songs you haven't played yet from this list.",
            "; The load time for 1200 entries is 20s",
            "PlayerInfoOnlyPlayedCustomSongs=1",
            "; Hide all custom songs from the Player Info menu.",
            "PlayerInfoHideCustomSongs=0",
            "",
        ]) + Environment.NewLine;

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

            int GetI(string section, string key, int defaultValue)
            {
                return int.TryParse(Get(section, key, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                    ? value
                    : defaultValue;
            }

            float GetF(string section, string key, float defaultValue)
            {
                return float.TryParse(Get(section, key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                    ? value
                    : defaultValue;
            }

            bool GetB(string section, string key, bool defaultValue)
            {
                string value = Get(section, key, "").ToLowerInvariant();
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
            s.TemplateSong = Get("CustomSongs", "TemplateSong", s.TemplateSong);
            s.FirstSongId = Math.Clamp(GetI("CustomSongs", "FirstSongId", s.FirstSongId), 100, 7000);
            s.DifficultiesCut = GetDifficultyCut("CustomSongs", "DifficultiesCut", s.DifficultiesCut);
            s.MaxSimultaneousHits = Math.Clamp(GetI("CustomSongs", "MaxSimultaneousHits", s.MaxSimultaneousHits), 1, 4);
            s.MinNoteGapMs = Math.Clamp(GetI("CustomSongs", "MinNoteGapMs", s.MinNoteGapMs), 0, 1000);
            s.CasualMinNoteGapMs = Math.Clamp(GetI("CustomSongs", "CasualMinNoteGapMs", s.CasualMinNoteGapMs), 0, 2000);
            s.CasualOnlyEighthNotes = GetB("CustomSongs", "CasualOnlyEighthNotes", s.CasualOnlyEighthNotes);
            s.RideOnRightCymbal = GetB("CustomSongs", "RideOnRightCymbal", s.RideOnRightCymbal);
            s.MinimumTimeBeforeFirstNoteMs = Math.Clamp(GetI("CustomSongs", "MinimumTimeBeforeFirstNoteMs", s.MinimumTimeBeforeFirstNoteMs), 0, 10000);
            s.MasterVolume = Math.Clamp(GetF("Audio", "MasterVolume", s.MasterVolume), 0f, 4f);
            s.BgmVolume = Math.Clamp(GetF("Audio", "BgmVolume", s.BgmVolume), 0f, 4f);
            s.DrumVolume = Math.Clamp(GetF("Audio", "DrumVolume", s.DrumVolume), 0f, 4f);
            s.AudioLatencyMs = Math.Clamp(GetI("Audio", "AudioLatencyMs", s.AudioLatencyMs), 10, 300);
            s.AudioOffsetMs = Math.Clamp(GetI("Audio", "AudioOffsetMs", s.AudioOffsetMs), -500, 500);
            s.ForceQuitOnSignal = GetB("Game", "ForceQuitOnSignal", s.ForceQuitOnSignal);
            s.AutoOfflineMode = GetB("Game", "AutoOfflineMode", s.AutoOfflineMode);
            s.SkipTitleScreen = GetB("Game", "SkipTitleScreen", s.SkipTitleScreen);
            s.SkipCautionScreen = GetB("Game", "SkipCautionScreen", s.SkipCautionScreen);
            s.SkipPostGameGesture = GetB("Game", "SkipPostGameGesture", s.SkipPostGameGesture);
            s.SkipPreGameGesture = GetB("Game", "SkipPreGameGesture", s.SkipPreGameGesture);
            s.UnlockAllAvatarParts = GetB("Game", "UnlockAllAvatarParts", s.UnlockAllAvatarParts);
            s.UnlockCampaignRewards = GetB("Game", "UnlockCampaignRewards", s.UnlockCampaignRewards);
            s.HideButtonsUnavailableOffline = GetB("Game", "HideButtonsUnavailableOffline", s.HideButtonsUnavailableOffline);
            s.PlayerName = configuration["Game:PlayerName"] ?? s.PlayerName;
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
