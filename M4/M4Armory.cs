using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;

namespace TonyMods
{
    // M4A1 rifle + 5.56 rounds: item registration, native GunTool patches, visuals on every
    // prefab that shows the item, and a sound relay so teammates hear the synthesized shots.
    public sealed partial class M4Armory : MonoBehaviour
    {
        public const ushort RifleId = 47930, AmmoId = 47931, MusketId = 360, BulletId = 290;
        private const string Channel = "Tony.M4.v1";
        private const byte Protocol = 1;
        private static readonly MethodInfo HandRig = AccessTools.Method(typeof(PlayerAnimTP), "DisableHandRig");
        private static readonly FieldInfo TpAnimator = AccessTools.Field(typeof(PlayerAnimTP), "animator");
        private static M4Armory instance;
        private ManualLogSource log;
        private Harmony harmony;
        private ConfigEntry<bool> sell, fde;
        private ConfigEntry<int> price, ammoPrice, ammoPerPurchase, damage, rpm, magazine, durability, scopePower;
        private ConfigEntry<float> recoil, reloadSeconds, modelScale, volume;
        private ConfigEntry<string> fpOffset;
        private ConfigEntry<KeyCode> modeKey;
        private ItemData rifleData, ammoData;
        private Sprite rifleIcon, ammoIcon;
        private Material brass;
        private NetworkManager network;
        private readonly Dictionary<ulong, float> budget = new Dictionary<ulong, float>();
        private readonly Dictionary<ulong, float> budgetAt = new Dictionary<ulong, float>();
        private bool chinese = true;

        internal static bool Ready { get { return instance != null && instance.rifleData != null && instance.ammoData != null; } }
        internal static ItemData RifleData { get { return instance != null ? instance.rifleData : null; } }
        internal static ItemData AmmoData { get { return instance != null ? instance.ammoData : null; } }
        internal static int Damage { get { return instance.damage.Value; } }
        internal static int Rpm { get { return instance.rpm.Value; } }
        internal static int ScopePower { get { return instance != null ? instance.scopePower.Value : 4; } }
        internal static float RecoilScale { get { return instance.recoil.Value; } }
        internal static float ReloadSeconds { get { return instance.reloadSeconds.Value; } }
        internal static float ModelScale { get { return instance.modelScale.Value; } }
        internal static bool Fde { get { return instance.fde.Value; } }
        internal static KeyCode FireModeKey { get { return instance.modeKey.Value; } }
        internal static Material BrassMaterial { get { return instance.brass; } }
        internal static Vector3 FirstPersonOffset { get { return ParseVector(instance.fpOffset.Value); } }
        internal static string Text(string zh, string en) { return instance == null || instance.chinese ? zh : en; }
        internal static bool IsM4(GunTool gun) { return gun != null && gun.GetComponent<M4Rifle>() != null; }

