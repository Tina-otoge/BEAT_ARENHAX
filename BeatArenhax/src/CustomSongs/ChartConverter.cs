using System;
using System.Collections.Generic;
using BeatArenhax.DTX;

namespace BeatArenhax.CustomSongs
{
    public enum GameLane
    {
        None = 0,
        BD = 1,
        SD = 2,
        HH = 3,
        LP = 4,
        HT = 5,
        LT = 6,
        FT = 7,
        RC = 8,
        LC = 9,
    }

    public struct ScheduledSound
    {
        public double TimeMs;
        public int WavId;
        public bool FromNote;
    }

    public sealed class ScheduledVideo
    {
        public double TimeMs;
        public string Path;
    }

    public sealed class ChartNote
    {
        public int Tick;
        public double TimeMs;
        public int Channel;
        public int WavId;
        public GameLane Lane;
        public bool Playable;
        public double BeatInMeasure;
    }

    public sealed class ConvertedChart
    {
        public DtxChart Source;
        public byte[] Midi;
        public List<ScheduledSound> Schedule = [];
        public List<ScheduledVideo> Videos = [];
        public List<ChartNote> Notes = [];
        public double LengthMs;
        public int PlayableCount;
        public int AutoCount;
        public int GameLevel;
        public HashSet<int> UsedWavIds = [];
        public List<string> Warnings = [];
        internal ChartTimeline Timeline;
        internal int EndTick;
    }

    public sealed class ChartConverter(Settings settings, Func<string, double> durationMs)
    {
        public const int Tpq = 480;
        public const string BgmLabel = "dtxbgm";
        public const string ChipLabelPrefix = "dtx#";

        private readonly Settings _settings = settings;
        private readonly Func<string, double> _durationMs = durationMs;

        public static int ToGameLevel(double dlevel) => dlevel <= 0 ? 3 : Math.Clamp((int)Math.Round((dlevel + 10) / 20.0), 1, 5);

        private GameLane MapChannel(int channel) => channel switch
        {
            0x13 => _settings.RightPedal.Enabled ? GameLane.BD : GameLane.None,
            0x1B or 0x1C => _settings.LeftPedal.Enabled ? GameLane.LP : GameLane.None,
            0x11 or 0x18 => GameLane.HH,
            0x12 => GameLane.SD,
            0x14 => GameLane.HT,
            0x15 => GameLane.LT,
            0x17 => GameLane.FT,
            0x16 => GameLane.RC,
            0x19 => _settings.RideOnRightCymbal ? GameLane.RC : GameLane.None,
            0x1A => GameLane.LC,
            _ => GameLane.None,
        };

        private static bool IsDrumChannel(int channel) => channel is >= 0x11 and <= 0x1C;

        private static bool IsAutoSoundChannel(int channel) => channel is 0x01
                or (>= 0x61 and <= 0x69) or (>= 0x70 and <= 0x79) or (>= 0x80 and <= 0x89) or (>= 0x90 and <= 0x92) or (>= 0x20 and <= 0x27) or (>= 0xA0 and <= 0xA7);

        private static bool IsVisualChannel(int channel) => channel is 0x04 or 0x06 or 0x07 or 0x0A;

        private static void AddChannelWarning(ConvertedChart chart, HashSet<int> warnedChannels, int channel, string reason)
        {
            if (warnedChannels.Add(channel))
                chart.Warnings.Add($"DTX channel {channel:X2} is {reason}");
        }

