using System;
using System.Collections.Generic;
using System.Linq;
using BeatArenhax;

namespace BeatArenhax.CustomSongs
{
    public static class CustomChartSelection
    {
        public const int VisibleCount = 4;

        public static IReadOnlyList<CustomSongChart> Select(IEnumerable<CustomSongChart> charts, ChartDifficultyCut cut)
        {
            ArgumentNullException.ThrowIfNull(charts);
            List<CustomSongChart> ordered = [.. charts.OrderBy(chart => chart.DLevel).ThenBy(chart => chart.SetIndex)];
            int start = cut switch
            {
                ChartDifficultyCut.Easiest => 0,
                ChartDifficultyCut.Hardest => Math.Max(0, ordered.Count - VisibleCount),
                _ => throw new ArgumentOutOfRangeException(nameof(cut)),
            };
            return ordered.Skip(start).Take(VisibleCount).ToArray();
        }

        public static string RecognizedColor(string label) => Colors.DifficultyColor(label);
    }
}
