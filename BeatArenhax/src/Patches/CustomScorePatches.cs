using System;
using System.Collections.Generic;
using System.IO;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace BeatArenhax
{
    internal static class CustomScorePatches
    {
        private static CustomScoreStore _store;
        private static readonly Dictionary<int, MusicRecord> Records = [];
        private static int _songId = -1;
        private static int _selectedIndex;

        internal static void Apply(Harmony h)
        {
            string path = Path.Combine(Plugin.GameDir, "custom_songs_scores.json");
            try { _store = new CustomScoreStore(path); }
            catch (Exception e)
            {
                Plugin.L.LogError("Cannot load custom scores; refusing to enable custom score routing: " + e);
                throw;
            }
            Patches.Patch(h, typeof(CustomScorePatches), typeof(MusicRecordTable), nameof(MusicRecordTable.GetMusicRecord), nameof(GetMusicRecord_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(PlayRecordManager), nameof(PlayRecordManager.GetCurrentRecord), nameof(GetCurrentRecord_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(PlayRecordManager), nameof(PlayRecordManager.CurrentRecordUpdate), nameof(CurrentRecordUpdate_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(PlayRecordManager), nameof(PlayRecordManager.RecordUpdate), nameof(RecordUpdate_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(MusicRecordTable), nameof(MusicRecordTable.RecordUpdate), nameof(TableRecordUpdate_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(MusicRecord), nameof(MusicRecord.RecordUpdate), nameof(MusicRecordUpdate_Pre));
            Patches.Patch(h, typeof(CustomScorePatches), typeof(Bvr.Player), "SubmitScore", nameof(SubmitScore_Pre));
        }

        internal static IReadOnlyList<CustomSongChart> Charts(CustomSong song) =>
            CustomChartSelection.Select(song.Charts, Plugin.Settings.DifficultiesCut);

        internal static int SelectedIndex(CustomSong song)
        {
            if (song == null) throw new ArgumentNullException(nameof(song));
            if (_songId != song.Id)
            {
                _songId = song.Id;
                _selectedIndex = Patches.DebugAutoplayActive && PlayerInfoManager.GetInstance().CurrentDifficulty == Difficulty.Advanced ? 1 : 0;
            }
            int count = Charts(song).Count;
            if (count == 0) throw new InvalidOperationException("Custom song has no playable charts: " + song.Id);
            if (_selectedIndex >= count) _selectedIndex = 0;
            return _selectedIndex;
        }

        internal static void Select(CustomSong song, int index)
        {
            IReadOnlyList<CustomSongChart> charts = Charts(song);
            if (index < 0 || index >= charts.Count) throw new ArgumentOutOfRangeException(nameof(index));
            _songId = song.Id;
            _selectedIndex = index;
        }

        internal static CustomScore Get(CustomSong song, PlayType playType, int index)
        {
            IReadOnlyList<CustomSongChart> charts = Charts(song);
            if (index < 0 || index >= charts.Count) return null;
            return _store.Get(new CustomScoreKey(song.Id, (int)playType, charts[index].SetIndex));
        }

        private static MusicRecord GetDetached(CustomSong song)
        {
            if (Records.TryGetValue(song.Id, out MusicRecord record)) return record;
            record = new MusicRecord(song.Id);
            IReadOnlyList<CustomSongChart> charts = Charts(song);
            foreach (CustomScore score in _store.GetSongScores(song.Id))
            {
                int index = -1;
                for (int i = 0; i < charts.Count; i++)
                    if (charts[i].SetIndex == score.SetIndex) { index = i; break; }
                if (index < 0) continue;
                Record gameRecord = new((PlayType)score.PlayType, (Difficulty)index)
                {
                    m_playCnt = score.PlayCount,
                    m_score = score.Score,
                    m_judges = score.Judges,
                };
                record.m_records.Add(gameRecord);
            }
            Records.Add(song.Id, record);
            return record;
        }

        private static bool GetMusicRecord_Pre(int musicID, ref MusicRecord __result)
        {
            CustomSong song = Patches.SongById(musicID);
            if (song == null) return true;
            __result = GetDetached(song);
            return false;
        }

        private static bool GetCurrentRecord_Pre(ref Record __result)
        {
            CustomSong song = Patches.CurrentSong();
            if (song == null) return true;
            PlayerInfoManager player = PlayerInfoManager.GetInstance();
            int index = player.CurrentPlayType == PlayType.Drum ? SelectedIndex(song)
                : Math.Min((int)player.CurrentDifficulty, Charts(song).Count - 1);
            __result = GetDetached(song).GetRecord(player.CurrentPlayType, (Difficulty)index);
            return false;
        }

        private static bool Save(CustomSong song, PlayType playType, int index, int score, Il2CppStructArray<int> judges)
        {
            if (judges == null || judges.Length != 5) throw new InvalidDataException("Expected five custom score judge counts.");
            int[] values = new int[5];
            for (int i = 0; i < values.Length; i++) values[i] = judges[i];
            IReadOnlyList<CustomSongChart> charts = Charts(song);
            if (index < 0 || index >= charts.Count) throw new InvalidOperationException("Custom score has no selected chart.");
            CustomScoreKey key = new(song.Id, (int)playType, charts[index].SetIndex);
            bool best;
            try { best = _store.Update(key, score, values); }
            catch (Exception e)
            {
                Plugin.L.LogError("Failed to save custom score for " + song.Title + " L" + key.SetIndex + ": " + e);
                throw;
            }
            Records.Remove(song.Id);
            Plugin.L.LogInfo($"Saved custom score: {song.Title} L{key.SetIndex}, {playType}, {score}");
            return best;
        }

        private static bool CurrentRecordUpdate_Pre(int score, Il2CppStructArray<int> judges, ref bool __result)
        {
            if (Patches.DebugAutoplayActive)
            {
                __result = false;
                Plugin.L.LogInfo("Debug autoplay: skipped saving the performance score.");
                return false;
            }
            CustomSong song = Patches.CurrentSong();
            if (song == null) return true;
            PlayerInfoManager player = PlayerInfoManager.GetInstance();
            int index = player.CurrentPlayType == PlayType.Drum ? SelectedIndex(song)
                : Math.Min((int)player.CurrentDifficulty, Charts(song).Count - 1);
            __result = Save(song, player.CurrentPlayType, index, score, judges);
            if (GameInfoManager.GetInstance().CurrentGameType == GameType.CAMPAIGN)
                CampaignRecordManager.GetInstance().CurrentRecordUpdate(score);
            return false;
        }

        private static bool RecordUpdate_Pre(int musicID, PlayType playType, Difficulty diff, int score, Il2CppStructArray<int> judges, ref bool __result) =>
            UpdateById(musicID, playType, diff, score, judges, ref __result);

        private static bool TableRecordUpdate_Pre(int musicID, PlayType playType, Difficulty diff, int score, Il2CppStructArray<int> judges, ref bool __result) =>
            UpdateById(musicID, playType, diff, score, judges, ref __result);

        private static bool MusicRecordUpdate_Pre(MusicRecord __instance, PlayType playType, Difficulty difficulty, int score,
            Il2CppStructArray<int> judges, ref bool __result) =>
            UpdateById(__instance.MusicID, playType, difficulty, score, judges, ref __result);

        private static bool UpdateById(int musicID, PlayType playType, Difficulty difficulty, int score,
            Il2CppStructArray<int> judges, ref bool result)
        {
            if (Patches.DebugAutoplayActive)
            {
                result = false;
                Plugin.L.LogInfo("Debug autoplay: skipped saving a score update.");
                return false;
            }
            CustomSong song = Patches.SongById(musicID);
            if (song == null) return true;
            int index = (int)difficulty;
            if (playType == PlayType.Drum && index <= 1 && Patches.CurrentSong()?.Id == musicID)
                index = SelectedIndex(song);
            else if (playType != PlayType.Drum)
                index = Math.Min(index, Charts(song).Count - 1);
            result = Save(song, playType, index, score, judges);
            return false;
        }

        private static bool SubmitScore_Pre()
        {
            if (!Patches.DebugAutoplayActive) return true;
            Plugin.L.LogInfo("Debug autoplay: skipped online score submission.");
            return false;
        }
    }
}