        public void Initialize(ConfigFile config, ManualLogSource logger)
        {
            instance = this; log = logger;
            sell = config.Bind("M4", "SellInShop", true, "List the M4A1 rifle and 5.56 rounds at the merchant. Items stay usable when false.");
            price = config.Bind("M4", "Price", 1, new ConfigDescription("M4A1 merchant price.", new AcceptableValueRange<int>(1, 60000)));
            ammoPrice = config.Bind("M4", "AmmoPrice", 1, new ConfigDescription("Price of one 5.56 purchase.", new AcceptableValueRange<int>(1, 60000)));
            ammoPerPurchase = config.Bind("M4", "AmmoPerPurchase", 1000, new ConfigDescription("5.56 rounds per purchase (native shop sells one full stack).", new AcceptableValueRange<int>(1, 9999)));
            damage = config.Bind("M4", "Damage", 9, new ConfigDescription("Damage per bullet (musket 35, crossbow 15).", new AcceptableValueRange<int>(1, 255)));
            rpm = config.Bind("M4", "RoundsPerMinute", 750, new ConfigDescription("Full-auto fire rate.", new AcceptableValueRange<int>(M4Rules.MinRpm, M4Rules.MaxRpm)));
            magazine = config.Bind("M4", "MagazineSize", 30, new ConfigDescription("Rounds per magazine. Applies to newly bought rifles and reloads.", new AcceptableValueRange<int>(1, 100)));
            durability = config.Bind("M4", "Durability", 3000, new ConfigDescription("Shots before the rifle breaks (repairable like the musket).", new AcceptableValueRange<int>(10, 60000)));
            reloadSeconds = config.Bind("M4", "ReloadSeconds", 2.2f, new ConfigDescription("Magazine change time; empty reloads add 0.4 s for the charging handle.", new AcceptableValueRange<float>(.5f, 6f)));
            recoil = config.Bind("M4", "RecoilScale", 1f, new ConfigDescription("Camera kick multiplier (0 disables recoil).", new AcceptableValueRange<float>(0f, 3f)));
            scopePower = config.Bind("M4", "ScopeMagnification", 4, new ConfigDescription("ACOG magnification; right mouse toggles it.", new AcceptableValueRange<int>(2, 8)));
            modeKey = config.Bind("M4", "FireModeKey", KeyCode.B, "Toggle full-auto / semi-auto while holding the M4.");
            fde = config.Bind("M4", "SandFurniture", false, "Use the sand (FDE) handguard, stock and magazine instead of black.");
            volume = config.Bind("M4", "Volume", .8f, new ConfigDescription("Rifle sound volume, multiplied by the game's sound volume.", new AcceptableValueRange<float>(0f, 1f)));
            modelScale = config.Bind("M4", "ModelScale", .85f, new ConfigDescription("Model size relative to the musket it replaces.", new AcceptableValueRange<float>(.5f, 1.5f)));
            fpOffset = config.Bind("M4", "FirstPersonOffset", "0,0,0", "First-person model nudge in musket mesh units: x (+ towards stock), y (up), z (right).");
            BindOptics(config);
            M4Audio.Initialize(logger, volume);
            if (!M4Rifle.ApiReady || HandRig == null || TpAnimator == null) throw new MissingMemberException("GunTool / PlayerAnimTP API changed; M4 disabled");
            rifleIcon = LoadIcon("Tony.M4.icon.png"); ammoIcon = LoadIcon("Tony.M4.ammo.png");
            LoadOpticIcons();
            brass = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            brass.name = "Tony M4 brass"; brass.hideFlags = HideFlags.DontUnloadUnusedAsset;
            brass.color = new Color(.8f, .6f, .24f);
            if (brass.HasProperty("_BaseColor")) brass.SetColor("_BaseColor", brass.color);
            if (brass.HasProperty("_Metallic")) brass.SetFloat("_Metallic", .8f);
            harmony = new Harmony("Tony.AleTaleMods.M4");
            try
            {
                Patch(typeof(ItemManager), "Awake", "RegisterItems", true);
                Patch(typeof(GunTool), "SetItem", "AfterSetItem", false);
                Patch(typeof(GunTool), "Selected", "AfterSelected", false);
                Patch(typeof(GunTool), "Fire", "BeforeFire", true);
                Patch(typeof(GunTool), "Reload", "BeforeReload", true);
                Patch(typeof(GunTool), "CheckReload", "BeforeCheckReload", true);
                MethodInfo hand = AccessTools.Method(typeof(PlayerAnimTP), "OnSelectedHandItemDataId");
                if (hand == null) throw new MissingMethodException("PlayerAnimTP", "OnSelectedHandItemDataId");
                harmony.Patch(hand, prefix: new HarmonyMethod(typeof(M4Armory), "BeforeHandItem"), postfix: new HarmonyMethod(typeof(M4Armory), "AfterHandItem"));
                Patch(typeof(CollectibleNet), "OnNetworkSpawn", "AfterCollectibleSpawn", false);
                Patch(typeof(CollectibleNet), "OnItemValueChanged", "AfterCollectibleItem", false);
                PatchOptics();
            }
            catch { harmony.UnpatchSelf(); harmony = null; throw; }
            LocalizationSettings.SelectedLocaleChanged += LocaleChanged;
            StartCoroutine(Localize());
            log.LogInfo("M4A1 ready: item " + RifleId + " + 5.56 rounds " + AmmoId + "; " + rpm.Value + " RPM, " + damage.Value + " dmg, " + magazine.Value +
                "-round magazine, ACOG " + scopePower.Value + "x, " + modeKey.Value + " toggles fire mode; optics 47932-47936 (drag onto the rifle, " + detachKey.Value + " removes).");
        }

