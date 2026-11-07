using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace BeatArenhax
{
    internal static class ModSettingsMenuPatches
    {
        private const int QuitIndex = (int)HomeSystemMenu.Menu.Max;
        private static HomeSystemMenu _owner;
        private static UIListView _menu;
        private static HomeOptionMenu _optionMenu;
        private static UiGameSettingWindow _window;
        private static ModSettingsPanel _panel;
        private static int _menuIndex = -1;
        private static bool _active;
        private static bool _windowOpened;
        private static float _openRequestedAt;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(HomeSystemMenu), "Start",
                nameof(Start_Pre), nameof(Start_Post), prefixPriority: Priority.Last);
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(UIListView), "MakeList", null, nameof(MakeList_Post));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(HomeSystemMenu), nameof(HomeSystemMenu.Open), nameof(Open_Pre));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(HomeSystemMenu), "SelectItem", nameof(SelectItem_Pre));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(HomeOptionMenu), "ChildCloseCallback", nameof(ChildCloseCallback_Pre));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(UiGameSettingWindow), "Open", null, nameof(GameSettingsOpen_Post));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(UiGameSettingWindow), "SetButtonMessages", null, nameof(SetButtonMessages_Post));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(UiGameSettingWindow), "SelectIndex", nameof(SelectIndex_Pre));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(UiGameSettingWindow), "SelectOK", nameof(SelectOK_Pre));
            Patches.Patch(h, typeof(ModSettingsMenuPatches), typeof(ButtonCollider), "Update", nameof(ButtonUpdate_Pre));
        }

        private static void Start_Pre(HomeSystemMenu __instance)
        {
            _owner = null;
            _menu = null;
            _optionMenu = null;
            _window = null;
            _panel = null;
            _menuIndex = -1;
            _active = false;
            _windowOpened = false;
            _openRequestedAt = 0f;
            try
            {
                Il2CppStringArray titles = __instance.MENU_TITLE_LABELS;
                if (titles == null || titles.Length < QuitIndex || titles.Length > QuitIndex + 1)
                    throw new InvalidOperationException("Unexpected system menu items.");
                int index = titles.Length;
                Il2CppStringArray expanded = new(index + 1);
                for (int i = 0; i < index; i++) expanded[i] = titles[i];
                expanded[index] = "";
                __instance.MENU_TITLE_LABELS = expanded;
                _menuIndex = index;
                _owner = __instance;
            }
            catch (Exception e) { Plugin.L.LogError("Adding the Mod settings menu item failed: " + e); }
        }

        private static void Start_Post(HomeSystemMenu __instance)
        {
            if (!IsOwner(__instance)) return;
            _menu = __instance.m_SystemMenuView;
            if (_menu != null && _menu.m_btnObj != null) SetLabel(_menu);
        }

        private static void MakeList_Post(UIListView __instance)
        {
            if (_menu != null && _menu.Pointer == __instance.Pointer) SetLabel(__instance);
        }

        private static void SetLabel(UIListView menu)
        {
            try
            {
                if (menu.m_menuString == null || menu.m_menuString.Count <= _menuIndex
                    || menu.m_menuString[_menuIndex] != "" || menu.m_btnObj == null
                    || menu.m_btnObj.Count <= _menuIndex)
                    throw new InvalidOperationException("Unexpected system menu list.");
                GameObject button = menu.m_btnObj[_menuIndex];
                Transform label = button != null ? button.transform.Find("Txt") : null;
                Bvr.TamamoText localizer = label != null ? label.GetComponent<Bvr.TamamoText>() : null;
                TextMeshProUGUI text = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
                if (localizer == null || text == null) throw new InvalidOperationException("Mod settings button label is missing.");
                localizer.StopAllCoroutines();
                localizer.m_langID = "";
                localizer.enabled = false;
                text.SetText("Mod settings");
                Plugin.L.LogInfo("Added Mod settings to the lobby system menu.");
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Setting the Mod settings menu label failed: " + e);
                if (menu.m_isBtnsDisplay != null && menu.m_isBtnsDisplay.Length > _menuIndex)
                    menu.SetButtonDisplay(_menuIndex, false);
            }
        }

        private static void Open_Pre(HomeSystemMenu __instance)
        {
            if (!IsOwner(__instance) || _menuIndex < 0) return;
            __instance.m_SystemMenuView.SetButtonActive(_menuIndex, true);
        }

        private static bool SelectItem_Pre(HomeSystemMenu __instance, ref int index)
        {
            if (!IsOwner(__instance) || index != _menuIndex) return true;
            if (_active) return false;

            try
            {
                _optionMenu = __instance.m_optionMenu;
                _window = _optionMenu != null ? _optionMenu.m_uiGameSettingWindow : null;
                if (_window == null)
                    throw new InvalidOperationException("The native Game Settings window is unavailable.");

                _active = true;
                _openRequestedAt = Time.realtimeSinceStartup;
                __instance.m_SystemMenuView.Close();
                OpenWhenReady();
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Opening Mod settings failed: " + e);
                EndModSettings(true);
            }
            return false;
        }

        private static bool ChildCloseCallback_Pre(HomeOptionMenu __instance)
        {
            if (!_active || _optionMenu == null || __instance == null
                || _optionMenu.Pointer != __instance.Pointer) return true;
            EndModSettings(true);
            return false;
        }

        private static void GameSettingsOpen_Post(UiGameSettingWindow __instance)
        {
            if (MatchesActiveWindow(__instance)) TryInitializePanel();
        }

        private static void SetButtonMessages_Post(UiGameSettingWindow __instance)
        {
            if (Owns(__instance)) Render();
        }

        private static bool SelectIndex_Pre(UiGameSettingWindow __instance, int index)
        {
            if (!Owns(__instance)) return true;
            if (index == 6) __instance.Close();
            return false;
        }

        private static bool SelectOK_Pre(UiGameSettingWindow __instance, int index)
        {
            if (!Owns(__instance)) return true;
            if (index == 6) __instance.Close();
            return false;
        }

        private static void ButtonUpdate_Pre(ButtonCollider __instance)
        {
            if (_active && _panel != null) _panel.UpdateSliderTouch(__instance);
        }

        private static void Render()
        {
            try { _panel?.Render(); }
            catch (Exception e) { Plugin.L.LogError("Rendering Mod settings failed: " + e); }
        }

        private static bool Owns(UiGameSettingWindow window) =>
            _active && _panel != null && _window != null && window != null && _window.Pointer == window.Pointer;

        private static bool MatchesActiveWindow(UiGameSettingWindow window) =>
            _active && _window != null && window != null && _window.Pointer == window.Pointer;

        internal static void Update()
        {
            if (!_active) return;
            if (!_windowOpened) OpenWhenReady();
            else if (_panel == null) TryInitializePanel();
        }

        private static void OpenWhenReady()
        {
            if (!_active || _windowOpened || _window == null) return;
            if (!_window.IsReady)
            {
                if (Time.realtimeSinceStartup - _openRequestedAt < 30f) return;
                Plugin.L.LogError("The native Game Settings window did not become ready within 30 seconds.");
                EndModSettings(true);
                return;
            }
            try
            {
                _windowOpened = true;
                _window.Open();
                Plugin.L.LogInfo("Opened Mod settings in the native Game Settings window.");
                TryInitializePanel();
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Opening the native Game Settings window failed: " + e);
                EndModSettings(true);
            }
        }

        private static void TryInitializePanel()
        {
            if (!_active || _panel != null || _window == null || !_window.IsReady) return;
            try
            {
                _panel = new ModSettingsPanel(_window);
                Render();
                Plugin.L.LogInfo("Mod settings controls are ready.");
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Initializing Mod settings controls failed: " + e);
                EndModSettings(true);
            }
        }

        private static bool IsOwner(HomeSystemMenu menu) =>
            _owner != null && menu != null && _owner.Pointer == menu.Pointer;

        private static void EndModSettings(bool reopenMenu)
        {
            HomeSystemMenu owner = _owner;
            ModSettingsPanel panel = _panel;
            _panel = null;
            _window = null;
            _optionMenu = null;
            _active = false;
            _windowOpened = false;
            _openRequestedAt = 0f;

            try { panel?.Dispose(); }
            catch (Exception e) { Plugin.L.LogError("Restoring the native Game Settings window failed: " + e); }

            if (!reopenMenu || owner == null) return;
            try { owner.Open(); }
            catch (Exception e) { Plugin.L.LogError("Returning to the lobby system menu failed: " + e); }
        }
    }
}
