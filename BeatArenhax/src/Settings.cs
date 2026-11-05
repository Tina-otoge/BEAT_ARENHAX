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
        public bool RideOnRightCymbal = true;
        public bool CasualOnlyEighthNotes = false;
        public int CasualMinNoteGapMs = 0;
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
        public bool SkipPostGameGesture = true;
        public bool SkipPreGameGesture = true;
        public bool UnlockAllAvatarParts = true;
        public bool UnlockCampaignRewards = false;
        public string PlayerName = "Player";

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
            "; Same but only for Casual charts",
            "CasualMinNoteGapMs=0",
            "; Ride cymbal goes to the right cymbal pad (false = played automatically)",
            "RideOnRightCymbal=true",
            "; Keep only 8ths in Casual charts",
            "CasualOnlyEighthNotes=false",
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
            "SkipPreGameGesture=1",
            "; Skip the 'send your impression to the members' countdown after a song and go straight to the score.",
            "SkipPostGameGesture=1",
            "; Unlock every avatar part, like the online service did (its 'unlock all parts' flag never arrives offline).",
            "UnlockAllAvatarParts=1",
            "; Also unlock all campaign rewards (feelings/stamps and reward parts) without clearing campaigns.",
            "UnlockCampaignRewards=0",
            "; Name shown in game (score boards, replays). Leave empty to use the game's own name.",
            "PlayerName=Player",
            "; PlayerName=",
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
            s.RideOnRightCymbal = GetB("CustomSongs", "RideOnRightCymbal", s.RideOnRightCymbal);
            s.CasualOnlyEighthNotes = GetB("CustomSongs", "CasualOnlyEighthNotes", s.CasualOnlyEighthNotes);
            s.CasualMinNoteGapMs = Math.Clamp(GetI("CustomSongs", "CasualMinNoteGapMs", s.CasualMinNoteGapMs), 0, 2000);
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
            s.PlayerName = configuration["Game:PlayerName"] ?? s.PlayerName;

            return s;
        }

    }
}
