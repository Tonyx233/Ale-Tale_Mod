using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace TonyMods
{
    // Host-only companion decisions. The existing TideCreature owns movement, HP, attacks and replication.
    internal sealed class TideFriend
    {
        private readonly TideCreature body;
        private readonly Dictionary<CreatureHostile, double> attacked = new Dictionary<CreatureHostile, double>();
        private readonly HashSet<CreatureHostile> candidates = new HashSet<CreatureHostile>();
        private double nextScan, nextMove, lastFollow = -1, stuckSince = -1;
        private Vector3 lastProgress;
        internal PlayerNet Master { get; private set; }

        internal TideFriend(TideCreature creature) { body = creature; lastProgress = body.transform.position; }
        internal bool ResolveOwner()
        {
            PlayerNet player;
            Master = PlayerManager.Instance != null && PlayerManager.Instance.players.TryGetValue(body.State.summoner, out player) ? player : null;
            return Master != null && Master.IsSpawned && !Master.isDespawning && Master.hp.Value > 0;
        }
        internal static bool Monster(CreatureHostile creature)
        {
            if (creature == null || !creature.IsSpawned || creature.GetComponent<PetBase>() != null ||
                creature.GetComponent<CreaturePaddockAnimal>() != null) return false;
            TideCreature tide = creature.GetComponent<TideCreature>();
            return tide == null || tide.State != null && !tide.State.friendly;
        }
        internal void Attacked(Vulnerable victim)
        {
            CreatureHostile enemy = victim == null ? null : victim.GetComponent<CreatureHostile>();
            if (!Monster(enemy)) return;
            attacked[enemy] = TideSummons.Now;
            candidates.Add(enemy);
        }
        internal void Pause(double seconds)
        {
            foreach (CreatureHostile enemy in new List<CreatureHostile>(attacked.Keys)) attacked[enemy] += seconds;
            nextScan += seconds; nextMove += seconds;
            if (lastFollow >= 0) lastFollow += seconds;
            if (stuckSince >= 0) stuckSince += seconds;
        }
        internal bool Eligible(CreatureHostile enemy)
        {
            if (!Monster(enemy) || Master == null) return false;
            Vulnerable hp = enemy.GetComponent<Vulnerable>();
            PlayerNet chased;
            bool threat = enemy.HasChasedPlayer(out chased) && chased == Master;
            // Enemy Tideforks disable native AI; ask their actual controller instead.
            TideCreature tide = enemy.GetComponent<TideCreature>();
            if (tide != null) threat = tide.Threatens(Master);
            double at; if (!attacked.TryGetValue(enemy, out at)) at = -1;
            return TideFriendRules.Authorized(true, false, hp != null && hp.hp.Value > 0 && hp.isActive.Value && !hp.invinsible.Value,
                threat, at, TideSummons.Now) && Vector3.Distance(enemy.transform.position, Master.transform.position) <= TideFriendRules.Leash;
        }
        internal List<CreatureHostile> Enemies()
        {
            double now = TideSummons.Now;
            if (now >= nextScan)
            {
                nextScan = now + .4;
                candidates.Clear();
                foreach (CreatureHostile enemy in new List<CreatureHostile>(attacked.Keys))
                {
                    if (enemy == null || now - attacked[enemy] >= TideFriendRules.AssistSeconds) attacked.Remove(enemy);
                    else candidates.Add(enemy);
                }
                if (Master != null)
                    foreach (Collider collider in Physics.OverlapSphere(Master.transform.position, TideFriendRules.Leash))
                    {
                        CreatureHostile enemy = collider.GetComponentInParent<CreatureHostile>();
                        if (enemy != null) candidates.Add(enemy);
                    }
            }
            var result = new List<CreatureHostile>();
            foreach (CreatureHostile enemy in candidates) if (Eligible(enemy)) result.Add(enemy);
            return result;
        }
        internal Component Target()
        {
            if (Master == null || Vector3.Distance(body.transform.position, Master.transform.position) > TideFriendRules.Leash) return null;
            CreatureHostile best = null; float distance = float.MaxValue;
            foreach (CreatureHostile enemy in Enemies())
            {
                float d = (enemy.transform.position - body.transform.position).sqrMagnitude;
                if (d < distance) { best = enemy; distance = d; }
            }
            return best;
        }
        internal void Follow(NavMeshAgent agent)
        {
            if (Master == null || !agent.enabled || !agent.isOnNavMesh) return;
            double now = TideSummons.Now;
            if (now < nextMove) return;
            // Standing still through a fight or a volley is not being stuck: measure progress afresh.
            if (lastFollow < 0 || now - lastFollow > 1) { lastProgress = body.transform.position; stuckSince = -1; }
            lastFollow = now;
            nextMove = now + .25;
            float distance = Vector3.Distance(body.transform.position, Master.transform.position);
            agent.stoppingDistance = TideFriendRules.FollowDistance;
            if (distance <= TideFriendRules.FollowDistance + .5f) { agent.isStopped = true; stuckSince = -1; return; }
            if (Vector3.Distance(lastProgress, body.transform.position) > .5f) { lastProgress = body.transform.position; stuckSince = now; }
            if (stuckSince < 0) stuckSince = now;
            Vector3 desired = Master.transform.position - Master.transform.forward * TideFriendRules.FollowDistance;
            NavMeshHit nav;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(desired, out nav, 3, filter)) return;
            // Same recovery principle as native pets: catch up after a teleport or a blocked doorway.
            if (distance > TideFriendRules.TeleportDistance || now - stuckSince > 5)
            {
                if (agent.Warp(nav.position)) { agent.ResetPath(); lastProgress = body.transform.position; stuckSince = now; }
            }
            else
            {
                // Stop FollowDistance from the owner, not short of the already-offset warp point.
                NavMeshHit destination;
                if (!NavMesh.SamplePosition(Master.transform.position, out destination, 3, filter)) return;
                agent.isStopped = false;
                if (!agent.pathPending) agent.SetDestination(destination.position);
            }
        }
        internal void Hit(CreatureHostile enemy, ushort damage)
        {
            // Recheck at impact, including splashes: unrelated neighbours must never take collateral damage.
            if (Eligible(enemy)) enemy.GetComponent<Vulnerable>().Hit(damage, 0);
        }
    }
}
