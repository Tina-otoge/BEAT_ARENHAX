using System;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class FolderNamePatches
    {
        private const string CaptionName = "BeatArenhax_FolderCaption";
        private const float MenuCaptionOffsetX = 62f;
        private const float MenuCaptionOffsetY = -14f;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(FolderNamePatches), typeof(MusicPlateBase), nameof(MusicPlateBase.Initialize), null, nameof(PlateInitialize_Post));
            Patches.Patch(h, typeof(FolderNamePatches), typeof(MusicInfoPlate), "SetMusicInfo", null, nameof(SetMusicInfo_Post));
        }

        private static void PlateInitialize_Post(MusicPlateBase __instance, Transform plateTrans)
        {
            try
            {
                Transform jacket = plateTrans.Find("Canvas/MP_JK_Dammy");
                Transform artist = plateTrans.Find("Canvas/MP_Txt_Artist");
                SetCaption(jacket, artist, Patches.SongById(__instance.MusicID), 12f);
            }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel folder caption: " + e.Message); }
        }

        private static void SetMusicInfo_Post(MusicInfoPlate __instance, int musicID)
        {
            try
            {
                SetMenuCaption(__instance.m_artistText, Patches.SongById(musicID));
            }
            catch (Exception e) { Plugin.L.LogWarning("Play menu folder caption: " + e.Message); }
        }

        private static void SetMenuCaption(TextMeshProUGUI artist, CustomSong song)
        {
            bool visible = Plugin.Settings.DisplayFolderName && !string.IsNullOrEmpty(song?.DisplayFolderName);
            if (artist == null)
            {
                if (visible) throw new InvalidOperationException("Artist text was not initialized.");
                return;
            }
            Transform caption = artist.transform.parent.Find(CaptionName);
            if (!visible)
            {
                if (caption != null) caption.gameObject.SetActive(false);
                return;
            }
            TextMeshProUGUI text;
            if (caption == null)
            {
                GameObject label = UnityEngine.Object.Instantiate(artist.gameObject, artist.transform.parent, false);
                label.name = CaptionName;
                text = label.GetComponent<TextMeshProUGUI>();
                RectTransform rect = text.rectTransform;
                // The space below the artist and above the instrument icons is to the right of the jacket.
                rect.anchoredPosition = artist.rectTransform.anchoredPosition + new Vector2(MenuCaptionOffsetX, MenuCaptionOffsetY);
                rect.sizeDelta = new Vector2(256f, 12f);
                text.alignment = TextAlignmentOptions.MidlineLeft;
                text.margin = Vector4.zero;
                text.color = Color.white;
                text.canvasRenderer.SetAlpha(1f);
                text.enableAutoSizing = true;
                text.fontSizeMin = 6f;
                text.fontSizeMax = 10f;
                text.fontSize = text.fontSizeMax;
                text.enableWordWrapping = false;
                text.richText = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.raycastTarget = false;
                caption = rect;
            }
            else
                text = caption.GetComponent<TextMeshProUGUI>();
            if (text == null) throw new InvalidOperationException("Play menu folder caption text is missing.");
            text.SetText(song.DisplayFolderName);
            caption.gameObject.SetActive(true);
        }

        private static void SetCaption(Transform jacket, Transform artist, CustomSong song, float height)
        {
            bool visible = Plugin.Settings.DisplayFolderName && !string.IsNullOrEmpty(song?.DisplayFolderName);
            if (jacket == null)
            {
                if (visible) throw new InvalidOperationException("Jacket transform was not initialized.");
                return;
            }
            Transform caption = jacket.Find(CaptionName);
            if (!visible)
            {
                if (caption != null) caption.gameObject.SetActive(false);
                return;
            }
            TextMeshProUGUI text;
            if (caption == null)
            {
                TextMeshProUGUI source = artist != null ? artist.GetComponent<TextMeshProUGUI>() : null;
                if (source == null) throw new InvalidOperationException("Artist font source was not initialized.");
                GameObject background = new(CaptionName);
                RectTransform rect = background.AddComponent<RectTransform>();
                rect.SetParent(jacket, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(1f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(0f, height);
                Image image = background.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.75f);
                image.raycastTarget = false;
                GameObject label = UnityEngine.Object.Instantiate(source.gameObject, rect, false);
                label.name = "Text";
                text = label.GetComponent<TextMeshProUGUI>();
                RectTransform labelRect = text.rectTransform;
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(3f, 0f);
                labelRect.offsetMax = new Vector2(-3f, 0f);
                text.alignment = TextAlignmentOptions.Center;
                text.color = Color.white;
                text.enableAutoSizing = true;
                text.fontSizeMin = 3f;
                text.fontSizeMax = height * 0.75f;
                text.fontSize = text.fontSizeMax;
                text.enableWordWrapping = false;
                text.richText = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                text.raycastTarget = false;
                label.SetActive(true);
                caption = rect;
            }
            else
                text = caption.GetComponentInChildren<TextMeshProUGUI>(true);
            if (text == null) throw new InvalidOperationException("Folder caption text is missing.");
            text.SetText(song.DisplayFolderName);
            caption.gameObject.SetActive(true);
        }
    }
}
