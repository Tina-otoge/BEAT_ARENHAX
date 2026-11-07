using System;
using System.Reflection;
using BeatArenhax.CustomSongs;
using HarmonyLib;

namespace BeatArenhax
{
    internal static class Patches
    {
        [ThreadStatic] private static bool _inCurrentSong;

        internal static bool DebugAutoplayActive;

        internal static void Apply(Harmony h)
        {
            StartupPatches.Apply(h);
            GrabWithoutWaitPatches.Apply(h);
            ResultMenuPatches.Apply(h);
            FastSlowPatches.Apply(h);
            VirtualScreenPatches.Apply(h);
            PedalPatches.Apply(h);
            SpectatorPatches.Apply();
            SongDataPatches.Apply(h);
            PartnerReplayPatches.Apply(h);
            CustomScorePatches.Apply(h);
            DecimalLevelPatches.Apply(h);
            CustomDifficultyPatches.Apply(h);
            AudioPatches.Apply(h);
            StageVideoPatches.Apply(h);
            SongSelectPatches.Apply(h);
            SongWheelStatsPatches.Apply(h);
            FolderNamePatches.Apply(h);
            FolderPickerPatches.Apply(h);
            SongWheelTouchPatches.Apply(h);
            OfflineUIPatches.Apply(h);
            QuitButtonPatches.Apply(h);
            ModSettingsMenuPatches.Apply(h);
            ArtworkPatches.Apply(h);
            SongWheelLazyPatches.Apply(h);
            PlayerInfoPatches.Apply(h);
            ResultDifficultyPatches.Apply(h);
        }

        // Finalizers run even when the original throws; use them to reset state set by a prefix.
        internal static void Patch(Harmony h, Type owner, Type targetType, string method, string prefix, string postfix = null, string finalizer = null, int prefixPriority = Priority.Normal)
        {
            try
            {
                MethodInfo target = AccessTools.Method(targetType, method) ?? throw new MissingMethodException(targetType.Name, method);
                _ = h.Patch(target,
                    prefix: prefix != null ? new HarmonyMethod(owner, prefix) { priority = prefixPriority } : null,
                    postfix: postfix != null ? new HarmonyMethod(owner, postfix) : null,
                    finalizer: finalizer != null ? new HarmonyMethod(owner, finalizer) : null,
                    ilmanipulator: null);
            }
            catch (Exception e)
            {
                Plugin.L.LogError($"Patch {targetType.Name}.{method} failed: {e.Message}");
            }
        }

        internal static CustomSong SongById(int id) => Plugin.Library.ById.TryGetValue(id, out CustomSong song) ? song : null;

        internal static CustomSong CurrentSong()
        {
            if (_inCurrentSong) return null;
            _inCurrentSong = true;
            try
            {
                PlayerInfoManager playerInfo = PlayerInfoManager.GetInstance();
                return playerInfo == null ? null : SongById(playerInfo.CurrentMusicID);
            }
            catch { return null; }
            finally { _inCurrentSong = false; }
        }

        internal static CustomSong CurrentDrumSong()
        {
            CustomSong song = CurrentSong();
            if (song == null || _inCurrentSong) return null;
            _inCurrentSong = true;
            try { return PlayerInfoManager.GetInstance().CurrentPlayType == PlayType.Drum ? song : null; }
            catch { return null; }
            finally { _inCurrentSong = false; }
        }
    }
}
