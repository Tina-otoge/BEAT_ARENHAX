using System.Collections.Generic;
using System.Linq;

namespace BeatArenhax.CustomSongs
{
    internal sealed class PedalNoteMap
    {
        private readonly Dictionary<int, Queue<int>> _keys = [];
        internal int Remaining { get; private set; }

        internal PedalNoteMap(ConvertedChart chart, Dictionary<int, int> wavToNote)
        {
            // The native MIDI reader merges BD/LP into automatic notes and discards track identity.
            // Only bound feet occupy those tracks; restore their lanes in time order per sound ID.
            foreach (ChartNote note in chart.Notes.OrderBy(note => note.Tick))
            {
                if (!note.Playable || note.Lane is not (GameLane.BD or GameLane.LP)) continue;
                int sound = wavToNote[note.WavId];
                if (!_keys.TryGetValue(sound, out Queue<int> keys))
                    _keys.Add(sound, keys = new Queue<int>());
                keys.Enqueue(note.Lane == GameLane.LP ? VirtualScreenLayout.LeftPedalKey : VirtualScreenLayout.RightPedalKey);
                Remaining++;
            }
        }

        internal bool TryTake(int sound, out int key)
        {
            if (_keys.TryGetValue(sound, out Queue<int> keys) && keys.TryDequeue(out key))
            {
                Remaining--;
                return true;
            }
            key = -1;
            return false;
        }
    }
}
