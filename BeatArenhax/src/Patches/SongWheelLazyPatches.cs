using System;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    // Every song plate is a full UI hierarchy (canvases, TextMeshPro, animator). Vanilla instantiates them active,
    // so with a thousand custom songs, waking them all up made the song select scene take half a minute to load.
    // Plates are created inactive instead, and each row is only activated the first time it is shown.
    internal static class SongWheelLazyPatches
    {
        private static GameObject _prefab;
        private static bool _building;

        // Unity sends the UI parent/canvas change callbacks even to inactive graphics, and each costs ~0.1 ms;
        // a plate has ~60 of them and is reparented while building the wheel. Graphics that were never enabled
        // aren't registered anywhere yet, and OnEnable sets everything up when the plate is first shown.
        private static bool SkipInactiveUi(UnityEngine.EventSystems.UIBehaviour __instance) => !_building || __instance.isActiveAndEnabled;

        private static bool SkipInactiveGraphic(UnityEngine.UI.Graphic __instance)
        {
            if (!_building || __instance.isActiveAndEnabled) return true;
            __instance.m_Canvas = null;
            return false;
        }

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.LazySongWheel) return;
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(MusicSelect), "ReadyMusicPlateList", nameof(ReadyMusicPlateList_Pre), nameof(ReadyMusicPlateList_Post), nameof(ReadyMusicPlateList_Finalizer));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(RoundMenu), "ResetVerticalScrolling", null, nameof(ActivateShownRows));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(RoundMenu), "UpdatePanelList", null, nameof(ActivateShownRows));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(UnityEngine.UI.Graphic), "OnBeforeTransformParentChanged", nameof(SkipInactiveUi));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(UnityEngine.UI.MaskableGraphic), "OnTransformParentChanged", nameof(SkipInactiveGraphic));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(UnityEngine.UI.Image), "OnCanvasHierarchyChanged", nameof(SkipInactiveUi));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(TMPro.TextMeshProUGUI), "OnTransformParentChanged", nameof(SkipInactiveGraphic));
            Patches.Patch(h, typeof(SongWheelLazyPatches), typeof(TMPro.TextMeshProUGUI), "OnCanvasHierarchyChanged", nameof(SkipInactiveUi));
        }

        private static void ReadyMusicPlateList_Pre(MusicSelect __instance)
        {
            _building = true;
            try
            {
                GameObject prefab = __instance.m_musicPanelHandle.Result;
                if (prefab == null || !prefab.activeSelf) return;
                prefab.SetActive(false);
                _prefab = prefab;
            }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel lazy load: " + e.Message); }
        }

        private static void ReadyMusicPlateList_Post(MusicSelect __instance)
        {
            _building = false;
            if (_prefab == null) return;
            try
            {
                _prefab.SetActive(true);
                _prefab = null;
                PrepareRows(__instance.m_musicListMenu, __instance.m_musicListObjects);
            }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel lazy load: " + e.Message); }
        }

        internal static void Rebuild(Action rebuild)
        {
            bool previous = _building;
            _building = previous || Plugin.Settings.LazySongWheel;
            try { rebuild(); }
            finally { _building = previous; }
        }

        internal static void PrepareRows(RoundMenu menu, Il2CppSystem.Collections.Generic.List<GameObject> plates)
        {
            if (!Plugin.Settings.LazySongWheel) return;
            // SetObjectList left the wheel root inactive; plates inside inactive rows stay asleep.
            Il2CppSystem.Collections.Generic.List<GameObject> rows = menu != null ? menu.m_panelCenterObjectList : null;
            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    GameObject row = rows[i];
                    if (row != null) row.SetActive(false);
                }
            }
            if (plates != null)
            {
                for (int i = 0; i < plates.Count; i++)
                {
                    GameObject plate = plates[i];
                    if (plate != null) plate.SetActive(true);
                }
            }
        }

        // Runs even if ReadyMusicPlateList threw: the prefab is a loaded asset, so leaving it inactive
        // would break every later wheel build too.
        private static void ReadyMusicPlateList_Finalizer()
        {
            _building = false;
            if (_prefab == null) return;
            try { _prefab.SetActive(true); }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel lazy load: " + e.Message); }
            _prefab = null;
        }

        private static void ActivateShownRows(RoundMenu __instance)
        {
            if (!SongSelectPatches.IsSongWheel(__instance)) return;
            try
            {
                Il2CppSystem.Collections.Generic.List<GameObject> rows = __instance.m_panelCenterObjectList;
                Il2CppSystem.Collections.Generic.List<CanvasGroup> groups = __instance.m_panelCanvasGroups;
                if (rows == null || groups == null) return;
                int count = Math.Min(rows.Count, groups.Count);
                for (int i = 0; i < count; i++)
                {
                    GameObject row = rows[i];
                    if (row == null || row.activeSelf || groups[i].alpha <= 0f) continue;
                    row.SetActive(true);
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel lazy load: " + e.Message); }
        }
    }
}
