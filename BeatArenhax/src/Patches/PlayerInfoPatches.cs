using System;
using System.Collections.Generic;
using HarmonyLib;

namespace BeatArenhax
{
    // The player info screen (left-stick menu) builds a score window for every selectable song, each time the song
    // select scene loads. With a thousand custom songs, creating, filling and later disabling those windows took
    // about 20 seconds, mostly on the way back from a performance. The list can omit unplayed or all custom songs.
    internal static class PlayerInfoPatches
    {
        private static List<int> _indices;
        private static int _depth;

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.PlayerInfoOnlyPlayedCustomSongs && !Plugin.Settings.PlayerInfoHideCustomSongs) return;
            Patches.Patch(h, typeof(PlayerInfoPatches), typeof(UiPlayerInfoView), "SetUiScore", nameof(BuildList_Pre), null, nameof(Leave));
            Patches.Patch(h, typeof(PlayerInfoPatches), typeof(UiPlayerInfoView), "SetScoreValue", nameof(Enter), null, nameof(Leave));
            Patches.Patch(h, typeof(PlayerInfoPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetMusicListSize), null, nameof(GetMusicListSize_Post));
            Patches.Patch(h, typeof(PlayerInfoPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetMusicInfo), nameof(GetMusicInfo_Pre));
        }

        // SetUiScore creates the windows by list index, SetScoreValue later fills them by the same index.
        private static void BuildList_Pre()
        {
            _indices = null;
            try
            {
                MusicInfoManager manager = MusicInfoManager.GetInstance();
                PlayRecordManager records = PlayRecordManager.GetInstance();
                if (manager == null || records == null) return;
                int count = manager.GetMusicListSize();
                List<int> indices = new(count);
                for (int i = 0; i < count; i++)
                {
                    MusicInfo info = manager.GetMusicInfo(i);
                    if (info == null) continue;
                    if (Patches.SongById(info.Id) != null)
                    {
                        if (Plugin.Settings.PlayerInfoHideCustomSongs) continue;
                        MusicRecord record = records.GetMusicRecord(info.Id);
                        if (Plugin.Settings.PlayerInfoOnlyPlayedCustomSongs && (record == null || record.GetTotalPlayCnt() <= 0)) continue;
                    }
                    indices.Add(i);
                }
                _indices = indices;
                string hiddenSongs = Plugin.Settings.PlayerInfoHideCustomSongs ? "custom songs hidden" : "unplayed custom songs hidden";
                Plugin.L.LogInfo($"Player info: {indices.Count} of {count} songs listed ({hiddenSongs})");
            }
            catch (Exception e) { Plugin.L.LogWarning("Player info song list: " + e.Message); }
            finally { _depth++; }
        }

        private static void Enter() => _depth++;
        private static void Leave() => _depth = Math.Max(0, _depth - 1);

        private static void GetMusicListSize_Post(ref int __result)
        {
            if (_depth > 0 && _indices != null) __result = _indices.Count;
        }

        private static void GetMusicInfo_Pre(ref int index)
        {
            if (_depth > 0 && _indices != null && index >= 0 && index < _indices.Count) index = _indices[index];
        }
    }
}
