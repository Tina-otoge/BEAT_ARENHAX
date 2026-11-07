using System;

namespace BeatArenhax
{
    internal sealed class SpectatorCameraControl
    {
        internal const float DefaultPitch = 8f;
        private const float MoveSpeed = 2f;
        private const float DegreesPerPixel = 0.2f;
        private const float MetresPerPixel = 0.005f;

        internal float X { get; private set; }
        internal float Y { get; private set; }
        internal float Z { get; private set; }
        internal float Yaw { get; private set; }
        internal float Pitch { get; private set; }

        internal void Reset()
        {
            X = Y = Z = Yaw = Pitch = 0f;
        }

        internal void Update(int horizontal, int forward, float deltaTime, float mouseX, float mouseY,
            bool rotate, bool vertical, bool reset)
        {
            if (reset)
            {
                Reset();
                return;
            }

            if (rotate)
            {
                Yaw = (Yaw + mouseX * DegreesPerPixel) % 360f;
                Pitch = Math.Clamp(Pitch - mouseY * DegreesPerPixel, -89f - DefaultPitch, 89f - DefaultPitch);
            }
            if (vertical) Y -= mouseY * MetresPerPixel;

            float length = MathF.Sqrt(horizontal * horizontal + forward * forward);
            if (length == 0f) return;
            float distance = MoveSpeed * Math.Max(0f, deltaTime) / length;
            float radians = Yaw * (MathF.PI / 180f);
            float sin = MathF.Sin(radians);
            float cos = MathF.Cos(radians);
            X += (horizontal * cos + forward * sin) * distance;
            Z += (forward * cos - horizontal * sin) * distance;
        }
    }
}
