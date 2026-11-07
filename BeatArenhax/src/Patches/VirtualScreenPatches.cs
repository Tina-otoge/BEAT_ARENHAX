using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class VirtualScreenPatches
    {
        private static readonly List<(Renderer renderer, bool enabled)> Hidden = [];
        private static readonly HashSet<int> HiddenIds = [];
        private static readonly Dictionary<int, Renderer[]> Renderers = [];
        private static readonly Dictionary<int, (Camera camera, bool included)> CameraMasks = [];
        private static Screen _screen;
        private static bool _failed;
        private static bool _spectatorRendering;
        private static float _nextLayerCheck;
        private static readonly Action<ScriptableRenderContext, Camera> BeginCamera = BeginCameraRendering;
        private static readonly Action<ScriptableRenderContext, Camera> EndCamera = EndCameraRendering;

        internal static int SpectatorMask(int mask) =>
            _screen != null && _screen.Layer >= 0 ? mask & ~(1 << _screen.Layer) : mask;

        private sealed class Screen
        {
            internal DrumPlayer Player;
            internal SequencePlayer Sequence;
            internal bool CustomSong;
            internal GameObject Root;
            internal Image Background;
            internal Renderer Green;
            internal Renderer Red;
            internal int Layer = -1;
            internal RectTransform Viewport;
            internal Texture2D Chips;
            internal Texture2D GrayChips;
            internal Texture2D Pads;
            internal Texture2D Fire;
            internal Texture2D Judgments;
            internal Sprite[] ChipSprites;
            internal Sprite[] GrayChipSprites;
            internal Sprite[] PadSprites;
            internal Sprite[,] Icons;
            internal Sprite[,] GrayIcons;
            internal Sprite[,] FireSprites;
            internal Sprite[,] JudgmentSprites;
            internal RectTransform Effects;
            internal readonly Image[] Fires = new Image[VirtualScreenLayout.LaneCount];
            internal readonly Image[] Judges = new Image[VirtualScreenLayout.LaneCount];
            internal readonly int[] FireStarted = new int[VirtualScreenLayout.LaneCount];
            internal readonly int[] JudgeStarted = new int[VirtualScreenLayout.LaneCount];
            internal readonly int[] JudgeGrades = new int[VirtualScreenLayout.LaneCount];
            internal readonly List<NoteView> Notes = [];
            internal readonly List<(int Time, int Key)> AutomaticNotes = [];
        }

        private sealed class NoteView
        {
            internal Image Base;
            internal Image Bar;
        }

        internal static void Apply(Harmony h)
        {
            if (string.IsNullOrWhiteSpace(Plugin.Settings.VirtualScreen)) return;
            Patches.Patch(h, typeof(VirtualScreenPatches), typeof(DrumPlayer), "DrawNotes",
                nameof(DrawNotes_Pre), nameof(DrawNotes_Post), nameof(DrawNotes_Finalizer));
            Patches.Patch(h, typeof(VirtualScreenPatches), typeof(SequencePlayer), "onJudged",
                null, nameof(OnJudged_Post));
            if (!Plugin.Settings.SpectatorShowVirtualScreen
                && (Plugin.Settings.SpectatorCamera != SpectatorCamera.VR || Plugin.Settings.SpectatorCinematicGameplay))
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
            }
        }

        internal static void Shutdown()
        {
            if (!string.IsNullOrWhiteSpace(Plugin.Settings.VirtualScreen)
                && !Plugin.Settings.SpectatorShowVirtualScreen
                && (Plugin.Settings.SpectatorCamera != SpectatorCamera.VR || Plugin.Settings.SpectatorCinematicGameplay))
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
            }
            Reset();
        }

        private static void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_screen == null || !SpectatorPatches.IsSpectatorCamera(camera)) return;
            try
            {
                _spectatorRendering = true;
                foreach ((Renderer renderer, bool enabled) in Hidden)
                    if (renderer != null) renderer.enabled = enabled;
            }
            catch (Exception e)
            {
                _failed = true;
                Reset();
                Plugin.L.LogError("Virtual screen spectator rendering failed; using native notes: " + e);
            }
        }

        private static void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!_spectatorRendering || !SpectatorPatches.IsSpectatorCamera(camera)) return;
            try
            {
                EndSpectatorRendering();
            }
            catch (Exception e)
            {
                _failed = true;
                Reset();
                Plugin.L.LogError("Restoring virtual screen after spectator rendering failed: " + e);
            }
        }

        private static void EndSpectatorRendering()
        {
            if (!_spectatorRendering) return;
            _spectatorRendering = false;
            foreach ((Renderer renderer, bool enabled) in Hidden)
                if (renderer != null && enabled) renderer.enabled = false;
        }

        private static void DrawNotes_Pre()
        {
            RestoreRenderers();
        }

        private static void DrawNotes_Post(DrumPlayer __instance)
        {
            if (_failed) return;
            try
            {
                SequencePlayer sequence = __instance.m_seqPlayer;
                if (sequence == null || !sequence.IsPlaying) return;
                if (_screen == null || _screen.Player != __instance)
                {
                    Reset();
                    _screen = CreateScreen(__instance, sequence);
                    if (_screen.Layer >= 0) ApplyCameraLayers();
                }
                Draw(_screen);
                HideNotes(__instance);
            }
            catch (Exception e)
            {
                _failed = true;
                Reset();
                Plugin.L.LogError("Virtual drum screen failed; using native notes: " + e);
            }
        }

        private static Exception DrawNotes_Finalizer(Exception __exception)
        {
            if (__exception != null) RestoreRenderers();
            return __exception;
        }

        private static void OnJudged_Post(SequencePlayer __instance, SequencePlayer.Note note)
        {
            if (_screen == null || note == null
                || !VirtualScreenLayout.ShowFeedback(note.Key, note.IsAutoNote, note.IsBackTrack, _screen.CustomSong)) return;
            int column = VirtualScreenLayout.Column(note.Key);
            int grade = (int)note.JudgeKind;
            if (column < 0 || grade < 0 || grade > 4) return;
            try
            {
                if (__instance != _screen.Sequence || !_screen.Sequence.IsPlaying) return;
                int now = _screen.Sequence.RenderMusicCount;
                _screen.JudgeGrades[column] = grade;
                _screen.JudgeStarted[column] = now;
                if (grade < (int)SequencePlayer.JUDGE_KIND.MISS)
                    _screen.FireStarted[column] = now;
            }
            catch (Exception e)
            {
                _failed = true;
                Reset();
                Plugin.L.LogError("Virtual screen judgment feedback failed; using native notes: " + e);
            }
        }

        internal static void Update()
        {
            if (_screen != null && (_screen.Player == null || _screen.Root == null
                || _screen.Sequence == null || !_screen.Sequence.IsPlaying))
                Reset();
            if (_screen != null && _screen.Layer >= 0)
                ApplyCameraLayers();
            if (_screen != null && _screen.Layer >= 0 && Time.realtimeSinceStartup >= _nextLayerCheck)
            {
                _nextLayerCheck = Time.realtimeSinceStartup + 1f;
                foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
                {
                    if (obj != null && obj.layer == _screen.Layer
                        && obj != _screen.Root && !obj.transform.IsChildOf(_screen.Root.transform))
                    {
                        _failed = true;
                        Reset();
                        Plugin.L.LogError("Virtual screen layer is in use by another object; using native notes.");
                        break;
                    }
                }
            }
        }

        internal static void Reset()
        {
            EndSpectatorRendering();
            int bit = _screen != null && _screen.Layer >= 0 ? 1 << _screen.Layer : 0;
            foreach ((Camera camera, bool included) in CameraMasks.Values)
                if (camera != null)
                    camera.cullingMask = included ? camera.cullingMask | bit : camera.cullingMask & ~bit;
            CameraMasks.Clear();
            RestoreRenderers();
            Renderers.Clear();
            if (_screen == null) return;
            Destroy(_screen);
            _screen = null;
        }

        private static Screen CreateScreen(DrumPlayer player, SequencePlayer sequence)
        {
            string skin = Path.GetFullPath(Path.Combine(Plugin.GameDir, Plugin.Settings.VirtualScreen));
            Screen screen = new() { Player = player, Sequence = sequence, CustomSong = Patches.CurrentDrumSong() != null };
            try
            {
                screen.Chips = LoadTexture(Path.Combine(skin, "Graphics", "7_chips_drums.png"), 718, 640);
                screen.Pads = LoadTexture(Path.Combine(skin, "Graphics", "7_pads.png"), 384, 288);
                screen.Fire = LoadOptionalTexture(Path.Combine(skin, "Graphics", "ScreenPlayDrums chip fire.png"), 1800, 1500, true);
                screen.Judgments = LoadOptionalTexture(Path.Combine(skin, "Graphics", "7_judge strings.png"), 1250, 4080);
                screen.ChipSprites = new Sprite[10];
                screen.PadSprites = new Sprite[VirtualScreenLayout.LaneCount];
                screen.Icons = new Sprite[10, 8];
                int[] chipX = [70, 126, 190, 246, 302, 358, 428, 538];
                int[] chipWidth = [56, 64, 56, 56, 56, 74, 48, 74];
                int[] padX = [96, 0, 96, 192, 288, 192, 288, 0];
                int[] padY = [0, 96, 96, 96, 96, 0, 0, 0];
                for (int i = 0; i < 8; i++)
                {
                    screen.ChipSprites[i] = SpriteAt(screen.Chips, chipX[i], 0, chipWidth[i], 64);
                    screen.PadSprites[i] = SpriteAt(screen.Pads, padX[i], padY[i], 96, 96);
                    if (i is 0 or 5 or 6 or 7)
                        for (int frame = 0; frame < 8; frame++)
                            screen.Icons[i, frame] = SpriteAt(screen.Chips, chipX[i], 128 + (64 * frame), chipWidth[i], 64);
                }
                screen.PadSprites[8] = SpriteAt(screen.Pads, 96, 192, 96, 96);
                screen.PadSprites[9] = SpriteAt(screen.Pads, 0, 192, 96, 96);
                screen.ChipSprites[8] = SpriteAt(screen.Chips, VirtualScreenLayout.LeftPedalChipX, 0, VirtualScreenLayout.LeftPedalChipWidth, 64);
                screen.ChipSprites[9] = SpriteAt(screen.Chips, 0, 0, 70, 64);
                for (int frame = 0; frame < 8; frame++)
                {
                    screen.Icons[8, frame] = SpriteAt(screen.Chips, VirtualScreenLayout.LeftPedalChipX, 128 + (64 * frame), VirtualScreenLayout.LeftPedalChipWidth, 64);
                    screen.Icons[9, frame] = SpriteAt(screen.Chips, 0, 128 + (64 * frame), 70, 64);
                }
                if (Plugin.Settings.VirtualScreenShowAuto)
                {
                    CustomSongs.CustomSong song = Patches.CurrentDrumSong();
                    if (song != null)
                    {
                        CustomSongs.ConvertedChart chart = song.GetConvertedChart(Plugin.Settings, Plugin.Audio.DurationMs,
                            CustomScorePatches.SelectedIndex(song)).Chart;
                        foreach (CustomSongs.ChartNote note in chart.Notes)
                        {
                            int key = VirtualScreenLayout.AutomaticKey(note);
                            if (key >= 0) screen.AutomaticNotes.Add(((int)Math.Round(note.TimeMs), key));
                        }
                        screen.AutomaticNotes.Sort((a, b) => a.Time.CompareTo(b.Time));
                    }
                    if (screen.AutomaticNotes.Count > 0 && Plugin.Settings.VirtualScreenGrayAuto)
                    {
                        screen.GrayChips = LoadTexture(Path.Combine(skin, "Graphics", "7_chips_drums.png"), 718, 640, gray: true);
                        screen.GrayChipSprites = new Sprite[10];
                        screen.GrayIcons = new Sprite[10, 8];
                        for (int key = 0; key < 10; key++)
                        {
                            screen.GrayChipSprites[key] = Sprite.Create(screen.GrayChips, screen.ChipSprites[key].rect, new Vector2(0.5f, 0.5f), 100f);
                            for (int frame = 0; frame < 8; frame++)
                                if (screen.Icons[key, frame] != null)
                                    screen.GrayIcons[key, frame] = Sprite.Create(screen.GrayChips, screen.Icons[key, frame].rect, new Vector2(0.5f, 0.5f), 100f);
                        }
                    }
                    Plugin.L.LogInfo($"Virtual screen automatic notes: {screen.AutomaticNotes.Count}, grayscale={Plugin.Settings.VirtualScreenGrayAuto}.");
                }
                if (screen.Fire != null)
                {
                    screen.FireSprites = new Sprite[10, VirtualScreenLayout.FireFrameCount];
                    for (int key = 0; key < 10; key++)
                        for (int frame = 0; frame < VirtualScreenLayout.FireFrameCount; frame++)
                            screen.FireSprites[key, frame] = SpriteAt(screen.Fire, frame * 150, VirtualScreenLayout.FireRow(key) * 150, 150, 150);
                }
                if (screen.Judgments != null)
                {
                    screen.JudgmentSprites = new Sprite[5, VirtualScreenLayout.JudgeFrameCount];
                    for (int grade = 0; grade < 5; grade++)
                        for (int frame = 0; frame < VirtualScreenLayout.JudgeFrameCount; frame++)
                            screen.JudgmentSprites[grade, frame] = SpriteAt(screen.Judgments, grade * 250, frame * 170, 250, 170);
                }

                DrumInput input = player.Input;
                if (input == null || input.drumObjects == null || input.drumObjects.Count <= 3)
                    throw new InvalidOperationException("The central drums are not initialized.");
                screen.Green = input.drumObjects[2].GetComponent<Renderer>();
                screen.Red = input.drumObjects[3].GetComponent<Renderer>();
                if (screen.Green == null || screen.Red == null)
                    throw new InvalidOperationException("The central drum renderers are unavailable.");
                if (!Plugin.Settings.SpectatorShowVirtualScreen
                    && (Plugin.Settings.SpectatorCamera != SpectatorCamera.VR || Plugin.Settings.SpectatorCinematicGameplay))
                {
                    screen.Layer = FindScreenLayer();
                    _nextLayerCheck = Time.realtimeSinceStartup + 1f;
                }
                screen.Root = new GameObject("BeatArenhax_VirtualScreen");
                screen.Root.SetActive(false);
                if (screen.Layer >= 0) screen.Root.layer = screen.Layer;
                RectTransform root = screen.Root.AddComponent<RectTransform>();
                root.sizeDelta = new Vector2(VirtualScreenLayout.Width, VirtualScreenLayout.Height);
                root.localScale = Vector3.one * (VirtualScreenLayout.WorldUnitsPerPixel * Plugin.Settings.VirtualScreenScale);
                UpdatePlacement(screen);
                root.SetParent(input.transform, true);
                Canvas canvas = screen.Root.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                Image background = screen.Root.AddComponent<Image>();
                background.color = new Color(0f, 0f, 0f, Plugin.Settings.VirtualScreenBackgroundOpacity);
                background.raycastTarget = false;
                screen.Background = background;
                screen.Viewport = root;
                _ = screen.Root.AddComponent<RectMask2D>();
                float laneWidth = VirtualScreenLayout.Width / VirtualScreenLayout.LaneCount;
                for (int column = 0; column <= VirtualScreenLayout.LaneCount; column++)
                    Solid(root, "Lane", new Vector2(column * laneWidth, VirtualScreenLayout.Height * 0.5f),
                        new Vector2(1f, VirtualScreenLayout.Height), new Color(0.35f, 0.35f, 0.35f, 1f));
                for (int key = 0; key < 8; key++)
                {
                    float x = (VirtualScreenLayout.Column(key) + 0.5f) * laneWidth;
                    Image pad = ImageAt(root, "Pad", new Vector2(x, 45f), new Vector2(laneWidth, 80f));
                    pad.sprite = screen.PadSprites[key];
                }
                Image leftPedal = ImageAt(root, "LeftPedal", new Vector2((VirtualScreenLayout.LeftPedalColumn + 0.5f) * laneWidth, 45f),
                    new Vector2(laneWidth, 80f));
                leftPedal.sprite = screen.PadSprites[8];
                Image bassDrum = ImageAt(root, "BassDrum", new Vector2((VirtualScreenLayout.BassDrumColumn + 0.5f) * laneWidth, 45f),
                    new Vector2(laneWidth, 80f));
                bassDrum.sprite = screen.PadSprites[9];
                Solid(root, "Judge", new Vector2(VirtualScreenLayout.Width * 0.5f, VirtualScreenLayout.JudgeY),
                    new Vector2(VirtualScreenLayout.Width, 3f), Color.yellow);
                GameObject effects = new("Feedback");
                effects.layer = root.gameObject.layer;
                screen.Effects = effects.AddComponent<RectTransform>();
                screen.Effects.SetParent(root, false);
                screen.Effects.anchorMin = screen.Effects.anchorMax = Vector2.zero;
                screen.Effects.pivot = Vector2.zero;
                screen.Effects.anchoredPosition = Vector2.zero;
                screen.Effects.sizeDelta = root.sizeDelta;
                for (int column = 0; column < VirtualScreenLayout.LaneCount; column++)
                {
                    float x = (column + 0.5f) * laneWidth;
                    screen.FireStarted[column] = int.MinValue;
                    screen.JudgeStarted[column] = int.MinValue;
                    screen.Fires[column] = ImageAt(screen.Effects, "Fire", new Vector2(x, VirtualScreenLayout.JudgeY),
                        new Vector2(150f, 150f));
                    screen.Judges[column] = ImageAt(screen.Effects, "Judgment", new Vector2(x, VirtualScreenLayout.JudgeY + 105f),
                        new Vector2(150f, 102f));
                    screen.Fires[column].gameObject.SetActive(false);
                    screen.Judges[column].gameObject.SetActive(false);
                }
                screen.Root.SetActive(true);
                Plugin.L.LogInfo("Virtual drum screen enabled using skin: " + skin);
                return screen;
            }
            catch
            {
                Destroy(screen);
                throw;
            }
        }

        private static int FindScreenLayer()
        {
            bool[] occupied = new bool[32];
            foreach (GameObject obj in Resources.FindObjectsOfTypeAll<GameObject>())
                if (obj != null) occupied[obj.layer] = true;
            for (int layer = 31; layer >= 22; layer--)
                if (!occupied[layer] && string.IsNullOrEmpty(LayerMask.LayerToName(layer)))
                    return layer;
            throw new InvalidOperationException("No unused layer is available for the virtual screen.");
        }

        private static void ApplyCameraLayers()
        {
            int bit = 1 << _screen.Layer;
            foreach (Camera camera in Camera.allCameras)
            {
                if (camera == null) continue;
                int mask = camera.cullingMask;
                if (!CameraMasks.ContainsKey(camera.GetInstanceID()))
                    CameraMasks.Add(camera.GetInstanceID(), (camera, (mask & bit) != 0));
                if (SpectatorPatches.IsSpectatorCamera(camera))
                    camera.cullingMask = mask & ~bit;
                else if ((mask & 1) != 0)
                    camera.cullingMask = mask | bit;
            }
        }

        private static Texture2D LoadTexture(string path, int width, int height, bool fire = false, bool gray = false)
        {
            Texture2D texture = new(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(texture, File.ReadAllBytes(path), !fire && !gray))
                    throw new InvalidDataException("Could not decode " + path);
                if (texture.width < width || texture.height < height)
                    throw new InvalidDataException($"Skin texture {path} is too small: {texture.width}x{texture.height}.");
                if (fire || gray)
                {
                    var pixels = texture.GetPixels32();
                    for (int i = 0; i < pixels.Length; i++)
                    {
                        Color32 pixel = pixels[i];
                        if (fire) VirtualScreenLayout.FirePixel(ref pixel.r, ref pixel.g, ref pixel.b, ref pixel.a);
                        if (gray) pixel.r = pixel.g = pixel.b = VirtualScreenLayout.GrayPixel(pixel.r, pixel.g, pixel.b);
                        pixels[i] = pixel;
                    }
                    texture.SetPixels32(pixels);
                    texture.Apply(false, true);
                }
                return texture;
            }
            catch
            {
                UnityEngine.Object.Destroy(texture);
                throw;
            }
        }

        private static Texture2D LoadOptionalTexture(string path, int width, int height, bool fire = false)
        {
            try { return LoadTexture(path, width, height, fire); }
            catch (Exception e)
            {
                Plugin.L.LogWarning("Virtual screen optional feedback texture unavailable: " + path + ": " + e.Message);
                return null;
            }
        }

        private static Sprite SpriteAt(Texture2D texture, int x, int y, int width, int height) =>
            Sprite.Create(texture, new Rect(x, texture.height - y - height, width, height), new Vector2(0.5f, 0.5f), 100f);

        private static Image ImageAt(RectTransform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject obj = new(name);
            obj.layer = parent.gameObject.layer;
            RectTransform rect = obj.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Image image = obj.AddComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static void Solid(RectTransform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            Image image = ImageAt(parent, name, position, size);
            image.color = color;
        }

        private static void UpdatePlacement(Screen screen)
        {
            if (screen.Green == null || screen.Red == null)
                throw new InvalidOperationException("The central drum renderers are unavailable.");
            Vector3 left = screen.Green.bounds.center;
            Vector3 right = screen.Red.bounds.center;
            (float x, float z) = VirtualScreenLayout.Forward(right.x - left.x, right.z - left.z);
            Vector3 forward = new(x, 0f, z);
            screen.Root.transform.position = (left + right) * 0.5f
                + forward * (VirtualScreenLayout.BaseDistance * Plugin.Settings.VirtualScreenDistanceScale)
                + Vector3.up * VirtualScreenLayout.CenterHeight(Plugin.Settings.VirtualScreenScale, Plugin.Settings.VirtualScreenOffsetY);
            screen.Root.transform.localScale = Vector3.one
                * (VirtualScreenLayout.WorldUnitsPerPixel * Plugin.Settings.VirtualScreenScale);
            screen.Root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            if (screen.Background != null)
                screen.Background.color = new Color(0f, 0f, 0f, Plugin.Settings.VirtualScreenBackgroundOpacity);
        }

        private static void Draw(Screen screen)
        {
            UpdatePlacement(screen);
            Il2CppSystem.Collections.Generic.List<SequencePlayer.Note> notes = screen.Sequence.m_playableNotes;
            if (notes == null) throw new InvalidOperationException("Playable drum notes are unavailable.");
            int current = screen.Sequence.RenderMusicCount;
            int first = 0, end = notes.Count;
            while (first < end)
            {
                int middle = first + ((end - first) / 2);
                if (notes[middle].MusicCount < current - 200) first = middle + 1;
                else end = middle;
            }
            int used = 0;
            for (int i = first; i < notes.Count; i++)
            {
                SequencePlayer.Note note = notes[i];
                if (note.MusicCount > current + VirtualScreenLayout.TravelMs) break;
                int column = VirtualScreenLayout.Column(note.Key);
                if (column < 0 || note.IsJudged || note.IsAutoNote) continue;
                DrawNote(screen, ref used, note.Key, note.MusicCount, current, false);
            }
            int low = 0, high = screen.AutomaticNotes.Count;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (screen.AutomaticNotes[middle].Time < current) low = middle + 1;
                else high = middle;
            }
            for (int i = low; i < screen.AutomaticNotes.Count; i++)
            {
                (int time, int key) = screen.AutomaticNotes[i];
                if (time > current + VirtualScreenLayout.TravelMs) break;
                DrawNote(screen, ref used, key, time, current, Plugin.Settings.VirtualScreenGrayAuto);
            }
            for (int i = used; i < screen.Notes.Count; i++) screen.Notes[i].Base.gameObject.SetActive(false);
            DrawFeedback(screen, current);
        }

        private static void DrawNote(Screen screen, ref int used, int key, int time, int current, bool gray)
        {
            float laneWidth = VirtualScreenLayout.Width / VirtualScreenLayout.LaneCount;
            if (used == screen.Notes.Count)
            {
                Image baseImage = ImageAt(screen.Viewport, "Note", Vector2.zero, new Vector2(laneWidth, 64f));
                Image bar = ImageAt(baseImage.rectTransform, "Bar", new Vector2(laneWidth * 0.5f, 32f), new Vector2(laneWidth, 64f));
                screen.Notes.Add(new NoteView { Base = baseImage, Bar = bar });
            }
            NoteView view = screen.Notes[used++];
            Sprite icon = (gray ? screen.GrayIcons : screen.Icons)[key, (int)(Time.time * (1000f / 70f)) % 8];
            Sprite chip = (gray ? screen.GrayChipSprites : screen.ChipSprites)[key];
            view.Base.sprite = icon != null ? icon : chip;
            view.Bar.sprite = chip;
            view.Bar.gameObject.SetActive(icon != null);
            view.Base.rectTransform.anchoredPosition = new Vector2((VirtualScreenLayout.Column(key) + 0.5f) * laneWidth,
                VirtualScreenLayout.NoteY(time, current));
            view.Base.gameObject.SetActive(true);
        }

        private static void DrawFeedback(Screen screen, int current)
        {
            screen.Effects.SetAsLastSibling();
            for (int key = 0; key < VirtualScreenLayout.LaneCount; key++)
            {
                int column = VirtualScreenLayout.Column(key);
                int fireFrame = screen.FireStarted[column] == int.MinValue ? -1
                    : VirtualScreenLayout.EffectFrame(current - screen.FireStarted[column], VirtualScreenLayout.FireFrameMs, VirtualScreenLayout.FireFrameCount);
                Image fire = screen.Fires[column];
                fire.gameObject.SetActive(screen.FireSprites != null && fireFrame >= 0);
                if (fireFrame >= 0 && screen.FireSprites != null) fire.sprite = screen.FireSprites[key, fireFrame];
                int judgeFrame = screen.JudgeStarted[column] == int.MinValue ? -1
                    : VirtualScreenLayout.EffectFrame(current - screen.JudgeStarted[column], VirtualScreenLayout.JudgeFrameMs, VirtualScreenLayout.JudgeFrameCount);
                Image judge = screen.Judges[column];
                judge.gameObject.SetActive(screen.JudgmentSprites != null && judgeFrame >= 0);
                if (judgeFrame >= 0 && screen.JudgmentSprites != null)
                    judge.sprite = screen.JudgmentSprites[screen.JudgeGrades[column], judgeFrame];
            }
        }

        private static void HideNotes(DrumPlayer player)
        {
            PedalPatches.VisitViews(player, Hide);
            foreach (NotesObjectPool.Note note in player.m_useNoteList)
                Hide(note.gameObject);
            foreach (var lane in player.m_sameLineList.notesList)
                foreach (NotesObjectPool.Note note in lane)
                    if (note != null) Hide(note.gameObject);
            foreach (GameObject effect in player.m_synchroGuitarObject) Hide(effect);
            foreach (GameObject effect in player.m_synchroKeyboardObject) Hide(effect);
        }

        private static void Hide(GameObject obj)
        {
            if (obj == null) return;
            int id = obj.GetInstanceID();
            if (!Renderers.TryGetValue(id, out Renderer[] renderers))
            {
                renderers = obj.GetComponentsInChildren<Renderer>(true);
                Renderers.Add(id, renderers);
            }
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !HiddenIds.Add(renderer.GetInstanceID())) continue;
                bool enabled = renderer.enabled;
                Hidden.Add((renderer, enabled));
                if (enabled) renderer.enabled = false;
            }
        }

        private static void RestoreRenderers()
        {
            foreach ((Renderer renderer, bool enabled) in Hidden)
                if (renderer != null) renderer.enabled = enabled;
            Hidden.Clear();
            HiddenIds.Clear();
        }

        private static void Destroy(Screen screen)
        {
            if (screen.Root != null) UnityEngine.Object.Destroy(screen.Root);
            if (screen.ChipSprites != null)
                foreach (Sprite sprite in screen.ChipSprites)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.GrayChipSprites != null)
                foreach (Sprite sprite in screen.GrayChipSprites)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.GrayIcons != null)
                foreach (Sprite sprite in screen.GrayIcons)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.PadSprites != null)
                foreach (Sprite sprite in screen.PadSprites)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.Icons != null)
                foreach (Sprite sprite in screen.Icons)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.FireSprites != null)
                foreach (Sprite sprite in screen.FireSprites)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.JudgmentSprites != null)
                foreach (Sprite sprite in screen.JudgmentSprites)
                    if (sprite != null) UnityEngine.Object.Destroy(sprite);
            if (screen.Chips != null) UnityEngine.Object.Destroy(screen.Chips);
            if (screen.GrayChips != null) UnityEngine.Object.Destroy(screen.GrayChips);
            if (screen.Pads != null) UnityEngine.Object.Destroy(screen.Pads);
            if (screen.Fire != null) UnityEngine.Object.Destroy(screen.Fire);
            if (screen.Judgments != null) UnityEngine.Object.Destroy(screen.Judgments);
        }
    }
}
