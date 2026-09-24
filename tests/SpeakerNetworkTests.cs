using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;
using TonyMods;
class SpeakerNetworkTests
{
    static int checks;static void Check(bool value,string why){checks++;if(!value)throw new Exception(why);}
    static Unity.Netcode.NetworkManager server;
    static JukeboxSpeaker speaker;
    static List<string> commands=new List<string>();
    static void Tick(){UnityEngine.Time.unscaledTime+=.3f;server.ServerTime.Time=UnityEngine.Time.unscaledTime;speaker.Update();}
    static void Request(ulong sender,JukeboxRequest request){UnityEngine.Time.unscaledTime+=.2f;server.CustomMessagingManager.Dispatch(sender,HorseJson.Serialize(request));}
    static JukeboxState State(){return HorseJson.Deserialize<JukeboxState>(server.CustomMessagingManager.Sent[1].Last());}
    static void Main()
    {
        server=new Unity.Netcode.NetworkManager{IsServer=true,LocalClientId=0,ConnectedClientsIds=new ulong[]{0,1,2}};
        Unity.Netcode.NetworkManager.Singleton=server;
        var box=new Jukebox {NetworkObjectId=42};server.SpawnManager.SpawnedObjects[42]=new Unity.Netcode.NetworkObject{component=box};
        var host=new PlayerNet{OwnerClientId=0};host.transform.position=new UnityEngine.Vector3(100);
        var client=new PlayerNet{OwnerClientId=1};var far=new PlayerNet{OwnerClientId=2};far.transform.position=new UnityEngine.Vector3(100);
        PlayerNet.Instance=host;PlayerManager.Instance=new PlayerManager();
        PlayerManager.Instance.players[0]=host;PlayerManager.Instance.players[1]=client;PlayerManager.Instance.players[2]=far;
        speaker=new JukeboxSpeaker(new BepInEx.Logging.ManualLogSource(),commands.Add);Tick();
        Request(1,new JukeboxRequest{op="hello"});
        Request(1,new JukeboxRequest{op="append",box=42,video="pEdxU1F-FE8"});Tick();
        var state=State();Check(state.current.addedBy==1,"nearby client can add with authenticated author");
        Check(commands.Any(x=>x.StartsWith("PLAY pEdxU1F-FE8")) && commands.Contains("VOLUME 0"),"distant host starts silent clock player");
        Request(2,new JukeboxRequest{op="append",box=42,video="cfS4YBuKgEw"});Check(State().pending.Length==0,"far client rejected");
        Request(1,new JukeboxRequest{op="append",box=42,video="cfS4YBuKgEw"});
        Request(1,new JukeboxRequest{op="append",box=42,video="M7lc1UVf-VE"});state=State();Tick();commands.Clear();
        Request(1,new JukeboxRequest{op="move",box=42,item=state.pending[1].id,before=state.pending[0].id});Tick();state=State();
        Check(state.pending[0].video=="M7lc1UVf-VE","client can reorder by stable ID");
        Check(!commands.Any(x=>x.StartsWith("PLAY ")) && commands.Where(x=>x.StartsWith("SYNC ")).All(x=>x.EndsWith(" 0 0")),"actual speaker does not reload or seek on queue edits");
        string snapshot=HorseJson.Serialize(state);
        var clientNet=new Unity.Netcode.NetworkManager{IsServer=false,LocalClientId=1,ConnectedClientsIds=new ulong[]{0,1}};
        clientNet.SpawnManager=server.SpawnManager;Unity.Netcode.NetworkManager.Singleton=clientNet;PlayerNet.Instance=client;
        var clientCommands=new List<string>();var clientSpeaker=new JukeboxSpeaker(new BepInEx.Logging.ManualLogSource(),clientCommands.Add);
        clientSpeaker.Update();clientNet.CustomMessagingManager.Dispatch(0,snapshot);UnityEngine.Time.unscaledTime+=.3f;clientSpeaker.Update();
        Check(clientCommands.Any(x=>x.StartsWith("PLAY pEdxU1F-FE8")),"late join gets current song");
        int sent=clientNet.CustomMessagingManager.Sent[0].Count;
        clientSpeaker.HandleBrowser("EVENT "+state.track+" PLAYER_STATE 0");
        Check(clientNet.CustomMessagingManager.Sent[0].Count==sent,"client ended cannot advance room");
        Unity.Netcode.NetworkManager.Singleton=server;PlayerNet.Instance=host;
        speaker.HandleBrowser("EVENT "+state.track+" PLAYER_STATE 0");var next=State();
        Check(next.video=="M7lc1UVf-VE" && next.track>state.track,"distant host advances room without proximity check");
        speaker.HandleBrowser("EVENT "+state.track+" PLAYER_STATE 0");Check(State().track==next.track,"old ended event ignored");
        commands.Clear();Request(1,new JukeboxRequest{op="append",box=42,playlist="PLdrk_BM8q45oxliginXQPrQuoTk2ppFjV"});
        string resolve=commands.Last(x=>x.StartsWith("RESOLVE "));string job=resolve.Substring(8).Split('~')[0];
        Check(State().video==next.video,"import keeps current song");
        Request(1,new JukeboxRequest{op="clear",box=42});
        speaker.HandleBrowser("IMPORT "+job+" PLAYLIST pEdxU1F-FE8,cfS4YBuKgEw");
        Check(State().pending.Length==0 && State().video==next.video && commands.Contains("CANCEL_RESOLVE"),"clear cancels in-flight import and ignores late callback");
        state=State();Request(1,new JukeboxRequest{op="stop",box=42,track=state.track,playRevision=state.playRevision});
        Check(State().stopped && State().current!=null,"stop preserves queue state");
        state=State();Request(1,new JukeboxRequest{op="state",box=42});Check(State().video==state.video,"client cannot replace server state");
        foreach(string mode in new[]{"append","insert","now"})
        {
            int count=State().pending.Length;
            commands.Clear();
            Request(1,new JukeboxRequest{op=mode,box=42,video="cfS4YBuKgEw",playlist="PLdrk_BM8q45oxliginXQPrQuoTk2ppFjV"});
            var single=State();
            Check(!commands.Any(x=>x.StartsWith("RESOLVE ")),mode+" with video and list does not fetch playlist");
            Check(single.pending.Length==count+(mode=="now"?0:1),mode+" adds only one song");
            Check((mode=="now"?single.current:mode=="insert"?single.pending[0]:single.pending.Last()).video=="cfS4YBuKgEw",mode+" keeps requested song");
        }
        var big=new JukeboxState();JukeboxState built;string error;
        var songs=new string[200];for(int i=0;i<songs.Length;i++)songs[i]="pEdxU1F-FE8";
        Check(big.TryInsert("append",songs,1,42,0,out built,out error),"large queue");
        string encoded=HorseJson.Serialize(built);
        Check(encoded.Length<60000 && HorseJson.Deserialize<JukeboxState>(encoded).Valid(),"200-song JSON snapshot fits transport bounds and roundtrips");
        Console.WriteLine("PASS: "+checks+" production speaker authority, distance, concurrent edit, late join, import cancellation and serialization checks");
    }
}
namespace BepInEx.Logging {public class ManualLogSource {public void LogInfo(object v){}public void LogWarning(object v){}public void LogError(object v){}}}
namespace UnityEngine
{
    public static class Time {public static float unscaledTime;}
    public struct Vector3 {public float x;public Vector3(float value){x=value;}public static float Distance(Vector3 a,Vector3 b){return Math.Abs(a.x-b.x);}}
    public class Transform {public Vector3 position;}
}
namespace Unity.Collections {public enum Allocator{Temp}}
namespace Unity.Netcode
{
    public enum NetworkDelivery{ReliableFragmentedSequenced}
    public class FastBufferReader {public string json;public int Length{get{return json.Length*2+4;}}public void ReadValueSafe(out string value,bool option){value=json;}}
    public class FastBufferWriter:IDisposable
    {public string json;readonly int max;public FastBufferWriter(int size,Unity.Collections.Allocator allocator){max=size;}public void WriteValueSafe(string value){if(value.Length*2+4>max)throw new Exception("Writer capacity");json=value;}public void Dispose(){}}
    public class NetworkObject {public object component;public T GetComponent<T>() where T:class{return component as T;}public T GetComponentInChildren<T>() where T:class{return component as T;}}
    public class SpawnManager {public Dictionary<ulong,NetworkObject> SpawnedObjects=new Dictionary<ulong,NetworkObject>();}
    public class Clock {public double Time;}
    public class Messaging
    {
        public readonly Dictionary<ulong,List<string>> Sent=new Dictionary<ulong,List<string>>();Action<ulong,FastBufferReader> callback;
        public void RegisterNamedMessageHandler(string channel,Action<ulong,FastBufferReader> action){callback=action;}
        public void UnregisterNamedMessageHandler(string channel){callback=null;}
        public void Dispatch(ulong sender,string json){callback(sender,new FastBufferReader{json=json});}
        public void SendNamedMessage(string channel,ulong id,FastBufferWriter writer,NetworkDelivery delivery){if(!Sent.ContainsKey(id))Sent[id]=new List<string>();Sent[id].Add(writer.json);}
    }
    public class NetworkManager
    {
        public static NetworkManager Singleton;public const ulong ServerClientId=0;
        public bool IsListening=true,IsServer;public ulong LocalClientId;public ulong[] ConnectedClientsIds;
        public Clock ServerTime=new Clock();public Messaging CustomMessagingManager=new Messaging();public SpawnManager SpawnManager=new SpawnManager();
    }
}
public class Number {public int Value=100;}
public class Jukebox {public ulong NetworkObjectId;public bool IsSpawned=true,isLocallyPlaying;public Number currentTrackId=new Number(),volume=new Number();public UnityEngine.Transform transform=new UnityEngine.Transform();public void Stop(){isLocallyPlaying=false;}}
public class PlayerNet {public static PlayerNet Instance;public ulong OwnerClientId;public bool IsSpawned=true;public Number hp=new Number();public UnityEngine.Transform transform=new UnityEngine.Transform();}
public class PlayerManager {public static PlayerManager Instance;public Dictionary<ulong,PlayerNet> players=new Dictionary<ulong,PlayerNet>();}
public class JukeboxManager {public static JukeboxManager Instance;public void RemoveJukeboxAround(Jukebox box){}public void AddJukeboxAround(Jukebox box){}}
namespace TonyMods {static class HorseJson {static JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=120000};public static string Serialize(object v){return json.Serialize(v);}public static T Deserialize<T>(string s){return json.Deserialize<T>(s);}}}
