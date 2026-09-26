using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Unity.Netcode;
using UnityEngine;

namespace TonyMods
{
    // Interchangeable optics: three scope items, attach by dragging a scope onto the rifle in any
    // inventory (native use-item-on-item flow), a detach key, and a loadout broadcast so teammates see
    // the optic (remote clients only receive the held item's data ID).
    public sealed partial class M4Armory
    {
        // Values 1 and 2 are native repair / reforge. Any other non-zero value makes
        // ContainerManager.OnItemDragServerRpc call ItemManager.UseItemOnItem, which natively ignores it.
        private const ItemData.UseItemOnItemType AttachUse = (ItemData.UseItemOnItemType)40;
        private const byte DetachKind = 16, LoadoutKind = 17, NoteKind = 18;
        private const byte NoteFitted = 1, NoteAlready = 2, NoteRemoved = 3, NoteNone = 4;
        private ConfigEntry<int> scopePrice;
        private ConfigEntry<KeyCode> detachKey;
        private readonly Dictionary<M4ScopeKind, ItemData> scopeItems = new Dictionary<M4ScopeKind, ItemData>();
        private readonly Dictionary<M4ScopeKind, Sprite> scopeIcons = new Dictionary<M4ScopeKind, Sprite>();
        private readonly Dictionary<ulong, M4ScopeKind> remoteScopes = new Dictionary<ulong, M4ScopeKind>();
        private readonly Dictionary<ulong, float> lastDetach = new Dictionary<ulong, float>();
        private string noteText;
        private float noteUntil;
        private GUIStyle noteStyle;

        internal static KeyCode DetachKey { get { return instance.detachKey.Value; } }

        internal static string ScopeName(M4ScopeKind kind)
        {
            M4ScopeProfile p = M4Scopes.Profile(kind);
            return Text(p.Zh, p.En);
        }

        private void BindOptics(ConfigFile config)
        {
            scopePrice = config.Bind("M4", "ScopePrice", 1, new ConfigDescription("Merchant price of each M4 optic.", new AcceptableValueRange<int>(1, 60000)));
            detachKey = config.Bind("M4", "DetachScopeKey", KeyCode.U, "Remove the optic from the held M4; it returns to your inventory (T is the native locator key).");
        }

        private void LoadOpticIcons()
        {
            foreach (M4ScopeKind kind in M4Scopes.ItemKinds) scopeIcons[kind] = LoadIcon("Tony.M4.scope" + (int)kind + ".png");
        }

        private void PatchOptics()
        {
            Patch(typeof(ItemManager), "UseItemOnItem", "BeforeUseItemOnItem", true);
            Patch(typeof(GunTool), "UpdSpecs", "AfterSpecs", false);
        }

        private void RegisterOptics(List<ItemData> items, ItemData template)
        {
            scopeItems.Clear();
            foreach (M4ScopeKind kind in M4Scopes.ItemKinds)
            {
                M4ScopeKind fitted = kind;
                scopeItems[fitted] = Upsert(items, M4Scopes.ItemId(fitted), "TonyM4Scope" + fitted, template, item => ConfigureScope(item, fitted));
            }
        }

        private void ConfigureScope(ItemData item, M4ScopeKind kind)
        {
            item.icon = scopeIcons[kind]; item.price = (ushort)scopePrice.Value; item.shopItem = sell.Value;
            item.buyByOne = true; item.maxStack = 1; item.maxCharge = 0; item.durability = 0;
            item.useItemOnItemType = AttachUse; item.isInvUseable = false; item.hotbarItem = false;
            Common(item);
        }

        // ---- Patches ----------------------------------------------------------------------------
        // Rifle Item changed (native OnItemChange -> UpdSpecs): pick up a new optic on the held gun.
        private static void AfterSpecs(GunTool __instance, Item __0)
        {
            M4Rifle rifle = __instance.GetComponent<M4Rifle>();
            if (rifle != null && __0.dataId == RifleId) rifle.OnItem(__0);
        }

