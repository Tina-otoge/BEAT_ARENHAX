namespace BeatArenhax.CustomSongs
{
    internal static class ReplayLoopTiming
    {
        internal static int Period(int lastFrameTime) =>
            lastFrameTime > 0 && lastFrameTime < int.MaxValue ? lastFrameTime + 1 : 0;

        internal static int Wrap(int time, int period) =>
            period > 0 && time >= period ? time % period : time;

        internal static bool NeedsCursorReset(int previousTime, int time, int period) =>
            period > 0 && time >= period &&
            (previousTime < 0 || previousTime / period != time / period || time < previousTime);
    }
}
