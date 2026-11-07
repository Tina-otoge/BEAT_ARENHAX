using System;
using System.Collections.Generic;
using BeatArenhax.CustomSongs;
using BeatArenhax.DTX;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class CustomDifficultyPatches
    {
        private static readonly Dictionary<IntPtr, Panel> Panels = [];
        private static int _extraSelection = -1;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), "SetUpDifficultyPlate", null, nameof(Refresh_Post));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), "SetGameMode", null, nameof(Refresh_Post));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), "SelectDifficulty", nameof(Select_Pre), nameof(Select_Post));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate.DifficultyPlate), nameof(MusicInfoPlate.DifficultyPlate.Selected), nameof(Selected_Pre));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(Bvr.TamamoText), nameof(Bvr.TamamoText.TextReload), nameof(TextReload_Pre));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), nameof(MusicInfoPlate.SetTouchActive), null, nameof(Touch_Post));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), nameof(MusicInfoPlate.SetButtonTouchActive), null, nameof(Touch_Post));
            Patches.Patch(h, typeof(CustomDifficultyPatches), typeof(MusicInfoPlate), "OnDestroy", null, nameof(Destroy_Post));
        }

        private static CustomSong ActiveSong(MusicInfoPlate plate)
        {
            if (plate.m_currentMode != 0 || plate.playerInfo == null || plate.playerInfo.CurrentPlayType != PlayType.Drum) return null;
            return Patches.SongById(plate.playerInfo.CurrentMusicID);
        }

        private static void Refresh_Post(MusicInfoPlate __instance)
        {
            try
            {
                CustomSong song = ActiveSong(__instance);
                if (!Panels.TryGetValue(__instance.Pointer, out Panel panel))
                {
                    if (song == null) return;
                    panel = new Panel(__instance);
                    Panels.Add(__instance.Pointer, panel);
                }
                panel.Refresh(song);
            }
            catch (Exception e) { Plugin.L.LogError("Updating custom difficulty panel failed: " + e); }
        }

        private static bool Select_Pre(MusicInfoPlate __instance, int slot)
        {
            CustomSong song = ActiveSong(__instance);
            if (song == null) return true;
            int selected = _extraSelection >= 0 ? _extraSelection : slot;
            if (__instance.m_difficultySelect)
            {
                if (selected < 0 || selected >= CustomScorePatches.Charts(song).Count) return false;
                CustomScorePatches.Select(song, selected);
                Plugin.L.LogInfo($"Selected custom chart: {song.Title}, L{CustomScorePatches.Charts(song)[selected].SetIndex}");
            }
            __instance.playerInfo.CurrentDifficulty = CustomScorePatches.SelectedIndex(song) == 0 ? Difficulty.Basic : Difficulty.Advanced;
            return false;
        }

        private static bool Selected_Pre(MusicInfoPlate.DifficultyPlate __instance)
        {
            foreach (Panel panel in Panels.Values)
                if (panel.Controls(__instance)) return false;
            return true;
        }

        private static bool TextReload_Pre(Bvr.TamamoText __instance)
        {
            foreach (Panel panel in Panels.Values)
                if (panel.Controls(__instance)) return false;
            return true;
        }

        private static void Select_Post(MusicInfoPlate __instance)
        {
            if (Panels.TryGetValue(__instance.Pointer, out Panel panel)) panel.Refresh(ActiveSong(__instance));
        }

        private static void Touch_Post(MusicInfoPlate __instance)
        {
            if (Panels.TryGetValue(__instance.Pointer, out Panel panel)) panel.SyncTouch();
        }

        private static void Destroy_Post(MusicInfoPlate __instance)
        {
            if (!Panels.Remove(__instance.Pointer, out Panel panel)) return;
            panel.Dispose();
        }

        private sealed class Panel
        {
            private const string DifficultyPath = "Canvas/Denmoku_Win/Win/MD_Win/Difficulty_Btn";
            private readonly MusicInfoPlate _plate;
            private readonly RectTransform _group;
            private readonly HorizontalLayoutGroup _layout;
            private readonly RectTransform[] _buttons = new RectTransform[4];
            private readonly ButtonCollider[] _touch = new ButtonCollider[4];
            private readonly MusicInfoPlate.DifficultyPlate[] _plates = new MusicInfoPlate.DifficultyPlate[4];
            private readonly TextMeshProUGUI[] _labels = new TextMeshProUGUI[4];
            private readonly Bvr.TamamoText[] _localizers = new Bvr.TamamoText[4];
            private readonly Image[] _frames = new Image[4];
            private readonly Material[] _materials = new Material[4];
            private readonly Material[] _originalMaterials = new Material[2];
            private readonly bool[] _originalGradients = new bool[2];
            private readonly bool[] _originalLocalizers = new bool[2];
            private readonly bool[] _originalAnimators = new bool[2];
            private readonly Color[] _originalFrameColors = new Color[2];
            private readonly Vector3[] _originalFrameScales = new Vector3[2];
            private readonly Vector3[] _originalButtonScales = new Vector3[2];
            private readonly string[] _originalLabels = new string[2];
            private readonly Color[] _originalColors = new Color[2];
            private readonly Vector2[] _originalPositions = new Vector2[2];
            private readonly Vector2[] _originalAnchorMin = new Vector2[2];
            private readonly Vector2[] _originalAnchorMax = new Vector2[2];
            private readonly RectTransform[] _resized = new RectTransform[3];
            private readonly Vector2[] _sizes = new Vector2[3];
            private readonly Vector2[] _positions = new Vector2[3];
            private readonly List<(RectTransform Rect, Vector2 Position, float Anchor)> _content = [];
            private bool _custom;
            private int _available;

            internal Panel(MusicInfoPlate plate)
            {
                _plate = plate;
                Transform root = plate.m_Menu != null ? plate.m_Menu.transform : null;
                Transform group = root != null ? root.Find(DifficultyPath) : null;
                if (group == null) throw new InvalidOperationException("Difficulty panel not found.");
                _group = group.GetComponent<RectTransform>();
                _layout = group.GetComponent<HorizontalLayoutGroup>();
                if (_group == null || _layout == null) throw new InvalidOperationException("Difficulty layout is missing.");
                for (int i = 0; i < 2; i++)
                {
                    Transform button = group.Find(i == 0 ? "MD_Btn_casual" : "MD_Btn_advanced");
                    if (button == null) throw new InvalidOperationException("Original difficulty button is missing.");
                    _buttons[i] = button.GetComponent<RectTransform>();
                    _touch[i] = button.GetComponent<ButtonCollider>();
                    _plates[i] = plate.m_diffPlates[i];
                    _labels[i] = button.Find("Tx_Difficulty").GetComponent<TextMeshProUGUI>();
                    _localizers[i] = button.Find("Tx_Difficulty").GetComponent<Bvr.TamamoText>();
                    _frames[i] = button.Find("Image_Frame").GetComponent<Image>();
                    if (_buttons[i] == null || _touch[i] == null || _plates[i] == null || _labels[i] == null || _localizers[i] == null || _frames[i] == null)
                        throw new InvalidOperationException("Original difficulty button components are missing.");
                    _originalLabels[i] = _labels[i].text;
                    _originalColors[i] = _labels[i].color;
                    _originalPositions[i] = _buttons[i].anchoredPosition;
                    _originalAnchorMin[i] = _buttons[i].anchorMin;
                    _originalAnchorMax[i] = _buttons[i].anchorMax;
                }
                _groupPosition = _group.anchoredPosition;
                string[] paths = ["Canvas/Denmoku_Win_base", "Canvas/Denmoku_Win/Win/MD_Win", "Canvas/Denmoku_Win/Win/PL_Win"];
                for (int i = 0; i < paths.Length; i++)
                {
                    Transform child = root.Find(paths[i]);
                    _resized[i] = child != null ? child.GetComponent<RectTransform>() : null;
                    if (_resized[i] == null) throw new InvalidOperationException("Difficulty panel background is missing: " + paths[i]);
                    _sizes[i] = _resized[i].sizeDelta;
                    _positions[i] = _resized[i].anchoredPosition;
                }
                for (int i = 0; i < _resized[1].childCount; i++)
                {
                    RectTransform child = _resized[1].GetChild(i).TryCast<RectTransform>();
                    if (child != null)
                        _content.Add((child, child.anchoredPosition, (child.anchorMin.y + child.anchorMax.y) * 0.5f));
                }
                Transform play = root.Find("Canvas/Play_Btn");
                if (play == null) throw new InvalidOperationException("Play button is missing.");
                _play = play.GetComponent<RectTransform>();
                _playPosition = _play.anchoredPosition;
                for (int i = 2; i < 4; i++)
                {
                    int index = i;
                    GameObject clone = UnityEngine.Object.Instantiate(_buttons[i - 2].gameObject, group, false);
                    clone.name = "BeatArenhax_Difficulty_" + i;
                    _buttons[i] = clone.GetComponent<RectTransform>();
                    _touch[i] = clone.GetComponent<ButtonCollider>();
                    _touch[i].onTouch = null;
                    _touch[i].onRelease = null;
                    _touch[i].onReleaseID = null;
                    _touch[i].onTouchID = (Action<int, Collider>)((_, _) => SelectExtra(index));
                    _plates[i] = new MusicInfoPlate.DifficultyPlate();
                    _plates[i].SetPlateObject(clone.transform);
                    _labels[i] = clone.transform.Find("Tx_Difficulty").GetComponent<TextMeshProUGUI>();
                    _localizers[i] = clone.transform.Find("Tx_Difficulty").GetComponent<Bvr.TamamoText>();
                    _frames[i] = clone.transform.Find("Image_Frame").GetComponent<Image>();
                    _localizers[i].enabled = false;
                    _plates[i].m_animator.enabled = false;
                    clone.SetActive(false);
                }
                Plugin.L.LogInfo("Created four-button custom difficulty panel.");
            }

            private readonly RectTransform _play;
            private readonly Vector2 _playPosition;
            private readonly Vector2 _groupPosition;

            internal bool Controls(MusicInfoPlate.DifficultyPlate plate)
            {
                if (!_custom || plate == null) return false;
                foreach (MusicInfoPlate.DifficultyPlate item in _plates)
                    if (item != null && item.Pointer == plate.Pointer) return true;
                return false;
            }

            internal bool Controls(Bvr.TamamoText localizer)
            {
                if (!_custom || localizer == null) return false;
                foreach (Bvr.TamamoText item in _localizers)
                    if (item != null && item.Pointer == localizer.Pointer) return true;
                return false;
            }

            internal void Dispose()
            {
                foreach (Material material in _materials)
                    if (material != null) UnityEngine.Object.Destroy(material);
            }

            private void SelectExtra(int index)
            {
                if (!_custom || index >= _available || !_touch[index].TouchActive || _plate.m_difficultySelect) return;
                _extraSelection = index;
                try { _plate.SelectIndex(8); }
                finally { _extraSelection = -1; }
            }

            internal void Refresh(CustomSong song)
            {
                if (song == null)
                {
                    if (!_custom) return;
                    _custom = false;
                    _layout.enabled = true;
                    for (int i = 0; i < 2; i++)
                    {
                        _plates[i].m_animator.enabled = _originalAnimators[i];
                        _labels[i].fontSharedMaterial = _originalMaterials[i];
                        _labels[i].enableVertexGradient = _originalGradients[i];
                        _localizers[i].enabled = _originalLocalizers[i];
                        _labels[i].SetText(_originalLabels[i]);
                        _labels[i].color = _originalColors[i];
                        _frames[i].color = _originalFrameColors[i];
                        _frames[i].transform.localScale = _originalFrameScales[i];
                        _buttons[i].localScale = _originalButtonScales[i];
                        if (_originalLocalizers[i]) _localizers[i].TextReload();
                        _buttons[i].anchorMin = _originalAnchorMin[i];
                        _buttons[i].anchorMax = _originalAnchorMax[i];
                        _buttons[i].anchoredPosition = _originalPositions[i];
                        _buttons[i + 2].gameObject.SetActive(false);
                    }
                    for (int i = 0; i < _resized.Length; i++)
                    {
                        _resized[i].sizeDelta = _sizes[i];
                        _resized[i].anchoredPosition = _positions[i];
                    }
                    foreach (var child in _content)
                        if (child.Rect != null) child.Rect.anchoredPosition = child.Position;
                    _play.anchoredPosition = _playPosition;
                    _group.anchoredPosition = _groupPosition;
                    for (int i = 0; i < 2; i++)
                        _plates[i].Selected((int)_plate.playerInfo.CurrentDifficulty == i, true);
                    return;
                }
                if (!_custom)
                {
                    for (int i = 0; i < 2; i++)
                    {
                        _originalMaterials[i] = _labels[i].fontSharedMaterial;
                        _originalGradients[i] = _labels[i].enableVertexGradient;
                        _originalLocalizers[i] = _localizers[i].enabled;
                        _originalAnimators[i] = _plates[i].m_animator.enabled;
                        _originalFrameColors[i] = _frames[i].color;
                        _originalFrameScales[i] = _frames[i].transform.localScale;
                        _originalButtonScales[i] = _buttons[i].localScale;
                    }
                    // The native selection clip animates absolute button positions and keeps its last frame.
                    for (int i = 0; i < 4; i++)
                    {
                        _localizers[i].StopAllCoroutines();
                        _localizers[i].enabled = false;
                        _plates[i].m_animator.enabled = false;
                        if (_materials[i] == null)
                            _materials[i] = new Material(_labels[i].fontSharedMaterial);
                        _labels[i].fontSharedMaterial = _materials[i];
                        _labels[i].enableVertexGradient = false;
                    }
                }
                _custom = true;
                IReadOnlyList<CustomSongChart> charts = CustomScorePatches.Charts(song);
                _available = charts.Count;
                _layout.enabled = false;
                _resized[0].sizeDelta = _sizes[0] + new Vector2(0, 110);
                _resized[0].anchoredPosition = _positions[0] + new Vector2(0, -55);
                _resized[1].sizeDelta = _sizes[1] + new Vector2(0, 110);
                _resized[1].anchoredPosition = _positions[1] + new Vector2(0, -55);
                // Keep every original child in world position while the parent grows downward.
                foreach (var child in _content)
                    if (child.Rect != null) child.Rect.anchoredPosition = child.Position + new Vector2(0, 110f * (1f - child.Anchor));
                _resized[2].anchoredPosition = _positions[2] + new Vector2(0, -110);
                _play.anchoredPosition = _playPosition + new Vector2(0, -110);
                int selected = CustomScorePatches.SelectedIndex(song);
                for (int i = 0; i < 4; i++)
                {
                    _buttons[i].gameObject.SetActive(true);
                    _buttons[i].anchorMin = new Vector2(0.5f, 0.5f);
                    _buttons[i].anchorMax = new Vector2(0.5f, 0.5f);
                    _buttons[i].anchoredPosition = new Vector2(i % 2 == 0 ? -100 : 100, i < 2 ? 0 : -110);
                    bool available = i < charts.Count;
                    _buttons[i].localScale = Vector3.one;
                    _frames[i].transform.localScale = Vector3.one;
                    _frames[i].color = new Color(1f, 1f, 1f, i == selected && available ? 1f : 0f);
                    _labels[i].SetText(available ? charts[i].Label : "-");
                    _labels[i].color = _originalColors[i % 2];
                    string color = available ? CustomChartSelection.RecognizedColor(charts[i].Label) : null;
                    if (color != null && ColorUtility.TryParseHtmlString(color, out Color parsed))
                    {
                        _labels[i].color = parsed;
                        _materials[i].SetColor("_OutlineColor", parsed);
                        _materials[i].SetColor("_GlowColor", new Color(parsed.r, parsed.g, parsed.b, 0.5f));
                    }
                    else
                    {
                        _materials[i].SetColor("_OutlineColor", _originalMaterials[i % 2].GetColor("_OutlineColor"));
                        _materials[i].SetColor("_GlowColor", _originalMaterials[i % 2].GetColor("_GlowColor"));
                    }
                    CustomScore score = available ? CustomScorePatches.Get(song, PlayType.Drum, i) : null;
                    _plates[i].SetRank(score != null && score.PlayCount > 0 ? bvr.ScoreRank(score.Score).ToString() : "-");
                    _plates[i].SetScore(score?.Score ?? 0);
                    if (available)
                    {
                        _plates[i].SetLevel(ChartConverter.ToGameLevel(charts[i].DLevel));
                        if (Plugin.Settings.DecimalNotation) DecimalLevelPatches.SetLevel(_plates[i].m_level, charts[i]);
                    }
                    else _plates[i].m_level.SetText("-");
                }
                SyncTouch();
            }

            internal void SyncTouch()
            {
                for (int i = 2; i < 4; i++)
                    _touch[i].TouchActive = _custom && i < _available && _touch[i - 2].TouchActive;
            }
        }
    }
}
