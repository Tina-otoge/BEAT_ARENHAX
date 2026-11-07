using System;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    internal static class GrabWithoutWaitPatches
    {
        [ThreadStatic] private static IntPtr _grabTouch;
        [ThreadStatic] private static bool _muteVibration;

        internal static void Apply(Harmony h)
        {
            if (Plugin.Settings.GrabWithoutWait)
            {
                Patches.Patch(h, typeof(GrabWithoutWaitPatches), typeof(ButtonCollider), "OnTriggerEnter",
                    nameof(OnTriggerEnter_Pre), nameof(OnTriggerEnter_Post), nameof(OnTriggerEnter_Finalizer));
                Patches.Patch(h, typeof(GrabWithoutWaitPatches), typeof(TouchIndicator), nameof(TouchIndicator.TouchStart),
                    nameof(TouchStart_Pre));
            }
            if (Plugin.Settings.GrabWithoutVibration)
            {
                Patches.Patch(h, typeof(GrabWithoutWaitPatches), typeof(ButtonCollider), "Update",
                    nameof(ButtonUpdate_Pre), finalizer: nameof(ButtonUpdate_Finalizer));
                try
                {
                    // StartVibration also has a GuitarHand overload; only button haptics use Hand.
                    var target = AccessTools.Method(typeof(BInput), nameof(BInput.StartVibration),
                        [typeof(BInput.Hand), typeof(BInput.Vibration), typeof(float)])
                        ?? throw new MissingMethodException(nameof(BInput), nameof(BInput.StartVibration));
                    _ = h.Patch(target, prefix: new HarmonyMethod(typeof(GrabWithoutWaitPatches), nameof(StartVibration_Pre)));
                }
                catch (Exception e)
                {
                    Plugin.L.LogError("Patch BInput.StartVibration(Hand) failed: " + e.Message);
                }
            }
        }

        private static bool IsGrabEdge(ButtonCollider collider)
        {
            MusicInfoPlate plate = collider.onTouchID?.Target?.TryCast<MusicInfoPlate>();
            if (plate == null) return false;
            Il2CppSystem.Collections.Generic.List<ButtonCollider> edges = plate.m_grabCollider;
            if (edges == null) return false;
            for (int i = 0; i < edges.Count; i++)
            {
                ButtonCollider edge = edges[i];
                if (edge != null && edge.Pointer == collider.Pointer) return true;
            }
            return false;
        }

        private static void OnTriggerEnter_Pre(ButtonCollider __instance, out IntPtr __state)
        {
            __state = _grabTouch;
            _grabTouch = IntPtr.Zero;
            try
            {
                if (Plugin.Settings.GrabWithoutWait && IsGrabEdge(__instance)) _grabTouch = __instance.Pointer;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Identifying info-panel grab edge failed: " + e.Message);
            }
        }

        private static bool TouchStart_Pre() => _grabTouch == IntPtr.Zero;

        private static void OnTriggerEnter_Post(ButtonCollider __instance, Collider other)
        {
            if (_grabTouch != __instance.Pointer) return;
            try
            {
                // Only complete a touch accepted by the game's hand, active-state and area checks.
                Collider hand = __instance.m_Hand;
                if (!__instance.m_touchMyCollider || !__instance.isStartTouch
                    || hand == null || other == null || hand.Pointer != other.Pointer) return;
                // Update uses a strict > 0.3 s test; reuse its callbacks, haptics and timer cleanup.
                __instance.touchTimer = 0.300001f;
                __instance.Update();
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Immediate info-panel grab activation failed: " + e.Message);
            }
        }

        private static void OnTriggerEnter_Finalizer(IntPtr __state) => _grabTouch = __state;

        private static void ButtonUpdate_Pre(ButtonCollider __instance, out bool __state)
        {
            __state = _muteVibration;
            _muteVibration = false;
            try
            {
                if (Plugin.Settings.GrabWithoutVibration && __instance.isStartTouch && IsGrabEdge(__instance))
                    _muteVibration = true;
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Identifying info-panel vibration scope failed: " + e.Message);
            }
        }

        private static bool StartVibration_Pre() => !_muteVibration;

        private static void ButtonUpdate_Finalizer(bool __state) => _muteVibration = __state;
    }
}
