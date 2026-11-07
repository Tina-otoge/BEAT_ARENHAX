using System;

namespace BeatArenhax.CustomSongs
{
    internal static class TemplateSelection
    {
        internal static int ChooseIndex(int count, Func<int, int> next)
        {
            if (count <= 0) throw new InvalidOperationException("No compatible template songs are available.");
            int index = next(count);
            if (index < 0 || index >= count) throw new InvalidOperationException("Template random index is out of range.");
            return index;
        }

        internal static bool Compatible(int id, string cue, int dataType, int thirdPlayType,
            int expectedDataType, int expectedThirdPlayType) =>
            id >= 0 && id < 8000 && !string.IsNullOrWhiteSpace(cue)
            && dataType == expectedDataType && thirdPlayType == expectedThirdPlayType;
    }
}
