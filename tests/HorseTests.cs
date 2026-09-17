using System;
using TonyMods;
class HorseTests
{
    static int checks;
    static void Check(bool value,string reason){checks++;if(!value)throw new Exception(reason);}
    static void Main()
    {
        var seats=new HorseSeats();
        Check(seats.Count==0,"Starts empty");
        Check(seats.Board(10)==0,"First rider drives");
        Check(seats.Board(20)==1,"Second rider passenger");
        Check(seats.Board(30)==-1,"Third rider rejected");
        string full=seats.Encode();
        Check(!seats.TrySwitch(10,1)&&seats.Encode()==full,"Cannot steal occupied passenger seat");
        Check(!seats.TrySwitch(20,0)&&seats.Encode()==full,"Cannot steal occupied driver seat");
        Check(!seats.TrySwitch(30,0),"Non-rider cannot switch");
        seats.Remove(10);
        Check(seats.Find(20)==1,"Driver departure does not auto-promote passenger");
        Check(seats.TrySwitch(20,0)&&seats.Find(20)==0&&seats.Count==1,"Passenger can take empty driver seat");
        Check(seats.TrySwitch(20,1)&&seats[0]==HorseSeats.Empty,"Driver can stop and move to passenger seat");
        Check(seats.TrySwitch(20,1)&&seats.Count==1,"Repeated request idempotent");
        Check(!seats.TrySwitch(20,2)&&!seats.TrySwitch(20,-1),"Invalid seat rejected");
        Check(seats.Board(30)==0,"New rider fills driver seat");
        var copy=new HorseSeats();Check(copy.Decode(seats.Encode())&&copy.Encode()==seats.Encode(),"Late-join roundtrip");
        foreach(string bad in new[]{null,"","1","1,1","1,2,3","-1,2","18446744073709551616,2"})
        {string before=copy.Encode();Check(!copy.Decode(bad)&&copy.Encode()==before,"Invalid snapshot is atomic");}
        Check(seats.Board(HorseSeats.Empty)==-1,"Reserved ID rejected");
        // Exercise both host/client boarding orders, including NGO's host ID zero.
        foreach (ulong first in new ulong[] { 0, 1 })
        {
            ulong second = 1 - first;
            var shared = new HorseSeats();
            Check(shared.Board(first)==0 && shared.Board(second)==1,"Host/client both boarding orders");
            var remote = new HorseSeats();
            Check(remote.Decode(shared.Encode()) && remote.Find(second)==1 && remote.Find(first)==0,"Passenger survives wire snapshot");
            shared.Remove(second);
            Check(shared.Board(second)==1 && shared.Find(first)==0,"Passenger can dismount and reboard without moving driver");
        }
        for(int i=0;i<4;i++)for(int step=0;step<360;step++)foreach(float run in new[]{0f,.5f,1f})
        {
            double phase=step*Math.PI/180+HorseGait.Offset(i,run);
            float upper=HorseGait.Upper(phase,1,run),lower=HorseGait.Lower(phase,1,run);
            Check(!Single.IsNaN(upper)&&Math.Abs(upper)<=38.001,"Upper-leg range");
            Check(lower>=0&&lower<=60.001,"Knee range");
            Check(HorseGait.Upper(phase,0,run)==0&&HorseGait.Lower(phase,0,run)==0,"Standing legs at rest");
            Check(Math.Abs(HorseGait.Upper(phase+Math.PI*2,1,run)-upper)<.001,"Loop continuity");
        }
        Console.WriteLine("PASS: "+checks+" two-seat, switching, snapshot and gait assertions");
    }
}
