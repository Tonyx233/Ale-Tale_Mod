using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using TonyMods;
class PlaylistPlaybackTests : Form
{
    readonly Process[] peers=new Process[2];
    readonly bool[] ready=new bool[2];
    readonly string[] loaded=new string[2],probes=new string[2];
    readonly long[] shown={-1,-1},synced={-1,-1};
    readonly Timer timer=new Timer();readonly Stopwatch clock=Stopwatch.StartNew();
    readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=120000};
    readonly string helper,playlist,first;
    JukeboxState state=new JukeboxState();
    bool started,finished,sought,resolving,imported;
    long initialTrack,initialPlaybackRevision;double lastProbe,resolveAt,initialPosition;string expectedNext;
    [STAThread] static int Main(string[] args){using(var f=new PlaylistPlaybackTests(args))Application.Run(f);return Environment.ExitCode;}
    PlaylistPlaybackTests(string[] args)
    {
        helper=args[0];playlist=args[1];first=args[2];Opacity=0;ShowInTaskbar=false;
        Shown+=delegate{for(int i=0;i<2;i++)StartPeer(i);timer.Interval=250;timer.Tick+=Tick;timer.Start();};
    }
    double Now {get{return clock.Elapsed.TotalSeconds;}}
    void StartPeer(int index)
    {
        var p=new Process();peers[index]=p;
        p.StartInfo=new ProcessStartInfo(helper,Handle.ToInt64()+" --background"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true};
        p.OutputDataReceived+=delegate(object sender,DataReceivedEventArgs e){if(e.Data==null || IsDisposed)return;BeginInvoke((Action)delegate{Receive(index,e.Data);});};
        p.Start();p.BeginOutputReadLine();
    }
    void Send(int i,string value){peers[i].StandardInput.WriteLine(value);peers[i].StandardInput.Flush();}
    void Receive(int i,string value)
    {
        if(finished)return;
        Console.WriteLine("Peer"+i+" "+(value.Length>500?value.Substring(0,100)+"...":value));
        if(value=="READY")ready[i]=true;
        if(value.StartsWith("TRACK_PROBE "))probes[i]=value.Substring(12).Trim('"');
        if(i==0 && value.StartsWith("IMPORT 1 PLAYLIST "))
        {
            string[] songs=JukeboxState.Tracks(value.Substring("IMPORT 1 PLAYLIST ".Length));
            if(songs==null || songs.Length<2){Finish("Invalid playlist result");return;}
            JukeboxState next;string error;
            if(!state.TryInsert("append",songs,1,42,Now,out next,out error)){Finish(error);return;}state=next;
            int target=Array.FindIndex(state.pending,e=>e.video!=first);
            if(target<0){Finish("No different second song");return;}
            expectedNext=state.pending[target].video;
            if(target>0)
            {
                if(!state.TryApply(new JukeboxRequest{op="move",box=42,item=state.pending[target].id,before=state.pending[0].id},Now,out next,out error)){Finish(error);return;}state=next;
            }
            if(state.track!=initialTrack || state.playRevision!=initialPlaybackRevision){Finish("Import or sorting changed playback revision");return;}
            imported=true;Send(0,"CANCEL_RESOLVE");Console.WriteLine("IMPORTED="+songs.Length+" NEXT="+expectedNext);
        }
        if(value.StartsWith("IMPORT 1 PLAYER_ERROR")){Finish(value);return;}
        if(!value.StartsWith("EVENT "))return;
        int split=value.IndexOf(' ',6);long token;
        if(split<0 || !Int64.TryParse(value.Substring(6,split-6),out token) || token!=state.track)return;
        string message=value.Substring(split+1);
        if(message.StartsWith("PLAYER_ERROR") || message=="AUTOPLAY_BLOCKED"){Finish(message);return;}
        JukeboxState advanced;
        if(i==0 && message=="PLAYER_STATE 0" && state.TryFinish(token,"",Now,out advanced))
        {state=advanced;probes[0]=probes[1]=null;Console.WriteLine("AUTO_ADVANCED="+state.video);}
    }
    void Tick(object sender,EventArgs args)
    {
        try
        {
            if(Now>100){Finish("Timeout");return;}
            if(!ready[0] || !ready[1])return;
            if(!started)
            {
                JukeboxState next;string error;
                if(!state.TryInsert("append",new[]{first},0,42,Now,out next,out error))throw new Exception(error);
                state=next;started=true;initialTrack=state.track;initialPlaybackRevision=state.playRevision;
            }
            for(int i=0;i<2;i++)
            {
                if(shown[i]!=state.revision){Send(i,"QUEUE "+Convert.ToBase64String(Encoding.UTF8.GetBytes(json.Serialize(state))));shown[i]=state.revision;}
                string spec=state.video+"~~"+state.track;
                if(loaded[i]!=spec){loaded[i]=spec;probes[i]=null;Send(i,"PLAY "+spec);}
                Send(i,"VOLUME 0");
                Send(i,"SYNC "+state.PositionAt(Now).ToString("F3",CultureInfo.InvariantCulture)+" 0 "+(synced[i]==state.playRevision?"0":"1"));synced[i]=state.playRevision;
            }
            if(Now-lastProbe>1){lastProbe=Now;Send(0,"TRACK_PROBE");Send(1,"TRACK_PROBE");}
            if(probes[0]==null || probes[1]==null)return;
            string[] a=probes[0].Split(','),b=probes[1].Split(',');
            if(a.Length!=4 || b.Length!=4 || a[0]!=state.video || b[0]!=state.video || a[2]!="1" || b[2]!="1")return;
            if(Math.Abs(Double.Parse(a[1],CultureInfo.InvariantCulture)-Double.Parse(b[1],CultureInfo.InvariantCulture))>3)return;
            if(!resolving)
            {resolving=true;resolveAt=Now;initialPosition=Double.Parse(a[1],CultureInfo.InvariantCulture);Send(0,"RESOLVE 1~"+playlist);return;}
            if(imported && !sought && Now-resolveAt>2)
            {
                double current=Double.Parse(a[1],CultureInfo.InvariantCulture);
                if(current<initialPosition+.5)throw new Exception("Playlist import interrupted current song");
                Console.WriteLine("NON_INTERRUPTING_IMPORT=PASS position "+initialPosition+" -> "+current);
                double duration=Double.Parse(a[3],CultureInfo.InvariantCulture);if(duration<6)return;
                JukeboxState next;string error;
                if(!state.TryApply(new JukeboxRequest{op="seek",box=42,track=state.track,playRevision=state.playRevision,position=duration-3},Now,out next,out error))throw new Exception(error);
                state=next;sought=true;
            }
            else if(sought && state.track>initialTrack)
            {if(state.video!=expectedNext)throw new Exception("Wrong queued next song");Finish(null);}
        }
        catch(Exception ex){Finish(ex.Message);}
    }
    void Finish(string error)
    {
        if(finished)return;finished=true;timer.Stop();
        for(int i=0;i<2;i++)if(peers[i]!=null)
        {try{if(!peers[i].HasExited){Send(i,"EXIT");if(!peers[i].WaitForExit(2000))peers[i].Kill();}}catch{}peers[i].Dispose();}
        Console.WriteLine(error==null?"PLAYLIST_PAIR_TEST=non-interrupting-import,queue-sort,hidden-host,ended,shared-next:PASS":"PLAYLIST_PAIR_TEST_FAIL "+error);
        Environment.ExitCode=error==null?0:1;Close();
    }
}