        // Host side of dragging a scope item onto an item. Returning false lets the native drag
        // continue (swap / move) when the target is not an M4.
        private static bool BeforeUseItemOnItem(uint __1, ItemData __2, uint __3, ref bool __result)
        {
            M4ScopeKind kind;
            if (instance == null || !Ready || __2 == null || !M4Scopes.FromItem(__2.id, out kind)) return true;
            try { __result = instance.Attach(__1, kind, __3); }
            catch (Exception ex) { __result = false; instance.log.LogError("M4 optic attach failed: " + ex); }
            return false;
        }

        private bool Attach(uint scopeItemId, M4ScopeKind incoming, uint gunItemId)
        {
            Item gun, scope; ContainerNet gunBox, scopeBox;
            if (ItemManager.Instance == null || !ItemManager.Instance.GetItemById(gunItemId, out gun, out gunBox) || gun.dataId != RifleId) return false;
            if (!ItemManager.Instance.GetItemById(scopeItemId, out scope, out scopeBox) || scope.amount < 1) return false;
            ulong owner; bool player = Owner(gunBox, out owner);
            M4ScopeSwap plan = M4Scopes.PlanAttach(gun.metaInt, gun.amount, incoming);
            if (!plan.Apply) { if (player) Note(owner, NoteAlready, incoming); return true; }
            if (!scopeBox.RemoveItemAmount(scopeItemId, 1)) return false;
            if (!Refit(gunBox, gun, plan, owner, player)) { Give(scopeBox, incoming, owner, player); return true; }
            if (M4Scopes.HasItem(plan.Returned)) Give(gunBox, plan.Returned, owner, player);
            if (player) Note(owner, NoteFitted, incoming);
            log.LogInfo("M4 optic fitted: " + incoming + " (returned " + plan.Returned + ")" + (plan.Split ? "; stack split" : ""));
            return true;
        }

        // Writes the new optic into the target rifle. A stacked rifle keeps its item ID for the refit
        // and the rest of the stack goes back as a separate item (dropped at the owner's feet if full).
        private bool Refit(ContainerNet box, Item gun, M4ScopeSwap plan, ulong owner, bool player)
        {
            if (!plan.Split) { gun.metaInt = plan.NewMeta; return box.SetItemById(gun.id, gun); }
            Item rest = gun;
            rest.id = 0; rest.order = 0; rest.contId = 0; rest.amount = (ushort)(gun.amount - 1);
            gun.amount = 1; gun.metaInt = plan.NewMeta;
            if (!box.SetItemById(gun.id, gun)) return false;
            box.AddNewItem(rest, DropPosition(box, owner, player), false);
            return true;
        }

        private void Give(ContainerNet box, M4ScopeKind kind, ulong owner, bool player)
        {
            ItemData data;
            if (!scopeItems.TryGetValue(kind, out data)) return;
            box.AddNewItem(new Item(data) { amount = 1 }, DropPosition(box, owner, player), false);
        }

        private static Vector3 DropPosition(ContainerNet box, ulong owner, bool player)
        {
            if (player && PlayerManager.Instance != null) return PlayerManager.Instance.GetPlayerDropPos(owner);
            return box.transform.position + Vector3.up * .5f;
        }

        private static bool Owner(ContainerNet box, out ulong owner)
        {
            owner = 0;
            if (box == null || PlayerManager.Instance == null || ContainerManager.Instance == null) return false;
            foreach (ulong id in PlayerManager.Instance.players.Keys.ToList())
            {
                ContainerNet own;
                if (ContainerManager.Instance.GetPlayerContainer(id, out own) && own == box) { owner = id; return true; }
            }
            return false;
        }

