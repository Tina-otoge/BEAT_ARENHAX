using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BeatArenhax.Audio;
using BeatArenhax.CustomSongs;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BeatArenhax
{
    [BepInPlugin(Guid, Name, "1.0.0")]
    public class Plugin : BasePlugin
    {
        public const string Guid = "beatarenhax";
        public const string Name = "BEAT ARENHAX";
        /// <summary>From the BEAT_ARENHAX_VERSION environment variable at build time.</summary>
        public static readonly string Version = BuildInfo.Version;

        internal static ManualLogSource L;
        internal static Settings Settings;
        internal static SongLibrary Library = new();
        internal static CustomAudioEngine Audio;
        internal static Task ScanTask = Task.CompletedTask;
        internal static string GameDir;
        internal static readonly ConcurrentQueue<Action> MainThread = new();

        public override void Load()
        {
            L = Log;
            GameDir = Paths.GameRootPath;
            string iniPath = Path.Combine(GameDir, "beat_arenhax.ini");
            string idsPath = Path.Combine(GameDir, "custom_songs_ids.txt");
            Settings = Settings.LoadOrCreate(iniPath);
            if (Settings.ForceQuitOnSignal)
            {
                Application.wantsToQuit += new Func<bool>(OnWantsToQuit);
            }
            string songRoot = Path.GetFullPath(Path.Combine(GameDir, Settings.SongPath));
            L.LogInfo($"BeatArenhax {Version}; settings: {iniPath}; song folder: {songRoot}; template: {Settings.TemplateSong}");
            if (!Directory.Exists(songRoot))
            {
                try { _ = Directory.CreateDirectory(songRoot); } catch { }
            }

            ScanTask = Task.Run(() =>
            {
                try
                {
                    Stopwatch sw = Stopwatch.StartNew();
                    Library.Scan(songRoot, Settings, idsPath);
                    foreach (string warning in Library.Warnings) L.LogWarning(warning);
                    L.LogInfo($"Found {Library.Songs.Count} DTX songs in {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception e) { L.LogError("Scan failed: " + e); }
            });

            try { Audio = new CustomAudioEngine(Settings, s => L.LogInfo(s)); }
            catch (Exception e) { L.LogError("Audio init failed: " + e); }

            _ = AddComponent<BeatArenhaxRunner>();
            Patches.Apply(new Harmony(Guid));
        }

        internal static void RunOnMainThread(Action a) => MainThread.Enqueue(a);

        private static bool OnWantsToQuit()
        {
            L.LogWarning("Quit signal received; forcing exit if the game does not close.");
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                try
                {
                    using Process process = Process.GetCurrentProcess();
                    process.Kill();
                }
                catch (Exception e)
                {
                    L.LogError("Could not force process exit: " + e.Message);
                }
            });
            return true;
        }

    }

    public class BeatArenhaxRunner(IntPtr ptr) : MonoBehaviour(ptr)
    {
        private float _titleSince = -1;
        private bool _debugStarted;
        private string _lastScene;

#pragma warning disable IDE0051 // Invoked by Unity.
        private void Update()
#pragma warning restore IDE0051
        {
            int n = 0;
            while (n++ < 64 && Plugin.MainThread.TryDequeue(out Action action))
            {
                try { action(); } catch (Exception e) { Plugin.L.LogError(e); }
            }
            try
            {
                string sn = SceneManager.GetActiveScene().name;
                if (sn != _lastScene)
                {
                    if (_lastScene != null)
                    {
                        AudioPatches.StopAllCustomAudio();
                    }
                    _lastScene = sn;
                    Plugin.L.LogInfo("Scene: " + sn);
                    SongDataPatches.EnsureInjected();
                    // GameInfoManager must not be created by us during startup.
                    if (sn is not "StartUpScene" and not "TransitionScene") StartupPatches.ApplyUnlocks();
                }
            }
            catch { }
            DebugAutoplay();
            DebugSelect();
            UpdateHubMonitorText();
        }

        private float _nextMonitorCheck;
        private HomeBackMonitor _monitor;

        private void UpdateHubMonitorText()
        {
            float now = Time.realtimeSinceStartup;
            if (now < _nextMonitorCheck) return;
            _nextMonitorCheck = now + 0.5f;
            try
            {
                if (_monitor == null) _monitor = FindObjectOfType<HomeBackMonitor>();
                if (_monitor == null) return;
                HomeBackMonitor.LineCommentView view = _monitor.m_lineCommentView;
                if (view == null) return;
                string msg = $"Running BEAT ARENHAX {Plugin.Version} | {Plugin.Library.Songs.Count} Custom songs loaded";
                Il2CppSystem.Collections.Generic.List<string> list = view.m_commentList;
                if (list == null || list.Count != 1 || list[0] != msg)
                {
                    Il2CppSystem.Collections.Generic.List<string> comments = new();
                    comments.Add(msg);
                    view.SetLineCommentList(comments);
                }
            }
            catch (Exception e)
            {
                _nextMonitorCheck = now + 10f;
                Plugin.L.LogWarning("Hub monitor text: " + e.Message);
            }
        }

        private int _selectStep;
        private float _selectAt;

        // Development aid: BEAT_ARENHAX_DEBUG_SELECT=<part of a song folder> opens the song wheel and selects that song.
        private void DebugSelect()
        {
            if (_selectStep < 0) return;
            string want = Environment.GetEnvironmentVariable("BEAT_ARENHAX_DEBUG_SELECT");
            if (string.IsNullOrEmpty(want)) { _selectStep = -1; return; }
            try
            {
                Bvr.SceneID scene = Bvr.SubSceneManager.GetCurrentSceneID();
                float now = Time.realtimeSinceStartup;
                switch (_selectStep)
                {
                    case 0:
                        if (scene != Bvr.SceneID.Title) return;
                        if (_selectAt == 0) { _selectAt = now + 8; return; }
                        if (now < _selectAt) return;
                        Plugin.L.LogInfo("BEAT_ARENHAX_DEBUG_SELECT: loading MusicSelect");
                        _ = Bvr.SubSceneManager.LoadScene(Bvr.SceneID.MusicSelect);
                        _selectStep = 1; _selectAt = now + 15;
                        return;
                    case 1:
                        if (now < _selectAt) return;
                        MusicSelect ms = FindObjectOfType<MusicSelect>();
                        Plugin.L.LogInfo("BEAT_ARENHAX_DEBUG_SELECT: scene " + scene + ", MusicSelect " + (ms != null));
                        if (ms == null) { _selectAt = now + 5; return; }
                        Plugin.L.LogInfo("BEAT_ARENHAX_DEBUG_SELECT: Open() = " + ms.Open());
                        _selectStep = 2; _selectAt = now + 10;
                        return;
                    case 2:
                        if (now < _selectAt) return;
                        MusicSelect sel = FindObjectOfType<MusicSelect>();
                        if (sel.m_musicPlateList == null || sel.m_musicPlateList.Count == 0)
                        {
                            Plugin.L.LogInfo("BEAT_ARENHAX_DEBUG_SELECT: building the song wheel");
                            sel.ReadyMusicPlateList();
                        }
                        Il2CppSystem.Collections.Generic.List<MusicInfo> list = MusicInfoManager.GetInstance().MusicList;
                        int index = -1;
                        for (int i = 0; i < list.Count; i++)
                        {
                            CustomSong song = Plugin.Library.ById.TryGetValue(list[i].Id, out CustomSong customSong) ? customSong : null;
                            if (song == null || song.Folder.IndexOf(want, StringComparison.OrdinalIgnoreCase) < 0) continue;
                            index = i;
                            break;
                        }
                        Plugin.L.LogInfo($"BEAT_ARENHAX_DEBUG_SELECT: list size {list.Count}, selecting index {index}");
                        if (index >= 0) sel.SelectMusic(index);
                        _selectStep = -1;
                        return;
                    default:
                        break;
                }
            }
            catch (Exception e)
            {
                _selectStep = -1;
                Plugin.L.LogError("BEAT_ARENHAX_DEBUG_SELECT failed: " + e);
            }
        }

        // Development aid: BEAT_ARENHAX_DEBUG_AUTOPLAY=<music id or part of the folder name> starts that song with autoplay.
        private void DebugAutoplay()
        {
            if (_debugStarted) return;
            string want = Environment.GetEnvironmentVariable("BEAT_ARENHAX_DEBUG_AUTOPLAY");
            if (string.IsNullOrEmpty(want)) { _debugStarted = true; return; }
            try
            {
                Bvr.SceneID scene = Bvr.SubSceneManager.GetCurrentSceneID();
                if (scene is not Bvr.SceneID.Title and not Bvr.SceneID.MusicSelect) { _titleSince = -1; return; }
                if (_titleSince < 0) { _titleSince = Time.realtimeSinceStartup; return; }
                if (Time.realtimeSinceStartup - _titleSince < 8) return;
                _debugStarted = true;
                if (!Plugin.ScanTask.IsCompleted) Plugin.ScanTask.Wait();
                CustomSong song = int.TryParse(want, out int id) && Plugin.Library.ById.TryGetValue(id, out CustomSong indexedSong) ? indexedSong
                    : Plugin.Library.Songs.Find(x => x.Folder.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0);
                if (song == null) { Plugin.L.LogWarning("BEAT_ARENHAX_DEBUG_AUTOPLAY: song not found: " + want); return; }
                MusicInfo info = MusicInfoManager.GetInstance().GetMusicInfoByID(song.Id);
                PlayerInfoManager playerInfo = PlayerInfoManager.GetInstance();
                playerInfo.SelectedMusicID = song.Id;
                playerInfo.CurrentMusicID = song.Id;
                Difficulty difficulty = Environment.GetEnvironmentVariable("BEAT_ARENHAX_DEBUG_DIFF") == "0" ? Difficulty.Basic : Difficulty.Advanced;
                playerInfo.CurrentDifficulty = difficulty;
                playerInfo.CurrentPlayerSlot = info.GetPlayerSlot(PlayType.Drum);
                SequencePlayer.IsAutoPlay = true;
                Patches.DebugAutoplayActive = true;
                Plugin.L.LogInfo($"BEAT_ARENHAX_DEBUG_AUTOPLAY: starting '{song.Title}' ({song.Id}) {difficulty}, slot {playerInfo.CurrentPlayerSlot}, play type {playerInfo.CurrentPlayType}");
                _ = Bvr.SubSceneManager.LoadScene(Bvr.SceneID.DrumGameScene);
            }
            catch (Exception e)
            {
                _debugStarted = true;
                Plugin.L.LogError("BEAT_ARENHAX_DEBUG_AUTOPLAY failed: " + e);
            }
        }
    }
}
