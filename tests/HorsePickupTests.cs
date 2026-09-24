using System;
using System.Collections.Generic;
using TonyMods;

// Synthetic host state drives the production X-store methods; the native hold UI and Netcode remain gameplay checks.
struct Vector3
{
    public float x, y, z;
    public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    public static Vector3 operator +(Vector3 a, Vector3 b) { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
    public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
}
struct Quaternion
{
    public float yaw;
    public static Quaternion Inverse(Quaternion q) { return new Quaternion { yaw = -q.yaw }; }
    public static Vector3 operator *(Quaternion q, Vector3 v) { double r = q.yaw * Math.PI / 180; return new Vector3((float)(v.x * Math.Cos(r) + v.z * Math.Sin(r)), v.y, (float)(-v.x * Math.Sin(r) + v.z * Math.Cos(r))); }
}
static class Mathf
{
    public static float Max(float a, float b) { return Math.Max(a, b); }
    public static float Abs(float v) { return Math.Abs(v); }
    public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
}
static class Time { public static float unscaledTime; }
class Transform { public Vector3 position; }
class PlayerNet { public Transform transform = new Transform(); public bool alive = true; }
class BoxCollider { public bool enabled = true; }
class Interactive
{
    public enum Event { None = 0, LookOver = 8, RemoveHold = 128, RemoveHoldBegin = 256, RemoveHoldCancel = 512 }
    public bool IsRemoveAvailable { get; set; }
}
class Flag { public bool Value; }
class FurnitureManager { public static FurnitureManager Instance; public Flag forbidClientsRemoveFurniture = new Flag(); }
class Net { public ulong LocalClientId = 0; public bool IsServer = true; public List<ulong> ConnectedClientsIds = new List<ulong>(); }
class Logger { public void LogInfo(string text) { } }
class Wire { public int op; public Vector3 position; }
class ItemData { public ushort id; }
struct Item { public ushort dataId, amount; public Item(ItemData data) { dataId = data.id; amount = 1; } }
class ContainerNet
{
    public int Free = 1;
    public readonly List<Item> Items = new List<Item>();
    public bool AddNewItem(Item item, out ushort left, bool doNotMerge)
    {
        left = item.amount;
        if (Free <= 0) return false;
        Free--; Items.Add(item); left = 0; return true;
    }
}
class ContainerManager
{
    public static ContainerManager Instance = new ContainerManager();
    public readonly Dictionary<ulong, ContainerNet> Players = new Dictionary<ulong, ContainerNet>();
    public bool GetPlayerContainer(ulong id, out ContainerNet container) { return Players.TryGetValue(id, out container); }
}
class ItemManager
{
    public static ItemManager Instance = new ItemManager();
    public readonly Dictionary<uint, ItemData> Data = new Dictionary<uint, ItemData>();
    public bool GetItemData(uint id, out ItemData data) { return Data.TryGetValue(id, out data); }
}

partial class TavernHorse
{
    public string Id = "horse-a";
    public int Variant;
    public object gameObject = new object();
    float Extension;
    Vector3 vehiclePosition;
    Quaternion vehicleRotation;
    HorseSeats seats = new HorseSeats(2);
    object cart = new object();
    Net network = new Net();
    bool collected;
    int localSeat = -1, broadcasts, collects;
    float driverGraceUntil;
    BoxCollider parkedCollider = new BoxCollider(), pickupCollider = new BoxCollider();
    Interactive pickup = new Interactive();
    Logger log = new Logger();
    public Func<TavernHorse, ulong, bool> Collect { get; set; }
    readonly Dictionary<ulong, float> peers = new Dictionary<ulong, float>(), lastRequest = new Dictionary<ulong, float>();
    readonly Dictionary<ulong, PlayerNet> players = new Dictionary<ulong, PlayerNet>();
    static readonly List<TavernHorse> all = new List<TavernHorse>();
    readonly List<string> notes = new List<string>();
    readonly List<int> requests = new List<int>();
    PlayerNet Player(ulong id) { PlayerNet p; return players.TryGetValue(id, out p) ? p : null; }
    static bool Alive(PlayerNet p) { return p != null && p.alive; }
    bool HasRider(ulong id) { return seats.Find(id) >= 0; }
    bool IsAirborne() { return false; }
    void Note(ulong target, string text) { notes.Add(target + ":" + text); }
    void ApplySeat() { }
    float MountDistance(Vector3 player) { return 1; }
    bool MountPathBlocked(PlayerNet player) { return false; }
    bool FindExit(PlayerNet player, out Vector3 exit) { exit = new Vector3(); return true; }
    void ExitAt(Vector3 exit) { }
    void Send(ulong target, Wire packet) { }
    void Broadcast() { broadcasts++; }
    void Request(int op) { requests.Add(op); }

    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    static bool Near(float a, float b) { return Math.Abs(a - b) < .001f; }
    static bool Says(string text, string part) { return text != null && text.Contains(part); }
    static PlayerNet At(float x, float y, float z) { var p = new PlayerNet(); p.transform.position = new Vector3(x, y, z); return p; }
    static TavernHorse Host(bool collectResult)
    {
        var h = new TavernHorse();
        h.players[0] = At(0, 0, 2);
        h.Collect = (horse, sender) => { h.collects++; if (horse != h) throw new Exception("wrong horse"); return collectResult; };
        return h;
    }
    static void Advance() { Time.unscaledTime += 1; }

