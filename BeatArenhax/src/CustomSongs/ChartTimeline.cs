using System;
using System.Collections.Generic;
using BeatArenhax.DTX;

namespace BeatArenhax.CustomSongs
{
    internal sealed class ChartTimeline
    {
        internal sealed class Anchor
        {
            public int Tick;
            public double TimeMs;
        }

        public readonly List<Anchor> Anchors = [];
        public readonly List<(int tick, int usPerQuarter)> Tempos = [];
        public readonly List<(int tick, int num, int den)> TimeSigs = [];
        public int[] MeasureStartTick;
        public int[] MeasureTicks;
        public double[] MeasureMult;
        public List<(double fraction, int ticks)>[] MeasureStops;

        public int MeasureTick(int measure, double fraction)
        {
            int tick = MeasureStartTick[measure] + (int)Math.Round(fraction * MeasureTicks[measure]);
            foreach ((double stopFraction, int ticks) in MeasureStops[measure])
            {
                if (stopFraction >= fraction) break;
                tick += ticks;
            }
            return tick;
        }

        public double TickToMs(int tick)
        {
            int index = Anchors.Count - 1;
            int low = 0;
            int high = Anchors.Count - 1;
            while (low <= high)
            {
                int middle = (low + high) / 2;
                if (Anchors[middle].Tick <= tick)
                {
                    index = middle;
                    low = middle + 1;
                }
                else high = middle - 1;
            }
            Anchor anchor = Anchors[index];
            double millisecondsPerTick = LastMillisecondsPerTick(index);
            return anchor.TimeMs + ((tick - anchor.Tick) * millisecondsPerTick);
        }

        public int MsToTick(double milliseconds)
        {
            int index = 0;
            for (int i = 0; i < Anchors.Count; i++)
            {
                if (!(Anchors[i].TimeMs <= milliseconds)) continue;
                index = i;
            }
            Anchor anchor = Anchors[index];
            double millisecondsPerTick = LastMillisecondsPerTick(index);
            return anchor.Tick + (int)Math.Ceiling((milliseconds - anchor.TimeMs) / millisecondsPerTick);
        }

        private double LastMillisecondsPerTick(int anchorIndex)
        {
            for (int i = anchorIndex; i + 1 < Anchors.Count; i++)
            {
                Anchor current = Anchors[i];
                Anchor next = Anchors[i + 1];
                if (next.Tick <= current.Tick) continue;
                return (next.TimeMs - current.TimeMs) / (next.Tick - current.Tick);
            }
            for (int i = anchorIndex; i > 0; i--)
            {
                Anchor previous = Anchors[i - 1];
                Anchor current = Anchors[i];
                if (current.Tick <= previous.Tick) continue;
                return (current.TimeMs - previous.TimeMs) / (current.Tick - previous.Tick);
            }
            return 60000.0 / 120 / ChartConverter.Tpq;
        }

