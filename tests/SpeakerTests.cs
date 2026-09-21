using System;
using TonyMods;
class SpeakerTests
{
    static int checks;
    const string A="pEdxU1F-FE8",B="cfS4YBuKgEw",C="M7lc1UVf-VE";
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static JukeboxState Add(JukeboxState s,string mode,double now,params string[] songs)
    {JukeboxState n;string error;Check(s.TryInsert(mode,songs,1,42,now,out n,out error),mode+": "+error);Check(n.Valid(),mode+" valid");return n;}
    static JukeboxRequest Req(JukeboxState s,string op,long item=0,long before=0,double position=0)
    {return new JukeboxRequest{op=op,box=42,item=item,before=before,position=position,track=s.track,playRevision=s.playRevision};}
    static JukeboxState Apply(JukeboxState s,JukeboxRequest r,double now)
    {JukeboxState n;string error;Check(s.TryApply(r,now,out n,out error),r.op+": "+error);Check(n.Valid(),r.op+" valid");return n;}
    static void Main()
    {
        var s=Add(new JukeboxState(),"append",100,A,B,C);JukeboxState n;string error;
        Check(s.video==A && s.pending.Length==2 && s.PositionAt(112)==12,"initial queue clock");
        long track=s.track,play=s.playRevision;var stalePause=Req(s,"pause");
        long b=s.pending[0].id,c=s.pending[1].id;
        s=Apply(s,Req(s,"move",c,b),112);
        Check(s.pending[0].video==C && s.track==track && s.playRevision==play && s.PositionAt(113)==13,"sorting cannot seek/reload current song");
        s=Apply(s,stalePause,114);Check(s.paused && s.PositionAt(130)==14,"queue edits do not invalidate pause");
        s=Apply(s,Req(s,"seek",0,0,33),131);Check(s.PositionAt(140)==33,"paused seek");
        s=Apply(s,Req(s,"resume"),141);Check(s.PositionAt(145)==37,"resume clock");
        Check(!s.TryApply(stalePause,146,out n,out error),"stale playback control rejected");
        s=Add(s,"insert",147,A,A);
        Check(s.pending[0].video==A && s.pending[1].video==A && s.pending[0].id!=s.pending[1].id,"duplicate videos have distinct IDs");
        track=s.track;play=s.playRevision;
        var move=Req(s,"move",s.pending[1].id,b);
        s=Apply(s,Req(s,"remove",s.pending[0].id),148);
        s=Apply(s,move,149);Check(s.track==track && s.playRevision==play,"concurrent removal and ID-based reorder");
        Check(!s.TryApply(Req(s,"move",c,99999),150,out n,out error),"missing anchor rejects instead of moving wrong item");
        Check(!s.TryApply(Req(s,"remove",99999),150,out n,out error),"missing item rejects");
        var renamed=s.WithTitle(A,"Song A");Check(renamed.current.title=="Song A" && s.current.title=="" && renamed.playRevision==s.playRevision,"metadata copy does not seek or mutate previous snapshot");
        s=Add(s,"now",151,B);Check(s.video==B && s.history[s.history.Length-1].video==A,"play now archives interrupted song");
        s=Apply(s,Req(s,"previous"),152);Check(s.video==A && s.pending[0].video==B,"previous uses history and preserves interrupted song");
        s=Apply(s,Req(s,"stop"),153);Check(!s.Active && s.current!=null && s.pending.Length>0,"stop retains queue and current");
        int count=s.pending.Length;s=Add(s,"append",154,C);Check(!s.Active && s.pending.Length==count+1,"adding while stopped does not restart");
        s=Apply(s,Req(s,"resume"),155);Check(s.Active && s.PositionAt(156)==1,"resume after stop");
        s=Apply(s,Req(s,"repeat"),157);Check(s.repeat==1,"repeat one");
        track=s.track;string current=s.video;
        Check(s.TryFinish(track,"",158,out n),"ended repeat one");s=n;Check(s.video==current && s.track>track && s.Valid(),"repeat one restarts same entry");
        Check(!s.TryFinish(track,"",159,out n),"duplicate ended rejected");
        s=Apply(s,Req(s,"next"),160);Check(s.video==B,"manual next bypasses repeat one");
        s=Apply(s,Req(s,"repeat"),161);Check(s.repeat==2,"repeat all");
        count=s.pending.Length;Check(s.TryFinish(s.track,"",162,out n),"ended repeat all");s=n;
        Check(s.pending.Length==count && s.pending[s.pending.Length-1].video==B && s.Valid(),"repeat all recycles with fresh ID");
        s=Apply(s,Req(s,"clear"),163);Check(s.pending.Length==0 && s.Active,"clear retains current playback");
        for(int i=0;i<3;i++){Check(s.TryFinish(s.track,"150",164+i,out n),"unavailable");s=n;}
        Check(s.stopped && s.current!=null && s.Valid(),"third failure stops repeat all without losing current");
        s=Add(new JukeboxState(),"append",0,A);
        var full=new string[200];for(int i=0;i<200;i++)full[i]=B;
        s=Add(s,"append",1,full);Check(s.pending.Length==200,"capacity");
        Check(!s.TryInsert("append",new[]{C},1,42,2,out n,out error) && error.Contains("Nothing"),"overflow rejects entire import");
        Check(!s.TryInsert("append",new[]{C},1,99,2,out n,out error),"other jukebox rejected");
        s=Apply(s,Req(s,"repeat"),2);s=Apply(s,Req(s,"repeat"),3);
        for(int i=0;i<210;i++){Check(s.TryFinish(s.track,"",4+i,out n),"long-running loop");s=n;Check(s.Valid(),"bounded unique loop state");}
        Check(s.pending.Length==200 && s.history.Length==50,"history bound without growing queue");
        Check(!s.TryApply(Req(s,"previous"),220,out n,out error),"previous cannot overflow pending queue");
        s=Add(new JukeboxState(),"append",0,A);
        s=Apply(s,Req(s,"pause"),1);Check(!s.TryFinish(s.track,"",2,out n),"paused ended ignored");
        s=Apply(s,Req(s,"resume"),3);Check(s.TryFinish(s.track,"",4,out n) && n.current==null && n.history.Length==1,"last song ends without loop");
        Check(!new JukeboxState{protocol=2}.Valid(),"old protocol rejected");
        Check(!new JukeboxRequest{position=Double.NaN}.Valid(),"NaN rejected");
        int last=100;for(int i=0;i<=300;i++){int v=JukeboxState.DistanceVolume(i/10.0,100);Check(v<=last && v>=0,"distance attenuation");last=v;}
        Check(JukeboxState.DistanceVolume(3,100)==100 && JukeboxState.DistanceVolume(30,100)==0,"distance limits");
        Console.WriteLine("PASS: "+checks+" queue identity, concurrent edit, timeline, capacity, history, repeat and distance checks");
    }
}
