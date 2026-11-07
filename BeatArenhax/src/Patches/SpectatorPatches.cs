using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using BeatArenhax.CustomSongs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace BeatArenhax
{
    internal static class SpectatorPatches
    {
        private const float CameraDistance = 1.5f;
        private const float CameraHeight = 1.5f;
        private const float LobbyCameraHeight = CameraHeight - 0.2f;
        private const float CameraPitch = SpectatorCameraControl.DefaultPitch;

        private static Camera _camera;
        private static Transform _eye;
        private static Vector3 _offset;
        private static float _height;
        private static Quaternion _heading;
        private static readonly CinematicCameraDirector Director = new(new System.Random());
        private static readonly SpectatorCameraControl CameraControl = new();
        private static Vector3 _previousMouse;
        private static bool _hasPreviousMouse;
        private sealed class NpcView
        {
            internal Transform Head;
            internal SkinnedMeshRenderer[] Renderers;
        }

        private static readonly List<NpcView> NpcViews = [];
        private static readonly List<(SkinnedMeshRenderer renderer, bool enabled)> NpcHidden = [];
        private static readonly Action<ScriptableRenderContext, Camera> BeginNpcView = BeginNpcRendering;
        private static readonly Action<ScriptableRenderContext, Camera> EndNpcView = EndNpcRendering;
        private static NpcView _npcView;
        private static bool _npcRenderFailed;
        private static CinematicShot _previousShot;
        private static float _nextNpcFind;
        private static int _sourceCullingMask;
        private static float _sourceFieldOfView;
        private static readonly Il2CppSystem.Collections.Generic.List<XRNodeState> TrackingNodes = new();
        private static bool _noNpcWarning;
        private static bool _savedDeviceView;
        private static bool _ownsDeviceView;
        private static Bvr.Player _player;
        private static float _nextEyeFind;
        private static float _nextPlayerFind;
        private static float _nextInfo;
        private static string _banner = "";
        private static string _stats = "";
        private static GUIStyle _bannerStyle;
        private static GUIStyle _statsStyle;
        private static bool _drawFailed;
        private static IntPtr _titleWindow;
        private static string _lastWindowTitle;
        private static bool _titleErrorLogged;

        internal static void Apply()
        {
            Plugin.L.LogInfo($"Spectator: Camera={Plugin.Settings.SpectatorCamera}, SongInfo={Plugin.Settings.SpectatorSongInfo}, CustomTitle={Plugin.Settings.SpectatorCustomTitle}");
            if (Plugin.Settings.SpectatorCinematicGameplay)
            {
                RenderPipelineManager.beginCameraRendering += BeginNpcView;
                RenderPipelineManager.endCameraRendering += EndNpcView;
            }
        }

        internal static void Shutdown()
        {
            RenderPipelineManager.beginCameraRendering -= BeginNpcView;
            RenderPipelineManager.endCameraRendering -= EndNpcView;
            ResetScene();
        }

        internal static bool IsSpectatorCamera(Camera camera) => _camera != null && camera == _camera;

        internal static void ResetScene()
        {
            if (_camera != null) UnityEngine.Object.Destroy(_camera.gameObject);
            _camera = null;
            if (_ownsDeviceView) XRSettings.showDeviceView = _savedDeviceView;
            _ownsDeviceView = false;
            _eye = null;
            _player = null;
            _banner = "";
            _stats = "";
            _nextEyeFind = 0;
            _nextPlayerFind = 0;
            _nextInfo = 0;
            _drawFailed = false;
            Director.Update(false, 0f);
            RestoreNpcRenderers();
            NpcViews.Clear();
            _npcView = null;
            _npcRenderFailed = false;
            _previousShot = CinematicShot.Standard;
            _nextNpcFind = 0f;
            _noNpcWarning = false;
            CameraControl.Reset();
            _hasPreviousMouse = false;
        }

        internal static void Update()
        {
            if (Plugin.Settings.SpectatorCamera == SpectatorCamera.VR
                && !Plugin.Settings.SpectatorCinematicGameplay
                && !Plugin.Settings.SpectatorSongInfo && !Plugin.Settings.SpectatorCustomTitle) return;
            float now = Time.realtimeSinceStartup;
            try
            {
                if (_ownsDeviceView && _eye == null) ResetScene();
                if (_eye == null && now >= _nextEyeFind)
                {
                    _nextEyeFind = now + 0.5f;
                    GameObject avatar = GameObject.FindWithTag("Player");
                    Transform eye = avatar != null
                        ? avatar.transform.Find("OVRCameraRig/TrackingSpace/CameraEyeRoot/CenterEyeAnchor") : null;
                    AvatarInitialize initialize = avatar != null ? avatar.GetComponent<AvatarInitialize>() : null;
                    if (eye != null && (initialize == null || initialize.IsInitialized))
                    {
                        _eye = eye;
                        if (Plugin.Settings.SpectatorCamera != SpectatorCamera.VR || Plugin.Settings.SpectatorCinematicGameplay) CreateCamera(avatar);
                    }
                }
                if (_player == null && now >= _nextPlayerFind
                    && (Plugin.Settings.SpectatorCinematicGameplay
                        || Plugin.Settings.SpectatorSongInfo || Plugin.Settings.SpectatorCustomTitle))
                {
                    _nextPlayerFind = now + 0.5f;
                    _player = UnityEngine.Object.FindObjectOfType<Bvr.Player>();
                }
                if (_camera != null)
                {
                    if (_eye == null)
                    {
                        ResetScene();
                        return;
                    }
                    _camera.aspect = (float)Math.Max(1, Screen.width) / Math.Max(1, Screen.height);
                    bool renderCamera = UpdateCamera(now);
                    _camera.enabled = renderCamera;
                    XRSettings.showDeviceView = renderCamera ? false : _savedDeviceView;
                }
                if ((Plugin.Settings.SpectatorSongInfo || Plugin.Settings.SpectatorCustomTitle) && now >= _nextInfo)
                {
                    _nextInfo = now + 0.1f;
                    UpdateInfo(now);
                }
            }
            catch (Exception e)
            {
                ResetScene();
                _nextEyeFind = now + 5f;
                _nextPlayerFind = now + 5f;
                _nextInfo = now + 5f;
                Plugin.L.LogError("Spectator update failed: " + e);
            }
            UpdateWindowTitle();
        }

        private static void CreateCamera(GameObject avatar)
        {
            Camera source = _eye.GetComponent<Camera>();
            if (source == null) throw new InvalidOperationException("Tracked center-eye camera is unavailable.");
            int headLayer = LayerMask.NameToLayer("PlayerHead");
            if (headLayer < 0) throw new InvalidOperationException("PlayerHead layer is unavailable.");
            Transform rig = avatar.transform.Find("OVRCameraRig");
            Transform tracking = rig != null ? rig.Find("TrackingSpace") : null;
            if (tracking == null) throw new InvalidOperationException("Player tracking space is unavailable.");
            _heading = Quaternion.Euler(0f, rig.eulerAngles.y, 0f);
            _offset = _heading * new Vector3(0f, 0f, -CameraDistance);
            string scene = SceneManager.GetActiveScene().name;
            float heightOffset = scene is "TitleScene" or "SelectMusicScene" ? LobbyCameraHeight : CameraHeight;
            _height = tracking.position.y + heightOffset;
            GameObject root = new("BeatArenhax_SpectatorCamera");
            root.SetActive(false);
            try
            {
                _camera = root.AddComponent<Camera>();
                _camera.CopyFrom(source);
                // CopyFrom also copies the VR rig's custom view/projection matrices.
                _camera.ResetWorldToCameraMatrix();
                _camera.ResetProjectionMatrix();
                _camera.stereoTargetEye = StereoTargetEyeMask.None;
                _camera.targetTexture = null;
                _camera.rect = new Rect(0f, 0f, 1f, 1f);
                _camera.fieldOfView = 60f;
                _camera.nearClipPlane = 0.05f;
                _sourceCullingMask = source.cullingMask;
                _sourceFieldOfView = source.fieldOfView;
                _camera.cullingMask = source.cullingMask | (1 << headLayer);
                _camera.depth = source.depth + 1f;
                _camera.transform.rotation = _heading * Quaternion.Euler(CameraPitch, 0f, 0f);
                UniversalAdditionalCameraData data = root.AddComponent<UniversalAdditionalCameraData>();
                data.renderType = CameraRenderType.Base;
                UniversalAdditionalCameraData sourceData = source.GetComponent<UniversalAdditionalCameraData>();
                if (sourceData != null)
                {
                    data.renderPostProcessing = sourceData.renderPostProcessing;
                    data.volumeLayerMask = sourceData.volumeLayerMask;
                    data.renderShadows = sourceData.renderShadows;
                }
                // No VR fade/error camera stack, tracking scripts, or AudioListener is copied.
                _camera.aspect = (float)Math.Max(1, Screen.width) / Math.Max(1, Screen.height);
                _savedDeviceView = XRSettings.showDeviceView;
                _ownsDeviceView = true;
                if (Plugin.Settings.SpectatorCamera != SpectatorCamera.VR
                    && (Screen.width != Plugin.Settings.SpectatorWidth || Screen.height != Plugin.Settings.SpectatorHeight))
                    Screen.SetResolution(Plugin.Settings.SpectatorWidth, Plugin.Settings.SpectatorHeight, Screen.fullScreenMode);
                root.SetActive(true);
                _camera.enabled = true;
                Plugin.L.LogInfo($"Spectator camera ready: scene={scene}, viewport={Plugin.Settings.SpectatorWidth}x{Plugin.Settings.SpectatorHeight}, height offset={heightOffset:F2}, height={_height:F2}, yaw={rig.eulerAngles.y:F1}, PlayerHead layer={headLayer}");
            }
            catch
            {
                UnityEngine.Object.Destroy(root);
                _camera = null;
                throw;
            }
        }

        private static bool UpdateCamera(float now)
        {
            bool playing = Plugin.Settings.SpectatorCinematicGameplay
                && _player != null && _player.m_seqPlayer != null
                && _player.m_seqPlayer.m_state == SequencePlayer.State.PLAYING;
            if (playing && now >= _nextNpcFind)
            {
                _nextNpcFind = now + 1f;
                FindNpcViews();
                if (_npcView != null)
                {
                    bool stillAvailable = false;
                    foreach (NpcView view in NpcViews)
                        if (view.Head == _npcView.Head)
                        {
                            stillAvailable = true;
                            break;
                        }
                    if (!stillAvailable) _npcView = null;
                }
            }
            bool hasNpc = !_npcRenderFailed && NpcViews.Count > 0;
            Director.Update(playing, now, hasNpc, Plugin.Settings.SpectatorCinematicCameraTypes);
            bool unavailable = playing && !hasNpc
                && Plugin.Settings.SpectatorCinematicCameraTypes.Length == 1
                && Plugin.Settings.SpectatorCinematicCameraTypes[0] == CinematicShot.NpcFirstPerson;
            if (unavailable && !_noNpcWarning)
                Plugin.L.LogWarning("No NPC first-person camera is available; using the configured spectator view until one is ready.");
            _noNpcWarning = unavailable;
            if (_previousShot != Director.Shot)
            {
                _npcView = null;
                _previousShot = Director.Shot;
                Plugin.L.LogDebug($"Spectator cinematic: {Director.Shot}, duration={Director.Duration:F1}s");
            }
            if (Director.Shot == CinematicShot.NpcFirstPerson && _npcView == null && NpcViews.Count > 0)
                _npcView = NpcViews[System.Random.Shared.Next(NpcViews.Count)];
            Vector3 position = _eye.position + _offset;
            position.y = _height;
            Quaternion rotation = _heading * Quaternion.Euler(CameraPitch, 0f, 0f);
            if (Plugin.Settings.SpectatorCamera == SpectatorCamera.ThirdPerson
                && Plugin.Settings.SpectatorThirdPersonKeyboardMouseControl
                && Director.Shot == CinematicShot.Standard)
            {
                UpdateCameraControl();
                position += _heading * new Vector3(CameraControl.X, CameraControl.Y, CameraControl.Z);
                rotation = _heading * Quaternion.Euler(CameraPitch + CameraControl.Pitch, CameraControl.Yaw, 0f);
            }
            else
                _hasPreviousMouse = false;
            _camera.cullingMask = VirtualScreenPatches.SpectatorMask(_sourceCullingMask | (1 << LayerMask.NameToLayer("PlayerHead")));
            _camera.fieldOfView = 60f;
            if (Director.Shot == CinematicShot.PlayerFirstPerson)
            {
                position = _eye.position;
                rotation = _eye.rotation;
                if (!HasHeadTracking())
                {
                    position.y = _height;
                    rotation = _heading * Quaternion.Euler(CameraPitch, 0f, 0f);
                }
                _camera.cullingMask = VirtualScreenPatches.SpectatorMask(_sourceCullingMask);
                _camera.fieldOfView = _sourceFieldOfView;
            }
            else if (Director.Shot == CinematicShot.NpcFirstPerson && _npcView != null && _npcView.Head != null)
            {
                Vector3 target = _eye.position;
                target.y = _height - CameraHeight + 1.15f;
                Vector3 direction = target - _npcView.Head.position;
                if (direction.sqrMagnitude > 0.01f)
                {
                    rotation = Quaternion.LookRotation(direction, Vector3.up);
                    position = _npcView.Head.position + direction.normalized * 0.3f;
                }
            }
            else if (Director.Shot is not CinematicShot.Standard and not CinematicShot.NpcFirstPerson)
            {
                float angle = Director.Shot switch
                {
                    CinematicShot.ArcUp => Mathf.Lerp(-75f, 75f, Director.Progress),
                    CinematicShot.ArcDown => Mathf.Lerp(75f, -75f, Director.Progress),
                    CinematicShot.High => 35f,
                    CinematicShot.Low => -35f,
                    CinematicShot.BackLeft => Mathf.Lerp(145f, 200f, Director.Progress),
                    CinematicShot.BackRight => Mathf.Lerp(215f, 160f, Director.Progress),
                    _ => throw new InvalidOperationException("Unknown cinematic spectator shot."),
                };
                float height = Director.Shot switch
                {
                    CinematicShot.ArcUp or CinematicShot.BackLeft => Mathf.Lerp(1.2f, 2.5f, Director.Progress),
                    CinematicShot.ArcDown or CinematicShot.BackRight => Mathf.Lerp(2.5f, 1.2f, Director.Progress),
                    CinematicShot.High => 3.2f,
                    CinematicShot.Low => 1.0f,
                    _ => throw new InvalidOperationException("Unknown cinematic spectator height."),
                };
                float distance = Director.Shot == CinematicShot.High ? 4.2f : 3.1f;
                float radians = angle * Mathf.Deg2Rad;
                Vector3 center = _eye.position;
                center.y = _height - CameraHeight;
                position = center + _heading * new Vector3(Mathf.Sin(radians) * distance, height, Mathf.Cos(radians) * distance);
                Vector3 target = center + Vector3.up * 1.1f;
                rotation = Quaternion.LookRotation(target - position, Vector3.up);
            }
            _camera.transform.SetPositionAndRotation(position, rotation);
            return Plugin.Settings.SpectatorCamera != SpectatorCamera.VR
                || (playing && Director.Shot != CinematicShot.Standard);
        }

        private static void UpdateCameraControl()
        {
            if (!Application.isFocused)
            {
                _hasPreviousMouse = false;
                return;
            }
            Vector3 mouse = Input.mousePosition;
            Vector3 delta = _hasPreviousMouse ? mouse - _previousMouse : Vector3.zero;
            _previousMouse = mouse;
            _hasPreviousMouse = true;
            bool left = Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.Q) || Input.GetKey(KeyCode.LeftArrow);
            bool right = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow);
            bool forward = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.Z) || Input.GetKey(KeyCode.UpArrow);
            bool back = Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);
            CameraControl.Update((right ? 1 : 0) - (left ? 1 : 0), (forward ? 1 : 0) - (back ? 1 : 0),
                Time.unscaledDeltaTime, delta.x, delta.y,
                Input.GetMouseButton(0) && !Input.GetMouseButtonDown(0),
                Input.GetMouseButton(1) && !Input.GetMouseButtonDown(1), Input.GetKeyDown(KeyCode.Space));
        }

        private static bool HasHeadTracking()
        {
            TrackingNodes.Clear();
            InputTracking.GetNodeStates(TrackingNodes);
            foreach (XRNodeState node in TrackingNodes)
                if ((node.nodeType == XRNode.CenterEye || node.nodeType == XRNode.Head)
                    && node.m_Tracked != 0 && node.TryGetPosition(out _) && node.TryGetRotation(out _))
                    return true;
            return false;
        }

        private static void FindNpcViews()
        {
            NpcViews.Clear();
            if (_player == null || _player.m_members == null) return;
            foreach (GameObject member in _player.m_members)
            {
                if (member == null || !member.activeInHierarchy) continue;
                Bvr.Npc npc = member.GetComponent<Bvr.Npc>();
                Transform head = npc != null ? npc.m_headTransform : null;
                if (head == null)
                {
                    AvatarReplay replay = member.GetComponent<AvatarReplay>();
                    if (replay != null) head = replay.headBone;
                }
                if (head != null && (head.position - _eye.position).sqrMagnitude > 0.25f)
                {
                    Transform avatar = member.transform.Find("MainVTuber");
                    if (avatar != null)
                        NpcViews.Add(new NpcView { Head = head, Renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true) });
                }
            }
        }

        private static void BeginNpcRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_npcRenderFailed || !IsSpectatorCamera(camera) || Director.Shot != CinematicShot.NpcFirstPerson
                || _npcView == null || _npcView.Head == null) return;
            try
            {
                foreach (SkinnedMeshRenderer renderer in _npcView.Renderers)
                {
                    if (renderer == null) continue;
                    NpcHidden.Add((renderer, renderer.enabled));
                    renderer.enabled = false;
                }
            }
            catch (Exception e)
            {
                RestoreNpcRenderers();
                _npcRenderFailed = true;
                Plugin.L.LogError("NPC first-person spectator rendering failed: " + e);
            }
        }

        private static void EndNpcRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!IsSpectatorCamera(camera)) return;
            RestoreNpcRenderers();
        }

        private static void RestoreNpcRenderers()
        {
            foreach ((SkinnedMeshRenderer renderer, bool enabled) in NpcHidden)
                if (renderer != null) renderer.enabled = enabled;
            NpcHidden.Clear();
        }

        private static void UpdateInfo(float now)
        {
            if (_player == null)
            {
                _banner = "";
                _stats = "";
                return;
            }
            if (_player == null || _player.m_seqPlayer == null)
            {
                _banner = "";
                _stats = "";
                return;
            }
            SequencePlayer sequence = _player.m_seqPlayer;
            PlayerInfoManager playerInfo = PlayerInfoManager.GetInstance();
            MusicInfo music = playerInfo.CurrentMusicInfo();
            if (music == null)
            {
                _banner = "";
                _stats = "";
                return;
            }
            CustomSong custom = Patches.SongById(music.Id);
            bool casual = sequence.Difficulty == Difficulty.Basic;
            string difficulty = casual ? "BSC" : "ADV";
            if (custom != null && playerInfo.CurrentPlayType == PlayType.Drum)
            {
                CustomSongChart chart = CustomScorePatches.Charts(custom)[CustomScorePatches.SelectedIndex(custom)];
                difficulty = chart.GetDifficultyTag(!casual);
            }
            string folder = custom == null ? "Original songs"
                : !string.IsNullOrEmpty(custom.DisplayFolderName) ? custom.DisplayFolderName : Path.GetFileName(custom.Folder);
            _banner = SingleLine($"{custom?.Artist ?? music.ArtistName} - {custom?.Title ?? music.MusicName} [{difficulty}] ({folder})");
            if (!Plugin.Settings.SpectatorSongInfo)
            {
                _stats = "";
                return;
            }
            SequencePlayer.ResultData result = sequence.Result;
            if (result == null || result.judge == null || result.judge.Length < (int)SequencePlayer.JUDGE_KIND.NUM)
                throw new InvalidOperationException("Live score counters are unavailable.");
            _stats = $"MAX COMBO: {result.maxCombo}\n"
                + $"PF: {result.judge[0]}\nGR: {result.judge[1]}\nGD: {result.judge[2]}\nBD: {result.judge[3]}\nMS: {result.judge[4]}";
        }

        private static string SingleLine(string text) => text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');

        private static void UpdateWindowTitle()
        {
            if (!Plugin.Settings.SpectatorCustomTitle) return;
            string title = string.IsNullOrEmpty(_banner) ? "BEAT ARENA" : "BEAT ARENA | " + _banner;
            try
            {
                using Process process = Process.GetCurrentProcess();
                IntPtr window = process.MainWindowHandle;
                if (window == IntPtr.Zero || (window == _titleWindow && title == _lastWindowTitle)) return;
                if (!SetWindowText(window, title))
                {
                    if (!_titleErrorLogged)
                        Plugin.L.LogError($"Failed to set spectator window title (Win32 error {Marshal.GetLastWin32Error()}).");
                    _titleErrorLogged = true;
                    return;
                }
                _titleWindow = window;
                _lastWindowTitle = title;
                _titleErrorLogged = false;
            }
            catch (Exception e)
            {
                if (!_titleErrorLogged) Plugin.L.LogError("Failed to update spectator window title: " + e);
                _titleErrorLogged = true;
            }
        }

        [DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowText(IntPtr window, string title);

        internal static void Draw()
        {
            if (_drawFailed || Screen.width <= 0 || Screen.height <= 0 || Event.current.type != UnityEngine.EventType.Repaint) return;
            try
            {
                GUI.depth = -100;
                if (!Plugin.Settings.SpectatorSongInfo || string.IsNullOrEmpty(_banner)) return;
                if (_bannerStyle == null)
                {
                    _bannerStyle = new GUIStyle { font = GUI.skin.label.font, wordWrap = false, richText = false, alignment = TextAnchor.MiddleCenter };
                    _bannerStyle.normal.textColor = Color.white;
                    _statsStyle = new GUIStyle { font = GUI.skin.label.font, wordWrap = false, richText = false, alignment = TextAnchor.UpperLeft };
                    _statsStyle.normal.textColor = Color.white;
                }
                float scale = Mathf.Clamp(Screen.height / 1080f, 0.5f, 2f);
                int fontSize = Mathf.RoundToInt(22f * scale);
                _bannerStyle.fontSize = fontSize;
                _statsStyle.fontSize = fontSize;
                float margin = 16f * scale;
                float padding = 12f * scale;
                GUIContent banner = new(_banner);
                float availableWidth = Mathf.Max(1f, Screen.width - padding * 2f);
                // Keep long song metadata on a single line instead of expanding the banner.
                while (_bannerStyle.CalcSize(banner).x > availableWidth && fontSize > 1)
                    _bannerStyle.fontSize = --fontSize;
                float bannerHeight = Mathf.RoundToInt(44f * scale);
                Rect bannerRect = new(0f, Screen.height - bannerHeight, Screen.width, bannerHeight);
                GUIContent stats = new(_stats);
                Vector2 statsSize = _statsStyle.CalcSize(stats);
                float width = statsSize.x + padding * 2f;
                float height = statsSize.y + padding * 2f;
                Rect statsRect = new(Screen.width - margin - width, bannerRect.y - margin - height, width, height);
                Color previous = GUI.color;
                try
                {
                    GUI.color = new Color(0f, 0f, 0f, 0.75f);
                    GUI.DrawTexture(bannerRect, Texture2D.whiteTexture);
                    GUI.DrawTexture(statsRect, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                    GUI.Label(new Rect(padding, bannerRect.y, availableWidth, bannerHeight), banner, _bannerStyle);
                    GUI.Label(new Rect(statsRect.x + padding, statsRect.y + padding, statsSize.x, statsSize.y), stats, _statsStyle);
                }
                finally { GUI.color = previous; }
            }
            catch (Exception e)
            {
                _drawFailed = true;
                Plugin.L.LogError("Spectator desktop drawing failed: " + e);
            }
        }
    }
}
