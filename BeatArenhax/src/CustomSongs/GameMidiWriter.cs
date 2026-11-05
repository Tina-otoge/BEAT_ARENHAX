using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BeatArenhax.CustomSongs
{
    internal static class GameMidiWriter
    {
        public static byte[] Write(ChartTimeline timeline, List<ChartNote> notes, Dictionary<int, int> wavToNote, int endTick)
        {
            List<byte[]> tracks = [];
            TrackWriter conductor = new();
            List<(int tick, int order, byte[] data)> conductorEvents = [];
            foreach ((int tick, int numerator, int denominator) in timeline.TimeSigs)
            {
                int denominatorPower = 0;
                while ((1 << denominatorPower) < denominator) denominatorPower++;
                conductorEvents.Add((tick, 0, new byte[] { 0xFF, 0x58, 0x04, (byte)numerator, (byte)denominatorPower, 24, 8 }));
            }
            foreach ((int tick, int microsecondsPerQuarter) in timeline.Tempos)
            {
                byte[] data =
                [
                    0xFF, 0x51, 0x03,
                    (byte)(microsecondsPerQuarter >> 16),
                    (byte)(microsecondsPerQuarter >> 8),
                    (byte)microsecondsPerQuarter,
                ];
                conductorEvents.Add((tick, 1, data));
            }
            foreach ((int tick, int order, byte[] data) in conductorEvents.OrderBy(item => item.tick).ThenBy(item => item.order))
                conductor.Raw(tick, data);
            conductor.Raw(endTick + ChartConverter.Tpq, [0xFF, 0x2F, 0x00]);
            tracks.Add(conductor.ToArray());

            string[] laneNames = ["BD", "SD", "HH", "LP", "HT", "LT", "FT", "RC", "LC"];
            for (int lane = 1; lane <= 9; lane++)
            {
                TrackWriter writer = new();
                writer.Name(laneNames[lane - 1]);
                writer.Raw(0, [0xB0, 7, 90]);
                List<ChartNote> laneNotes = [.. notes
                    .Where(note => note.Playable && (int)note.Lane == lane && wavToNote.ContainsKey(note.WavId))
                    .OrderBy(note => note.Tick)];
                List<(int tick, int order, byte[] data)> events = [];
                for (int i = 0; i < laneNotes.Count; i++)
                {
                    ChartNote note = laneNotes[i];
                    int midiNote = wavToNote[note.WavId];
                    int length = 119;
                    if (i + 1 < laneNotes.Count) length = Math.Min(length, laneNotes[i + 1].Tick - note.Tick - 1);
                    if (length < 1) length = 1;
                    events.Add((note.Tick, 1, new byte[] { 0x90, (byte)midiNote, 127 }));
                    events.Add((note.Tick + length, 0, new byte[] { 0x80, (byte)midiNote, 64 }));
                }
                foreach ((int tick, int order, byte[] data) in events.OrderBy(item => item.tick).ThenBy(item => item.order))
                    writer.Raw(tick, data);
                writer.Raw(endTick + ChartConverter.Tpq, [0xFF, 0x2F, 0x00]);
                tracks.Add(writer.ToArray());
            }

            TrackWriter system = new();
            system.Name("sys");
            system.Raw(0, [0xB0, 7, 90]);
            system.Raw(0, [0xB0, 54, 127]);
            system.Raw(endTick, [0xB0, 54, 0]);
            system.Raw(endTick + 431, [0xB0, 60, 0]);
            system.Raw(endTick + ChartConverter.Tpq, [0xFF, 0x2F, 0x00]);
            tracks.Add(system.ToArray());

            TrackWriter automatic = new();
            automatic.Name("auto");
            automatic.Raw(0, [0xB0, 7, 90]);
            automatic.Raw(endTick + ChartConverter.Tpq, [0xFF, 0x2F, 0x00]);
            tracks.Add(automatic.ToArray());

            MemoryStream midi = new();
            midi.Write(Encoding.ASCII.GetBytes("MThd"), 0, 4);
            WriteBigEndian32(midi, 6);
            WriteBigEndian16(midi, 1);
            WriteBigEndian16(midi, tracks.Count);
            WriteBigEndian16(midi, ChartConverter.Tpq);
            foreach (byte[] track in tracks)
            {
                midi.Write(Encoding.ASCII.GetBytes("MTrk"), 0, 4);
                WriteBigEndian32(midi, track.Length);
                midi.Write(track, 0, track.Length);
            }
            return midi.ToArray();
        }

        private static void WriteBigEndian16(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private static void WriteBigEndian32(Stream stream, int value)
        {
            stream.WriteByte((byte)(value >> 24));
            stream.WriteByte((byte)(value >> 16));
            stream.WriteByte((byte)(value >> 8));
            stream.WriteByte((byte)value);
        }

        private sealed class TrackWriter
        {
            private readonly MemoryStream _stream = new();
            private int _lastTick;

            public void Name(string name)
            {
                byte[] nameBytes = Encoding.ASCII.GetBytes(name + "\0");
                byte[] data = new byte[3 + nameBytes.Length];
                data[0] = 0xFF;
                data[1] = 0x03;
                data[2] = (byte)nameBytes.Length;
                Array.Copy(nameBytes, 0, data, 3, nameBytes.Length);
                Raw(0, data);
            }

            public void Raw(int tick, byte[] data)
            {
                if (tick < _lastTick) tick = _lastTick;
                WriteVariableLength(tick - _lastTick);
                _lastTick = tick;
                _stream.Write(data, 0, data.Length);
            }

            private void WriteVariableLength(int value)
            {
                Stack<byte> bytes = new();
                bytes.Push((byte)(value & 0x7F));
                value >>= 7;
                while (value > 0)
                {
                    bytes.Push((byte)((value & 0x7F) | 0x80));
                    value >>= 7;
                }
                while (bytes.Count > 0) _stream.WriteByte(bytes.Pop());
            }

            public byte[] ToArray() => _stream.ToArray();
        }
    }
}
