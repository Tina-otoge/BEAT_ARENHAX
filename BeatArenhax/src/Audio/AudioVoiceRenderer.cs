using System;
using System.Collections.Generic;

namespace BeatArenhax.Audio
{
    internal sealed class AudioVoiceRenderer(int sampleRate)
    {
        private sealed class Voice
        {
            public AudioBuffer Buffer;
            public int Position;
            public long Delay;
            public float GainLeft;
            public float GainRight;
            public int PolyphonyKey;
            public bool IsSong;
            public bool Loop;
            public bool IsPreview;
            public float Fade = 1f;
            public float FadeStep;
            public long Serial;
        }

        internal const int PolyphonyPerSound = 4;
        private readonly int _sampleRate = sampleRate;
        private readonly List<Voice> _voices = [];
        private long _serial;

        public void RemoveSongVoices() => _ = _voices.RemoveAll(voice => voice.IsSong);

        public void FadeSongVoices(float durationMs)
        {
            float fadeStep = -1f / (_sampleRate * durationMs / 1000f);
            foreach (Voice voice in _voices)
            {
                if (!voice.IsSong) continue;
                voice.FadeStep = fadeStep;
            }
        }

        public void FadePreviewVoices(float durationMs)
        {
            float fadeStep = -1f / (_sampleRate * durationMs / 1000f);
            foreach (Voice voice in _voices)
            {
                if (!voice.IsPreview) continue;
                voice.FadeStep = fadeStep;
            }
        }

        public void Add(
            AudioBuffer buffer,
            float gainLeft,
            float gainRight,
            int polyphonyKey,
            bool isSong = false,
            bool loop = false,
            bool isPreview = false,
            int position = 0,
            long delay = 0,
            float fade = 1f,
            float fadeStep = 0)
        {
            Voice voice = new()
            {
                Buffer = buffer,
                Position = position,
                Delay = delay,
                GainLeft = gainLeft,
                GainRight = gainRight,
                PolyphonyKey = polyphonyKey,
                IsSong = isSong,
                Loop = loop,
                IsPreview = isPreview,
                Fade = fade,
                FadeStep = fadeStep,
                Serial = ++_serial,
            };

            if (voice.PolyphonyKey > 0)
            {
                int count = 0;
                Voice oldest = null;
                foreach (Voice existing in _voices)
                {
                    if (existing.PolyphonyKey != voice.PolyphonyKey || existing.FadeStep < 0) continue;
                    count++;
                    if (oldest == null || existing.Serial < oldest.Serial) oldest = existing;
                }
                if (count >= PolyphonyPerSound && oldest != null)
                    oldest.FadeStep = -1f / (_sampleRate * 0.005f);
            }
            _voices.Add(voice);
        }

        public void Render(float[] mix, int frames, bool songRunning, float songVolume, float oneShotVolume)
        {
            for (int voiceIndex = _voices.Count - 1; voiceIndex >= 0; voiceIndex--)
            {
                Voice voice = _voices[voiceIndex];
                if (voice.IsSong && !songRunning && voice.FadeStep >= 0) continue;
                float gainLeft = voice.GainLeft;
                float gainRight = voice.GainRight;
                if (voice.IsSong)
                {
                    gainLeft *= songVolume;
                    gainRight *= songVolume;
                }
                else if (!voice.IsPreview)
                {
                    gainLeft *= oneShotVolume;
                    gainRight *= oneShotVolume;
                }

                float[] data = voice.Buffer.Data;
                int frame = 0;
                if (voice.Delay > 0)
                {
                    long skippedFrames = Math.Min(voice.Delay, frames);
                    voice.Delay -= skippedFrames;
                    frame = (int)skippedFrames;
                }
                bool finished = false;
                for (; frame < frames; frame++)
                {
                    if (voice.Position >= voice.Buffer.Frames)
                    {
                        if (voice.Loop) voice.Position = 0;
                        else
                        {
                            finished = true;
                            break;
                        }
                    }
                    if (voice.FadeStep != 0)
                    {
                        voice.Fade += voice.FadeStep;
                        if (voice.Fade >= 1f)
                        {
                            voice.Fade = 1f;
                            voice.FadeStep = 0;
                        }
                        else if (voice.Fade <= 0f)
                        {
                            voice.Fade = 0;
                            finished = true;
                            break;
                        }
                    }
                    int sampleIndex = voice.Position * 2;
                    mix[frame * 2] += data[sampleIndex] * gainLeft * voice.Fade;
                    mix[(frame * 2) + 1] += data[sampleIndex + 1] * gainRight * voice.Fade;
                    voice.Position++;
                }
                if (!finished) continue;
                _voices.RemoveAt(voiceIndex);
            }
        }
    }
}
