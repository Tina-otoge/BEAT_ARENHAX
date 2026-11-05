using System;
using System.Collections.Generic;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BeatArenhax
{
    internal static class SongDataPatches
    {
        private static bool _injected;
        private static string _lastChartName;
        private static MusicInfo _templateInfo;

        internal static string TemplateCue => Plugin.Settings.TemplateSong;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(SongDataPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetInstance), null, nameof(MusicInfoManager_GetInstance_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(GameInfoManager), nameof(GameInfoManager.GetDefaultReplayID), nameof(GetDefaultReplayID_Pre));
            Patches.Patch(h, typeof(SongDataPatches), typeof(Bvr.Player), "SubmitScore", nameof(SubmitScore_Pre));
            Patches.Patch(h, typeof(SongDataPatches), typeof(TextAsset), "get_bytes", null, nameof(TextAsset_Bytes_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(TextAsset), "get_text", null, nameof(TextAsset_Text_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetAllMusicListSize), null, nameof(GetAllMusicListSize_Post));
        }

        private static int _vanillaSongCount = -1;

        // At startup DefaultReplayLoader loads and parses the default band replays of every song, one at a time.
        // Custom songs share the template's cue, so it parsed the template's replays again for each of them (about
        // 50 s with a thousand songs), while GetDefaultReplayID_Pre already sends them to the template's replays.
        // Custom songs are at the end of the list, so the loader just doesn't see them.
        private static void GetAllMusicListSize_Post(ref int __result)
        {
            if (_vanillaSongCount >= 0 && __result > _vanillaSongCount && DefaultReplayLoader.IsLoading) __result = _vanillaSongCount;
        }

        internal static void EnsureInjected()
        {
            try { Inject(MusicInfoManager.GetInstance()); }
            catch (Exception e) { Plugin.L.LogError("EnsureInjected: " + e); }
        }

        private static void MusicInfoManager_GetInstance_Post(MusicInfoManager __result)
        {
            if (!_injected && __result != null) Inject(__result);
        }

        private static void Inject(MusicInfoManager manager)
        {
            if (_injected) return;
            try
            {
                if (!Plugin.ScanTask.Wait(TimeSpan.FromSeconds(120))) Plugin.L.LogWarning("Song scan still running, continuing");
                Il2CppSystem.Collections.Generic.List<MusicInfo> list = manager.allMusicList;
                if (list == null || list.Count == 0) return;

                MusicInfo template = null, fallback = null;
                HashSet<int> existingIds = [];
                for (int i = 0; i < list.Count; i++)
                {
                    MusicInfo musicInfo = list[i];
                    _ = existingIds.Add(musicInfo.Id);
                    if (musicInfo.CueSheetName == TemplateCue) template = musicInfo;
                    if (fallback == null && musicInfo.Instrumental == 1 && musicInfo.Id < 8000) fallback = musicInfo;
                }
                if (template == null)
                {
                    Plugin.L.LogWarning($"Template song '{TemplateCue}' not found, using '{fallback?.CueSheetName}'");
                    template = fallback ?? list[0];
                    Plugin.Settings.TemplateSong = template.CueSheetName;
                }
                _templateInfo = template;
                _vanillaSongCount = list.Count;

                PlayerSlot guitarSlot = template.GetPlayerSlot(PlayType.Guitar);
                PlayerSlot thirdSlot = template.GetPlayerSlot(template.ThirdPlayType);
                int added = 0;
                foreach (CustomSong song in Plugin.Library.Songs)
                {
                    if (existingIds.Contains(song.Id)) continue;
                    MusicInfoJSON json = new()
                    {
                        Id = song.Id,
                        Instrumental = template.Instrumental,
                        DataType = (int)template.DataType,
                        SequenceOffset = 0,
                        MusicName = song.Title,
                        EN_MusicName = song.Title,
                        SortMusicName = song.Title.ToLowerInvariant(),
                        EN_SortMusicName = song.Title.ToLowerInvariant(),
                        ArtistName = song.Artist,
                        EN_ArtistName = song.Artist,
                        CueSheetName = template.CueSheetName,
                        ThirdPlayType = (int)template.ThirdPlayType,
                        EventType = 0,
                        CasualGuitarLevel = template.GetLevel(guitarSlot, Difficulty.Basic),
                        AdvancedGuitarLevel = template.GetLevel(guitarSlot, Difficulty.Advanced),
                        CasualThirdPlayTypeLevel = template.GetLevel(thirdSlot, Difficulty.Basic),
                        AdvancedThirdPlayTypeLevel = template.GetLevel(thirdSlot, Difficulty.Advanced),
                        CasualDrumLevel = song.CasualLevel,
                        AdvancedDrumLevel = song.AdvancedLevel,
                    };
                    list.Add(new MusicInfo(json));
                    added++;
                }
                manager.RefreshActiveList();
                Plugin.L.LogInfo($"Added {added} custom songs to the music list (template '{template.CueSheetName}'), {manager.GetMusicListSize()} selectable");
                _injected = true;
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Injecting songs failed: " + e);
            }
        }

        private static void GetDefaultReplayID_Pre(ref int musicID)
        {
            if (Patches.SongById(musicID) != null && _templateInfo != null) musicID = _templateInfo.Id;
        }

        private static bool SubmitScore_Pre()
        {
            if (Patches.CurrentSong() == null) return true;
            Plugin.L.LogInfo("Custom song: online score submission skipped");
            return false;
        }

        private static void TextAsset_Bytes_Post(TextAsset __instance, ref Il2CppStructArray<byte> __result)
        {
            if (Plugin.Audio == null || Plugin.Library.Songs.Count == 0) return;
            string name;
            try { name = __instance.name; } catch { return; }
            int difficulty;
            if (name == TemplateCue + "_dr0") difficulty = 0;
            else if (name == TemplateCue + "_dr1") difficulty = 1;
            else return;
            CustomSong song = Patches.CurrentDrumSong();
            if (song == null) return;
            try
            {
                ConvertedChart chart = Plugin.Audio.Arm(song, difficulty);
                __result = chart.Midi;
                if (_lastChartName != song.Folder + difficulty)
                {
                    _lastChartName = song.Folder + difficulty;
                    Plugin.L.LogInfo($"Loaded custom chart '{song.Title}' ({(difficulty == 0 ? "Casual" : "Advanced")}): {chart.PlayableCount} notes, {chart.AutoCount} auto, {chart.LengthMs / 1000:0.0}s");
                }
            }
            catch (Exception e) { Plugin.L.LogError("Chart conversion failed for " + song.Folder + ": " + e); }
        }

        private static void TextAsset_Text_Post(TextAsset __instance, ref string __result)
        {
            if (Plugin.Audio == null || Plugin.Library.Songs.Count == 0) return;
            string name;
            try { name = __instance.name; } catch { return; }
            if (name != TemplateCue + "_dr_param") return;
            CustomSong song = Patches.CurrentDrumSong();
            if (song == null) return;
            try { __result = Plugin.Audio.GetConverted(song).ParamJson; }
            catch (Exception e) { Plugin.L.LogError("Param generation failed for " + song.Folder + ": " + e); }
        }
    }
}
