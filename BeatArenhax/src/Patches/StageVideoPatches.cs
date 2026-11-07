using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Video;

namespace BeatArenhax
{
    internal static class StageVideoPatches
    {
        private const string MonitorTexture = "Texture2D_15597B87";
        private const int VideoBackgroundQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry - 2;
        private static readonly Dictionary<int, MonitorState> Monitors = [];
        private static ConvertedChart _chart;
        private static StageDirector _director;
        private static GameObject _object;
        private static VideoPlayer _player;
        private static NativeVideo _native;
        private static Shader _videoShader;
        private static int _event = -1;
        private static int _loadedEvent = -1;
        private static float _prepareAt;
        private static bool _started, _paused, _failed, _ended;
        private static double _lastTime;
        private static RenderTexture _dimmedVideo;
        private static Material _dimOverlay;
        private static GameObject _centerObject;
        private static Mesh _centerMesh;
        private static Material _centerMaterial;

        private sealed class MonitorState
        {
            internal Material Material;
            internal Texture Original, Replacement;
            internal Shader Shader;
            internal int RenderQueue;
        }

        internal static void Apply(Harmony harmony) =>
            Patches.Patch(harmony, typeof(StageVideoPatches), typeof(StageDirector), "Update", nameof(Update_Pre), nameof(Update_Post));

        private static void Update_Pre(StageDirector __instance)
        {
            if (_director != __instance) return;
            foreach (MonitorState state in Monitors.Values)
            {
                if (state.Material == null) continue;
                state.Material.shader = state.Shader;
                state.Material.renderQueue = state.RenderQueue;
                state.Material.SetTexture(MonitorTexture, state.Original);
            }
        }

        private static void Update_Post(StageDirector __instance)
        {
            if (!Plugin.Settings.LoadVideos || Plugin.Audio == null) return;
            ConvertedChart chart = Plugin.Audio.ArmedChart;
            if (chart == null || chart.Videos.Count == 0) return;
            try
            {
                if (_chart != chart || _director != __instance)
                {
                    Stop();
                    _chart = chart;
                    _director = __instance;
                    int first = chart.Videos.FindIndex(video => video.Path != null);
                    if (first >= 0) Load(first);
                }
                if (!Plugin.Audio.Mixer.SongActive) return;
                double now = Plugin.Audio.Mixer.SongTimeMs + Plugin.Settings.AudioOffsetMs;
                int index = -1;
                while (index + 1 < chart.Videos.Count && chart.Videos[index + 1].TimeMs <= now) index++;
                if (index != _event)
                {
                    RestoreMonitors();
                    _event = index;
                    _started = false;
                    if (index >= 0 && index != _loadedEvent) Load(index);
                    else if (index < 0 && _player != null) _native.Pause(_player.Pointer);
                }
                if (index < 0 || _player == null || _failed) return;
                if (!_native.Prepared(_player.Pointer))
                {
                    if (Time.realtimeSinceStartup - _prepareAt > 15)
                        Fail("Video preparation timed out");
                    return;
                }
                double elapsed = Math.Max(0, (now - chart.Videos[index].TimeMs) / 1000);
                bool paused = InGameTimer.isPaused;
                if (!_started || now < _lastTime - 100 || now > _lastTime + 1000)
                {
                    _native.SetTime(_player.Pointer, elapsed);
                    _native.Play(_player.Pointer);
                    _started = true;
                    _paused = false;
                    _ended = false;
                    Plugin.L.LogInfo($"DTX video started: {chart.Videos[index].Path} at {now:0} ms (trigger {chart.Videos[index].TimeMs:0} ms)");
                }
                _native.SetReferenceTime(_player.Pointer, elapsed);
                _lastTime = now;
                if (_ended) return;
                if (paused != _paused)
                {
                    if (paused) _native.Pause(_player.Pointer);
                    else _native.Play(_player.Pointer);
                    _paused = paused;
                }
                IntPtr pointer = _native.GetTexture(_player.Pointer);
                if (pointer != IntPtr.Zero && __instance.m_helper != null)
                {
                    Render(__instance.m_helper, new Texture(pointer));
                }
            }
            catch (Exception error)
            {
                Fail("Stage video playback: " + error);
            }
        }

