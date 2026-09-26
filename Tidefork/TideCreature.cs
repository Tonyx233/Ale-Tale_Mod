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
        private bool server, hit, hidden, shotArmed, stranded;
        private double nextWave, nextPath, nextShot, nextShotPlan;
        private double previousAge;
        private double lastHostClock;
        private ushort previousHp;
        private float hitFlash;
        private Vector3 previousPosition;
        private float speed;
        // Host-side run velocity of the current target, for the shell's lead.
        private PlayerNet tracked;
        private Vector3 trackedPosition, trackedVelocity;
        private double trackedAt;

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
                agent.speed = TideRules.Speed; agent.acceleration = TideRules.Acceleration; agent.angularSpeed = TideRules.TurnSpeed;
                agent.stoppingDistance = TideRules.StopDistance; agent.autoBraking = true; agent.isStopped = true;
                nextWave = record.born + TideRules.Duration(TideRules.Summon) + TideRules.FirstWaveDelay;
                nextShot = record.born + TideRules.Duration(TideRules.Summon) + TideRules.FirstShotDelay;
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
            // The shell runs on its own clock, independent of the idol's current action.
            model.Shell(State.shotAt > 0 ? (float)(TideSummons.Now - State.shotAt) : float.NaN, State.shotFrom, State.shotTo, State.shotFlight, State.shotApex);
        }
        private void Tick()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
            double now = TideSummons.Now;
            double paused = Time.timeScale <= 0 ? Math.Max(0, now - lastHostClock) : 0;
            lastHostClock = now;
            if (paused > 0)
            {
                // Preserve the telegraph, a shell in the air and the cooldowns across a paused solo game.
                State.started += paused; State.born += paused; nextWave += paused; nextPath += paused;
                nextShot += paused; nextShotPlan += paused; if (State.shotAt > 0) State.shotAt += paused;
                return;
            }
            double elapsed = now - State.started;
            if (State.action != TideRules.Death && vulnerable.hp.Value == 0) Enter(TideRules.Death);
            if (State.action == TideRules.Death)
            {
                Stop(); vulnerable.invinsible.Value = true;
                if (now - State.started >= TideRules.Duration(TideRules.Death)) owner.Remove(this);
                return;
            }
            // The shell lands whatever the idol is doing by then (walking, biting, sweeping).
            if (shotArmed && TideRules.ShotDue(now, State.shotAt, State.shotFlight)) { shotArmed = false; Splash(); }
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
            if (State.action == TideRules.Shot)
            {
                // Stands still for the windup and throw only; the chase resumes while the shell is in the air.
                Stop();
                if (elapsed >= TideRules.Duration(TideRules.Shot)) Enter(TideRules.Walk);
                return;
            }
            if (!agent.enabled || !agent.isOnNavMesh) { Enter(TideRules.Death); return; }
            PlayerNet target = Nearest();
            if (target == null) { Stop(); stranded = false; return; }
            Track(target, now);
            Vector3 delta = target.transform.position - transform.position; delta.y = 0;
            float distance = delta.magnitude;
            if (distance <= TideRules.EngageDistance && ClearSight(target))
            {
                Stop(); if (distance > .01f) transform.rotation = Quaternion.LookRotation(delta);
                byte action = now >= nextWave ? TideRules.Wave : distance < TideRules.SweepDistance ? TideRules.Sweep : TideRules.Bite;
                if (action == TideRules.Wave) nextWave = now + TideRules.WaveCooldown;
                Enter(action); return;
            }
            if (now >= nextShot && now >= nextShotPlan && TideRules.InShotRange(distance, stranded))
            {
                // Arc checks cost dozens of raycasts: retry a blocked arc a few times a second, not every frame.
                nextShotPlan = now + TideRules.ShotReplan;
                if (Fire(target, delta, now)) return;
            }
            if (now >= nextPath)
            {
                nextPath = now + TideRules.RepathInterval;
                // A partial path means the target stands where the idol cannot walk (roof, crate, fenced yard).
                bool partial = agent.hasPath && !agent.pathPending && agent.pathStatus == NavMeshPathStatus.PathPartial;
                NavMeshHit nav;
                stranded = !NavMesh.SamplePosition(target.transform.position, out nav, 1, agent.areaMask);
                if (!stranded) { agent.isStopped = false; agent.SetDestination(nav.position); stranded = partial; }
                else Stop();
            }
        }
        private void Enter(byte action)
        {
            State.action = action; State.started = TideSummons.Now; hit = false; previousAge = 0;
            // Dead idols stop attacking, including a shell still in the air.
            if (action == TideRules.Death) { shotArmed = false; State.shotAt = 0; }
            if (action != TideRules.Walk) Stop();
            owner.Changed();
        }
        private static float Flat(Vector3 offset) { offset.y = 0; return offset.magnitude; }
        // 潮彈: the landing point (half lead on the target's run) and the highest clear arc are fixed before the
        // windup, so every client draws the same ring and flight from one snapshot.
        private bool Fire(PlayerNet target, Vector3 delta, double now)
        {
            if (shotArmed) return false;
            Quaternion facing = delta.sqrMagnitude > .0001f ? Quaternion.LookRotation(delta) : transform.rotation;
            Vector3 from = transform.position + facing * new Vector3(0, TideRules.MuzzleHeight, TideRules.MuzzleForward);
            // The shell forms above the crown: a ceiling right over the head rules the throw out.
            if (Blocked(transform.position + Vector3.up * 1.6f, from)) return false;
            Vector3 feet = target.transform.position;
            Vector3 lead = trackedVelocity * TideRules.LeadSeconds(TideRules.ShotFlight(delta.magnitude)); lead.y = 0;
            lead = Vector3.ClampMagnitude(lead, TideRules.ShotLeadMax);
            foreach (Vector3 aim in new[] { feet + lead, feet })
            {
                Vector3 ground; float apex;
                if (!Ground(aim, feet.y, out ground)) continue;
                float distance = Flat(ground - transform.position);
                if (!TideRules.InShotRange(distance, stranded) || !ClearArc(from, ground, out apex)) continue;
                // Plant the feet now: braking at full chase speed would slide the crown ~0.7 m off the gathering shell.
                if (agent.enabled && agent.isOnNavMesh) agent.velocity = Vector3.zero;
                transform.rotation = facing;
                nextShot = now + TideRules.ShotCooldown;
                State.shotFrom = from; State.shotTo = ground; State.shotApex = apex;
                State.shotFlight = TideRules.ShotFlight(distance);
                State.shotAt = now + TideRules.Windup(TideRules.Shot);
                shotArmed = true;
                Enter(TideRules.Shot);
                return true;
            }
            return false;
        }
        // Ground under the aim point at the target's own level (a lead past a ledge falls back to the feet).
        private bool Ground(Vector3 aim, float level, out Vector3 ground)
        {
            ground = Vector3.zero;
            float best = float.MaxValue;
            foreach (RaycastHit ray in Physics.RaycastAll(new Vector3(aim.x, level + 1.5f, aim.z), Vector3.down, 3.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (ray.distance < best && !Passable(ray.collider)) { best = ray.distance; ground = ray.point; }
            return best < float.MaxValue && Math.Abs(ground.y - level) <= 1.5f;
        }
        private bool ClearArc(Vector3 from, Vector3 to, out float apex)
        {
            foreach (float height in TideRules.ShotApexes)
            {
                Vector3 previous = from;
                bool clear = true;
                for (int i = 1; i <= 16 && clear; i++)
                {
                    Vector3 next = TideSummons.Arc(from, to + Vector3.up * .16f, i / 16f, height);
                    clear = !Blocked(previous, next);
                    previous = next;
                }
                if (clear) { apex = height; return true; }
            }
            apex = 0;
            return false;
        }
        // Players, creatures (other idols included) and this idol never stop the shell; only the world does.
        private bool Passable(Collider collider)
        {
            return collider == null || collider.transform.IsChildOf(transform) ||
                collider.GetComponentInParent<PlayerNet>() != null || collider.GetComponentInParent<CreatureBase>() != null;
        }
        private bool Blocked(Vector3 from, Vector3 to)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < .001f) return false;
            foreach (RaycastHit ray in Physics.RaycastAll(from, direction / length, length, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                if (!Passable(ray.collider)) return true;
            return false;
        }
        // Remote players move by NetworkTransform, so the velocity comes from position deltas.
        private void Track(PlayerNet target, double now)
        {
            Vector3 position = target.transform.position;
            double dt = now - trackedAt;
            if (target == tracked && dt <= 0) return;
            if (target != tracked || dt > .5) trackedVelocity = Vector3.zero;
            else
            {
                Vector3 velocity = (position - trackedPosition) / (float)dt; velocity.y = 0;
                // Teleports and respawns are not running.
                if (velocity.sqrMagnitude > 400) velocity = Vector3.zero;
                trackedVelocity = Vector3.Lerp(trackedVelocity, velocity, Mathf.Clamp01((float)dt * 6));
            }
            tracked = target; trackedPosition = position; trackedAt = now;
        }
        private void Stop() { if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true; }
        private PlayerNet Nearest()
        {
            if (PlayerManager.Instance == null) return null;
            PlayerNet best = null; float nearest = TideRules.SearchRadius * TideRules.SearchRadius;
            foreach (PlayerNet player in PlayerManager.Instance.players.Values)
            {
                if (player == null || !player.IsSpawned || player.hp.Value <= 0 || player.isDespawning || player.isInvisible.Value) continue;
                if (Math.Abs(player.transform.position.y - transform.position.y) > TideRules.SearchHeight) continue;
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
        private void Splash()
        {
            if (!server || PlayerManager.Instance == null || vulnerable.hp.Value == 0) return;
            Vector3 center = State.shotTo;
            foreach (PlayerNet player in PlayerManager.Instance.players.Values)
            {
                if (player == null || !player.IsSpawned || player.hp.Value <= 0 || player.isDespawning || player.isInvisible.Value) continue;
                Vector3 offset = player.transform.position - center;
                if (!TideRules.InSplash(offset.x, offset.z, offset.y) || Blocked(center + Vector3.up * .5f, player.transform.position + Vector3.up)) continue;
                // Falls from above, so it cannot be blocked. The native effect RPC slows on the owner after the same
                // distance re-check as the damage; a new slow replaces a running one instead of stacking.
                player.HitClientRpc(TideRules.Damage(TideRules.Shot), center, TideRules.Range(TideRules.Shot), false, EffectsController.EffectType.None);
                player.HitEffectClientRpc(EffectsController.EffectType.Slow, TideRules.ShotSlowSeconds, TideRules.ShotSlowPercent, center, TideRules.Range(TideRules.Shot), false);
            }
        }
        private void OnDestroy() { if (model != null) Destroy(model.gameObject); if (hitbox != null) Destroy(hitbox); }
    }
}
