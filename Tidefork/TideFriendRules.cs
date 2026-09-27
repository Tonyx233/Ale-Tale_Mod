using System;
using System.Collections.Generic;

namespace TonyMods
{
    internal static class TideFriendRules
    {
        internal const ushort ItemId = 47941;
        internal const float FollowDistance = 2.5f, Leash = 35, TeleportDistance = 45;
        internal const double AssistSeconds = 15;
        // Native monsters that turn on the idol when it hurts them: their regular "Hit" anim event reaches
        // CreatureHostile.OnAnim's chased-Vulnerable branch and they keep the native Attack coroutine (run-tide-friend.ps1
        // checks both). Bosses and ranged casters keep their native player targeting.
        private static readonly HashSet<string> tauntable = new HashSet<string>
        { "CreatureHostile", "CreatureBear", "CreatureBoar", "CreatureGhostMelee", "CreatureHornet", "CreatureSlime", "OrcMelee", "SkeletonWarrior2H" };
        internal static bool Tauntable(string type) { return type != null && tauntable.Contains(type); }
        // CreatureBase.State Idle 1, Walk 2, Chase 4, Tired 5, Attack 6, WalkToSpecificPoint 11: the states native OnHit
        // retargets from (RunAway, DigIn, SpecialAttack, Block, Death and boss phases are left alone).
        internal static bool TauntState(int state) { return state == 1 || state == 2 || state == 4 || state == 5 || state == 6 || state == 11; }
        // threat = the monster fights the owner or the idol itself; attackedAt = the owner's last hit on it.
        internal static bool Authorized(bool monster, bool protectedTarget, bool alive, bool threat, double attackedAt, double now)
        {
            return monster && !protectedTarget && alive && (threat ||
                TideRules.Finite(attackedAt) && TideRules.Finite(now) && attackedAt >= 0 && now >= attackedAt && now - attackedAt < AssistSeconds);
        }
    }
}