        private static void Load(int index)
        {
            _loadedEvent = index;
            _started = false;
            _failed = false;
            _ended = false;
            string path = _chart.Videos[index].Path;
            if (_player != null) _native.Stop(_player.Pointer);
            if (path == null)
            {
                _failed = true;
                Plugin.L.LogWarning("DTX video trigger has no readable file; keeping vanilla monitors");
                return;
            }
            _native ??= new NativeVideo();
            if (_player == null)
            {
                _object = new GameObject("BeatArenhax Stage Video");
                _object.SetActive(false);
                _player = _object.AddComponent<VideoPlayer>();
                _native.SetPlayOnAwake(_player.Pointer, 0);
                _native.SetAudioMode(_player.Pointer, (int)VideoAudioOutputMode.None);
                _native.SetRenderMode(_player.Pointer, (int)VideoRenderMode.APIOnly);
                _native.SetSource(_player.Pointer, (int)VideoSource.Url);
                _native.SetLooping(_player.Pointer, 0);
                _native.SetTimeReference(_player.Pointer, (int)VideoTimeReference.ExternalTime);
                _player.errorReceived = DelegateSupport.ConvertDelegate<VideoPlayer.ErrorEventHandler>(
                    new Action<VideoPlayer, string>((player, message) => Fail("DTX video decoder: " + message)));
                _player.loopPointReached = DelegateSupport.ConvertDelegate<VideoPlayer.EventHandler>(
                    new Action<VideoPlayer>(player =>
                    {
                        _ended = true;
                        RestoreMonitors();
                        Plugin.L.LogInfo("DTX video ended; restored vanilla stage animation");
                    }));
                _object.SetActive(true);
            }
            _native.SetUrl(_player.Pointer, IL2CPP.ManagedStringToIl2Cpp(System.IO.Path.GetFullPath(path)));
            _prepareAt = Time.realtimeSinceStartup;
            _native.Prepare(_player.Pointer);
            Plugin.L.LogInfo("Preparing DTX video: " + path);
        }

        private static void Render(Bvr.StageHelper helper, Texture texture)
        {
            if (_videoShader == null)
            {
                _videoShader = Shader.Find("Hidden/VideoComposite");
                if (_videoShader == null) throw new InvalidOperationException("The stereo-compatible video shader Hidden/VideoComposite is unavailable.");
                Plugin.L.LogInfo("DTX video shader: Hidden/VideoComposite (2D video in mono and stereo)");
            }
            var lists = helper.m_monitorLists;
            if (lists == null) return;
            if (Monitors.Count == 0)
            {
                foreach (var list in lists)
                {
                    if (list == null) continue;
                    foreach (var monitor in list) CaptureMonitor(monitor.m_material);
                }
            }
            foreach (MonitorState state in Monitors.Values)
            {
                Material material = state.Material;
                if (material == null) continue;
                Texture current = material.GetTexture(MonitorTexture);
                if (current != state.Replacement) state.Original = current;
                ConfigureVideoMaterial(material, VideoBackgroundQueue);
                material.SetTexture("_MainTex", texture);
                state.Replacement = texture;
            }
            if (Plugin.Settings.CenterVideoDim > 0f)
            {
                EnsureCenterOverlay(helper);
                _centerMaterial.SetTexture("_MainTex", DimCenterVideo(texture));
            }
        }

        private static void ConfigureVideoMaterial(Material material, int renderQueue)
        {
            // BlitCopy expects a texture array in instanced VR. VideoComposite samples
            // the decoder's 2D texture in both eyes, without fog or mesh vertex tint.
            material.shader = _videoShader;
            material.renderQueue = renderQueue;
            material.SetFloat("_AlphaParam", 1f);
            material.SetVector("_RightEyeUVOffset", Vector4.zero);
            material.SetTextureScale("_MainTex", Vector2.one);
            material.SetTextureOffset("_MainTex", Vector2.zero);
        }