        private void Patch(Type type, string target, string handler, bool prefix)
        {
            MethodInfo method = AccessTools.Method(type, target);
            if (method == null) throw new MissingMethodException(type.Name, target);
            HarmonyMethod patch = new HarmonyMethod(typeof(M4Armory), handler);
            harmony.Patch(method, prefix: prefix ? patch : null, postfix: prefix ? null : patch);
        }

        private static Vector3 ParseVector(string text)
        {
            Vector3 v = Vector3.zero;
            string[] parts = (text ?? "").Split(',');
            float x;
            for (int i = 0; i < 3 && i < parts.Length; i++)
                if (Single.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out x) && Math.Abs(x) < 1) v[i] = x;
            return v;
        }

        private Sprite LoadIcon(string resource)
        {
            using (var stream = typeof(M4Armory).Assembly.GetManifestResourceStream(resource))
            {
                if (stream == null) throw new InvalidOperationException("Missing icon resource " + resource);
                var bytes = new byte[stream.Length]; int read = 0;
                while (read < bytes.Length) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidOperationException("Cannot decode " + resource);
                texture.name = resource; texture.hideFlags = HideFlags.DontUnloadUnusedAsset; texture.wrapMode = TextureWrapMode.Clamp;
                Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                sprite.name = resource; sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                return sprite;
            }
        }

        // ---- Items ----------------------------------------------------------------------------
        private static void RegisterItems(ItemManager __instance)
        {
            if (instance == null) return;
            try { instance.Register(__instance); }
            catch (Exception ex) { instance.rifleData = null; instance.ammoData = null; instance.log.LogError("M4 item registration failed: " + ex); }
        }

        private void Register(ItemManager manager)
        {
            List<ItemData> items = manager.itemDataHub.itemData.ToList();
            ItemData musket = items.FirstOrDefault(i => i != null && i.id == MusketId);
            ItemData bullet = items.FirstOrDefault(i => i != null && i.id == BulletId);
            if (musket == null || bullet == null || musket.fpPrefab == null || musket.fpPrefab.GetComponent<GunTool>() == null)
                throw new InvalidOperationException("Native musket / bullet item data missing");
            rifleData = Upsert(items, RifleId, "TonyM4", musket, ConfigureRifle);
            ammoData = Upsert(items, AmmoId, "TonyM556", bullet, ConfigureAmmo);
            RegisterOptics(items, bullet);
            manager.itemDataHub.itemData = items.ToArray();
        }

        private ItemData Upsert(List<ItemData> items, ushort id, string key, ItemData template, Action<ItemData> configure)
        {
            ItemData existing = items.FirstOrDefault(i => i != null && i.id == id);
            if (existing != null)
            {
                if (existing.name != key + "Name") throw new InvalidOperationException("Item ID " + id + " is used by another mod (" + existing.name + ")");
                configure(existing); return existing;
            }
            ItemData item = Instantiate(template);
            item.hideFlags = HideFlags.DontUnloadUnusedAsset;
            item.id = id; item.name = key + "Name"; item.itemDescription = key + "Description";
            configure(item); items.Add(item);
            return item;
        }

        private void ConfigureRifle(ItemData item)
        {
            item.icon = rifleIcon; item.price = (ushort)price.Value; item.shopItem = sell.Value; item.buyByOne = true;
            item.maxStack = 1; item.maxCharge = (ushort)magazine.Value; item.durability = (ushort)durability.Value; item.durabilityPerHit = 1;
            Common(item);
        }

