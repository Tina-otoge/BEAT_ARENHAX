using System;
using System.Collections.Generic;
using System.Globalization;

namespace BeatArenhax
{
    public sealed class PedalBinding
    {
        public int Key { get; }
        public int Button { get; }
        public int MIDI { get; }
        public bool Enabled => Key >= 0 || Button >= 0 || MIDI >= 0;

        private PedalBinding(int key, int button, int midi)
        {
            Key = key;
            Button = button;
            MIDI = midi;
        }

        public static PedalBinding Parse(string key, string button, string midi) =>
            new(ParseKey(key), ParseButton(button), ParseMIDI(midi));

        private static readonly Dictionary<string, int> Keys = new(StringComparer.OrdinalIgnoreCase)
        {
            ["left"] = 276, ["leftarrow"] = 276, ["right"] = 275, ["rightarrow"] = 275,
            ["up"] = 273, ["uparrow"] = 273, ["down"] = 274, ["downarrow"] = 274,
            ["space"] = 32, ["spacebar"] = 32, ["enter"] = 13, ["return"] = 13,
            ["tab"] = 9, ["escape"] = 27, ["esc"] = 27, ["backspace"] = 8,
            ["delete"] = 127, ["insert"] = 277, ["home"] = 278, ["end"] = 279,
            ["pageup"] = 280, ["pagedown"] = 281,
            ["leftshift"] = 304, ["rightshift"] = 303, ["shift"] = 304,
            ["leftctrl"] = 306, ["leftcontrol"] = 306, ["ctrl"] = 306,
            ["rightctrl"] = 305, ["rightcontrol"] = 305,
            ["leftalt"] = 308, ["rightalt"] = 307, ["alt"] = 308,
            ["minus"] = 45, ["equals"] = 61, ["comma"] = 44, ["period"] = 46,
            ["slash"] = 47, ["backslash"] = 92, ["semicolon"] = 59,
            ["quote"] = 39, ["backquote"] = 96, ["leftbracket"] = 91, ["rightbracket"] = 93,
            ["clear"] = 12, ["pause"] = 19, ["capslock"] = 301, ["numlock"] = 300, ["scrolllock"] = 302,
            ["leftwindows"] = 311, ["rightwindows"] = 312, ["altgr"] = 313,
            ["help"] = 315, ["print"] = 316, ["printscreen"] = 316, ["sysreq"] = 317,
            ["break"] = 318, ["menu"] = 319,
            ["keypadperiod"] = 266, ["keypaddivide"] = 267, ["keypadmultiply"] = 268,
            ["keypadminus"] = 269, ["keypadplus"] = 270, ["keypadenter"] = 271, ["keypadequals"] = 272,
        };

        private static string Normalize(string value) =>
            (value ?? "").Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();

        private static int ParseKey(string value)
        {
            string name = Normalize(value);
            if (name.Length == 0 && string.IsNullOrWhiteSpace(value)) return -1;
            if (value?.Trim() is string literal && literal.Length == 1)
            {
                char c = char.ToLowerInvariant(literal[0]);
                if (c is >= 'a' and <= 'z' or >= '0' and <= '9' || "-=,./\\;'`[]".Contains(c))
                    return c;
            }
            if (Keys.TryGetValue(name, out int code)) return code;
            if (name.StartsWith("f") && int.TryParse(name[1..], out int f) && f is >= 1 and <= 15)
                return 281 + f;
            if (name.StartsWith("keypad") && int.TryParse(name[6..], out int digit) && digit is >= 0 and <= 9)
                return 256 + digit;
            throw new FormatException($"Invalid pedal keyboard key '{value}': use a letter, digit, arrow, space, F1-F15 or a named keyboard key.");
        }

        private static int ParseButton(string value)
        {
            string name = (value ?? "").Trim().ToLowerInvariant();
            if (name.Length == 0) return -1;
            int label = name switch
            {
                "a" => 0, "b" => 1, "x" => 2, "y" => 3, "lb" => 4, "rb" => 5,
                "back" => 6, "start" => 7, "ls" => 8, "rs" => 9,
                _ => -1,
            };
            if (label >= 0) return label;
            if (int.TryParse(name, NumberStyles.None, CultureInfo.InvariantCulture, out int number)
                && number is >= 1 and <= 20) return number - 1;
            throw new FormatException($"Invalid pedal joypad button '{value}': use 1-20 or A/B/X/Y/LB/RB/Back/Start/LS/RS.");
        }

        private static int ParseMIDI(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return -1;
            if (int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int note)
                && note is >= 0 and <= 127) return note;
            throw new FormatException($"Invalid pedal MIDI note '{value}': expected 0-127 or empty.");
        }
    }
}