    static void Main()
    {
        // Distance from the player to the same box as the parked collider.
        var h = new TavernHorse();
        Check(Near(h.BodyDistance(new Vector3(0, 1, 0)), 0), "Inside the body is distance zero");
        Check(Near(h.BodyDistance(new Vector3(.45f + 2, 1, 0)), 2), "Side distance measured from the flank");
        Check(Near(h.BodyDistance(new Vector3(0, 2.4f + 1.5f, 0)), 1.5f), "Height above the saddle counts");
        Check(Near(h.BodyDistance(new Vector3(.45f + 3, 1, 1.35f + 4)), 5), "Corner distance is Euclidean");
        h.vehicleRotation = new Quaternion { yaw = 90 };
        Check(Near(h.BodyDistance(new Vector3(0, 1, 1.35f + 2)), 3.35f - .45f), "Distance rotates with the horse");
        h.vehicleRotation = new Quaternion(); h.vehiclePosition = new Vector3(10, 0, -4);
        Check(Near(h.BodyDistance(new Vector3(10 + .45f + 1, 0, -4)), 1), "Distance follows the parked position");
        h.vehiclePosition = new Vector3(); h.Extension = HorseVariant.Extension;
        Check(Near(h.BodyDistance(new Vector3(0, 1, -1.35f - HorseVariant.Extension - 1)), 1), "Horse 2 tail extends the body");
        Check(Near(h.BodyDistance(new Vector3(0, 1, 1.35f + 2)), 2), "Horse 2 head keeps the original front");

        // Host-side refusal rules.
        h = new TavernHorse();
        Check(h.PickupRefusal(0, At(0, 0, 1.35f + 2.9f)) == null, "Empty nearby horse can be stored");
        Check(h.PickupRefusal(0, At(0, 0, 1.35f + PickupRange - .01f)) == null, "Store allowed just inside range");
        Check(Says(h.PickupRefusal(0, At(0, 0, 1.35f + PickupRange + .05f)), "closer"), "Remote store is refused");
        h.seats.Board(7);
        Check(Says(h.PickupRefusal(0, At(0, 0, 2)), "dismount"), "Occupied horse is refused");
        h.seats.Clear(); FurnitureManager.Instance = new FurnitureManager();
        FurnitureManager.Instance.forbidClientsRemoveFurniture.Value = true;
        Check(Says(h.PickupRefusal(5, At(0, 0, 2)), "host"), "Guest follows native furniture removal lock");
        Check(h.PickupRefusal(0, At(0, 0, 2)) == null, "Host may store despite guest lock");
        FurnitureManager.Instance.forbidClientsRemoveFurniture.Value = false;
        Check(h.PickupRefusal(5, At(0, 0, 2)) == null, "Guest may store when furniture removal is allowed");
        h.cart = null;
        Check(Says(h.PickupRefusal(0, At(0, 0, 2)), "not ready"), "Missing body is refused");

        // Native prompt availability mirrors the blocking box and permissions.
        h = new TavernHorse(); h.UpdatePickup();
        Check(h.pickupCollider.enabled && h.pickup.IsRemoveAvailable, "Parked horse offers native X store");
        h.parkedCollider.enabled = false; h.UpdatePickup();
        Check(!h.pickupCollider.enabled && !h.pickup.IsRemoveAvailable, "Ridden horse hides X target");
        h.parkedCollider.enabled = true; h.collected = true; h.UpdatePickup();
        Check(!h.pickupCollider.enabled && !h.pickup.IsRemoveAvailable, "Stored horse hides X target");
        h.collected = false; h.network.IsServer = false; FurnitureManager.Instance.forbidClientsRemoveFurniture.Value = true; h.UpdatePickup();
        Check(h.pickupCollider.enabled && !h.pickup.IsRemoveAvailable, "Locked guest sees horse but no X store");
        h.network.IsServer = true; h.UpdatePickup();
        Check(h.pickup.IsRemoveAvailable, "Host keeps X store while guests are locked");
        FurnitureManager.Instance.forbidClientsRemoveFurniture.Value = false;
        h.pickup = null; h.UpdatePickup();
        Check(true, "Missing native component is tolerated");

        // Only a completed native hold sends the store request.
        h = new TavernHorse();
        h.PickupInteract(Interactive.Event.RemoveHoldBegin, 0, 0); h.PickupInteract(Interactive.Event.LookOver, 0, 0); h.PickupInteract(Interactive.Event.RemoveHoldCancel, 0, 0);
        Check(h.requests.Count == 0, "Hold start, look and cancel do not store");
        h.PickupInteract(Interactive.Event.RemoveHold, 0, 0);
        Check(h.requests.Count == 1 && h.requests[0] == 3, "Completed X hold requests op 3");
        h.localSeat = 0; h.PickupInteract(Interactive.Event.RemoveHold, 0, 0);
        Check(h.requests.Count == 1, "Rider cannot store");

        // Production request gate and op 3 wiring.
        h = Host(true); Advance();
        h.ServerRequest(0, new Wire { op = 0 }); Advance();
        h.ServerRequest(0, new Wire { op = 3 });
        Check(h.collects == 1 && h.collected && h.broadcasts == 0, "Host store marks horse collected without seat broadcast");
        Advance(); h.ServerRequest(0, new Wire { op = 3 }); h.ServerRequest(0, new Wire { op = 1 });
        Check(h.collects == 1 && h.seats.Count == 0, "Collected horse ignores later store and mount");

        h = Host(true); h.players[5] = At(0, 0, -2); Advance();
        h.ServerRequest(5, new Wire { op = 3 });
        Check(h.collects == 0, "Disconnected sender ignored");
        h.network.ConnectedClientsIds.Add(5); h.ServerRequest(5, new Wire { op = 3 });
        Check(h.collects == 0, "Sender without horse handshake ignored");
        h.ServerRequest(5, new Wire { op = 0 }); Advance(); h.ServerRequest(5, new Wire { op = 3 });
        Check(h.collects == 1 && h.collected, "Guest store reaches the stable");

        h = Host(false); h.ServerRequest(0, new Wire { op = 0 }); Advance();
        h.ServerRequest(0, new Wire { op = 3 });
        Check(h.collects == 1 && !h.collected && h.broadcasts == 0, "Failed store keeps the horse");
        h.ServerRequest(0, new Wire { op = 3 });
        Check(h.collects == 1, "Store requests share the 0.25s rate limit");
        Advance(); h.seats.Board(9); h.ServerRequest(0, new Wire { op = 3 });
        Check(h.collects == 1 && h.notes.Count == 1 && Says(h.notes[0], "dismount"), "Occupied horse refused before stable");
        Advance(); h.seats.Clear(); h.players[0].alive = false; h.ServerRequest(0, new Wire { op = 3 });
        Check(h.collects == 1, "Dead sender ignored");
        h.players[0].alive = true; Advance(); h.ServerRequest(0, new Wire { op = 4 });
        Check(h.collects == 1 && h.broadcasts == 0, "Host-only state op is not accepted from clients");

        HorseStable.Run();
        Console.WriteLine("PASS: " + checks + " production horse X-store scenarios");
    }
}

partial class HorseStable
{
    public const ushort ItemId = 47920, Item2Id = 47921;
    Net network = new Net();
    bool itemReady = true, item2Ready = true;
    readonly Dictionary<string, TavernHorse> horses = new Dictionary<string, TavernHorse>();
    readonly List<string> notes = new List<string>();
    readonly List<object> destroyed = new List<object>();
    int broadcasts;
    Logger log = new Logger();
    void Note(ulong id, string text) { notes.Add(id + ":" + text); }
    void Destroy(object o) { destroyed.Add(o); }
    void Broadcast() { broadcasts++; }

