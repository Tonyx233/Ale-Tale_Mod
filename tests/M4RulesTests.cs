using System;
using TonyMods;

internal static class M4RulesTests
{
    private static int count;
    private static void Check(bool condition, string name)
    { if (!condition) throw new Exception(name); count++; }
    private static bool Near(double a, double b, double eps) { return Math.Abs(a - b) <= eps; }

    public static void Main()
    {
        // Fire rate: 750 RPM = one shot per 0.08 s = 13 shots inside one second (t = 0 .. 0.96).
        Check(Near(M4Rules.ShotInterval(750), .08, 1e-6), "750 RPM interval");
        Check(Near(M4Rules.ShotInterval(10), 1, 1e-6) && Near(M4Rules.ShotInterval(99999), .05, 1e-6), "RPM clamps to 60..1200");
        int shots = 0; for (double t = 0; t <= 1.0001; t += M4Rules.ShotInterval(750)) shots++;
        Check(shots == 13, "13 shots in the first second at 750 RPM");

        // Reload moves the real count; native Reload removed the rounds but loaded one.
        Check(M4Rules.ReloadCount(1000, 0, 30) == 30, "Empty magazine takes 30");
        Check(M4Rules.ReloadCount(1000, 27, 30) == 3, "Tactical reload tops up");
        Check(M4Rules.ReloadCount(12, 0, 30) == 12, "Short on rounds loads what is owned");
        Check(M4Rules.ReloadCount(0, 0, 30) == 0 && M4Rules.ReloadCount(50, 30, 30) == 0, "Nothing to load");
        Check(M4Rules.ReloadCount(5, -3, 30) == 5, "Corrupt negative clip never over-draws");
        Check(M4Rules.ReloadCount(uint.MaxValue, 0, 30) == 30, "Huge stacks stay bounded");

        // RPC batching: a full-auto magazine sends at most ceil(30/6)=5 charge flushes.
        int flushes = 0, pending = 0;
        for (int clip = 29; clip >= 0; clip--) { pending++; if (M4Rules.ShouldFlush(pending, 0, clip)) { flushes++; pending = 0; } }
        Check(flushes == 5 && pending == 0, "Magazine batches into 5 flushes with nothing left over");
        Check(!M4Rules.ShouldFlush(0, 9, 0), "No empty flush");
        Check(M4Rules.ShouldFlush(1, M4Rules.FlushSeconds, 20), "Single semi-auto shot flushes after the time window");
        // Per second at 12.5 shots/s: flushes(charge) + flushes(durability) <= 4.2 RPC list updates.
        Check(12.5 / M4Rules.FlushShots * 2 <= 4.2, "Inventory list updates stay near 4 per second");

        // Spread bloom: bounded, grows with heat, scoped is tighter, cools after a pause.
        Check(Near(M4Rules.Spread(0, false), M4Rules.BaseSpread, 1e-6), "First shot uses base spread");
        Check(M4Rules.Spread(10, false) > M4Rules.Spread(2, false), "Spread grows while firing");
        Check(Near(M4Rules.Spread(1000, false), M4Rules.MaxSpread, 1e-6), "Spread is capped");
        Check(M4Rules.Spread(5, true) < M4Rules.Spread(5, false) * .5f, "Scope tightens spread");
        Check(M4Rules.CoolHeat(10, .1f, .05f) == 10, "Heat holds between automatic shots");
        Check(M4Rules.CoolHeat(10, .1f, .5f) < 10 && M4Rules.CoolHeat(1, 5, 1) == 0, "Heat decays to zero");

        Check(Near(M4Rules.Recoil(false, 1), .4, 1e-6) && M4Rules.Recoil(true, 1) < M4Rules.Recoil(false, 1), "Recoil per shot, scoped lighter");
        Check(M4Rules.Recoil(false, -2) == 0, "Negative scale disables recoil");
        Check(Near(M4Rules.ReloadSeconds(true, 2.2f), 2.6, 1e-5) && Near(M4Rules.ReloadSeconds(false, 2.2f), 2.2, 1e-5), "Empty reload adds charging handle");

        // Proposal balance table (wolf 120, OrcMelee 200, SpiderBoss 600).
        Check(Near(M4Rules.TimeToKill(120, 9, .08f, 30, 2.2f), 1.04, .005), "Recommended vs wolf");
        Check(Near(M4Rules.TimeToKill(200, 9, .08f, 30, 2.2f), 1.76, .005), "Recommended vs OrcMelee");
        Check(Near(M4Rules.TimeToKill(600, 9, .08f, 30, 2.2f), 9.68, .005), "Recommended vs SpiderBoss");
        Check(Near(M4Rules.TimeToKill(200, 6, .1f, 30, 2.2f), 5.5, .005), "Conservative vs OrcMelee");
        Check(Near(M4Rules.TimeToKill(120, 35, 1.5f, 1, 0), 4.5, .005), "Musket vs wolf");

        // Scope stages: M4 single stage 4x, musket keeps 3x -> 6x -> off.
        ScopeCycle acog = new ScopeCycle(4);
        acog.Advance(); Check(acog.IsActive && acog.Magnification == 4 && acog.IsLastStage, "ACOG opens at 4x");
        acog.Advance(); Check(!acog.IsActive && acog.Magnification == 1, "Second press closes ACOG");
        ScopeCycle musket = new ScopeCycle();
        musket.Advance(); Check(musket.Magnification == 3 && !musket.IsLastStage, "Musket first stage");
        musket.Advance(); Check(musket.Magnification == 6 && musket.IsLastStage, "Musket last stage");
        bool threw = false; try { new ScopeCycle(new int[0]); } catch (ArgumentException) { threw = true; }
        Check(threw, "Scope needs a stage");

        // Synthesized audio: valid RIFF/PCM16, peaks normalized, no NaN, deterministic per variant.
        foreach (M4Sound.Kind kind in Enum.GetValues(typeof(M4Sound.Kind)))
        {
            int variants = kind == M4Sound.Kind.Shot ? M4Sound.ShotVariants : 1;
            for (int v = 0; v < variants; v++)
            {
                float[] samples = M4Sound.Synth(kind, v);
                float peak = 0; bool finite = true;
                foreach (float s in samples) { peak = Math.Max(peak, Math.Abs(s)); finite &= !float.IsNaN(s) && !float.IsInfinity(s); }
                Check(finite && peak > .3f && peak <= .93f, kind + " level " + peak);
                Check(Math.Abs(samples[samples.Length - 1]) < 1e-3, kind + " fades to silence");
                byte[] wav = M4Sound.Wav(samples);
                Check(wav.Length == 44 + samples.Length * 2 && wav[0] == 'R' && wav[8] == 'W' && BitConverter.ToInt32(wav, 24) == M4Sound.Rate &&
                    BitConverter.ToInt16(wav, 34) == 16 && BitConverter.ToInt32(wav, 40) == samples.Length * 2, kind + " WAV header");
                float[] again = M4Sound.Synth(kind, v);
                Check(again.Length == samples.Length && again[again.Length / 3] == samples[samples.Length / 3], kind + " deterministic");
            }
        }
        float[] shot = M4Sound.Synth(M4Sound.Kind.Shot, 0);
        Check(shot.Length / (double)M4Sound.Rate < .6 && shot.Length / (double)M4Sound.Rate > .4, "Shot length 0.4-0.6 s");
        double early = 0, late = 0;
        for (int i = 0; i < M4Sound.Rate / 50; i++) early += shot[i] * shot[i];
        for (int i = M4Sound.Rate / 4; i < M4Sound.Rate / 4 + M4Sound.Rate / 50; i++) late += shot[i] * shot[i];
        Check(early > late * 20, "Shot energy is front-loaded (transient, then tail)");
        Check(M4Sound.Synth(M4Sound.Kind.Shot, 1)[400] != shot[400], "Shot variants differ");
        Check(M4Sound.Valid(5) && !M4Sound.Valid(6), "Network sound kinds validated");
        Console.WriteLine("PASS: " + count + " M4 rule, scope stage and audio checks");
    }
}
