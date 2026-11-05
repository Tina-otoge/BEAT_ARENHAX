using System;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    /// <summary>Hides buttons that only work with the (closed) online service.</summary>
    internal static class OfflineUIPatches
    {
        // MusicInfoPlate.SELECT_ITEM_PATHS[ButtonIndex.ReplaySearch]: "Members search" on the instrument / difficulty panel.
        private const string MembersSearchPath = "Canvas/Denmoku_Win/Win/PL_Win/Btn_Search";

        private static bool _loggedSearch;
        private static bool _loggedFollowing;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(OfflineUIPatches), typeof(MusicInfoPlate), nameof(MusicInfoPlate.Open), null, nameof(MusicInfoPlate_Post));
            Patches.Patch(h, typeof(OfflineUIPatches), typeof(MusicInfoPlate), "SetMusicInfo", null, nameof(MusicInfoPlate_Post));
            Patches.Patch(h, typeof(OfflineUIPatches), typeof(HomeSystemMenu), nameof(HomeSystemMenu.Open), nameof(HomeSystemMenuOpen_Pre));
        }

        private static bool Active
        {
            get
            {
                if (!Plugin.Settings.HideButtonsUnavailableOffline) return false;
                try
                {
                    GameInfoManager info = GameInfoManager.GetInstance();
                    return info != null && info.IsOffline;
                }
                catch { return false; }
            }
        }

        private static void MusicInfoPlate_Post(MusicInfoPlate __instance)
        {
            if (!Active) return;
            try
            {
                Transform button = FindMembersSearch(__instance);
                if (button == null || !button.gameObject.activeSelf) return;
                button.gameObject.SetActive(false);
                if (!_loggedSearch) { _loggedSearch = true; Plugin.L.LogInfo("Offline: hid the members search button"); }
            }
            catch (Exception e) { Plugin.L.LogWarning("Hiding the members search button failed: " + e.Message); }
        }

        // The plate's window is instantiated from an addressable prefab into m_Menu; the button paths are relative to it.
        private static Transform FindMembersSearch(MusicInfoPlate plate)
        {
            foreach (GameObject root in new[] { plate.m_Menu, plate.m_MenuParentObject, plate.gameObject })
            {
                Transform found = root != null ? root.transform.Find(MembersSearchPath) : null;
                if (found != null) return found;
            }
            return null;
        }

        // The menu opened with the left stick. Its items are hidden through UIListView's display flags,
        // which UIListView applies when it opens.
        private static void HomeSystemMenuOpen_Pre(HomeSystemMenu __instance)
        {
            if (!Active) return;
            try
            {
                UIListView list = __instance.m_SystemMenuView;
                if (list == null || list.m_isBtnsDisplay == null) return;
                list.SetButtonDisplay((int)HomeSystemMenu.Menu.Friend, false);
                if (!_loggedFollowing) { _loggedFollowing = true; Plugin.L.LogInfo("Offline: hid the following menu button"); }
            }
            catch (Exception e) { Plugin.L.LogWarning("Hiding the following menu button failed: " + e.Message); }
        }
    }
}
