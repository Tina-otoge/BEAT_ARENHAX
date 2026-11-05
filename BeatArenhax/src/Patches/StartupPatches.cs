using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using CRI.Adx2;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BeatArenhax
{
    internal static class StartupPatches
    {
        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(StartupPatches), typeof(Bvr.Title), "SelectOfflineOrBackToStartUp", nameof(SelectOffline_Pre));
            Patches.Patch(h, typeof(StartupPatches), typeof(CampaignRecordManager), nameof(CampaignRecordManager.GetUnlockRewardList), nameof(GetUnlockRewardList_Pre));
            Patches.Patch(h, typeof(StartupPatches), typeof(PlayerInfoManager), "get_PlayerName", null, nameof(PlayerName_Post));
            Patches.Patch(h, typeof(StartupPatches), typeof(Bvr.StartUp), "Start", nameof(StartUpStart_Pre));
            Patches.Patch(h, typeof(StartupPatches), typeof(Bvr.Title), "VrWarning", nameof(VrWarning_Pre));

            try
            {
                Type iterator = typeof(Bvr.Player).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(type => type.Name.Contains("PostGameTime"));
                MethodInfo moveNext = (iterator == null ? null : AccessTools.Method(iterator, "MoveNext")) ?? throw new MissingMethodException("Bvr.Player", "PostGameTime.MoveNext");
                _ = h.Patch(moveNext, new HarmonyMethod(typeof(StartupPatches), nameof(PostGameTime_MoveNext_Pre)));
            }
            catch (Exception e) { Plugin.L.LogError("Patch PostGameTime failed: " + e.Message); }

            try
            {
                Type iterator = typeof(Bvr.Player).GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(type => type.Name.Contains("PreGameTime"));
                MethodInfo moveNext = (iterator == null ? null : AccessTools.Method(iterator, "MoveNext")) ?? throw new MissingMethodException("Bvr.Player", "PreGameTime.MoveNext");
                _ = h.Patch(moveNext, new HarmonyMethod(typeof(StartupPatches), nameof(PreGameTime_MoveNext_Pre)));
            }
            catch (Exception e) { Plugin.L.LogError("Patch PreGameTime failed: " + e.Message); }
        }

        private static unsafe void PreGameTime_MoveNext_Pre(object __instance)
        {
            if (!(Plugin.Settings.SkipPreGameGesture || Patches.DebugAutoplayActive)) return;
            if (__instance is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) return;
            byte* p = (byte*)o.Pointer;
            if (*(int*)(p + 0x10) != 1) return;
            IntPtr playerPtr = *(IntPtr*)(p + 0x20);
            if (playerPtr == IntPtr.Zero) return;
            try
            {
                Il2CppReferenceArray<GameObject> members = new Bvr.Player(playerPtr).m_members;
                if (members == null) return;
                int marked = 0;
                foreach (GameObject go in members)
                {
                    if (go == null) continue;
                    Bvr.Npc npc = go.GetComponent<Bvr.Npc>();
                    if (npc == null || npc.m_isReady) continue;
                    npc.m_nextState = Bvr.Npc.State.IDLE;
                    npc.m_isReady = true;
                    marked++;
                }
                if (marked > 0) Plugin.L.LogInfo($"Skipping the pre-song ready gesture ({marked} band members)");
            }
            catch (Exception e) { Plugin.L.LogWarning("Pre-song gesture skip failed: " + e.Message); }
        }

        private static unsafe void PostGameTime_MoveNext_Pre(object __instance)
        {
            if (!Plugin.Settings.SkipPostGameGesture || __instance is not Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase o) return;
            byte* p = (byte*)o.Pointer;
            int state = *(int*)(p + 0x10);
            if (state is 1 or 2)
            {
                if (*(float*)(p + 0x38) > 0f) Plugin.L.LogInfo("Skipping the post-song impression countdown");
                *(float*)(p + 0x38) = 0f;
            }
        }

        private static bool StartUpStart_Pre(ref Il2CppSystem.Collections.IEnumerator __result)
        {
            if (!Plugin.Settings.SkipTitleScreen) return true;
            Plugin.L.LogInfo("Skipping the logo intro");
            __result = SkipStartUp().WrapToIl2Cpp();
            return false;
        }

        private static IEnumerator SkipStartUp()
        {
            yield return null;
            float timeout = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < timeout)
            {
                bool loading;
                try { SoundManager soundManager = SoundManager.GetInstance(); loading = soundManager == null || soundManager.IsSheetLoading(); }
                catch { loading = false; }
                if (!loading) break;
                yield return null;
            }
            _ = Bvr.SubSceneManager.LoadScene(Bvr.SceneID.Title);
        }

        private static bool VrWarning_Pre(ref Il2CppSystem.Collections.IEnumerator __result)
        {
            if (!Plugin.Settings.SkipCautionScreen) return true;
            Plugin.L.LogInfo("Skipping the caution screen");
            __result = Done().WrapToIl2Cpp();
            return false;
        }

        private static IEnumerator Done()
        {
            yield break;
        }

        private static void PlayerName_Post(ref string __result)
        {
            string name = Plugin.Settings.PlayerName;
            if (!string.IsNullOrWhiteSpace(name)) __result = name.Trim();
        }

        private static bool GetUnlockRewardList_Pre(ref Il2CppSystem.Collections.Generic.List<int> __result)
        {
            if (!Plugin.Settings.UnlockCampaignRewards) return true;
            __result = CampaignInfoManager.GetInstance().GetUnlockRewardID(100000, false);
            return false;
        }

        internal static void ApplyUnlocks()
        {
            if (!Plugin.Settings.UnlockAllAvatarParts) return;
            try
            {
                GameInfoManager gameInfo = GameInfoManager.GetInstance();
                if (gameInfo != null && !gameInfo.IsServerAppendixUnlockAllParts)
                {
                    gameInfo.IsServerAppendixUnlockAllParts = true;
                    Plugin.L.LogInfo("All avatar parts unlocked");
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Unlock failed: " + e.Message); }
        }

        private static bool SelectOffline_Pre(Bvr.Title __instance, ref Il2CppSystem.Collections.IEnumerator __result)
        {
            if (!Plugin.Settings.AutoOfflineMode) return true;
            Plugin.L.LogInfo("Server unreachable: entering offline mode automatically");
            GameInfoManager.GetInstance().IsOnline = false;
            __result = __instance.NextScene();
            return false;
        }
    }
}
