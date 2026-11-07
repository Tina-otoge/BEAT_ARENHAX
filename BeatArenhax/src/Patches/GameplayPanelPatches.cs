using System;
using UnityEngine;
using UnityEngine.UI;

namespace BeatArenhax
{
    internal static class GameplayPanelPatches
    {
        private static Image _background;
        private static Color _original;
        private static float _nextFind;
        private static float _retryAt;

        internal static void Reset()
        {
            if (_background != null) _background.color = _original;
            _background = null;
            _nextFind = _retryAt = 0;
        }

        internal static void Update()
        {
            float opacity = Plugin.Settings.GameplayPanelOpacity;
            if (_background == null && opacity == 1f) return;
            if (Time.realtimeSinceStartup < _retryAt) return;
            try
            {
                if (_background == null)
                {
                    if (Time.realtimeSinceStartup < _nextFind) return;
                    _nextFind = Time.realtimeSinceStartup + 1;
                    BackScreen screen = UnityEngine.Object.FindObjectOfType<BackScreen>();
                    if (screen == null || screen.m_backScreen == null) return;
                    Transform node = screen.m_backScreen.transform.Find("Ingame_item/Img_Background");
                    _background = node != null ? node.GetComponent<Image>() : null;
                    if (_background == null)
                        throw new InvalidOperationException("Gameplay panel prefab has no Ingame_item/Img_Background Image.");
                    _original = _background.color;
                }
                Color color = _original;
                color.a *= opacity;
                _background.color = color;
            }
            catch (Exception error)
            {
                _retryAt = Time.realtimeSinceStartup + 10;
                Plugin.L.LogError("Gameplay panel opacity: " + error);
            }
        }
    }
}
