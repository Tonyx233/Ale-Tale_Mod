// Chest quick stack rules, compiled with minimal game-type doubles. The fake host
// mirrors ItemManager.MoveItemToContServerRpc: unknown id -> no-op; otherwise
// AddNewItem merges same dataId+rarity up to the cap, then fills the first empty
// slot, and any remainder is written back to the source item.
using System;
using System.Collections.Generic;
using System.Linq;
using TonyMods;

public class ItemData { public ushort id; public bool quest, pet; }
public struct Item
{
    public uint id;
    public ushort dataId, amount, order, contId;
    public byte rarity;
}

internal sealed class FakeContainer
{
    public readonly ushort Id;
    public readonly Item[] Slots;
    public FakeContainer(ushort id, int size) { Id = id; Slots = new Item[size]; }
    public void Put(ushort order, uint id, ushort dataId, ushort amount, byte rarity = 0)
    { Slots[order] = new Item { id = id, dataId = dataId, amount = amount, order = order, contId = Id, rarity = rarity }; }
    public int Count(ushort dataId) { return Slots.Where(s => s.id != 0 && s.dataId == dataId).Sum(s => (int)s.amount); }
}

internal static class QuickStackTests
{
    private static int count;
    private static uint nextId = 100000;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); count++; }

    private static readonly Dictionary<ushort, ItemData> Data = new Dictionary<ushort, ItemData>();
    private static ItemData Lookup(ushort id) { ItemData data; return Data.TryGetValue(id, out data) ? data : null; }

    // Server side of MoveItemToContServerRpc(itemId, fromCont, toCont) for one container pair.
    private static void Move(FakeContainer from, FakeContainer to, uint itemId, ushort cap)
    {
        int index = Array.FindIndex(from.Slots, s => s.id == itemId && s.id != 0);
        if (index < 0) return;
        Item moving = from.Slots[index];
        int left = moving.amount;
        for (int i = 0; i < to.Slots.Length && left > 0; i++)
        {
            Item slot = to.Slots[i];
            if (slot.id == 0 || slot.dataId != moving.dataId || slot.rarity != moving.rarity || slot.amount >= cap) continue;
            int add = Math.Min(left, cap - slot.amount);
            to.Slots[i].amount += (ushort)add; left -= add;
        }
        for (int i = 0; i < to.Slots.Length && left > 0; i++)
        {
            if (to.Slots[i].id != 0) continue;
            int add = Math.Min(left, (int)cap);
            to.Put((ushort)i, nextId++, moving.dataId, (ushort)add, moving.rarity);
            left -= add;
        }
        if (left == 0) from.Slots[index] = default(Item);
        else from.Slots[index].amount = (ushort)left;
    }

    private static List<Item> Select(FakeContainer bag, FakeContainer chest, bool generic = false)
    { return QuickStackRules.Select(bag.Slots, chest.Slots, generic, Lookup); }

    public static void Main()
    {
        for (ushort id = 1; id <= 12; id++) Data[id] = new ItemData { id = id };
        Data[9].quest = true;
        Data[10].pet = true;

        // Target containers: player/pet backpack ids (<9), shops and pet containers are refused.
        for (ushort id = 0; id < 9; id++) Check(!QuickStackRules.CanTarget(id, false, false), "Player backpack id " + id + " refused");
        Check(QuickStackRules.CanTarget(9, false, false) && QuickStackRules.CanTarget(4000, false, false), "Storage ids accepted");
        Check(!QuickStackRules.CanTarget(50, true, false), "Shop refused: MoveItemToContServerRpc would bypass selling");
        Check(!QuickStackRules.CanTarget(50, false, true), "Pet container refused");

        FakeContainer bag = new FakeContainer(1, 40), chest = new FakeContainer(20, 30);
        chest.Put(0, 1, 1, 5); chest.Put(3, 2, 2, 1);
        for (ushort slot = 0; slot < 10; slot++) bag.Put(slot, (uint)(10 + slot), 1, 3); // hotbar full of kind 1
        bag.Put(10, 30, 1, 7); bag.Put(11, 31, 3, 4); bag.Put(12, 32, 2, 2, 2); bag.Put(15, 33, 1, 1, 1);
        List<Item> moves = Select(bag, chest);
        Check(moves.Select(m => m.id).SequenceEqual(new uint[] { 30, 32, 33 }), "Kinds already in chest, backpack order, hotbar 1-0 excluded");
        Check(moves.All(m => m.order >= QuickStackRules.HotbarSlots), "No hotbar slot selected");
        Check(moves.All(m => m.contId == bag.Id), "RPC source is the item's own container");
        Check(!moves.Any(m => m.dataId == 3), "Kind missing from chest stays in backpack");

        Check(Select(bag, new FakeContainer(21, 30)).Count == 0, "Empty chest deposits nothing");
        Data[0] = new ItemData { id = 0 };
        FakeContainer zeroKind = new FakeContainer(22, 5); FakeContainer zeroBag = new FakeContainer(1, 20);
        zeroBag.Put(12, 40, 0, 1);
        Check(Select(zeroBag, zeroKind).Count == 0, "Empty chest slots (id 0, dataId 0) do not match dataId 0 items");
        Data.Remove(0);
        FakeContainer ghostBag = new FakeContainer(1, 20);
        ghostBag.Slots[13] = new Item { id = 0, dataId = 1, amount = 1, order = 13, contId = 1 };
        Check(Select(ghostBag, chest).Count == 0, "Backpack entry with id 0 is never sent");
        FakeContainer onlyHotbar = new FakeContainer(1, 20); onlyHotbar.Put(9, 41, 1, 1);
        Check(Select(onlyHotbar, chest).Count == 0, "Last hotbar slot (key 0) kept");
        onlyHotbar.Put(10, 42, 1, 1);
        Check(Select(onlyHotbar, chest).Single().id == 42, "First backpack slot after hotbar deposits");

        FakeContainer quest = new FakeContainer(23, 10); quest.Put(0, 50, 9, 1); quest.Put(1, 51, 11, 1);
        FakeContainer questBag = new FakeContainer(1, 20); questBag.Put(10, 52, 9, 1); questBag.Put(11, 53, 11, 1);
        Check(Select(questBag, quest, true).Single().id == 53, "Quest item never enters dungeon (generic) container");
        Check(Select(questBag, quest, false).Count == 2, "Quest item allowed in normal chest, like vanilla double-click");
        FakeContainer unknown = new FakeContainer(24, 5); unknown.Put(0, 60, 999, 1);
        FakeContainer unknownBag = new FakeContainer(1, 20); unknownBag.Put(10, 61, 999, 1);
        Check(Select(unknownBag, unknown).Count == 0, "Unknown item data skipped");

        // Full chest: host keeps the remainder in the backpack; a stale second press is a no-op.
        FakeContainer full = new FakeContainer(25, 2); full.Put(0, 70, 1, 95); full.Put(1, 71, 4, 1);
        FakeContainer fullBag = new FakeContainer(1, 20); fullBag.Put(10, 72, 1, 10);
        List<Item> stale = Select(fullBag, full);
        foreach (Item item in stale) Move(fullBag, full, item.id, 99);
        Check(full.Count(1) == 99 && fullBag.Count(1) == 6, "Partial fit leaves remainder in backpack");
        foreach (Item item in stale) Move(fullBag, full, item.id, 99);
        Check(full.Count(1) == 99 && fullBag.Count(1) == 6, "Repeat on full chest changes nothing");

        // Randomized: stale repeated presses conserve totals, never touch hotbar or other kinds.
        Random random = new Random(7);
        for (int round = 0; round < 3000; round++)
        {
            ushort cap = round % 3 == 0 ? (ushort)9999 : (ushort)99;
            FakeContainer b = new FakeContainer(1, 40), c = new FakeContainer((ushort)(9 + round), random.Next(1, 31));
            for (ushort s = 0; s < b.Slots.Length; s++) if (random.Next(3) > 0) b.Put(s, nextId++, (ushort)random.Next(1, 13), (ushort)random.Next(1, cap + 1), (byte)random.Next(2));
            for (ushort s = 0; s < c.Slots.Length; s++) if (random.Next(3) > 0) c.Put(s, nextId++, (ushort)random.Next(1, 13), (ushort)random.Next(1, cap + 1), (byte)random.Next(2));
            bool generic = random.Next(4) == 0;
            HashSet<ushort> chestKinds = new HashSet<ushort>(c.Slots.Where(s => s.id != 0).Select(s => s.dataId));
            Dictionary<ushort, int> before = new Dictionary<ushort, int>();
            for (ushort k = 1; k <= 12; k++) before[k] = b.Count(k) + c.Count(k);
            Item[] hotbar = b.Slots.Take(10).ToArray();
            Item[] untouched = b.Slots.Where(s => s.id != 0 && s.order >= 10 && (!chestKinds.Contains(s.dataId) || (generic && Data[s.dataId].quest))).ToArray();
            List<Item> plan = Select(b, c, generic);
            int presses = random.Next(1, 4);
            for (int p = 0; p < presses; p++) foreach (Item item in plan) Move(b, c, item.id, cap);
            for (ushort k = 1; k <= 12; k++) Check(b.Count(k) + c.Count(k) == before[k], "Totals conserved (kind " + k + ", round " + round + ")");
            Check(b.Slots.Take(10).SequenceEqual(hotbar), "Hotbar unchanged (round " + round + ")");
            Check(untouched.All(u => b.Slots[u.order].id == u.id && b.Slots[u.order].amount == u.amount), "Other kinds and blocked quest items unchanged (round " + round + ")");
            Check(c.Slots.Where(s => s.id != 0).All(s => chestKinds.Contains(s.dataId)), "Chest gains no new kinds (round " + round + ")");
        }
        Console.WriteLine("PASS: " + count + " quick stack checks");
    }
}
