using System;
using System.Collections.Generic;
using UnityEngine;

namespace BeatArenhax
{
    internal static class StoolPatches
    {
        private sealed class Chair
        {
            internal Transform Transform;
            internal Vector3 Position;
            internal SkinnedMeshRenderer Skin;
            internal Transform[] Bones;
            internal readonly List<Transform> OffsetBones = [];
            internal Bounds Bounds;
        }

        private static readonly List<Chair> Chairs = [];
        private static DrumPlayer _player;
        private static float _nextSearch;
        private static bool _failed;

        internal static void Reset()
        {
            foreach (Chair chair in Chairs)
            {
                if (chair.Skin != null)
                {
                    chair.Skin.bones = chair.Bones;
                    chair.Skin.localBounds = chair.Bounds;
                }
                else if (chair.Transform != null)
                    chair.Transform.localPosition = chair.Position;
                foreach (Transform bone in chair.OffsetBones)
                    if (bone != null) UnityEngine.Object.Destroy(bone.gameObject);
            }
            Chairs.Clear();
            _player = null;
            _nextSearch = 0f;
            _failed = false;
        }

        internal static void Update()
        {
            if (_failed) return;
            if (Plugin.Settings.StoolHeight == 0f
                && Plugin.Settings.StoolOffsetX == 0f && Plugin.Settings.StoolOffsetZ == 0f)
            {
                if (Chairs.Count > 0) Reset();
                return;
            }
            try
            {
                if (_player == null && Time.realtimeSinceStartup >= _nextSearch)
                {
                    Reset();
                    _nextSearch = Time.realtimeSinceStartup + 1f;
                    DrumPlayer player = UnityEngine.Object.FindObjectOfType<DrumPlayer>();
                    if (player == null || player.Input == null) return;
                    foreach (Renderer renderer in player.Input.GetComponentsInChildren<Renderer>(true))
                    {
                        if (renderer.name is not "mesh_chair" and not "mesh_chair1") continue;
                        Chair chair = new() { Transform = renderer.transform, Position = renderer.transform.localPosition };
                        Chairs.Add(chair);
                        chair.Skin = renderer.TryCast<SkinnedMeshRenderer>();
                        if (chair.Skin == null) continue;
                        chair.Bounds = chair.Skin.localBounds;
                        chair.Bones = chair.Skin.bones;
                        foreach (Transform source in chair.Bones)
                        {
                            if (source == null) throw new InvalidOperationException("The vanilla stool has a missing skin bone.");
                            Transform bone = new GameObject("BeatArenhax_StoolHeight").transform;
                            chair.OffsetBones.Add(bone);
                            bone.SetParent(source, false);
                        }
                        chair.Skin.bones = chair.OffsetBones.ToArray();
                    }
                    if (Chairs.Count == 0)
                        throw new InvalidOperationException("No vanilla stool meshes were found under the local drum input.");
                    _player = player;
                    Plugin.L.LogInfo($"Vanilla stool: {Chairs.Count} meshes, offsets X={Plugin.Settings.StoolOffsetX:F3}, Y={Plugin.Settings.StoolHeight:F3}, Z={Plugin.Settings.StoolOffsetZ:F3} m.");
                }

                if (_player == null || _player.Input == null) return;
                Quaternion heading = Quaternion.Euler(0f, _player.Input.transform.eulerAngles.y, 0f);
                Vector3 offset = heading * new Vector3(Plugin.Settings.StoolOffsetX, Plugin.Settings.StoolHeight, Plugin.Settings.StoolOffsetZ);
                foreach (Chair chair in Chairs)
                {
                    if (chair.Transform == null) continue;
                    if (chair.Skin == null)
                    {
                        Transform parent = chair.Transform.parent;
                        chair.Transform.localPosition = chair.Position + (parent != null ? parent.InverseTransformVector(offset) : offset);
                        continue;
                    }
                    // Offset private skin bones so the shared drum bones and other instruments stay unchanged.
                    foreach (Transform bone in chair.OffsetBones)
                        if (bone != null) bone.localPosition = bone.parent.InverseTransformVector(offset);
                    Transform root = chair.Skin.rootBone;
                    Bounds bounds = chair.Bounds;
                    bounds.center += root != null ? root.InverseTransformVector(offset) : chair.Transform.InverseTransformVector(offset);
                    chair.Skin.localBounds = bounds;
                }
            }
            catch (Exception e)
            {
                Reset();
                _failed = true;
                Plugin.L.LogError("Vanilla stool position adjustment failed: " + e);
            }
        }
    }
}
