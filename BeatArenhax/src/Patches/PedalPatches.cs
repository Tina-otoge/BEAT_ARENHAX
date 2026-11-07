using System;
using System.Collections.Generic;
using BeatArenhax.Audio;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using UnityEngine;

namespace BeatArenhax
{
    internal static class PedalPatches
    {
        private const long PedalMask = (1L << VirtualScreenLayout.LeftPedalKey) | (1L << VirtualScreenLayout.RightPedalKey);
        private static PedalMidiInput _midi;
        private static float _nextDevices;
        private static DrumPlayer _player;
        private static readonly List<GameObject> Notes = [];
        private static GameObject[] _targets;
        private static Mesh _mesh;
        private static Material[] _materials;
        private static readonly float[] Flashed = [-1f, -1f];
        private static bool _drawFailed;

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.LeftPedal.Enabled && !Plugin.Settings.RightPedal.Enabled) return;
            Patches.Patch(h, typeof(PedalPatches), typeof(MidiSequenceReader), "onPostLoad", nameof(Reader_Pre));
            Patches.Patch(h, typeof(PedalPatches), typeof(SequencePlayer), "LateUpdate", nameof(Input_Pre));
            Patches.Patch(h, typeof(PedalPatches), typeof(SequencePlayer), "processMistouch", nameof(Mistouch_Pre));
            Patches.Patch(h, typeof(PedalPatches), typeof(HitEffect), nameof(HitEffect.OnJudged), nameof(Judged_Pre));
            Patches.Patch(h, typeof(PedalPatches), typeof(DrumPlayer), "DrawNotes", null, nameof(Draw_Post));
            if (Plugin.Settings.LeftPedal.MIDI >= 0 || Plugin.Settings.RightPedal.MIDI >= 0)
                _midi = new PedalMidiInput(message => Plugin.L.LogWarning(message));
            Plugin.L.LogInfo("Custom-song pedals enabled; left=LP/left bass, right=bass drum.");
        }

        private static bool Owns(SequencePlayer sequence) =>
            sequence != null && sequence.m_seqReader != null && sequence.m_seqReader.Type == PlayType.Drum
            && sequence.m_seqReader.HasInitiative && Patches.CurrentDrumSong() != null;

        private static void Reader_Pre(MidiSequenceReader __instance)
        {
            if (__instance.Type != PlayType.Drum || !__instance.HasInitiative || Patches.CurrentDrumSong() == null) return;
            int left = 0, right = 0;
            CustomSong song = Patches.CurrentDrumSong();
            ConvertedCustomChart chart = song.GetConvertedChart(Plugin.Settings, Plugin.Audio.DurationMs, CustomScorePatches.SelectedIndex(song));
            PedalNoteMap map = new(chart.Chart, chart.WavToNote);
            List<SequenceReader.NoteData> pedals = [];
            foreach (SequenceReader.NoteData note in __instance.Notes)
                if (note.IsAutoNote && !note.IsLinkNote && note.sound >= 0) pedals.Add(note);
            pedals.Sort((a, b) => a.beatCount.CompareTo(b.beatCount));
            foreach (SequenceReader.NoteData note in pedals)
            {
                if (!map.TryTake(note.sound, out int key))
                    throw new InvalidOperationException($"Unexpected automatic sound {note.sound} while restoring custom pedal lanes.");
                note.key = key;
                if (key == VirtualScreenLayout.LeftPedalKey)
                {
                    left++;
                }
                else
                {
                    right++;
                }
                note.length = 0;
                note.width = 1;
            }
            if (map.Remaining != 0)
                throw new InvalidOperationException($"The native reader omitted {map.Remaining} custom pedal notes.");
            Plugin.L.LogInfo($"Custom pedal notes loaded: left={left}, right={right}.");
        }

        private static void Input_Pre(SequencePlayer __instance)
        {
            if (!Owns(__instance) || !__instance.IsPlaying || InGameTimer.isPaused
                || SequencePlayer.IsAutoPlay || __instance.m_forceAutoPlay || __instance.IsReplay) return;
            Poll(__instance, Plugin.Settings.LeftPedal, VirtualScreenLayout.LeftPedalKey);
            Poll(__instance, Plugin.Settings.RightPedal, VirtualScreenLayout.RightPedalKey);
            if (_midi == null) return;
            while (_midi.TryRead(out int note, out double age))
            {
                int at = __instance.MusicCount - (int)Math.Round(age);
                if (note == Plugin.Settings.LeftPedal.MIDI) __instance.SetKeyDown(VirtualScreenLayout.LeftPedalKey, at);
                if (note == Plugin.Settings.RightPedal.MIDI) __instance.SetKeyDown(VirtualScreenLayout.RightPedalKey, at);
            }
        }

        private static void Poll(SequencePlayer sequence, PedalBinding binding, int lane)
        {
            if ((binding.Key >= 0 && Input.GetKeyDown((KeyCode)binding.Key))
                || (binding.Button >= 0 && Input.GetKeyDown((KeyCode)((int)KeyCode.JoystickButton0 + binding.Button))))
                sequence.SetKeyDown(lane);
        }

        private static void Mistouch_Pre(SequencePlayer __instance, ref long clicked)
        {
            if (Owns(__instance)) clicked &= ~PedalMask;
        }

        private static bool Judged_Pre(HitEffect __instance, SequencePlayer.Note note)
        {
            if (note == null || note.Key is not (VirtualScreenLayout.LeftPedalKey or VirtualScreenLayout.RightPedalKey))
                return true;
            DrumPlayer player = __instance.GetComponent<DrumPlayer>();
            if (player == null || !Owns(player.m_seqPlayer)) return true;
            if (note.JudgeKind >= SequencePlayer.JUDGE_KIND.PERFECT && note.JudgeKind <= SequencePlayer.JUDGE_KIND.BAD)
                Flashed[note.Key - VirtualScreenLayout.LeftPedalKey] = Time.time;
            // HitEffect has only eight drum parents; scoring/judgment callbacks still run normally.
            return false;
        }

        internal static void Update()
        {
            if (_midi != null)
            {
                if (Time.realtimeSinceStartup >= _nextDevices)
                {
                    _nextDevices = Time.realtimeSinceStartup + 1f;
                    try { _midi.RefreshDevices(); }
                    catch (Exception e)
                    {
                        Plugin.L.LogError("MIDI pedal device initialization failed: " + e);
                        _midi.Dispose();
                        _midi = null;
                    }
                }
                if (_player == null || _player.m_seqPlayer == null || !_player.m_seqPlayer.IsPlaying
                    || InGameTimer.isPaused || SequencePlayer.IsAutoPlay || _player.m_seqPlayer.m_forceAutoPlay
                    || _player.m_seqPlayer.IsReplay)
                    while (_midi.TryRead(out _, out _)) { }
            }
            if (_player != null && _player.m_seqPlayer != null && _player.m_seqPlayer.IsPlaying) return;
            Reset();
        }

        [HarmonyPriority(Priority.First)]
        private static void Draw_Post(DrumPlayer __instance)
        {
            SequencePlayer sequence = __instance.m_seqPlayer;
            if (_drawFailed || !Owns(sequence) || !sequence.IsPlaying) return;
            try
            {
                if (_player != __instance)
                {
                    Reset();
                    _player = __instance;
                }
                EnsureObjects();
                for (int side = 0; side < 2; side++)
                {
                    bool enabled = side == 0 ? Plugin.Settings.LeftPedal.Enabled : Plugin.Settings.RightPedal.Enabled;
                    _targets[side].SetActive(enabled);
                    Position(_targets[side], side, 0f);
                    _targets[side].transform.localScale *= Time.time - Flashed[side] < 0.15f ? 1.2f : 1f;
                }
                PlayerInfoManager info = PlayerInfoManager.GetInstance();
                float speed = info.ScrollSpeed;
                int used = 0;
                foreach (SequencePlayer.Note note in sequence.m_playableNotes)
                {
                    if (note.Key is not (VirtualScreenLayout.LeftPedalKey or VirtualScreenLayout.RightPedalKey)
                        || note.IsJudged || note.IsAutoNote) continue;
                    float t = Math.Max(0, note.MusicCount - sequence.RenderMusicCount) * speed;
                    if (t > info.ScrollMax) continue;
                    if (used == Notes.Count) Notes.Add(CreateFoot("PedalNote"));
                    GameObject view = Notes[used++];
                    int side = note.Key - VirtualScreenLayout.LeftPedalKey;
                    view.GetComponent<MeshRenderer>().sharedMaterial = _materials[side];
                    Position(view, side, t);
                    view.SetActive(true);
                }
                for (int i = used; i < Notes.Count; i++) Notes[i].SetActive(false);
            }
            catch (Exception e)
            {
                _drawFailed = true;
                Reset();
                Plugin.L.LogError("3D pedal rendering failed: " + e);
            }
        }

        private static void Position(GameObject view, int side, float t)
        {
            int lane = side == 0 ? 2 : 3;
            view.transform.SetParent(_player.trParentList[lane].transform, false);
            _player.setPosition(view, lane, t);
            view.transform.localPosition += new Vector3(side == 0 ? -0.12f : 0.12f, -0.55f, 0f);
            view.transform.localScale = new Vector3(side == 0 ? 1f : -1f, 1f, 1f);
        }

        internal static void VisitViews(DrumPlayer player, Action<GameObject> visit)
        {
            if (_player != player) return;
            foreach (GameObject view in Notes) visit(view);
            if (_targets != null) foreach (GameObject target in _targets) visit(target);
        }

        private static void EnsureObjects()
        {
            if (_targets != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Pedal unlit shader is unavailable.");
            _mesh = CreateMesh();
            _materials = [new Material(shader), new Material(shader)];
            _materials[0].SetColor("_BaseColor", Color.red);
            _materials[1].SetColor("_BaseColor", new Color(0.65f, 0.25f, 1f, 1f));
            _targets = [CreateFoot("LeftPedalTarget"), CreateFoot("RightPedalTarget")];
            for (int side = 0; side < 2; side++)
                _targets[side].GetComponent<MeshRenderer>().sharedMaterial = _materials[side];
        }

        private static GameObject CreateFoot(string name)
        {
            GameObject view = new("BeatArenhax_" + name);
            view.AddComponent<MeshFilter>().sharedMesh = _mesh;
            view.AddComponent<MeshRenderer>();
            return view;
        }

        private static Mesh CreateMesh()
        {
            // Original embedded footprint and toes, extruded into a solid note (no skin file dependency).
            List<Vector3> vertices = [];
            List<int> triangles = [];
            AddSolid(
            [
                new(-0.06f, -0.16f), new(0.045f, -0.16f), new(0.055f, -0.07f),
                new(0.075f, 0.045f), new(0.06f, 0.09f), new(0.025f, 0.11f),
                new(-0.03f, 0.11f), new(-0.075f, 0.085f), new(-0.08f, 0.04f),
                new(-0.055f, -0.065f),
            ]);
            (float x, float y, float radius)[] toes =
            {
                (-0.053f, 0.15f, 0.025f), (-0.008f, 0.148f, 0.018f),
                (0.028f, 0.14f, 0.016f), (0.055f, 0.122f, 0.012f), (0.073f, 0.101f, 0.009f),
            };
            foreach (var toe in toes)
            {
                Vector2[] ring = new Vector2[12];
                for (int i = 0; i < ring.Length; i++)
                {
                    float angle = i * Mathf.PI * 2 / ring.Length;
                    ring[i] = new Vector2(toe.x + Mathf.Cos(angle) * toe.radius,
                        toe.y + Mathf.Sin(angle) * toe.radius);
                }
                AddSolid(ring);
            }
            Mesh mesh = new() { name = "BeatArenhax_EmbeddedFoot", vertices = vertices.ToArray(), triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;

            void AddSolid(Vector2[] outline)
            {
                int n = outline.Length, offset = vertices.Count;
                Vector2 center = Vector2.zero;
                foreach (Vector2 point in outline) center += point;
                center /= n;
                for (int face = 0; face < 2; face++)
                {
                    int start = offset + face * (n + 1);
                    float z = face == 0 ? -0.018f : 0.018f;
                    vertices.Add(new Vector3(center.x, center.y, z));
                    for (int i = 0; i < n; i++)
                    {
                        vertices.Add(new Vector3(outline[i].x, outline[i].y, z));
                        int next = ((i + 1) % n) + 1;
                        if (face == 0) triangles.AddRange([start, start + next, start + i + 1]);
                        else triangles.AddRange([start, start + i + 1, start + next]);
                    }
                }
                for (int i = 0; i < n; i++)
                {
                    int a = offset + i + 1, b = offset + ((i + 1) % n) + 1, c = a + n + 1, d = b + n + 1;
                    triangles.AddRange([a, b, c, b, d, c]);
                }
            }
        }

        internal static void Reset()
        {
            foreach (GameObject view in Notes) if (view != null) UnityEngine.Object.Destroy(view);
            Notes.Clear();
            if (_targets != null) foreach (GameObject target in _targets) if (target != null) UnityEngine.Object.Destroy(target);
            if (_materials != null) foreach (Material material in _materials) if (material != null) UnityEngine.Object.Destroy(material);
            if (_mesh != null) UnityEngine.Object.Destroy(_mesh);
            _targets = null;
            _materials = null;
            _mesh = null;
            _player = null;
            Flashed[0] = Flashed[1] = -1f;
        }

        internal static void Shutdown()
        {
            Reset();
            _midi?.Dispose();
            _midi = null;
        }
    }
}
