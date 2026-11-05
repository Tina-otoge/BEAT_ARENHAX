using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BeatArenhax.CustomSongs
{
    internal static class GameParamWriter
    {
        public static Dictionary<int, int> AssignNotes(IEnumerable<List<ChartNote>> charts)
        {
            Dictionary<int, int> counts = [];
            foreach (List<ChartNote> notes in charts)
            {
                foreach (ChartNote note in notes)
                {
                    if (!note.Playable) continue;
                    counts[note.WavId] = counts.TryGetValue(note.WavId, out int count) ? count + 1 : 1;
                }
            }

            Dictionary<int, int> map = [];
            int nextNote = 1;
            foreach (KeyValuePair<int, int> entry in counts.OrderByDescending(item => item.Value))
            {
                if (nextNote > 127) break;
                map[entry.Key] = nextNote++;
            }
            return map;
        }

        public static string BuildJson(Dictionary<int, int> wavToNote, List<ChartNote> defaultsFrom)
        {
            StringBuilder json = new();
            _ = json.Append("{\"SoundDefine\":[\n{\"id\":-1, \"name\":\"").Append(ChartConverter.BgmLabel).Append("\"}");
            foreach (KeyValuePair<int, int> entry in wavToNote.OrderBy(item => item.Value))
            {
                _ = json.Append(",\n{\"id\":").Append(entry.Value.ToString(CultureInfo.InvariantCulture))
                    .Append(", \"name\":\"").Append(ChartConverter.ChipLabelPrefix)
                    .Append(entry.Key.ToString(CultureInfo.InvariantCulture)).Append("\"}");
            }
            _ = json.Append("\n],\n\"DefaultSound\":[");

            (int index, GameLane lane, GameLane[] fallback)[] indexLanes =
            [
                (0, GameLane.HH, new[] { GameLane.RC, GameLane.LC }),
                (1, GameLane.SD, new GameLane[0]),
                (2, GameLane.HT, new[] { GameLane.LT, GameLane.FT }),
                (3, GameLane.LT, new[] { GameLane.HT, GameLane.FT }),
                (4, GameLane.FT, new[] { GameLane.LT, GameLane.HT }),
                (6, GameLane.RC, new[] { GameLane.LC, GameLane.HH }),
                (7, GameLane.LC, new[] { GameLane.RC, GameLane.HH }),
            ];

            bool first = true;
            foreach ((int index, GameLane lane, GameLane[] fallback) in indexLanes)
            {
                int note = MostUsed(lane, defaultsFrom, wavToNote);
                foreach (GameLane fallbackLane in fallback)
                {
                    if (note >= 0) break;
                    note = MostUsed(fallbackLane, defaultsFrom, wavToNote);
                }
                if (note < 0) continue;
                _ = json.Append(first ? "\n" : ",\n");
                first = false;
                _ = json.Append("{\"index\":").Append(index).Append(", \"sound\":").Append(note).Append("}");
            }
            _ = json.Append("\n]}");
            return json.ToString();
        }

        private static int MostUsed(GameLane lane, List<ChartNote> notes, Dictionary<int, int> wavToNote)
        {
            IGrouping<int, ChartNote> group = notes
                .Where(note => note.Playable && note.Lane == lane && wavToNote.ContainsKey(note.WavId))
                .GroupBy(note => note.WavId)
                .OrderByDescending(wavGroup => wavGroup.Count())
                .FirstOrDefault();
            return group == null ? -1 : wavToNote[group.Key];
        }
    }
}
