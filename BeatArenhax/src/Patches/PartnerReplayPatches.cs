using System;
using System.Collections.Generic;
using BeatArenhax.CustomSongs;
using HarmonyLib;

namespace BeatArenhax
{
    internal static class PartnerReplayPatches
    {
        private sealed class ReplayState
        {
            internal bool IsTemplate;
            internal int PreviousMusicTime = -1;
            internal int PreviousAutoTime = -1;
        }

        private static readonly Dictionary<IntPtr, ReplayState> States = [];
        [ThreadStatic] private static IntPtr _activeReplay;
        [ThreadStatic] private static int _period;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(PartnerReplayPatches), typeof(AvatarReplay), "Update",
                nameof(Update_Pre), nameof(Update_Post), nameof(Update_Finalizer));
            Patches.Patch(h, typeof(PartnerReplayPatches), typeof(AvatarReplay), "findIndex", nameof(FindIndex_Pre));
            Patches.Patch(h, typeof(PartnerReplayPatches), typeof(AvatarReplay), nameof(AvatarReplay.EndReplay), nameof(EndReplay_Pre));
        }

        private static void Update_Pre(AvatarReplay __instance, out int __state)
        {
            __state = 0;
            if (!__instance.isPlaying || __instance.isLerping || Patches.CurrentDrumSong() == null) return;
            if (__instance.m_gameMain == null || __instance.m_seqPlayer == null || __instance.trackingData == null) return;
            SequencePlayer leader = __instance.m_gameMain.GetComponent<SequencePlayer>();
            if (leader == null || leader.m_state != SequencePlayer.State.PLAYING) return;

            int count = __instance.trackingData.Count;
            if (count < 2) return;
            int period = ReplayLoopTiming.Period(__instance.trackingData[count - 1].Time);
            if (period == 0) return;

            if (!States.TryGetValue(__instance.Pointer, out ReplayState state))
            {
                int replayId = __instance.ReplayId;
                GameInfoManager gameInfo = GameInfoManager.GetInstance();
                PlayerInfoManager playerInfo = PlayerInfoManager.GetInstance();
                bool isTemplate = false;
                if (replayId != 0 && gameInfo != null && playerInfo != null)
                {
                    for (int slot = 0; slot < 3; slot++)
                        if (gameInfo.GetDefaultReplayID(playerInfo.CurrentMusicID, slot) == replayId) isTemplate = true;
                }
                States[__instance.Pointer] = state = new ReplayState { IsTemplate = isTemplate };
            }
            if (!state.IsTemplate) return;

            int musicTime = leader.MusicCount;
            int autoTime = leader.AutoMusicCount;
            int wrappedMusic = ReplayLoopTiming.Wrap(musicTime, period);
            int wrappedAuto = ReplayLoopTiming.Wrap(autoTime, period);
            bool musicReset = ReplayLoopTiming.NeedsCursorReset(state.PreviousMusicTime, musicTime, period);
            bool autoReset = ReplayLoopTiming.NeedsCursorReset(state.PreviousAutoTime, autoTime, period);
            if (musicTime >= period && (musicReset || (__instance.lastIndex >= 0 && __instance.lastIndex < count
                && __instance.trackingData[__instance.lastIndex].Time > wrappedMusic))) __instance.lastIndex = 0;
            if (autoTime >= period && (autoReset || (__instance.lastAutoIndex >= 0 && __instance.lastAutoIndex < count
                && __instance.trackingData[__instance.lastAutoIndex].Time > wrappedAuto))) __instance.lastAutoIndex = 0;

            if (autoReset)
            {
                __instance.m_seqPlayer.SetPlayCursor(wrappedAuto, true);
                Plugin.L.LogInfo($"Looping template partner replay {__instance.ReplayId} at {autoTime} ms");
            }
            state.PreviousMusicTime = musicTime;
            state.PreviousAutoTime = autoTime;
            if (musicTime < period && autoTime < period) return;
            _activeReplay = __instance.Pointer;
            _period = period;
            __state = period;
        }

        private static void FindIndex_Pre(AvatarReplay __instance, ref int musicCount)
        {
            if (__instance.Pointer == _activeReplay) musicCount = ReplayLoopTiming.Wrap(musicCount, _period);
        }

        private static void Update_Post(AvatarReplay __instance, int __state)
        {
            if (__state != 0)
                __instance.m_seqPlayer.OverwriteMusicCount = ReplayLoopTiming.Wrap(__instance.m_seqPlayer.OverwriteMusicCount, __state);
        }

        private static void Update_Finalizer()
        {
            _activeReplay = IntPtr.Zero;
            _period = 0;
        }

        private static void EndReplay_Pre(AvatarReplay __instance) => States.Remove(__instance.Pointer);
    }
}
