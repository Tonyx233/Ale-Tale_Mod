using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace TonyMods
{
    public sealed class TideSummons : MonoBehaviour
    {
        // v6 adds the hunting role alongside friendly/summoner identity. Snapshots are split into ChunkSize-record parts because
        // the idol count is unbounded; strings travel as UTF-16, and tests/run-tidefork.ps1 keeps a worst-case part
        // under UnityTransport's 6144-byte payload.
        private const string Channel = "Tony.Tidefork.v6";
        private const int ChunkSize = 2, MaxParts = 1000;
        private static TideSummons instance;
        private static int petHitDepth;
        private bool friendReady;
        private ManualLogSource log;
        private Harmony patches;
        private Harmony huntPatches;
        private bool huntReady;
        private NetworkManager network;
        private bool ready, dirty;
        private float lastSend = -1, nextHello, noticeUntil;
        private int sequence, incomingSequence = -1, incomingPart;
        private readonly Dictionary<ulong, Record> incoming = new Dictionary<ulong, Record>();
        private string notice;
        private Sprite icon;
        private Texture2D iconTexture;
        private readonly Dictionary<ulong, TideCreature> creatures = new Dictionary<ulong, TideCreature>();
        private readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, double> uses = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, Record> pending = new Dictionary<ulong, Record>();
        [Serializable] public sealed class Record
        {
            public ulong id;
            public bool friendly, hunting;
            public ulong summoner;
            public string scene;
            public byte action;
            public double started, born;
            public Vector3 from, landing;
            // Current volley: shell k leaves shotFrom at shotAt + k * ShotInterval (shotAt 0 = none). shotTo/shotApex
            // grow as the host aims each shell; apex 0 marks a skipped shell. Flights follow from shotFrom-shotTo.
            public double shotAt;
            public Vector3 shotFrom;
            public Vector3[] shotTo;
            public float[] shotApex;
        }
        [Serializable] public sealed class Snapshot { public int version = 6, sequence, part, parts; public Record[] records; }
        internal static double Now { get { return NetworkManager.Singleton == null ? 0 : NetworkManager.Singleton.ServerTime.Time; } }
        internal static bool Owned(Component component) { return component != null && component.GetComponentInParent<TideCreature>() != null; }
        // 十魚架(友) on any peer (clients know it from the snapshot): player weapons never hurt it and the M4 shoots through it.
        internal static bool Friendly(Component component)
        {
            TideCreature creature = component == null ? null : component.GetComponentInParent<TideCreature>();
            return creature != null && creature.State != null && creature.State.friendly;
        }

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            instance = this; log = logger;
            patches = new Harmony("Tony.AleTaleMods.Tidefork");
            huntPatches = new Harmony("Tony.AleTaleMods.TideHunt");
            try { TideHuntBuilding.Initialize(huntPatches); huntReady = true; }
            catch (Exception ex) { huntPatches.UnpatchSelf(); log.LogError("Hunting buildings disabled: " + ex); }
            Patch(typeof(ItemManager), "Awake", "Register");
            Patch(typeof(InventoryItemUseManager), "UseInventoryItem", "UseItem");
            // Only our marked instances skip native AI/death. Native weapon/RPC health stays intact.
            foreach (string method in new[] { "OnHpChanged", "SetState" }) Patch(typeof(CreatureBase), method, "NativeCreature");
            foreach (string method in new[] { "FixedUpdate", "OnAnim", "OnAlarm", "OnHit", "OnDeath" }) Patch(typeof(CreatureHostile), method, "NativeCreature");
            Patch(typeof(Vulnerable), "OnDeath", "NativeCreature");
            Patch(typeof(SaveManager), "LoadGame", "BeforeWorld");
            Patch(typeof(SaveManager), "NewGame", "BeforeWorld");
            // Preserve hostile summons if the hook fails, but disable companions without attack attribution.
            try
            {
                PatchWeaponHits();
                patches.Patch(AccessTools.Method(typeof(PetGuard), "OnAnim"), prefix: new HarmonyMethod(typeof(TideSummons), "BeforePetHit"),
                    finalizer: new HarmonyMethod(typeof(TideSummons), "AfterPetHit"));
                friendReady = true;
                log.LogInfo("Friendly Tidefork ready: item 47941, reusable, one per player, owner-only assistance, immune to player weapons, protocol v6.");
            }
            catch (Exception ex) { log.LogWarning("Tidefork attacker detection unavailable; friendly summons disabled: " + ex.Message); }
            // Companions still work without it; native monsters just never turn on them (hostile idols still do).
            try { TideTaunt.Resolve(); log.LogInfo("Friendly Tidefork: monsters it hurts turn on it (native melee monsters and hostile Tideforks)."); }
            catch (Exception ex) { log.LogWarning("Native monsters will not attack friendly Tideforks: " + ex.Message); }
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            SceneManager.activeSceneChanged += SceneChanged;
            StartCoroutine(Localize());
            log.LogInfo("Tidefork enabled: item 47940, price 1, host-authoritative summons, no room cap or lifetime, " + TideRules.Health + " HP, water shell volleys of " + TideRules.ShotCount + " at " + TideRules.ShotMin + "-" + TideRules.ShotMax + " m, attackers hunted for " + TideRules.AggroSeconds + " s at any range.");
        }
        private void Patch(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            if (target == null) throw new MissingMethodException(type.Name, method);
            patches.Patch(target, prefix: new HarmonyMethod(typeof(TideSummons), prefix));
        }
        // Vulnerable.HitServerRpc carries no attacker, but its NGO receiver knows the sender: every player weapon (GunTool
        // incl. the M4, WeaponTool, DualSickles, PlayerProjectile) sends the 5-argument overload from the shooter's own
        // peer, and the host's own shots run the same receiver as client 0. The name is NGO's hash of that signature.
        private void PatchWeaponHits()
        {
            var handler = AccessTools.Method(typeof(Vulnerable), "__rpc_handler_3894916604");
            if (handler == null) throw new MissingMethodException("Vulnerable", "__rpc_handler_3894916604");
            patches.Patch(handler, prefix: new HarmonyMethod(typeof(TideSummons), "BeforeWeaponHit"), postfix: new HarmonyMethod(typeof(TideSummons), "AfterWeaponHit"));
        }
        // PetGuard sends from the host, but its hits must not be credited to player 0.
        private static void BeforePetHit(out int __state) { __state = petHitDepth; petHitDepth++; }
        private static void AfterPetHit(int __state) { petHitDepth = __state; }
        // Skipping the receiver drops player (and PetGuard) hits on 十魚架(友) on the host, whatever the shooter's version.
        private static bool BeforeWeaponHit(NetworkBehaviour target, out ushort __state)
        {
            var vulnerable = target as Vulnerable; __state = vulnerable == null ? (ushort)0 : vulnerable.hp.Value;
            return !Friendly(vulnerable);
        }
        // Only accepted player damage authorizes assist targets; rejected weapons/invulnerability do not.
        private static void AfterWeaponHit(NetworkBehaviour target, __RpcParams rpcParams, ushort __state)
        {
            var vulnerable = target as Vulnerable;
            if (instance == null || petHitDepth > 0 || vulnerable == null || vulnerable.hp.Value >= __state) return;
            TideCreature creature = vulnerable.GetComponent<TideCreature>();
            ulong sender = rpcParams.Server.Receive.SenderClientId;
            foreach (TideCreature ally in instance.creatures.Values)
                if (ally != null && ally.State.friendly && ally.State.summoner == sender) ally.Assist(vulnerable);
            if (creature != null && creature.Provoke(sender)) instance.log.LogInfo("Tidefork provoked: network=" + creature.State.id + "; player=" + sender);
        }
        private static bool NativeCreature(Component __instance) { return !Owned(__instance); }
        private static void BeforeWorld() { if (instance != null) instance.Clear(true); }
        private void SceneChanged(Scene oldScene, Scene newScene) { Clear(true); }
        private void Update()
        {
            NetworkManager current = NetworkManager.Singleton;
            if (current == null || !current.IsListening) { if (network != null) Disconnect(); return; }
            if (current != network)
            {
                Disconnect(); network = current;
                network.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
                lastSend = -1; nextHello = 0;
            }
            foreach (ulong id in creatures.Keys.ToArray()) if (creatures[id] == null) { creatures.Remove(id); dirty = true; }
            if (network.IsServer)
            {
                // Clients extrapolate from action start times: send action changes (at most 10/s) plus a heartbeat.
                float since = Time.unscaledTime - lastSend;
                if (lastSend < 0 || since >= .5f || dirty && since >= .1f) Broadcast();
            }
            else
            {
                if (Time.unscaledTime >= nextHello) { nextHello = Time.unscaledTime + 1; Send(NetworkManager.ServerClientId, "hello"); }
                foreach (var pair in pending.ToArray())
                {
                    NetworkObject obj;
                    if (pair.Value.scene != SceneManager.GetActiveScene().name) continue;
                    if (!network.SpawnManager.SpawnedObjects.TryGetValue(pair.Key, out obj)) continue;
                    TideCreature creature;
                    if (!creatures.TryGetValue(pair.Key, out creature))
                    {
                        creature = obj.GetComponent<TideCreature>();
                        if (creature == null) creature = obj.gameObject.AddComponent<TideCreature>();
                        try { if (creature.State == null) creature.Initialize(this, pair.Value, false); creatures.Add(pair.Key, creature); }
                        catch (Exception ex) { log.LogError("Tidefork client model failed: " + ex); Destroy(creature); pending.Remove(pair.Key); continue; }
                    }
                    creature.Apply(pair.Value);
                }
            }
        }
        private static void Register(ItemManager __instance)
        {
            if (instance == null) return;
            try { instance.RegisterItem(__instance); }
            catch (Exception ex) { instance.ready = false; instance.log.LogError("Tidefork item unavailable: " + ex); }
        }
        private void RegisterItem(ItemManager manager)
        {
            var items = manager.itemDataHub.itemData.ToList();
            ItemData item = items.FirstOrDefault(i => i != null && i.id == TideRules.ItemId);
            if (item != null && item.name != "TonyTideName") throw new InvalidOperationException("Item ID collision: 47940");
            if (item == null)
            {
                ItemData template = items.FirstOrDefault(i => i != null && i.type == ItemData.Type.Material && i.collectiblePrefab != null);
                if (template == null) throw new InvalidOperationException("No collectible template");
                item = ScriptableObject.CreateInstance<ItemData>();
                item.collectiblePrefab = template.collectiblePrefab;
                item.fpPrefab = template.fpPrefab; item.tpPrefab = template.tpPrefab; item.netPrefab = template.netPrefab;
                items.Add(item);
            }
            item.id = TideRules.ItemId; item.name = "TonyTideName"; item.itemDescription = "TonyTideDescription"; item.useDescription = "TonyTideUse";
            item.type = ItemData.Type.Material; item.price = TideRules.Price; item.maxStack = 1;
            item.shopItem = true; item.buyByOne = true; item.isInvUseable = true; item.doNotRemoveOnUse = true;
            item.hotbarItem = false; item.levelDependant = 0; item.questDependant = 0; item.hasRarity = false;
            item.quest = false; item.doNotSave = false; item.playerCantDrop = false;
            item.icon = MakeIcon();
            RegisterFriend(items, item);
            if (huntReady) TideHuntBuilding.Register(items);
            manager.itemDataHub.itemData = items.ToArray(); ready = true;
        }
        private void RegisterFriend(List<ItemData> items, ItemData template)
        {
            ItemData item = items.FirstOrDefault(i => i != null && i.id == TideFriendRules.ItemId);
            if (item != null && item.name != "TonyTideFriendName") throw new InvalidOperationException("Item ID collision: 47941");
            if (item == null)
            {
                item = UnityEngine.Object.Instantiate(template); item.id = TideFriendRules.ItemId;
                items.Add(item);
            }
            item.name = "TonyTideFriendName"; item.itemDescription = "TonyTideFriendDescription"; item.useDescription = "TonyTideFriendUse";
            item.isInvUseable = true; item.doNotRemoveOnUse = true; item.doNotSave = false;
            item.shopItem = true; item.buyByOne = true; item.price = TideRules.Price;
        }
        private Sprite MakeIcon()
        {
            if (icon != null) return icon;
            iconTexture = new Texture2D(96, 96, TextureFormat.RGBA32, false);
            var pixels = new Color[96 * 96];
            for (int y = 0; y < 96; y++) for (int x = 0; x < 96; x++)
            {
                float dx = x - 48, dy = y - 46, distance = Mathf.Sqrt(dx * dx + dy * dy);
                Color color = Color.clear;
                if (distance < 34) color = Color.Lerp(new Color(.12f, .3f, .27f), new Color(.5f, .22f, .14f), Mathf.Clamp01((x + y) / 150f));
                if (distance > 29 && distance < 34 || Math.Abs(dx) < 2 && distance < 30) color = new Color(.18f, .9f, .9f);
                if (y > 72 && y < 88 && (Math.Abs(dx) < 2 || Math.Abs(dx - (y - 73)) < 2 || Math.Abs(dx + (y - 73)) < 2)) color = new Color(.38f, .6f, .45f);
                pixels[y * 96 + x] = color;
            }
            iconTexture.SetPixels(pixels); iconTexture.Apply();
            return icon = Sprite.Create(iconTexture, new Rect(0, 0, 96, 96), new Vector2(.5f, .5f), 96);
        }
        private static bool UseItem(ContainerNet __0, uint __1, ItemData __2, ulong __3)
        {
            if (__2 == null || (__2.id != TideRules.ItemId && __2.id != TideFriendRules.ItemId) || instance == null) return true;
            try { instance.Throw(__0, __1, __3); }
            catch (Exception ex) { instance.log.LogError("Tidefork throw failed: " + ex); instance.Note(__3, "召喚失敗，請查看 mod log。"); }
            return false;
        }
        private void Throw(ContainerNet container, uint itemId, ulong sender)
        {
            if (!ready || network == null || !network.IsServer || PlayerManager.Instance == null || SpawnManager.Instance == null) return;
            if (Master.Instance != null && Master.Instance.HasConnectingClients())
            { Note(sender, "有玩家正在載入房間，請載入完成後再召喚；道具未消耗。"); return; }
            PlayerNet player; ContainerNet owned; Item item;
            if (!PlayerManager.Instance.players.TryGetValue(sender, out player) || player == null ||
                !ContainerManager.Instance.GetPlayerContainer(sender, out owned) || !container.GetItemById(itemId, out item, true)) return;
            bool friendly = item.dataId == TideFriendRules.ItemId;
            if (friendly && !friendReady) { Note(sender, "友軍功能未通過相容性檢查，請查看 mod log。"); return; }
            double last; if (!uses.TryGetValue(sender, out last)) last = Now - 2;
            if (!TideRules.CanUse(true, player.IsSpawned && player.hp.Value > 0, container == owned, friendly ? TideRules.ItemId : item.dataId, item.amount, Now - last))
            { Note(sender, "無法召喚：每次召喚需間隔 1 秒。"); return; }
            if (friendly)
            {
                TideCreature existing = creatures.Values.FirstOrDefault(c => c != null && c.State.friendly && !c.State.hunting && c.State.summoner == sender);
                if (existing != null)
                {
                    bool alive = existing.GetComponent<Vulnerable>().hp.Value > 0;
                    Remove(existing);
                    if (alive) { uses[sender] = Now; Broadcast(); Note(sender, "已收回十魚架(友)，道具保留。"); return; }
                }
            }
            foreach (ulong peer in network.ConnectedClientsIds)
            {
                float seen;
                if (peer != network.LocalClientId && (!peers.TryGetValue(peer, out seen) || Time.unscaledTime - seen > 5))
                { Note(sender, "所有玩家需安裝支援十魚架的版本；剛加入時請稍候。"); return; }
            }
            Vector3 origin = player.transform.position + Vector3.up * 1.3f;
            Vector3 direction = Quaternion.Euler(0, player.hAngleNet.Value, 0) * Vector3.forward;
            Vector3 ground;
            if (!Landing(player.transform.position, origin, direction, out ground))
            { Note(sender, "前方 3–6 公尺需要可行走、無遮擋的空地；道具未消耗。"); return; }
            Spawnable spawn = null;
            bool committed = false;
            try
            {
                // Reuse a registered native network prefab; never add/reorder NetworkBehaviours.
                if (!SpawnManager.Instance.ManualSpawn(Spawnable.Type.Spider, ground, Quaternion.LookRotation(direction), out spawn, true) || spawn == null)
                { Note(sender, "怪物生成失敗，道具未消耗。"); return; }
                if (!spawn.IsSpawned) throw new InvalidOperationException("World is still spawning joining players; retry after joining completes");
                var record = new Record { id = spawn.NetworkObjectId, scene = SceneManager.GetActiveScene().name, action = TideRules.Summon,
                    started = Now, born = Now, from = origin, landing = ground, friendly = friendly, summoner = sender };
                TideCreature creature = spawn.gameObject.AddComponent<TideCreature>();
                creature.Initialize(this, record, true);
                creatures.Add(record.id, creature);
                if (!friendly && !container.RemoveItemAmount(itemId, 1)) throw new InvalidOperationException("Item consumption rejected");
                committed = true; uses[sender] = Now;
                log.LogInfo("Tidefork summoned: network=" + record.id + "; player=" + sender + "; active=" + creatures.Count);
                Broadcast(); Note(sender, friendly ? "已召喚十魚架(友)。再次使用道具可收回。" : "已投出十魚架球。十魚架會攻擊所有玩家，包含召喚者！");
            }
            catch
            {
                if (!committed && spawn != null)
                {
                    if (spawn.IsSpawned) creatures.Remove(spawn.NetworkObjectId);
                    SpawnManager.Instance.RemoveById(spawn.id.Value, true);
                }
                throw;
            }
        }
        internal static bool Landing(Vector3 feet, Vector3 origin, Vector3 direction, out Vector3 ground)
        {
            foreach (float distance in TideRules.ThrowDistances)
                if (LandingAt(feet + direction * distance, origin, out ground)) return true;
            ground = Vector3.zero;
            return false;
        }
        private static bool LandingAt(Vector3 desired, Vector3 origin, out Vector3 ground)
        {
            ground = Vector3.zero;
            RaycastHit hit; NavMeshHit nav;
            if (!Physics.Raycast(desired + Vector3.up * 3, Vector3.down, out hit, 7, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                Vector3.Angle(hit.normal, Vector3.up) > 35 || !NavMesh.SamplePosition(hit.point, out nav, .6f, NavMesh.AllAreas) ||
                Math.Abs(nav.position.y - hit.point.y) > .5f) return false;
            ground = nav.position;
            // Door-height clearance: the idol walks under the same ceilings after it spawns anyway.
            if (Physics.CheckCapsule(ground + Vector3.up * .6f, ground + Vector3.up * 1.85f, .45f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
            Vector3 previous = origin;
            for (int i = 1; i <= 16; i++)
            {
                Vector3 next = Arc(origin, ground + Vector3.up * .16f, i / 16f);
                if (Physics.Linecast(previous, next, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
                previous = next;
            }
            return true;
        }
        internal static float Flat(Vector3 offset) { offset.y = 0; return offset.magnitude; }
        // Host and clients derive each shell's flight from the same two synced points.
        internal static float Flight(Vector3 from, Vector3 to) { return TideRules.ShotFlight(Flat(to - from)); }
        internal static Vector3 Arc(Vector3 from, Vector3 to, float phase) { return Arc(from, to, phase, 1.1f); }
        internal static Vector3 Arc(Vector3 from, Vector3 to, float phase, float apex)
        { return Vector3.Lerp(from, to, phase) + Vector3.up * TideRules.ArcLift(phase, apex); }
        private void Send(ulong id, string message)
        {
            if (network == null || !network.IsListening) return;
            using (var writer = new FastBufferWriter(16384, Allocator.Temp))
            { writer.WriteValueSafe(message); network.CustomMessagingManager.SendNamedMessage(Channel, id, writer, NetworkDelivery.ReliableFragmentedSequenced); }
        }
        private void Broadcast()
        {
            if (network == null || !network.IsServer) return;
            lastSend = Time.unscaledTime; dirty = false;
            ulong[] targets = network.ConnectedClientsIds.Where(id => id != network.LocalClientId && peers.ContainsKey(id)).ToArray();
            if (targets.Length == 0) return;
            // Chunks stay a few KB so no packet exceeds the transport's max payload, however many idols exist.
            Record[] records = creatures.Values.Where(c => c != null).Select(c => c.State).ToArray();
            int parts = Math.Max(1, (records.Length + ChunkSize - 1) / ChunkSize);
            sequence = sequence == int.MaxValue ? 0 : sequence + 1;
            for (int part = 0; part < parts; part++)
            {
                string json = HorseJson.Serialize(new Snapshot { sequence = sequence, part = part, parts = parts,
                    records = records.Skip(part * ChunkSize).Take(ChunkSize).ToArray() });
                foreach (ulong id in targets) Send(id, json);
            }
        }
        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (reader.Length > 16384) return;
                string message; reader.ReadValueSafe(out message, false);
                if (network.IsServer)
                { if (message == "hello" && network.ConnectedClientsIds.Contains(sender)) peers[sender] = Time.unscaledTime; return; }
                if (sender != NetworkManager.ServerClientId) return;
                if (message.StartsWith("note:", StringComparison.Ordinal)) { Tell(message.Substring(5)); return; }
                Snapshot snapshot = HorseJson.Deserialize<Snapshot>(message);
                if (!Valid(snapshot)) return;
                Accept(snapshot);
            }
            catch (Exception ex) { log.LogWarning("Tidefork packet rejected: " + ex.Message); }
        }
        // Parts arrive in order (ReliableFragmentedSequenced); apply only a complete, consistent set.
        private void Accept(Snapshot snapshot)
        {
            if (snapshot.part == 0) { incoming.Clear(); incomingSequence = snapshot.sequence; incomingPart = 0; }
            if (snapshot.sequence != incomingSequence || snapshot.part != incomingPart) { incomingSequence = -1; return; }
            foreach (Record record in snapshot.records)
                if (incoming.ContainsKey(record.id)) { incomingSequence = -1; return; } else incoming.Add(record.id, record);
            incomingPart++;
            if (incomingPart < snapshot.parts) return;
            pending.Clear(); foreach (var pair in incoming) pending.Add(pair.Key, pair.Value);
            incoming.Clear(); incomingSequence = -1;
            foreach (ulong id in creatures.Keys.ToArray()) if (!pending.ContainsKey(id))
            { if (creatures[id] != null) creatures[id].Hide(); creatures.Remove(id); }
        }
        private static bool Valid(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.version != 6 || snapshot.records == null || snapshot.records.Length > ChunkSize ||
                snapshot.parts < 1 || snapshot.parts > MaxParts || snapshot.part < 0 || snapshot.part >= snapshot.parts) return false;
            var ids = new HashSet<ulong>();
            double now = Now;
            foreach (Record r in snapshot.records)
            {
                int shells = r == null || r.shotTo == null ? 0 : r.shotTo.Length;
                if (r == null || r.hunting && !r.friendly || !ids.Add(r.id) || r.action > TideRules.Shot || String.IsNullOrEmpty(r.scene) || r.scene.Length > TideRules.SceneNameMax ||
                    !TideRules.Finite(r.started) || !TideRules.Finite(r.born) || r.started < r.born || r.started > now + 5 ||
                    !TideRules.ValidVolley(r.shotAt, shells, r.born, now) || (r.shotApex == null ? 0 : r.shotApex.Length) != shells) return false;
                var points = new List<Vector3> { r.from, r.landing, r.shotFrom };
                for (int shell = 0; shell < shells; shell++) { if (!TideRules.ValidApex(r.shotApex[shell])) return false; points.Add(r.shotTo[shell]); }
                foreach (Vector3 p in points)
                    if (!TideRules.Finite(p.x) || !TideRules.Finite(p.y) || !TideRules.Finite(p.z) || Math.Abs(p.x) > 100000 || Math.Abs(p.y) > 100000 || Math.Abs(p.z) > 100000) return false;
            }
            return true;
        }
        internal static TideCreature SpawnHunter(TideHuntHome home, Vector3 ground)
        {
            var self = instance;
            if (self == null || self.network == null || !self.network.IsServer || !self.friendReady || !self.huntReady)
            { home.Report("等待房主狩獵模組就緒"); return null; }
            foreach (ulong peer in self.network.ConnectedClientsIds)
            {
                float seen;
                if (peer != self.network.LocalClientId && (!self.peers.TryGetValue(peer, out seen) || Time.unscaledTime - seen > 5))
                { home.Report("等待所有玩家更新並完成同步"); return null; }
            }
            Spawnable spawn = null;
            try
            {
                if (!SpawnManager.Instance.ManualSpawn(Spawnable.Type.Spider, ground, Quaternion.identity, out spawn, true) || spawn == null)
                { home.Report("原版生成器暫時無法生成，稍後重試"); return null; }
                if (!spawn.IsSpawned) throw new InvalidOperationException("Hunt spawn deferred");
                var record = new Record { id = spawn.NetworkObjectId, scene = SceneManager.GetActiveScene().name,
                    friendly = true, hunting = true, action = TideRules.Summon, started = Now, born = Now, from = ground + Vector3.up, landing = ground };
                var creature = spawn.gameObject.AddComponent<TideCreature>();
                creature.Initialize(self, record, true); creature.SetHunter(home);
                self.creatures.Add(record.id, creature); self.Changed();
                home.Report("十魚架已生成");
                return creature;
            }
            catch
            {
                if (spawn != null) SpawnManager.Instance.RemoveById(spawn.id.Value, true);
                throw;
            }
        }
        internal static void RemoveHunter(TideCreature creature)
        { if (instance != null && instance.network != null && instance.network.IsServer) instance.Remove(creature); }
        internal void Changed() { dirty = true; }
        // Host: companions that monster attacks can land on (TideCreature.Strike/Splash).
        internal List<TideCreature> Companions()
        {
            var companions = new List<TideCreature>();
            foreach (TideCreature creature in creatures.Values)
                if (creature != null && creature.State != null && creature.State.friendly && creature.Standing) companions.Add(creature);
            return companions;
        }
        internal void Remove(TideCreature creature)
        {
            creature.Hide(); creatures.Remove(creature.State.id); Changed();
            Spawnable spawn = creature.GetComponent<Spawnable>();
            if (spawn != null && SpawnManager.Instance != null) SpawnManager.Instance.RemoveById(spawn.id.Value, true);
        }
        private void Clear(bool despawn)
        {
            foreach (TideCreature creature in creatures.Values.ToArray()) if (creature != null)
            { if (despawn && network != null && network.IsServer) Remove(creature); else creature.Hide(); }
            creatures.Clear(); pending.Clear(); uses.Clear(); incoming.Clear(); incomingSequence = -1;
        }
        private void Disconnect()
        {
            Clear(false); peers.Clear();
            if (network != null && network.CustomMessagingManager != null) network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            network = null;
        }
        private void Note(ulong id, string text) { if (network != null && id != network.LocalClientId) Send(id, "note:" + text); else Tell(text); }
        private void Tell(string text) { notice = text; noticeUntil = Time.unscaledTime + 6; }
        private void OnGUI() { if (Time.unscaledTime < noticeUntil) GUI.Box(new Rect(Screen.width / 2 - 360, Screen.height - 170, 720, 42), notice); }
        private void LocaleChanged(Locale locale) { StartCoroutine(Localize()); }
        private IEnumerator Localize()
        {
            yield return LocalizationSettings.InitializationOperation;
            var handle = LocalizationSettings.StringDatabase.GetTableAsync("ItemData"); yield return handle;
            if (handle.Result == null) yield break;
            bool zh = LocalizationSettings.SelectedLocale != null && LocalizationSettings.SelectedLocale.Identifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            string[] keys = { "TonyTideName", "TonyTideDescription", "TonyTideUse", "TonyTideFriendName", "TonyTideFriendDescription", "TonyTideFriendUse", "TonyTideHuntName", "TonyTideHuntDescription" };
            // Named after the source sculpture; name and caption read the same in every locale.
            string[] values = { "十魚架球", "十魚架\n天野 裕夫\n平成元年3月", zh ? "投擲召喚十魚架" : "Throw to summon 十魚架",
                "十魚架(友)", "跟隨召喚者，只攻擊威脅主人或被主人攻擊的怪物。每人限一隻；再次使用收回。道具不消耗，死亡後可重新召喚滿血個體。", "召喚／收回十魚架(友)",
                "十魚架(狩獵)", "狩獵基地：隨機狩獵基地100公尺內的自然野生動物與普通怪物。30件戰利品後返回酒館招牌外側放下，繼續出勤。長按啟用／召回；排除任務怪、家畜、NPC與Boss。" };
            for (int i = 0; i < keys.Length; i++) { var entry = handle.Result.GetEntry(keys[i]); if (entry == null) handle.Result.AddEntry(keys[i], values[i]); else entry.Value = values[i]; }
            var titles = LocalizationSettings.StringDatabase.GetTableAsync("Interactive"); yield return titles;
            if (titles.Result != null)
            {
                string[] huntKeys = { "TonyTideHuntName", "TonyTideHuntStart", "TonyTideHuntStop" };
                string[] huntText = { "十魚架(狩獵)", "開始狩獵", "停止狩獵並返回交貨" };
                for (int i = 0; i < huntKeys.Length; i++)
                { var entry = titles.Result.GetEntry(huntKeys[i]); if (entry == null) titles.Result.AddEntry(huntKeys[i], huntText[i]); else entry.Value = huntText[i]; }
            }
            if (titles.Result != null)
            { var entry = titles.Result.GetEntry("TonyTideTitle"); const string title = "十魚架"; if (entry == null) titles.Result.AddEntry("TonyTideTitle", title); else entry.Value = title;
                entry = titles.Result.GetEntry("TonyTideFriendTitle");
                if (entry == null) titles.Result.AddEntry("TonyTideFriendTitle", "十魚架(友)"); else entry.Value = "十魚架(友)";
            }
        }
        private void OnDestroy()
        {
            Clear(true); Disconnect();
            if (patches != null) patches.UnpatchSelf();
            if (huntPatches != null) huntPatches.UnpatchSelf();
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged; SceneManager.activeSceneChanged -= SceneChanged;
            if (icon != null) Destroy(icon); if (iconTexture != null) Destroy(iconTexture);
            if (instance == this) instance = null;
        }
    }
}
