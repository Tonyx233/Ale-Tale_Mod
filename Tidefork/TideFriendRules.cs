using System;

namespace TonyMods
{
    internal static class TideFriendRules
    {
        internal const ushort ItemId = 47941;
        internal const float FollowDistance = 2.5f, Leash = 35, TeleportDistance = 45;
        internal const double AssistSeconds = 15;
        internal static bool Authorized(bool monster, bool protectedTarget, bool alive, bool threatensOwner, double attackedAt, double now)
        {
            return monster && !protectedTarget && alive && (threatensOwner ||
                TideRules.Finite(attackedAt) && TideRules.Finite(now) && attackedAt >= 0 && now >= attackedAt && now - attackedAt < AssistSeconds);
        }
    }
}
