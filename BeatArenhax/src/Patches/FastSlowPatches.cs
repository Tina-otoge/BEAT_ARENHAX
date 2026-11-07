using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class FastSlowPatches
    {
        private const float Lifetime = 0.55f;
        private const float PixelScale = 0.001f;
        private static readonly Color FastColor = new(0.2f, 0.8f, 1f, 1f);
        private static readonly Color SlowColor = Color.red;
        private static readonly Color OutlineColor = Color.black;
        private static readonly List<Label> Labels = [];
        private static Font _font;

        private sealed class Label
        {
            internal DrumPlayer Player;
            internal Renderer GreenDrum;
            internal Renderer RedDrum;
            internal Transform Eye;
            internal GameObject Root;
            internal Text Text;
            internal float Started;
        }

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(FastSlowPatches), typeof(HitEffect), nameof(HitEffect.OnJudged), null, nameof(OnJudged_Post));
        }

        private static void OnJudged_Post(HitEffect __instance, SequencePlayer.Note note)
        {
            if (!Plugin.Settings.DisplayFastSlow || note == null || note.IsAutoNote || note.IsHolding
                || SequencePlayer.IsAutoPlay) return;
            SequencePlayer.JUDGE_KIND grade = note.JudgeKind;
            if (grade < SequencePlayer.JUDGE_KIND.PERFECT || grade > SequencePlayer.JUDGE_KIND.BAD
                || (grade == SequencePlayer.JUDGE_KIND.PERFECT && !Plugin.Settings.DisplayFastSlowOnPerfects)) return;
            try
            {
                DrumPlayer player = __instance.GetComponent<DrumPlayer>();
                if (player == null || player.m_seqPlayer == null || player.m_seqPlayer.m_forceAutoPlay) return;
                if (!VirtualScreenLayout.ShowFeedback(note.Key, note.IsAutoNote, note.IsBackTrack, Patches.CurrentDrumSong() != null)) return;
                Label label = Labels.Find(item => item.Player == player && item.Root != null);
                if (label == null)
                {
                    label = CreateLabel(player);
                    Labels.Add(label);
                }
                // JudgeDiff is JudgeCount - MusicCount; zero follows the game's SLOW convention.
                bool fast = note.JudgeDiff < 0;
                label.Text.text = fast ? "FAST" : "SLOW";
                label.Text.color = fast ? FastColor : SlowColor;
                label.Started = Time.time;
                PositionLabel(label);
                label.Root.SetActive(true);
            }
            catch (Exception e) { Plugin.L.LogError("Drum FAST/SLOW feedback: " + e); }
        }

        private static Label CreateLabel(DrumPlayer player)
        {
            DrumInput input = player.Input;
            if (input == null || input.drumObjects == null || input.drumObjects.Count <= 3)
                throw new InvalidOperationException("Central drums are not initialized.");
            // Input lanes 2/3 are the physical green high tom/red low tom, not animated note effects.
            GameObject green = input.drumObjects[2], red = input.drumObjects[3];
            Renderer greenDrum = green != null ? green.GetComponent<Renderer>() : null;
            Renderer redDrum = red != null ? red.GetComponent<Renderer>() : null;
            if (greenDrum == null || redDrum == null)
                throw new InvalidOperationException("Central drum renderers are unavailable.");
            GameObject avatar = GameObject.FindWithTag("Player");
            Transform eye = avatar != null
                ? avatar.transform.Find("OVRCameraRig/TrackingSpace/CameraEyeRoot/CenterEyeAnchor")
                : null;
            if (eye == null) throw new InvalidOperationException("Tracked center-eye camera is unavailable.");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (_font == null) throw new InvalidOperationException("Built-in Arial font is unavailable.");
            GameObject root = new("BeatArenhax_FastSlow");
            try
            {
                root.SetActive(false);
                RectTransform rect = root.AddComponent<RectTransform>();
                rect.SetParent(player.transform, false);
                Vector3 scale = player.transform.lossyScale;
                rect.localScale = new Vector3(PixelScale / scale.x, PixelScale / scale.y, PixelScale / scale.z);
                rect.sizeDelta = new Vector2(160f, 48f);
                Canvas canvas = root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Text text = root.AddComponent<Text>();
                text.font = _font;
                text.fontSize = 32;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.horizontalOverflow = HorizontalWrapMode.Overflow;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.supportRichText = false;
                text.raycastTarget = false;
                Outline outline = root.AddComponent<Outline>();
                outline.effectColor = OutlineColor;
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = true;
                return new Label
                {
                    Player = player, GreenDrum = greenDrum, RedDrum = redDrum,
                    Eye = eye, Root = root, Text = text,
                };
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                throw;
            }
        }

        internal static void Update()
        {
            for (int i = Labels.Count - 1; i >= 0; i--)
            {
                Label label = Labels[i];
                if (label.Player == null || label.Root == null)
                {
                    if (label.Root != null) UnityEngine.Object.Destroy(label.Root);
                    Labels.RemoveAt(i);
                    continue;
                }
                if (!label.Root.activeSelf) continue;
                float age = Time.time - label.Started;
                if (age >= Lifetime || label.GreenDrum == null || label.RedDrum == null || label.Eye == null)
                {
                    label.Root.SetActive(false);
                    continue;
                }
                try
                {
                    PositionLabel(label);
                    Color color = label.Text.color;
                    color.a = 1f - Mathf.Clamp01((age - 0.3f) / (Lifetime - 0.3f));
                    label.Text.color = color;
                }
                catch (Exception e)
                {
                    label.Root.SetActive(false);
                    Plugin.L.LogError("Updating drum FAST/SLOW feedback: " + e);
                }
            }
        }

        private static void PositionLabel(Label label)
        {
            Bounds green = label.GreenDrum.bounds, red = label.RedDrum.bounds;
            Vector3 position = (green.center + red.center) * 0.5f;
            position.y = Mathf.Max(green.max.y, red.max.y) + 0.15f;
            Transform transform = label.Root.transform;
            transform.position = position;
            Vector3 forward = position - label.Eye.position;
            if (forward.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }
    }
}
