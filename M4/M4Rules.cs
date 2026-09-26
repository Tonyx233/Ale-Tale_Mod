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
        // Native spread is spreadAngle / FOV in viewport units, roughly degrees of radius.
        public const float BaseSpread = .9f, SpreadPerShot = .14f, MaxSpread = 3.2f, ScopedSpread = .35f;
        public const float HeatHold = .12f, HeatDecay = 7f;
        public const float EmptyExtraReload = .4f;

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

        public static float Spread(float heat, bool scoped)
        {
            float spread = Math.Min(MaxSpread, BaseSpread + Math.Max(0, heat) * SpreadPerShot);
            return scoped ? spread * ScopedSpread : spread;
        }

        public static float CoolHeat(float heat, float dt, float sinceShot)
        {
            return sinceShot < HeatHold ? heat : Math.Max(0, heat - dt * HeatDecay);
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
