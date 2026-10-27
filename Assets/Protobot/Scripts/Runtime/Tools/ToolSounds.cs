using UnityEngine;

namespace Protobot.Tools
{
    /// <summary>
    /// The tools' sounds, synthesised once at runtime (no audio files to ship): saw strokes, the drill's motor
    /// and bite, the spray gun's hiss, and a soft clack for metal pieces coming apart.
    /// </summary>
    public static class ToolSounds
    {
        const int Rate = 44100;

        static AudioClip saw, drill, spray, clack;

        /// <summary>About half a second of sawing: six rasping strokes (matches the saw's stroke animation).</summary>
        public static AudioClip Saw => saw ??= Make("Saw", 0.47f, (t, noise) =>
        {
            // Each stroke swells and fades; the blade's teeth add a fast chatter.
            var stroke = Mathf.Abs(Mathf.Sin(t * 40f));
            var chatter = 0.6f + 0.4f * Mathf.Sin(t * Mathf.PI * 2f * 190f);
            return noise.Band(0.35f, 0.08f) * stroke * stroke * chatter * 0.9f;
        });

        /// <summary>Motor spin-up, the bit biting through, spin-down (about 0.6 s).</summary>
        public static AudioClip Drill => drill ??= Make("Drill", 0.62f, (t, noise) =>
        {
            var spinUp = Mathf.Clamp01(t / 0.07f);
            var spinDown = Mathf.Clamp01((0.62f - t) / 0.14f);
            var speed = Mathf.Min(spinUp, spinDown);
            var frequency = 180f + 260f * speed;
            var phase = t * frequency;
            var motor = (Mathf.Repeat(phase, 1f) * 2f - 1f) * 0.18f + Mathf.Sin(phase * Mathf.PI * 4f) * 0.12f;
            var bite = t > 0.12f && t < 0.5f ? noise.Band(0.5f, 0.15f) * 0.45f * (0.7f + 0.3f * Mathf.Sin(t * 90f)) : 0f;
            return (motor + bite) * speed;
        });

        /// <summary>A short burst of hiss.</summary>
        public static AudioClip Spray => spray ??= Make("Spray", 0.4f, (t, noise) =>
        {
            var envelope = Mathf.Clamp01(t / 0.02f) * Mathf.Clamp01((0.4f - t) / 0.12f);
            return noise.High() * envelope * 0.5f;
        });

        /// <summary>A small metal piece knocking against the bench.</summary>
        public static AudioClip Clack => clack ??= Make("Clack", 0.18f, (t, noise) =>
        {
            var decay = Mathf.Exp(-t * 38f);
            var ring = Mathf.Sin(t * Mathf.PI * 2f * 1350f) * 0.35f + Mathf.Sin(t * Mathf.PI * 2f * 2470f) * 0.2f;
            return (ring + noise.White() * 0.3f) * decay;
        });

        /// <summary>White noise with a couple of simple one-pole filters.</summary>
        sealed class Noise
        {
            System.Random random = new(1234);
            float low, band, previous;

            public float White() => (float)(random.NextDouble() * 2.0 - 1.0);

            /// <summary>Noise kept to a band (two one-pole low-passes, subtracted).</summary>
            public float Band(float upper, float lower)
            {
                var w = White();
                low += (w - low) * upper;
                band += (w - band) * lower;
                return (low - band) * 2f;
            }

            /// <summary>Bright, airy noise (first difference of white noise).</summary>
            public float High()
            {
                var w = White();
                var h = w - previous;
                previous = w;
                return h * 0.5f;
            }
        }

        static AudioClip Make(string name, float seconds, System.Func<float, Noise, float> sample)
        {
            var count = Mathf.CeilToInt(seconds * Rate);
            var data = new float[count];
            var noise = new Noise();
            for (var i = 0; i < count; i++)
            {
                var t = i / (float)Rate;
                // A few milliseconds of fade at each end so nothing clicks.
                var edge = Mathf.Clamp01(t / 0.004f) * Mathf.Clamp01((seconds - t) / 0.004f);
                data[i] = Mathf.Clamp(sample(t, noise) * edge, -1f, 1f);
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
