using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace TonyMods
{
    // Host-only work controller. Combat geometry/animations remain in TideCreature; every impact rechecks eligibility.
    internal sealed class TideHunter
    {
        private readonly TideCreature body;
        internal readonly TideHuntHome Home;
        internal readonly List<CollectibleNet> Drops = new List<CollectibleNet>();
        private readonly List<CreatureBase> corpses = new List<CreatureBase>();
        private readonly Dictionary<CreatureBase, double> rejected = new Dictionary<CreatureBase, double>();
        private CreatureBase prey;
        private double nextSearch, chaseUntil, nextMove, stuckSince;
        private Vector3 progress;
        private bool returning;
        private static readonly FieldInfo lootField = AccessTools.Field(typeof(CreatureBase), "lootItems");
        private static readonly FieldInfo moneyField = AccessTools.Field(typeof(CreatureBase), "lootMoney");
        private static readonly FieldInfo availableField = AccessTools.Field(typeof(CreatureBase), "isLootAvailable");
        private static readonly FieldInfo rarityField = AccessTools.Field(typeof(CreatureBase), "rarity");
        private static List<LootItem> LootTable(CreatureBase c) { return (List<LootItem>)lootField.GetValue(c); }
        private static NetworkVariable<bool> LootAvailable(CreatureBase c) { return (NetworkVariable<bool>)availableField.GetValue(c); }
        private readonly Dictionary<CreatureBase, double> killedAt = new Dictionary<CreatureBase, double>();
        internal TideHunter(TideCreature creature, TideHuntHome home)
        { body = creature; Home = home; progress = body.transform.position; stuckSince = TideSummons.Now; }
        internal bool Ready { get { return Home != null && Home.House != null && Home.House.IsSpawned; } }
        private void BeginReturn()
        {
            if (returning) return;
            returning = true; progress = body.transform.position; stuckSince = TideSummons.Now;
        }
        private static bool QuestLoot(IEnumerable<LootItem> items)
        { return items != null && items.Any(l => l != null && l.itemData != null && l.itemData.quest); }
        internal bool Allowed(CreatureBase creature, bool alive)
        {
            if (!Ready || creature == null || !creature.IsSpawned || creature.gameObject.scene != Home.gameObject.scene) return false;
            Spawnable spawn = creature.GetComponent<Spawnable>(); Vulnerable hp = creature.GetComponent<Vulnerable>();
            if (spawn == null || !TideHuntBuilding.Natural.Contains(spawn) || spawn.isDungeon || !TideHuntRules.Species(spawn.type.ToString()) ||
                hp == null || alive && (hp.invinsible.Value || !hp.isActive.Value || hp.hp.Value == 0) ||
                creature.GetComponent<TideCreature>() != null || creature.GetComponent<PetBase>() != null ||
                creature.GetComponent<CreaturePaddockAnimal>() != null || creature.GetComponent<PaddockAnimal>() != null ||
                creature.GetComponentInParent<QuestGiver>() != null || QuestLoot(LootTable(creature)) || QuestLoot(hp.dropItems)) return false;
            Vector3 offset = creature.transform.position - Home.transform.position;
            return TideHuntRules.Within(offset.x, offset.z);
        }
        internal Component Target()
        {
            if (!Ready) return null;
            if (TideHuntRules.Return(Home.Count, Home.Active)) BeginReturn();
            if (returning || HasLoot()) { prey = null; return null; }
            Vector3 offset = body.transform.position - Home.transform.position;
            if (!TideHuntRules.Within(offset.x, offset.z)) { BeginReturn(); prey = null; return null; }
            double now = TideSummons.Now;
            if (prey != null && Allowed(prey, true) && now < chaseUntil) return prey;
            if (prey != null) { rejected[prey] = now + 30; prey = null; body.ReleaseTaunts(); }
            if (now < nextSearch) return null;
            nextSearch = now + 2;
            foreach (CreatureBase key in rejected.Keys.ToArray()) if (key == null || rejected[key] <= now) rejected.Remove(key);
            var choices = new List<CreatureBase>();
            foreach (Spawnable spawn in TideHuntBuilding.Natural.ToArray())
            {
                if (spawn == null) { TideHuntBuilding.Natural.Remove(spawn); continue; }
                CreatureBase creature = spawn.GetComponent<CreatureBase>(); double until;
                if (!Allowed(creature, true) || rejected.TryGetValue(creature, out until) && now < until) continue;
                choices.Add(creature);
            }
            // Randomize before path checks: all reachable eligible prey have a chance, not just the nearest.
            while (choices.Count > 0)
            {
                int index = UnityEngine.Random.Range(0, choices.Count); CreatureBase candidate = choices[index]; choices.RemoveAt(index);
                if (!Reachable(candidate.transform.position)) { rejected[candidate] = now + 30; continue; }
                prey = candidate; chaseUntil = now + 45; return prey;
            }
            return null;
        }
        private bool Reachable(Vector3 point)
        {
            var agent = body.GetComponent<NavMeshAgent>();
            var path = new NavMeshPath(); NavMeshHit nav;
            return agent.enabled && agent.isOnNavMesh && NavMesh.SamplePosition(point, out nav, 2, agent.areaMask) &&
                agent.CalculatePath(nav.position, path) && path.status == NavMeshPathStatus.PathComplete;
        }
        private bool HasLoot()
        {
            // Native death animation may enable corpse loot later; do not discard it on the first dead frame.
            corpses.RemoveAll(c => c == null || !c.IsSpawned || !LootAvailable(c).Value && TideSummons.Now - killedAt[c] > 10);
            foreach (CreatureBase c in killedAt.Keys.ToArray()) if (!corpses.Contains(c)) killedAt.Remove(c);
            Drops.RemoveAll(d => d == null || !d.IsSpawned || d.item.Value.amount == 0 || d.itemData == null || d.itemData.quest);
            return corpses.Count > 0 || Drops.Count > 0;
        }
        internal void Hit(CreatureBase enemy, ushort damage)
        {
            if (returning || Home.Count >= TideHuntRules.ReturnCount || !Home.Active || !Allowed(enemy, true)) return;
            var hp = enemy.GetComponent<Vulnerable>();
            // Ownership only lasts for this actual hit; another player's kill is not the hunter's loot.
            TideHuntBuilding.Victims[hp] = this;
            try { hp.Hit(damage, 0); }
            finally { TideHuntBuilding.Victims.Remove(hp); }
            if (hp.hp.Value == 0)
            {
                if (!corpses.Contains(enemy)) { corpses.Add(enemy); killedAt[enemy] = TideSummons.Now; }
                enemy.doNotDespawnUntilLooted = true;
            }
            else { CreatureHostile hostile = enemy as CreatureHostile; if (hostile != null) body.Taunt(hostile); }
        }
        internal void Move(NavMeshAgent agent)
        {
            if (!Ready || !agent.enabled || !agent.isOnNavMesh || TideSummons.Now < nextMove) return;
            nextMove = TideSummons.Now + .25;
            if (TideHuntRules.Return(Home.Count, Home.Active)) BeginReturn();
            if (returning)
            {
                body.ReleaseTaunts(); Vector3 delivery;
                if (!Home.GetDelivery(out delivery)) { agent.isStopped = true; return; }
                if (Go(agent, delivery, 2))
                {
                    Home.DropAt(delivery);
                    if (Home.Count == 0) { returning = false; nextSearch = TideSummons.Now + 2; }
                }
                return;
            }
            HasLoot();
            if (corpses.Count > 0)
            {
                CreatureBase corpse = corpses[0];
                if (!Allowed(corpse, false) || !Reachable(corpse.transform.position)) { corpses.RemoveAt(0); return; }
                if (Go(agent, corpse.transform.position, 2.5f) && LootAvailable(corpse).Value) { Loot(corpse); corpses.RemoveAt(0); }
                return;
            }
            if (Drops.Count > 0)
            {
                CollectibleNet drop = Drops[0];
                if (!Reachable(drop.transform.position)) { Drops.RemoveAt(0); return; }
                if (Go(agent, drop.transform.position, 2)) { drop.PickupByHelper(Home.Cargo); Drops.RemoveAt(0); }
                return;
            }
            // Empty territory: wait at the base, periodically retry naturally populated prey.
            Go(agent, Home.transform.position + Home.transform.forward * 2, 2);
        }
        private bool Go(NavMeshAgent agent, Vector3 point, float stop)
        {
            if (Vector3.Distance(body.transform.position, point) <= stop + .25f)
            { agent.isStopped = true; stuckSince = TideSummons.Now; progress = body.transform.position; return true; }
            NavMeshHit nav;
            if (!NavMesh.SamplePosition(point, out nav, 2, agent.areaMask)) { agent.isStopped = true; return false; }
            agent.stoppingDistance = stop; agent.isStopped = false;
            if (!agent.pathPending) agent.SetDestination(nav.position);
            if (Vector3.Distance(body.transform.position, progress) > .5f)
            { progress = body.transform.position; stuckSince = TideSummons.Now; }
            // Returning cargo must not be lost to a blocked doorway. Recover only on a verified walkable point.
            if (returning && TideSummons.Now - stuckSince > 20 && agent.Warp(nav.position))
            { agent.ResetPath(); progress = body.transform.position; stuckSince = TideSummons.Now; }
            return false;
        }
        private void Loot(CreatureBase corpse)
        {
            if (!LootAvailable(corpse).Value || corpse.GetComponent<Vulnerable>().hp.Value != 0 || !Allowed(corpse, false)) return;
            var rolled = new List<Item>();
            foreach (LootItem loot in LootTable(corpse))
            {
                if (loot.itemData == null || loot.itemData.quest || UnityEngine.Random.value > loot.dropChance) continue;
                Item item = new Item(loot.itemData);
                if (loot.amountOverride > 0) item.amount = loot.amountOverride;
                if (loot.amountMinMax != Vector2Int.zero) item.amount = (ushort)UnityEngine.Random.Range(loot.amountMinMax.x, loot.amountMinMax.y + 1);
                ItemData.Rarity rarity = ((NetworkVariable<ItemData.Rarity>)rarityField.GetValue(corpse)).Value;
                if (loot.itemData.hasRarity && (byte)rarity != 255) item.rarity = rarity;
                ItemManager.Instance.CheckWeapon(ref item, false);
                if (item.amount > 0) rolled.Add(item);
            }
            // Consume the corpse once on the main server thread, just as native LootServerRpc does.
            // Materialize leftovers at the corpse if a cargo insertion cannot be completed.
            LootAvailable(corpse).Value = false;
            foreach (Item item in rolled)
            {
                ushort remaining; Home.Cargo.AddNewItem(item, out remaining, false);
                if (remaining > 0)
                {
                    Item rest = item; rest.amount = remaining;
                    CollectibleNet drop = CollectibleManager.Instance.Spawn(rest, corpse.transform.position + Vector3.up * .3f, Quaternion.identity, 0, true);
                    if (drop != null) Drops.Add(drop);
                }
            }
            // Coin drops follow native shared-wallet behavior and never count towards the 30 carried items.
            Vector2Int money = (Vector2Int)moneyField.GetValue(corpse);
            if (money != Vector2Int.zero)
            {
                int gold = UnityEngine.Random.Range(money.x, money.y);
                if (gold > 0) GameStatus.Instance.AddMoney((ulong)gold);
            }
        }
        internal void Pause(double seconds) { nextSearch += seconds; chaseUntil += seconds; nextMove += seconds; stuckSince += seconds; }
        internal void Died()
        {
            if (!Ready) return;
            Home.House.isHelperActive.Value = false;
            Home.DropAt(body.transform.position);
        }
    }
}