        private static void CaptureMonitor(Material material)
        {
            if (material == null || Monitors.ContainsKey(material.GetInstanceID())) return;
            Monitors.Add(material.GetInstanceID(), new MonitorState
            {
                Material = material,
                Original = material.GetTexture(MonitorTexture),
                Shader = material.shader,
                RenderQueue = material.renderQueue,
            });
            Plugin.L.LogInfo("DTX video monitor: " + material.name);
        }

        private static void EnsureCenterOverlay(Bvr.StageHelper helper)
        {
            if (_centerObject != null) return;
            Transform backdrop = helper.transform.Find("pCylinder65");
            if (backdrop == null) throw new InvalidOperationException("Center video backdrop pCylinder65 was not found.");
            // The authored pCylinder65 mesh is not CPU-readable. Its center and side
            // panels share Outside's material; Inside is a different stage display.
            // Match the four center-panel corners/UVs from the original stage asset.
            _centerMesh = new Mesh
            {
                name = "BeatArenhax center video",
                vertices = new Vector3[]
                {
                    new(-6.8609943f, -11.752952f, 23.271477f),
                    new(-6.8609943f, 2.0071523f, 23.271477f),
                    new(6.863912f, 2.0071523f, 23.271477f),
                    new(6.863912f, -11.752952f, 23.271477f),
                },
                uv = new Vector2[] { new(0, 0), new(0, 1), new(1, 1), new(1, 0) },
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
            };
            _centerMesh.RecalculateBounds();
            _centerMaterial = new Material(_videoShader);
            ConfigureVideoMaterial(_centerMaterial, VideoBackgroundQueue + 1);
            _centerObject = new GameObject("BeatArenhax_CenterVideo");
            _centerObject.layer = backdrop.gameObject.layer;
            _centerObject.transform.SetParent(backdrop, false);
            _centerObject.AddComponent<MeshFilter>().sharedMesh = _centerMesh;
            MeshRenderer renderer = _centerObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = _centerMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Texture DimCenterVideo(Texture source)
        {
            if (_dimOverlay == null)
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null) throw new InvalidOperationException("Center video dimming requires UI/Default.");
                _dimOverlay = new Material(shader);
                _dimOverlay.SetColor("_Color", new Color(0, 0, 0, Plugin.Settings.CenterVideoDim));
            }
            if (_dimmedVideo == null || _dimmedVideo.width != source.width || _dimmedVideo.height != source.height)
            {
                ReleaseDimmedVideo();
                _dimmedVideo = new RenderTexture(source.width, source.height, 0, RenderTextureFormat.ARGB32);
                if (!_dimmedVideo.Create()) throw new InvalidOperationException("Could not allocate the center video render texture.");
                Plugin.L.LogInfo($"DTX center video dim: {Plugin.Settings.CenterVideoDim:0.###}, {source.width}x{source.height}; outside monitors unchanged");
            }
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.Blit(source, _dimmedVideo);
                RenderTexture.active = _dimmedVideo;
                if (!_dimOverlay.SetPass(0)) throw new InvalidOperationException("Could not activate the center video dimming shader.");
                GL.PushMatrix();
                try
                {
                    GL.LoadOrtho();
                    GL.Begin(GL.QUADS);
                    GL.Color(Color.white);
                    GL.TexCoord2(0, 0); GL.Vertex3(0, 0, 0);
                    GL.TexCoord2(0, 1); GL.Vertex3(0, 1, 0);
                    GL.TexCoord2(1, 1); GL.Vertex3(1, 1, 0);
                    GL.TexCoord2(1, 0); GL.Vertex3(1, 0, 0);
                    GL.End();
                }
                finally { GL.PopMatrix(); }
            }
            finally { RenderTexture.active = previous; }
            return _dimmedVideo;
        }

        private static void ReleaseDimmedVideo()
        {
            if (_dimmedVideo == null) return;
            _dimmedVideo.Release();
            UnityEngine.Object.Destroy(_dimmedVideo);
            _dimmedVideo = null;
        }

        private static void RestoreMonitors()
        {
            if (_centerObject != null)
            {
                _centerObject.SetActive(false);
                UnityEngine.Object.Destroy(_centerObject);
            }
            if (_centerMaterial != null) UnityEngine.Object.Destroy(_centerMaterial);
            if (_centerMesh != null) UnityEngine.Object.Destroy(_centerMesh);
            _centerObject = null;
            _centerMaterial = null;
            _centerMesh = null;
            foreach (MonitorState state in Monitors.Values)
            {
                if (state.Material == null) continue;
                state.Material.shader = state.Shader;
                state.Material.renderQueue = state.RenderQueue;
                state.Material.SetTexture(MonitorTexture, state.Original);
            }
            Monitors.Clear();
            ReleaseDimmedVideo();
        }

        private static void Fail(string message)
        {
            _failed = true;
            Plugin.L.LogError(message + "; keeping vanilla stage animation");
            RestoreMonitors();
            if (_player != null) _native.Stop(_player.Pointer);
        }

        internal static void Stop()
        {
            RestoreMonitors();
            if (_player != null)
            {
                _player.errorReceived = null;
                _player.loopPointReached = null;
                _native.Stop(_player.Pointer);
            }
            if (_object != null) UnityEngine.Object.Destroy(_object);
            _object = null;
            _player = null;
            _chart = null;
            _director = null;
            _event = _loadedEvent = -1;
            _started = _paused = _failed = _ended = false;
            _lastTime = 0;
            if (_dimOverlay != null) UnityEngine.Object.Destroy(_dimOverlay);
            _dimOverlay = null;
        }

        // VideoPlayer's managed API was stripped from this game. Use Unity 2019.4's
        // named internal calls, never version-specific native offsets or modified interop DLLs.
        private sealed class NativeVideo
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ActionCall(IntPtr player);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate byte BoolCall(IntPtr player);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ObjectCall(IntPtr player);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void IntCall(IntPtr player, int value);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void ByteCall(IntPtr player, byte value);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void DoubleCall(IntPtr player, double value);
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate void StringCall(IntPtr player, IntPtr value);

            internal readonly ActionCall Prepare = Resolve<ActionCall>("Prepare");
            internal readonly ActionCall Play = Resolve<ActionCall>("Play");
            internal readonly ActionCall Pause = Resolve<ActionCall>("Pause");
            internal readonly ActionCall Stop = Resolve<ActionCall>("Stop");
            private readonly BoolCall _prepared = Resolve<BoolCall>("get_isPrepared");
            internal readonly ObjectCall GetTexture = Resolve<ObjectCall>("get_texture");
            internal readonly IntCall SetSource = Resolve<IntCall>("set_source");
            internal readonly IntCall SetAudioMode = Resolve<IntCall>("set_audioOutputMode");
            internal readonly IntCall SetRenderMode = Resolve<IntCall>("set_renderMode");
            internal readonly IntCall SetTimeReference = Resolve<IntCall>("set_timeReference");
            internal readonly ByteCall SetPlayOnAwake = Resolve<ByteCall>("set_playOnAwake");
            internal readonly ByteCall SetLooping = Resolve<ByteCall>("set_isLooping");
            internal readonly DoubleCall SetTime = Resolve<DoubleCall>("set_time");
            internal readonly DoubleCall SetReferenceTime = Resolve<DoubleCall>("set_externalReferenceTime");
            internal readonly StringCall SetUrl = Resolve<StringCall>("set_url");

            internal bool Prepared(IntPtr player) => _prepared(player) != 0;

            private static T Resolve<T>(string method) where T : Delegate =>
                IL2CPP.ResolveICall<T>("UnityEngine.Video.VideoPlayer::" + method);
        }
    }
}
