using System;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;
using UnityEngine;

namespace BeatArenhax
{
    internal static class QuitButtonPatches
    {
        private const int QuitIndex = (int)HomeSystemMenu.Menu.Max;
        private static HomeSystemMenu _owner;
        private static UIListView _menu;
        private static HomeSystemMenu _confirming;

        internal static void Apply(Harmony h)
        {
            if (!Plugin.Settings.QuitButton) return;
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(HomeSystemMenu), "Start", nameof(Start_Pre), nameof(Start_Post));
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(UIListView), "MakeList", null, nameof(MakeList_Post));
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(HomeSystemMenu), nameof(HomeSystemMenu.Open), nameof(Open_Pre));
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(HomeSystemMenu), "SelectItem", nameof(SelectItem_Pre), nameof(SelectItem_Post));
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(HomeSystemMenu), "CancelBackTitle", nameof(CancelBackTitle_Pre));
            Patches.Patch(h, typeof(QuitButtonPatches), typeof(HomeSystemMenu), "BackTitle", nameof(BackTitle_Pre));
        }

        private static void Start_Pre(HomeSystemMenu __instance)
        {
            _owner = null;
            _menu = null;
            _confirming = null;
            try
            {
                Il2CppStringArray titles = __instance.MENU_TITLE_LABELS;
                if (titles == null || titles.Length != QuitIndex)
                    throw new InvalidOperationException("Unexpected system menu items.");
                Il2CppStringArray expanded = new(QuitIndex + 1);
                for (int i = 0; i < QuitIndex; i++) expanded[i] = titles[i];
                expanded[QuitIndex] = "";
                __instance.MENU_TITLE_LABELS = expanded;
                _owner = __instance;
            }
            catch (Exception e) { Plugin.L.LogError("Adding the quit menu item failed: " + e); }
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
                if (menu.m_menuString == null || menu.m_menuString.Count <= QuitIndex
                    || menu.m_menuString[QuitIndex] != "" || menu.m_btnObj == null
                    || menu.m_btnObj.Count <= QuitIndex)
                    throw new InvalidOperationException("Unexpected system menu list.");
                GameObject button = menu.m_btnObj[QuitIndex];
                Transform label = button != null ? button.transform.Find("Txt") : null;
                Bvr.TamamoText localizer = label != null ? label.GetComponent<Bvr.TamamoText>() : null;
                TextMeshProUGUI text = label != null ? label.GetComponent<TextMeshProUGUI>() : null;
                if (localizer == null || text == null) throw new InvalidOperationException("Quit button label is missing.");
                localizer.StopAllCoroutines();
                localizer.m_langID = "";
                localizer.enabled = false;
                text.SetText("Quit Game");
                Plugin.L.LogInfo("Added Quit Game to the lobby system menu.");
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Setting the quit menu label failed: " + e);
                if (menu.m_isBtnsDisplay != null && menu.m_isBtnsDisplay.Length > QuitIndex)
                    menu.SetButtonDisplay(QuitIndex, false);
            }
        }

        private static void Open_Pre(HomeSystemMenu __instance)
        {
            if (!IsOwner(__instance)) return;
            __instance.m_SystemMenuView.SetButtonActive(QuitIndex, true);
        }

        private static bool SelectItem_Pre(HomeSystemMenu __instance, ref int index, out bool __state)
        {
            __state = false;
            if (index != QuitIndex || !IsOwner(__instance)) return true;
            UIAlertView dialog = __instance.m_backToTitle;
            if (dialog == null || !dialog.IsReady || dialog.m_messageText == null
                || dialog.m_alertButtons == null || dialog.m_alertButtons.Count < 2)
            {
                Plugin.L.LogError("Cannot open quit confirmation: the system dialog is not ready.");
                return false;
            }

            __state = true;
            index = (int)HomeSystemMenu.Menu.Title;
            return true;
        }

        private static bool IsOwner(HomeSystemMenu menu) =>
            _owner != null && menu != null && _owner.Pointer == menu.Pointer;

        private static void SelectItem_Post(HomeSystemMenu __instance, bool __state)
        {
            if (!__state) return;
            UIAlertView dialog = __instance.m_backToTitle;
            try
            {
                SetDialogText(dialog.m_messageText, "Are you sure you want to quit the game?");
                SetDialogText(dialog.m_alertButtons[0].tamamoText, "NO");
                SetDialogText(dialog.m_alertButtons[1].tamamoText, "YES");
                _confirming = __instance;
            }
            catch (Exception e)
            {
                Plugin.L.LogError("Opening quit confirmation failed: " + e);
                RestoreDialog(dialog);
                __instance.CancelBackTitle();
            }
        }

        private static void SetDialogText(Bvr.TamamoText localizer, string value)
        {
            if (localizer == null) throw new InvalidOperationException("Confirmation label is missing.");
            TextMeshProUGUI text = localizer.GetComponent<TextMeshProUGUI>();
            if (text == null) throw new InvalidOperationException("Confirmation text is missing.");
            localizer.StopAllCoroutines();
            localizer.enabled = false;
            text.SetText(value);
        }

        private static void RestoreDialog(UIAlertView dialog)
        {
            if (dialog.m_messageText != null) dialog.m_messageText.enabled = true;
            for (int i = 0; i < 2 && i < dialog.m_alertButtons.Count; i++)
            {
                Bvr.TamamoText localizer = dialog.m_alertButtons[i].tamamoText;
                if (localizer != null) localizer.enabled = true;
            }
            dialog.Message = "system_dialog_back_to_title";
            dialog.SetButtonText(UIAlertView.Btn.Left, "common_dialog_btn_cancel");
            dialog.SetButtonText(UIAlertView.Btn.Right, "common_dialog_btn_ok");
        }

        private static void CancelBackTitle_Pre(HomeSystemMenu __instance)
        {
            if (_confirming == null || _confirming.Pointer != __instance.Pointer) return;
            _confirming = null;
            RestoreDialog(__instance.m_backToTitle);
        }

        private static bool BackTitle_Pre(HomeSystemMenu __instance)
        {
            if (_confirming == null || _confirming.Pointer != __instance.Pointer) return true;
            _confirming = null;
            Plugin.QuitGame();
            return false;
        }
    }
}
