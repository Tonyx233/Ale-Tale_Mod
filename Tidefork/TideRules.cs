using System;

namespace TonyMods
{
    // Pure combat geometry/timing shared by host, visuals and offline tests.
    internal static class TideRules
    {
        internal const ushort ItemId = 47940;
        internal const int Price = 1, Limit = 8, Health = 180;
        internal const float Lifetime = 120, Flight = .65f, Rise = 1.5f, Radius = .48f;
        internal const byte Summon = 0, Walk = 1, Bite = 2, Sweep = 3, Wave = 4, Death = 5;
        internal static float Windup(byte action) { return action == Bite ? .7f : action == Sweep ? 1 : action == Wave ? 1.6f : 0; }
        internal static float Strike(byte action) { return action == Bite ? .2f : action == Sweep ? .35f : action == Wave ? .4f : 0; }
        internal static float Duration(byte action)
        { return action == Summon ? Flight + Rise : action == Bite ? 1.7f : action == Sweep ? 2.35f : action == Wave ? 3.4f : action == Death ? 1.4f : 0; }
        internal static short Damage(byte action) { return (short)(action == Bite ? 12 : action == Sweep ? 16 : 20); }
        // PlayerNet.HitClientRpc drops the hit when the owner's 3D distance exceeds this, so it must cover
        // every InHit point (box corners and the 1.8 m height allowance) plus a little movement latency.
        internal static float Range(byte action) { return action == Bite ? 3.2f : action == Sweep ? 3.4f : 4.7f; }
        // Preferred throw distance first, then closer/farther fallbacks for cluttered interiors.
        internal static readonly float[] ThrowDistances = { 5, 4, 3, 6 };
        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static bool InHit(byte action, float side, float forward, float height)
        {
            if (!Finite(side) || !Finite(forward) || !Finite(height) || Math.Abs(height) > 1.8f) return false;
            float d2 = side * side + forward * forward;
            if (action == Bite) return forward >= -.15f && forward <= 2.3f && Math.Abs(side) <= .65f;
            // The tail swings through the front 120 degree fan shown by the visual telegraph.
            if (action == Sweep) return d2 <= 2.6f * 2.6f && forward >= 0 && forward * forward >= d2 * .25f;
            return action == Wave && d2 <= 16;
        }
        internal static bool CrossedHit(byte action, double previous, double now)
        { return action >= Bite && action <= Wave && previous < Windup(action) && now >= Windup(action); }
        internal static bool CanUse(bool server, bool alive, bool ownsContainer, ushort dataId, int amount, int count, double sinceLast)
        { return server && alive && ownsContainer && dataId == ItemId && amount > 0 && count < Limit && Finite(sinceLast) && sinceLast >= 1; }
    }
}
