using System;
using System.Globalization;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace BeatArenhax
{
    internal static class ResultDifficultyPatches
    {
        private const string CaptionName = "BeatArenhax_ResultDifficulty";
        private const string FolderCaptionName = "BeatArenhax_ResultFolder";
        private const string MusicInfoPath = "Ingame_Result/Img_Background/Music_Info";
        private const float CaptionWidth = 2400f;
        private const float CaptionHeight = 250f;
        private const float CaptionGap = 150f;
        private const float DifficultyLineOffsetY = 100f;
        private const float FolderLineOffsetY = -150f;

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.ShowDifficultyOnResultScreen) return;
            Patches.Patch(h, typeof(ResultDifficultyPatches), typeof(GameResult), "Update", null, nameof(Update_Post));
        }

        private static void Update_Post(GameResult __instance)
        {
            try
            {
                MusicInfo info = __instance.musicInfo;
                GameObject resultGo = __instance.resultGo;
                if (info == null || resultGo == null) return;
                (string difficulty, string level, string folder, string color) =
                    BuildCaption(info, PlayerInfoManager.GetInstance());

                Transform musicInfo = resultGo.transform.Find(MusicInfoPath);
                Transform jacket = musicInfo != null ? musicInfo.Find("JK_Dummy") : null;
                Transform artist = musicInfo != null ? musicInfo.Find("Tx_Artist") : null;
                if (jacket == null || artist == null)
                    throw new InvalidOperationException("Result jacket or artist text was not initialized.");

                TextMeshProUGUI source = artist.GetComponent<TextMeshProUGUI>();
                RectTransform jacketRect = jacket.GetComponent<RectTransform>();
                if (source == null || jacketRect == null)
                    throw new InvalidOperationException("Result caption source or jacket layout is missing.");
                if (!ColorUtility.TryParseHtmlString(color, out Color difficultyColor))
                    throw new InvalidOperationException("Result difficulty color is invalid: " + color);

                Vector2 linePosition = jacketRect.anchoredPosition
                    + new Vector2(jacketRect.rect.width / 2f + CaptionWidth / 2f + CaptionGap, 0f);
                TextMeshProUGUI difficultyText = FindOrCreateLabel(musicInfo, source, CaptionName,
                    linePosition + new Vector2(0f, DifficultyLineOffsetY), difficultyColor);
                string content = difficulty + " " + level;
                if (difficultyText.text != content) difficultyText.SetText(content);
                difficultyText.color = difficultyColor;
                difficultyText.gameObject.SetActive(true);

                Transform folderCaption = musicInfo.Find(FolderCaptionName);
                if (string.IsNullOrWhiteSpace(folder))
                {
                    if (folderCaption != null) folderCaption.gameObject.SetActive(false);
                }
                else
                {
                    TextMeshProUGUI folderText = FindOrCreateLabel(musicInfo, source, FolderCaptionName,
                        linePosition + new Vector2(0f, FolderLineOffsetY), Color.white);
                    if (folderText.text != folder) folderText.SetText(folder);
                    folderText.gameObject.SetActive(true);
                }
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Result difficulty caption: " + e.Message);
            }
        }

        private static (string Difficulty, string Level, string Folder, string Color) BuildCaption(MusicInfo info, PlayerInfoManager player)
        {
            if (player == null) throw new InvalidOperationException("Player info was not initialized.");

            CustomSong song = Patches.SongById(info.Id);
            string difficulty;
            string level;
            string folder = null;
            string color;
            if (song != null)
            {
                var charts = CustomScorePatches.Charts(song);
                if (charts.Count == 0) throw new InvalidOperationException("Custom song has no playable charts: " + song.Id);
                int index = player.CurrentPlayType == PlayType.Drum
                    ? CustomScorePatches.SelectedIndex(song)
                    : Math.Min((int)player.CurrentDifficulty, charts.Count - 1);
                if (index < 0 || index >= charts.Count)
                    throw new InvalidOperationException("Selected custom chart is outside the available chart range.");

                CustomSongChart chart = charts[index];
                difficulty = string.IsNullOrWhiteSpace(chart.Label)
                    ? GetDifficultyName(player.CurrentPlayType, player.CurrentDifficulty)
                    : chart.Label.Trim();
                color = Colors.DifficultyColor(chart.Label)
                    ?? GetDifficultyColor(player.CurrentPlayType, player.CurrentDifficulty);
                level = Plugin.Settings.DecimalNotation
                    ? chart.GetDecimalLevelText()
                    : ChartConverter.ToGameLevel(chart.DLevel).ToString(CultureInfo.InvariantCulture);
                if (Plugin.Settings.DisplayFolderName) folder = song.DisplayFolderName;
            }
            else
            {
                difficulty = GetDifficultyName(player.CurrentPlayType, player.CurrentDifficulty);
                color = GetDifficultyColor(player.CurrentPlayType, player.CurrentDifficulty);
                level = info.GetLevel(player.CurrentPlayerSlot, player.CurrentDifficulty).ToString(CultureInfo.InvariantCulture);
            }

            return (difficulty, level, folder, color);
        }

        private static string GetDifficultyName(PlayType playType, Difficulty difficulty) =>
            playType == PlayType.Drum && difficulty == Difficulty.Basic ? "Casual" : difficulty.ToString();

        private static string GetDifficultyColor(PlayType playType, Difficulty difficulty) =>
            playType == PlayType.Drum && difficulty == Difficulty.Basic
                ? Colors.CasualDifficulty
                : Colors.DifficultyColor(GetDifficultyName(playType, difficulty)) ?? Colors.BeginnerDifficulty;

        private static TextMeshProUGUI FindOrCreateLabel(Transform parent, TextMeshProUGUI source, string name,
            Vector2 position, Color color)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                TextMeshProUGUI text = existing.GetComponent<TextMeshProUGUI>();
                RectTransform rect = existing.GetComponent<RectTransform>();
                if (text == null || rect == null) throw new InvalidOperationException(name + " layout is missing.");
                rect.anchoredPosition = position;
                return text;
            }

            GameObject label = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
            label.name = name;
            TextMeshProUGUI created = label.GetComponent<TextMeshProUGUI>();
            if (created == null) throw new InvalidOperationException(name + " text is missing.");
            RectTransform createdRect = created.rectTransform;
            createdRect.sizeDelta = new Vector2(CaptionWidth, CaptionHeight);
            createdRect.anchoredPosition = position;
            created.alignment = TextAlignmentOptions.MidlineLeft;
            created.margin = Vector4.zero;
            created.color = color;
            created.enableAutoSizing = true;
            created.fontSizeMin = 80f;
            created.fontSizeMax = 200f;
            created.fontSize = created.fontSizeMax;
            created.enableWordWrapping = false;
            created.richText = false;
            created.overflowMode = TextOverflowModes.Ellipsis;
            created.raycastTarget = false;
            return created;
        }
    }
}
