using System;

namespace TonyMods
{
    // Unity-free rules shared by the rifle and tests/M4RulesTests.cs.
    internal static class M4Rules
    {
        public const int MinRpm = 60, MaxRpm = 1200;
        // Per-shot inventory RPCs are merged: at most one flush per FlushShots rounds or FlushSeconds.
        public const int FlushShots = 6;
        public const float FlushSeconds = .5f;
        // Half-angle of the shot cone in degrees, the same in every direction (M4Rifle.Shoot). The native
        // RaycastShot scales a viewport offset by spreadAngle / FOV, which on 16:9 spread 1.78x wider sideways.
        public const float BaseSpread = .35f, SpreadPerShot = .07f, MaxSpread = 1.5f;
        public const float HeatHold = .12f, HeatDecay = 7f;
        public const float EmptyExtraReload = .4f;
        // Native RaycastShot range. A thin ray that finds no hitbox is swept again as a sphere of AssistRadius
        // metres: the native capsule hitboxes leave tails, snouts, limbs and spider legs uncovered.
        public const float Range = 1000, AssistRadius = .2f;
        // BepInEx keeps old defaults in existing cfg files; [M4] DefaultsRevision records the migrations run.
        public const int DefaultDamage = 10, OldDefaultDamage = 9, DefaultsRevision = 1;

        public static float ShotInterval(int rpm)
        {
            return 60f / Math.Max(MinRpm, Math.Min(MaxRpm, rpm));
        }

        // Rounds moved from inventory into the magazine; native Reload loads only one.
        public static int ReloadCount(uint owned, int clip, int capacity)
        {
            if (capacity <= 0 || clip >= capacity || owned == 0) return 0;
            return (int)Math.Min(owned, (uint)(capacity - Math.Max(0, clip)));
        }

        public static bool ShouldFlush(int pending, float oldestAge, int clip)
        {
            return pending > 0 && (pending >= FlushShots || oldestAge >= FlushSeconds || clip <= 0);
        }

        // factor: 1 from the hip, M4ScopeProfile.SpreadFactor while aiming.
        public static float Spread(float heat, float factor)
        {
            return Math.Min(MaxSpread, BaseSpread + Math.Max(0, heat) * SpreadPerShot) * Math.Max(0, Math.Min(1, factor));
        }

        public static float CoolHeat(float heat, float dt, float sinceShot)
        {
            return sinceShot < HeatHold ? heat : Math.Max(0, heat - dt * HeatDecay);
        }

        // Tangent-plane offset (x right, y up, forward = 1) for a uniform point (u, v) in the unit disc,
        // so the shot lands uniformly inside a round cone of half-angle spreadDegrees.
        public static void ConeOffset(float spreadDegrees, float u, float v, out float x, out float y)
        {
            double t = Math.Tan(Math.Max(0, Math.Min(45, spreadDegrees)) * Math.PI / 180);
            x = (float)(u * t); y = (float)(v * t);
        }

        // Revision 1: the damage default went from 9 to 10. A value the player changed is kept.
        public static int MigrateDamage(int revision, int damage)
        {
            return revision < 1 && damage == OldDefaultDamage ? DefaultDamage : damage;
        }

        // Upward camera kick in degrees per shot, before the +/-15% random variation.
        public static float Recoil(bool scoped, float scale)
        {
            return (scoped ? .3f : .4f) * Math.Max(0, scale);
        }

        public static float ReloadSeconds(bool empty, float tactical)
        {
            return empty ? tactical + EmptyExtraReload : tactical;
        }

        // First shot at t = 0; a magazine swap after every full magazine. Matches the proposal table.
        public static double TimeToKill(int hp, int damage, float interval, int magazine, float reload)
        {
            if (hp <= 0) return 0;
            int shots = (hp + damage - 1) / damage;
            int intervals = shots - 1;
            return intervals * (double)interval + (intervals / magazine) * (double)reload;
        }
    }

    // Shots waiting for the merged charge/durability RPCs. Take() clears the counts before the
    // caller sends anything: on the host a ServerRpc runs synchronously, the inventory change fires
    // GunTool.OnItemsChanged -> CheckReload -> Reload, and that nested call must find nothing to resend.
    internal sealed class M4Batch
    {
        public int Charge { get; private set; }
        public int Wear { get; private set; }
        public float Since { get; private set; }
        public void Add(float now)
        {
            if (Charge == 0 && Wear == 0) Since = now;
            Charge++; Wear++;
        }
        public bool Take(out int charge, out int wear)
        {
            charge = Charge; wear = Wear; Charge = 0; Wear = 0;
            return charge > 0 || wear > 0;
        }
    }
}
