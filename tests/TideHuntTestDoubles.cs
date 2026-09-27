using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
namespace HarmonyLib { public static class AccessTools { public static FieldInfo Field(Type t, string n) { return t.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); } public static MethodInfo Method(Type t, string n) { return t.GetMethod(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance); } } }
namespace Unity.Netcode { public class NetworkVariable<T> { public T Value; public NetworkVariable(T value) { Value = value; } } }
namespace UnityEngine
{
    public class Object { public static void Destroy(object o){} }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform { get { return gameObject.transform; } }
        public T GetComponent<T>() where T : class { return gameObject.Get<T>(); }
        public T GetComponentInParent<T>() where T : class { return GetComponent<T>(); }
    }
    public class GameObject
    {
        public readonly List<Component> components = new List<Component>();
        public int scene;
        public Transform transform = new Transform();
        public T Add<T>() where T : Component,new() { var c = new T(); c.gameObject = this; components.Add(c); return c; }
        public T Get<T>() where T:class { foreach(var c in components) if(c is T) return c as T; return null; }
    }
    public class Transform { public Vector3 position; public Vector3 forward = new Vector3(0,0,1); }
    public struct Vector3
    {
        public float x,y,z;
        public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 up { get { return new Vector3(0,1,0); } }
        public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
        public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
        public static Vector3 operator *(Vector3 a,float b){return new Vector3(a.x*b,a.y*b,a.z*b);}
        public static float Distance(Vector3 a,Vector3 b){var p=a-b;return (float)Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z);}
    }
    public struct Vector2Int
    {
        public int x,y; public Vector2Int(int a,int b){x=a;y=b;}
        public static Vector2Int zero { get { return new Vector2Int(); } }
        public static bool operator ==(Vector2Int a,Vector2Int b){return a.x==b.x&&a.y==b.y;}
        public static bool operator !=(Vector2Int a,Vector2Int b){return !(a==b);}
        public override bool Equals(object b){return b is Vector2Int && this==(Vector2Int)b;}
        public override int GetHashCode(){return x^y;}
    }
    public struct Quaternion { public static Quaternion identity {get{return new Quaternion();}} }
    public static class Random { public static float value=.1f; public static int Pick; public static int Range(int a,int b){return Math.Min(b-1, Math.Max(a,Pick));} }
}
namespace UnityEngine.AI
{
    public enum NavMeshPathStatus { PathComplete, PathPartial }
    public class NavMeshPath { public NavMeshPathStatus status; }
    public struct NavMeshHit { public UnityEngine.Vector3 position; }
    public static class NavMesh
    {
        public static bool SamplePosition(UnityEngine.Vector3 p,out NavMeshHit n,float radius,int mask){n=new NavMeshHit{position=p};return true;}
    }
    public class NavMeshAgent : UnityEngine.Component
    {
        public bool enabled=true,isOnNavMesh=true,isStopped,pathPending,reachable=true;
        public int areaMask; public float stoppingDistance; public UnityEngine.Vector3 destination;
        public bool CalculatePath(UnityEngine.Vector3 p,NavMeshPath path){path.status=reachable?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return true;}
        public void SetDestination(UnityEngine.Vector3 p){destination=p;}
        public bool Warp(UnityEngine.Vector3 p){transform.position=p;return true;}
        public void ResetPath(){}
    }
}
public class Spawnable : UnityEngine.Component { public bool isDungeon; public Species type=Species.Wolf; public enum Species { Wolf,Spider,Rabbit,SpiderBoss,OrcHostage } }
public class CreatureBase : UnityEngine.Component
{
    public bool IsSpawned=true,doNotDespawnUntilLooted;
    public List<LootItem> lootItems=new List<LootItem>();
    public Unity.Netcode.NetworkVariable<bool> isLootAvailable=new Unity.Netcode.NetworkVariable<bool>(false);
    public Unity.Netcode.NetworkVariable<ItemData.Rarity> rarity=new Unity.Netcode.NetworkVariable<ItemData.Rarity>(ItemData.Rarity.Rare);
    public UnityEngine.Vector2Int lootMoney;
}
public class CreatureHostile : CreatureBase{}
public class CreaturePaddockAnimal : UnityEngine.Component{}
public class PaddockAnimal : UnityEngine.Component{}
public class PetBase : UnityEngine.Component{}
public class QuestGiver : UnityEngine.Component{}
public class Vulnerable : UnityEngine.Component
{
    public Unity.Netcode.NetworkVariable<ushort> hp=new Unity.Netcode.NetworkVariable<ushort>(100);
    public Unity.Netcode.NetworkVariable<bool> invinsible=new Unity.Netcode.NetworkVariable<bool>(false),isActive=new Unity.Netcode.NetworkVariable<bool>(true);
    public LootItem[] dropItems=new LootItem[0]; public int hits;
    public void Hit(ushort d,ushort w){hits++; hp.Value=(ushort)Math.Max(0,hp.Value-d); if(hp.Value==0)GetComponent<CreatureBase>().isLootAvailable.Value=true;}
}
public class ItemData { public ushort id=1,maxStack=1; public bool quest,hasRarity=true; public enum Rarity:byte { Common,Rare } }
public class LootItem { public ItemData itemData=new ItemData(); public float dropChance=1; public ushort amountOverride; public UnityEngine.Vector2Int amountMinMax; }
public struct Item
{
    public uint id; public ushort dataId,amount,charge,durability,contId,order; public ItemData.Rarity rarity;
    public Item(ItemData d){id=0;dataId=d.id;amount=d.maxStack;charge=7;durability=8;contId=order=0;rarity=ItemData.Rarity.Common;}
}
public class ContainerNet
{
    private uint nextId=1;
    public int Capacity=1000; public List<Item> data=new List<Item>();
    public Item[] orderedItems {get{return data.ToArray();}}
    public bool AddNewItem(Item item,out ushort remaining,bool b)
    {
        int take=Math.Min(item.amount, Math.Max(0, Capacity-data.Sum(i=>(int)i.amount)));
        remaining=(ushort)(item.amount-take); item.amount=(ushort)take;
        if(take>0){item.id=nextId++;data.Add(item);}return take>0;
    }
    public bool RemoveItemById(uint id){return data.RemoveAll(i=>i.id==id)>0;}
}
public class CollectibleNet : UnityEngine.Component
{
    public bool IsSpawned=true; public ItemData itemData=new ItemData();
    public Unity.Netcode.NetworkVariable<Item> item=new Unity.Netcode.NetworkVariable<Item>(new Item());
    public void PickupByHelper(ContainerNet bag){ushort left; bag.AddNewItem(item.Value,out left,false);var i=item.Value;i.amount=left;item.Value=i;}
}
public class CollectibleManager
{
    public static CollectibleManager Instance=new CollectibleManager(); public List<CollectibleNet> drops=new List<CollectibleNet>(); public bool Reject,ThrowAfterSpawn,ThrowBeforeSpawn;
    private uint nextId=1; private Dictionary<uint,CollectibleNet> byId=new Dictionary<uint,CollectibleNet>();
    public uint GenId(){return nextId++;}
    public bool GetCollectible(uint id,out CollectibleNet c,bool warn){return byId.TryGetValue(id,out c);}
    public void RemoveCollectible(uint id){drops.Remove(byId[id]);byId.Remove(id);}
    public CollectibleNet Spawn(Item item,UnityEngine.Vector3 p,UnityEngine.Quaternion q,uint id,bool persist)
    {
        if(Reject)return null; var c=new UnityEngine.GameObject().Add<CollectibleNet>();c.transform.position=p;c.item.Value=item;drops.Add(c);byId[id]=c;
        if(ThrowBeforeSpawn){c.IsSpawned=false;throw new Exception("spawn failed");}if(ThrowAfterSpawn)throw new Exception("callback failed");return c;
    }
}
public class ItemManager {public static ItemManager Instance=new ItemManager(); public void CheckWeapon(ref Item i,bool b){} }
public class GameStatus {public static GameStatus Instance=new GameStatus();public ulong Gold; public void AddMoney(ulong g){Gold+=g;} }
public class Master {public static Master Instance=new Master(); public bool Joining;public bool HasConnectingClients(){return Joining;} }
public class HelperHouse { public bool IsSpawned=true,IsServer=true;public Unity.Netcode.NetworkVariable<bool> isHelperActive=new Unity.Netcode.NetworkVariable<bool>(true); }
namespace TonyMods
{
    public static class TideSummons {public static double Now=100;}
    public class TideCreature : UnityEngine.Component {public int taunts,releases;public void Taunt(CreatureHostile c){taunts++;}public void ReleaseTaunts(){releases++;}}
    internal static class TideHuntBuilding
    {
        internal static HashSet<Spawnable> Natural=new HashSet<Spawnable>();
        internal static Dictionary<Vulnerable,TideHunter> Victims=new Dictionary<Vulnerable,TideHunter>();
    }
    public partial class TideHuntHome : UnityEngine.Component
    {
        public HelperHouse House=new HelperHouse();public ContainerNet Cargo=new ContainerNet();
        public bool Active {get{return House.isHelperActive.Value;}}
        public int Count {get{return Cargo.data.Sum(i=>(int)i.amount);}}
        public UnityEngine.Vector3 delivery=new UnityEngine.Vector3(5,0,0);
        public bool GetDelivery(out UnityEngine.Vector3 p){p=delivery;return true;}
    }
}
