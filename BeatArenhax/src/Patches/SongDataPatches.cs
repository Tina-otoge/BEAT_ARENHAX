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
        private static readonly List<MusicInfo> _templates = [];
        private static int _preparedSongId = -1;
        private static bool _legacyStarting;

        internal static string TemplateCue => _templateInfo != null ? _templateInfo.CueSheetName : Plugin.Settings.TemplateSong;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(SongDataPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetInstance), null, nameof(MusicInfoManager_GetInstance_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(GameInfoManager), nameof(GameInfoManager.GetDefaultReplayID), nameof(GetDefaultReplayID_Pre));
            Patches.Patch(h, typeof(SongDataPatches), typeof(Bvr.Player), "SubmitScore", nameof(SubmitScore_Pre));
            Patches.Patch(h, typeof(SongDataPatches), typeof(TextAsset), "get_bytes", null, nameof(TextAsset_Bytes_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(TextAsset), "get_text", null, nameof(TextAsset_Text_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(MusicInfoManager), nameof(MusicInfoManager.GetAllMusicListSize), null, nameof(GetAllMusicListSize_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(HomeMain), "GameStart", nameof(HomeGameStart_Pre));
            Patches.Patch(h, typeof(SongDataPatches), typeof(MusicSelectLegacy), "GameStart", nameof(LegacyGameStart_Pre), null, nameof(LegacyGameStart_Finalizer));
            Patches.Patch(h, typeof(SongDataPatches), typeof(Bvr.SubSceneManager), nameof(Bvr.SubSceneManager.LoadScene), nameof(LoadScene_Pre), nameof(LoadScene_Post));
            Patches.Patch(h, typeof(SongDataPatches), typeof(LyricsDisplayer), "Start", nameof(LyricsStart_Pre), null, nameof(LyricsStart_Finalizer));
            Patches.Patch(h, typeof(SongDataPatches), typeof(LyricsDisplayer), nameof(LyricsDisplayer.SetActiveLyrics), nameof(SetActiveLyrics_Pre));
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
                bool randomTemplate = string.IsNullOrWhiteSpace(Plugin.Settings.TemplateSong);
                HashSet<int> existingIds = [];
                _templates.Clear();
                for (int i = 0; i < list.Count; i++)
                {
                    MusicInfo musicInfo = list[i];
                    _ = existingIds.Add(musicInfo.Id);
                    if (musicInfo.CueSheetName == TemplateCue) template = musicInfo;
                    if (fallback == null && musicInfo.Instrumental == 1 && musicInfo.Id < 8000) fallback = musicInfo;
                    if (musicInfo.Id >= 0 && musicInfo.Id < 8000 && !string.IsNullOrWhiteSpace(musicInfo.CueSheetName)) _templates.Add(musicInfo);
                }
                if (template == null)
                {
                    template = fallback ?? list[0];
                    if (!randomTemplate) Plugin.L.LogWarning($"Template song '{TemplateCue}' not found, using '{template.CueSheetName}'");
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
            if (_legacyStarting && Patches.SongById(musicID) != null && _preparedSongId != musicID)
                PrepareTemplate(musicID);
            if (Patches.SongById(musicID) != null && _templateInfo != null) musicID = _templateInfo.Id;
        }

        private static void HomeGameStart_Pre()
        {
            PlayerInfoManager player = PlayerInfoManager.GetInstance();
            if (player != null && Patches.SongById(player.CurrentMusicID) != null) PrepareTemplate(player.CurrentMusicID);
        }

        private static void LegacyGameStart_Pre() => _legacyStarting = true;
        private static void LegacyGameStart_Finalizer() => _legacyStarting = false;

        private static void LoadScene_Pre(Bvr.SceneID sceneID, out bool __state)
        {
            __state = false;
            if (Bvr.SubSceneManager.IsChanging || sceneID < Bvr.SceneID.GuitarGameScene || sceneID > Bvr.SceneID.NostalgiaGameScene)
                return;
            PlayerInfoManager player = PlayerInfoManager.GetInstance();
            if (player == null || Patches.SongById(player.CurrentMusicID) == null) return;
            __state = true;
            if (_preparedSongId == player.CurrentMusicID) return;

            MusicInfo previous = _templateInfo;
            PrepareTemplate(player.CurrentMusicID);
            if (previous == null || previous.Id == _templateInfo.Id) return;
            ReplayDataManager replayManager = ReplayDataManager.Instance;
            if (replayManager == null || replayManager.replayIdArray == null) return;
            GameInfoManager gameInfo = GameInfoManager.GetInstance();
            for (int slot = 0; slot < replayManager.replayIdArray.Length; slot++)
            {
                int previousId = gameInfo.GetDefaultReplayID(previous.Id, slot);
                if (replayManager.replayIdArray[slot] == previousId)
                    replayManager.replayIdArray[slot] = gameInfo.GetDefaultReplayID(_templateInfo.Id, slot);
            }
        }

        private static void LoadScene_Post(bool __state, bool __result)
        {
            if (__state && __result) _preparedSongId = -1;
        }

        private static void PrepareTemplate(int songId)
        {
            if (_templateInfo == null || !string.IsNullOrWhiteSpace(Plugin.Settings.TemplateSong))
            {
                _preparedSongId = songId;
                return;
            }
            MusicInfo info = MusicInfoManager.GetInstance().GetMusicInfoByID(songId);
            if (info == null) throw new InvalidOperationException($"Custom music info {songId} not found.");
            List<MusicInfo> compatible = _templates.FindAll(candidate => TemplateSelection.Compatible(candidate.Id,
                candidate.CueSheetName, (int)candidate.DataType, (int)candidate.ThirdPlayType,
                (int)info.DataType, (int)info.ThirdPlayType));
            MusicInfo selected = compatible[TemplateSelection.ChooseIndex(compatible.Count, System.Random.Shared.Next)];
            info._CueSheetName_k__BackingField = selected.CueSheetName;
            info._Instrumental_k__BackingField = selected.Instrumental;
            _templateInfo = selected;
            _preparedSongId = songId;
            Plugin.L.LogInfo($"Custom song {songId} uses random performance template '{selected.CueSheetName}'");
        }

        private static void LyricsStart_Pre(out MusicInfo __state)
        {
            __state = null;
            PlayerInfoManager player = PlayerInfoManager.GetInstance();
            if (player == null || Patches.SongById(player.CurrentMusicID) == null) return;
            MusicInfo info = player.CurrentMusicInfo();
            if (info == null || info.Instrumental != 0) return;
            __state = info;
            info._Instrumental_k__BackingField = 1;
        }

        private static void LyricsStart_Finalizer(MusicInfo __state)
        {
            if (__state != null) __state._Instrumental_k__BackingField = 0;
        }

        private static void SetActiveLyrics_Pre(ref bool isActive)
        {
            if (Patches.CurrentSong() != null) isActive = false;
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
            if (name != TemplateCue + "_dr0" && name != TemplateCue + "_dr1") return;
            CustomSong song = Patches.CurrentDrumSong();
            if (song == null) return;
            try
            {
                int selected = CustomScorePatches.SelectedIndex(song);
                ConvertedCustomChart converted = Plugin.Audio.ArmSelectedChart(song, selected);
                ConvertedChart chart = converted.Chart;
                __result = chart.Midi;
                if (_lastChartName != song.Folder + selected)
                {
                    _lastChartName = song.Folder + selected;
                    Plugin.L.LogInfo($"Loaded custom chart '{song.Title}' ({converted.Definition.Label}, L{converted.Definition.SetIndex}): {chart.PlayableCount} notes, {chart.AutoCount} auto, {chart.LengthMs / 1000:0.0}s");
                }
            }
            catch (Exception e) { Plugin.L.LogError("Chart conversion failed for " + song.Folder + ": " + e); throw; }
        }

        private static void TextAsset_Text_Post(TextAsset __instance, ref string __result)
        {
            if (Plugin.Audio == null || Plugin.Library.Songs.Count == 0) return;
            string name;
            try { name = __instance.name; } catch { return; }
            if (name != TemplateCue + "_dr_param") return;
            CustomSong song = Patches.CurrentDrumSong();
            if (song == null) return;
            try { __result = Plugin.Audio.GetConvertedChart(song, CustomScorePatches.SelectedIndex(song)).ParamJson; }
            catch (Exception e) { Plugin.L.LogError("Param generation failed for " + song.Folder + ": " + e); throw; }
        }
    }
}