        public ConvertedChart Prepare(DtxChart chart, bool casual)
        {
            ConvertedChart result = new() { Source = chart, GameLevel = ToGameLevel(chart.DLevel) };
            result.Warnings.AddRange(chart.Warnings);
            HashSet<int> warnedChannels = [];
            int lastMeasure = Math.Max(chart.MaxMeasure, 1);
            ChartTimeline probe = ChartTimeline.Build(chart, 0, lastMeasure);
            double firstNoteMs = double.MaxValue;
            foreach (DtxChip chip in chart.Chips)
            {
                if (!IsDrumChannel(chip.Channel) && !IsAutoSoundChannel(chip.Channel)) continue;
                int tick = probe.MeasureStartTick[chip.Measure]
                    + (int)Math.Round(chip.Frac * probe.MeasureTicks[chip.Measure]);
                firstNoteMs = Math.Min(firstNoteMs, probe.TickToMs(tick));
            }
            if (firstNoteMs == double.MaxValue) firstNoteMs = 0;
            double startBpm = chart.Bpm > 0 ? chart.Bpm : 120;
            double measureMs = 4 * 60000.0 / startBpm;
            int leadInMeasures = firstNoteMs >= _settings.MinimumTimeBeforeFirstNoteMs
                ? 0
                : (int)Math.Ceiling((_settings.MinimumTimeBeforeFirstNoteMs - firstNoteMs) / measureMs);
            if (leadInMeasures < 1) leadInMeasures = 1;
            ChartTimeline timeline = ChartTimeline.Build(chart, leadInMeasures, lastMeasure);

            int TickOf(DtxChip chip)
            {
                int measure = chip.Measure + leadInMeasures;
                return timeline.MeasureStartTick[measure]
                    + (int)Math.Round(chip.Frac * timeline.MeasureTicks[measure]);
            }

            List<ChartNote> notes = [];
            foreach (DtxChip chip in chart.Chips)
            {
                if (chip.Channel is 0x54 or 0x5A)
                {
                    if (_settings.LoadVideos)
                    {
                        string path = chart.Videos.TryGetValue(chip.Value, out string file) ? chart.ResolveFile(file, false) : null;
                        if (path == null)
                            result.Warnings.Add($"DTX video {chip.Value} on channel {chip.Channel:X2} is undefined or missing: {file}");
                        result.Videos.Add(new ScheduledVideo { TimeMs = timeline.TickToMs(TickOf(chip)), Path = path });
                    }
                    continue;
                }
                if (IsAutoSoundChannel(chip.Channel))
                {
                    int tick = TickOf(chip);
                    result.Schedule.Add(new ScheduledSound { TimeMs = timeline.TickToMs(tick), WavId = chip.Value });
                    _ = result.UsedWavIds.Add(chip.Value);
                    continue;
                }
                if (chip.Channel is 0x03 or 0x08 or 0x09) continue;
                if (IsVisualChannel(chip.Channel))
                {
                    AddChannelWarning(result, warnedChannels, chip.Channel, "preserved but not rendered");
                    continue;
                }
                if (!IsDrumChannel(chip.Channel))
                {
                    AddChannelWarning(result, warnedChannels, chip.Channel, "preserved but not interpreted");
                    continue;
                }
                int noteTick = TickOf(chip);
                GameLane lane = MapChannel(chip.Channel);
                ChartNote note = new()
                {
                    Tick = noteTick,
                    TimeMs = timeline.TickToMs(noteTick),
                    Channel = chip.Channel,
                    WavId = chip.Value,
                    Lane = lane,
                    Playable = lane != GameLane.None,
                    BeatInMeasure = chip.Frac * 4 * timeline.MeasureMult[chip.Measure + leadInMeasures],
                };
                notes.Add(note);
                _ = result.UsedWavIds.Add(chip.Value);
            }
            notes.Sort((left, right) => left.Tick != right.Tick
                ? left.Tick.CompareTo(right.Tick)
                : ChartNoteSimplifier.Priority(right).CompareTo(ChartNoteSimplifier.Priority(left)));
            ChartNoteSimplifier.Simplify(notes, _settings, casual);
            result.Notes = notes;
            foreach (ChartNote note in notes)
            {
                if (note.Playable) continue;
                result.Schedule.Add(new ScheduledSound { TimeMs = note.TimeMs, WavId = note.WavId, FromNote = true });
            }
            result.Schedule.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
            result.Videos.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));

            double lastNoteMs = 0;
            foreach (ChartNote note in notes) lastNoteMs = Math.Max(lastNoteMs, note.TimeMs);
            double lastChipEndMs = 0;
            Dictionary<int, double> durationCache = [];
            foreach (ScheduledSound sound in result.Schedule)
            {
                lastNoteMs = Math.Max(lastNoteMs, sound.TimeMs);
                if (!durationCache.TryGetValue(sound.WavId, out double durationMs))
                {
                    string path = chart.ResolveWavPath(sound.WavId);
                    durationMs = path != null ? _durationMs(path) : 0;
                    durationCache[sound.WavId] = durationMs;
                }
                lastChipEndMs = Math.Max(lastChipEndMs, sound.TimeMs + durationMs);
            }
            double endMs = Math.Max(lastNoteMs + 2000, Math.Min(lastChipEndMs + 500, lastNoteMs + 30000));
            int endTick = timeline.MsToTick(endMs);
            endTick = (endTick + 59) / 60 * 60;
            result.LengthMs = timeline.TickToMs(endTick + Tpq);
            result.Timeline = timeline;
            result.EndTick = endTick;
            return result;
        }

        public static void Finish(ConvertedChart chart, Dictionary<int, int> wavToNote)
        {
            ChartNoteSimplifier.RemapOverflow(chart.Notes, wavToNote);
            chart.PlayableCount = 0;
            chart.AutoCount = 0;
            _ = chart.Schedule.RemoveAll(sound => sound.FromNote);
            foreach (ChartNote note in chart.Notes)
            {
                if (note.Playable)
                {
                    chart.PlayableCount++;
                    continue;
                }
                chart.AutoCount++;
                chart.Schedule.Add(new ScheduledSound { TimeMs = note.TimeMs, WavId = note.WavId, FromNote = true });
            }
            chart.Schedule.Sort((left, right) => left.TimeMs.CompareTo(right.TimeMs));
            chart.Midi = GameMidiWriter.Write(chart.Timeline, chart.Notes, wavToNote, chart.EndTick);
        }
    }
}