        // ---- Detach -----------------------------------------------------------------------------
        internal static void RequestDetach(uint itemId)
        {
            if (instance == null || instance.network == null || !instance.network.IsListening || itemId == 0) return;
            if (instance.network.IsServer) { instance.Detach(instance.network.LocalClientId, itemId); return; }
            byte[] data = new byte[6];
            data[0] = Protocol; data[1] = DetachKind;
            Buffer.BlockCopy(BitConverter.GetBytes(itemId), 0, data, 2, 4);
            instance.Send(NetworkManager.ServerClientId, data, NetworkDelivery.ReliableSequenced);
        }

        private void Detach(ulong sender, uint itemId)
        {
            float last;
            if (lastDetach.TryGetValue(sender, out last) && Time.unscaledTime - last < .4f) return;
            lastDetach[sender] = Time.unscaledTime;
            ContainerNet box; Item gun;
            if (ContainerManager.Instance == null || !ContainerManager.Instance.GetPlayerContainer(sender, out box) ||
                !box.GetItemById(itemId, out gun, true) || gun.dataId != RifleId) return;
            M4ScopeSwap plan = M4Scopes.PlanDetach(gun.metaInt, gun.amount);
            if (!plan.Apply) { Note(sender, NoteNone, M4ScopeKind.Iron); return; }
            if (!Refit(box, gun, plan, sender, true)) return;
            Give(box, plan.Returned, sender, true);
            Note(sender, NoteRemoved, plan.Returned);
            log.LogInfo("M4 optic removed: " + plan.Returned + "; client=" + sender);
        }

        // ---- Loadout broadcast ------------------------------------------------------------------
        // Client -> host: [protocol, 17, kind]. Host -> others: [protocol, 17, kind, owner u64].
        internal static void SendLoadout(M4ScopeKind kind)
        {
            if (instance == null || instance.network == null || !instance.network.IsListening) return;
            NetworkManager net = instance.network;
            if (net.IsServer) { instance.remoteScopes[net.LocalClientId] = kind; instance.Relay(LoadoutPayload(kind, net.LocalClientId), net.LocalClientId, NetworkDelivery.ReliableSequenced); }
            else instance.Send(NetworkManager.ServerClientId, new[] { Protocol, LoadoutKind, (byte)kind }, NetworkDelivery.ReliableSequenced);
        }

        private static byte[] LoadoutPayload(M4ScopeKind kind, ulong owner)
        {
            byte[] data = new byte[11];
            data[0] = Protocol; data[1] = LoadoutKind; data[2] = (byte)kind;
            Buffer.BlockCopy(BitConverter.GetBytes(owner), 0, data, 3, 8);
            return data;
        }

        private static bool ValidKind(byte kind) { return M4Scopes.IsKind(kind); }

        private void ReceiveControl(ulong sender, byte[] data)
        {
            bool fromHost = sender == NetworkManager.ServerClientId;
            switch (data[1])
            {
                case DetachKind:
                    if (network.IsServer && data.Length >= 6 && sender != network.LocalClientId && network.ConnectedClientsIds.Contains(sender))
                        Detach(sender, BitConverter.ToUInt32(data, 2));
                    break;
                case LoadoutKind:
                    if (data.Length < 3 || !ValidKind(data[2])) return;
                    if (network.IsServer)
                    {
                        if (sender == network.LocalClientId || !network.ConnectedClientsIds.Contains(sender) || !Spend(sender)) return;
                        ApplyRemote(sender, (M4ScopeKind)data[2]);
                        Relay(LoadoutPayload((M4ScopeKind)data[2], sender), sender, NetworkDelivery.ReliableSequenced);
                    }
                    else if (fromHost && data.Length >= 11)
                    {
                        ulong owner = BitConverter.ToUInt64(data, 3);
                        if (owner != network.LocalClientId) ApplyRemote(owner, (M4ScopeKind)data[2]);
                    }
                    break;
                case NoteKind:
                    if (!network.IsServer && fromHost && data.Length >= 4) ShowNote(data[2], (M4ScopeKind)data[3]);
                    break;
            }
        }

