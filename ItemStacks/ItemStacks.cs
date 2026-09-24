using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using Unity.Netcode;
using UnityEngine;

namespace TonyMods
{
    internal static class ItemStacks
    {
        internal const string Owner = "Tony.AleTaleMods.ItemStacks";
        internal const string LegacyStack = "com.travv.aleandtale.itemstack99safe";
        internal const string LegacyFood = "KinkoCraft.StackableFood";
        internal static ManualLogSource Log;
        private static Harmony harmony;
        private static readonly PropertyInfo ListItem = AccessTools.Field(typeof(ContainerNet), "items").FieldType.GetProperty("Item");

        internal static void Initialize(ManualLogSource logger)
        {
            Log = logger;
            harmony = new Harmony(Owner);
            // Soft dependencies ensure both legacy Awake methods have finished.
            // Restore the captured vanilla quantities, then take over their patches
            // in memory only. Neither DLL nor either config file is rewritten.
            Dictionary<ushort, ushort> originals = null;
            BepInEx.PluginInfo legacy;
            if (Chainloader.PluginInfos.TryGetValue(LegacyStack, out legacy))
            {
                FieldInfo field = AccessTools.Field(legacy.Instance.GetType(), "OriginalMaxById");
                originals = field == null ? null : field.GetValue(null) as Dictionary<ushort, ushort>;
                if (originals == null) throw new InvalidOperationException("Unknown ItemStackerFix version: vanilla quantities cannot be recovered.");
            }
            StackPatches.Validate();
            var previous = new Dictionary<ItemData, ushort>();
            foreach (ItemData data in Resources.FindObjectsOfTypeAll<ItemData>())
                if (data != null) previous[data] = data.maxStack;
            try
            {
                Harmony.UnpatchID(LegacyStack);
                Harmony.UnpatchID(LegacyFood);
                if (originals != null)
                    foreach (ItemData data in previous.Keys)
                    {
                        ushort original;
                        if (originals.TryGetValue(data.id, out original)) data.maxStack = original;
                    }
                StackPatches.Install(harmony);
            }
            catch
            {
                harmony.UnpatchSelf();
                foreach (var pair in previous) if (pair.Key != null) pair.Key.maxStack = pair.Value;
                foreach (string owner in new[] { LegacyStack, LegacyFood })
                    if (Chainloader.PluginInfos.TryGetValue(owner, out legacy))
                        new Harmony(owner).PatchAll(legacy.Instance.GetType().Assembly);
                throw;
            }
            Log.LogInfo("Item stacks: all item types have capacity 9999; vanilla purchase/loot quantities preserved; legacy stack patches replaced in memory.");
        }

        internal static ushort Capacity(ItemData data) { return StackRules.Limit; }

        internal static bool AddLarge(ContainerNet __instance, Item item, ref ushort __1, bool __2, ref bool __result)
        {
            if (item.amount <= StackRules.Limit || !__instance.IsServer) return true;
            int remaining = item.amount;
            while (remaining > 0)
            {
                Item part = item; part.amount = (ushort)Math.Min(remaining, StackRules.Limit);
                ushort left;
                __instance.AddNewItem(part, out left, __2);
                int added = part.amount - left;
                remaining -= added;
                if (added == 0 || left != 0) break;
            }
            __1 = (ushort)remaining;
            __result = remaining < item.amount;
            return false;
        }

        internal static bool RemoveOne(ContainerNet container, uint id)
        {
            Item item;
            if (container == null || !container.IsServer || !container.GetItemById(id, out item, false) || item.amount == 0) return false;
            if (item.amount == 1) return container.RemoveItemById(id);
            item.amount--;
            container.SetItem(item, item.order);
            if (Game.Instance != null) Game.Instance.InvokeGameEvent((GameEvent)16, item.dataId, 1);
            return true;
        }

        internal static bool RemoveConsumed(ContainerNet container, uint id)
        {
            Item item; ItemData data;
            if (container.GetItemById(id, out item, false) && ItemManager.Instance.GetItemData(item.dataId, out data) && data.maxCharge > 0)
                return RemoveOne(container, id);
            return container.RemoveItemById(id);
        }

        internal static bool GetOne(ContainerNet container, uint id, out Item item, bool warn)
        {
            if (!container.GetItemById(id, out item, warn) || item.amount == 0) return false;
            item.amount = 1;
            return true;
        }

        internal static bool RemoveOneByData(ContainerNet container, ushort dataId, out Item result)
        {
            result = default(Item);
            if (container == null || !container.IsServer) return false;
            for (int i = 0; i < container.orderedItems.Length; i++)
            {
                Item item = container.orderedItems[i];
                if (item.dataId != dataId || item.amount == 0) continue;
                if (!RemoveOne(container, item.id)) return false;
                result = item; result.amount = 1;
                return true;
            }
            return false;
        }

