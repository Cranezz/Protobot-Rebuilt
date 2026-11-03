using UnityEngine;

namespace Protobot.Audio
{
    /// <summary>
    /// The game's sound effects, synthesised once at runtime (no audio files to ship), and two ways to play
    /// them: <see cref="Play"/> for sounds "in your hands" (tools, UI) and <see cref="PlayAt"/> for sounds out
    /// in the room (a part landing on the bench). Every action should make some noise, errors included.
    /// </summary>
    public static class Sounds
    {
        const int Rate = 44100;

        static AudioClip air, motorLoop, saw, drill, spray, clack, tick, error, pop, pickup, release, metalHit, plasticHit, trash, freeze;

        // ---------------------------------------------------------------- tools

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

        /// <summary>A small metal piece knocking against the bench (the saw's offcut).</summary>
        public static AudioClip Clack => clack ??= Make("Clack", 0.18f, (t, noise) =>
        {
            var decay = Mathf.Exp(-t * 38f);
            var ring = Mathf.Sin(t * Mathf.PI * 2f * 1350f) * 0.35f + Mathf.Sin(t * Mathf.PI * 2f * 2470f) * 0.2f;
            return (ring + noise.White() * 0.3f) * decay;
        });

        // ---------------------------------------------------------------- moving parts

        /// <summary>A pneumatic cylinder firing: a sharp blast of air through the valve, fading as the stroke ends.</summary>
        public static AudioClip Air => air ??= Make("Air", 0.35f, (t, noise) =>
        {
            var envelope = Mathf.Clamp01(t / 0.008f) * Mathf.Exp(-t * 9f);
            return (noise.High() * 0.9f + noise.Band(0.6f, 0.2f) * 0.4f) * envelope;
        });

        /// <summary>
        /// A V5 motor running, one second that loops seamlessly: the electric hum, the whine of the motor and its
        /// first gear stage (the motor inside turns at 3600 rpm whatever the cartridge), a little bearing noise.
        /// Played looped with the pitch following the motor's speed.
        /// </summary>
        public static AudioClip MotorLoop => motorLoop ??= MakeLoop("Motor", 1f, (t, noise) =>
        {
            const float Tau = Mathf.PI * 2f;
            var hum = Mathf.Sin(Tau * 120f * t) * 0.1f;
            var whine = Mathf.Sin(Tau * 480f * t) * 0.16f + Mathf.Sin(Tau * 960f * t) * 0.08f + Mathf.Sin(Tau * 1440f * t) * 0.04f;
            var gears = (Mathf.Repeat(720f * t, 1f) * 2f - 1f) * 0.06f;
            var flutter = 0.85f + 0.15f * Mathf.Sin(Tau * 60f * t);
            return (hum + whine * flutter + gears + noise.Band(0.4f, 0.1f) * 0.12f) * 0.8f;
        });

        // ---------------------------------------------------------------- interface

        /// <summary>A small click for settings changing (angle steps, flipping a side, a menu button).</summary>
        public static AudioClip Tick => tick ??= Make("Tick", 0.035f, (t, noise) =>
            (Mathf.Sin(t * Mathf.PI * 2f * 2300f) * 0.5f + noise.White() * 0.15f) * Mathf.Exp(-t * 160f));

        /// <summary>Two falling notes: "can't do that".</summary>
        public static AudioClip Error => error ??= Make("Error", 0.3f, (t, noise) =>
        {
            var frequency = t < 0.13f ? 392f : 294f;
            var local = t < 0.13f ? t : t - 0.15f;
            if (local < 0f)
                return 0f;
            var envelope = Mathf.Clamp01(local / 0.01f) * Mathf.Exp(-local * 14f);
            var wave = Mathf.Sin(t * Mathf.PI * 2f * frequency) + 0.35f * Mathf.Sin(t * Mathf.PI * 2f * frequency * 2f);
            return wave * envelope * 0.3f;
        });

        /// <summary>A bright rising "bloop": something appeared (a part placed on the bench).</summary>
        public static AudioClip Pop => pop ??= Make("Pop", 0.14f, (t, noise) =>
        {
            var phase = 600f * t + 1800f * t * t;  // 600 Hz rising to ~1100 Hz
            return Mathf.Sin(phase * Mathf.PI * 2f) * Mathf.Exp(-t * 26f) * 0.45f;
        });

        /// <summary>Picking something up: a soft rising swish.</summary>
        public static AudioClip Pickup => pickup ??= Make("Pickup", 0.1f, (t, noise) =>
            (noise.Band(0.4f, 0.1f) * 0.35f + Mathf.Sin(t * Mathf.PI * 2f * (700f + 3000f * t)) * 0.15f) * Mathf.Sin(t / 0.1f * Mathf.PI));

        /// <summary>Letting go: a soft falling swish.</summary>
        public static AudioClip Release => release ??= Make("Release", 0.1f, (t, noise) =>
            (noise.Band(0.3f, 0.08f) * 0.3f + Mathf.Sin(t * Mathf.PI * 2f * (1000f - 2500f * t)) * 0.12f) * Mathf.Sin(t / 0.1f * Mathf.PI));

