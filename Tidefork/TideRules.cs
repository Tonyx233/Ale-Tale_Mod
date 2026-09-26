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
        internal const float Speed = 4.725f, Acceleration = 15.75f, TurnSpeed = 360, StopDistance = 3.7125f;
        internal const float SearchRadius = 30, SearchHeight = 4, RepathInterval = .15f;
        // Attack when the target is within EngageDistance; sweep when closer than SweepDistance, else bite.
        internal const float EngageDistance = 4.8375f, SweepDistance = 3.2625f;
        internal const float FirstWaveDelay = 6, WaveCooldown = 10;
        // Hit shapes in the idol's local space (metres). HitHeight keeps other floors safe.
        internal const float BiteReach = 6, BiteHalfWidth = 1.3f, BiteBack = .3f, SweepRadius = 3.5f, WaveRadius = 6, HitHeight = 1.8f;
        // Shot is last so snapshot validation can bound the action byte with it.
        internal const byte Summon = 0, Walk = 1, Bite = 2, Sweep = 3, Wave = 4, Death = 5, Shot = 6;
        // 潮彈: a lobbed water shell for targets 7-22 m away (0-22 m when the target cannot be reached). The idol
        // stands still for the windup and throw only; the shell then lands on its own clock while the idol chases.
        internal const float ShotMin = 7, ShotMax = 22, FirstShotDelay = 3, ShotCooldown = 5, ShotReplan = .4f;
        // Damage radius, and the slightly wider ring drawn on the ground as the landing telegraph.
        internal const float ShotRadius = 2.2f, ShotRing = 2.5f, ShotSplash = .6f;
        // Half lead on the target's run, capped so a sprinting player still outruns the shell.
        internal const float ShotLead = .5f, ShotLeadMax = 4;
        internal const int ShotSlowSeconds = 3;
        internal const byte ShotSlowPercent = 30;
        // Launch point above the crown in the idol's local space, clear of its 2.6 m tall hitbox.
        internal const float MuzzleHeight = 2.95f, MuzzleForward = .35f;
        // Highest clear arc wins; the low fallbacks keep the shell usable under tavern ceilings.
        internal static readonly float[] ShotApexes = { 3, 2, 1.2f };
        internal static float Windup(byte action) { return action == Bite || action == Shot ? .7f : action == Sweep ? 1 : action == Wave ? 1.6f : 0; }
        internal static float Strike(byte action) { return action == Bite || action == Shot ? .2f : action == Sweep ? .35f : action == Wave ? .4f : 0; }
        // Windup + strike + recovery (Bite .8, Sweep 1.0, Wave 1.4, Shot .1).
        internal static float Duration(byte action)
        {
            return action == Summon ? Flight + Rise : action == Bite ? 1.7f : action == Sweep ? 2.35f : action == Wave ? 3.4f :
                action == Death ? 1.4f : action == Shot ? 1 : 0;
        }
        internal static short Damage(byte action) { return (short)(action == Bite ? 12 : action == Sweep || action == Shot ? 16 : 20); }
        // PlayerNet.HitClientRpc drops the hit when the owner's 3D distance exceeds this, so it must cover
        // every InHit point (box corners and the height allowance) plus a little movement latency.
        internal static float Range(byte action) { return action == Bite ? 6.7f : action == Sweep ? 4.2f : action == Shot ? 3.5f : 6.5f; }
        // Longest scene name a snapshot may carry; the game's scenes are 10 characters or fewer.
        internal const int SceneNameMax = 32;
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
        // Horizontal distance to the landing point decides the flight: 7 m ≈ 1.13 s, 22 m ≈ 1.63 s.
        internal static float ShotFlight(float distance) { return .9f + distance / 30; }
        internal static bool InShotRange(float distance, bool stranded)
        { return Finite(distance) && distance >= 0 && distance <= ShotMax && (stranded || distance >= ShotMin); }
        internal static float LeadSeconds(float flight) { return (Windup(Shot) + flight) * ShotLead; }
        internal static float ArcLift(float phase, float apex) { return 4 * phase * (1 - phase) * apex; }
        // Offset from the landing point to the player's feet.
        internal static bool InSplash(float dx, float dz, float height)
        {
            if (!Finite(dx) || !Finite(dz) || !Finite(height) || Math.Abs(height) > HitHeight) return false;
            return dx * dx + dz * dz <= ShotRadius * ShotRadius;
        }
        // at = release time (0 = no shell); the caller's armed flag makes the splash happen once.
        internal static bool ShotDue(double now, double at, float flight) { return at > 0 && Finite(now) && Finite(at) && now >= at + flight; }
        internal static bool ValidShot(double at, float flight, float apex, double born, double now)
        {
            if (at == 0) return true;
            return Finite(at) && Finite(flight) && Finite(apex) && at >= born && at <= now + 6 &&
                flight > 0 && flight <= ShotFlight(ShotMax) + .01f && apex >= 0 && apex <= ShotApexes[0];
        }
        internal static bool CanUse(bool server, bool alive, bool ownsContainer, ushort dataId, int amount, double sinceLast)
        { return server && alive && ownsContainer && dataId == ItemId && amount > 0 && Finite(sinceLast) && sinceLast >= 1; }
    }
}
