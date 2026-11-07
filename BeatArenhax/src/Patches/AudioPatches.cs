using System;
using BeatArenhax.CustomSongs;
using CRI.Adx2;
using HarmonyLib;

namespace BeatArenhax
{
    internal static class AudioPatches
    {
        private const string FakeBgm = "dtxbgm";
        private const string FakeFollower = "dtxfollow";
        private const string FakePreview = "dtxprev";
        private const string FakeChip = "dtxchip";
        internal const string PreviewPrefix = "dtxprev:";

        internal static string PreviewCue(int musicId) => PreviewPrefix + musicId;

        private static PlayInfo ChipInfo => field ??= MakeFake(FakeChip);

        [ThreadStatic] private static int _inPlaySE;
        [ThreadStatic] private static bool _muteSE;

        internal static void Apply(Harmony h)
        {
            Patches.Patch(h, typeof(AudioPatches), typeof(SoundManager), nameof(SoundManager.PlayBgm), nameof(PlayBgm_Pre));
            Patches.Patch(h, typeof(AudioPatches), typeof(SoundManager), nameof(SoundManager.PlayGameSE), nameof(PlayGameSE_Pre));
            Patches.Patch(h, typeof(AudioPatches), typeof(SequencePlayer), "playSE", nameof(PlaySE_Pre), nameof(PlaySE_Post));
            Patches.Patch(h, typeof(AudioPatches), typeof(SequenceBgm), "Update", nameof(SequenceBgmUpdate_Pre));
            Patches.Patch(h, typeof(AudioPatches), typeof(InGameTimer), nameof(InGameTimer.Pause), null, nameof(InGameTimer_Pause_Post));
            Patches.Patch(h, typeof(AudioPatches), typeof(InGameTimer), nameof(InGameTimer.Resume), null, nameof(InGameTimer_Resume_Post));
            Patches.Patch(h, typeof(AudioPatches), typeof(Bvr.Player), "StatePlayResult", null, nameof(StatePlayResult_Post));
        }

        internal static void StopAllCustomAudio()
        {
            StageVideoPatches.Stop();
            SongWheelTouchPatches.Forget();
            if (Plugin.Audio == null) return;
            Plugin.Audio.Mixer.StopSong();
            Plugin.Audio.StopPreview();
            Plugin.Audio.ReleaseArmedChart();
        }

        private static PlayInfo MakeFake(string kind, long length = 0)
        {
            PlayInfo info = new()
            {
                cueName = kind,
                category = SoundCategory.Bgm,
                length = length,
                index = -1,
                instanceId = 0xFFFFFFFEu
            };
            return info;
        }

        private static bool IsFake(PlayInfo info, out string kind)
        {
            kind = null;
            if (info == null) return false;
            string cue;
            try { cue = info.cueName; } catch { return false; }
            if (cue == null || !cue.StartsWith("dtx", StringComparison.Ordinal)) return false;
            kind = cue;
            return true;
        }

        private static bool PlayBgm_Pre(string cueName, float volume, SoundPlayerDelegate startCallback, ref PlayInfo __result)
        {
            if (SongWheelTouchPatches.TryHandOver(cueName, out PlayInfo touchPreview))
            {
                __result = touchPreview;
                Started(startCallback);
                return false;
            }
            if (cueName == null || Plugin.Audio == null || !(cueName.StartsWith("dtx", StringComparison.Ordinal) || cueName == SongDataPatches.TemplateCue + "_bg"))
                return true;
            try
            {
                if (cueName == FakeBgm)
                {
                    CustomSong song = Patches.CurrentDrumSong();
                    if (song != null) _ = Plugin.Audio.ArmSelectedChart(song, CustomScorePatches.SelectedIndex(song));
                    _ = Plugin.Audio.StartSong(volume);
                    __result = MakeFake(FakeBgm, (long)(Plugin.Audio.ArmedChart?.LengthMs ?? 0));
                    Started(startCallback);
                    Plugin.L.LogInfo("Custom song BGM started");
                    return false;
                }
                if (cueName.StartsWith(PreviewPrefix, StringComparison.Ordinal))
                {
                    CustomSong song = int.TryParse(cueName[PreviewPrefix.Length..], out int id) ? Patches.SongById(id) : null;
                    if (song != null) Plugin.Audio.PlayPreview(song, volume);
                    __result = MakeFake(FakePreview);
                    Started(startCallback);
                    return false;
                }
                if (cueName == SongDataPatches.TemplateCue + "_bg" && Patches.CurrentDrumSong() != null)
                {
                    __result = MakeFake(FakeFollower);
                    Started(startCallback);
                    return false;
                }
            }
            catch (Exception e) { Plugin.L.LogError("PlayBgm hook: " + e); }
            return true;
        }

        private static void Started(SoundPlayerDelegate start)
        {
            if (start != null) Plugin.RunOnMainThread(start.Invoke);
        }

        private static void PlaySE_Pre()
        {
            _inPlaySE++;
            _muteSE = Patches.CurrentDrumSong() != null;
        }

        private static void PlaySE_Post()
        {
            if (--_inPlaySE <= 0) { _inPlaySE = 0; _muteSE = false; }
        }

        private static bool PlayGameSE_Pre(string cueName, ref float volume, ref PlayInfo __result)
        {
            if (cueName != null && cueName.StartsWith(ChartConverter.ChipLabelPrefix, StringComparison.Ordinal))
            {
                if (Plugin.Audio != null && int.TryParse(cueName[ChartConverter.ChipLabelPrefix.Length..], out int wav))
                    Plugin.Audio.PlayChip(wav, volume);
                __result = ChipInfo;
                return false;
            }
            if (_inPlaySE > 0 && _muteSE) volume = 0f;
            return true;
        }

        private static bool SequenceBgmUpdate_Pre(SequenceBgm __instance)
        {
            PlayInfo bgm;
            try { bgm = __instance.m_bgm; } catch { return true; }
            if (!IsFake(bgm, out string kind) || (kind != FakeBgm && kind != FakeFollower)) return true;
            SequenceBgm.State state = __instance.m_state;
            if (state == SequenceBgm.State.PLAYING)
            {
                if (!InGameTimer.isPaused) __instance.m_frameMusicCount = Math.Max(0, (int)Plugin.Audio.Mixer.SongTimeMs);
            }
            else if (state == SequenceBgm.State.END)
            {
                __instance.m_frameMusicCount += InGameTimer.DeltaMilliSecond;
            }
            return false;
        }

        private static void InGameTimer_Pause_Post()
        {
            if (Plugin.Audio != null && Plugin.Audio.Mixer.SongActive) Plugin.Audio.Mixer.PauseSong();
        }

        private static void InGameTimer_Resume_Post()
        {
            if (Plugin.Audio != null && Plugin.Audio.Mixer.SongActive) Plugin.Audio.Mixer.ResumeSong();
        }

        private static void StatePlayResult_Post(Bvr.Player.Proc proc)
        {
            if (proc != Bvr.Player.Proc.Begin || Patches.CurrentSong() == null) return;
            Plugin.L.LogInfo("Custom song finished, showing results");
            StageVideoPatches.Stop();
            Plugin.Audio?.Mixer.StopSong();
            Plugin.Audio?.ReleaseArmedChart();
        }
    }
}
