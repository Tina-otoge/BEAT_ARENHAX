using System;
using System.Collections.Generic;
using System.Globalization;
using BeatArenhax.CustomSongs;
using BeatArenhax.DTX;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class SongWheelStatsPatches
    {
        private const float StatsFontSize = 5f;
        private const float StatsMinFontSize = 2.5f;
        private const float StatsHorizontalPadding = 1.5f;
        private const float StatsHeight = 15f;
        private static readonly string[] CustomLevelColors =
            [Colors.BeginnerDifficulty, Colors.AdvancedDifficulty, Colors.ExtremeDifficulty, Colors.MasterDifficulty];
        private const string GradeColor = "#FFFFFF";
        private const string StatsName = "BeatArenhax_WheelStats";
        private static readonly HashSet<IntPtr> WheelPlates = [];
        private static readonly List<MusicPlateBase> InitializedPlates = [];
        private static readonly Dictionary<int, MusicRecord> Records = [];

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.ShowStats) return;
            Patches.Patch(h, typeof(SongWheelStatsPatches), typeof(MusicSelect), "ReadyMusicPlateList", null, nameof(Ready_Post));
            Patches.Patch(h, typeof(SongWheelStatsPatches), typeof(MusicSelect), nameof(MusicSelect.Open), null, nameof(Open_Post));
            Patches.Patch(h, typeof(SongWheelStatsPatches), typeof(MusicPlateBase), "Start", null, nameof(Start_Post));
        }

        private static void Ready_Post(MusicSelect __instance)
        {
            try
            {
                WheelPlates.Clear();
                InitializedPlates.Clear();
                for (int i = 0; i < __instance.m_musicPlateList.Count; i++)
                    WheelPlates.Add(__instance.m_musicPlateList[i].Pointer);
                RefreshRecords();
            }
            catch (Exception e) { Plugin.L.LogError("Preparing song wheel stats failed: " + e); }
        }

        private static void Open_Post(MusicSelect __instance, bool __result)
        {
            if (!__result) return;
            try
            {
                RefreshRecords();
                foreach (MusicPlateBase plate in InitializedPlates)
                    if (plate != null) SetStats(plate);
            }
            catch (Exception e) { Plugin.L.LogError("Refreshing song wheel stats failed: " + e); }
        }

        private static void Start_Post(MusicPlateBase __instance)
        {
            if (!WheelPlates.Contains(__instance.Pointer)) return;
            try
            {
                SetStats(__instance);
                InitializedPlates.Add(__instance);
            }
            catch (Exception e) { Plugin.L.LogError("Creating song wheel stats failed: " + e); }
        }

        private static void RefreshRecords()
        {
            PlayRecordManager manager = PlayRecordManager.GetInstance();
            if (manager == null || manager.m_musicRecordTable == null || manager.m_musicRecordTable.m_musicRecords == null)
                throw new InvalidOperationException("The saved score table is unavailable.");
            Records.Clear();
            var records = manager.m_musicRecordTable.m_musicRecords;
            for (int i = 0; i < records.Count; i++)
            {
                MusicRecord record = records[i];
                if (record != null) Records[record.MusicID] = record;
            }
        }

        private static void SetStats(MusicPlateBase plate)
        {
            Transform jacket = plate.transform.Find("Canvas/MP_JK_Dammy");
            Transform artist = plate.transform.Find("Canvas/MP_Txt_Artist");
            if (jacket == null || artist == null) throw new InvalidOperationException("Song plate artwork or font is missing.");
            Transform existing = jacket.Find(StatsName);
            TextMeshProUGUI text;
            if (existing == null)
            {
                TextMeshProUGUI source = artist.GetComponent<TextMeshProUGUI>();
                if (source == null) throw new InvalidOperationException("Song plate artist font is missing.");
                GameObject background = new(StatsName);
                RectTransform rect = background.AddComponent<RectTransform>();
                rect.SetParent(jacket, false);
                rect.anchorMin = new Vector2(1f, 0f);
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(1f, 0f);
                rect.anchoredPosition = new Vector2(0f, 12f);
                rect.sizeDelta = new Vector2(44f, StatsHeight);
                Image image = background.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.75f);
                image.raycastTarget = false;
                GameObject label = UnityEngine.Object.Instantiate(source.gameObject, rect, false);
                label.name = "Text";
                text = label.GetComponent<TextMeshProUGUI>();
                RectTransform labelRect = text.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(StatsHorizontalPadding, 0f);
                labelRect.offsetMax = new Vector2(-StatsHorizontalPadding, 0f);
                text.alignment = TextAlignmentOptions.MidlineRight;
                text.margin = Vector4.zero;
                text.color = Color.white;
                text.enableAutoSizing = true;
                text.fontSizeMin = StatsMinFontSize;
                text.fontSizeMax = StatsFontSize;
                text.fontSize = StatsFontSize;
                text.enableWordWrapping = false;
                text.richText = true;
                text.overrideColorTags = false;
                text.raycastTarget = false;
                label.SetActive(true);
            }
            else
                text = existing.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null) throw new InvalidOperationException("Song wheel stats text is missing.");

            MusicInfo info = MusicInfoManager.GetInstance().GetMusicInfoByID(plate.MusicID);
            if (info == null) throw new InvalidOperationException("Song metadata is missing: " + plate.MusicID);
            PlayerSlot slot = PlayerInfoManager.GetInstance().CurrentPlayerSlot;
            PlayType playType = info.GetPlayType(slot);
            Records.TryGetValue(plate.MusicID, out MusicRecord record);
            CustomSong song = Patches.SongById(plate.MusicID);
            string content;
            bool customDrum = song != null && playType == PlayType.Drum;
            if (customDrum)
            {
                IReadOnlyList<CustomSongChart> charts = CustomScorePatches.Charts(song);
                string[] lines = new string[CustomChartSelection.VisibleCount];
                for (int i = 0; i < lines.Length; i++)
                {
                    string level = "--", grade = "--";
                    if (i < charts.Count)
                    {
                        level = Plugin.Settings.DecimalNotation ? charts[i].GetDecimalLevelText()
                            : ChartConverter.ToGameLevel(charts[i].DLevel).ToString(CultureInfo.InvariantCulture);
                        CustomScore score = CustomScorePatches.Get(song, playType, i);
                        if (score != null && score.PlayCount > 0) grade = bvr.ScoreRank(score.Score).ToString();
                    }
                    lines[i] = SongWheelStatsText.FormatLine(level, grade, CustomLevelColors[i], GradeColor);
                }
                content = string.Join("\n", lines);
            }
            else
            {
                if (song != null) record = PlayRecordManager.GetInstance().GetMusicRecord(song.Id);
                (string casualLevel, string casualGrade) = GetStats(info, song, record, slot, playType, Difficulty.Basic);
                (string advancedLevel, string advancedGrade) = GetStats(info, song, record, slot, playType, Difficulty.Advanced);
                content = SongWheelStatsText.FormatLine(casualLevel, casualGrade, Colors.CasualDifficulty, GradeColor)
                    + "\n" + SongWheelStatsText.FormatLine(advancedLevel, advancedGrade, Colors.AdvancedDifficulty, GradeColor);
            }
            text.enableAutoSizing = false;
            text.fontSize = StatsFontSize;
            Vector2 preferred = text.GetPreferredValues(content, float.PositiveInfinity, float.PositiveInfinity);
            if (float.IsNaN(preferred.x) || float.IsInfinity(preferred.x) || preferred.x <= 0f)
                throw new InvalidOperationException("Song wheel stats text has no usable preferred width.");
            RectTransform backgroundRect = text.transform.parent.GetComponent<RectTransform>();
            if (backgroundRect == null) throw new InvalidOperationException("Song wheel stats background is missing.");
            backgroundRect.sizeDelta = new Vector2(preferred.x + 2f * StatsHorizontalPadding, customDrum ? StatsHeight * 2f : StatsHeight);
            text.enableAutoSizing = true;
            text.SetText(content);
        }

        private static (string Level, string Grade) GetStats(MusicInfo info, CustomSong song, MusicRecord musicRecord,
            PlayerSlot slot, PlayType playType, Difficulty difficulty)
        {
            string level = song != null && playType == PlayType.Drum && Plugin.Settings.DecimalNotation
                ? (difficulty == Difficulty.Basic ? song.CasualChart : song.AdvancedChart).GetDecimalLevelText()
                : info.GetLevel(slot, difficulty).ToString(CultureInfo.InvariantCulture);
            string grade = "--";
            if (musicRecord != null && musicRecord.m_records != null)
            {
                var records = musicRecord.m_records;
                for (int i = 0; i < records.Count; i++)
                {
                    Record record = records[i];
                    if (record != null && record.PlayType == playType && record.Difficulty == difficulty && record.PlayCount > 0)
                    {
                        grade = bvr.ScoreRank(record.Score).ToString();
                        break;
                    }
                }
            }
            return (level, grade);
        }
    }
}
