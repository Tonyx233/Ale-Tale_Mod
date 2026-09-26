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

        // Host re-entrancy (0.14.0 crash): every "RPC" synchronously re-enters Flush, as
        // RemoveItemCharge/DamageTool -> OnItemsChanged -> CheckReload -> Reload -> Flush does on the host.
        M4Batch batch = new M4Batch();
        int sentCharge = 0, sentWear = 0, depth = 0, maxDepth = 0, rpcs = 0;
        Action flush = null;
        flush = delegate
        {
            int c, w;
            if (!batch.Take(out c, out w)) return;
            depth++; maxDepth = Math.Max(maxDepth, depth);
            sentCharge += c; rpcs++; flush();
            sentWear += w; rpcs++; flush();
            depth--;
        };
        for (int clip = 29; clip >= 0; clip--)
        {
            batch.Add(30 - clip);
            if (M4Rules.ShouldFlush(batch.Charge, 0, clip)) flush();
        }
        Check(sentCharge == 30 && sentWear == 30, "Re-entrant host flush sends each shot exactly once");
        Check(maxDepth == 1 && rpcs == 10, "Re-entrant host flush does not recurse (5 flushes x 2 RPCs)");
        int dummyC, dummyW;
        Check(!batch.Take(out dummyC, out dummyW) && batch.Charge == 0, "Nothing left after the magazine");
        batch.Add(5); batch.Add(5.2f);
        Check(batch.Since == 5 && batch.Charge == 2, "Batch age starts at the oldest pending shot");

        // Spread bloom: bounded, grows with heat, scoped is tighter, cools after a pause.
        Check(Near(M4Rules.Spread(0, 1), M4Rules.BaseSpread, 1e-6), "First shot uses base spread");
        Check(M4Rules.Spread(10, 1) > M4Rules.Spread(2, 1), "Spread grows while firing");
        Check(Near(M4Rules.Spread(1000, 1), M4Rules.MaxSpread, 1e-6), "Spread is capped");
        Check(Near(M4Rules.Spread(5, .35f), M4Rules.Spread(5, 1) * .35f, 1e-6) && M4Rules.Spread(5, 3) == M4Rules.Spread(5, 1), "Aim factor scales spread and is clamped");
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
        bool threw = false; try { new ScopeCycle(new float[0]); } catch (ArgumentException) { threw = true; }
        Check(threw, "Scope needs a stage");

        // Interchangeable optics: metaInt low byte, 0 = factory ACOG so 0.14.x rifles are unchanged.
        Check(M4Scopes.Effective(0) == M4ScopeKind.Acog && M4Scopes.Effective(7) == M4ScopeKind.Acog && M4Scopes.Effective(255) == M4ScopeKind.Acog, "Default / unknown optic is the ACOG");
        for (int k = 1; k <= M4Scopes.Count; k++)
        {
            int meta = M4Scopes.WithKind(0x12345600, (M4ScopeKind)k);
            Check(M4Scopes.Effective(meta) == (M4ScopeKind)k && (meta & ~0xFF) == 0x12345600, "Optic byte round-trips and keeps other bits " + k);
        }
        Check(M4Scopes.ItemKinds.Length == 5 && !M4Scopes.HasItem(M4ScopeKind.Iron) && !M4Scopes.HasItem(M4ScopeKind.Default), "Five optic items; iron sights are not an item");
        foreach (M4ScopeKind kind in M4Scopes.ItemKinds)
        {
            M4ScopeKind back;
            ushort id = M4Scopes.ItemId(kind);
            Check(id >= 47932 && id <= 47936 && M4Scopes.FromItem(id, out back) && back == kind, "Optic item id maps both ways " + kind);
        }
        M4ScopeKind none;
        Check(!M4Scopes.FromItem(47931, out none) && !M4Scopes.FromItem(47937, out none) && !M4Scopes.FromItem(290, out none), "Ammo / neighbours are not optics");
        M4ScopeSwap swap = M4Scopes.PlanAttach(0, 1, M4ScopeKind.RedDot);
        Check(swap.Apply && swap.Returned == M4ScopeKind.Acog && M4Scopes.Effective(swap.NewMeta) == M4ScopeKind.RedDot && !swap.Split, "Red dot on a factory rifle returns the ACOG");
        swap = M4Scopes.PlanAttach(M4Scopes.WithKind(0, M4ScopeKind.Iron), 1, M4ScopeKind.Sniper);
        Check(swap.Apply && swap.Returned == M4ScopeKind.Iron && !M4Scopes.HasItem(swap.Returned), "Optic on iron sights returns nothing");
        Check(!M4Scopes.PlanAttach(0, 1, M4ScopeKind.Acog).Apply && !M4Scopes.PlanAttach(0, 1, M4ScopeKind.Iron).Apply && !M4Scopes.PlanAttach(0, 0, M4ScopeKind.Holo).Apply, "Same optic, iron or empty stack is refused");
        Check(M4Scopes.PlanAttach(0, 3, M4ScopeKind.Holo).Split, "Stacked rifles are split so one gets the optic");
        swap = M4Scopes.PlanDetach(M4Scopes.WithKind(0, M4ScopeKind.Brass), 1);
        Check(swap.Apply && swap.Returned == M4ScopeKind.Brass && M4Scopes.Effective(swap.NewMeta) == M4ScopeKind.Iron, "Detach returns the optic and leaves iron sights");
        Check(M4Scopes.PlanDetach(0, 1).Returned == M4ScopeKind.Acog && !M4Scopes.PlanDetach(M4Scopes.WithKind(0, M4ScopeKind.Iron), 1).Apply, "Detach factory ACOG; nothing to detach from irons");
        for (int k = 1; k <= M4Scopes.Count; k++)
        {
            M4ScopeProfile p = M4Scopes.Profile((M4ScopeKind)k);
            Check(p.Kind == (M4ScopeKind)k && p.Stages.Length > 0 && p.SpreadFactor > 0 && p.SpreadFactor < 1 && p.Role != null, "Profile complete " + k);
            for (int i = 1; i < p.Stages.Length; i++) Check(p.Stages[i] > p.Stages[i - 1], "Stages ascend " + k);
            Check(p.Magnified == (p.Stages[0] >= 3), "Magnified optics start at 3x or more; 1x sights align " + k);
            Check(p.Magnified || (p.EyeDistance > .03f && p.EyeDistance < .2f && p.SightY > .08f && p.SightY < .1f), "Aligned sights have an eye point " + k);
            Check(p.FoldedIrons == (p.Kind != M4ScopeKind.Iron && p.Kind != M4ScopeKind.Sniper), "Folded BUIS except irons and the sniper mount " + k);
        }
        Check(M4Scopes.Stages(M4ScopeKind.Acog, 6)[0] == 6 && M4Scopes.Stages(M4ScopeKind.Sniper, 6).Length == 3, "ACOG power from config; sniper 3/6/9");
        Check(M4Scopes.Profile(M4ScopeKind.Sniper).SpreadFactor < M4Scopes.Profile(M4ScopeKind.RedDot).SpreadFactor, "Higher power aims tighter");
        float ring = M4Scopes.MoaToPixels(M4Scopes.HoloRingMoa, 70 / 1.5f, 1080);
        Check(ring > 20 && ring < 30, "65 MOA holo ring is ~24 px at 1080p / 1.5x: " + ring);
        ScopeCycle close = new ScopeCycle(1.3f);
        close.Advance(); Check(close.IsActive && Math.Abs(close.Magnification - 1.3f) < 1e-6 && close.IsLastStage, "1.3x iron-sight stage");

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
