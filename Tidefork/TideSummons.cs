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
        private const string Channel = "Tony.Tidefork.v1";
        private static TideSummons instance;
        private ManualLogSource log;
        private Harmony patches;
        private NetworkManager network;
        private bool ready;
        private float nextSend, nextHello, noticeUntil;
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
            public string scene;
            public byte action;
            public double started, born;
            public Vector3 from, landing;
        }
        [Serializable] public sealed class Snapshot { public int version = 1; public Record[] records; }
        internal static double Now { get { return NetworkManager.Singleton == null ? 0 : NetworkManager.Singleton.ServerTime.Time; } }
        internal static bool Owned(Component component) { return component != null && component.GetComponentInParent<TideCreature>() != null; }

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            instance = this; log = logger;
            patches = new Harmony("Tony.AleTaleMods.Tidefork");
            Patch(typeof(ItemManager), "Awake", "Register");
            Patch(typeof(InventoryItemUseManager), "UseInventoryItem", "UseItem");
            // Only our marked instances skip native AI/death. Native weapon/RPC health stays intact.
            foreach (string method in new[] { "OnHpChanged", "SetState" }) Patch(typeof(CreatureBase), method, "NativeCreature");
            foreach (string method in new[] { "FixedUpdate", "OnAnim", "OnAlarm", "OnHit", "OnDeath" }) Patch(typeof(CreatureHostile), method, "NativeCreature");
            Patch(typeof(Vulnerable), "OnDeath", "NativeCreature");
            Patch(typeof(SaveManager), "LoadGame", "BeforeWorld");
            Patch(typeof(SaveManager), "NewGame", "BeforeWorld");
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            SceneManager.activeSceneChanged += SceneChanged;
            StartCoroutine(Localize());
            log.LogInfo("Tidefork enabled: item 47940, price 1, host-authoritative summons, 8 maximum, 120 seconds.");
        }
        private void Patch(Type type, string method, string prefix)
        {
            var target = AccessTools.Method(type, method);
            if (target == null) throw new MissingMethodException(type.Name, method);
            patches.Patch(target, prefix: new HarmonyMethod(typeof(TideSummons), prefix));
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
                nextSend = nextHello = 0;
            }
            foreach (ulong id in creatures.Keys.ToArray()) if (creatures[id] == null) creatures.Remove(id);
            if (network.IsServer)
            {
                // Clients extrapolate from action start times; send on every action change plus a heartbeat.
                if (Time.unscaledTime >= nextSend) { nextSend = Time.unscaledTime + .5f; Broadcast(); }
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
            manager.itemDataHub.itemData = items.ToArray(); ready = true;
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
            if (__2 == null || __2.id != TideRules.ItemId || instance == null) return true;
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
            double last; if (!uses.TryGetValue(sender, out last)) last = Now - 2;
            if (!TideRules.CanUse(true, player.IsSpawned && player.hp.Value > 0, container == owned, item.dataId, item.amount, creatures.Count, Now - last))
            { Note(sender, "無法召喚：請稍候，或等待現有叉潮像消失（全場最多 8 隻）。"); return; }
            foreach (ulong peer in network.ConnectedClientsIds)
            {
                float seen;
                if (peer != network.LocalClientId && (!peers.TryGetValue(peer, out seen) || Time.unscaledTime - seen > 5))
                { Note(sender, "所有玩家需安裝支援叉潮像的版本；剛加入時請稍候。"); return; }
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
                    started = Now, born = Now, from = origin, landing = ground };
                TideCreature creature = spawn.gameObject.AddComponent<TideCreature>();
                creature.Initialize(this, record, true);
                creatures.Add(record.id, creature);
                if (!container.RemoveItemAmount(itemId, 1)) throw new InvalidOperationException("Item consumption rejected");
                committed = true; uses[sender] = Now;
                log.LogInfo("Tidefork summoned: network=" + record.id + "; player=" + sender + "; active=" + creatures.Count);
                Broadcast(); Note(sender, "已投出叉潮封印球。叉潮像會攻擊所有玩家，包含召喚者！");
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
        internal static Vector3 Arc(Vector3 from, Vector3 to, float phase)
        { return Vector3.Lerp(from, to, phase) + Vector3.up * (4 * phase * (1 - phase) * 1.1f); }
        private void Send(ulong id, string message)
        {
            if (network == null || !network.IsListening) return;
            using (var writer = new FastBufferWriter(16384, Allocator.Temp))
            { writer.WriteValueSafe(message); network.CustomMessagingManager.SendNamedMessage(Channel, id, writer, NetworkDelivery.ReliableFragmentedSequenced); }
        }
        private void Broadcast()
        {
            if (network == null || !network.IsServer) return;
            string json = HorseJson.Serialize(new Snapshot { records = creatures.Values.Where(c => c != null).Select(c => c.State).ToArray() });
            foreach (ulong id in network.ConnectedClientsIds) if (id != network.LocalClientId && peers.ContainsKey(id)) Send(id, json);
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
                pending.Clear(); foreach (Record record in snapshot.records) pending.Add(record.id, record);
                foreach (ulong id in creatures.Keys.ToArray()) if (!pending.ContainsKey(id))
                { if (creatures[id] != null) creatures[id].Hide(); creatures.Remove(id); }
            }
            catch (Exception ex) { log.LogWarning("Tidefork packet rejected: " + ex.Message); }
        }
        private static bool Valid(Snapshot snapshot)
        {
            if (snapshot == null || snapshot.version != 1 || snapshot.records == null || snapshot.records.Length > TideRules.Limit) return false;
            var ids = new HashSet<ulong>();
            foreach (Record r in snapshot.records)
            {
                if (r == null || !ids.Add(r.id) || r.action > TideRules.Death || String.IsNullOrEmpty(r.scene) || r.scene.Length > 128 ||
                    !TideRules.Finite(r.started) || !TideRules.Finite(r.born) || r.started < r.born || r.started > Now + 5 || Now - r.born > 140) return false;
                foreach (float v in new[] { r.from.x, r.from.y, r.from.z, r.landing.x, r.landing.y, r.landing.z })
                    if (!TideRules.Finite(v) || Math.Abs(v) > 100000) return false;
            }
            return true;
        }
        internal void Changed() { nextSend = 0; }
        internal void Remove(TideCreature creature)
        {
            creatures.Remove(creature.State.id); Changed();
            Spawnable spawn = creature.GetComponent<Spawnable>();
            if (spawn != null && SpawnManager.Instance != null) SpawnManager.Instance.RemoveById(spawn.id.Value, true);
        }
        private void Clear(bool despawn)
        {
            foreach (TideCreature creature in creatures.Values.ToArray()) if (creature != null)
            { if (despawn && network != null && network.IsServer) Remove(creature); else creature.Hide(); }
            creatures.Clear(); pending.Clear(); uses.Clear();
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
            string[] keys = { "TonyTideName", "TonyTideDescription", "TonyTideUse" };
            string[] values = zh ? new[] { "叉潮封印球", "向前投出封印球，召喚會攻擊所有玩家（包含自己）的叉潮像。可擊殺；120 秒消失；全場最多 8 隻。所有玩家需安裝相同版本。", "投擲召喚叉潮像" } :
                new[] { "Tidefork Seal", "Throw forward to summon a hostile Tidefork Idol. Attacks everyone, including you. Killable; lasts 120 seconds; room limit 8. All players need the same mod version.", "Throw and summon Tidefork Idol" };
            for (int i = 0; i < keys.Length; i++) { var entry = handle.Result.GetEntry(keys[i]); if (entry == null) handle.Result.AddEntry(keys[i], values[i]); else entry.Value = values[i]; }
            var titles = LocalizationSettings.StringDatabase.GetTableAsync("Interactive"); yield return titles;
            if (titles.Result != null)
            { var entry = titles.Result.GetEntry("TonyTideTitle"); string title = zh ? "叉潮像" : "Tidefork Idol"; if (entry == null) titles.Result.AddEntry("TonyTideTitle", title); else entry.Value = title; }
        }
        private void OnDestroy()
        {
            Clear(true); Disconnect();
            if (patches != null) patches.UnpatchSelf();
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged; SceneManager.activeSceneChanged -= SceneChanged;
            if (icon != null) Destroy(icon); if (iconTexture != null) Destroy(iconTexture);
            if (instance == this) instance = null;
        }
    }
}
