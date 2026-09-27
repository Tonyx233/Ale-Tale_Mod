using System;
using System.Collections.Generic;

// Narrow offline doubles for the production TideFriend controller, not a replacement implementation.
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float a, float b, float c) { x = a; y = b; z = c; }
        public float sqrMagnitude { get { return x*x+y*y+z*z; } }
        public static Vector3 operator -(Vector3 a, Vector3 b) { return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z); }
        public static Vector3 operator *(Vector3 a, float b) { return new Vector3(a.x*b,a.y*b,a.z*b); }
        public static float Distance(Vector3 a, Vector3 b) { return (float)Math.Sqrt((a-b).sqrMagnitude); }
    }
    public class Transform { public Vector3 position; public Vector3 forward = new Vector3(0,0,1); }
    public class Component
    {
        public Transform transform = new Transform();
        private Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public void Attach(Component c) { components[c.GetType()] = c; c.components = components; components[GetType()] = this; }
        public T GetComponent<T>() where T : Component { Component c; return this is T ? (T)this : components.TryGetValue(typeof(T), out c) ? (T)c : null; }
        public T GetComponentInParent<T>() where T : Component { return GetComponent<T>(); }
    }
    public class Collider : Component { }
    public static class Physics
    {
        public static Collider[] colliders = new Collider[0];
        public static Collider[] OverlapSphere(Vector3 p, float radius) { return colliders; }
    }
}
namespace UnityEngine.AI
{
    public struct NavMeshHit { public UnityEngine.Vector3 position; }
    public struct NavMeshQueryFilter { public int agentTypeID, areaMask; }
    public static class NavMesh
    {
        public static bool SamplePosition(UnityEngine.Vector3 p, out NavMeshHit hit, float radius, NavMeshQueryFilter filter)
        { hit = new NavMeshHit { position = p }; return true; }
    }
    public class NavMeshAgent
    {
        public bool enabled = true, isOnNavMesh = true, isStopped, pathPending;
        public int agentTypeID, areaMask, destinations, warps;
        public float stoppingDistance;
        public UnityEngine.Vector3 lastDestination;
        public bool Warp(UnityEngine.Vector3 p) { warps++; return true; }
        public void ResetPath() { }
        public bool SetDestination(UnityEngine.Vector3 p) { destinations++; lastDestination = p; return true; }
    }
}
public class Variable<T> { public T Value; public Variable(T value) { Value = value; } }
public class PlayerNet : UnityEngine.Component
{
    public bool IsSpawned = true, isDespawning;
    public Variable<short> hp = new Variable<short>(100);
}
public class PlayerManager
{
    public static PlayerManager Instance;
    public Dictionary<ulong, PlayerNet> players = new Dictionary<ulong, PlayerNet>();
}
public class CreatureHostile : UnityEngine.Collider
{
    public bool IsSpawned = true;
    public PlayerNet chased;
    public bool HasChasedPlayer(out PlayerNet player) { player = chased; return chased != null; }
}
public class CreaturePaddockAnimal : UnityEngine.Component { }
public class PetBase : UnityEngine.Component { }
public class Vulnerable : UnityEngine.Component
{
    public Variable<ushort> hp = new Variable<ushort>(100);
    public Variable<bool> invinsible = new Variable<bool>(false), isActive = new Variable<bool>(true);
    public int hits;
    public void Hit(ushort damage, ushort weapon) { hits++; hp.Value -= damage; }
}
namespace TonyMods
{
    internal static class TideSummons
    {
        internal static double Now;
        internal class Record { public bool friendly; public ulong summoner; }
    }
    internal class TideCreature : UnityEngine.Component
    {
        internal TideSummons.Record State = new TideSummons.Record();
        internal HashSet<CreatureHostile> chasers = new HashSet<CreatureHostile>();
        internal List<CreatureHostile> taunts = new List<CreatureHostile>();
        internal bool Threatens(UnityEngine.Component who) { return false; }
        internal bool ChasedBy(CreatureHostile enemy) { return chasers.Contains(enemy); }
        internal void Taunt(CreatureHostile enemy) { taunts.Add(enemy); }
    }
}
