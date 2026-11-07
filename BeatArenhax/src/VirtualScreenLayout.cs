namespace BeatArenhax
{
    internal static class VirtualScreenLayout
    {
        internal const float Width = 700f;
        internal const float Height = 800f;
        internal const float WorldUnitsPerPixel = 0.002f;
        internal const float BaseDistance = 1.5f;

        internal static float CenterHeight(float scale, float offsetY) =>
            0.65f + Height * WorldUnitsPerPixel * 0.5f * (scale - 1f) + offsetY;
        internal const float JudgeY = 100f;
        internal const float TravelMs = 1800f;
        internal const int LaneCount = 10;
        internal const int PlayableLaneCount = 8;
        internal const int LeftPedalColumn = 2;
        internal const int BassDrumColumn = 5;
        internal const int LeftPedalKey = 8;
        internal const int RightPedalKey = 9;
        internal const int LeftPedalChipX = 663;
        internal const int LeftPedalChipWidth = 55;
        internal const int FireFrameCount = 12;
        internal const int FireFrameMs = 50;
        internal const int JudgeFrameCount = 24;
        internal const int JudgeFrameMs = 14;

        internal static int AutomaticKey(CustomSongs.ChartNote note) => note.Playable ? -1 : note.Channel switch
        {
            0x11 or 0x18 => 0,
            0x12 => 1,
            0x13 => RightPedalKey,
            0x14 => 2,
            0x15 => 3,
            0x17 => 4,
            0x16 => 5,
            0x19 => 6,
            0x1A => 7,
            0x1B or 0x1C => LeftPedalKey,
            _ => -1,
        };

        internal static byte GrayPixel(byte r, byte g, byte b) =>
            (byte)((299 * r + 587 * g + 114 * b + 500) / 1000);

        internal static (float X, float Z) Forward(float rightX, float rightZ)
        {
            float length = System.MathF.Sqrt(rightX * rightX + rightZ * rightZ);
            if (!float.IsFinite(length) || length < 0.0001f)
                throw new System.InvalidOperationException("The central drums do not define a horizontal screen direction.");
            return (-rightZ / length, rightX / length);
        }

        // BEAT ARENA's drum keys are HH, SD, HT, LT, FT, crash, ride, left crash.
        internal static int Column(int key) => key switch
        {
            7 => 0,
            0 => 1,
            1 => 3,
            2 => 4,
            3 => 6,
            4 => 7,
            5 => 8,
            6 => 9,
            LeftPedalKey => LeftPedalColumn,
            RightPedalKey => BassDrumColumn,
            _ => -1,
        };

        internal static float NoteY(int noteMs, int currentMs) =>
            (float)(JudgeY + ((noteMs - (double)currentMs) / TravelMs * (Height - JudgeY)));

        internal static int FireRow(int key) => key switch
        {
            7 => 0, 0 => 1, 1 => 2, RightPedalKey => 3, 2 => 4,
            3 => 5, 4 => 6, 5 => 7, LeftPedalKey => 8, 6 => 9,
            _ => -1,
        };

        // Native IsBackTrack tests sound ID 1, which custom charts also use for playable samples.
        internal static bool ShowFeedback(int key, bool auto, bool backtrack, bool customSong) =>
            Column(key) >= 0 && !auto && (!backtrack || customSong);

        internal static int EffectFrame(int elapsedMs, int frameMs, int count) =>
            elapsedMs >= 0 && elapsedMs < frameMs * count ? elapsedMs / frameMs : -1;

        internal static void FirePixel(ref byte r, ref byte g, ref byte b, ref byte a)
        {
            int peak = System.Math.Max(r, System.Math.Max(g, b));
            if (peak == 0) { a = 0; return; }
            a = (byte)((a * peak + 127) / 255);
            r = (byte)((r * 255 + peak / 2) / peak);
            g = (byte)((g * 255 + peak / 2) / peak);
            b = (byte)((b * 255 + peak / 2) / peak);
        }
    }
}
