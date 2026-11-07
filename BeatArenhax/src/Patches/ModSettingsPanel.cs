using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal sealed class ModSettingsPanel
    {
        private const int RowsPerPage = 6;
        private static readonly float[] RowY = [249f, 164f, 79f, -6f, -91f, -176f];
        private static readonly SettingDefinition[] Definitions = CreateDefinitions();

        private readonly Transform _windowRoot;
        private readonly GameObject _labelTemplate;
        private readonly string _iniPath;
        private readonly Settings _values;
        private readonly List<SettingEntry> _entries;
        private readonly Row[] _rows = new Row[RowsPerPage];
        private readonly List<GameObject> _created = [];
        private readonly TextMeshProUGUI _notice;
        private readonly TextMeshProUGUI _pageLabel;
        private readonly TextMeshProUGUI _status;
        private readonly ButtonCollider _previousButton;
        private readonly ButtonCollider _nextButton;
        private int _page;
        private int _activeSliderRow = -1;
        private Collider _activeHand;
        private bool _disposed;

        private sealed class SettingDefinition
        {
            internal readonly string Name;
            internal readonly string Section;
            internal readonly string Key;
            internal readonly string Label;
            internal readonly FieldInfo Field;
            internal readonly float Minimum;
            internal readonly float Maximum;
            internal readonly bool ApplyImmediately;

            internal SettingDefinition(string name, string section, string key, string label,
                float minimum, float maximum, bool applyImmediately)
            {
                Name = name;
                Section = section;
                Key = key;
                Label = label;
                Minimum = minimum;
                Maximum = maximum;
                ApplyImmediately = applyImmediately;
                Field = typeof(Settings).GetField(name, BindingFlags.Instance | BindingFlags.Public)
                    ?? throw new MissingFieldException(typeof(Settings).FullName, name);
            }

            internal bool IsBoolean => Field.FieldType == typeof(bool);
            internal bool IsFloat => Field.FieldType == typeof(float);
        }

        private sealed class SettingEntry
        {
            internal readonly SettingDefinition Definition;
            internal object CommittedValue;

            internal SettingEntry(SettingDefinition definition, object value)
            {
                Definition = definition;
                CommittedValue = value;
            }
        }

        private sealed class Row
        {
            internal TextMeshProUGUI Label;
            internal Bvr.TamamoText Localizer;
            internal RectTransform LabelRect;
            internal TextMeshProUGUI ValueLabel;
            internal GameObject Toggle;
            internal ButtonCollider ToggleCollider;
            internal Animator ToggleAnimator;
            internal Slider Slider;
            internal GameObject SliderRoot;
            internal ButtonCollider SliderCollider;
            internal LabelState LabelState;
            internal ToggleState ToggleState;
        }

        private sealed class LabelState
        {
            internal Transform Parent;
            internal int SiblingIndex;
            internal string Text;
            internal float FontSize;
            internal float FontSizeMin;
            internal float FontSizeMax;
            internal Color Color;
            internal TextAlignmentOptions Alignment;
            internal bool AutoSize;
            internal bool Active;
            internal bool LocalizerEnabled;
            internal string LocalizerLanguageId;
            internal Vector3 LocalPosition;
            internal Quaternion LocalRotation;
            internal Vector3 LocalScale;
            internal Vector2 AnchorMin;
            internal Vector2 AnchorMax;
            internal Vector2 Pivot;
            internal Vector2 Size;
        }

        private sealed class ToggleState
        {
            internal bool Active;
            internal bool TouchActive;
            internal bool EnableTouchArea;
            internal bool AnimatorEnabled;
            internal int Id;
            internal ButtonCollider.OnTouch OnTouch;
            internal ButtonCollider.OnRelease OnRelease;
            internal ButtonCollider.OnTouchID OnTouchId;
            internal ButtonCollider.OnReleaseID OnReleaseId;
            internal AnimatorStateInfo AnimatorState;
        }

        private ModSettingsPanel(UiGameSettingWindow window, string iniPath, Settings values)
        {
            _iniPath = iniPath;
            _values = values;
            _entries = Definitions.Select(definition =>
                new SettingEntry(definition, definition.Field.GetValue(values))).ToList();

            GameObject menu = window.m_Menu;
            Transform menuTransform = menu != null ? menu.transform : null;
            _windowRoot = menuTransform != null ? menuTransform.Find("Canvas/Object/Window/Window_00") : null;
            if (_windowRoot == null) throw new InvalidOperationException("Native Game Settings window body was not found.");
            Transform labelTemplate = _windowRoot.Find("Text_Message/Text_0");
            _labelTemplate = labelTemplate != null ? labelTemplate.gameObject : null;
            if (_labelTemplate == null) throw new InvalidOperationException("Native Game Settings row text was not found.");

            try
            {
                for (int i = 0; i < RowsPerPage; i++) _rows[i] = CreateRow(i);
                _notice = CloneLabel("BeatArenhax_ModSettingsNotice", _windowRoot, new Vector2(0f, 319f),
                    new Vector2(640f, 30f), 18f, TextAlignmentOptions.Center);
                _pageLabel = CloneLabel("BeatArenhax_ModSettingsPage", _windowRoot, new Vector2(0f, -278f),
                    new Vector2(150f, 30f), 18f, TextAlignmentOptions.Center);
                _status = CloneLabel("BeatArenhax_ModSettingsStatus", _windowRoot, new Vector2(0f, -244f),
                    new Vector2(610f, 28f), 16f, TextAlignmentOptions.Center);
                _previousButton = CreateNavigationButton("Previous", new Vector2(-210f, -278f), -1);
                _nextButton = CreateNavigationButton("Next", new Vector2(210f, -278f), 1);
                _notice.SetText("Changes save automatically. Some apply now; others require a restart.");
                Render();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal ModSettingsPanel(UiGameSettingWindow window)
            : this(window, System.IO.Path.Combine(Plugin.GameDir, "beat_arenhax.ini"),
                Settings.LoadOrCreate(System.IO.Path.Combine(Plugin.GameDir, "beat_arenhax.ini")))
        {
        }

        internal void Render()
        {
            if (_disposed) return;
            for (int i = 0; i < RowsPerPage; i++)
            {
                int entryIndex = _page * RowsPerPage + i;
                SettingEntry entry = entryIndex < _entries.Count ? _entries[entryIndex] : null;
                RenderRow(i, entry);
            }
            _pageLabel.SetText($"Page {_page + 1} / {(_entries.Count + RowsPerPage - 1) / RowsPerPage}");
            _previousButton.gameObject.SetActive(_page > 0);
            _nextButton.gameObject.SetActive((_page + 1) * RowsPerPage < _entries.Count);
            _status.gameObject.SetActive(!string.IsNullOrEmpty(_status.text));
        }

        private Row CreateRow(int index)
        {
            Transform labelTransform = _windowRoot.Find($"Text_Message/Text_{index}");
            Transform toggleTransform = _windowRoot.Find($"ToggleButtons/Btn_{index}");
            TextMeshProUGUI label = labelTransform != null ? labelTransform.GetComponent<TextMeshProUGUI>() : null;
            Bvr.TamamoText localizer = labelTransform != null ? labelTransform.GetComponent<Bvr.TamamoText>() : null;
            ButtonCollider toggleCollider = toggleTransform != null ? toggleTransform.GetComponent<ButtonCollider>() : null;
            Animator animator = toggleTransform != null ? toggleTransform.GetComponent<Animator>() : null;
            RectTransform labelRect = labelTransform != null ? labelTransform.GetComponent<RectTransform>() : null;
            if (label == null || localizer == null || toggleTransform == null || toggleCollider == null
                || animator == null || labelRect == null)
                throw new InvalidOperationException($"Native Game Settings row {index} is missing required controls.");

            Row row = new()
            {
                Label = label,
                Localizer = localizer,
                LabelRect = labelRect,
                Toggle = toggleTransform.gameObject,
                ToggleCollider = toggleCollider,
                ToggleAnimator = animator,
                LabelState = CaptureLabel(label, localizer),
                ToggleState = CaptureToggle(toggleTransform.gameObject, toggleCollider, animator),
            };
            _rows[index] = row;
            labelRect.SetParent(_windowRoot, false);
            labelRect.anchorMin = labelRect.anchorMax = new Vector2(0.5f, 0.5f);
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.anchoredPosition = new Vector2(-145f, RowY[index]);
            labelRect.sizeDelta = new Vector2(320f, 62f);
            label.fontSize = 24f;
            label.enableAutoSizing = true;
            label.fontSizeMin = 14f;
            label.fontSizeMax = 24f;
            label.alignment = TextAlignmentOptions.MidlineLeft;
            localizer.StopAllCoroutines();
            localizer.m_langID = "";
            localizer.enabled = false;

            row.ValueLabel = CloneLabel($"BeatArenhax_ModSettingsValue_{index}", _windowRoot,
                new Vector2(310f, RowY[index]), new Vector2(74f, 44f), 17f, TextAlignmentOptions.Center);
            (row.Slider, row.SliderRoot, row.SliderCollider) = CreateSlider(index, RowY[index]);
            return row;
        }

        private void RenderRow(int rowIndex, SettingEntry entry)
        {
            Row row = _rows[rowIndex];
            bool visible = entry != null;
            row.Label.gameObject.SetActive(visible);
            row.Toggle.SetActive(visible && entry.Definition.IsBoolean);
            row.SliderRoot.SetActive(visible && entry.Definition.IsFloat);
            row.ValueLabel.gameObject.SetActive(visible && entry.Definition.IsFloat);
            if (!visible) return;

            row.Label.SetText(entry.Definition.Label);
            row.Localizer.enabled = false;
            if (entry.Definition.IsBoolean)
            {
                row.ToggleCollider.m_id = rowIndex;
                row.ToggleCollider.TouchActive = true;
                row.ToggleCollider.EnableTouchArea = false;
                row.ToggleCollider.VibrationType = BInput.Vibration.UI_TOUCH;
                row.ToggleCollider.onTouch = null;
                row.ToggleCollider.onRelease = null;
                row.ToggleCollider.onReleaseID = null;
                row.ToggleCollider.onTouchID = (Action<int, Collider>)ToggleTouch;
                row.ToggleAnimator.enabled = true;
                row.ToggleAnimator.Play((bool)entry.Definition.Field.GetValue(_values)
                    ? "Btn_Timing_default_On" : "Btn_Timing_default_Off", 0, 1f);
                return;
            }

            row.SliderCollider.m_id = rowIndex;
            row.SliderCollider.TouchActive = true;
            row.SliderCollider.EnableTouchArea = false;
            row.SliderCollider.VibrationType = BInput.Vibration.UI_TOUCH;
            row.SliderCollider.onTouch = null;
            row.SliderCollider.onRelease = null;
            row.SliderCollider.onTouchID = (Action<int, Collider>)SliderTouch;
            row.SliderCollider.onReleaseID = (Action<int, Collider>)SliderRelease;
            UpdateSlider(rowIndex, entry);
        }

        private (Slider Slider, GameObject Root, ButtonCollider Touch) CreateSlider(int index, float y)
        {
            RectTransform rect = CreateRect($"BeatArenhax_ModSettingsSlider_{index}", _windowRoot,
                new Vector2(245f, 38f), new Vector2(165f, y));
            _created.Add(rect.gameObject);
            Image track = rect.gameObject.AddComponent<Image>();
            track.color = new Color(0.12f, 0.16f, 0.19f, 1f);
            track.raycastTarget = false;

            RectTransform fillRect = CreateRect("Fill", rect, Vector2.zero, Vector2.zero);
            fillRect.anchorMin = new Vector2(0f, 0f);
            fillRect.anchorMax = new Vector2(1f, 1f);
            fillRect.pivot = new Vector2(0f, 0.5f);
            Image fill = fillRect.gameObject.AddComponent<Image>();
            fill.color = new Color(0.1f, 0.67f, 0.78f, 1f);
            fill.raycastTarget = false;

            RectTransform handleRect = CreateRect("Handle", rect, new Vector2(18f, 34f), Vector2.zero);
            Image handle = handleRect.gameObject.AddComponent<Image>();
            handle.color = Color.white;
            handle.raycastTarget = false;

            Slider slider = rect.gameObject.AddComponent<Slider>();
            slider.targetGraphic = track;
            slider.fillRect = fillRect;
            slider.handleRect = handleRect;
            slider.direction = Slider.Direction.LeftToRight;
            slider.interactable = true;
            slider.wholeNumbers = false;

            BoxCollider hitbox = rect.gameObject.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.size = new Vector3(245f, 55f, 100f);
            ButtonCollider touch = rect.gameObject.AddComponent<ButtonCollider>();
            touch.TouchActive = true;
            touch.EnableTouchArea = false;
            touch.VibrationType = BInput.Vibration.UI_TOUCH;
            return (slider, rect.gameObject, touch);
        }

        private ButtonCollider CreateNavigationButton(string label, Vector2 position, int direction)
        {
            RectTransform rect = CreateRect($"BeatArenhax_ModSettings{label}", _windowRoot,
                new Vector2(160f, 46f), position);
            _created.Add(rect.gameObject);
            Image background = rect.gameObject.AddComponent<Image>();
            background.color = new Color(0.12f, 0.24f, 0.29f, 1f);
            background.raycastTarget = false;
            CloneLabel(label + "Label", rect, Vector2.zero, new Vector2(156f, 42f), 16f,
                TextAlignmentOptions.Center).SetText(label);
            BoxCollider hitbox = rect.gameObject.AddComponent<BoxCollider>();
            hitbox.isTrigger = true;
            hitbox.size = new Vector3(160f, 54f, 100f);
            ButtonCollider button = rect.gameObject.AddComponent<ButtonCollider>();
            button.m_id = direction;
            button.TouchActive = true;
            button.EnableTouchArea = false;
            button.VibrationType = BInput.Vibration.UI_TOUCH;
            button.onTouchID = (Action<int, Collider>)((id, _) => ChangePage(id));
            return button;
        }

        private TextMeshProUGUI CloneLabel(string name, Transform parent, Vector2 position, Vector2 size,
            float fontSize, TextAlignmentOptions alignment)
        {
            GameObject clone = UnityEngine.Object.Instantiate(_labelTemplate, parent, false);
            _created.Add(clone);
            clone.name = name;
            clone.SetActive(true);
            Bvr.TamamoText localizer = clone.GetComponent<Bvr.TamamoText>();
            if (localizer != null)
            {
                localizer.StopAllCoroutines();
                localizer.m_langID = "";
                localizer.enabled = false;
            }
            TextMeshProUGUI text = clone.GetComponent<TextMeshProUGUI>();
            if (text == null) throw new InvalidOperationException("Could not clone native Game Settings text.");
            RectTransform rect = clone.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            text.fontSize = fontSize;
            text.enableAutoSizing = false;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.SetText("");
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            GameObject gameObject = new(name);
            RectTransform rect = gameObject.AddComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        private void ChangePage(int direction)
        {
            int pageCount = (_entries.Count + RowsPerPage - 1) / RowsPerPage;
            _page = Mathf.Clamp(_page + direction, 0, pageCount - 1);
            _activeSliderRow = -1;
            _activeHand = null;
            Render();
        }

        private void ToggleTouch(int rowIndex, Collider hand)
        {
            int entryIndex = _page * RowsPerPage + rowIndex;
            if (entryIndex >= _entries.Count) return;
            SettingEntry entry = _entries[entryIndex];
            if (!entry.Definition.IsBoolean) return;
            object value = !(bool)entry.Definition.Field.GetValue(_values);
            if (Commit(entry, value)) Render();
        }

        private void SliderTouch(int rowIndex, Collider hand)
        {
            if (rowIndex < 0 || rowIndex >= RowsPerPage) return;
            int entryIndex = _page * RowsPerPage + rowIndex;
            if (entryIndex >= _entries.Count || !_entries[entryIndex].Definition.IsFloat) return;
            _activeSliderRow = rowIndex;
            _activeHand = hand;
            UpdateSliderTouch(_rows[rowIndex].SliderCollider);
        }

        private void SliderRelease(int rowIndex, Collider hand)
        {
            if (rowIndex != _activeSliderRow) return;
            if (hand != null) _activeHand = hand;
            UpdateSliderTouch(_rows[rowIndex].SliderCollider);
            int entryIndex = _page * RowsPerPage + rowIndex;
            if (entryIndex < _entries.Count)
            {
                SettingEntry entry = _entries[entryIndex];
                object value = entry.Definition.Field.GetValue(_values);
                if (!Equals(value, entry.CommittedValue)) Commit(entry, value);
            }
            _activeSliderRow = -1;
            _activeHand = null;
            Render();
        }

        internal void UpdateSliderTouch(ButtonCollider button)
        {
            if (_disposed || _activeSliderRow < 0 || button == null) return;
            Row row = _rows[_activeSliderRow];
            if (row.SliderCollider == null || row.SliderCollider.Pointer != button.Pointer) return;
            Collider hand = button.m_Hand != null ? button.m_Hand : _activeHand;
            if (hand == null) return;

            RectTransform rect = row.Slider.GetComponent<RectTransform>();
            Vector3 local = rect.InverseTransformPoint(hand.transform.position);
            float halfHandle = row.Slider.handleRect.rect.width * 0.5f;
            float left = rect.rect.xMin + halfHandle;
            float right = rect.rect.xMax - halfHandle;
            float t = right > left ? Mathf.Clamp01((local.x - left) / (right - left)) : 0f;
            SettingEntry entry = _entries[_page * RowsPerPage + _activeSliderRow];
            (float minimum, float maximum) = GetRange(entry.Definition);
            float value = minimum + (maximum - minimum) * t;
            value = Mathf.Round(value * 100f) / 100f;
            Preview(entry, value);
        }

        private void Preview(SettingEntry entry, float value)
        {
            SettingDefinition definition = entry.Definition;
            (float minimum, float maximum) = GetRange(definition);
            value = Mathf.Clamp(value, minimum, maximum);
            if ((float)definition.Field.GetValue(_values) == value) return;
            definition.Field.SetValue(_values, value);
            try
            {
                if (definition.ApplyImmediately) ApplyLive(definition, value);
            }
            catch (Exception e)
            {
                Revert(entry, "Applying the setting failed; the change was reverted.", e);
                return;
            }

            int rowIndex = _activeSliderRow;
            Row row = _rows[rowIndex];
            row.Slider.minValue = minimum;
            row.Slider.maxValue = maximum;
            row.Slider.value = value;
            row.ValueLabel.SetText(FormatFloat(value));
        }

        private bool Commit(SettingEntry entry, object value)
        {
            SettingDefinition definition = entry.Definition;
            object old = entry.CommittedValue;
            definition.Field.SetValue(_values, value);
            try
            {
                if (definition.ApplyImmediately) ApplyLive(definition, value);
            }
            catch (Exception e)
            {
                Revert(entry, "Applying the setting failed; the change was reverted.", e);
                return false;
            }

            try
            {
                string serialized = definition.IsBoolean
                    ? ((bool)value ? "1" : "0")
                    : FormatFloat((float)value);
                Settings.SaveModMenuValues(_iniPath, [(definition.Section, definition.Key, serialized)]);
                entry.CommittedValue = value;
                SetStatus("");
                return true;
            }
            catch (Exception e)
            {
                definition.Field.SetValue(_values, old);
                try
                {
                    if (definition.ApplyImmediately) ApplyLive(definition, old);
                }
                catch (Exception rollbackError)
                {
                    Plugin.L.LogError($"Rolling back {definition.Name} after an ini save failure failed: {rollbackError}");
                }
                Plugin.L.LogError($"Saving {definition.Section}.{definition.Key} failed: {e}");
                SetStatus("Save failed; the change was reverted.");
                return false;
            }
        }

        private void Revert(SettingEntry entry, string message, Exception error)
        {
            SettingDefinition definition = entry.Definition;
            _activeSliderRow = -1;
            _activeHand = null;
            definition.Field.SetValue(_values, entry.CommittedValue);
            try
            {
                if (definition.ApplyImmediately) ApplyLive(definition, entry.CommittedValue);
            }
            catch (Exception rollbackError)
            {
                Plugin.L.LogError($"Rolling back {definition.Name} failed: {rollbackError}");
            }
            Plugin.L.LogError($"{definition.Name}: {error}");
            SetStatus(message);
            Render();
        }

        private void ApplyLive(SettingDefinition definition, object value)
        {
            definition.Field.SetValue(Plugin.Settings, value);
            if (definition.Name == "MasterVolume" && Plugin.Audio != null && Plugin.Audio.Mixer != null)
                Plugin.Audio.Mixer.MasterVolume = (float)value;
        }

        private void UpdateSlider(int rowIndex, SettingEntry entry)
        {
            Row row = _rows[rowIndex];
            (float minimum, float maximum) = GetRange(entry.Definition);
            float value = (float)entry.Definition.Field.GetValue(_values);
            row.Slider.minValue = minimum;
            row.Slider.maxValue = maximum;
            row.Slider.value = Mathf.Clamp(value, minimum, maximum);
            row.ValueLabel.SetText(FormatFloat(value));
        }

        private (float Minimum, float Maximum) GetRange(SettingDefinition definition)
        {
            float maximum = definition.Maximum;
            if (definition.Name == "SongWheelRadiusScale")
            {
                float distance = (float)typeof(Settings).GetField(nameof(Settings.SongWheelDistanceScale))
                    .GetValue(_values);
                maximum = Mathf.Min(maximum, 2f / distance);
            }
            else if (definition.Name == "SongWheelDistanceScale")
            {
                float radius = (float)typeof(Settings).GetField(nameof(Settings.SongWheelRadiusScale))
                    .GetValue(_values);
                maximum = Mathf.Min(maximum, 2f / radius);
            }
            return (definition.Minimum, maximum);
        }

        private void SetStatus(string message)
        {
            if (_status == null) return;
            _status.SetText(message);
            _status.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        private static string FormatFloat(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        private static LabelState CaptureLabel(TextMeshProUGUI label, Bvr.TamamoText localizer)
        {
            RectTransform rect = label.rectTransform;
            return new LabelState
            {
                Parent = rect.parent,
                SiblingIndex = rect.GetSiblingIndex(),
                Text = label.text,
                FontSize = label.fontSize,
                FontSizeMin = label.fontSizeMin,
                FontSizeMax = label.fontSizeMax,
                Color = label.color,
                Alignment = label.alignment,
                AutoSize = label.enableAutoSizing,
                Active = label.gameObject.activeSelf,
                LocalizerEnabled = localizer.enabled,
                LocalizerLanguageId = localizer.m_langID,
                LocalPosition = rect.localPosition,
                LocalRotation = rect.localRotation,
                LocalScale = rect.localScale,
                AnchorMin = rect.anchorMin,
                AnchorMax = rect.anchorMax,
                Pivot = rect.pivot,
                Size = rect.sizeDelta,
            };
        }

        private static ToggleState CaptureToggle(GameObject gameObject, ButtonCollider collider, Animator animator)
        {
            return new ToggleState
            {
                Active = gameObject.activeSelf,
                TouchActive = collider.TouchActive,
                EnableTouchArea = collider.EnableTouchArea,
                AnimatorEnabled = animator.enabled,
                Id = collider.m_id,
                OnTouch = collider.onTouch,
                OnRelease = collider.onRelease,
                OnTouchId = collider.onTouchID,
                OnReleaseId = collider.onReleaseID,
                AnimatorState = animator.GetCurrentAnimatorStateInfo(0),
            };
        }

        internal void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Row row in _rows)
            {
                if (row == null) continue;
                RestoreLabel(row);
                RestoreToggle(row);
            }
            foreach (GameObject gameObject in _created)
                if (gameObject != null) UnityEngine.Object.Destroy(gameObject);
            _created.Clear();
        }

        private static void RestoreLabel(Row row)
        {
            if (row.Label == null || row.LabelState == null) return;
            LabelState state = row.LabelState;
            RectTransform rect = row.Label.rectTransform;
            rect.SetParent(state.Parent, false);
            rect.SetSiblingIndex(state.SiblingIndex);
            rect.anchorMin = state.AnchorMin;
            rect.anchorMax = state.AnchorMax;
            rect.pivot = state.Pivot;
            rect.sizeDelta = state.Size;
            rect.localPosition = state.LocalPosition;
            rect.localRotation = state.LocalRotation;
            rect.localScale = state.LocalScale;
            row.Label.SetText(state.Text);
            row.Label.fontSize = state.FontSize;
            row.Label.fontSizeMin = state.FontSizeMin;
            row.Label.fontSizeMax = state.FontSizeMax;
            row.Label.color = state.Color;
            row.Label.alignment = state.Alignment;
            row.Label.enableAutoSizing = state.AutoSize;
            row.Label.gameObject.SetActive(state.Active);
            if (row.Localizer != null)
            {
                row.Localizer.m_langID = state.LocalizerLanguageId;
                row.Localizer.enabled = state.LocalizerEnabled;
            }
        }

        private static void RestoreToggle(Row row)
        {
            if (row.Toggle == null || row.ToggleState == null) return;
            ToggleState state = row.ToggleState;
            row.Toggle.SetActive(state.Active);
            if (row.ToggleCollider != null)
            {
                row.ToggleCollider.TouchActive = state.TouchActive;
                row.ToggleCollider.EnableTouchArea = state.EnableTouchArea;
                row.ToggleCollider.m_id = state.Id;
                row.ToggleCollider.onTouch = state.OnTouch;
                row.ToggleCollider.onRelease = state.OnRelease;
                row.ToggleCollider.onTouchID = state.OnTouchId;
                row.ToggleCollider.onReleaseID = state.OnReleaseId;
            }
            if (row.ToggleAnimator != null)
            {
                row.ToggleAnimator.enabled = state.AnimatorEnabled;
                if (state.AnimatorEnabled)
                    row.ToggleAnimator.Play(state.AnimatorState.fullPathHash, 0, state.AnimatorState.normalizedTime);
            }
        }

        private static SettingDefinition Bool(string name, string section, string key, string label, bool live = false) =>
            new(name, section, key, label, 0f, 1f, live);

        private static SettingDefinition Float(string name, string section, string key, string label,
            float minimum, float maximum, bool live = false) =>
            new(name, section, key, label, minimum, maximum, live);

        private static SettingDefinition[] CreateDefinitions()
        {
            SettingDefinition[] definitions =
            [
                Bool(nameof(Settings.LoadVideos), "CustomSongs", "LoadVideos", "Load song videos"),
                Float(nameof(Settings.CenterVideoDim), "CustomSongs", "CenterVideoDim", "Center video dim", 0f, 1f),
                Bool(nameof(Settings.DecimalNotation), "CustomSongs", "DecimalNotation", "Decimal level notation"),
                Bool(nameof(Settings.DisplayFolderName), "CustomSongs", "DisplayFolderName", "Show collection names"),
                Bool(nameof(Settings.DisplayFolderPicker), "CustomSongs", "DisplayFolderPicker", "Show folder picker"),
                Bool(nameof(Settings.BeginnerOnlyEightNotes), "CustomSongs", "BeginnerOnlyEightNotes", "Beginner: eighth notes only"),
                Bool(nameof(Settings.RideOnRightCymbal), "CustomSongs", "RideOnRightCymbal", "Ride on right cymbal"),
                Float(nameof(Settings.MasterVolume), "Audio", "MasterVolume", "Master volume", 0f, 4f, true),
                Float(nameof(Settings.BgmVolume), "Audio", "BgmVolume", "BGM volume", 0f, 4f, true),
                Float(nameof(Settings.DrumVolume), "Audio", "DrumVolume", "Drum volume", 0f, 4f, true),
                Bool(nameof(Settings.ForceQuitOnSignal), "Game", "ForceQuitOnSignal", "Force quit on signal"),
                Bool(nameof(Settings.QuitButton), "Game", "QuitButton", "Show Quit Game button"),
                Float(nameof(Settings.GameplayPanelOpacity), "Game", "GameplayPanelOpacity", "Gameplay panel opacity", 0f, 1f, true),
                Float(nameof(Settings.StoolHeight), "Game", "StoolHeight", "Stool height (m)", -0.5f, 0.5f, true),
                Float(nameof(Settings.StoolOffsetX), "Game", "StoolOffsetX", "Stool horizontal offset (m)", -0.5f, 0.5f, true),
                Float(nameof(Settings.StoolOffsetZ), "Game", "StoolOffsetZ", "Stool forward offset (m)", -0.5f, 0.5f, true),
                Bool(nameof(Settings.AutoOfflineMode), "Game", "AutoOfflineMode", "Start in offline mode"),
                Bool(nameof(Settings.SkipTitleScreen), "Game", "SkipTitleScreen", "Skip title screens"),
                Bool(nameof(Settings.SkipCautionScreen), "Game", "SkipCautionScreen", "Skip caution screen"),
                Bool(nameof(Settings.SkipPostGameGesture), "Game", "SkipPostGameGesture", "Skip post-game gesture"),
                Bool(nameof(Settings.SkipPreGameGesture), "Game", "SkipPreGameGesture", "Skip pre-game gesture"),
                Bool(nameof(Settings.GrabWithoutWait), "Game", "GrabWithoutWait", "Grab info panel immediately"),
                Bool(nameof(Settings.GrabWithoutVibration), "Game", "GrabWithoutVibration", "Disable info-panel vibration"),
                Bool(nameof(Settings.MoveResultMenuToTheSide), "Game", "MoveResultMenuToTheSide", "Move results menu aside"),
                Bool(nameof(Settings.ShowDifficultyOnResultScreen), "Game", "ShowDifficultyOnResultScreen", "Show result difficulty"),
                Bool(nameof(Settings.DisplayFastSlow), "Game", "DisplayFastSlow", "Show FAST / SLOW", true),
                Bool(nameof(Settings.DisplayFastSlowOnPerfects), "Game", "DisplayFastSlowOnPerfects", "Show timing on Perfect", true),
                Bool(nameof(Settings.UnlockAllAvatarParts), "Game", "UnlockAllAvatarParts", "Unlock avatar parts"),
                Bool(nameof(Settings.UnlockCampaignRewards), "Game", "UnlockCampaignRewards", "Unlock campaign rewards"),
                Bool(nameof(Settings.HideButtonsUnavailableOffline), "Game", "HideButtonsUnavailableOffline", "Hide online-only buttons"),
                Float(nameof(Settings.VirtualScreenScale), "VirtualScreen", "Scale", "Virtual screen size", 0.25f, 4f, true),
                Float(nameof(Settings.VirtualScreenOffsetY), "VirtualScreen", "OffsetY", "Virtual screen height (m)", -5f, 5f, true),
                Float(nameof(Settings.VirtualScreenDistanceScale), "VirtualScreen", "DistanceScale", "Virtual screen distance", 0.25f, 4f, true),
                Float(nameof(Settings.VirtualScreenBackgroundOpacity), "VirtualScreen", "BackgroundOpacity", "Virtual screen opacity", 0f, 1f, true),
                Bool(nameof(Settings.VirtualScreenShowAuto), "VirtualScreen", "ShowAuto", "Show automatic notes", true),
                Bool(nameof(Settings.VirtualScreenGrayAuto), "VirtualScreen", "GrayAuto", "Gray automatic notes", true),
                Bool(nameof(Settings.SpectatorThirdPersonKeyboardMouseControl), "Spectator", "ThirdPersonKeyboardMouseControl", "Spectator keyboard/mouse control"),
                Bool(nameof(Settings.SpectatorCinematicGameplay), "Spectator", "CinematicGameplay", "Cinematic spectator camera"),
                Bool(nameof(Settings.SpectatorSongInfo), "Spectator", "SongInfo", "Show spectator song info"),
                Bool(nameof(Settings.SpectatorCustomTitle), "Spectator", "CustomTitle", "Show custom spectator title"),
                Bool(nameof(Settings.SpectatorShowVirtualScreen), "Spectator", "ShowVirtualScreen", "Show virtual screen to spectator"),
                Bool(nameof(Settings.ShowStats), "SongSelect", "ShowStats", "Show song wheel stats"),
                Float(nameof(Settings.SongWheelRadiusScale), "SongSelect", "SongWheelRadiusScale", "Song wheel size", 0.5f, 2f, true),
                Float(nameof(Settings.SongWheelDistanceScale), "SongSelect", "SongWheelDistanceScale", "Song wheel distance", 0.5f, 4f, true),
                Bool(nameof(Settings.SongWheelForwardOnly), "SongSelect", "SongWheelForwardOnly", "Keep song wheel in front"),
                Float(nameof(Settings.SongWheelForwardArc), "SongSelect", "SongWheelForwardArc", "Song wheel arc (degrees)", 30f, 360f, true),
                Bool(nameof(Settings.SongWheelRememberPosition), "SongSelect", "SongWheelRememberPosition", "Remember song wheel position"),
                Bool(nameof(Settings.SongWheelPlayPreviewByTouching), "SongSelect", "SongWheelPlayPreviewByTouching", "Preview songs by touching"),
                Bool(nameof(Settings.LazySongWheel), "Performance", "LazySongWheel", "Load song wheel lazily"),
                Bool(nameof(Settings.BackgroundJacketDecoding), "Performance", "BackgroundJacketDecoding", "Decode jackets in background"),
                Bool(nameof(Settings.PlayerInfoOnlyPlayedCustomSongs), "Performance", "PlayerInfoOnlyPlayedCustomSongs", "Hide unplayed custom songs"),
                Bool(nameof(Settings.PlayerInfoHideCustomSongs), "Performance", "PlayerInfoHideCustomSongs", "Hide custom songs from player info"),
            ];
            ValidateDefinitions(definitions);
            return definitions;
        }

        private static void ValidateDefinitions(SettingDefinition[] definitions)
        {
            string[] declared = typeof(Settings).GetFields(BindingFlags.Instance | BindingFlags.Public)
                .Where(field => field.FieldType == typeof(bool) || field.FieldType == typeof(float))
                .Select(field => field.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            string[] exposed = definitions.Select(definition => definition.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            if (!declared.SequenceEqual(exposed, StringComparer.Ordinal))
                throw new InvalidOperationException("The Mod settings list must expose every boolean and float setting exactly once.");
            foreach (SettingDefinition definition in definitions)
            {
                if (definition.IsBoolean == definition.IsFloat || definition.Maximum <= definition.Minimum)
                    throw new InvalidOperationException($"Invalid Mod settings definition for {definition.Name}.");
            }
        }
    }
}
