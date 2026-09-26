using System;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

namespace TonyMods
{
    // Plain MonoBehaviour added after native network spawning: no RPC layout changes.
    public sealed class TideCreature : MonoBehaviour
    {
        internal TideSummons.Record State { get; private set; }
        private TideSummons owner;
        private Vulnerable vulnerable;
        private CreatureHostile native;
        private NavMeshAgent agent;
        private CapsuleCollider hitbox;
        private TideModel model;
        private bool server, hit, hidden;
        private double nextWave, nextPath;
        private double previousAge;
        private double lastHostClock;
        private ushort previousHp;
        private float hitFlash;
        private Vector3 previousPosition;
        private float speed;

        internal void Initialize(TideSummons manager, TideSummons.Record record, bool isServer)
        {
            owner = manager; State = record; server = isServer;
            native = GetComponent<CreatureHostile>(); vulnerable = GetComponent<Vulnerable>(); agent = GetComponent<NavMeshAgent>();
            if (native == null || vulnerable == null || agent == null) throw new InvalidOperationException("Native Spider must provide CreatureHostile, Vulnerable and NavMeshAgent");
            native.StopAllCoroutines(); native.enabled = false;
            // forceRenderingOff survives ArachnophobiaMeshSwitch re-enabling the spider renderer on settings apply.
            foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true)) { renderer.enabled = false; renderer.forceRenderingOff = true; }
            // Animators stay enabled: NetworkAnimator polls them every frame without an enabled guard, and the
            // stopped native AI never changes their parameters.
            foreach (Collider collider in GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            transform.localScale = Vector3.one;
            Interactive interactive = GetComponent<Interactive>();
            if (interactive != null) interactive.ObjectTitle = "TonyTideTitle";
            RaiseHealthBar();
            hitbox = gameObject.AddComponent<CapsuleCollider>();
            hitbox.center = new Vector3(0, 1.3f, 0); hitbox.radius = .5f; hitbox.height = 2.6f;
            hitbox.enabled = false;
            model = TideModel.Create(transform);
            native.doNotDespawnUntilLooted = false;
            vulnerable.dropItems = new LootItem[0]; vulnerable.dropCommonLoot = vulnerable.dropIngredientLoot = false;
            vulnerable.deathGameEvent = default(GameEvent); vulnerable.despawnOnDeath = vulnerable.deactivateOnDeath = false;
            vulnerable.ignorePlayerWeapon = false; vulnerable.specialWeapon = new ItemData[0];
            vulnerable.hitMultiplier = 1;
            previousPosition = transform.position;
            if (server)
            {
                vulnerable.SetHpMax(TideRules.Health); vulnerable.hp.Value = TideRules.Health;
                vulnerable.invinsible.Value = true;
                agent.enabled = true;
                if (!Place(record.landing)) throw new InvalidOperationException("Cannot place native agent on navigation mesh");
                agent.radius = TideRules.Radius; agent.height = 2.8f;
                agent.speed = 2.1f; agent.acceleration = 7; agent.angularSpeed = 160;
                agent.stoppingDistance = 1.65f; agent.autoBraking = true; agent.isStopped = true;
                nextWave = record.born + TideRules.Duration(TideRules.Summon) + 6;
                lastHostClock = TideSummons.Now;
            }
            else agent.enabled = false;
            previousHp = vulnerable.hp.Value;
        }
        // The landing test samples every agent type; retry on the spider's own NavMesh if Warp misses it.
        private bool Place(Vector3 landing)
        {
            if (agent.Warp(landing) && agent.isOnNavMesh) return true;
            NavMeshHit nav;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            return NavMesh.SamplePosition(landing, out nav, 1.5f, filter) && agent.Warp(nav.position) && agent.isOnNavMesh;
        }
        // The spider's name/HP canvas sits about 2 m up, inside the idol's chest; billboard it above the crown.
        private void RaiseHealthBar()
        {
            try
            {
                foreach (RotateTowardsPlayer view in GetComponentsInChildren<RotateTowardsPlayer>(true))
                {
                    AccessTools.Field(typeof(RotateTowardsPlayer), "useHeight").SetValue(view, true);
                    AccessTools.Field(typeof(RotateTowardsPlayer), "height").SetValue(view, 3.2f);
                }
            }
            catch (Exception) { }
        }
        internal void Apply(TideSummons.Record record)
        { State = record; hidden = false; if (model != null) model.gameObject.SetActive(true); }
        internal void Hide() { hidden = true; if (model != null) model.gameObject.SetActive(false); if (hitbox != null) hitbox.enabled = false; }
        private void Update()
        {
            if (hidden || State == null || model == null) return;
            if (server) Tick();
            if (this == null || model == null) return;
            if (vulnerable.hp.Value < previousHp) hitFlash = .3f;
            previousHp = vulnerable.hp.Value; hitFlash = Mathf.Max(0, hitFlash - Time.deltaTime);
            float moved = Vector3.Distance(previousPosition, transform.position);
            previousPosition = transform.position;
            speed = Mathf.Lerp(speed, Time.deltaTime > 0 && moved < 3 ? moved / Time.deltaTime : 0, Time.deltaTime * 9);
            float age = (float)(TideSummons.Now - State.started);
            hitbox.enabled = State.action != TideRules.Summon && State.action != TideRules.Death;
            model.Animate(State.action, age, speed, hitFlash, TideSummons.Arc(State.from, State.landing + Vector3.up * .16f, Mathf.Clamp01(age / TideRules.Flight)));
        }
        private void Tick()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            double now = TideSummons.Now;
            double paused = Time.timeScale <= 0 ? Math.Max(0, now - lastHostClock) : 0;
            lastHostClock = now;
            if (paused > 0)
            {
                // Preserve the telegraph and lifetime across a paused solo game.
                State.started += paused; State.born += paused; nextWave += paused; nextPath += paused;
                return;
            }
            double elapsed = now - State.started;
            if (State.action != TideRules.Death && (vulnerable.hp.Value == 0 || now - State.born >= TideRules.Lifetime)) Enter(TideRules.Death);
            if (State.action == TideRules.Death)
            {
                Stop(); vulnerable.invinsible.Value = true;
                if (now - State.started >= TideRules.Duration(TideRules.Death)) owner.Remove(this);
                return;
            }
            if (State.action == TideRules.Summon)
            {
                Stop();
                if (elapsed >= TideRules.Duration(TideRules.Summon)) { vulnerable.invinsible.Value = false; Enter(TideRules.Walk); }
                return;
            }
            if (State.action >= TideRules.Bite && State.action <= TideRules.Wave)
            {
                Stop();
                if (!hit && TideRules.CrossedHit(State.action, previousAge, elapsed)) { hit = true; Strike(State.action); }
                previousAge = elapsed;
                if (elapsed >= TideRules.Duration(State.action)) Enter(TideRules.Walk);
                return;
            }
            if (!agent.enabled || !agent.isOnNavMesh) { Enter(TideRules.Death); return; }
            PlayerNet target = Nearest();
            if (target == null) { Stop(); return; }
            Vector3 delta = target.transform.position - transform.position; delta.y = 0;
            float distance = delta.magnitude;
            if (distance <= 2.15f && ClearSight(target))
            {
                Stop(); if (distance > .01f) transform.rotation = Quaternion.LookRotation(delta);
                byte action = now >= nextWave ? TideRules.Wave : distance < 1.45f ? TideRules.Sweep : TideRules.Bite;
                if (action == TideRules.Wave) nextWave = now + 10;
                Enter(action); return;
            }
            if (now >= nextPath)
            {
                nextPath = now + .3;
                NavMeshHit nav;
                if (NavMesh.SamplePosition(target.transform.position, out nav, 1, agent.areaMask))
                { agent.isStopped = false; agent.SetDestination(nav.position); }
                else Stop();
            }
        }
        private void Enter(byte action)
        {
            State.action = action; State.started = TideSummons.Now; hit = false; previousAge = 0;
            if (action != TideRules.Walk) Stop();
            owner.Changed();
        }
        private void Stop() { if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true; }
        private PlayerNet Nearest()
        {
            if (PlayerManager.Instance == null) return null;
            PlayerNet best = null; float nearest = 30 * 30;
            foreach (PlayerNet player in PlayerManager.Instance.players.Values)
            {
                if (player == null || !player.IsSpawned || player.hp.Value <= 0 || player.isDespawning || player.isInvisible.Value) continue;
                if (Math.Abs(player.transform.position.y - transform.position.y) > 4) continue;
                float distance = (player.transform.position - transform.position).sqrMagnitude;
                if (distance < nearest) { nearest = distance; best = player; }
            }
            return best;
        }
        private bool ClearSight(PlayerNet player)
        {
            Vector3 from = transform.position + Vector3.up * 1.1f, to = player.transform.position + Vector3.up;
            Vector3 direction = to - from;
            foreach (RaycastHit obstacle in Physics.RaycastAll(from, direction.normalized, direction.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (obstacle.transform.IsChildOf(transform) || obstacle.transform == transform ||
                    obstacle.transform.IsChildOf(player.transform) || obstacle.transform == player.transform) continue;
                return false;
            }
            return true;
        }
        private void Strike(byte action)
        {
            if (!server || PlayerManager.Instance == null || vulnerable.hp.Value == 0) return;
            foreach (PlayerNet player in PlayerManager.Instance.players.Values)
            {
                if (player == null || !player.IsSpawned || player.hp.Value <= 0 || player.isDespawning || player.isInvisible.Value) continue;
                Vector3 local = transform.InverseTransformPoint(player.transform.position);
                if (!TideRules.InHit(action, local.x, local.z, local.y) || !ClearSight(player)) continue;
                // Same authoritative-to-owner damage path as CreatureHostile. One call per target per attack.
                player.HitClientRpc(TideRules.Damage(action), transform.position, TideRules.Range(action), true, EffectsController.EffectType.None);
            }
        }
        private void OnDestroy() { if (model != null) Destroy(model.gameObject); if (hitbox != null) Destroy(hitbox); }
    }
}