        public static ChartTimeline Build(DtxChart chart, int leadInMeasures, int lastMeasure)
        {
            ChartTimeline timeline = new();
            int totalMeasures = leadInMeasures + lastMeasure + 2;
            timeline.MeasureStartTick = new int[totalMeasures + 1];
            timeline.MeasureTicks = new int[totalMeasures];
            timeline.MeasureMult = new double[totalMeasures];
            timeline.MeasureStops = new List<(double, int)>[totalMeasures];
            for (int i = 0; i < totalMeasures; i++) timeline.MeasureStops[i] = [];

            List<TimingChange> changes = [];
            foreach (DtxChip chip in chart.Chips)
            {
                if (chip.Channel == 0x03)
                {
                    changes.Add(new TimingChange
                    {
                        Measure = chip.Measure + leadInMeasures,
                        Fraction = chip.Frac,
                        Bpm = chart.BaseBpm + chip.Value,
                    });
                }
                else if (chip.Channel == 0x08)
                {
                    if (chart.BpmDefs.TryGetValue(chip.Value, out double bpm))
                    {
                        changes.Add(new TimingChange
                        {
                            Measure = chip.Measure + leadInMeasures,
                            Fraction = chip.Frac,
                            Bpm = chart.BaseBpm + bpm,
                        });
                    }
                    else AddWarning(chart, $"BPM change references undefined BPM{chip.Value:X2}");
                }
                else if (chip.Channel == 0x09)
                {
                    if (chart.StopDefs.TryGetValue(chip.Value, out double stop) && stop > 0)
                    {
                        changes.Add(new TimingChange
                        {
                            Measure = chip.Measure + leadInMeasures,
                            Fraction = chip.Frac,
                            Stop = stop,
                        });
                    }
                    else AddWarning(chart, $"Stop references undefined STOP{chip.Value:X2}");
                }
            }
            changes.Sort((left, right) => left.Measure != right.Measure
                ? left.Measure.CompareTo(right.Measure)
                : left.Fraction != right.Fraction
                    ? left.Fraction.CompareTo(right.Fraction)
                    : left.IsStop.CompareTo(right.IsStop));

            double bpmNow = chart.Bpm > 0 ? chart.Bpm : 120;
            double timeMs = 0;
            int tick = 0;
            int changeIndex = 0;
            (int num, int den) lastTimeSignature = (0, 0);
            for (int measure = 0; measure < totalMeasures; measure++)
            {
                double measureMult = measure < leadInMeasures ? 1.0
                    : (chart.MeasureLength.TryGetValue(measure - leadInMeasures, out double length) ? length : 1.0);
                (int num, int den) signature = TimeSigFor(measureMult);
                int measureTicks = signature.num * ChartConverter.Tpq * 4 / signature.den;
                timeline.MeasureStartTick[measure] = tick;
                timeline.MeasureTicks[measure] = measureTicks;
                timeline.MeasureMult[measure] = measureMult;
                if (signature != lastTimeSignature)
                {
                    timeline.TimeSigs.Add((tick, signature.num, signature.den));
                    lastTimeSignature = signature;
                }

                timeline.Anchors.Add(new Anchor { Tick = tick, TimeMs = timeMs });
                double fraction = 0;
                int stopTicks = 0;
                while (changeIndex < changes.Count && changes[changeIndex].Measure == measure)
                {
                    TimingChange change = changes[changeIndex++];
                    timeMs += (change.Fraction - fraction) * 4 * measureMult * 60000.0 / bpmNow;
                    fraction = change.Fraction;
                    int changeTick = tick + (int)Math.Round(fraction * measureTicks) + stopTicks;
                    if (!change.IsStop)
                    {
                        bpmNow = change.Bpm;
                        timeline.Anchors.Add(new Anchor { Tick = changeTick, TimeMs = timeMs });
                    }
                    else if (change.Stop > 0 && bpmNow > 0)
                    {
                        int addedTicks = Math.Max(1, (int)Math.Round(change.Stop * ChartConverter.Tpq / 192.0));
                        timeline.MeasureStops[measure].Add((fraction, addedTicks));
                        timeline.Anchors.Add(new Anchor { Tick = changeTick, TimeMs = timeMs });
                        timeMs += change.Stop * 60000.0 / bpmNow / 192.0;
                        stopTicks += addedTicks;
                        timeline.Anchors.Add(new Anchor { Tick = changeTick + addedTicks, TimeMs = timeMs });
                    }
                }
                timeMs += (1 - fraction) * 4 * measureMult * 60000.0 / bpmNow;
                tick += measureTicks + stopTicks;
            }
            timeline.MeasureStartTick[totalMeasures] = tick;
            timeline.Anchors.Add(new Anchor { Tick = tick, TimeMs = timeMs });

            int lastMicrosecondsPerQuarter = -1;
            for (int i = 0; i + 1 < timeline.Anchors.Count; i++)
            {
                Anchor current = timeline.Anchors[i];
                Anchor next = timeline.Anchors[i + 1];
                if (next.Tick <= current.Tick) continue;
                double microsecondsPerQuarter = (next.TimeMs - current.TimeMs) * 1000.0
                    / ((double)(next.Tick - current.Tick) / ChartConverter.Tpq);
                int microseconds = (int)Math.Clamp(Math.Round(microsecondsPerQuarter), 1000, 0xFFFFFF);
                if (microseconds == lastMicrosecondsPerQuarter) continue;
                timeline.Tempos.Add((current.Tick, microseconds));
                lastMicrosecondsPerQuarter = microseconds;
            }
            if (timeline.Tempos.Count == 0) timeline.Tempos.Add((0, (int)Math.Round(60000000.0 / bpmNow)));
            if (timeline.Tempos[0].tick != 0) timeline.Tempos.Insert(0, (0, timeline.Tempos[0].usPerQuarter));

            timeline.Anchors.Clear();
            double milliseconds = 0;
            int previousTick = 0;
            int currentMicrosecondsPerQuarter = timeline.Tempos[0].usPerQuarter;
            foreach ((int tick, int microsecondsPerQuarter) tempo in timeline.Tempos)
            {
                milliseconds += (double)(tempo.tick - previousTick) * currentMicrosecondsPerQuarter / 1000.0 / ChartConverter.Tpq;
                timeline.Anchors.Add(new Anchor { Tick = tempo.tick, TimeMs = milliseconds });
                previousTick = tempo.tick;
                currentMicrosecondsPerQuarter = tempo.microsecondsPerQuarter;
            }
            milliseconds += (double)(tick - previousTick) * currentMicrosecondsPerQuarter / 1000.0 / ChartConverter.Tpq;
            timeline.Anchors.Add(new Anchor { Tick = tick, TimeMs = milliseconds });
            return timeline;
        }

        private static (int num, int den) TimeSigFor(double measureMult)
        {
            foreach (int denominator in new[] { 4, 8, 16, 32, 64 })
            {
                double numerator = measureMult * denominator;
                if (!(Math.Abs(numerator - Math.Round(numerator)) < 1e-6 && Math.Round(numerator) >= 1 && Math.Round(numerator) <= 255)) continue;
                return ((int)Math.Round(numerator), denominator);
            }
            return (Math.Clamp((int)Math.Round(measureMult * 64), 1, 255), 64);
        }

        private static void AddWarning(DtxChart chart, string message)
        {
            if (!chart.Warnings.Contains(message)) chart.Warnings.Add(message);
        }

        private sealed class TimingChange
        {
            public int Measure;
            public double Fraction;
            public double Bpm;
            public double Stop;
            public bool IsStop => Stop > 0;
        }
    }
}
