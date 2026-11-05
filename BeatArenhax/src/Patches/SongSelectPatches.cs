using System;
using System.Collections.Generic;
using System.IO;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    internal static class SongSelectPatches
    {
        private static readonly Dictionary<int, Sprite> Sprites = [];
        private static readonly HashSet<int> FailedSprites = [];

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(SongSelectPatches), typeof(MusicInfo), "get_PreviewCueName", null, nameof(PreviewCueName_Post));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(MusicInfoPlate), "StopPreview", null, nameof(StopPreview_Post));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(MusicPlateBase), "Initialize", nameof(PlateInitialize_Pre));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(MusicInfoPlate), "GetArtworkSprite", null, nameof(InfoPlateArtwork_Post));
        }

        private static void PreviewCueName_Post(MusicInfo __instance, ref string __result)
        {
            try
            {
                if (Patches.SongById(__instance.Id) != null) __result = AudioPatches.PreviewCue(__instance.Id);
            }
            catch { }
        }

        private static void StopPreview_Post() => Plugin.Audio?.StopPreview();

        private static void PlateInitialize_Pre(MusicPlateBase __instance)
        {
            try
            {
                CustomSong song = Patches.SongById(__instance.m_musicID);
                if (song?.PreImagePath == null) return;
                Sprite sprite = LoadSprite(song, __instance.m_artwork);
                if (sprite != null) __instance.m_artwork = sprite;
            }
            catch { }
        }

        private static void InfoPlateArtwork_Post(ref Sprite __result)
        {
            try
            {
                CustomSong song = Patches.SongById(PlayerInfoManager.GetInstance().SelectedMusicID);
                if (song?.PreImagePath == null) return;
                Sprite sprite = LoadSprite(song, __result);
                if (sprite != null) __result = sprite;
            }
            catch { }
        }

        private static Sprite LoadSprite(CustomSong song, Sprite template)
        {
            if (Sprites.TryGetValue(song.Id, out Sprite cached) && cached != null) return cached;
            if (FailedSprites.Contains(song.Id) || song.PreImagePath == null) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(song.PreImagePath);
                Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, false)) { _ = FailedSprites.Add(song.Id); return null; }
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                float pixelsPerUnit = template != null
                    ? template.pixelsPerUnit * texture.width / Math.Max(1f, template.rect.width)
                    : 100f;
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Sprites[song.Id] = sprite;
                return sprite;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Artwork load failed for " + song.PreImagePath + ": " + e.Message);
                _ = FailedSprites.Add(song.Id);
                return null;
            }
        }
    }
}