        private void ConfigureAmmo(ItemData item)
        {
            item.icon = ammoIcon; item.price = (ushort)ammoPrice.Value; item.shopItem = sell.Value;
            // Native shop purchases give Item(itemData).amount = maxStack, priced per stack when !buyByOne.
            item.buyByOne = false; item.maxStack = (ushort)ammoPerPurchase.Value;
            Common(item);
        }

        private static void Common(ItemData item)
        {
            item.levelDependant = 0; item.questDependant = 0; item.hasRarity = false; item.topShopItem = false;
            item.isBarterShopItem = false; item.barterItemData = null; item.commonLootItem = false; item.isHalloweenItem = false;
            item.quest = false; item.playerCantSell = false; item.playerCantDrop = false; item.doNotSave = false;
        }

        // ---- Native GunTool patches -----------------------------------------------------------
        private static void AfterSetItem(GunTool __instance, Item __0)
        {
            if (!Ready || __0.dataId != RifleId) return;
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            try
            {
                if (rifle == null) rifle = __instance.gameObject.AddComponent<M4Rifle>();
                rifle.Bind(__instance, __0);
            }
            catch (Exception ex)
            {
                if (rifle != null) Destroy(rifle);
                instance.log.LogError("M4 first-person setup failed: " + ex);
            }
        }

        private static void AfterSelected(GunTool __instance)
        {
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            if (rifle != null) rifle.AfterSelected();
        }

        private static bool BeforeFire(GunTool __instance)
        {
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            if (rifle == null) return true;
            try { rifle.Fire(); }
            catch (Exception ex) { instance.log.LogError("M4 shot failed: " + ex); }
            return false;
        }

        private static bool BeforeReload(GunTool __instance)
        {
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            if (rifle == null) return true;
            try { rifle.StartReload(); }
            catch (Exception ex) { instance.log.LogError("M4 reload failed: " + ex); }
            return false;
        }

        // Native CheckReload reloads whenever the gun is selected; the M4 only does so when empty.
        private static bool BeforeCheckReload(GunTool __instance)
        {
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            return rifle == null || (__instance.clipContent <= 0 && !rifle.Busy);
        }

        // Third-person: the native pose switch only knows musket/crossbow IDs (TorsoState 3).
        // Only a hand item created by this call is skinned; invisible avatars return early natively.
        private static void BeforeHandItem(PlayerAnimTP __instance, out GameObject __state) { __state = __instance.handItem; }
        private static void AfterHandItem(PlayerAnimTP __instance, uint __1, GameObject __state)
        {
            if (!Ready || __1 != RifleId || __instance.handItem == null || __instance.handItem == __state) return;
            try
            {
                M4Model.Skin(__instance.handItem.transform, false, instance.modelScale.Value, Vector3.zero, instance.fde.Value, instance.RemoteScope(__instance));
                HandRig.Invoke(__instance, null);
                Animator animator = (Animator)TpAnimator.GetValue(__instance);
                if (animator != null) animator.SetInteger("TorsoState", 3);
            }
            catch (Exception ex) { instance.log.LogError("M4 third-person setup failed: " + ex); }
        }

        private static void AfterCollectibleSpawn(CollectibleNet __instance) { SkinCollectible(__instance, __instance.item.Value); }
        private static void AfterCollectibleItem(CollectibleNet __instance, Item __1) { SkinCollectible(__instance, __1); }
        private static void SkinCollectible(CollectibleNet collectible, Item item)
        {
            if (!Ready || item.dataId != RifleId || collectible == null) return;
            try { M4Model.Skin(collectible.transform, false, instance.modelScale.Value, Vector3.zero, instance.fde.Value, M4Scopes.Effective(item.metaInt)); }
            catch (Exception ex) { instance.log.LogError("M4 dropped-item model failed: " + ex); }
        }