        private void ApplyRemote(ulong owner, M4ScopeKind kind)
        {
            remoteScopes[owner] = kind;
            PlayerNet player = Shooter(owner);
            PlayerAnimTP tp = player != null ? player.GetComponentInChildren<PlayerAnimTP>(true) : null;
            M4Model model = tp != null && tp.handItem != null ? tp.handItem.GetComponentInChildren<M4Model>(true) : null;
            if (model != null) model.SetScope(kind);
        }

        private M4ScopeKind RemoteScope(PlayerAnimTP tp)
        {
            PlayerNet player = tp != null ? tp.GetComponentInParent<PlayerNet>() : null;
            M4ScopeKind kind;
            return player != null && remoteScopes.TryGetValue(player.OwnerClientId, out kind) ? kind : M4ScopeKind.Iron;
        }

        // ---- Notices ----------------------------------------------------------------------------
        private void Note(ulong client, byte code, M4ScopeKind kind)
        {
            if (network == null) return;
            if (client == network.LocalClientId) ShowNote(code, kind);
            else Send(client, new[] { Protocol, NoteKind, code, (byte)kind }, NetworkDelivery.ReliableSequenced);
        }

        private void ShowNote(byte code, M4ScopeKind kind)
        {
            string name = ScopeName(kind);
            switch (code)
            {
                case NoteFitted: noteText = Text("已安裝 " + name + "，原本的瞄準鏡已放回背包", "Fitted " + name + "; the previous optic went to your inventory"); break;
                case NoteAlready: noteText = Text("這把 M4 已經裝著 " + name, "This M4 already has " + name); break;
                case NoteRemoved: noteText = Text("已拆下 " + name + "，改用機械瞄具", "Removed " + name + "; using iron sights"); break;
                case NoteNone: noteText = Text("這把 M4 沒有裝瞄準鏡", "No optic fitted"); break;
                default: return;
            }
            noteUntil = Time.unscaledTime + 3;
        }

        private void OnGUI()
        {
            if (noteText == null || Time.unscaledTime >= noteUntil || Event.current.type != EventType.Repaint) return;
            float unit = Screen.height / 1080f;
            if (noteStyle == null) noteStyle = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, wordWrap = true };
            noteStyle.fontSize = Mathf.RoundToInt(20 * unit);
            GUI.Box(new Rect(Screen.width / 2f - 330 * unit, Screen.height - 230 * unit, 660 * unit, 42 * unit), noteText, noteStyle);
        }

        // ---- Localization -----------------------------------------------------------------------
        private void LocalizeOptics(UnityEngine.Localization.Tables.StringTable table)
        {
            string key = detachKey.Value.ToString();
            foreach (M4ScopeKind kind in M4Scopes.ItemKinds)
            {
                M4ScopeProfile p = M4Scopes.Profile(kind);
                string stages = string.Join(" / ", M4Scopes.Stages(kind).Select(s => s.ToString("0.#") + "×").ToArray());
                string feature = chinese
                    ? (p.Magnified ? "放大 " + stages + "，右鍵切換倍率。" : "近距離用，開鏡 " + stages + "，槍身直接對準準星。")
                    : (p.Magnified ? "Magnified " + stages + "; right mouse cycles power. " : "Close range, " + stages + ", aims down the real sight. ");
                SetText(table, "TonyM4Scope" + kind + "Name", ScopeName(kind));
                SetText(table, "TonyM4Scope" + kind + "Description", chinese
                    ? "M4A1 用瞄準鏡。" + feature + "在背包把它拖到 M4A1 上安裝，原本的鏡會放回背包；拿著 M4A1 按 " + key + " 拆下。"
                    : "M4A1 optic. " + feature + "Drag it onto an M4A1 in your inventory to fit it (the old optic returns); press " + key + " while holding the rifle to remove it.");
            }
        }
    }
}
