using System;
using TonyMods;
class SpeakerTests
{
    static int checks;
    static void Check(bool ok, string why) { checks++; if (!ok) throw new Exception(why); }
    static void Main()
    {
        JukeboxState s = new JukeboxState(), next;
        Check(s.TryApply(new JukeboxState { op="play",video="ytQ3Hs3WjQ4",box=42 },100,out next),"start"); s=next;
        Check(s.revision==1 && s.PositionAt(112)==12,"shared clock / late join");
        Check(!s.TryApply(new JukeboxState { op="pause",box=42,revision=0 },112,out next),"stale pause rejected");
        Check(!s.TryApply(new JukeboxState { op="stop",box=99,revision=1 },112,out next),"wrong speaker rejected");
        Check(s.TryApply(new JukeboxState { op="pause",box=42,revision=1 },112,out next),"pause"); s=next;
        Check(s.paused && s.PositionAt(150)==12,"paused timeline frozen");
        Check(s.TryApply(new JukeboxState { op="seek",box=42,revision=2,position=33 },150,out next),"seek paused"); s=next;
        Check(s.PositionAt(160)==33,"paused seek retained");
        Check(s.TryApply(new JukeboxState { op="resume",box=42,revision=3 },160,out next),"resume"); s=next;
        Check(s.PositionAt(165)==38,"resume clock");
        Check(s.TryApply(new JukeboxState { op="play",video=s.video,box=42 },170,out next),"same song restart"); s=next;
        Check(s.PositionAt(170)==0 && s.revision==5,"restart revision");
        foreach (string bad in new[]{"x","abcdefghijk/","a';alert(1)","https://evil"}) Check(!new JukeboxState {video=bad}.Valid(),"video validation");
        foreach(double bad in new[]{Double.NaN,Double.PositiveInfinity,-1,604801}) Check(!new JukeboxState {position=bad}.Valid(),"position validation");
        Check(!new JukeboxState {protocol=2}.Valid(),"version rejected");
        Check(!s.TryApply(new JukeboxState {op="state",video="M7lc1UVf-VE"},171,out next),"client cannot submit state");
        Check(s.TryApply(new JukeboxState {op="stop",box=42,revision=5},175,out next) && next.video=="","shared stop");
        Check(JukeboxState.DistanceVolume(0,100)==100 && JukeboxState.DistanceVolume(3,100)==100,"near field");
        Check(JukeboxState.DistanceVolume(30,100)==0 && JukeboxState.DistanceVolume(300,100)==0,"far field");
        Check(JukeboxState.DistanceVolume(3,50)==50,"native volume scale");
        Check(JukeboxState.DistanceVolume(Double.NaN,100)==0,"invalid distance muted");
        int last=100;
        for(int i=0;i<=300;i++){int v=JukeboxState.DistanceVolume(i/10.0,100);Check(v<=last && v>=0,"monotonic attenuation");last=v;}
        Console.WriteLine("PASS: "+checks+" speaker timeline, revision, validation and attenuation checks");
    }
}
