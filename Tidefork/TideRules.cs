using System;

namespace TonyMods
{
    // Pure combat geometry/timing shared by host, visuals and offline tests. All balance numbers live here.
    internal static class TideRules
    {
        internal const ushort ItemId = 47940;
        // No room cap and no lifetime: idols stay until killed, a save/new game loads, or the scene changes.
        internal const int Price = 1, Health = 1000;
        internal const float Flight = .65f, Rise = 1.5f, Radius = .48f;
        // Movement (NavMeshAgent), host only.
        internal const float Speed = 3.15f, Acceleration = 10.5f, TurnSpeed = 240, StopDistance = 2.475f;
        internal const float SearchRadius = 45, SearchHeight = 6, RepathInterval = .3f;
        // Attack when the target is within EngageDistance; sweep when closer than SweepDistance, else bite.
        internal const float EngageDistance = 3.225f, SweepDistance = 2.175f;
        internal const float FirstWaveDelay = 6, WaveCooldown = 10;
        // Hit shapes in the idol's local space (metres). HitHeight keeps other floors safe.
        internal const float BiteReach = 4.6f, BiteHalfWidth = 1.3f, BiteBack = .3f, SweepRadius = 5.2f, WaveRadius = 8, HitHeight = 1.8f;
        internal const byte Summon = 0, Walk = 1, Bite = 2, Sweep = 3, Wave = 4, Death = 5;
        internal static float Windup(byte action) { return action == Bite ? .35f : action == Sweep ? .5f : action == Wave ? .8f : 0; }
        internal static float Strike(byte action) { return action == Bite ? .2f : action == Sweep ? .35f : action == Wave ? .4f : 0; }
        // Windup + strike + recovery; recovery unchanged (Bite .8, Sweep 1.0, Wave 1.4).
        internal static float Duration(byte action)
        { return action == Summon ? Flight + Rise : action == Bite ? 1.35f : action == Sweep ? 1.85f : action == Wave ? 2.6f : action == Death ? 1.4f : 0; }
        internal static short Damage(byte action) { return (short)(action == Bite ? 12 : action == Sweep ? 16 : 20); }
        // PlayerNet.HitClientRpc drops the hit when the owner's 3D distance exceeds this, so it must cover
        // every InHit point (box corners and the height allowance) plus a little movement latency.
        internal static float Range(byte action) { return action == Bite ? 5.4f : action == Sweep ? 5.8f : 8.5f; }
        // Preferred throw distance first, then closer/farther fallbacks for cluttered interiors.
        internal static readonly float[] ThrowDistances = { 5, 4, 3, 6 };
        internal static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        internal static bool InHit(byte action, float side, float forward, float height)
        {
            if (!Finite(side) || !Finite(forward) || !Finite(height) || Math.Abs(height) > HitHeight) return false;
            float d2 = side * side + forward * forward;
            if (action == Bite) return forward >= -BiteBack && forward <= BiteReach && Math.Abs(side) <= BiteHalfWidth;
            // The tail swings through the front 120 degree fan shown by the visual telegraph.
            if (action == Sweep) return d2 <= SweepRadius * SweepRadius && forward >= 0 && forward * forward >= d2 * .25f;
            return action == Wave && d2 <= WaveRadius * WaveRadius;
        }
        internal static bool CrossedHit(byte action, double previous, double now)
        { return action >= Bite && action <= Wave && previous < Windup(action) && now >= Windup(action); }
        internal static bool CanUse(bool server, bool alive, bool ownsContainer, ushort dataId, int amount, double sinceLast)
        { return server && alive && ownsContainer && dataId == ItemId && amount > 0 && Finite(sinceLast) && sinceLast >= 1; }
    }
}
