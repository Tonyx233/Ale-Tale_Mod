using System;

namespace TonyMods
{
    internal static class TideHuntRules
    {
        internal const ushort ItemId = 47942;
        internal const int ReturnCount = 30;
        internal const float Radius = 1000;
        // Fail closed: only naturally spawned, ordinary creatures from this explicit list qualify.
        internal static bool Species(string type)
        {
            switch (type)
            {
                case "Boar": case "Wolf": case "Bear": case "Hornet": case "Turtle":
                case "Crab": case "Toad": case "Rabbit": case "Wildfowl":
                case "SkeletonWarrior2H": case "SkeletonWarriorShield": case "OrcMelee":
                case "Zombie": case "Spider": case "ZombieHound": case "Ghoul":
                case "Mummy": case "Snake": case "Snail": case "Slug": return true;
                default: return false;
            }
        }
        internal static bool Within(float x, float z)
        { return !Single.IsNaN(x) && !Single.IsNaN(z) && x * x + z * z <= Radius * Radius; }
        internal static bool Return(int count, bool active) { return count >= ReturnCount || !active; }
    }
}
