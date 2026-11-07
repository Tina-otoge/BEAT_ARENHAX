using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BeatArenhax.Audio
{
    internal sealed class PedalMidiInput(Action<string> log) : IDisposable
    {
        private const uint CallbackFunction = 0x30000;
        private const uint DataMessage = 0x3C3;
        private const uint ErrorMessage = 0x3C5;
        private readonly List<IntPtr> _ports = [];
        private readonly ConcurrentQueue<(int note, long time)> _notes = new();
        private MidiCallback _rootedCallback;
        private uint _devices = uint.MaxValue;

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void MidiCallback(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr timestamp);

        [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr handle, uint device, MidiCallback callback, UIntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr handle);
        [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr handle);

        internal void RefreshDevices()
        {
            uint count = midiInGetNumDevs();
            if (count == _devices) return;
            ClosePorts();
            _devices = count;
            _rootedCallback ??= Receive;
            if (count == 0) log("No MIDI input devices found; configured MIDI pedals cannot receive hits.");
            for (uint device = 0; device < count; device++)
            {
                uint result = midiInOpen(out IntPtr port, device, _rootedCallback, UIntPtr.Zero, CallbackFunction);
                if (result != 0)
                {
                    log($"MIDI input {device} could not open (WinMM error {result}).");
                    continue;
                }
                _ports.Add(port);
                result = midiInStart(port);
                if (result != 0) log($"MIDI input {device} could not start (WinMM error {result}).");
                else log($"MIDI pedal input opened device {device}.");
            }
        }

        private void Receive(IntPtr handle, uint message, UIntPtr instance, UIntPtr data, UIntPtr timestamp)
        {
            if (message == ErrorMessage)
            {
                log("MIDI input received an invalid short message.");
                return;
            }
            if (message != DataMessage) return;
            uint packed = (uint)data.ToUInt64();
            if (!IsHit(packed, out int note)) return;
            if (_notes.Count >= 256)
            {
                log("MIDI pedal input overflow; discarding excess events.");
                return;
            }
            _notes.Enqueue((note, Stopwatch.GetTimestamp()));
        }

        internal static bool IsHit(uint packed, out int note)
        {
            note = (int)((packed >> 8) & 0x7F);
            return (packed & 0xF0) == 0x90 && ((packed >> 16) & 0x7F) != 0;
        }

        internal bool TryRead(out int note, out double ageMs)
        {
            if (_notes.TryDequeue(out var hit))
            {
                note = hit.note;
                ageMs = (Stopwatch.GetTimestamp() - hit.time) * 1000.0 / Stopwatch.Frequency;
                return true;
            }
            note = -1;
            ageMs = 0;
            return false;
        }

        private void ClosePorts()
        {
            for (int i = _ports.Count - 1; i >= 0; i--)
            {
                IntPtr port = _ports[i];
                Check(midiInStop(port), "stop");
                Check(midiInReset(port), "reset");
                uint result = midiInClose(port);
                if (result == 0) _ports.RemoveAt(i);
                else Check(result, "close");
            }
            if (_ports.Count != 0)
                throw new InvalidOperationException("MIDI input ports could not close; retaining their callback until close succeeds.");
        }

        private void Check(uint result, string operation)
        {
            if (result != 0) log($"MIDI input {operation} failed (WinMM error {result}).");
        }

        public void Dispose()
        {
            ClosePorts();
            _notes.Clear();
            GC.KeepAlive(_rootedCallback);
        }
    }
}
