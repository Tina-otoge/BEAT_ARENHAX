using System;
using System.Collections.Generic;
using System.Linq;

namespace BeatArenhax.CustomSongs
{
    internal static class ChartNoteSimplifier
    {
        public static void Simplify(List<ChartNote> notes, Settings settings, bool casual)
        {
            int maxHits = settings.MaxSimultaneousHits;
            int minimumGap = casual ? Math.Max(settings.MinNoteGapMs, settings.CasualMinNoteGapMs) : settings.MinNoteGapMs;

            if (casual && settings.CasualOnlyEighthNotes)
            {
                foreach (ChartNote note in notes)
                {
                    if (!note.Playable) continue;
                    double eighths = note.BeatInMeasure * 2;
                    if (!(Math.Abs(eighths - Math.Round(eighths)) > 0.02)) continue;
                    note.Playable = false;
                }
            }

            int start = 0;
            while (start < notes.Count)
            {
                int end = start;
                while (end < notes.Count && notes[end].Tick == notes[start].Tick) end++;
                HashSet<GameLane> usedLanes = [];
                List<ChartNote> simultaneous = [];
                for (int i = start; i < end; i++)
                {
                    if (!notes[i].Playable) continue;
                    simultaneous.Add(notes[i]);
                }
                simultaneous.Sort((left, right) => Priority(right).CompareTo(Priority(left)));

                int kept = 0;
                foreach (ChartNote note in simultaneous)
                {
                    if (usedLanes.Contains(note.Lane))
                    {
                        if (note.Lane == GameLane.RC && !usedLanes.Contains(GameLane.LC)) note.Lane = GameLane.LC;
                        else if (note.Lane == GameLane.LC && !usedLanes.Contains(GameLane.RC)) note.Lane = GameLane.RC;
                        else
                        {
                            note.Playable = false;
                            continue;
                        }
                    }
                    if (kept >= maxHits || (casual && kept >= 1 && note.Lane == GameLane.HH))
                    {
                        note.Playable = false;
                        continue;
                    }
                    _ = usedLanes.Add(note.Lane);
                    kept++;
                }
                start = end;
            }

            if (minimumGap <= 0) return;
            Dictionary<GameLane, double> lastTimes = [];
            foreach (ChartNote note in notes)
            {
                if (!note.Playable) continue;
                if (lastTimes.TryGetValue(note.Lane, out double lastTime) && note.TimeMs - lastTime < minimumGap)
                {
                    note.Playable = false;
                    continue;
                }
                lastTimes[note.Lane] = note.TimeMs;
            }
        }

        public static void RemapOverflow(List<ChartNote> notes, Dictionary<int, int> wavToNote)
        {
            Dictionary<GameLane, int> mostUsedByLane = [];
            IEnumerable<IGrouping<GameLane, ChartNote>> lanes = notes
                .Where(note => note.Playable && wavToNote.ContainsKey(note.WavId))
                .GroupBy(note => note.Lane);
            foreach (IGrouping<GameLane, ChartNote> lane in lanes)
                mostUsedByLane[lane.Key] = lane.GroupBy(note => note.WavId).OrderByDescending(group => group.Count()).First().Key;

            foreach (ChartNote note in notes)
            {
                if (!note.Playable || wavToNote.ContainsKey(note.WavId)) continue;
                if (mostUsedByLane.TryGetValue(note.Lane, out int replacement)) note.WavId = replacement;
                else note.Playable = false;
            }
        }

        internal static int Priority(ChartNote note) => note.Lane switch
        {
            GameLane.SD => 50,
            GameLane.LC => 40,
            GameLane.RC => note.Channel == 0x19 ? 25 : 40,
            GameLane.HT or GameLane.LT or GameLane.FT => 30,
            GameLane.HH => 20,
            GameLane.None => 0,
            GameLane.BD => 0,
            GameLane.LP => 0,
            _ => 0,
        };
    }
}
