using System;
using System.Collections.Generic;
using TonyMods;

// Synthetic point obstacles exercise production sweeps; native Unity collision remains a gameplay check.
struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
    public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
    public static Vector3 Lerp(Vector3 a,Vector3 b,float t){return new Vector3(a.x+(b.x-a.x)*t,a.y+(b.y-a.y)*t,a.z+(b.z-a.z)*t);}
    public static float Distance(Vector3 a,Vector3 b){var d=a-b;return (float)Math.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z);}
}
struct Quaternion
{
    public float yaw;
    public static Quaternion Inverse(Quaternion q){return new Quaternion{yaw=-q.yaw};}
    public static float Angle(Quaternion a,Quaternion b){return Math.Abs(a.yaw-b.yaw);}
    public static Quaternion Slerp(Quaternion a,Quaternion b,float t){return new Quaternion{yaw=a.yaw+(b.yaw-a.yaw)*t};}
    public static Vector3 operator *(Quaternion q,Vector3 v){double r=q.yaw*Math.PI/180;return new Vector3((float)(v.x*Math.Cos(r)+v.z*Math.Sin(r)),v.y,(float)(-v.x*Math.Sin(r)+v.z*Math.Cos(r)));}
}
static class Mathf
{
    public const float Deg2Rad=(float)(Math.PI/180);
    public static float Clamp(float v,float a,float b){return Math.Max(a,Math.Min(b,v));}
    public static int CeilToInt(float v){return (int)Math.Ceiling(v);}
}
class Transform
{
    public Transform parent;
    public Vector3 position;
    public bool IsChildOf(Transform root){for(var p=this;p!=null;p=p.parent)if(p==root)return true;return false;}
}
class GameObject{public Transform transform=new Transform();}
class PlayerNet{public Transform transform=new Transform();}
class Collider{public Transform transform=new Transform();}
enum QueryTriggerInteraction{Ignore}
static class Physics
{
    public const int DefaultRaycastLayers=1;
    public static readonly List<Collider> Obstacles=new List<Collider>();
    public static Collider[] OverlapBox(Vector3 center,Vector3 half,Quaternion rotation,int layers,QueryTriggerInteraction query)
    {
        var result=new List<Collider>();
        foreach(var c in Obstacles){var p=Quaternion.Inverse(rotation)*(c.transform.position-center);if(Math.Abs(p.x)<=half.x&&Math.Abs(p.y)<=half.y&&Math.Abs(p.z)<=half.z)result.Add(c);}
        return result.ToArray();
    }
}
partial class TavernHorse
{
    float Extension=2.04f;
    Vector3 vehiclePosition;
    Quaternion vehicleRotation;
    Vector3[] offsets={new Vector3(0,0,.2f),new Vector3(0,0,-.48f),new Vector3(0,0,-1.16f),new Vector3(0,0,-1.84f),new Vector3(0,0,-2.52f)};
    HorseSeats seats=new HorseSeats(5);
    GameObject cart=new GameObject();
    Dictionary<ulong,PlayerNet> players=new Dictionary<ulong,PlayerNet>();
    PlayerNet Player(ulong id){PlayerNet p;return players.TryGetValue(id,out p)?p:null;}
    static int checks;
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
    static Collider Obstacle(float x,float z){var c=new Collider();c.transform.position=new Vector3(x,1.4f,z);Physics.Obstacles.Add(c);return c;}
    static void Main()
    {
        var h=new TavernHorse();
        Check(Math.Abs(h.MountDistance(new Vector3(1,0,-2.52f))-1)<.001,"Board beside fifth saddle using body distance");
        h.vehicleRotation=new Quaternion{yaw=90};
        Check(Math.Abs(h.MountDistance(new Vector3(-2.52f,0,1))-1)<.001,"Mount distance rotates with the horse");
        h.Extension=0;
        Check(Math.Abs(h.MountDistance(new Vector3(1,0,-2.52f))-Vector3.Distance(new Vector3(1,0,-2.52f),h.vehiclePosition))<.001,"Original horse keeps origin-based mount range");
        h.Extension=2.04f;h.vehicleRotation=new Quaternion();
        Check(h.ExtendedPathClear(new Vector3(.5f,0,0),new Quaternion()),"Clear translation accepted");
        Obstacle(.5f,-3);
        Check(!h.ExtendedPathClear(new Vector3(.5f,0,0),new Quaternion()),"Rear-body collision blocks despite clear driver path");
        Physics.Obstacles.Clear();Obstacle(-2.12f,-2.12f);
        Check(!h.ExtendedPathClear(h.vehiclePosition,new Quaternion{yaw=90}),"Rotation sweep catches obstacle between start and end poses");
        Physics.Obstacles.Clear();var own=Obstacle(.3f,-2);own.transform.parent=h.cart.transform;
        Check(h.ExtendedPathClear(new Vector3(.2f,0,0),new Quaternion()),"Own parked collider is ignored");
        Physics.Obstacles.Clear();var rider=new PlayerNet();h.players[4]=rider;h.seats.Board(4);var body=Obstacle(.3f,-2);body.transform.parent=rider.transform;
        Check(h.ExtendedPathClear(new Vector3(.2f,0,0),new Quaternion()),"Seated occupant is ignored");
        h.seats.Remove(4);
        Check(!h.ExtendedPathClear(new Vector3(.2f,0,0),new Quaternion()),"Unseated player remains an obstacle");
        Physics.Obstacles.Clear();
        Check(!h.ExtendedPathClear(new Vector3(30,0,0),new Quaternion()),"Unbounded sweep is rejected");
        Console.WriteLine("PASS: "+checks+" production mount/long-body sweep scenarios");
    }
}