        /// <summary>Holding a part still in mid air: a short rising chime with a click.</summary>
        public static AudioClip Freeze => freeze ??= Make("Freeze", 0.2f, (t, noise) =>
        {
            var chime = Mathf.Sin(t * Mathf.PI * 2f * (1400f + 2200f * t)) * Mathf.Exp(-t * 12f) * 0.3f;
            var click = t < 0.01f ? noise.White() * 0.4f : 0f;
            return chime + click;
        });

        // ---------------------------------------------------------------- the room

        /// <summary>Metal hitting something: a short inharmonic ring.</summary>
        public static AudioClip MetalHit => metalHit ??= Make("Metal hit", 0.3f, (t, noise) =>
        {
            var ring = Mathf.Sin(t * Mathf.PI * 2f * 1460f) * Mathf.Exp(-t * 22f) * 0.3f
                       + Mathf.Sin(t * Mathf.PI * 2f * 2390f) * Mathf.Exp(-t * 30f) * 0.22f
                       + Mathf.Sin(t * Mathf.PI * 2f * 3730f) * Mathf.Exp(-t * 45f) * 0.15f;
            return ring + noise.White() * Mathf.Exp(-t * 120f) * 0.4f;
        });

        /// <summary>Plastic or rubber hitting something: a dull knock.</summary>
        public static AudioClip PlasticHit => plasticHit ??= Make("Plastic hit", 0.09f, (t, noise) =>
            (Mathf.Sin(t * Mathf.PI * 2f * 520f) * 0.4f + noise.Band(0.25f, 0.05f) * 0.5f) * Mathf.Exp(-t * 55f));

        /// <summary>A part landing in the trash can: a clatter of knocks.</summary>
        public static AudioClip Trash => trash ??= Make("Trash", 0.5f, (t, noise) =>
        {
            var sum = 0f;
            foreach (var (at, pitch) in new[] { (0f, 1f), (0.07f, 1.3f), (0.15f, 0.85f), (0.26f, 1.15f) })
            {
                var local = t - at;
                if (local < 0f)
                    continue;
                sum += Mathf.Sin(local * Mathf.PI * 2f * 900f * pitch) * Mathf.Exp(-local * 30f) * 0.25f;
            }
            return sum + noise.Band(0.3f, 0.05f) * Mathf.Exp(-t * 8f) * 0.25f;
        });

        // ---------------------------------------------------------------- playing

        static AudioSource handSource;
        static AudioSource[] roomSources;
        static int nextRoomSource;

        /// <summary>Plays a sound right at the player's ears (not positioned), with a little pitch variety.</summary>
        public static void Play(AudioClip clip, float volume = 0.6f)
        {
            if (clip == null)
                return;
            if (handSource == null)
            {
                handSource = Host().AddComponent<AudioSource>();
                handSource.playOnAwake = false;
                handSource.spatialBlend = 0f;
            }
            handSource.pitch = Random.Range(0.95f, 1.05f);
            handSource.PlayOneShot(clip, volume);
        }

        /// <summary>Plays a sound from a point in the room (quieter further away).</summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 0.6f, float pitch = 1f)
        {
            if (clip == null)
                return;
            if (roomSources == null || roomSources[0] == null)
            {
                var host = Host();
                roomSources = new AudioSource[10];
                for (var i = 0; i < roomSources.Length; i++)
                {
                    var go = new GameObject("Room Sound " + i);
                    go.transform.SetParent(host.transform, false);
                    var s = go.AddComponent<AudioSource>();
                    s.playOnAwake = false;
                    s.spatialBlend = 1f;
                    s.rolloffMode = AudioRolloffMode.Logarithmic;
                    s.minDistance = 0.4f;
                    s.maxDistance = 20f;
                    s.dopplerLevel = 0f;
                    roomSources[i] = s;
                }
            }
            var source = roomSources[nextRoomSource];
            nextRoomSource = (nextRoomSource + 1) % roomSources.Length;
            source.transform.position = position;
            source.pitch = pitch * Random.Range(0.93f, 1.07f);
            source.volume = volume;
            source.clip = clip;
            source.Play();
        }

        static GameObject host;

        static GameObject Host()
        {
            if (host == null)
            {
                host = new GameObject("Sounds");
                Object.DontDestroyOnLoad(host);
            }
            return host;
        }

        // ---------------------------------------------------------------- synthesis

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

        // A clip whose end runs into its start: made a little long, the extra faded over the beginning.
        static AudioClip MakeLoop(string name, float seconds, System.Func<float, Noise, float> sample)
        {
            var count = Mathf.CeilToInt(seconds * Rate);
            var fade = Rate / 10;
            var data = new float[count + fade];
            var noise = new Noise();
            for (var i = 0; i < data.Length; i++)
                data[i] = sample(i / (float)Rate, noise);
            var loop = new float[count];
            for (var i = 0; i < count; i++)
            {
                var value = data[i];
                if (i < fade)
                {
                    var k = i / (float)fade;
                    value = value * k + data[count + i] * (1f - k);
                }
                loop[i] = Mathf.Clamp(value, -1f, 1f);
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(loop, 0);
            return clip;
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
                var edge = Mathf.Clamp01(t / 0.003f) * Mathf.Clamp01((seconds - t) / 0.004f);
                data[i] = Mathf.Clamp(sample(t, noise) * edge, -1f, 1f);
            }
            var clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
