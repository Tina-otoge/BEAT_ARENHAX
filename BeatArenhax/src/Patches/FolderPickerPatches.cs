using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BeatArenhax.CustomSongs;
using BeatArenhax.Images;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class FolderPickerPatches
    {
        private const float PreviousCanvasScale = 0.0012f;
        private const float CanvasScale = PreviousCanvasScale * 0.5f;
        private const float TouchDepth = 0.01f;
        private const float ReleaseMargin = 0.025f;
        private static readonly Vector3 HeadOffset = new(0.50f, -0.42f, 0.42f);
        private static readonly Vector3 KeyboardHeadOffset = new(0f, SongSearchKeyboardLayout.HeadOffsetY, SongSearchKeyboardLayout.HeadOffsetZ);
        private static FolderSelectionKind _selectedKind;
        private static string _selectedFolder;
        private static string _search = "";
        private static Panel _panel;

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.DisplayFolderPicker) return;
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(MusicSelect), "ReadyMusicPlateList", null, nameof(Ready_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(MusicSelect), nameof(MusicSelect.Open), null, nameof(Open_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(RoundMenu), nameof(RoundMenu.Close), null, nameof(Close_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(RoundMenu), nameof(RoundMenu.SelectClose), null, nameof(Close_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(RoundMenu), "UpdateHorizontalControll", nameof(Control_Pre));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(RoundMenu), "UpdateVerticalControll", nameof(Control_Pre));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(ButtonCollider), "OnTriggerEnter", null, nameof(TouchEnter_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(ButtonCollider), "OnTriggerExit", null, nameof(TouchExit_Post));
            Patches.Patch(h, typeof(FolderPickerPatches), typeof(ButtonCollider), "Update", nameof(ButtonUpdate_Pre));
        }


        private static void TouchEnter_Post(ButtonCollider __instance)
        {
            if (_panel != null && _panel.HasButton(__instance) && __instance.m_touchMyCollider)
                _panel.BeginTouch(__instance);
        }

        private static bool ButtonUpdate_Pre(ButtonCollider __instance) =>
            _panel == null || !_panel.HasButton(__instance) || _panel.UpdateButton(__instance);

        private static void TouchExit_Post(ButtonCollider __instance, Collider other)
        {
            if (_panel != null && _panel.HasButton(__instance)) _panel.EndTouch(__instance, other);
        }

        private static void Ready_Post(MusicSelect __instance)
        {
            try
            {
                _panel?.Dispose();
                _panel = null;
                _panel = new Panel(__instance);
            }
            catch (Exception e) { Plugin.L.LogError("Creating folder panel failed: " + e); }
        }

        private static void Open_Post(MusicSelect __instance, bool __result)
        {
            if (!__result || _panel == null || _panel.Select.Pointer != __instance.Pointer) return;
            try { _panel.Open(); }
            catch (Exception e) { Plugin.L.LogError("Opening folder panel failed: " + e); }
        }

        private static void Close_Post(RoundMenu __instance, bool __result)
        {
            if (__result && _panel != null && _panel.Menu.Pointer == __instance.Pointer) _panel.Hide();
        }

        private static bool Control_Pre(RoundMenu __instance) =>
            _panel == null || _panel.Menu.Pointer != __instance.Pointer || !_panel.Touching;

        internal static void Update()
        {
            if (_panel == null) return;
            if (_panel.Select == null)
            {
                _panel.Dispose();
                _panel = null;
                return;
            }
            try { _panel.Update(); }
            catch (Exception e)
            {
                Plugin.L.LogError("Folder panel update failed: " + e);
                _panel.Hide();
            }
        }

        private sealed class Panel : IDisposable
        {
            internal readonly MusicSelect Select;
            internal RoundMenu Menu => Select.m_musicListMenu;
            private readonly List<MusicPlateBase> _allPlates = [];
            private readonly List<GameObject> _allObjects = [];
            private readonly List<FolderSelection> _items = [];
            private readonly List<Image> _tileBackgrounds = [];
            private readonly List<Sprite> _sprites = [];
            private readonly List<ButtonCollider> _buttons = [];
            private readonly List<BoxCollider> _hitboxes = [];
            private readonly Dictionary<IntPtr, int> _buttonIndices = [];
            private readonly GameObject _root;
            private readonly GameObject _storage;
            private readonly RectTransform _viewport;
            private readonly RectTransform _content;
            private readonly TextMeshProUGUI _heading;
            private readonly GameObject _keyboard;
            private readonly TextMeshProUGUI _searchText;
            private readonly List<string> _keyActions = [];
            private string _draft = "";
            private bool _keyboardOpen;
            private bool _switchKeyboard;
            private bool _open;
            private bool _needsFilter;
            private int _selection;
            private float _scroll;
            private Collider _hand;
            private int _touchedTile = -1;
            private readonly FolderTouch _touch = new();
            private bool _dragging;
            private bool _pressed;
            internal bool Touching => _hand != null;

            internal Panel(MusicSelect select)
            {
                Select = select;
                for (int i = 0; i < select.m_musicPlateList.Count; i++)
                {
                    _allPlates.Add(select.m_musicPlateList[i]);
                    _allObjects.Add(select.m_musicListObjects[i]);
                }
                if (_allPlates.Count == 0) throw new InvalidOperationException("No song plates to supply the panel font.");
                Transform source = _allObjects[0].transform.Find("Canvas/MP_Txt_Artist");
                TextMeshProUGUI fontSource = source != null ? source.GetComponent<TextMeshProUGUI>() : null;
                if (fontSource == null) throw new InvalidOperationException("Song plate artist font is missing.");
                _items.Add(new FolderSelection { Kind = FolderSelectionKind.All });
                _items.Add(new FolderSelection { Kind = FolderSelectionKind.Custom });
                _items.Add(new FolderSelection { Kind = FolderSelectionKind.Original });
                _items.AddRange(Plugin.Library.Folders.Select(folder => new FolderSelection { Kind = FolderSelectionKind.Folder, Folder = folder }));
                _selection = _items.FindIndex(item => item.Kind == _selectedKind
                    && (item.Kind != FolderSelectionKind.Folder || item.Name == _selectedFolder));
                if (_selection < 0) _selection = 0;
                _needsFilter = _selection != 0 || !string.IsNullOrWhiteSpace(_search);

                _storage = new GameObject("BeatArenhax_HiddenSongs");
                _storage.transform.SetParent(select.transform, false);
                _storage.SetActive(false);
                try
                {
                    RectTransform root = Rect("BeatArenhax_FolderPicker", select.transform, new Vector2(FolderGrid.Width + 24f, FolderGrid.Height + 60f));
                    _root = root.gameObject;
                    root.localScale = Vector3.one * CanvasScale;
                    Rigidbody body = _root.AddComponent<Rigidbody>();
                    body.isKinematic = true;
                    body.useGravity = false;
                    Canvas canvas = _root.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.WorldSpace;
                    Transform wheelCanvasTransform = _allObjects[0].transform.Find("Canvas");
                    Canvas wheelCanvas = wheelCanvasTransform != null ? wheelCanvasTransform.GetComponent<Canvas>() : null;
                    if (wheelCanvas == null) throw new InvalidOperationException("Song wheel canvas is missing.");
                    canvas.overrideSorting = true;
                    canvas.sortingLayerID = wheelCanvas.sortingLayerID;
                    int wheelOrder = wheelCanvas.sortingOrder;
                    foreach (GameObject plate in _allObjects)
                        foreach (Canvas plateCanvas in plate.GetComponentsInChildren<Canvas>(true))
                            wheelOrder = Math.Max(wheelOrder, plateCanvas.sortingOrder);
                    canvas.sortingOrder = wheelOrder + 1;
                    Image background = _root.AddComponent<Image>();
                    background.color = new Color(0.025f, 0.035f, 0.045f, 0.96f);
                    background.raycastTarget = false;
                    _heading = Label(fontSource, root, "Heading", new Vector2(FolderGrid.Width, 24f), 14f);
                    _heading.rectTransform.anchoredPosition = new Vector2(0f, FolderGrid.Height * 0.5f + 16f);
                    RectTransform launcher = AddKey(fontSource, root, "SEARCH", "KEYBOARD",
                        new Vector2((FolderGrid.Width + 24f + SongSearchKeyboardLayout.LauncherSize) * 0.5f + 10f,
                            FolderGrid.Height * 0.5f - SongSearchKeyboardLayout.LauncherSize * 0.5f),
                        Vector2.one * SongSearchKeyboardLayout.LauncherSize);
                    launcher.Find("Label").GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -30f);
                    AddSearchIcon(launcher);
                    _viewport = Rect("Viewport", root, new Vector2(FolderGrid.Width, FolderGrid.Height));
                    _viewport.gameObject.AddComponent<RectMask2D>();
                    _content = Rect("Content", _viewport, new Vector2(FolderGrid.Width, Math.Max(FolderGrid.Height,
                        ((_items.Count + 2) / 3) * FolderGrid.Pitch - FolderGrid.Gap)));
                    _content.anchorMin = _content.anchorMax = new Vector2(0f, 1f);
                    _content.pivot = new Vector2(0f, 1f);
                    for (int i = 0; i < _items.Count; i++)
                    {
                        RectTransform tile = Rect("Folder_" + i, _content, new Vector2(FolderGrid.TileSize, FolderGrid.TileSize));
                        tile.anchorMin = tile.anchorMax = new Vector2(0f, 1f);
                        tile.pivot = new Vector2(0f, 1f);
                        tile.anchoredPosition = new Vector2((i % 3) * FolderGrid.Pitch, -(i / 3) * FolderGrid.Pitch);
                        Image image = tile.gameObject.AddComponent<Image>();
                        image.raycastTarget = false;
                        if (_items[i].Folder?.PreImagePath != null)
                            LoadFolderSprite(_items[i].Folder.PreImagePath, image);
                        _tileBackgrounds.Add(image);
                        RectTransform shade = Rect("NameBackground", tile, new Vector2(100f, 38f));
                        Image strip = shade.gameObject.AddComponent<Image>();
                        strip.raycastTarget = false;
                        strip.color = new Color(0f, 0f, 0f, 0.7f);
                        TextMeshProUGUI label = Label(fontSource, shade, "Name", new Vector2(94f, 36f), 11f);
                        label.SetText(_items[i].Name);
                        BoxCollider hitbox = tile.gameObject.AddComponent<BoxCollider>();
                        hitbox.isTrigger = true;
                        hitbox.center = new Vector3(FolderGrid.TileSize * 0.5f, -FolderGrid.TileSize * 0.5f, 0f);
                        hitbox.size = new Vector3(FolderGrid.TileSize, FolderGrid.TileSize, TouchDepth * 2f / CanvasScale);
                        _hitboxes.Add(hitbox);
                        ButtonCollider button = tile.gameObject.AddComponent<ButtonCollider>();
                        button.m_id = i;
                        button.TouchActive = true;
                        button.EnableTouchArea = false;
                        button.VibrationType = BInput.Vibration.UI_TOUCH;
                        button.onTouchID = (Action<int, Collider>)Press;
                        _buttons.Add(button);
                        _buttonIndices.Add(button.Pointer, i);
                    }
                    RectTransform keyboard = Rect("BeatArenhax_SearchKeyboard", root,
                        new Vector2(SongSearchKeyboardLayout.Width, SongSearchKeyboardLayout.Height));
                    _keyboard = keyboard.gameObject;
                    keyboard.localScale = Vector3.one * SongSearchKeyboardLayout.Scale;
                    Image keyboardBackground = keyboard.gameObject.AddComponent<Image>();
                    keyboardBackground.color = new Color(0.025f, 0.035f, 0.045f, 0.98f);
                    keyboardBackground.raycastTarget = false;
                    _searchText = Label(fontSource, keyboard, "SearchText", new Vector2(SongSearchKeyboardLayout.Width - 32f, 48f), 22f);
                    _searchText.rectTransform.anchoredPosition = new Vector2(0f, 195f);
                    string[] rows = ["1234567890", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"];
                    for (int row = 0; row < rows.Length; row++)
                    {
                        string keys = rows[row];
                        for (int column = 0; column < keys.Length; column++)
                        {
                            string key = keys[column].ToString();
                            AddKey(fontSource, keyboard, key, key,
                                new Vector2((column - (keys.Length - 1) * 0.5f) * SongSearchKeyboardLayout.KeyPitch,
                                    SongSearchKeyboardLayout.FirstRowY - row * SongSearchKeyboardLayout.RowPitch),
                                new Vector2(SongSearchKeyboardLayout.KeyWidth, SongSearchKeyboardLayout.KeyHeight),
                                SongSearchKeyboardLayout.Scale);
                        }
                    }
                    AddKey(fontSource, keyboard, "SPACE", " ", new Vector2(-200f, -100f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "BACK", "BACK", new Vector2(0f, -100f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "CLEAR", "CLEAR", new Vector2(200f, -100f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "-", "-", new Vector2(-200f, -155f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "'", "'", new Vector2(0f, -155f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "CLOSE", "CLOSE", new Vector2(200f, -155f), new Vector2(190f, 46f), SongSearchKeyboardLayout.Scale);
                    AddKey(fontSource, keyboard, "SEARCH", "SEARCH", new Vector2(0f, -210f), new Vector2(400f, 46f), SongSearchKeyboardLayout.Scale);
                    _keyboard.SetActive(false);
                    UpdateHeading();
                    RefreshSelection();
                    UpdateHitboxes();
                    _root.SetActive(false);
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            internal void Open()
            {
                Transform head = Menu.m_headTransform;
                if (head == null) throw new InvalidOperationException("Wheel head transform is missing.");
                Quaternion yaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
                _root.transform.position = head.position + yaw * HeadOffset;
                _root.transform.rotation = Quaternion.LookRotation(_root.transform.position - head.position, Vector3.up);
                Vector3 corner = new((FolderGrid.Width + 24f) * 0.5f, -(FolderGrid.Height + 60f) * 0.5f, 0f);
                _root.transform.position += _root.transform.rotation * (corner * (PreviousCanvasScale - CanvasScale));
                _keyboard.transform.position = head.position + yaw * KeyboardHeadOffset;
                _keyboard.transform.rotation = Quaternion.LookRotation(_keyboard.transform.position - head.position, Vector3.up);
                _open = true;
                _root.SetActive(true);
            }

            internal void Hide()
            {
                _open = false;
                ResetTouch();
                _keyboardOpen = false;
                _switchKeyboard = false;
                _keyboard.SetActive(false);
                _viewport.gameObject.SetActive(true);
                UpdateHeading();
                RefreshSelection();
                if (_root != null) _root.SetActive(false);
            }

            private void ResetTouch()
            {
                _hand = null;
                _touchedTile = -1;
                _dragging = false;
                _pressed = false;
                foreach (ButtonCollider button in _buttons)
                {
                    if (button.isStartTouch || button.m_touchMyCollider)
                    {
                        button.DeactivateHandIndicator();
                        button.SetTouchFlag(false);
                    }
                    button.isStartTouch = false;
                    button.touchTimer = 0f;
                    button.TouchActive = true;
                }
            }

            internal void Update()
            {
                if (!_open) return;
                if (Menu.m_currentStep == RoundMenu.Step.Close || Menu.m_currentStep == RoundMenu.Step.End)
                {
                    Hide();
                    return;
                }
                if (Menu.m_currentStep != RoundMenu.Step.Select) return;
                if (_switchKeyboard)
                {
                    _switchKeyboard = false;
                    ResetTouch();
                    _viewport.gameObject.SetActive(!_keyboardOpen);
                    _keyboard.SetActive(_keyboardOpen);
                    UpdateHeading();
                    RefreshSelection();
                }
                if (_needsFilter)
                {
                    _needsFilter = false;
                    Filter();
                }
                UpdateTouch();
            }

            internal bool HasButton(ButtonCollider button) => _buttonIndices.ContainsKey(button.Pointer);



            internal void BeginTouch(ButtonCollider button)
            {
                if (!_open || _hand != null || button.m_Hand == null) return;
                _hand = button.m_Hand;
                _touchedTile = _buttonIndices[button.Pointer];
                if (_touchedTile < _items.Count)
                {
                    Vector2 point = Point(_hand);
                    _touch.Start(point.x, point.y, _scroll, _items.Count);
                }
                _dragging = false;
                _pressed = false;
                RefreshSelection();
            }

            internal bool UpdateButton(ButtonCollider button)
            {
                if (!_open || Menu.m_currentStep != RoundMenu.Step.Select) return false;
                if (_hand != null && !_pressed && _touchedTile < _items.Count) MoveTouch();
                return !_dragging;
            }

            internal void EndTouch(ButtonCollider button, Collider hand)
            {
                if (_dragging || _hand == null || hand == null || _hand.Pointer != hand.Pointer
                    || _buttonIndices[button.Pointer] != _touchedTile) return;
                _touch.Release();
                _hand = null;
                _touchedTile = -1;
                _pressed = false;
                RefreshSelection();
            }

            private void UpdateHitboxes()
            {
                for (int i = 0; i < _hitboxes.Count; i++)
                {
                    float top = i / FolderGrid.Columns * FolderGrid.Pitch - _scroll;
                    float start = Math.Max(0f, -top);
                    float end = Math.Min(FolderGrid.TileSize, FolderGrid.Height - top);
                    BoxCollider hitbox = _hitboxes[i];
                    hitbox.enabled = end > start;
                    if (!hitbox.enabled) continue;
                    hitbox.center = new Vector3(FolderGrid.TileSize * 0.5f, -(start + end) * 0.5f, 0f);
                    hitbox.size = new Vector3(FolderGrid.TileSize, end - start, TouchDepth * 2f / CanvasScale);
                }
            }

            private void MoveTouch()
            {
                Vector2 current = Point(_hand);
                float scroll = _touch.Move(current.x, current.y, _items.Count);
                if (_scroll != scroll)
                {
                    _scroll = scroll;
                    _content.anchoredPosition = new Vector2(0f, _scroll);
                    UpdateHitboxes();
                }
                if (!_dragging && _touch.Dragged)
                {
                    _dragging = true;
                    foreach (ButtonCollider button in _buttons)
                    {
                        if (button.isStartTouch || button.m_touchMyCollider)
                        {
                            button.DeactivateHandIndicator();
                            button.SetTouchFlag(false);
                        }
                        button.isStartTouch = false;
                        button.touchTimer = 0f;
                        button.TouchActive = false;
                    }
                }
            }

            private void UpdateTouch()
            {
                if (_hand == null) return;
                if (!_hand.enabled || !_hand.gameObject.activeInHierarchy)
                {
                    Hide();
                    return;
                }
                if (!_pressed && _touchedTile < _items.Count) MoveTouch();
                if (!_dragging) return;
                bool contact = false;
                foreach (Collider collider in Contacts(ReleaseMargin))
                    if (collider.Pointer == _hand.Pointer) { contact = true; break; }
                if (!contact)
                {
                    _touch.Release();
                    _hand = null;
                    _touchedTile = -1;
                    _dragging = false;
                    _pressed = false;
                    foreach (ButtonCollider button in _buttons) button.TouchActive = true;
                    RefreshSelection();
                }
            }

            private void Press(int index, Collider hand)
            {
                if (!_open || _dragging || _pressed || index != _touchedTile) return;
                _pressed = true;
                if (index >= _items.Count)
                {
                    PressKey(_keyActions[index - _items.Count]);
                    return;
                }
                _selection = index;
                _selectedKind = _items[index].Kind;
                _selectedFolder = _items[index].Folder?.Name;
                _needsFilter = true;
                RefreshSelection();
                Plugin.L.LogInfo("Folder press '" + _items[index].Name + "' (vanilla picker)");
            }

            private RectTransform AddKey(TextMeshProUGUI font, Transform parent, string label, string action, Vector2 position, Vector2 size, float scale = 1f)
            {
                RectTransform key = Rect("Key_" + label, parent, size);
                key.anchoredPosition = position;
                Image image = key.gameObject.AddComponent<Image>();
                image.color = new Color(0.08f, 0.25f, 0.32f, 1f);
                image.raycastTarget = false;
                Label(font, key, "Label", size - new Vector2(4f, 4f), 13f).SetText(label);
                BoxCollider hitbox = key.gameObject.AddComponent<BoxCollider>();
                hitbox.isTrigger = true;
                hitbox.size = new Vector3(size.x, size.y, TouchDepth * 2f / (CanvasScale * scale));
                ButtonCollider button = key.gameObject.AddComponent<ButtonCollider>();
                int index = _items.Count + _keyActions.Count;
                _keyActions.Add(action);
                button.m_id = index;
                button.TouchActive = true;
                button.EnableTouchArea = false;
                button.VibrationType = BInput.Vibration.UI_TOUCH;
                button.onTouchID = (Action<int, Collider>)Press;
                _buttons.Add(button);
                _buttonIndices.Add(button.Pointer, index);
                return key;
            }

            private static void AddSearchIcon(RectTransform parent)
            {
                const int segments = 20;
                const float radius = 19f;
                for (int i = 0; i < segments; i++)
                {
                    float angle = i * Mathf.PI * 2f / segments;
                    RectTransform segment = Rect("MagnifierRing", parent, new Vector2(7f, 4f));
                    segment.anchoredPosition = new Vector2(Mathf.Cos(angle) * radius - 5f, Mathf.Sin(angle) * radius + 16f);
                    segment.localRotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg + 90f);
                    Image image = segment.gameObject.AddComponent<Image>();
                    image.color = Color.white;
                    image.raycastTarget = false;
                }
                RectTransform handle = Rect("MagnifierHandle", parent, new Vector2(24f, 5f));
                handle.anchoredPosition = new Vector2(16f, -5f);
                handle.localRotation = Quaternion.Euler(0f, 0f, -45f);
                Image handleImage = handle.gameObject.AddComponent<Image>();
                handleImage.color = Color.white;
                handleImage.raycastTarget = false;
            }

            private void PressKey(string action)
            {
                switch (action)
                {
                    case "KEYBOARD":
                        _draft = _search;
                        _keyboardOpen = !_keyboardOpen;
                        _switchKeyboard = true;
                        break;
                    case "CLOSE":
                        _keyboardOpen = false;
                        _switchKeyboard = true;
                        break;
                    case "SEARCH":
                        _search = _draft.Trim();
                        _keyboardOpen = false;
                        _switchKeyboard = true;
                        _needsFilter = true;
                        break;
                    case "CLEAR": _draft = ""; break;
                    case "BACK":
                        if (_draft.Length > 0) _draft = _draft.Substring(0, _draft.Length - 1);
                        break;
                    default:
                        if (_draft.Length < 100) _draft += action;
                        break;
                }
                _searchText.SetText(string.IsNullOrEmpty(_draft) ? "Type a search" : _draft);
            }

            private void UpdateHeading() =>
                _heading.SetText(_keyboardOpen ? "Search songs" :
                    string.IsNullOrEmpty(_search) ? "Select a folder" : "Search: " + _search);

            private Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<Collider> Contacts(float margin)
            {
                Vector3 scale = _viewport.lossyScale;
                Vector3 halfSize = new(FolderGrid.Width * Math.Abs(scale.x) * 0.5f + margin,
                    FolderGrid.Height * Math.Abs(scale.y) * 0.5f + margin, TouchDepth + margin);
                return Physics.OverlapBox(_viewport.position, halfSize, _viewport.rotation, -1, QueryTriggerInteraction.Collide);
            }

            private Vector2 Point(Collider hand)
            {
                // Use the collider surface, not the gesture anchor: sticks and hands have offset collision shapes.
                Vector3 local = _viewport.InverseTransformPoint(hand.bounds.center);
                local.z = 0f;
                local = _viewport.InverseTransformPoint(hand.ClosestPoint(_viewport.TransformPoint(local)));
                return new Vector2(local.x + FolderGrid.Width * 0.5f, FolderGrid.Height * 0.5f - local.y);
            }

            private void Filter()
            {
                Stopwatch elapsed = Stopwatch.StartNew();
                FolderSelection selection = _items[_selection];
                List<int> indices = [];
                MusicInfoManager manager = null;
                for (int i = 0; i < _allPlates.Count; i++)
                    if (selection.Matches(_allPlates[i].MusicID, Plugin.Library))
                    {
                        int id = _allPlates[i].MusicID;
                        if (!string.IsNullOrEmpty(_search))
                        {
                            if (Plugin.Library.ById.TryGetValue(id, out CustomSong song))
                            {
                                if (!SongSearch.Matches(_search, song.Title, song.Artist, song.Folder)) continue;
                            }
                            else
                            {
                                if (manager == null) manager = MusicInfoManager.GetInstance();
                                if (manager == null) throw new InvalidOperationException("Music info manager is missing.");
                                MusicInfo info = manager.GetMusicInfoByID(id);
                                if (info == null) throw new InvalidOperationException("Music info is missing for music ID " + id);
                                if (!SongSearch.Matches(_search, info.MusicName, info.ArtistName, null)) continue;
                            }
                        }
                        if (_allPlates[i].m_btnCollider == null)
                            throw new InvalidOperationException("Song plate touch collider is missing for music ID " + _allPlates[i].MusicID);
                        indices.Add(i);
                    }
                if (indices.Count == 0)
                {
                    Plugin.L.LogInfo("No songs match the current folder and search.");
                }
                RoundMenu menu = Menu;
                Vector3 position = menu.m_MenuCenterObject.transform.position;
                Quaternion rotation = menu.m_MenuCenterObject.transform.rotation;
                SongWheelTouchPatches.StopForFilter();
                SongWheelLazyPatches.Rebuild(() =>
                {
                    GameObject oldCenter = menu.m_MenuCenterObject;
                    oldCenter.SetActive(false);
                    foreach (GameObject plate in _allObjects)
                    {
                        plate.SetActive(false);
                        plate.transform.SetParent(_storage.transform, false);
                    }
                    menu.StopAllCoroutines();
                    Il2CppSystem.Collections.Generic.List<GameObject> objects = new();
                    Il2CppSystem.Collections.Generic.List<MusicPlateBase> plates = new();
                    foreach (int index in indices)
                    {
                        MusicPlateBase plate = _allPlates[index];
                        plate.Index = objects.Count;
                        // Native TouchIndex forwards the collider's ID, not the plate's m_index.
                        plate.m_btnCollider.m_id = objects.Count;
                        objects.Add(_allObjects[index]);
                        plates.Add(plate);
                    }
                    Select.m_musicListObjects = objects;
                    Select.m_musicPlateList = plates;
                    SongSelectPatches.RebuildSongWheel(menu, Select.m_parentAvatar, objects);
                    SongWheelLazyPatches.PrepareRows(menu, objects);
                    if (!Plugin.Settings.LazySongWheel)
                        foreach (GameObject plate in objects) plate.SetActive(true);
                    UnityEngine.Object.Destroy(oldCenter);
                    menu.m_MenuCenterObject.transform.position = position;
                    menu.m_MenuCenterObject.transform.rotation = rotation;
                    menu.m_MenuCenterObject.SetActive(true);
                    menu.selectedLine = -1;
                    menu.selectedIndex = -1;
                    menu.m_selectedObject = null;
                    menu.m_rowCursor = 0;
                    menu.m_currentStep = RoundMenu.Step.Select;
                    menu.ResetVerticalScrolling();
                    menu.UpdatePanelList();
                    foreach (MusicPlateBase plate in _allPlates) plate.SetTouchActive(false);
                    foreach (MusicPlateBase plate in plates) plate.SetTouchActive(true);
                });
                Plugin.L.LogInfo("Folder filter '" + selection.Name + "', search '" + _search + "': " + indices.Count + " songs (wheel only; " + elapsed.ElapsedMilliseconds + " ms)");
            }

            private void RefreshSelection()
            {
                for (int i = 0; i < _tileBackgrounds.Count; i++)
                    _tileBackgrounds[i].color = i == _touchedTile ? new Color(0.65f, 1f, 1f, 1f)
                        : i == _selection ? new Color(0.25f, 1f, 0.8f, 1f)
                        : _tileBackgrounds[i].sprite != null ? Color.white
                        : i < 3 ? new Color(0.08f, 0.25f, 0.32f, 1f) : new Color(0.16f, 0.16f, 0.18f, 1f);
            }

            public void Dispose()
            {
                if (_root != null) UnityEngine.Object.Destroy(_root);
                if (_storage != null) UnityEngine.Object.Destroy(_storage);
                foreach (Sprite sprite in _sprites)
                {
                    if (sprite == null) continue;
                    UnityEngine.Object.Destroy(sprite.texture);
                    UnityEngine.Object.Destroy(sprite);
                }
            }

            private static RectTransform Rect(string name, Transform parent, Vector2 size)
            {
                GameObject obj = new(name);
                RectTransform rect = obj.AddComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.sizeDelta = size;
                return rect;
            }

            private static TextMeshProUGUI Label(TextMeshProUGUI source, Transform parent, string name, Vector2 size, float fontSize)
            {
                GameObject obj = UnityEngine.Object.Instantiate(source.gameObject, parent, false);
                obj.name = name;
                TextMeshProUGUI text = obj.GetComponent<TextMeshProUGUI>();
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = text.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                text.rectTransform.anchoredPosition = Vector2.zero;
                text.rectTransform.sizeDelta = size;
                text.margin = Vector4.zero;
                text.alignment = TextAlignmentOptions.Center;
                text.enableAutoSizing = true;
                text.fontSizeMin = 5f;
                text.fontSizeMax = fontSize;
                text.fontSize = fontSize;
                text.color = Color.white;
                text.richText = false;
                text.raycastTarget = false;
                text.overflowMode = TextOverflowModes.Ellipsis;
                obj.SetActive(true);
                return text;
            }

            private void LoadFolderSprite(string path, Image image)
            {
                _ = Task.Run(() =>
                {
                    try
                    {
                        byte[] bytes = File.ReadAllBytes(path);
                        JacketPreloader.Pixels pixels = JacketPreloader.DecodePixels(bytes);
                        Plugin.RunOnMainThread(() =>
                        {
                            if (_root == null || image == null) return;
                            Texture2D texture = new(pixels != null ? pixels.Width : 2, pixels != null ? pixels.Height : 2, TextureFormat.RGBA32, false);
                            try
                            {
                                if (pixels != null)
                                {
                                    unsafe { fixed (byte* data = pixels.Rgba) texture.LoadRawTextureData((IntPtr)data, pixels.Rgba.Length); }
                                    texture.Apply(false, true);
                                }
                                else if (!ImageConversion.LoadImage(texture, bytes, true))
                                    throw new InvalidDataException("Unsupported or invalid image.");
                                texture.wrapMode = TextureWrapMode.Clamp;
                                Sprite sprite = Sprite.Create(texture, new UnityEngine.Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
                                _sprites.Add(sprite);
                                image.sprite = sprite;
                                RefreshSelection();
                            }
                            catch (Exception e)
                            {
                                UnityEngine.Object.Destroy(texture);
                                Plugin.L.LogWarning("Folder image '" + path + "': " + e.Message);
                            }
                        });
                    }
                    catch (Exception e) { Plugin.L.LogWarning("Reading folder image '" + path + "': " + e.Message); }
                });
            }
        }
    }
}
