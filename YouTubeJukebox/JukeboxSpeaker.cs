using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BepInEx.Logging;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
namespace TonyMods
{
    internal sealed class JukeboxSpeaker
    {
        private const string Channel = "Tony.YouTubeSpeaker.v3";
        private readonly ManualLogSource log;
        private readonly Action<string> emit;
        private NetworkManager network;
        private JukeboxState state = new JukeboxState();
        private readonly Dictionary<ulong,float> peers = new Dictionary<ulong,float>(), requests = new Dictionary<ulong,float>();
        private float nextNetwork, nextAudio, lastSnapshot;
        private string loadedVideo = "";
        private long loadedRevision=-1, shownRevision=-1;
        private Jukebox ambientBox;
        private bool actuallyPlaying;
        private sealed class Import { public long id; public ulong sender; public JukeboxRequest request; }
        private readonly Queue<Import> imports = new Queue<Import>();
        private Import resolving;
        private long importSerial;
        private float resolveDeadline;
        public Jukebox Selected;
        public string Message="";
        public JukeboxSpeaker(ManualLogSource logger,Action<string> output) {log=logger;emit=output;}
        private double Now { get {return network.ServerTime.Time;} }
        public void ResetPlayer() {loadedVideo="";loadedRevision=shownRevision=-1;actuallyPlaying=false;SetAmbient(null);}
        public void Update()
        {
            var net=NetworkManager.Singleton;
            if(net==null || !net.IsListening) {if(!System.Object.ReferenceEquals(network,null))Disconnect();return;}
            if(net!=network)
            {
                Disconnect();network=net;network.CustomMessagingManager.RegisterNamedMessageHandler(Channel,Receive);
                lastSnapshot=Time.unscaledTime;nextNetwork=0;log.LogInfo("YouTube queue sync bound; server="+network.IsServer);
            }
            if(network.IsServer)
            {
                if((state.current!=null || state.pending.Length>0) && Resolve(state.box)==null)
                {CancelImports();state=new JukeboxState {revision=state.revision+1,track=state.track+1,playRevision=state.playRevision+1};Broadcast();}
                if(resolving!=null && Time.unscaledTime>resolveDeadline) EndImport(null,"Playlist loading timed out; current music was kept.");
                PumpImports();
            }
            if(Time.unscaledTime>=nextNetwork)
            {
                nextNetwork=Time.unscaledTime+2;
                if(network.IsServer)Broadcast();else Send(NetworkManager.ServerClientId,new JukeboxRequest {op="hello"});
            }
            if(!network.IsServer && Time.unscaledTime-lastSnapshot>8)
            {if(loadedVideo!=""){emit("STOP");ResetPlayer();}Message="Waiting for host sync. All players need 0.8.1.";emit("META "+Message);return;}
            if(Time.unscaledTime>=nextAudio) {nextAudio=Time.unscaledTime+.25f;ApplyAudio();}
        }
        private Jukebox Resolve(ulong id)
        {
            NetworkObject obj;
            return network!=null && network.SpawnManager!=null && network.SpawnManager.SpawnedObjects.TryGetValue(id,out obj)
                ? obj.GetComponent<Jukebox>() ?? obj.GetComponentInChildren<Jukebox>() : null;
        }
        private bool Nearby(ulong id,Jukebox box)
        {
            if(box==null || PlayerManager.Instance==null)return false;
            foreach(PlayerNet p in PlayerManager.Instance.players.Values)
                if(p!=null && p.OwnerClientId==id && p.IsSpawned && p.hp.Value>0)
                    return Vector3.Distance(p.transform.position,box.transform.position)<=8;
            return false;
        }
        private void Request(JukeboxRequest request,Jukebox box)
        {
            if(network==null || box==null || !box.IsSpawned)return;
            if(!Nearby(network.LocalClientId,box)){Message="Move within 8m of the jukebox to control music.";emit("META "+Message);return;}
            request.box=box.NetworkObjectId;
            if(network.IsServer)ServerRequest(network.LocalClientId,request);else Send(NetworkManager.ServerClientId,request);
        }
        public void Stop(Jukebox box)
        {if(box!=null && box.NetworkObjectId==state.box)Request(new JukeboxRequest {op="stop",track=state.track,playRevision=state.playRevision},box);}
        public void HandleBrowser(string value)
        {
            if(value=="READY" && resolving!=null && network!=null && network.IsServer)
            {emit("RESOLVE "+resolving.id+"~"+resolving.request.playlist);return;}
            if(value.StartsWith("REQUEST ",StringComparison.Ordinal))
            {
                try
                {
                    if(value.Length>4096)return;
                    var request=HorseJson.Deserialize<JukeboxRequest>(Encoding.UTF8.GetString(Convert.FromBase64String(value.Substring(8))));
                    if(request!=null && request.Valid())Request(request,request.op=="append" || request.op=="insert" || request.op=="now" ? Selected : Resolve(state.box));
                }
                catch(Exception ex){log.LogWarning("Queue request rejected: "+ex.Message);}return;
            }
            if(value.StartsWith("IMPORT ",StringComparison.Ordinal))
            {
                if(network==null || !network.IsServer || resolving==null)return;
                int split=value.IndexOf(' ',7);long id;
                if(split<0 || !Int64.TryParse(value.Substring(7,split-7),out id) || id!=resolving.id)return;
                string result=value.Substring(split+1);
                if(result.StartsWith("PLAYLIST ",StringComparison.Ordinal))EndImport(JukeboxState.Tracks(result.Substring(9)),"");
                else if(result.StartsWith("PLAYER_ERROR",StringComparison.Ordinal))EndImport(null,"Cannot read playlist. Current music was kept.");
                return;
            }
            if(!value.StartsWith("EVENT ",StringComparison.Ordinal))return;
            int end=value.IndexOf(' ',6);long token;
            if(end<0 || !Int64.TryParse(value.Substring(6,end-6),out token) || token!=state.track || !state.Active)return;
            value=value.Substring(end+1);
            if(value=="PLAYER_STATE 1")actuallyPlaying=true;
            if(value=="PLAYER_STATE 0" || value=="PLAYER_STATE 2" || value.StartsWith("PLAYER_ERROR",StringComparison.Ordinal))actuallyPlaying=false;
            if(network!=null && network.IsServer)
            {
                JukeboxState next;
                string error=value.StartsWith("PLAYER_ERROR ",StringComparison.Ordinal)?value.Substring(13):value=="AUTOPLAY_BLOCKED"?value:null;
                if((value=="PLAYER_STATE 0" || error!=null) && state.TryFinish(token,error ?? "",Now,out next))
                {state=next;Message=state.notice;Broadcast();return;}
            }
            if(value=="REQUEST_PAUSE" || value=="REQUEST_RESUME" || value.StartsWith("REQUEST_SEEK ",StringComparison.Ordinal))
            {
                var req=new JukeboxRequest {op=value=="REQUEST_PAUSE"?"pause":value=="REQUEST_RESUME"?"resume":"seek",track=state.track,playRevision=state.playRevision};
                if(req.op=="seek" && !Double.TryParse(value.Substring(13),NumberStyles.Float,CultureInfo.InvariantCulture,out req.position))return;
                Request(req,Resolve(state.box));
            }
            if(value.StartsWith("PLAYER_ERROR",StringComparison.Ordinal) || value=="AUTOPLAY_BLOCKED")
            {Message="Local YouTube playback blocked; other players keep their place.";emit("META "+Message);}
        }
        private void PumpImports()
        {
            if(resolving!=null || imports.Count==0)return;
            resolving=imports.Dequeue();
            if(resolving.request.video!="") {EndImport(new[]{resolving.request.video},"");return;}
            resolveDeadline=Time.unscaledTime+40;
            emit("RESOLVE "+resolving.id+"~"+resolving.request.playlist);
        }
        private void EndImport(string[] songs,string error)
        {
            Import job=resolving;if(job==null)return;resolving=null;emit("CANCEL_RESOLVE");
            if(songs==null || songs.Length==0)error=error==""?"Playlist is empty, unavailable, or exceeds 200 songs.":error;
            if(error=="" && job.request.video!="")
            {
                int first=Array.IndexOf(songs,job.request.video);
                if(first<0)error="Selected video is outside the readable playlist (maximum 200 songs).";
                else {var remaining=new string[songs.Length-first];Array.Copy(songs,first,remaining,0,remaining.Length);songs=remaining;}
            }
            JukeboxState next;
            if(error=="" && Resolve(job.request.box)==null)error="Jukebox no longer exists.";
            if(error=="" && state.TryInsert(job.request.op,songs,job.sender,job.request.box,Now,out next,out error))
            {state=next;var box=Resolve(state.box);if(state.Active && box!=null){box.currentTrackId.Value=0;box.Stop();}}
            if(error!="")state=state.WithNotice(error);
            Message=state.notice;Broadcast();
        }
        private void CancelImports() {imports.Clear();resolving=null;emit("CANCEL_RESOLVE");}
        private void Receive(ulong sender,FastBufferReader reader)
        {
            try
            {
                if(reader.Length>131072 || (network.IsServer && reader.Length>4096))return;
                string json;reader.ReadValueSafe(out json,false);if(json.Length>60000)return;
                if(network.IsServer)
                {var request=HorseJson.Deserialize<JukeboxRequest>(json);if(request!=null && request.Valid())ServerRequest(sender,request);}
                else if(sender==NetworkManager.ServerClientId)
                {
                    var packet=HorseJson.Deserialize<JukeboxState>(json);
                    if(packet!=null && packet.Valid() && packet.revision>=state.revision){state=packet;lastSnapshot=Time.unscaledTime;Message=state.notice;}
                }
            }
            catch(Exception ex){log.LogWarning("YouTube queue packet rejected: "+ex.Message);}
        }
        private void ServerRequest(ulong sender,JukeboxRequest request)
        {
            bool connected=false;foreach(ulong id in network.ConnectedClientsIds)if(id==sender)connected=true;
            if(!connected || !request.Valid())return;
            if(request.op=="hello"){peers[sender]=Time.unscaledTime;Send(sender,state);return;}
            if(!Nearby(sender,Resolve(request.box)))return;
            float previous;if(requests.TryGetValue(sender,out previous) && Time.unscaledTime-previous<.1f)return;
            requests[sender]=Time.unscaledTime;
            if(request.op=="append" || request.op=="insert" || request.op=="now")
            {
                if(request.video=="" && request.playlist=="")return;
                if(imports.Count+(resolving==null?0:1)>=8){state=state.WithNotice("Too many pending imports; wait for the current import.");Broadcast();return;}
                if(state.current==null && state.pending.Length==0 && resolving==null && imports.Count==0)
                {state=state.WithNotice("");state.box=request.box;}
                imports.Enqueue(new Import {id=++importSerial,sender=sender,request=request});PumpImports();return;
            }
            JukeboxState next;string error;
            if(state.TryApply(request,Now,out next,out error))
            {state=next;if(request.op=="clear" || request.op=="stop")CancelImports();}
            else state=state.WithNotice(error);
            Message=state.notice;Broadcast();
        }
        private void Send(ulong id,object packet)
        {
            string json=HorseJson.Serialize(packet);
            if(json.Length>60000){log.LogError("YouTube queue snapshot exceeded limit.");return;}
            using(var writer=new FastBufferWriter(131072,Allocator.Temp))
            {writer.WriteValueSafe(json);network.CustomMessagingManager.SendNamedMessage(Channel,id,writer,NetworkDelivery.ReliableFragmentedSequenced);}
        }
        private void Broadcast()
        {
            foreach(ulong id in network.ConnectedClientsIds)
            {float seen;if(id!=network.LocalClientId && peers.TryGetValue(id,out seen) && Time.unscaledTime-seen<10)Send(id,state);}
        }
        private void ApplyAudio()
        {
            if(shownRevision!=state.revision)
            {emit("QUEUE "+Convert.ToBase64String(Encoding.UTF8.GetBytes(HorseJson.Serialize(state))));shownRevision=state.revision;}
            if(!state.Active)
            {if(loadedVideo!=""){emit("STOP");loadedVideo="";loadedRevision=-1;}actuallyPlaying=false;SetAmbient(null);return;}
            Jukebox box=Resolve(state.box);PlayerNet player=PlayerNet.Instance;
            if(box==null){emit("VOLUME 0");SetAmbient(null);return;}
            int volume=player==null || !player.IsSpawned?0:JukeboxState.DistanceVolume(Vector3.Distance(player.transform.position,box.transform.position),box.volume.Value);
            SetAmbient(volume>0 && !state.paused && actuallyPlaying?box:null);
            if(loadedVideo=="" && volume==0 && !network.IsServer)return;
            if(box.isLocallyPlaying)box.Stop();
            string playback=state.video+"~~"+state.track;
            if(loadedVideo!=playback){emit("PLAY "+playback);loadedVideo=playback;actuallyPlaying=false;}
            emit("VOLUME "+volume);
            emit("SYNC "+state.PositionAt(Now).ToString("F3",CultureInfo.InvariantCulture)+" "+(state.paused?"1":"0")+" "+(loadedRevision==state.playRevision?"0":"1"));
            loadedRevision=state.playRevision;
            emit("CONTROL "+(Nearby(network.LocalClientId,box)?"1":"0"));
        }
        private void SetAmbient(Jukebox box)
        {
            JukeboxManager manager=JukeboxManager.Instance;
            if(!System.Object.ReferenceEquals(ambientBox,box))
            {
                if(manager!=null && !System.Object.ReferenceEquals(ambientBox,null) && !(ambientBox!=null && ambientBox.currentTrackId.Value>0 && ambientBox.isLocallyPlaying))manager.RemoveJukeboxAround(ambientBox);
                ambientBox=box;
            }
            if(manager!=null && box!=null)manager.AddJukeboxAround(box);
        }
        public void Disconnect()
        {
            if(network!=null && network.CustomMessagingManager!=null)network.CustomMessagingManager.UnregisterNamedMessageHandler(Channel);
            CancelImports();emit("EXIT");network=null;state=new JukeboxState();peers.Clear();requests.Clear();Selected=null;ResetPlayer();
        }
    }
}
