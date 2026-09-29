using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace TonyMods
{
    // Reuses the registered owl-house prefab and its exact NGO layout. The native container name carries the
    // variant marker BEFORE network spawn, including late joins; cargo is saved with the native furniture.
    internal static class TideHuntBuilding
    {
        internal const string Marker = "Tony.TideHunt.1";
        private static ItemData item;
        internal static readonly HashSet<Spawnable> Natural = new HashSet<Spawnable>();
        internal static readonly Dictionary<Vulnerable, TideHunter> Victims = new Dictionary<Vulnerable, TideHunter>();
        private static TideHunter dropping;

        internal static void Initialize(Harmony harmony)
        {
            foreach (MethodInfo m in AccessTools.GetDeclaredMethods(typeof(FurnitureManager)).Where(m => m.Name == "PlaceFurnitureServerRpc"))
                harmony.Patch(m, transpiler: new HarmonyMethod(typeof(TideHuntBuilding), "Place"));
            harmony.Patch(AccessTools.Method(typeof(FurnitureManager), "OnNetworkSpawn"), transpiler: new HarmonyMethod(typeof(TideHuntBuilding), "Load"));
            Hook(harmony, typeof(Furniture), "OnNetworkSpawn", "FurnitureSpawn", false);
            Hook(harmony, typeof(HelperHouse), "OnNetworkSpawn", "HouseSpawn", false);
            Hook(harmony, typeof(HelperHouse), "OnNetworkDespawn", "HouseDespawn", false);
            Hook(harmony, typeof(ContainerNet), "OnNetworkSpawn", "ContainerSpawn", false);
            Hook(harmony, typeof(SpawnManager), "Spawn", "BeforeNatural", false);
            Hook(harmony, typeof(SpawnManager), "Spawn", "AfterNatural", true);
            Hook(harmony, typeof(Spawnable), "OnNetworkDespawn", "ForgetNatural", true);
            harmony.Patch(AccessTools.Method(typeof(Vulnerable), "DropItems"), prefix: new HarmonyMethod(typeof(TideHuntBuilding), "BeforeDrops"),
                finalizer: new HarmonyMethod(typeof(TideHuntBuilding), "AfterDrops"));
            Hook(harmony, typeof(CollectibleManager), "Spawn", "Dropped", true);
        }
        private static void Hook(Harmony h, Type t, string method, string patch, bool post)
        {
            MethodInfo m = AccessTools.Method(t, method);
            if (m == null) throw new MissingMethodException(t.Name, method);
            var p = new HarmonyMethod(typeof(TideHuntBuilding), patch);
            h.Patch(m, prefix: post ? null : p, postfix: post ? p : null);
        }
        internal static void Register(List<ItemData> items)
        {
            ItemData existing = items.FirstOrDefault(i => i != null && i.id == TideHuntRules.ItemId);
            if (existing != null)
            {
                if (existing.name != "TonyTideHuntName") throw new InvalidOperationException("Item ID collision: 47942");
                item = existing; return;
            }
            ItemData template = items.FirstOrDefault(i => i != null && i.type == ItemData.Type.Furniture && i.netPrefab != null &&
                i.netPrefab.GetComponent<HelperHouse>() != null && i.netPrefab.GetComponent<HelperHouse>().helperWaiterPrefab is HelperCleaner);
            if (template == null) throw new InvalidOperationException("Native cleaner owl house not found");
            item = UnityEngine.Object.Instantiate(template);
            item.id = TideHuntRules.ItemId; item.name = "TonyTideHuntName"; item.itemDescription = "TonyTideHuntDescription";
            item.price = 1; item.shopItem = true; item.buyByOne = true; item.maxStack = 1;
            item.levelDependant = 0; item.questDependant = 0; item.quest = false; item.doNotSave = false;
            items.Add(item);
        }
        // Preserve the existing factory call so Farmer Owl can route its own item IDs.
        // Decorating its result works both before and after Farmer Owl's transpiler.
        private static IEnumerable<CodeInstruction> Place(IEnumerable<CodeInstruction> source) { return Rewrite(source, false); }
        private static IEnumerable<CodeInstruction> Load(IEnumerable<CodeInstruction> source) { return Rewrite(source, true); }
        private static bool IsFurnitureFactory(MethodInfo method, bool load)
        {
            if (method == null || !method.IsStatic || method.ReturnType != typeof(GameObject)) return false;
            bool native = method.DeclaringType == typeof(UnityEngine.Object) && method.Name == "Instantiate";
            bool farmer = method.DeclaringType.FullName == "KinkoCraft.FarmerOwl.PrefabPatch" &&
                method.DeclaringType.Assembly.GetName().Name == "KinkoCraft.FarmerOwl" &&
                method.Name == (load ? "InstantiateFurnitureOnLoad" : "InstantiateFurniture");
            if (!native && !farmer) return false;
            Type[] expected = load ? new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion), typeof(Transform) }
                : new[] { typeof(GameObject), typeof(Vector3), typeof(Quaternion) };
            if (farmer) expected = expected.Concat(new[] { typeof(uint) }).ToArray();
            return method.GetParameters().Select(p => p.ParameterType).SequenceEqual(expected);
        }
        private static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> source, bool load)
        {
            var code = source.ToList(); int found = 0;
            for (int i = 0; i < code.Count; i++)
            {
                if (code[i].opcode != OpCodes.Call || !IsFurnitureFactory(code[i].operand as MethodInfo, load)) continue;
                // Loading also creates default furniture; only the first factory uses SavedDevice.
                if (load && found > 0) continue;
                var mark = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TideHuntBuilding), "Mark"));
                // Keep end-of-exception metadata after the complete stack operation.
                mark.blocks.AddRange(code[i].blocks.Where(b => b.blockType == ExceptionBlockType.EndExceptionBlock));
                code[i].blocks.RemoveAll(b => b.blockType == ExceptionBlockType.EndExceptionBlock);
                code.Insert(++i, new CodeInstruction(OpCodes.Ldloc_2));
                code.Insert(++i, new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(load ? typeof(SavedDevice) : typeof(Item), load ? "itemDataId" : "dataId")));
                code.Insert(++i, mark);
                found++;
            }
            if (found != 1) throw new InvalidOperationException("Hunt house instantiate contract changed: " + found);
            return code;
        }
        private static GameObject Mark(GameObject go, ushort id)
        {
            if (id != TideHuntRules.ItemId) return go;
            go.GetComponent<Furniture>().itemData = item;
            go.GetComponent<HelperHouse>().container.contName.Value = Marker;
            return go;
        }
        internal static bool IsHouse(Component component)
        {
            var house = component == null ? null : component.GetComponent<HelperHouse>();
            if (house == null || house.container == null) return false;
            var f = house.GetComponent<Furniture>();
            return f != null && f.itemData != null && f.itemData.id == TideHuntRules.ItemId || house.container.contName.Value.ToString() == Marker;
        }
        private static void FurnitureSpawn(Furniture __instance)
        { if (IsHouse(__instance) && item != null) __instance.itemData = item; }
        private static void ContainerSpawn(ContainerNet __instance)
        { if (IsHouse(__instance)) { __instance.ignoreInteraction = true; __instance.showSettings = false; } }
        private static bool HouseSpawn(HelperHouse __instance)
        {
            if (!IsHouse(__instance)) return true;
            var f = __instance.GetComponent<Furniture>(); f.itemData = item;
            if (__instance.GetComponent<TideHuntHome>() == null) __instance.gameObject.AddComponent<TideHuntHome>().Initialize(__instance);
            return false;
        }
        private static bool HouseDespawn(HelperHouse __instance)
        {
            if (!IsHouse(__instance)) return true;
            var home = __instance.GetComponent<TideHuntHome>(); if (home != null) home.Close();
            return false;
        }
        private static void BeforeNatural(SpawnManager __instance, out HashSet<ushort> __state)
        { __state = new HashSet<ushort>(__instance.spawnables.Keys); }
        private static void AfterNatural(SpawnManager __instance, HashSet<ushort> __state)
        {
            foreach (var p in __instance.spawnables) if (!__state.Contains(p.Key)) Natural.Add(p.Value);
        }
        private static void ForgetNatural(Spawnable __instance)
        { Natural.Remove(__instance); var hp = __instance.GetComponent<Vulnerable>(); if (hp != null) Victims.Remove(hp); }
        private static void BeforeDrops(Vulnerable __instance, out TideHunter __state)
        {
            __state = dropping; TideHunter hunter;
            dropping = Victims.TryGetValue(__instance, out hunter) ? hunter : null;
        }
        private static void AfterDrops(TideHunter __state) { dropping = __state; }
        private static void Dropped(CollectibleNet __result)
        { if (dropping != null && __result != null) dropping.Drops.Add(__result); }
    }
}
