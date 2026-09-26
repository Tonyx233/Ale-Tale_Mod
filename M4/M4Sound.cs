using System;
using System.IO;

namespace TonyMods
{
    // Procedurally synthesized rifle sounds (no recorded or third-party audio). Unity-free so the
    // waveform checks in tests/M4RulesTests.cs can run offline.
    internal static class M4Sound
    {
        public const int Rate = 44100, ShotVariants = 3;
        public enum Kind : byte { Shot = 0, Empty = 1, MagOut = 2, MagIn = 3, Charge = 4, Mode = 5 }
        public static bool Valid(byte kind) { return kind <= (byte)Kind.Mode; }

        public static float[] Synth(Kind kind, int variant)
        {
            Random random = new Random(7919 * (int)kind + 104729 * variant + 17);
            float[] samples;
            switch (kind)
            {
                case Kind.Shot: samples = Shot(random, variant); break;
                case Kind.Empty: samples = Buffer(.09f); Tick(samples, random, 0, 1f, 3400); Tick(samples, random, .011f, .45f, 2900); break;
                case Kind.MagOut: samples = Buffer(.26f); Tick(samples, random, 0, .8f, 2600); Slide(samples, random, .03f, .2f, .28f); break;
                case Kind.MagIn: samples = Buffer(.26f); Slide(samples, random, 0, .12f, .25f); Clack(samples, random, .135f, 1f, 180, 1800); break;
                case Kind.Charge: samples = Buffer(.46f); Slide(samples, random, 0, .14f, .3f); Tick(samples, random, .15f, .6f, 2300); Clack(samples, random, .29f, 1f, 140, 2200); break;
                default: samples = Buffer(.05f); Tick(samples, random, 0, .7f, 2400); break;
            }
            Finish(samples, kind == Kind.Shot ? .92f : .6f);
            return samples;
        }

        private static float[] Buffer(float seconds) { return new float[(int)(Rate * seconds)]; }

        private static float[] Shot(Random random, int variant)
        {
            float[] s = Buffer(.55f);
            float tune = 1 + (variant - 1) * .06f, decay = 1 + (variant - 1) * .1f;
            double phase = 0;
            float hp = 0, last = 0, low = 0, dark = 0;
            for (int i = 0; i < s.Length; i++)
            {
                float t = i / (float)Rate, noise = (float)(random.NextDouble() * 2 - 1);
                hp = .82f * (hp + noise - last); last = noise;
                low += .25f * (noise - low);
                dark += .04f * (noise - dark);
                float crack = hp * (float)Math.Exp(-t / .0035f) * 1.1f;
                double freq = (48 + 150 * Math.Exp(-t / .018)) * tune;
                phase += 2 * Math.PI * freq / Rate;
                float attack = t < .0015f ? t / .0015f : 1;
                float boom = (float)Math.Sin(phase) * (float)Math.Exp(-t / (.055f * decay)) * .95f * attack;
                float body = low * (float)Math.Exp(-t / (.028f * decay)) * .9f;
                float tail = dark * (float)Math.Exp(-t / (.16f * decay)) * 1.4f * Math.Min(1, t / .01f);
                float clack = t >= .03f && t < .04f ? hp * .25f * (float)Math.Exp(-(t - .03f) / .002f) : 0;
                s[i] = crack + boom + body + tail + clack;
            }
            return s;
        }

        // Metallic click: a high-passed noise burst plus a short ring.
        private static void Tick(float[] s, Random random, float at, float gain, float ring)
        {
            int start = (int)(at * Rate);
            float hp = 0, last = 0;
            for (int i = start; i < s.Length && i < start + Rate / 20; i++)
            {
                float t = (i - start) / (float)Rate, noise = (float)(random.NextDouble() * 2 - 1);
                hp = .7f * (hp + noise - last); last = noise;
                s[i] += gain * (hp * (float)Math.Exp(-t / .0012f) + .35f * (float)(Math.Sin(2 * Math.PI * ring * t) * Math.Exp(-t / .008f)));
            }
        }

        // Heavier seating hit: low thump plus a click.
        private static void Clack(float[] s, Random random, float at, float gain, float thump, float ring)
        {
            int start = (int)(at * Rate);
            for (int i = start; i < s.Length && i < start + Rate / 8; i++)
            {
                float t = (i - start) / (float)Rate;
                s[i] += gain * .7f * (float)(Math.Sin(2 * Math.PI * thump * t) * Math.Exp(-t / .02f));
            }
            Tick(s, random, at, gain, ring);
        }

        // Friction noise with a raised-cosine envelope.
        private static void Slide(float[] s, Random random, float from, float to, float gain)
        {
            int a = (int)(from * Rate), b = Math.Min(s.Length, (int)(to * Rate));
            float band = 0, lowpass = 0;
            for (int i = a; i < b; i++)
            {
                float noise = (float)(random.NextDouble() * 2 - 1);
                lowpass += .35f * (noise - lowpass); band = noise - lowpass;
                float x = (i - a) / (float)Math.Max(1, b - a);
                s[i] += gain * band * .5f * (1 - (float)Math.Cos(2 * Math.PI * x));
            }
        }

        private static void Finish(float[] s, float peak)
        {
            float max = 0;
            for (int i = 0; i < s.Length; i++) { s[i] = (float)Math.Tanh(1.8 * s[i]); max = Math.Max(max, Math.Abs(s[i])); }
            float gain = max > 0 ? peak / max : 0;
            int fade = Math.Min(s.Length, Rate / 50);
            for (int i = 0; i < s.Length; i++)
            {
                float edge = i >= s.Length - fade ? (s.Length - 1 - i) / (float)fade : 1;
                s[i] *= gain * edge;
            }
        }

        public static byte[] Wav(float[] samples)
        {
            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                int bytes = samples.Length * 2;
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' }); writer.Write(36 + bytes);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E', (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(Rate); writer.Write(Rate * 2);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' }); writer.Write(bytes);
                foreach (float sample in samples) writer.Write((short)Math.Round(Math.Max(-1, Math.Min(1, sample)) * 32767));
                writer.Flush();
                return memory.ToArray();
            }
        }
    }
}
