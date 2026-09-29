using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using HarmonyLib;

class TideHuntCompatibilityTests
{
    static int checks;
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); checks++; }
    static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in new[] { "Ale and Tale Tavern_Data/Managed", "BepInEx/core" }) {
                string path = Path.Combine(Environment.GetEnvironmentVariable("HORSE_TEST_GAME"), dir, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Run(args[0]);
    }
    static List<CodeInstruction> Apply(MethodInfo patch, List<CodeInstruction> code)
    { return ((IEnumerable<CodeInstruction>)patch.Invoke(null,new object[]{code})).ToList(); }
    static void Run(string farmerPath)
    {
        var mod = Assembly.LoadFrom("Tony.TeammateHealthBars.dll");
        var hunt = mod.GetType("TonyMods.TideHuntBuilding",true);
        var farmer = Assembly.LoadFrom(farmerPath).GetType("KinkoCraft.FarmerOwl.PrefabPatch",true);
        foreach (var original in typeof(FurnitureManager).GetMethods(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)
            .Where(m => m.Name=="PlaceFurnitureServerRpc" || m.Name=="OnNetworkSpawn"))
        {
            bool load=original.Name=="OnNetworkSpawn";
            var ours=AccessTools.Method(hunt,load?"Load":"Place");
            var other=AccessTools.Method(farmer,load?"Furniture_OnNetworkSpawn":"RouteInstantiateThroughDataId");
            var wrapper=AccessTools.Method(farmer,load?"InstantiateFurnitureOnLoad":"InstantiateFurniture");
            for(int order=0;order<3;order++)
            {
                var code=PatchProcessor.GetOriginalInstructions(original,(ILGenerator)null);
                int nativeCount=code.Count(c=>c.operand is MethodInfo && ((MethodInfo)c.operand).DeclaringType==typeof(UnityEngine.Object) && ((MethodInfo)c.operand).Name=="Instantiate");
                if(order==0) code=Apply(ours,code);
                if(order==1) code=Apply(ours,Apply(other,code));
                if(order==2) code=Apply(other,Apply(ours,code));
                var marks=code.Select((c,i)=>new {c,i}).Where(x=>x.c.operand as MethodInfo==AccessTools.Method(hunt,"Mark")).ToArray();
                Check(marks.Length==1,"Exactly one hunter marker: "+original+" order="+order);
                int at=marks[0].i;
                Check(code[at-2].opcode==OpCodes.Ldloc_2,"Marker reads native item/save local");
                Check((code[at-1].operand as FieldInfo).Name==(load?"itemDataId":"dataId"),"Correct item ID source");
                var factory=code[at-3].operand as MethodInfo;
                Check(factory!=null && factory.ReturnType==typeof(UnityEngine.GameObject),"Marker follows existing factory result");
                int remaining=code.Count(c=>c.operand is MethodInfo && ((MethodInfo)c.operand).DeclaringType==typeof(UnityEngine.Object) && ((MethodInfo)c.operand).Name=="Instantiate");
                Check(remaining==nativeCount-(order==0?0:1),"No unrelated instantiate was changed");
                if(order!=0) Check(factory==wrapper && code.Count(c=>c.operand as MethodInfo==wrapper)==1,"Farmer Owl routing retained");
                Console.WriteLine("PASS: "+original.Name+" params="+original.GetParameters().Length+" order="+order);
            }
            for(int order=0;order<2;order++)
            {
                var oursHarmony=new Harmony("TonyTests.Hunt");
                var farmerHarmony=new Harmony("TonyTests.Farmer");
                try {
                    if(order==0) { farmerHarmony.Patch(original,transpiler:new HarmonyMethod(other)); oursHarmony.Patch(original,transpiler:new HarmonyMethod(ours)); }
                    else { oursHarmony.Patch(original,transpiler:new HarmonyMethod(ours)); farmerHarmony.Patch(original,transpiler:new HarmonyMethod(other)); }
                    Check(Harmony.GetPatchInfo(original).Transpilers.Count==2,"Both transpilers compile together under game Mono");
                } finally { oursHarmony.UnpatchSelf(); farmerHarmony.UnpatchSelf(); }
            }
            bool rejected=false;
            try { Apply(ours,new List<CodeInstruction>()); }
            catch(TargetInvocationException e) { rejected=e.InnerException is InvalidOperationException; }
            Check(rejected,"Unknown factory shape fails closed");
        }
        Console.WriteLine("PASS: "+checks+" production transpiler checks using supplied Farmer Owl DLL and native game IL");
    }
}
