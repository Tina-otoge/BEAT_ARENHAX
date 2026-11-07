namespace BeatArenhax
{
    internal static class SongWheelStatsText
    {
        internal static string FormatLine(string level, string grade, string levelColor, string gradeColor) =>
            "<color=" + levelColor + ">" + level + "</color> <color=" + gradeColor + ">" + grade + "</color>";
    }
}
