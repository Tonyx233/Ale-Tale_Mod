using System;
using System.Collections.Generic;

namespace TonyMods
{
    // Pure selection rules for chest quick stack, kept free of Unity calls so tests
    // can compile them against game-type doubles.
    internal static class QuickStackRules
    {
        // PlayerInventory hotbar = inventory places 0-9 (number keys 1-0, scroll wraps at 9).
        internal const ushort HotbarSlots = 10;
        // InventoryUI shows the player/pet backpack switcher for container ids below 9.
        internal const ushort FirstStorageId = 9;

        internal static bool CanTarget(ushort containerId, bool isShop, bool isPet)
        {
            return containerId >= FirstStorageId && !isShop && !isPet;
        }

        // Backpack items outside the hotbar whose dataId already exists in the chest,
        // in backpack order. Inputs are ContainerNet.orderedItems, where id 0 marks an
        // empty slot. Rarity/durability may differ: the host merges what it can
        // and places the rest in empty slots, leaving anything that does not fit.
        internal static List<Item> Select(IEnumerable<Item> backpack, IEnumerable<Item> chest, bool chestIsGeneric, Func<ushort, ItemData> lookup)
        {
            HashSet<ushort> kinds = new HashSet<ushort>();
            foreach (Item item in chest) if (item.id != 0) kinds.Add(item.dataId);
            List<Item> moves = new List<Item>();
            if (kinds.Count == 0) return moves;
            foreach (Item item in backpack)
            {
                if (item.id == 0 || item.order < HotbarSlots || !kinds.Contains(item.dataId)) continue;
                ItemData data = lookup(item.dataId);
                if (data == null) continue;
                // Mirrors InventoryUI.OnItemDoubleClickBP; MoveItemToContServerRpc does not check it.
                if (data.quest && chestIsGeneric) continue;
                moves.Add(item);
            }
            return moves;
        }
    }
}
