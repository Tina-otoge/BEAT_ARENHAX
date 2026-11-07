using System;
using System.Collections.Generic;
using CRI.Adx2;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    /// <summary>
    /// Touch behaviour of the song wheel plates:
    /// - a longer touch before the song is selected (vanilla 0.3 s, too quick to listen to previews in VR);
    /// - start a song's preview as soon as its plate is touched, instead of after the touch timer selected the
    ///   song and the info plate opened. The info plate then takes over the playing preview.
    /// </summary>
    internal static class SongWheelTouchPatches
    {
        // Song wheel plate touch colliders (ButtonCollider) -> plate.
        private static readonly Dictionary<IntPtr, MusicPlateBase> Plates = [];

        private const float VanillaSelectDelay = 0.3f;

        private static int _musicId;
        private static string _cue;
        private static PlayInfo _info;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(SongWheelTouchPatches), typeof(MusicSelect), "ReadyMusicPlateList", nameof(ReadyMusicPlateList_Pre));
            Patches.Patch(h, typeof(SongWheelTouchPatches), typeof(MusicPlateBase), "Initialize", null, nameof(PlateInitialize_Post));
            Patches.Patch(h, typeof(SongWheelTouchPatches), typeof(ButtonCollider), "OnTriggerEnter", null, nameof(OnTriggerEnter_Post));
            Patches.Patch(h, typeof(SongWheelTouchPatches), typeof(ButtonCollider), "Update", nameof(ButtonColliderUpdate_Pre));
            Patches.Patch(h, typeof(SongWheelTouchPatches), typeof(RoundMenu), nameof(RoundMenu.Close), nameof(Close_Pre));
        }

        // While touched (isStartTouch), Update adds the frame time to touchTimer and selects the song once it
        // exceeds 0.3 s. Take back part of this frame's increment so the threshold is reached after the
        // configured delay instead.
        private static void ButtonColliderUpdate_Pre(ButtonCollider __instance)
        {
            float delay = Plugin.Settings.SongWheelTouchSelectTimeMs / 1000f;
            if (delay <= VanillaSelectDelay || !__instance.isStartTouch || !Plates.ContainsKey(__instance.Pointer)) return;
            __instance.touchTimer -= Time.deltaTime * (1f - (VanillaSelectDelay / delay));
        }

        internal static bool Pending => _cue != null;
        internal static void StopForFilter()
        {
            Stop();
            foreach (MusicPlateBase plate in Plates.Values)
            {
                if (plate == null || plate.m_btnCollider == null) continue;
                ButtonCollider collider = plate.m_btnCollider;
                collider.StopAllCoroutines();
                collider.isStartTouch = false;
                collider.touchTimer = 0f;
                collider.SetTouchFlag(false);
            }
        }

        /// <summary>
        /// Called from the PlayBgm hook: when the info plate starts the preview of the touched song, hand it the
        /// preview that is already playing instead of restarting it.
        /// </summary>
        internal static bool TryHandOver(string cueName, out PlayInfo info)
        {
            info = null;
            if (_cue == null || cueName != _cue) return false;
            bool custom = _cue.StartsWith(AudioPatches.PreviewPrefix, StringComparison.Ordinal);
            info = _info;
            Clear();
            if (info == null) return false;
            // Custom previews loop in our mixer until stopped; game previews must still be playing.
            bool playing;
            try { playing = custom || SoundManager.GetInstance().IsPlaying(info); }
            catch { playing = false; }
            if (playing) Plugin.L.LogInfo($"Touch preview handed over: {cueName}");
            return playing;
        }

        /// <summary>Drops the pending preview without stopping it (scene change, custom audio reset).</summary>
        internal static void Forget() => Clear();

        private static void Clear()
        {
            _musicId = 0;
            _cue = null;
            _info = null;
        }

        private static void Stop()
        {
            string cue = _cue;
            PlayInfo info = _info;
            Clear();
            if (cue == null) return;
            try
            {
                if (cue.StartsWith(AudioPatches.PreviewPrefix, StringComparison.Ordinal)) Plugin.Audio?.StopPreview();
                else if (info != null)
                {
                    SoundManager sound = SoundManager.GetInstance();
                    if (sound != null) sound.Stop(info, -1, false);
                }
            }
            catch (Exception e) { Plugin.L.LogWarning("Stopping the touch preview failed: " + e.Message); }
        }

        private static void ReadyMusicPlateList_Pre() => Plates.Clear();

        private static void PlateInitialize_Post(MusicPlateBase __instance)
        {
            try
            {
                ButtonCollider collider = __instance.m_btnCollider;
                if (collider != null) Plates[collider.Pointer] = __instance;
            }
            catch { }
        }

        // OnTriggerEnter only sets m_touchMyCollider when the plate accepted the touch (touch active, a hand, in area);
        // the song is selected later, once ButtonCollider.Update's touch timer completes.
        private static void OnTriggerEnter_Post(ButtonCollider __instance)
        {
            if (!Plugin.Settings.SongWheelPlayPreviewByTouching) return;
            if (!Plates.TryGetValue(__instance.Pointer, out MusicPlateBase plate)) return;
            try
            {
                if (!__instance.m_touchMyCollider) return;
                int musicId = plate.m_musicID;
                if (musicId == _musicId) return;
                Play(musicId);
            }
            catch (Exception e) { Plugin.L.LogWarning("Touch preview failed: " + e.Message); }
        }

        private static void Play(int musicId)
        {
            MusicInfoManager manager = MusicInfoManager.GetInstance();
            MusicInfo music = manager != null ? manager.GetMusicInfoByID(musicId) : null;
            if (music == null) return;
            Stop();
            // Stop whatever preview the info plate may still be playing.
            MusicInfoPlate infoPlate = UnityEngine.Object.FindObjectOfType<MusicInfoPlate>();
            if (infoPlate != null) infoPlate.StopPreview();
            string cue = music.PreviewCueName;
            // Same call as MusicInfoPlate.PlayPreview; custom songs are routed to our mixer by the PlayBgm hook.
            SoundManager sound = SoundManager.GetInstance();
            PlayInfo info = sound != null ? sound.PlayBgm(cue, 1f, 0, 0, 0, 0f, null, null) : null;
            _musicId = musicId;
            _cue = cue;
            _info = info;
        }

        // Close is only used when leaving the wheel without picking a song (picking uses SelectClose).
        private static void Close_Pre(RoundMenu __instance)
        {
            if (Pending && SongSelectPatches.IsSongWheel(__instance)) Stop();
        }
    }
}