    static int checks;
    static void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks++; Console.WriteLine("PASS: " + name); }
    bool Told(string part) { return notes.Count > 0 && notes[0].Contains(part); }
    static HorseStable Stable(out TavernHorse horse, out ContainerNet bag, int variant = HorseVariant.Original)
    {
        var s = new HorseStable();
        horse = new TavernHorse { Id = "h" + variant, Variant = variant };
        s.horses[horse.Id] = horse;
        bag = new ContainerNet();
        ContainerManager.Instance = new ContainerManager(); ContainerManager.Instance.Players[5] = bag;
        ItemManager.Instance = new ItemManager();
        ItemManager.Instance.Data[ItemId] = new ItemData { id = ItemId };
        ItemManager.Instance.Data[Item2Id] = new ItemData { id = Item2Id };
        return s;
    }

    internal static void Run()
    {
        TavernHorse horse; ContainerNet bag;
        var s = Stable(out horse, out bag);
        Check(s.CollectHorse(horse, 5), "Original horse stored");
        Check(bag.Items.Count == 1 && bag.Items[0].dataId == ItemId && bag.Items[0].amount == 1, "Inventory receives one original horse item");
        Check(!s.horses.ContainsKey(horse.Id) && s.destroyed.Count == 1 && s.destroyed[0] == horse.gameObject, "Stored horse leaves the stable");
        Check(s.broadcasts == 1 && s.notes.Count == 1 && s.Told("5:Horse returned"), "Manifest and owner notice sent");
        Check(!s.CollectHorse(horse, 5) && bag.Items.Count == 1, "Second store cannot duplicate the item");

        s = Stable(out horse, out bag, HorseVariant.Extended);
        Check(s.CollectHorse(horse, 5) && bag.Items[0].dataId == Item2Id, "Horse 2 returns the Horse 2 item");

        s = Stable(out horse, out bag); bag.Free = 0;
        Check(!s.CollectHorse(horse, 5), "Full inventory refuses store");
        Check(s.horses.ContainsKey(horse.Id) && s.destroyed.Count == 0 && s.broadcasts == 0 && s.Told("full"), "Full inventory keeps the horse parked");

        s = Stable(out horse, out bag); s.horses[horse.Id] = new TavernHorse { Id = horse.Id };
        Check(!s.CollectHorse(horse, 5) && bag.Items.Count == 0 && s.destroyed.Count == 0, "Stale horse reference refused");

        s = Stable(out horse, out bag); s.network.IsServer = false;
        Check(!s.CollectHorse(horse, 5) && bag.Items.Count == 0, "Clients cannot store");
        s = Stable(out horse, out bag); s.network = null;
        Check(!s.CollectHorse(horse, 5) && bag.Items.Count == 0, "Offline stable cannot store");

        s = Stable(out horse, out bag, HorseVariant.Extended); s.item2Ready = false;
        Check(!s.CollectHorse(horse, 5) && bag.Items.Count == 0 && s.horses.ContainsKey(horse.Id) && s.Told("unavailable"), "Disabled Horse 2 item keeps the horse");
        s = Stable(out horse, out bag); ItemManager.Instance.Data.Remove(ItemId);
        Check(!s.CollectHorse(horse, 5) && s.horses.ContainsKey(horse.Id), "Missing item data keeps the horse");
        s = Stable(out horse, out bag);
        Check(!s.CollectHorse(horse, 6) && s.horses.ContainsKey(horse.Id) && s.Told("not found"), "Missing inventory keeps the horse");
        Console.WriteLine("PASS: " + checks + " production stable store scenarios");
    }
}