        // ---- Sound relay ----------------------------------------------------------------------
        // Client -> host: [protocol, kind, pos xyz]. Host -> others: the same plus shooter id.
        internal static void SendSound(M4Sound.Kind kind, Vector3 position)
        {
            if (instance == null || instance.network == null || !instance.network.IsListening) return;
            NetworkManager net = instance.network;
            byte[] payload = Payload(kind, position, net.IsServer ? net.LocalClientId : 0, net.IsServer);
            NetworkDelivery delivery = kind == M4Sound.Kind.Shot ? NetworkDelivery.Unreliable : NetworkDelivery.ReliableSequenced;
            if (net.IsServer) instance.Relay(payload, net.LocalClientId, delivery);
            else instance.Send(NetworkManager.ServerClientId, payload, delivery);
        }

        private static byte[] Payload(M4Sound.Kind kind, Vector3 p, ulong shooter, bool withShooter)
        {
            byte[] data = new byte[withShooter ? 22 : 14];
            data[0] = Protocol; data[1] = (byte)kind;
            Buffer.BlockCopy(BitConverter.GetBytes(p.x), 0, data, 2, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(p.y), 0, data, 6, 4);
            Buffer.BlockCopy(BitConverter.GetBytes(p.z), 0, data, 10, 4);
            if (withShooter) Buffer.BlockCopy(BitConverter.GetBytes(shooter), 0, data, 14, 8);
            return data;
        }

        private void Send(ulong client, byte[] payload, NetworkDelivery delivery)
        {
            using (var writer = new FastBufferWriter(64, Allocator.Temp))
            {
                writer.WriteBytesSafe(payload, payload.Length, 0);
                network.CustomMessagingManager.SendNamedMessage(Channel, client, writer, delivery);
            }
        }

        private void Relay(byte[] payload, ulong shooter, NetworkDelivery delivery)
        {
            foreach (ulong id in network.ConnectedClientsIds)
                if (id != shooter && id != network.LocalClientId) Send(id, payload, delivery);
        }

        private void Receive(ulong sender, FastBufferReader reader)
        {
            try
            {
                int length = reader.Length;
                if (length < 2 || length > 64) return;
                byte[] data = new byte[length];
                reader.ReadBytesSafe(ref data, length, 0);
                if (data[0] != Protocol) return;
                if (!M4Sound.Valid(data[1])) { ReceiveControl(sender, data); return; }
                if (length < 14) return;
                var kind = (M4Sound.Kind)data[1];
                Vector3 position = new Vector3(BitConverter.ToSingle(data, 2), BitConverter.ToSingle(data, 6), BitConverter.ToSingle(data, 10));
                if (network.IsServer)
                {
                    if (sender == network.LocalClientId || !network.ConnectedClientsIds.Contains(sender) || !Spend(sender)) return;
                    PlayerNet shooter = Shooter(sender);
                    if (shooter == null) return;
                    if (!Finite(position) || (position - shooter.transform.position).sqrMagnitude > 100) position = shooter.transform.position + Vector3.up * 1.5f;
                    PlayRemote(kind, position, shooter);
                    Relay(Payload(kind, position, sender, true), sender, kind == M4Sound.Kind.Shot ? NetworkDelivery.Unreliable : NetworkDelivery.ReliableSequenced);
                }
                else
                {
                    if (sender != NetworkManager.ServerClientId || length < 22 || !Finite(position)) return;
                    ulong id = BitConverter.ToUInt64(data, 14);
                    if (id == network.LocalClientId) return;
                    PlayRemote(kind, position, Shooter(id));
                }
            }
            catch (Exception ex) { log.LogWarning("M4 message rejected: " + ex.Message); }
        }

        // Host-side flood guard: 30 messages per second per client (750 RPM is 12.5 shots/s).
        private bool Spend(ulong sender)
        {
            float tokens, at, now = Time.unscaledTime;
            if (!budget.TryGetValue(sender, out tokens)) tokens = 30;
            if (budgetAt.TryGetValue(sender, out at)) tokens = Mathf.Min(30, tokens + (now - at) * 30);
            budgetAt[sender] = now;
            if (tokens < 1) { budget[sender] = tokens; return false; }
            budget[sender] = tokens - 1; return true;
        }

        private static PlayerNet Shooter(ulong id)
        {
            PlayerNet player;
            return PlayerManager.Instance != null && PlayerManager.Instance.players.TryGetValue(id, out player) ? player : null;
        }

