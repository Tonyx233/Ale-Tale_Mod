using System;
using System.Collections.Generic;
using TonyMods;

// Synthetic ray hits exercise the production filtering method without Unity's native runtime.
struct Vector3
{
    public float x,y,z;
    public Vector3(float a,float b,float c){x=a;y=b;z=c;}
    public static Vector3 up { get { return new Vector3(0,1,0); } }
    public static Vector3 operator +(Vector3 a,Vector3 b){return new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);}
    public static Vector3 operator -(Vector3 a,Vector3 b){return new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);}
    public float sqrMagnitude { get { return x*x+y*y+z*z; } }
    public float magnitude { get { return (float)Math.Sqrt(sqrMagnitude); } }
    public Vector3 normalized { get { float m=magnitude; return new Vector3(x/m,y/m,z/m); } }
}
class Transform
{
    public string name;
    public Transform parent;
    public Vector3 position;
    public bool IsChildOf(Transform other){for(Transform t=this;t!=null;t=t.parent)if(t==other)return true;return false;}
}
class PlayerNet { public ulong OwnerClientId; public Transform transform=new Transform(); }
class GameObject { public Transform transform=new Transform(); }
struct RaycastHit { public Transform transform; }
enum QueryTriggerInteraction { Ignore }
static class Physics
{
    public const int DefaultRaycastLayers=1;
    public static RaycastHit[] Hits=new RaycastHit[0];
    public static RaycastHit[] RaycastAll(Vector3 start,Vector3 direction,float distance,int layers,QueryTriggerInteraction trigger){return Hits;}
}
class Logger { public void LogInfo(string value){} }
partial class TavernHorse
{
    string Id="test";
    GameObject cart=new GameObject();
    Vector3 vehiclePosition=new Vector3(0,0,0);
    HorseSeats seats=new HorseSeats();
    Vector3 MountPoint(Vector3 player){return vehiclePosition;}
    Logger log=new Logger();
    Dictionary<ulong,PlayerNet> players=new Dictionary<ulong,PlayerNet>();
    PlayerNet Player(ulong id){PlayerNet p;return players.TryGetValue(id,out p)?p:null;}
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS: "+name);}
    static RaycastHit Hit(Transform t){return new RaycastHit{transform=t};}
    static void Main()
    {
        foreach(int capacity in new[]{2,5}) foreach(ulong driverId in new ulong[]{0,1})
        {
            var h=new TavernHorse(); h.seats=new HorseSeats(capacity);
            var driver=new PlayerNet{OwnerClientId=driverId};
            var passenger=new PlayerNet{OwnerClientId=1-driverId};
            passenger.transform.position=new Vector3(2,0,0);
            h.players[driverId]=driver;h.players[1-driverId]=passenger;h.seats.Board(driverId);
            var driverBody=new Transform{parent=driver.transform,name="driver body"};
            var wall=new Transform{name="wall"};
            Physics.Hits=new[]{Hit(driverBody)};
            Check(!h.MountPathBlocked(passenger),"Seated driver does not block passenger, driver="+driverId);
            Physics.Hits=new[]{Hit(driverBody),Hit(wall)};
            Check(h.MountPathBlocked(passenger),"Wall behind driver still blocks");
            Physics.Hits=new[]{Hit(wall),Hit(driverBody)};
            Check(h.MountPathBlocked(passenger),"Hit order does not hide wall");
            Physics.Hits=new[]{Hit(h.cart.transform),Hit(passenger.transform),Hit(driverBody)};
            Check(!h.MountPathBlocked(passenger),"Horse and boarding player ignored");
            h.seats.Remove(driverId);Physics.Hits=new[]{Hit(driverBody)};
            Check(h.MountPathBlocked(passenger),"Unseated player remains an obstacle");
            Physics.Hits=new RaycastHit[0];
            Check(!h.MountPathBlocked(passenger),"Clear path allowed");
        }
        Console.WriteLine("PASS: "+checks+" synthetic mount-path scenarios");
    }
}
