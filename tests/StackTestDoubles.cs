// Test-only game services. Production sources are compiled unchanged alongside
// these doubles; Cecil reads actual game IL without executing Unity/Harmony.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Mono.Cecil;
using TonyMods;

public class ItemData
{
    public enum Type { Material = 0, Food = 4, Dish = 5, Drink = 6, Tool = 10 }
    public enum Rarity { Common, One, Two, Three, Four }
    public ushort id, maxStack = 1, price, maxCharge, durability;
    public bool hasRarity;
    public Type type;
    public ItemData containerClean, containerDirty;
    public Dictionary<Rarity, ushort> shopRarityPrices;
}
public struct Item
{
    public uint id;
    public ushort dataId, amount, charge, durability, repairCount, order, contId;
    public int metaInt;
    public ItemData.Rarity rarity;
    public byte weaponDamage, weaponDamageDeviation, weaponChargedHitDamageMul;
    public Item(ItemData data) : this() { dataId = data.id; amount = data.maxStack; charge = data.maxCharge; durability = data.durability; }
}
namespace UnityEngine
{
    public struct Vector3 { }
    public class Transform { public Vector3 position; }
    public static class Resources { public static object[] Loaded = new object[0]; public static T[] FindObjectsOfTypeAll<T>() { return Loaded.OfType<T>().ToArray(); } }
}
namespace Unity.Netcode
{
    public class NetworkBehaviour { public bool IsServer = true; public int __rpc_exec_stage; public UnityEngine.Transform transform = new UnityEngine.Transform(); }
    public class NetworkList<T> : List<T> { }
    public class NetworkVariable<T> { public T Value; }
    public struct ServerRpcReceiveParams { public ulong SenderClientId; }
    public struct ServerRpcParams { public ServerRpcReceiveParams Receive; }
}
namespace BepInEx
{
    public class PluginInfo { public object Instance; }
}
namespace BepInEx.Bootstrap
{
    public static class Chainloader { public static Dictionary<string, BepInEx.PluginInfo> PluginInfos = new Dictionary<string, BepInEx.PluginInfo>(); }
}
namespace BepInEx.Logging
{
    public class ManualLogSource { public void LogInfo(object value) { } }
}
public enum GameEvent { Removed = 16 }
public class Game
{
    public static Game Instance = new Game();
    public void InvokeGameEvent(GameEvent e, uint id, int count) { }
}
public class ContainerNet : Unity.Netcode.NetworkBehaviour
{
    public Unity.Netcode.NetworkList<Item> items = new Unity.Netcode.NetworkList<Item>();
    public Item[] orderedItems { get { return items.ToArray(); } }
    public List<Item> dropped = new List<Item>();
    public int slots = 20;
    private static uint nextId = 1;
    public bool AddNewItem(Item item, out ushort leftAmount, bool doNotMerge)
    {
        int left = item.amount;
        if (!doNotMerge)
            for (int i = 0; i < items.Count && left > 0; i++)
            {
                Item old = items[i];
                if (!StackRules.Compatible(old, item)) continue;
                int add = Math.Min(left, StackRules.Limit - old.amount);
                old.amount += (ushort)add; left -= add; items[i] = old;
            }
        if (left > 0 && items.Count < slots)
        { item.id = nextId++; item.amount = (ushort)left; item.order = (ushort)items.Count; items.Add(item); left = 0; }
        leftAmount = (ushort)left;
        return left < item.amount;
    }
    public void AddNewItem(Item item, UnityEngine.Vector3 pos, bool noMerge)
    {
        ushort left;
        AddNewItem(item, out left, noMerge);
        if (left > 0) { item.amount = left; dropped.Add(item); }
    }
    public bool GetItemById(uint id, out Item item, bool warn)
    { foreach (Item current in items) if (current.id == id) { item = current; return true; } item = default(Item); return false; }
    public bool RemoveItemById(uint id) { int i = items.FindIndex(x => x.id == id); if (i < 0) return false; items.RemoveAt(i); return true; }
    public bool RemoveItemByDataId(ushort id, out Item item) { item = default(Item); return false; }
    public bool RemoveItemAmount(uint id, ushort amount)
    {
        int i = items.FindIndex(x => x.id == id);
        if (i < 0 || items[i].amount < amount) return false;
        Item item = items[i]; item.amount -= amount;
        if (item.amount == 0) items.RemoveAt(i); else items[i] = item;
        return true;
    }
    public void SetItem(Item item, ushort order)
    { int i = items.FindIndex(x => x.id == item.id); ItemStacks.SetPreservingCopies(items, i, item, this); }
    public bool SetItemById(uint id, Item item) { SetItem(item, item.order); return true; }
    public bool SetItemByPlace(ushort place, Item item) { return true; }
    public void AddCharge() { }
    public void MergeSimilar() { }
    public void SplitItemServerRpc() { }
    public void GetItemCharge() { }
    public void TryRemoveCharge() { }
}
public class ItemManager
{
    public static ItemManager Instance = new ItemManager();
    public Dictionary<ushort, ItemData> data = new Dictionary<ushort, ItemData>();
    public bool GetItemData(uint id, out ItemData item) { return data.TryGetValue((ushort)id, out item); }
    public void DamageToolServerRpc() { }
    public void SellItemServerRpc() { }
    public void GetItemSellPrice() { }
}
public class ContainerManager
{
    public static ContainerManager Instance = new ContainerManager();
    public ContainerNet player;
    public bool GetPlayerContainer(ulong id, out ContainerNet result) { result = player; return player != null; }
    public void OnItemDragServerRpc() { }
}
public class ServingTableSlot
{
    public Unity.Netcode.NetworkVariable<ushort> itemDataId = new Unity.Netcode.NetworkVariable<ushort>();
    public ItemData.Rarity itemRarity;
    public void ServeSingleDishServerRpc() { }
}
public class ServingTable : Unity.Netcode.NetworkBehaviour
{
    public ServingTableSlot[] slots = Enumerable.Range(0, 6).Select(i => new ServingTableSlot()).ToArray();
    public bool HaveFreeSlot(out ServingTableSlot slot) { slot = slots.FirstOrDefault(s => s.itemDataId.Value == 0); return slot != null; }
    public void ServeDishesServerRpc() { }
}
public class TableFeedPlace { public void ServeDish() { } }
public class HelperWaiterServeDishState { public void TryServeCustomerDish() { } }
public class FurnitureManager { public void PlaceFurnitureServerRpc(uint id, ushort place, Unity.Netcode.ServerRpcParams rpc) { } public void PlaceFurnitureServerRpc(uint id, UnityEngine.Vector3 position, object rotation, Unity.Netcode.ServerRpcParams rpc) { } }
public class InventoryItemUseManager { public void UseRecipe() { } }
public class ItemPlace { public void PutServerRpc() { } }
public class ItemTransformer { public void InteractServerRpc() { } }
public class ItemCharger { public void InteractServerRpc() { } }
public class BannerManager { public void InteractServerRpc() { } }
public class WineCrushBasket { public void TakeInteractServerRpc() { } }
public class LabDevice { public void OnItemDropServerRpc() { } }
public class CookingDevice { public void OnItemDropServerRpc() { } }
public class SupplySource { public void Charge() { } }
public class ContainerItemUI { public void Upd() { } }
public class InventoryUI { public void OnItemMMBClickBP() { } public void OnItemMMBClickExt() { } }

