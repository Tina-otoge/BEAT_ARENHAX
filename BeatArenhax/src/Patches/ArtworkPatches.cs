using System;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    // Custom jackets on screens outside the song wheel. Custom songs share the template's artwork name,
    // so the sprite has to be picked from the song being displayed rather than from the name.
    internal static class ArtworkPatches
    {
        private static int _lastArtworkSongId;
        private static string _lastArtworkName;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(ArtworkPatches), typeof(GameResult), "Update", null, nameof(GameResultUpdate_Post));
            Patches.Patch(h, typeof(ArtworkPatches), typeof(MusicInfo), "get_ArtworkName", null, nameof(ArtworkName_Post));
            Patches.Patch(h, typeof(ArtworkPatches), typeof(UiPlayerInfoView), "GetMusicArtworkSprite", null, nameof(PlayerInfoArtwork_Post));
        }

        // GameResult.Start sets resultGo/IMAGE_PATH[0] to the thumbnail found by artwork name.
        private static void GameResultUpdate_Post(GameResult __instance)
        {
            try
            {
                MusicInfo info = __instance.musicInfo;
                GameObject resultGo = __instance.resultGo;
                if (info == null || resultGo == null) return;
                CustomSong song = Patches.SongById(info.Id);
                if (song?.PreImagePath == null) return;
                Sprite current = __instance.thumbnail;
                Sprite sprite = SongSelectPatches.LoadSprite(song, current);
                if (sprite == null || current == sprite) return;
                __instance.thumbnail = sprite;
                string[] paths = __instance.IMAGE_PATH;
                Transform image = paths != null && paths.Length > 0 ? resultGo.transform.Find(paths[0]) : null;
                Image component = image != null ? image.GetComponent<Image>() : null;
                if (component != null) component.sprite = sprite;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Result artwork: " + e.Message);
            }
        }

        // UiPlayerInfoView.SetUiScore reads info.ArtworkName right before GetMusicArtworkSprite(name).
        private static void ArtworkName_Post(MusicInfo __instance, string __result)
        {
            _lastArtworkSongId = __instance.Id;
            _lastArtworkName = __result;
        }

        private static void PlayerInfoArtwork_Post(string artworkName, ref Sprite __result)
        {
            try
            {
                if (artworkName != _lastArtworkName) return;
                CustomSong song = Patches.SongById(_lastArtworkSongId);
                if (song?.PreImagePath == null) return;
                Sprite sprite = SongSelectPatches.LoadSprite(song, __result);
                if (sprite != null) __result = sprite;
            }
            catch { }
        }
    }
}
