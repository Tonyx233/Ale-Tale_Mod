using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.SceneManagement;

namespace TonyMods
{
    public sealed class HorseStable : MonoBehaviour
    {
        private const string Channel = "Tony.HorseStable.v050";
        public const ushort ItemId = 47920;
        private static HorseStable instance;
        private ConfigFile config;
        private ManualLogSource log;
        private Harmony patches;
        private ConfigEntry<int> price;
        private ConfigEntry<bool> enabledSetting;
        private NetworkManager network;
        private SaveManager subscribedSave;
        private readonly Dictionary<string, TavernHorse> horses = new Dictionary<string, TavernHorse>();
        private readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> lastUse = new Dictionary<ulong, float>();
        private string loadKey;
        private bool itemReady;
        private bool loaded;
        private float nextManifest, noticeUntil, nextSyncWarning;
        private bool receivedManifest;
        private string notice;
        private Sprite icon;
        private Texture2D iconTexture;
        [Serializable] public sealed class Record { public string id, scene; public Vector3 position; public float yaw; }
        [Serializable] public sealed class Snapshot { public int version = 1; public Record[] horses; }

        public void Initialize(ConfigFile cfg, ManualLogSource logger)
        {
            instance = this; config = cfg; log = logger;
            enabledSetting = cfg.Bind("Horse", "Enabled", true, "Enable purchasable two-seat horses.");
            price = cfg.Bind("Horse", "Price", 1, new ConfigDescription("Merchant price in gold.", new AcceptableValueRange<int>(1, 60000)));
            patches = new Harmony("Tony.AleTaleMods.Horse");
            TavernHorse.InstallPatches(patches);
            patches.Patch(AccessTools.Method(typeof(ItemManager), "Awake"), prefix: new HarmonyMethod(typeof(HorseStable), "RegisterItem"));
            patches.Patch(AccessTools.Method(typeof(InventoryItemUseManager), "UseInventoryItem"), prefix: new HarmonyMethod(typeof(HorseStable), "UseItem"));
            patches.Patch(AccessTools.Method(typeof(SaveManager), "LoadGame"), postfix: new HarmonyMethod(typeof(HorseStable), "AfterLoad"));
            patches.Patch(AccessTools.Method(typeof(SaveManager), "NewGame"), prefix: new HarmonyMethod(typeof(HorseStable), "BeforeNewGame"));
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            StartCoroutine(Localize());
            log.LogInfo("Tony horses 0.5.0: merchant item 47920; inventory use places horse; E mount; Ctrl+F1/F2 switch seats. F6 cart removed.");
        }
        private void Update()
        {
            if (subscribedSave != SaveManager.Instance)
            {
                if (subscribedSave != null) subscribedSave.onGameSaved -= Saved;
                subscribedSave = SaveManager.Instance;
                if (subscribedSave != null) subscribedSave.onGameSaved += Saved;
            }
            NetworkManager net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) { if (network != null) Disconnect(); return; }
            if (network != net)
            {
                Disconnect(); network = net;
                network.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
                nextManifest = 0; nextSyncWarning = Time.unscaledTime + 10; receivedManifest = false;
                log.LogInfo("Horse sync 0.5.3 bound; server=" + network.IsServer);
            }
            if (network.IsServer && !loaded && PlayerNet.Instance != null && PlayerNet.Instance.IsSpawned)
            {
                loaded = true; RestoreSnapshot();
            }
            if (PlayerManager.Instance != null)
                foreach (PlayerNet player in PlayerManager.Instance.players.Values)
                {
                    if (player == null) continue;
                    TavernHorse riddenHorse = player.hp.Value > 0 ? horses.Values.FirstOrDefault(h => h.HasRider(player.OwnerClientId)) : null;
                    bool riding = riddenHorse != null;
                    var pose = player.GetComponent<HorseRiderPose>();
                    if (riding && pose == null) pose = player.gameObject.AddComponent<HorseRiderPose>();
                    if (pose != null) { pose.Riding = riding; pose.Horse = riddenHorse; }
                }
            if (!network.IsServer && !receivedManifest && Time.unscaledTime >= nextSyncWarning)
            {
                nextSyncWarning = Time.unscaledTime + 30;
                log.LogWarning("Horse sync: no valid manifest from host; ensure both players use 0.5.3.");
            }
            if (Time.unscaledTime >= nextManifest)
            {
                nextManifest = Time.unscaledTime + 1;
                if (network.IsServer) Broadcast(); else Send(NetworkManager.ServerClientId, "hello");
            }
        }
        private void Send(ulong id, string json)
        {
            using (var writer = new FastBufferWriter(32768, Allocator.Temp))
            { writer.WriteValueSafe(json); network.CustomMessagingManager.SendNamedMessage(Channel, id, writer, NetworkDelivery.ReliableFragmentedSequenced); }
        }
        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                if (reader.Length > 32768) return;
                string json; reader.ReadValueSafe(out json, false);
                if (network.IsServer)
                {
                    if (json == "hello" && network.ConnectedClientsIds.Contains(sender))
                    {
                        if (!peers.ContainsKey(sender)) log.LogInfo("Horse sync: peer handshake " + sender);
                        peers[sender] = Time.unscaledTime;
                    }
                    return;
                }
                if (sender != NetworkManager.ServerClientId) return;
                if (json.StartsWith("note:", StringComparison.Ordinal)) { Tell(json.Substring(5)); return; }
                Snapshot snapshot = HorseJson.Deserialize<Snapshot>(json);
                if (!Valid(snapshot))
                {
                    if (Time.unscaledTime >= nextSyncWarning) { nextSyncWarning = Time.unscaledTime + 30; log.LogWarning("Horse sync: invalid manifest from host; ensure matching 0.5.3 DLLs."); }
                    return;
                }
                foreach (Record record in snapshot.horses) if (!horses.ContainsKey(record.id)) Spawn(record);
                if (!receivedManifest) log.LogInfo("Horse sync: first valid manifest; horses=" + snapshot.horses.Length);
                receivedManifest = true;
                var keep = new HashSet<string>(snapshot.horses.Select(h => h.id));
                foreach (string id in horses.Keys.ToArray()) if (!keep.Contains(id)) { Destroy(horses[id].gameObject); horses.Remove(id); }
            }
            catch (Exception ex) { log.LogWarning("Horse manifest rejected: " + ex.Message); }
        }
        private void Broadcast()
        {
            string json = HorseJson.Serialize(Capture());
            foreach (ulong id in network.ConnectedClientsIds)
            {
                float at;
                if (id != network.LocalClientId && peers.TryGetValue(id, out at) && Time.unscaledTime - at < 5) Send(id, json);
            }
        }
        private Snapshot Capture()
        {
            return new Snapshot { horses = horses.Values.Select(h => new Record { id = h.Id, scene = h.SceneName, position = h.Position, yaw = h.Yaw }).OrderBy(h => h.id, StringComparer.Ordinal).ToArray() };
        }
        private static bool Valid(Snapshot s)
        {
            if (s == null || s.version != 1 || s.horses == null || s.horses.Length > 32) return false;
            var ids = new HashSet<string>();
            foreach (Record r in s.horses)
            {
                Guid id;
                if (r == null || !Guid.TryParseExact(r.id,"N",out id) || !ids.Add(r.id) || String.IsNullOrEmpty(r.scene) || r.scene.Length > 128 ||
                    !Finite(r.position.x) || !Finite(r.position.y) || !Finite(r.position.z) || !Finite(r.yaw)) return false;
            }
            return true;
        }
        private static bool Finite(float n) { return !Single.IsNaN(n) && !Single.IsInfinity(n) && Math.Abs(n) < 100000; }
        private void Spawn(Record r)
        {
            GameObject go = new GameObject("Tony Horse " + r.id); go.transform.SetParent(transform, false);
            try
            {
                TavernHorse h = go.AddComponent<TavernHorse>(); h.Initialize(config, log, network, r.id, r.scene, r.position, r.yaw);
                horses.Add(r.id, h);
            }
            catch { Destroy(go); throw; }
        }
        private void Disconnect()
        {
            if (network != null && network.CustomMessagingManager != null) network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            ClearHorses();
            horses.Clear(); peers.Clear(); lastUse.Clear(); loaded = false; network = null;
        }
        private void ClearHorses()
        {
            if (PlayerManager.Instance != null)
                foreach (var player in PlayerManager.Instance.players.Values)
                    if (player != null) { var pose = player.GetComponent<HorseRiderPose>(); if (pose != null) pose.Riding = false; }
            foreach (var h in horses.Values) if (h != null) Destroy(h.gameObject);
            horses.Clear();
        }
        private void Tell(string text) { notice = text; noticeUntil = Time.unscaledTime + 6; }
        private void Note(ulong id, string text)
        { if (id == network.LocalClientId) Tell(text); else Send(id, "note:" + text); }
        private void OnGUI()
        { if (Time.unscaledTime < noticeUntil) GUI.Box(new Rect(Screen.width/2-300, Screen.height-220,600,38),notice); }

        private static void RegisterItem(ItemManager __instance)
        {
            if (instance == null) return;
            ItemData[] items = __instance.itemDataHub.itemData;
            ItemData existing = items.FirstOrDefault(i => i != null && i.id == ItemId);
            if (existing != null)
            {
                if (existing.name != "TonyHorseName") { instance.itemReady = false; instance.log.LogError("Horse item ID collision: 47920; horse item disabled."); return; }
                existing.price = (ushort)instance.price.Value; instance.itemReady = true; return;
            }
            ItemData template = items.FirstOrDefault(i => i != null && i.isInvUseable && i.collectiblePrefab != null && i.type == ItemData.Type.Material);
            if (template == null) template = items.FirstOrDefault(i => i != null && i.collectiblePrefab != null && i.type == ItemData.Type.Material);
            if (template == null) { instance.log.LogError("Horse item registration failed: no collectible template."); return; }
            ItemData item = ScriptableObject.CreateInstance<ItemData>();
            item.id = ItemId; item.name = "TonyHorseName"; item.itemDescription = "TonyHorseDescription";
            item.useDescription = "TonyHorseUse"; item.type = ItemData.Type.Material;
            item.maxStack = 1; item.price = (ushort)instance.price.Value; item.shopItem = true; item.buyByOne = true;
            item.isInvUseable = true; item.doNotRemoveOnUse = true; item.hotbarItem = false;
            item.icon = instance.MakeIcon(); item.collectiblePrefab = template.collectiblePrefab;
            item.fpPrefab = template.fpPrefab; item.tpPrefab = template.tpPrefab; item.netPrefab = template.netPrefab;
            __instance.itemDataHub.itemData = items.Concat(new[] { item }).ToArray(); instance.itemReady = true;
        }
        // Run at the native server-side inventory-use entry, not the generated RPC wrapper.
        private static bool UseItem(ContainerNet __0, uint __1, ItemData __2, ulong __3)
        {
            if (__2 == null || __2.id != ItemId || instance == null || !instance.itemReady) return true;
            if (instance != null) instance.PlaceFromInventory(__0, __1, __3);
            return false;
        }
        private void PlaceFromInventory(ContainerNet container, uint itemId, ulong sender)
        {
            if (network == null || !network.IsServer || !enabledSetting.Value) return;
            PlayerNet player;
            ContainerNet owned; Item item;
            if (!PlayerManager.Instance.players.TryGetValue(sender,out player) || player == null || player.hp.Value <= 0 ||
                !ContainerManager.Instance.GetPlayerContainer(sender,out owned) || owned != container || !container.GetItemById(itemId,out item,true) || item.dataId != ItemId || item.amount < 1) return;
            float last;
            if (lastUse.TryGetValue(sender,out last) && Time.unscaledTime-last < .5f) return;
            lastUse[sender] = Time.unscaledTime;
            if (horses.Count >= 32) { Note(sender,"Stable limit reached (32 horses). Item was not consumed."); return; }
            foreach (var h in horses.Values) if (h.HasRider(sender)) { Note(sender,"Dismount before placing another horse."); return; }
            Vector3 ground;
            if (!FindGround(player, out ground)) { Note(sender,"Use the horse item in a clear outdoor area. Item was not consumed."); return; }
            Record record = new Record { id = Guid.NewGuid().ToString("N"), scene = SceneManager.GetActiveScene().name, position = ground, yaw = player.transform.eulerAngles.y };
            try { Spawn(record); }
            catch (Exception ex) { log.LogError("Horse creation failed; item retained: " + ex); return; }
            if (!container.RemoveItemAmount(itemId,1)) { Destroy(horses[record.id].gameObject); horses.Remove(record.id); return; }
            Note(sender,"Horse placed. E mounts; Ctrl+F1/F2 changes seats."); Broadcast();
        }
        private static bool FindGround(PlayerNet player, out Vector3 ground)
        {
            ground = Vector3.zero;
            Vector3 near = player.transform.position + Vector3.ProjectOnPlane(player.transform.forward,Vector3.up).normalized * 3.5f;
            RaycastHit hit;
            if (!Physics.Raycast(near+Vector3.up*2,Vector3.down,out hit,5,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || Vector3.Angle(hit.normal,Vector3.up)>25) return false;
            ground=hit.point+Vector3.up*.05f;
            if (Physics.Linecast(player.transform.position+Vector3.up,ground+Vector3.up,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) return false;
            if (Physics.Raycast(ground+Vector3.up*.1f,Vector3.up,5,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) return false;
            Quaternion yaw=Quaternion.Euler(0,player.transform.eulerAngles.y,0);
            if (Physics.CheckBox(ground+Vector3.up*1.45f,new Vector3(.65f,1.4f,1.6f),yaw,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)) return false;
            foreach (Vector3 p in new[] {new Vector3(-.55f,0,-1.3f),new Vector3(.55f,0,-1.3f),new Vector3(-.55f,0,1.3f),new Vector3(.55f,0,1.3f)})
            {
                RaycastHit support;
                if (!Physics.Raycast(ground+yaw*p+Vector3.up,Vector3.down,out support,1.4f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore) || Math.Abs(support.point.y-hit.point.y)>.3f) return false;
            }
            return true;
        }
        private Sprite MakeIcon()
        {
            if (icon != null) return icon;
            iconTexture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            // Original horse-head silhouette. Clear outside the head, no external artwork.
            var pixels = new Color[64*64];
            for(int y=0;y<64;y++) for(int x=0;x<64;x++)
            {
                bool neck=x>17 && x<36 && y>7 && y<43;
                bool head=x>25 && x<49 && y>34 && y<52;
                bool nose=x>=43 && x<58 && y>31 && y<43;
                bool ear=x>28 && x<36 && y>=51 && y<61;
                if(neck||head||nose||ear)pixels[y*64+x]=new Color(.65f,.34f,.15f,1);
                if(x>40&&x<44&&y>44&&y<48)pixels[y*64+x]=Color.black;
            }
            iconTexture.SetPixels(pixels);iconTexture.Apply();
            icon=Sprite.Create(iconTexture,new Rect(0,0,64,64),new Vector2(.5f,.5f));return icon;
        }
        private void LocaleChanged(Locale locale) { StartCoroutine(Localize()); }
        private IEnumerator Localize()
        {
            yield return LocalizationSettings.InitializationOperation;
            var handle=LocalizationSettings.StringDatabase.GetTableAsync("ItemData"); yield return handle;
            var table=handle.Result;if(table==null)yield break;
            bool zh=LocalizationSettings.SelectedLocale!=null && LocalizationSettings.SelectedLocale.Identifier.Code.StartsWith("zh",StringComparison.OrdinalIgnoreCase);
            SetText(table,"TonyHorseName",zh?"牛馬":"Two-seat Horse");
            SetText(table,"TonyHorseDescription",zh?"可供一位駕駛與一位乘客騎乘。於戶外使用背包物品放置；成功後消耗一匹。E 上下馬，Shift 加速，Ctrl+F1/F2 換位。":"Use from your inventory outdoors to place a horse for a driver and passenger. E mounts, Shift boosts, Ctrl+F1/F2 switches seats.");
            SetText(table,"TonyHorseUse",zh?"戶外使用：放置雙人馬":"Use outdoors: place horse");
        }
        private static void SetText(UnityEngine.Localization.Tables.StringTable table, string key, string value)
        { var entry=table.GetEntry(key); if(entry==null)table.AddEntry(key,value); else entry.Value=value; }
        // Sidecars are bound to the full native save snapshot, including backups / Save As.
        // Original save bytes and schema remain untouched.
        private static string Key(SaveData data)
        {
            if(data==null)return null;
            using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(JsonUtility.ToJson(data)))).Replace("-","").ToLowerInvariant();
        }
        private static string SavePath(string key) { return Path.Combine(Paths.ConfigPath,"Tony.Horses",key+".json"); }
        private static void AfterLoad(SaveManager __instance,bool __result)
        { if(instance!=null&&__result) {instance.ClearHorses();instance.loadKey=Key(__instance.saveData);instance.loaded=false;} }
        private static void BeforeNewGame() { if(instance!=null){instance.ClearHorses();instance.loadKey=null;instance.loaded=false;} }
        private void Saved()
        {
            if(network==null||!network.IsServer||!loaded)return;
            try
            {
                string key=Key(subscribedSave.saveData);if(key==null)return;
                string file=SavePath(key);Directory.CreateDirectory(Path.GetDirectoryName(file));
                string temp=file+".tmp";File.WriteAllText(temp,HorseJson.Serialize(Capture()),Encoding.UTF8);
                if(File.Exists(file))File.Replace(temp,file,file+".bak");else File.Move(temp,file);
            }
            catch(Exception ex){log.LogError("Horse save failed: "+ex);Tell("Horse save failed. See BepInEx log.");}
        }
        private void RestoreSnapshot()
        {
            if(String.IsNullOrEmpty(loadKey))return;
            try
            {
                string file=SavePath(loadKey);if(!File.Exists(file))return;
                if(new FileInfo(file).Length>65536)throw new InvalidDataException("Horse save too large");
                Snapshot s=HorseJson.Deserialize<Snapshot>(File.ReadAllText(file));if(!Valid(s))throw new InvalidDataException("Invalid horse save");
                foreach(Record r in s.horses)Spawn(r);
                log.LogInfo("Restored "+s.horses.Length+" horses for this save snapshot.");
            }
            catch(Exception ex){log.LogError("Horse restore failed: "+ex);Tell("Horse restore failed. Existing sidecar was retained.");}
        }
        private void OnDestroy()
        {
            if(subscribedSave!=null)subscribedSave.onGameSaved-=Saved;
            LocalizationSettings.SelectedLocaleChanged-=LocaleChanged;
            Disconnect();if(patches!=null)patches.UnpatchSelf();if(instance==this)instance=null;
            if(icon!=null)Destroy(icon);if(iconTexture!=null)Destroy(iconTexture);
        }
    }
}