        private static bool Finite(Vector3 v)
        {
            return !(Single.IsNaN(v.x) || Single.IsNaN(v.y) || Single.IsNaN(v.z) || Single.IsInfinity(v.x) || Single.IsInfinity(v.y) || Single.IsInfinity(v.z)) &&
                Math.Abs(v.x) < 100000 && Math.Abs(v.y) < 100000 && Math.Abs(v.z) < 100000;
        }

        private static void PlayRemote(M4Sound.Kind kind, Vector3 position, PlayerNet shooter)
        {
            M4Audio.Play(kind, position, false);
            if (kind != M4Sound.Kind.Shot || shooter == null) return;
            PlayerAnimTP tp = shooter.GetComponentInChildren<PlayerAnimTP>(true);
            M4Model model = tp != null && tp.handItem != null ? tp.handItem.GetComponentInChildren<M4Model>(true) : null;
            if (model != null) model.Flash();
        }

        private void Update()
        {
            NetworkManager net = NetworkManager.Singleton;
            if (net == null || !net.IsListening) { if (network != null) Disconnect(); return; }
            if (network == net) return;
            Disconnect(); network = net;
            network.CustomMessagingManager.RegisterNamedMessageHandler(Channel, Receive);
        }

        private void Disconnect()
        {
            if (network != null && network.CustomMessagingManager != null) network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            network = null; budget.Clear(); budgetAt.Clear(); remoteScopes.Clear(); lastDetach.Clear();
        }

        // ---- Localization ---------------------------------------------------------------------
        private void LocaleChanged(Locale locale) { StartCoroutine(Localize()); }
        private IEnumerator Localize()
        {
            yield return LocalizationSettings.InitializationOperation;
            var handle = LocalizationSettings.StringDatabase.GetTableAsync("ItemData"); yield return handle;
            var table = handle.Result; if (table == null) yield break;
            chinese = LocalizationSettings.SelectedLocale == null || LocalizationSettings.SelectedLocale.Identifier.Code.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
            string key = modeKey.Value.ToString(), rounds = ammoPerPurchase.Value.ToString();
            SetText(table, "TonyM4Name", chinese ? "M4A1 步槍" : "M4A1 Rifle");
            string detach = detachKey.Value.ToString();
            SetText(table, "TonyM4Description", chinese
                ? "全自動卡賓槍，" + magazine.Value + " 發彈匣，出廠附 ACOG " + scopePower.Value + "× 瞄準鏡。按住左鍵連發，R 換彈，" + key + " 切換全自動／半自動，右鍵開鏡。把紅點、全像、黃銅鏡或狙擊鏡拖到槍上即可換鏡，" + detach + " 拆下改用機械瞄具。使用 5.56 子彈。"
                : "Full-auto carbine with a " + magazine.Value + "-round magazine and a " + scopePower.Value + "x ACOG. Hold fire for automatic, R reloads, " + key + " toggles auto/semi, right mouse aims. Drag a red dot, holographic, brass or sniper optic onto it to swap; " + detach + " removes the optic for iron sights. Uses 5.56 rounds.");
            SetText(table, "TonyM556Name", chinese ? "5.56 子彈" : "5.56 Rounds");
            SetText(table, "TonyM556Description", chinese ? "M4A1 步槍用彈藥。商店每次購買 " + rounds + " 發。" : "Ammunition for the M4A1 rifle. Each merchant purchase gives " + rounds + " rounds.");
            LocalizeOptics(table);
        }
        private static void SetText(UnityEngine.Localization.Tables.StringTable table, string key, string value)
        { var entry = table.GetEntry(key); if (entry == null) table.AddEntry(key, value); else entry.Value = value; }

        private void OnDestroy()
        {
            LocalizationSettings.SelectedLocaleChanged -= LocaleChanged;
            Disconnect();
            if (harmony != null) harmony.UnpatchSelf();
            M4Audio.Shutdown();
            if (brass != null) Destroy(brass);
            if (instance == this) instance = null;
        }
    }
}
