using System;
using System.Collections.Generic;
using System.IO;
using BeatArenhax.CustomSongs;
using BeatArenhax.Images;
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
            Patches.Patch(h, typeof(SongSelectPatches), typeof(MusicSelect), "ReadyMusicPlateList", nameof(ReadyMusicPlateList_Pre), null, nameof(ReadyMusicPlateList_Finalizer));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(RoundMenu), nameof(RoundMenu.SetObjectList), nameof(SetObjectList_Pre), nameof(SetObjectList_Post));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(RoundMenu), "SetPanelPosition", nameof(SetPanelPosition_Pre));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(RoundMenu), "UpdatePanelList", nameof(UpdatePanelList_Pre));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(RoundMenu), "ResetVerticalScrolling", null, nameof(ResetVerticalScrolling_Post));
            Patches.Patch(h, typeof(SongSelectPatches), typeof(RoundMenu), "CheckCenterDistance", nameof(CheckCenterDistance_Pre));
        }

        // RoundMenu is also used by the replay lists on the same screen; only the song wheel is changed.
        private static bool _buildingSongWheel;
        private static IntPtr _songWheel;

        private static void ReadyMusicPlateList_Pre() => _buildingSongWheel = true;
        private static void ReadyMusicPlateList_Finalizer() => _buildingSongWheel = false;

        private static void SetObjectList_Pre(RoundMenu __instance, Il2CppSystem.Collections.Generic.List<GameObject> objects, ref int lineSize, ref float panelHeight)
        {
            if (!_buildingSongWheel)
            {
                if (__instance.Pointer == _songWheel) _songWheel = IntPtr.Zero;
                return;
            }
            _songWheel = __instance.Pointer;
            // Vanilla packs every song on ~5 rows ((count + 4) / 5 columns), so extra songs go to extra rows instead.
            int count = objects?.Count ?? 0;
            int columns = Plugin.Settings.SongWheelColumns;
            float scale = Plugin.Settings.SongWheelRadiusScale;
            Plugin.L.LogInfo($"Song wheel: {count} songs, {columns} columns ({lineSize} vanilla), {(count + columns - 1) / columns} rows, radius x{scale}, distance x{Plugin.Settings.SongWheelDistanceScale}"
                + (Plugin.Settings.SongWheelForwardOnly ? $", {Plugin.Settings.SongWheelForwardArc} deg arc in front" : ""));
            lineSize = columns;
            // Rows spread out by the same factor so the wheel keeps its proportions.
            panelHeight *= scale;
        }

        private static readonly string[] HorizontalArrowNames = ["Image_Left", "Image_Left2", "Image_Right", "Image_Right2"];

        // Each plate carries a thumbstick hint with 4 arrows; rows can't scroll left/right on a forward-only wheel.
        private static void SetObjectList_Post(RoundMenu __instance, Il2CppSystem.Collections.Generic.List<GameObject> objects)
        {
            if (!Plugin.Settings.SongWheelForwardOnly || !IsSongWheel(__instance) || objects == null) return;
            try
            {
                int hidden = 0;
                for (int i = 0; i < objects.Count; i++)
                {
                    GameObject plate = objects[i];
                    Transform hint = plate != null ? plate.transform.Find("Canvas/Gripmessage_Group/Image_Upline/Text_Grip") : null;
                    if (hint == null) continue;
                    foreach (string name in HorizontalArrowNames)
                    {
                        Transform arrow = hint.Find(name);
                        if (arrow == null) continue;
                        arrow.gameObject.SetActive(false);
                        hidden++;
                    }
                }
                Plugin.L.LogInfo($"Song wheel: hid {hidden} left/right scroll arrows");
            }
            catch (Exception e) { Plugin.L.LogWarning("Hiding the song wheel's left/right arrows failed: " + e.Message); }
        }

        internal static bool IsSongWheel(RoundMenu menu) => _songWheel != IntPtr.Zero && menu.Pointer == _songWheel;

        internal static void RebuildSongWheel(RoundMenu menu, GameObject avatar, Il2CppSystem.Collections.Generic.List<GameObject> objects)
        {
            _savedRow = -1;
            _savedAngles = null;
            _buildingSongWheel = true;
            try { menu.SetObjectList(avatar, objects, Plugin.Settings.SongWheelColumns, 0.45f); }
            finally { _buildingSongWheel = false; }
            // Native scrolling always accesses three visible rows, even for an empty or small filter.
            while (menu.m_panelCenterList.Count < 3)
            {
                GameObject row = new("BeatArenhax_EmptyRow");
                row.transform.SetParent(menu.m_MenuCenterObject.transform, false);
                CanvasGroup group = row.AddComponent<CanvasGroup>();
                menu.m_panelCenterList.Add(row.transform);
                menu.m_panelCenterObjectList.Add(row);
                menu.m_panelCanvasGroups.Add(group);
                menu.m_panelCanvases.Add(new Il2CppSystem.Collections.Generic.List<Canvas>());
                menu.m_panelColliders.Add(new Il2CppSystem.Collections.Generic.List<Collider>());
                menu.m_panelCenterSpeedOfsts.Add(0f);
                menu.m_panelCenterAngle.Add(0f);
            }
            menu.m_panelLineSize = menu.m_panelCenterList.Count;
        }

        // Each plate sits on its row's ring: localPosition (sin a, 0, cos a) * dist, localRotation yaw a,
        // where vanilla uses a = column * 360 / columns and spins the row's parent to scroll left/right.
        private static bool SetPanelPosition_Pre(RoundMenu __instance, int line, int column, ref float dist)
        {
            if (!IsSongWheel(__instance)) return true;
            dist *= Plugin.Settings.SongWheelRadiusScale * Plugin.Settings.SongWheelDistanceScale;
            if (!Plugin.Settings.SongWheelForwardOnly) return true;
            try
            {
                int columns = __instance.m_panelColumnSize;
                // Vanilla also refreshes this on every call; other RoundMenu code reads it.
                __instance.m_panelDeltaRad = __instance.m_panelDeltaAngle / 360f * Mathf.PI * 2f;
                Il2CppSystem.Collections.Generic.List<GameObject> panels = __instance.m_panelObjects;
                int index = (line * columns) + column;
                if (panels == null || columns <= 0 || index < 0 || index >= panels.Count) return false;
                GameObject panel = panels[index];
                if (panel == null) return false;
                // Centered arc in front of where the wheel was opened, one equal slot per column.
                float degrees = (column - ((columns - 1) / 2f)) * (Plugin.Settings.SongWheelForwardArc / columns);
                float radians = degrees * Mathf.Deg2Rad;
                Transform t = panel.transform;
                t.localPosition = new Vector3(Mathf.Sin(radians) * dist, 0f, Mathf.Cos(radians) * dist);
                t.localRotation = Quaternion.Euler(0f, degrees, 0f);
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Song wheel layout failed: " + e.Message);
                return true;
            }
            return false;
        }

        // Rows are spun left/right by integrating these per-row speeds into angles; keep the arc facing forward.
        private static void UpdatePanelList_Pre(RoundMenu __instance)
        {
            if (!IsSongWheel(__instance)) return;
            if (Plugin.Settings.SongWheelRememberPosition) SavePosition(__instance);
            if (!Plugin.Settings.SongWheelForwardOnly) return;
            try
            {
                Il2CppSystem.Collections.Generic.List<float> speeds = __instance.m_panelCenterSpeedOfsts;
                Il2CppSystem.Collections.Generic.List<float> angles = __instance.m_panelCenterAngle;
                if (speeds != null) for (int i = 0; i < speeds.Count; i++) speeds[i] = 0f;
                if (angles != null) for (int i = 0; i < angles.Count; i++) angles[i] = 0f;
            }
            catch (Exception e) { Plugin.L.LogWarning("Song wheel row lock failed: " + e.Message); }
        }

        // Song wheel position kept across wheel openings and scene loads (not saved to disk).
        private static int _savedRow = -1;
        private static float[] _savedAngles;

        private static void SavePosition(RoundMenu menu)
        {
            try
            {
                if (menu.m_currentStep != RoundMenu.Step.Select) return;
                _savedRow = menu.m_rowCursor;
                Il2CppSystem.Collections.Generic.List<float> angles = menu.m_panelCenterAngle;
                if (angles == null || Plugin.Settings.SongWheelForwardOnly) return;
                if (_savedAngles == null || _savedAngles.Length != angles.Count) _savedAngles = new float[angles.Count];
                for (int i = 0; i < _savedAngles.Length; i++) _savedAngles[i] = angles[i];
            }
            catch { }
        }

        // ResetVerticalScrolling runs every time the wheel opens: rows 0-2 shown at y = 0, -h, -2h, the other rows
        // hidden, cursor 0. Scrolling keeps the same layout around the cursor (row (cursor + i) % rows at -h * i),
        // so the remembered row is restored by redoing it around that row.
        private static void ResetVerticalScrolling_Post(RoundMenu __instance)
        {
            if (!Plugin.Settings.SongWheelRememberPosition || !IsSongWheel(__instance)) return;
            try
            {
                int rows = __instance.m_panelLineSize;
                if (_savedAngles != null && !Plugin.Settings.SongWheelForwardOnly)
                {
                    Il2CppSystem.Collections.Generic.List<float> angles = __instance.m_panelCenterAngle;
                    if (angles != null && angles.Count == _savedAngles.Length)
                        for (int i = 0; i < _savedAngles.Length; i++) angles[i] = _savedAngles[i];
                }
                if (_savedRow <= 0 || rows < 3) return;
                int cursor = Math.Min(_savedRow, rows - 1);
                float height = __instance.m_panelHeight;
                Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<Canvas>> canvases = __instance.m_panelCanvases;
                Il2CppSystem.Collections.Generic.List<CanvasGroup> groups = __instance.m_panelCanvasGroups;
                Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.List<Collider>> colliders = __instance.m_panelColliders;
                for (int row = 0; row < rows; row++)
                {
                    int slot = (row - cursor + rows) % rows;
                    bool visible = slot < 3;
                    if (canvases != null && row < canvases.Count)
                        foreach (Canvas canvas in canvases[row]) canvas.enabled = visible;
                    if (colliders != null && row < colliders.Count)
                        foreach (Collider collider in colliders[row]) collider.enabled = visible;
                    if (groups == null || row >= groups.Count) continue;
                    CanvasGroup group = groups[row];
                    group.alpha = visible ? 1f : 0f;
                    if (visible) group.transform.localPosition = new Vector3(0f, -height * slot, 0f);
                }
                __instance.m_rowCursor = cursor;
                Plugin.L.LogInfo($"Song wheel: restored row {cursor + 1}/{rows}");
            }
            catch (Exception e) { Plugin.L.LogWarning("Restoring the song wheel position failed: " + e.Message); }
        }

        private const float VanillaWheelRadius = 0.7f;
        private const float VanillaLeaveDistance = 0.4f;

        // Songs are picked by touching their plate, and the wheel closes once the head moves more than 0.4 m
        // (horizontally) from where it was opened. Keep the vanilla margin between that limit and the plates,
        // so they stay reachable when the wheel is bigger.
        private static bool CheckCenterDistance_Pre(RoundMenu __instance)
        {
            if (!IsSongWheel(__instance)) return true;
            float radius = VanillaWheelRadius * Plugin.Settings.SongWheelRadiusScale * Plugin.Settings.SongWheelDistanceScale;
            float limit = Math.Max(VanillaLeaveDistance, radius - (VanillaWheelRadius - VanillaLeaveDistance));
            try
            {
                GameObject center = __instance.m_MenuCenterObject;
                Transform head = __instance.m_headTransform;
                if (center == null || head == null) return false;
                Vector3 c = center.transform.position;
                Vector3 p = head.position;
                float dx = p.x - c.x, dz = p.z - c.z;
                if ((dx * dx) + (dz * dz) > limit * limit) __instance.leaveBorderOver?.Invoke();
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Song wheel distance check failed: " + e.Message);
                return true;
            }
            return false;
        }

        private static void PreviewCueName_Post(MusicInfo __instance, ref string __result)
        {
            try
            {
                if (Patches.SongById(__instance.Id) != null) __result = AudioPatches.PreviewCue(__instance.Id);
            }
            catch { }
        }

        // MusicInfoPlate.PlayPreview calls StopPreview right before starting the preview of the touched song,
        // which must keep playing so it can be handed over.
        private static void StopPreview_Post()
        {
            if (!SongWheelTouchPatches.Pending) Plugin.Audio?.StopPreview();
        }

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

        internal static Sprite LoadSprite(CustomSong song, Sprite template)
        {
            if (Sprites.TryGetValue(song.Id, out Sprite cached) && cached != null) return cached;
            if (FailedSprites.Contains(song.Id) || song.PreImagePath == null) return null;
            try
            {
                Texture2D texture = CreateTexture(song);
                if (texture == null) { _ = FailedSprites.Add(song.Id); return null; }
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

        private static Texture2D CreateTexture(CustomSong song)
        {
            JacketPreloader.Pixels pixels = Plugin.Settings.BackgroundJacketDecoding ? JacketPreloader.Take(song) : null;
            if (pixels != null)
            {
                Texture2D raw = new(pixels.Width, pixels.Height, TextureFormat.RGBA32, false);
                unsafe
                {
                    fixed (byte* p = pixels.Rgba) raw.LoadRawTextureData((IntPtr)p, pixels.Rgba.Length);
                }
                raw.Apply(false, true);
                return raw;
            }
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            if (ImageConversion.LoadImage(texture, File.ReadAllBytes(song.PreImagePath), true)) return texture;
            UnityEngine.Object.Destroy(texture);
            return null;
        }
    }
}