namespace HarmonyLib
{
    public class Harmony
    {
        public static bool FailInstall;
        public static List<string> Restored = new List<string>();
        private string id;
        public Harmony(string owner) { id = owner; }
        public static void UnpatchID(string id) { }
        public void UnpatchSelf() { }
        public void PatchAll(Assembly assembly) { Restored.Add(id); }
        public void Patch(MethodBase m, HarmonyMethod prefix = null, HarmonyMethod transpiler = null)
        { if (FailInstall) throw new InvalidOperationException("Simulated patch failure"); }
    }
    public class HarmonyMethod { public HarmonyMethod(System.Type type, string name) { } }
    public class CodeInstruction
    {
        public System.Reflection.Emit.OpCode opcode;
        public object operand;
        public List<object> labels = new List<object>(), blocks = new List<object>();
        public CodeInstruction(System.Reflection.Emit.OpCode op, object value = null) { opcode = op; operand = value; }
        public CodeInstruction(CodeInstruction other) { opcode = other.opcode; operand = other.operand; labels.AddRange(other.labels); blocks.AddRange(other.blocks); }
    }
    public static class AccessTools
    {
        public const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        public static FieldInfo Field(System.Type t, string name) { return t.GetField(name, all); }
        public static MethodInfo Method(System.Type t, string name, System.Type[] args = null) { return args == null ? t.GetMethod(name, all) : t.GetMethod(name, all, null, args, null); }
        public static MethodInfo PropertySetter(System.Type t, string name) { return t.GetProperty(name).GetSetMethod(); }
    }
    public static class PatchProcessor
    {
        public static AssemblyDefinition GameAssembly;
        public static List<CodeInstruction> GetOriginalInstructions(MethodBase method)
        {
            var type = GameAssembly.MainModule.Types.Single(t => t.Name == method.DeclaringType.Name);
            var candidates = type.Methods.Where(m => m.Name == method.Name).ToArray();
            var source = candidates.Length == 1 ? candidates[0] : candidates.Single(m =>
                m.Parameters.Count == method.GetParameters().Length && (method.Name != "AddNewItem" || m.ReturnType.FullName == "System.Boolean"));
            var opcodes = typeof(System.Reflection.Emit.OpCodes).GetFields().Where(f => f.FieldType == typeof(System.Reflection.Emit.OpCode))
                .Select(f => (System.Reflection.Emit.OpCode)f.GetValue(null)).ToDictionary(op => op.Name);
            var result = new List<CodeInstruction>();
            foreach (var instruction in source.Body.Instructions)
            {
                object operand = instruction.Operand;
                var field = operand as FieldReference;
                if (field != null && (field.DeclaringType.Name == "Item" || field.DeclaringType.Name == "ItemData"))
                    operand = AccessTools.Field(field.DeclaringType.Name == "Item" ? typeof(Item) : typeof(ItemData), field.Name) ?? operand;
                var call = operand as MethodReference;
                if (call != null)
                {
                    if (call.DeclaringType.Name.StartsWith("NetworkList") && call.Name == "set_Item")
                        operand = AccessTools.PropertySetter(typeof(Unity.Netcode.NetworkList<Item>), "Item");
                    else if (call.DeclaringType.Name == "Item" && call.Name == ".ctor")
                        operand = typeof(Item).GetConstructor(new[] { typeof(ItemData) });
                    else if (call.DeclaringType.Name == "ContainerNet" && new[] { "RemoveItemById", "RemoveItemByDataId", "GetItemById" }.Contains(call.Name))
                        operand = AccessTools.Method(typeof(ContainerNet), call.Name);
                }
                result.Add(new CodeInstruction(opcodes[instruction.OpCode.Name], operand));
            }
            return result;
        }
    }
}
