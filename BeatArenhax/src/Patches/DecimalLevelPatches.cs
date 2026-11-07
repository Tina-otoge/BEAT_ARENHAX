using System;
using System.Globalization;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using TMPro;

namespace BeatArenhax
{
    internal static class DecimalLevelPatches
    {
        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.DecimalNotation) return;
            Patches.Patch(h, typeof(DecimalLevelPatches), typeof(MusicPlateBase), nameof(MusicPlateBase.SetDifficulty), null, nameof(SetDifficulty_Post));
        }

        private static void SetDifficulty_Post(MusicPlateBase __instance, PlayerSlot slot, Difficulty difficulty)
        {
            CustomSong song = Patches.SongById(__instance.MusicID);
            if (song == null) return;
            try
            {
                MusicInfo info = MusicInfoManager.GetInstance().GetMusicInfoByID(song.Id);
                if (info.GetPlayType(slot) != PlayType.Drum) return;
                SetLevel(__instance.m_levelText, difficulty == Difficulty.Basic ? song.CasualChart : song.AdvancedChart);
            }
            catch (Exception e) { Plugin.L.LogError("Song wheel decimal level: " + e); }
        }

        internal static void SetLevel(TextMeshProUGUI text, CustomSongChart chart)
        {
            if (text == null) throw new InvalidOperationException("Level text was not initialized.");
            if (!text.richText) throw new InvalidOperationException("Level text does not support scoped font sizing.");
            string level = chart.GetDecimalLevelText();
            UnityEngine.Vector2 preferred = text.GetPreferredValues(level, float.PositiveInfinity, float.PositiveInfinity);
            UnityEngine.Rect rect = text.rectTransform.rect;
            float width = rect.width - text.margin.x - text.margin.z;
            float height = rect.height - text.margin.y - text.margin.w;
            if (width <= 0 || height <= 0 || preferred.x <= 0 || preferred.y <= 0)
                throw new InvalidOperationException("Level text has no usable layout area.");
            float scale = Math.Min(1f, Math.Min(width / preferred.x, height / preferred.y));
            // Scoped tags leave the shared label's font and wrapping unchanged for the next official song.
            string size = (text.fontSize * scale * 0.98f).ToString("0.##", CultureInfo.InvariantCulture);
            text.SetText("<nobr><size=" + size + ">" + level + "</size></nobr>");
        }
    }
}
