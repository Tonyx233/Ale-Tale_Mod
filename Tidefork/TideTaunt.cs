using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace TonyMods
{
    // Host-only: turns a native monster on 十魚架(友) through the game's own defence-quest targeting. CreatureHostile
    // chases _chasedTarget and, on the "Hit" anim event, calls Vulnerable.Hit(attackPower, 0) on _chasedVulnerable
    // when it chases no player. tests/run-tide-friend.ps1 pins every native member used here.
    internal static class TideTaunt
    {
        private static FieldInfo hasVulnerable, chasedVulnerable, hasPlayer, chasedTarget, hasTarget;
        private static MethodInfo setVulnerable, removePlayer;
        internal static bool Ready { get; private set; }

        internal static void Resolve()
        {
            Type type = typeof(CreatureHostile);
            hasVulnerable = Field(type, "_hasChasedVulnerable"); chasedVulnerable = Field(type, "_chasedVulnerable");
            hasPlayer = Field(type, "_hasChasedPlayer"); chasedTarget = Field(type, "_chasedTarget"); hasTarget = Field(type, "_hasChasedTarget");
            setVulnerable = AccessTools.Method(type, "SetChasedVulnerable", new[] { typeof(Vulnerable) });
            removePlayer = AccessTools.Method(type, "RemoveChasedPlayer", Type.EmptyTypes);
            if (setVulnerable == null) throw new MissingMethodException(type.Name, "SetChasedVulnerable");
            if (removePlayer == null) throw new MissingMethodException(type.Name, "RemoveChasedPlayer");
            Ready = true;
        }
        private static FieldInfo Field(Type type, string name)
        {
            FieldInfo field = AccessTools.Field(type, name);
            if (field == null) throw new MissingFieldException(type.Name, name);
            return field;
        }
        // The hit that preceded this already ran native OnHit synchronously (hp change -> OnHpChanged -> OnHit), which
        // sent the monster after the nearest player. Take it back: drop the player, chase the idol, and wake it from the
        // same states native OnHit does (Attack re-reads its target every frame and follows on its own).
        internal static bool Pull(CreatureHostile enemy, Vulnerable idol)
        {
            if (!Ready || enemy == null || idol == null || !TideFriendRules.Tauntable(enemy.GetType().Name)) return false;
            Vulnerable health = enemy.GetComponent<Vulnerable>();
            int state = (int)enemy.CurrentState;
            if (health == null || health.hp.Value == 0 || !TideFriendRules.TauntState(state)) return false;
            removePlayer.Invoke(enemy, null);
            setVulnerable.Invoke(enemy, new object[] { idol });
            if (state != (int)CreatureBase.State.Attack) enemy.SetState(CreatureBase.State.Chase);
            return true;
        }
        internal static bool Chasing(CreatureHostile enemy, Vulnerable idol)
        {
            return Ready && enemy != null && idol != null && (bool)hasVulnerable.GetValue(enemy) && !(bool)hasPlayer.GetValue(enemy) &&
                (Vulnerable)chasedVulnerable.GetValue(enemy) == idol;
        }
        // Before the idol dies or despawns. Chase and Attack stop once _hasChasedTarget is false, but Attack's exit can
        // still read _chasedTarget.position, so it must not be left on a destroyed idol.
        internal static void Release(CreatureHostile enemy, Vulnerable idol)
        {
            if (!Ready || enemy == null || idol == null || (Vulnerable)chasedVulnerable.GetValue(enemy) != idol) return;
            chasedVulnerable.SetValue(enemy, null);
            if ((bool)hasVulnerable.GetValue(enemy)) { hasVulnerable.SetValue(enemy, false); hasTarget.SetValue(enemy, hasPlayer.GetValue(enemy)); }
            if ((Transform)chasedTarget.GetValue(enemy) == idol.transform) chasedTarget.SetValue(enemy, enemy.transform);
        }
    }
}
