using System;
using System.Collections.Generic;

namespace BeatArenhax
{
    public enum CinematicShot
    {
        Standard,
        ArcUp,
        ArcDown,
        High,
        Low,
        BackLeft,
        BackRight,
        PlayerFirstPerson,
        NpcFirstPerson,
    }

    internal sealed class CinematicCameraDirector
    {
        private readonly Random _random;
        private bool _active;
        private float _started;
        private float _duration;
        private readonly CinematicShot[] _available = new CinematicShot[Enum.GetValues<CinematicShot>().Length];

        internal CinematicShot Shot { get; private set; }
        internal float Progress { get; private set; }
        internal float Duration => _duration;

        internal CinematicCameraDirector(Random random) => _random = random;

        internal static bool IsMoving(CinematicShot shot) =>
            shot is CinematicShot.ArcUp or CinematicShot.ArcDown or CinematicShot.BackLeft
                or CinematicShot.BackRight or CinematicShot.PlayerFirstPerson or CinematicShot.NpcFirstPerson;

        internal static CinematicShot[] ParseTypes(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Enum.GetValues<CinematicShot>();
            List<CinematicShot> result = [];
            foreach (string part in value.Split(','))
            {
                string name = part.Trim();
                if (!Enum.TryParse(name, true, out CinematicShot shot)
                    || !Enum.IsDefined(shot) || !string.Equals(name, shot.ToString(), StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"Invalid CinematicCameraTypes entry '{name}' in [Spectator].");
                if (!result.Contains(shot)) result.Add(shot);
            }
            return result.ToArray();
        }

        internal void Update(bool playing, float now, bool hasNpc = false, CinematicShot[] allowed = null)
        {
            if (!playing)
            {
                _active = false;
                Shot = CinematicShot.Standard;
                Progress = 0f;
                return;
            }
            allowed ??= Enum.GetValues<CinematicShot>();
            int count = 0;
            bool hasStandard = false;
            bool hasCurrent = false;
            foreach (CinematicShot shot in allowed)
            {
                if (shot == CinematicShot.NpcFirstPerson && !hasNpc) continue;
                _available[count++] = shot;
                hasStandard |= shot == CinematicShot.Standard;
                hasCurrent |= shot == Shot;
            }
            if (count == 0)
            {
                _active = false;
                Shot = CinematicShot.Standard;
                Progress = 0f;
                return;
            }
            if (!_active || !hasCurrent)
            {
                _active = true;
                Shot = hasStandard ? CinematicShot.Standard : _available[_random.Next(count)];
                StartShot(now);
            }
            else if (now - _started >= _duration)
            {
                if (Shot != CinematicShot.Standard && hasStandard)
                    Shot = CinematicShot.Standard;
                else if (count > 1)
                {
                    int index = _random.Next(count - 1);
                    for (int i = 0; i < count; i++)
                        if (_available[i] != Shot && index-- == 0)
                        {
                            Shot = _available[i];
                            break;
                        }
                }
                StartShot(now);
            }
            Progress = Math.Clamp((now - _started) / _duration, 0f, 1f);
        }

        private void StartShot(float now)
        {
            _started = now;
            float minimum = IsMoving(Shot) ? 6f : 3f;
            _duration = minimum + (float)_random.NextDouble() * (15f - minimum);
        }
    }
}
