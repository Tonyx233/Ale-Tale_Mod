using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using TonyMods;
using HarmonyLib;

internal static class ItemStackTests
{
    public sealed class Legacy { public static System.Collections.Generic.Dictionary<ushort, ushort> OriginalMaxById = new System.Collections.Generic.Dictionary<ushort, ushort>(); }
    private static int checks;
    private static void Check(bool condition, string message)
    { checks++; if (!condition) throw new Exception(message); }
    private static ItemData Data(ushort id, ushort max, ushort price, ItemData.Type type)
    {
        var data = new ItemData { id = id, maxStack = max, price = price, type = type };
        ItemManager.Instance.data[id] = data; return data;
    }
    private static ContainerNet Bag(ItemData data, ushort amount, int slots = 20)
    {
        var bag = new ContainerNet { slots = slots };
        Item item = new Item(data); item.amount = amount;
        bag.AddNewItem(item, new UnityEngine.Vector3(), false); return bag;
    }
    private static int Count(ContainerNet bag, ushort dataId)
    { return bag.items.Concat(bag.dropped).Where(i => i.dataId == dataId).Sum(i => (int)i.amount); }
    public static int Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string file = Path.Combine(args[0], "BepInEx", "core", new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(file) ? Assembly.LoadFrom(file) : null;
        };
        try { Run(args[0]); Console.WriteLine("PASS: " + checks + " stack checks; real game IL plans=" + StackPatches.Plans.Count + ". Unity/network runtime remains untested."); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Run(string game)
    {
        PatchProcessor.GameAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(game, "Ale and Tale Tavern_Data", "Managed", "Assembly-CSharp.dll"));
        StackPatches.Validate();
        foreach (var plan in StackPatches.Plans)
        {
            var code = PatchProcessor.GetOriginalInstructions(plan.Method);
            var rewritten = StackPatches.Rewrite(code, plan).ToList();
            Check(rewritten.Count >= code.Count, "Unexpected IL removal " + plan.Method);
            bool failed = false;
            try { StackPatches.Rewrite(new CodeInstruction[0], plan).ToList(); }
            catch (InvalidOperationException) { failed = true; }
            Check(failed, "Changed API must fail closed " + plan.Method);
        }
        var material = Data(1, 5, 10, ItemData.Type.Material);
        Legacy.OriginalMaxById[1] = 5;
        BepInEx.Bootstrap.Chainloader.PluginInfos[ItemStacks.LegacyStack] = new BepInEx.PluginInfo { Instance = new Legacy() };
        BepInEx.Bootstrap.Chainloader.PluginInfos[ItemStacks.LegacyFood] = new BepInEx.PluginInfo { Instance = new Legacy() };
        UnityEngine.Resources.Loaded = new object[] { material };
        material.maxStack = 99; Harmony.FailInstall = true;
        bool rollback = false;
        try { ItemStacks.Initialize(new BepInEx.Logging.ManualLogSource()); } catch (InvalidOperationException) { rollback = true; }
        Check(rollback && material.maxStack == 99 && Harmony.Restored.Count == 2, "Failed takeover restores both legacy modules and values");
        Harmony.FailInstall = false;
        ItemStacks.Initialize(new BepInEx.Logging.ManualLogSource());
        Check(material.maxStack == 5, "Takeover recovers vanilla quantity from legacy snapshot");
        var food = Data(2, 1, 10, ItemData.Type.Food);
        var tool = Data(3, 1, 30, ItemData.Type.Tool); tool.durability = 100;
        var bottle = Data(4, 1, 20, ItemData.Type.Drink); bottle.maxCharge = 10;
        var dirty = Data(5, 1, 2, ItemData.Type.Material); bottle.containerDirty = dirty; bottle.containerClean = dirty;
        foreach (ushort original in new ushort[] { 0, 1, 2, 99, 999, 9999, 65535 })
        {
            var data = Data(9, original, 1, ItemData.Type.Material);
            Check(ItemStacks.Capacity(data) == 9999, "All item capacities");
            Check(data.maxStack == original, "Vanilla generation amount must be unchanged");
        }
        Item a = new Item(tool), b = a;
        Check(StackRules.Compatible(a, b), "Identical tools stack");
        foreach (string field in new[] { "dataId", "rarity", "charge", "durability", "repairCount", "metaInt", "weaponDamage", "weaponDamageDeviation", "weaponChargedHitDamageMul" })
        {
            object changed = a; var info = typeof(Item).GetField(field);
            object value = info.FieldType.IsEnum ? Enum.ToObject(info.FieldType, 1) : Convert.ChangeType(Convert.ToInt32(info.GetValue(a)) + 1, info.FieldType);
            info.SetValue(changed, value);
            Check(!StackRules.Compatible(a, (Item)changed), "Metadata mismatch " + field);
        }
        b.id = 123; b.order = 4; b.contId = 2; b.amount = 55;
        Check(StackRules.Compatible(a, b), "Identity/order are not stack metadata");
        foreach (ushort amount in new ushort[] { 10000, 19998, 19999, 65535 })
            foreach (int slots in new[] { 0, 1, 2, 8 })
            {
                var bag = new ContainerNet { slots = slots };
                Item input = new Item(material); input.amount = amount;
                ushort left = 0; bool result = false;
                Check(!ItemStacks.AddLarge(bag, input, ref left, false, ref result), "Large requests handled");
                Check(bag.items.Sum(i => (int)i.amount) + left == amount, "No loss on full/partial bag");
                Check(bag.items.All(i => i.amount <= 9999), "Each new stack <= 9999");
                Check(result == (left < amount), "Partial success result");
            }
        var foodBag = Bag(food, 9999);
        uint foodId = foodBag.items[0].id;
        Check(ItemStacks.RemoveOne(foodBag, foodId) && Count(foodBag, food.id) == 9998, "Serve one by id");
        Item serving;
        Check(ItemStacks.RemoveOneByData(foodBag, food.id, out serving) && serving.amount == 1 && Count(foodBag, food.id) == 9997, "Waiter one serving");
        Check(ItemStacks.GetOne(foodBag, foodId, out serving, false) && serving.amount == 1 && Count(foodBag, food.id) == 9997, "World placement takes a copy of one");
        ContainerManager.Instance.player = foodBag;
        var table = new ServingTable { __rpc_exec_stage = 1 };
        Check(!ItemStacks.ServeAll(table, new Unity.Netcode.ServerRpcParams()), "RPC execute handled");
        Check(table.slots.All(s => s.itemDataId.Value == food.id) && Count(foodBag, food.id) == 9991, "Six slots consume exactly six");
        Check(table.__rpc_exec_stage == 0, "RPC stage restored");
        Check(ItemStacks.ServeAll(new ServingTable { IsServer = false }, new Unity.Netcode.ServerRpcParams()), "Client RPC transport retained");
        foreach (int slots in new[] { 1, 20 })
        {
            var bag = Bag(tool, 9999, slots); Item changed = bag.items[0]; changed.durability = 99;
            ItemStacks.SetPreservingCopies(bag.items, 0, changed, bag);
            Check(Count(bag, tool.id) == 9999, "Tool copy conservation");
            Check(bag.items[0].amount == 1 && bag.items[0].durability == 99, "Only used tool damaged");
            Check(bag.items.Concat(bag.dropped).Where(i => i.durability == 100).Sum(i => (int)i.amount) == 9998, "Remaining tools unchanged including overflow");
        }
        var mix = Bag(material, 99); Item replacement = new Item(food); replacement.id = mix.items[0].id;
        ItemStacks.SetPreservingCopies(mix.items, 0, replacement, mix);
        Check(Count(mix, material.id) == 98 && Count(mix, food.id) == 1, "Mix replaces only one destination");
        var chargeBag = Bag(bottle, 9999);
        uint chargeTotal = 0; ItemStacks.ChargeTotal(chargeBag, bottle.id, ref chargeTotal);
        Check(chargeTotal == 99990, "Charge total counts all copies");
        ushort removed = 0, remaining = 0;
        ItemStacks.TakeCharge(chargeBag, bottle.id, 25, ref removed, ref remaining);
        Check(removed == 2 && remaining == 0, "Two full bottles and one partial");
        Check(Count(chargeBag, bottle.id) == 9997 && Count(chargeBag, dirty.id) == 2, "Empty bottle conservation");
        ItemStacks.ChargeTotal(chargeBag, bottle.id, ref chargeTotal);
        Check(chargeTotal == 99965, "Charge conservation after partial split");
        var insufficient = Bag(bottle, 2);
        ItemStacks.TakeCharge(insufficient, bottle.id, 25, ref removed, ref remaining);
        Check(removed == 2 && remaining == 5 && Count(insufficient, dirty.id) == 2, "Insufficient charge reports exact remainder");
        var chargedUse = Bag(bottle, 9999);
        Check(ItemStacks.RemoveConsumed(chargedUse, chargedUse.items[0].id) && Count(chargedUse, bottle.id) == 9998, "Cooking one charged container");
        var bulkUse = Bag(material, 99);
        Check(ItemStacks.RemoveConsumed(bulkUse, bulkUse.items[0].id) && Count(bulkUse, material.id) == 0, "Cooking full ingredient branch stays bulk");
        Item sale = new Item(material); sale.amount = 9999;
        Check(StackRules.SaleValue(sale, material) == 19998, "Material price uses original bundle size");
        sale = new Item(food); sale.amount = 9999;
        Check(StackRules.SaleValue(sale, food) == 99990 && StackRules.SaleBatch(sale, food) == 6553, "Sale batches fit UInt16");
        var saleBag = Bag(food, 9999);
        Item batch;
        Check(ItemStacks.GetSaleBatch(saleBag, saleBag.items[0].id, out batch, false) && batch.amount == 6553, "Sale quote quantity");
        Check(ItemStacks.RemoveSaleBatch(saleBag, saleBag.items[0].id) && Count(saleBag, food.id) == 3446, "Unsold copies retained");
        for (int price = 0; price <= 65535; price += 257)
        {
            food.price = (ushort)price;
            ushort size = StackRules.SaleBatch(sale, food); Item probe = sale; probe.amount = size;
            Check(StackRules.SaleValue(probe, food) <= 65535, "No money overflow");
            if (size < sale.amount) { probe.amount++; Check(StackRules.SaleValue(probe, food) > 65535, "Largest safe batch"); }
        }
    }
}
