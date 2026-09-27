using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// The runner compiles Throw verbatim from TideSummons.cs. Only engine/services below are doubled.
namespace TonyMods
{
    internal partial class TideSummons
    {
        private bool ready = true, friendReady = true;
        private Network network = new Network();
        private Dictionary<ulong, double> uses = new Dictionary<ulong, double>();
        private Dictionary<ulong, float> peers = new Dictionary<ulong, float>();
        private Dictionary<ulong, TideCreature> creatures = new Dictionary<ulong, TideCreature>();
        private Logger log = new Logger();
        internal static double Now = 100;
        private static bool landingWorks = true;
        private static int checks;
        internal class Record
        {
            public ulong id, summoner;
            public string scene;
            public byte action;
            public double started, born;
            public Vector3 from, landing;
            public bool friendly, hunting;
        }
        private void Remove(TideCreature c) { creatures.Remove(c.State.id); SpawnManager.Instance.RemoveById((ushort)c.State.id, true); }
        private static bool Landing(Vector3 feet, Vector3 origin, Vector3 direction, out Vector3 ground) { ground = feet; return landingWorks; }
        private void Broadcast() { }
        private void Note(ulong sender, string message) { }
        private static void Check(bool result, string message) { checks++; if (!result) throw new Exception(message); }
        private TideCreature For(ulong owner) { return creatures.Values.FirstOrDefault(c => c.State.friendly && c.State.summoner == owner); }
        private static ContainerNet Inventory(ulong owner, ushort dataId)
        {
            var inventory = new ContainerNet { item = new Item { dataId = dataId, amount = 3 } };
            ContainerManager.Instance.containers[owner] = inventory;
            PlayerManager.Instance.players[owner] = new PlayerNet();
            return inventory;
        }
        private static void Main()
        {
            var manager = new TideSummons();
            var host = Inventory(0, TideFriendRules.ItemId); var client = Inventory(7, TideFriendRules.ItemId);
            manager.Throw(host, 1, 0);
            Check(manager.For(0) != null && host.item.amount == 3 && host.removals == 0, "friendly summon does not consume item");
            manager.Throw(host, 1, 0);
            Check(manager.creatures.Count == 1, "rapid repeated input cannot create a second companion");
            manager.Throw(client, 1, 7);
            Check(manager.creatures.Count == 2 && manager.For(7) != null, "each player has an independent slot");
            Now += 2; manager.Throw(host, 1, 0);
            Check(manager.For(0) == null && manager.For(7) != null && host.item.amount == 3, "second use recalls only caller's companion");
            Now += 2; manager.Throw(host, 1, 0);
            manager.For(0).hp.hp.Value = 0;
            ulong deadId = manager.For(0).State.id;
            Now += 2; manager.Throw(host, 1, 0);
            Check(manager.For(0).State.id != deadId && manager.creatures.Count == 2 && host.item.amount == 3, "dead body is replaced, never duplicated or charged");
            Check(manager.For(0).hp.hp.Value == TideRules.Health, "new instance is initialized, not a reused dead body");
            Now += 2; manager.Throw(host, 1, 0);
            Now += 2; landingWorks = false; manager.Throw(host, 1, 0);
            Check(manager.For(0) == null && host.item.amount == 3, "bad landing costs no item or slot");
            landingWorks = true; SpawnManager.Instance.fail = true; manager.Throw(host, 1, 0);
            Check(manager.For(0) == null && host.item.amount == 3, "failed native spawn costs no item");
            SpawnManager.Instance.fail = false; TideCreature.fail = true;
            int removed = SpawnManager.Instance.removed;
            try { manager.Throw(host, 1, 0); } catch (InvalidOperationException) { }
            Check(manager.For(0) == null && SpawnManager.Instance.removed == removed+1 && host.item.amount == 3, "initialization failure rolls back native spawn");
            TideCreature.fail = false;
            manager.Throw(client, 1, 0);
            Check(manager.For(0) == null, "another player's inventory cannot summon");
            manager.network.IsServer = false; manager.Throw(host, 1, 0);
            Check(manager.For(0) == null, "client cannot run authoritative use");
            manager.network.IsServer = true; PlayerManager.Instance.players[0].hp.Value = 0;
            manager.Throw(host, 1, 0); Check(manager.For(0) == null, "dead player cannot summon");
            PlayerManager.Instance.players[0].hp.Value = 100;
            manager.network.ConnectedClientsIds.Add(9); manager.Throw(host, 1, 0);
            Check(manager.For(0) == null && host.item.amount == 3, "incompatible peer blocks creation without charging");
            manager.network.ConnectedClientsIds.Remove(9);
            manager.friendReady = false; manager.Throw(host, 1, 0);
            Check(manager.For(0) == null, "missing attribution hook fails closed");
            manager.friendReady = true;
            host.item.dataId = TideRules.ItemId; manager.Throw(host, 1, 0);
            Check(host.item.amount == 2 && host.removals == 1 && manager.creatures.Count == 2, "original hostile item still consumes one and coexists");
            Now += 2; manager.Throw(host, 1, 0);
            Check(host.item.amount == 1 && manager.creatures.Count == 3, "original hostile summons remain uncapped");
            var hunter = new TideCreature { State = new Record { id = 60000, friendly = true, hunting = true, summoner = 0 } };
            manager.creatures.Add(hunter.State.id, hunter);
            host.item.dataId = TideFriendRules.ItemId;
            Now += 2; manager.Throw(host, 1, 0);
            Check(manager.creatures.ContainsKey(60000) && manager.creatures.Values.Any(c => c.State.friendly && !c.State.hunting && c.State.summoner == 0), "building hunter does not occupy companion slot");
            Now += 2; manager.Throw(host, 1, 0);
            Check(manager.creatures.ContainsKey(60000) && !manager.creatures.Values.Any(c => c.State.friendly && !c.State.hunting && c.State.summoner == 0), "recalling companion leaves building hunter alone");
            Console.WriteLine("PASS: " + checks + " production summon/recall, per-owner limit, failures and non-consumption checks");
        }
    }
    internal class TideCreature
    {
        internal TideSummons.Record State;
        internal Vulnerable hp = new Vulnerable();
        internal static bool fail;
        internal void Initialize(TideSummons manager, TideSummons.Record state, bool server)
        { if (fail) throw new InvalidOperationException(); State = state; hp.hp.Value = TideRules.Health; }
        public T GetComponent<T>() where T : class { return hp as T; }
    }
}
namespace UnityEngine
{
    public struct Vector3
    {
        public static Vector3 up, forward;
        public static Vector3 operator +(Vector3 a, Vector3 b) { return a; }
        public static Vector3 operator *(Vector3 a, float b) { return a; }
    }
    public struct Quaternion
    {
        public static Quaternion Euler(float a, float b, float c) { return new Quaternion(); }
        public static Quaternion LookRotation(Vector3 v) { return new Quaternion(); }
        public static Vector3 operator *(Quaternion q, Vector3 v) { return v; }
    }
    public class Transform { public Vector3 position; }
    public static class Time { public static float unscaledTime; }
    public class GameObject { public T AddComponent<T>() where T : new() { return new T(); } }
}
public class NetValue<T> { public T Value; public NetValue(T v) { Value = v; } }
public struct Item { public ushort dataId, amount; }
public class ContainerNet
{
    public Item item;
    public int removals;
    public bool GetItemById(uint id, out Item result, bool flag) { result = item; return id == 1; }
    public bool RemoveItemAmount(uint id, ushort amount) { removals++; item.amount -= amount; return true; }
}
public class ContainerManager
{
    public static ContainerManager Instance = new ContainerManager();
    public Dictionary<ulong, ContainerNet> containers = new Dictionary<ulong, ContainerNet>();
    public bool GetPlayerContainer(ulong id, out ContainerNet c) { return containers.TryGetValue(id, out c); }
}
public class PlayerNet
{
    public bool IsSpawned = true;
    public NetValue<short> hp = new NetValue<short>(100);
    public NetValue<float> hAngleNet = new NetValue<float>(0);
    public Transform transform = new Transform();
}
public class PlayerManager
{
    public static PlayerManager Instance = new PlayerManager();
    public Dictionary<ulong, PlayerNet> players = new Dictionary<ulong, PlayerNet>();
}
public class Master { public static Master Instance; public bool HasConnectingClients() { return false; } }
public class Vulnerable { public NetValue<ushort> hp = new NetValue<ushort>(0); }
public class Spawnable
{
    public enum Type { Spider }
    public bool IsSpawned = true;
    public ulong NetworkObjectId;
    public NetValue<ushort> id = new NetValue<ushort>(0);
    public GameObject gameObject = new GameObject();
}
public class SpawnManager
{
    public static SpawnManager Instance = new SpawnManager();
    public bool fail;
    public int removed;
    private ushort next;
    public bool ManualSpawn(Spawnable.Type type, Vector3 p, Quaternion q, out Spawnable spawn, bool flag)
    { spawn = fail ? null : new Spawnable { NetworkObjectId = ++next, id = new NetValue<ushort>(next) }; return !fail; }
    public void RemoveById(ushort id, bool flag) { removed++; }
}
public class Network
{
    public bool IsServer = true;
    public ulong LocalClientId = 0;
    public List<ulong> ConnectedClientsIds = new List<ulong> { 0 };
}
public class Logger { public void LogInfo(string text) { } }
public struct Scene { public string name; }
public static class SceneManager { public static Scene GetActiveScene() { return new Scene { name = "World" }; } }
