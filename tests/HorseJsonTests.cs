using System;
using System.Reflection;
using UnityEngine;

class HorseJsonTests
{
    static Assembly mod;
    static Type codec;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS: " + name); }
    static object RoundTrip(object value)
    {
        string json = (string)codec.GetMethod("Serialize").Invoke(null, new[] { value });
        Check(!json.Contains("normalized") && json.Length < 4096, "bounded coordinate-only JSON");
        return codec.GetMethod("Deserialize").MakeGenericMethod(value.GetType()).Invoke(null, new object[] { json });
    }
    static void Main()
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args) { string name = new AssemblyName(args.Name).Name + ".dll"; string root = Environment.GetEnvironmentVariable("HORSE_TEST_GAME"); foreach (string dir in new[] { "Ale and Tale Tavern_Data/Managed", "BepInEx/core" }) { string path = System.IO.Path.Combine(root, dir, name); if (System.IO.File.Exists(path)) return Assembly.LoadFrom(path); } return null; };
        Run();
    }
    static void Run()
    {
        mod = Assembly.LoadFrom("Tony.TeammateHealthBars.dll");
        codec = mod.GetType("TonyMods.HorseJson", true);
        Type recordType = mod.GetType("TonyMods.HorseStable+Record", true);
        object record = Activator.CreateInstance(recordType);
        string id = Guid.NewGuid().ToString("N");
        recordType.GetField("id").SetValue(record, id);
        recordType.GetField("scene").SetValue(record, "Tavern 測試");
        recordType.GetField("position").SetValue(record, new Vector3(12.5f, -2.25f, 80));
        recordType.GetField("yaw").SetValue(record, 135f);
        Type snapshotType = mod.GetType("TonyMods.HorseStable+Snapshot", true);
        object snapshot = Activator.CreateInstance(snapshotType);
        Array records = Array.CreateInstance(recordType, 1); records.SetValue(record, 0);
        snapshotType.GetField("horses").SetValue(snapshot, records);
        object copy = RoundTrip(snapshot);
        object horse = ((Array)snapshotType.GetField("horses").GetValue(copy)).GetValue(0);
        Check((string)recordType.GetField("id").GetValue(horse) == id, "manifest preserves horse identity");
        Check((string)recordType.GetField("scene").GetValue(horse) == "Tavern 測試", "manifest preserves scene");
        Vector3 position = (Vector3)recordType.GetField("position").GetValue(horse);
        Check(position.x == 12.5f && position.y == -2.25f && position.z == 80, "manifest preserves position");
        Check((float)recordType.GetField("yaw").GetValue(horse) == 135f, "manifest preserves yaw");
        snapshotType.GetField("horses").SetValue(snapshot, Array.CreateInstance(recordType, 0));
        Check(((Array)snapshotType.GetField("horses").GetValue(RoundTrip(snapshot))).Length == 0, "empty manifest clears horses");
        Type wireType = mod.GetType("TonyMods.TavernHorse+Wire", true);
        object wire = Activator.CreateInstance(wireType, true);
        wireType.GetField("op").SetValue(wire, 4);
        wireType.GetField("revision").SetValue(wire, 42);
        wireType.GetField("exists").SetValue(wire, true);
        wireType.GetField("occupants").SetValue(wire, "0,1");
        object state = RoundTrip(wire);
        Check((int)wireType.GetField("op").GetValue(state) == 4 && (int)wireType.GetField("revision").GetValue(state) == 42 && (bool)wireType.GetField("exists").GetValue(state) && (string)wireType.GetField("occupants").GetValue(state) == "0,1", "private riding packet preserves state");
        bool rejected = false;
        try { codec.GetMethod("Deserialize").MakeGenericMethod(recordType).Invoke(null, new object[] { "{\"position\":{\"x\":1}}" }); }
        catch (TargetInvocationException) { rejected = true; }
        Check(rejected, "incomplete coordinates rejected");
    }
}

