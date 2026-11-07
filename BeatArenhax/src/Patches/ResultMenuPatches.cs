using System;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    internal static class ResultMenuPatches
    {
        private const float OffsetX = 0.6f;
        private const float OffsetY = -0.05f;
        private const float OffsetZ = 0f;
        private const float RotationX = 0f;
        private const float RotationY = 25f;
        private const float RotationZ = 0f;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(ResultMenuPatches), typeof(ResultMenu), "Start", nameof(Start_Pre));
        }

        private static void Start_Pre(ResultMenu __instance)
        {
            if (!Plugin.Settings.MoveResultMenuToTheSide) return;
            try
            {
                // Position the host once, before the animated menu and replay-save window are created.
                Transform root = __instance.transform;
                root.position += root.right * OffsetX + root.up * OffsetY + root.forward * OffsetZ;
                root.rotation *= Quaternion.Euler(RotationX, RotationY, RotationZ);
                Plugin.L.LogInfo($"Result menu offset ({OffsetX}, {OffsetY}, {OffsetZ}) m, rotation ({RotationX}, {RotationY}, {RotationZ}) degrees");
            }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Result menu side offset failed: " + e.Message);
            }
        }
    }
}
