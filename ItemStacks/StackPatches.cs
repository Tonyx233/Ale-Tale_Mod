using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unity.Netcode;

namespace TonyMods
{
    internal static class StackPatches
    {
        internal sealed class Plan
        {
            internal MethodBase Method;
            internal int Capacity, Compare, Setters, Remove, Get, ByData, Mix;
            internal string RemoveTo = "RemoveOne", GetTo = "GetOne";
        }
        internal static readonly List<Plan> Plans = CreatePlans();
        private static MethodInfo Method(Type type, string name, params Type[] args)
        {
            MethodInfo method = args.Length == 0 ? AccessTools.Method(type, name) : AccessTools.Method(type, name, args);
            if (method == null) throw new MissingMethodException(type.FullName, name);
            return method;
        }
        private static List<Plan> CreatePlans()
        {
            var plans = new List<Plan>();
            plans.Add(new Plan { Method = Method(typeof(ContainerNet), "AddNewItem", typeof(Item), typeof(ushort).MakeByRefType(), typeof(bool)), Capacity = 5, Compare = 1, Setters = 1 });
            plans.Add(new Plan { Method = Method(typeof(ContainerNet), "MergeSimilar"), Capacity = 6, Compare = 1, Setters = 2 });
            plans.Add(new Plan { Method = Method(typeof(ContainerNet), "SplitItemServerRpc"), Capacity = 1 });
            plans.Add(new Plan { Method = Method(typeof(ContainerManager), "OnItemDragServerRpc"), Capacity = 10, Compare = 2, Mix = 2 });
            plans.Add(new Plan { Method = Method(typeof(ContainerItemUI), "Upd"), Capacity = 1 });
            plans.Add(new Plan { Method = Method(typeof(InventoryUI), "OnItemMMBClickBP"), Capacity = 1 });
            plans.Add(new Plan { Method = Method(typeof(InventoryUI), "OnItemMMBClickExt"), Capacity = 1 });
            foreach (string name in new[] { "SetItemById", "SetItemByPlace", "SetItem", "AddCharge" })
                plans.Add(new Plan { Method = Method(typeof(ContainerNet), name), Setters = 1 });
            plans.Add(new Plan { Method = Method(typeof(ServingTableSlot), "ServeSingleDishServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(TableFeedPlace), "ServeDish"), ByData = 1 });
            plans.Add(new Plan { Method = Method(typeof(HelperWaiterServeDishState), "TryServeCustomerDish"), ByData = 1 });
            foreach (MethodInfo method in typeof(FurnitureManager).GetMethods(AccessTools.all).Where(m => m.Name == "PlaceFurnitureServerRpc"))
                plans.Add(new Plan { Method = method, Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(InventoryItemUseManager), "UseRecipe"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(ItemManager), "DamageToolServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(ItemPlace), "PutServerRpc"), Remove = 1, Get = 1 });
            plans.Add(new Plan { Method = Method(typeof(ItemTransformer), "InteractServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(BannerManager), "InteractServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(ItemCharger), "InteractServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(WineCrushBasket), "TakeInteractServerRpc"), Remove = 1 });
            plans.Add(new Plan { Method = Method(typeof(LabDevice), "OnItemDropServerRpc"), Remove = 2, RemoveTo = "RemoveConsumed" });
            plans.Add(new Plan { Method = Method(typeof(CookingDevice), "OnItemDropServerRpc"), Remove = 2, RemoveTo = "RemoveConsumed" });
            plans.Add(new Plan { Method = Method(typeof(SupplySource), "Charge"), Remove = 2, RemoveTo = "RemoveConsumed" });
            plans.Add(new Plan { Method = Method(typeof(ItemManager), "SellItemServerRpc"), Remove = 1, Get = 1, RemoveTo = "RemoveSaleBatch", GetTo = "GetSaleBatch" });
            return plans;
        }

        internal static void Validate()
        {
            foreach (Plan plan in Plans)
                Rewrite(PatchProcessor.GetOriginalInstructions(plan.Method), plan).ToList();
            if (AccessTools.Field(typeof(NetworkBehaviour), "__rpc_exec_stage") == null)
                throw new MissingFieldException("NetworkBehaviour.__rpc_exec_stage");
        }

        internal static void Install(Harmony harmony)
        {
            foreach (Plan plan in Plans)
                harmony.Patch(plan.Method, transpiler: new HarmonyMethod(typeof(StackPatches), "Transpile"));
            harmony.Patch(Plans[0].Method, prefix: new HarmonyMethod(typeof(ItemStacks), "AddLarge"));
            harmony.Patch(Method(typeof(ItemManager), "GetItemSellPrice"), prefix: new HarmonyMethod(typeof(ItemStacks), "SellPrice"));
            harmony.Patch(Method(typeof(ServingTable), "ServeDishesServerRpc"), prefix: new HarmonyMethod(typeof(ItemStacks), "ServeAll"));
            harmony.Patch(Method(typeof(ContainerNet), "GetItemCharge"), prefix: new HarmonyMethod(typeof(ItemStacks), "ChargeTotal"));
            harmony.Patch(Method(typeof(ContainerNet), "TryRemoveCharge"), prefix: new HarmonyMethod(typeof(ItemStacks), "TakeCharge"));
        }

        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            return Rewrite(instructions, Plans.Single(p => p.Method == __originalMethod));
        }

        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> instructions, Plan plan)
        {
            var code = instructions.Select(i => new CodeInstruction(i)).ToList();
            FieldInfo max = AccessTools.Field(typeof(ItemData), "maxStack");
            FieldInfo rarity = AccessTools.Field(typeof(Item), "rarity");
            MethodInfo remove = Method(typeof(ContainerNet), "RemoveItemById");
            MethodInfo get = Method(typeof(ContainerNet), "GetItemById");
            MethodInfo byData = Method(typeof(ContainerNet), "RemoveItemByDataId");
            MethodInfo setter = AccessTools.PropertySetter(AccessTools.Field(typeof(ContainerNet), "items").FieldType, "Item");
            int caps = 0, compares = 0, sets = 0, removes = 0, gets = 0, dataCalls = 0, mixes = 0;
            for (int i = 0; i < code.Count; i++)
            {
                CodeInstruction op = code[i];
                if (plan.Capacity > 0 && op.opcode == OpCodes.Ldfld && Equals(op.operand, max))
                { op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), "Capacity"); caps++; }
                if (plan.Compare > 0 && i + 3 < code.Count && op.opcode == OpCodes.Ldfld && Equals(op.operand, rarity) &&
                    code[i + 2].opcode == OpCodes.Ldfld && Equals(code[i + 2].operand, rarity) &&
                    (code[i + 3].opcode == OpCodes.Bne_Un || code[i + 3].opcode == OpCodes.Bne_Un_S))
                {
                    op.opcode = OpCodes.Nop; op.operand = null;
                    code[i + 2].opcode = OpCodes.Call; code[i + 2].operand = Method(typeof(StackRules), "Compatible");
                    code[i + 3].opcode = OpCodes.Brfalse;
                    compares++;
                }
                if (plan.Setters > 0 && Calls(op, setter))
                {
                    var self = new CodeInstruction(OpCodes.Ldarg_0);
                    self.labels.AddRange(op.labels); op.labels.Clear();
                    self.blocks.AddRange(op.blocks); op.blocks.Clear();
                    code.Insert(i++, self);
                    op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), "SetPreservingCopies"); sets++;
                }
                if (plan.Remove > 0 && Calls(op, remove))
                { op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), plan.RemoveTo); removes++; }
                if (plan.Mix > 0 && Calls(op, remove) && i + 4 < code.Count &&
                    code[i + 4].operand is ConstructorInfo && ((ConstructorInfo)code[i + 4].operand).DeclaringType == typeof(Item))
                { op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), "RemoveOne"); mixes++; }
                if (plan.Get > 0 && Calls(op, get))
                { op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), plan.GetTo); gets++; }
                if (plan.ByData > 0 && Calls(op, byData))
                { op.opcode = OpCodes.Call; op.operand = Method(typeof(ItemStacks), "RemoveOneByData"); dataCalls++; }
            }
            if (caps != plan.Capacity || compares != plan.Compare || sets != plan.Setters || removes != plan.Remove || gets != plan.Get || dataCalls != plan.ByData || mixes != plan.Mix)
                throw new InvalidOperationException("Stack patch layout changed: " + plan.Method +
                    " actual capacity/compare/set/remove/get/data/mix=" + caps + "/" + compares + "/" + sets + "/" + removes + "/" + gets + "/" + dataCalls + "/" + mixes);
            return code;
        }

        private static bool Calls(CodeInstruction op, MethodInfo method)
        { return (op.opcode == OpCodes.Call || op.opcode == OpCodes.Callvirt) && Equals(op.operand, method); }
    }
}