        // Called instead of a container NetworkList setter. Stateful mutations
        // affect one object; untouched copies keep their original metadata.
        internal static void SetPreservingCopies(object list, int index, Item value, ContainerNet container)
        {
            Item old = (Item)ListItem.GetValue(list, new object[] { index });
            if (container.IsServer && old.id == value.id && old.amount > 1 &&
                ((old.dataId == value.dataId && old.amount == value.amount && !StackRules.Compatible(old, value)) ||
                 old.dataId != value.dataId))
            {
                Item rest = old;
                rest.id = 0; rest.contId = 0; rest.order = 0; rest.amount--;
                if (old.dataId == value.dataId) value.amount = 1;
                ListItem.SetValue(list, value, new object[] { index });
                // Overflow uses the game's ordinary drop path, preserving copies
                // even when the bag has no free slot after a tool/charge change.
                container.AddNewItem(rest, container.transform.position, false);
                return;
            }
            ListItem.SetValue(list, value, new object[] { index });
        }

        internal static bool ChargeTotal(ContainerNet __instance, ushort __0, ref uint __result)
        {
            ulong total = 0;
            foreach (Item item in __instance.orderedItems)
                if (item.id != 0 && item.dataId == __0) total += (ulong)item.charge * item.amount;
            __result = (uint)Math.Min(total, uint.MaxValue);
            return false;
        }

        internal static bool TakeCharge(ContainerNet __instance, ushort __0, ushort __1, ref ushort __2, ref ushort __result)
        {
            int remaining = __1;
            __2 = 0; __result = __1;
            ItemData data;
            if (!__instance.IsServer || !ItemManager.Instance.GetItemData(__0, out data)) return false;
            // Snapshot ids, then reread each item after every write. A partially
            // depleted stack can split and change the live list/order.
            Item[] snapshot = (Item[])__instance.orderedItems.Clone();
            foreach (Item original in snapshot)
            {
                Item item;
                if (remaining == 0) break;
                if (original.id == 0 || original.dataId != __0 || !__instance.GetItemById(original.id, out item, false) || item.charge == 0) continue;
                int full = Math.Min(item.amount, remaining / item.charge);
                if (full > 0)
                {
                    if (!__instance.RemoveItemAmount(item.id, (ushort)full)) break;
                    remaining -= full * item.charge;
                    __2 += (ushort)full;
                    Item empty = data.containerDirty != null ? new Item(data.containerDirty) : item;
                    empty.id = 0; empty.contId = 0; empty.order = 0; empty.amount = (ushort)full;
                    if (data.containerDirty == null) empty.charge = 0;
                    __instance.AddNewItem(empty, __instance.transform.position, false);
                }
                if (remaining > 0 && __instance.GetItemById(original.id, out item, false) && item.charge > remaining)
                {
                    item.charge -= (ushort)remaining;
                    __instance.SetItem(item, item.order);
                    remaining = 0;
                }
            }
            __result = (ushort)remaining;
            return false;
        }

        internal static bool SellPrice(Item item, ItemData itemData, ref ushort __result)
        {
            item.amount = StackRules.SaleBatch(item, itemData);
            __result = (ushort)StackRules.SaleValue(item, itemData);
            return false;
        }

        internal static bool GetSaleBatch(ContainerNet container, uint id, out Item item, bool warn)
        {
            ItemData data;
            if (!container.GetItemById(id, out item, warn) || !ItemManager.Instance.GetItemData(item.dataId, out data)) return false;
            item.amount = StackRules.SaleBatch(item, data);
            return item.amount > 0;
        }

        internal static bool RemoveSaleBatch(ContainerNet container, uint id)
        {
            Item item;
            if (!GetSaleBatch(container, id, out item, false)) return false;
            return container.RemoveItemAmount(id, item.amount);
        }

        internal static bool ServeAll(ServingTable __instance, ServerRpcParams serverRpcParams)
        {
            // Preserve the generated RPC transport and authorization path.
            object stage = AccessTools.Field(typeof(NetworkBehaviour), "__rpc_exec_stage").GetValue(__instance);
            if (!__instance.IsServer || stage == null || Convert.ToInt32(stage) != 1) return true;
            AccessTools.Field(typeof(NetworkBehaviour), "__rpc_exec_stage").SetValue(__instance, 0);
            ContainerNet container;
            if (ContainerManager.Instance == null || !ContainerManager.Instance.GetPlayerContainer(serverRpcParams.Receive.SenderClientId, out container)) return false;
            ServingTableSlot slot;
            while (__instance.HaveFreeSlot(out slot))
            {
                Item chosen = default(Item);
                for (int i = 0; i < container.orderedItems.Length; i++)
                {
                    Item candidate = container.orderedItems[i]; ItemData data;
                    if (candidate.amount > 0 && ItemManager.Instance.GetItemData(candidate.dataId, out data) && StackRules.IsFood(data.type))
                    { chosen = candidate; break; }
                }
                if (chosen.id == 0 || !RemoveOne(container, chosen.id)) break;
                slot.itemDataId.Value = chosen.dataId;
                slot.itemRarity = chosen.rarity;
            }
            return false;
        }
    }
}
