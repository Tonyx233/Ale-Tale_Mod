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
        var five = new HorseSeats(5);
        for (ulong i = 0; i < 5; i++) Check(five.Board(i)==(int)i,"Five distinct riders receive distinct saddles");
        Check(five.Board(5)==-1 && five.Count==5,"Sixth rider rejected without mutation");
        var fiveRemote = new HorseSeats(5);
        Check(fiveRemote.Decode(five.Encode()) && fiveRemote.Find(4)==4,"Late join preserves fifth rider");
        Check(!five.TrySwitch(4,0),"Fifth rider cannot steal driver seat");
        five.Remove(0);
        Check(five.TrySwitch(4,0) && five.Find(4)==0,"Fifth rider can take empty driver seat");
        Check(five.TrySwitch(4,4) && five.Find(4)==4,"Driver can return to fifth saddle");
        Check(!five.TrySwitch(4,5) && !five.TrySwitch(4,-1),"Invalid extended seat rejected");
        Check(!five.Decode("0,1") && !new HorseSeats().Decode(five.Encode()),"Wrong variant seat packets rejected");
        foreach(string bad in new[]{"0,1,2,3,3","0,1,2,3,-1","0,1,2,3,18446744073709551616"})
        { string before=five.Encode();Check(!five.Decode(bad)&&five.Encode()==before,"Five-seat malformed snapshot is atomic"); }
        five.Clear();
        Check(five.Encode().Length==104 && fiveRemote.Decode(five.Encode()),"Longest five-seat empty wire fits decoder");
        Check(HorseVariant.Seats(0)==2 && HorseVariant.Seats(1)==5 && !HorseVariant.Valid(2),"Variant capacities and unknown variant");
        Check(Math.Abs(HorseVariant.SeatZ(4)+2.52f)<.0001f,"Fifth saddle stays aligned with model");
        for(int i=0;i<10;i++)for(int step=0;step<360;step++)foreach(float run in new[]{0f,.5f,1f})
        {
            double phase=step*Math.PI/180+HorseGait.Offset(i,run,10);
            float upper=HorseGait.Upper(phase,1,run), lower=HorseGait.Lower(phase,1,run);
            Check(!Single.IsNaN(upper)&&Math.Abs(upper)<=38.001 && lower>=0&&lower<=60.001,"Ten-leg animated joint bounds");
            Check(Math.Abs(HorseGait.Upper(phase+Math.PI*2,1,run)-upper)<.001,"Ten-leg loop continuity");
        }
        Console.WriteLine("PASS: "+checks+" original/five-seat, switching, snapshot and gait assertions");
    }
}
