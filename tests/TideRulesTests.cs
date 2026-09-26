using System;
using TonyMods;
internal static class TideRulesTests
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static bool Near(double a, double b) { return Math.Abs(a - b) < 1e-4; }
    private static void Main()
    {
        Check(TideRules.Price == 1 && TideRules.ItemId == 47940, "merchant contract");
        Check(TideRules.CanUse(true,true,true,47940,1,1), "no room cap: any number of idols");
        Check(!TideRules.CanUse(false,true,true,47940,1,2), "client cannot create");
        Check(!TideRules.CanUse(true,false,true,47940,1,2), "dead player cannot create");
        Check(!TideRules.CanUse(true,true,false,47940,1,2), "foreign inventory rejected");
        Check(!TideRules.CanUse(true,true,true,47920,1,2), "horse item never consumed");
        Check(!TideRules.CanUse(true,true,true,47940,0,2), "empty item rejected");
        Check(!TideRules.CanUse(true,true,true,47940,1,.99), "duplicate use throttled");
        Check(!TideRules.CanUse(true,true,true,47940,1,Double.NaN), "bad time rejected");
        Check(!TideRules.CanUse(true,true,true,47940,1,Double.PositiveInfinity), "infinite time rejected");
        // Tony's 0.16.2 tuning: 1000 HP, movement +50%, windups halved, hit ranges doubled.
        Check(TideRules.Health == 1000, "health 1000");
        Check(Near(TideRules.Speed,2.1*1.5) && Near(TideRules.Acceleration,7*1.5) && Near(TideRules.TurnSpeed,160*1.5), "movement +50%");
        Check(Near(TideRules.StopDistance,1.65*1.5) && Near(TideRules.EngageDistance,2.15*1.5) && Near(TideRules.SweepDistance,1.45*1.5), "engage distances +50%");
        Check(Near(TideRules.SearchRadius,30*1.5) && Near(TideRules.SearchHeight,4*1.5), "search +50%");
        Check(Near(TideRules.Windup(TideRules.Bite),.35) && Near(TideRules.Windup(TideRules.Sweep),.5) && Near(TideRules.Windup(TideRules.Wave),.8), "windups halved");
        Check(Near(TideRules.Duration(TideRules.Bite)-TideRules.Windup(TideRules.Bite),1.7-.7) &&
              Near(TideRules.Duration(TideRules.Sweep)-TideRules.Windup(TideRules.Sweep),2.35-1) &&
              Near(TideRules.Duration(TideRules.Wave)-TideRules.Windup(TideRules.Wave),3.4-1.6), "strike and recovery unchanged");
        Check(Near(TideRules.BiteReach,2.3*2) && Near(TideRules.BiteHalfWidth,.65*2) && Near(TideRules.SweepRadius,2.6*2) && Near(TideRules.WaveRadius,4*2), "ranges doubled");
        // The idol must be inside its own attack range before it stops walking.
        Check(TideRules.StopDistance < TideRules.EngageDistance && TideRules.SweepDistance < TideRules.EngageDistance, "stops inside engage distance");
        Check(TideRules.EngageDistance <= TideRules.BiteReach && TideRules.SweepDistance <= TideRules.SweepRadius, "engaged targets are reachable");
        Check(TideRules.InHit(TideRules.Bite,0,TideRules.EngageDistance,0) && TideRules.InHit(TideRules.Sweep,0,TideRules.SweepDistance,0), "chosen attack reaches the target ahead");
        Check(TideRules.InHit(TideRules.Bite,0,4,0), "bite ahead");
        Check(!TideRules.InHit(TideRules.Bite,1.4f,2,0), "side dodge");
        Check(!TideRules.InHit(TideRules.Bite,0,-1,0), "bite cannot hit behind");
        Check(!TideRules.InHit(TideRules.Bite,0,4.8f,0), "bite range");
        Check(TideRules.InHit(TideRules.Wave,0,-7,0), "wave hits behind");
        Check(!TideRules.InHit(TideRules.Wave,6,6,0), "outside circle safe");
        Check(!TideRules.InHit(TideRules.Wave,0,0,2), "different floor safe");
        Check(!TideRules.InHit(TideRules.Wave,Single.NaN,0,0), "bad coordinates rejected");
        Check(!TideRules.InHit(TideRules.Death,0,0,0), "dead cannot attack");
        for(int degrees=-180;degrees<=180;degrees++)
        {
            double radians=degrees*Math.PI/180;
            float x=(float)Math.Sin(radians),z=(float)Math.Cos(radians);
            foreach(float r in new[]{1f,5f})
                if(Math.Abs(degrees)!=60) Check(TideRules.InHit(TideRules.Sweep,x*r,z*r,0)==(Math.Abs(degrees)<60), "sweep fan "+degrees+"@"+r);
            Check(!TideRules.InHit(TideRules.Sweep,x*5.4f,z*5.4f,0), "sweep outer edge "+degrees);
            Check(TideRules.InHit(TideRules.Wave,x*7.9f,z*7.9f,0), "wave inner ring "+degrees);
            Check(!TideRules.InHit(TideRules.Wave,x*8.2f,z*8.2f,0), "wave outer ring "+degrees);
        }
        foreach(byte action in new[]{TideRules.Bite,TideRules.Sweep,TideRules.Wave})
        {
            Check(TideRules.Duration(action)>TideRules.Windup(action)+TideRules.Strike(action), "recovery exists");
            foreach(double dt in new[]{.008,.017,.033,.1,.4,2.0})
            {
                int hits=0;double last=0;
                for(double now=dt;now<5;now+=dt){if(TideRules.CrossedHit(action,last,now))hits++;last=now;}
                Check(hits==1,"one impact with stalled frames "+action+"/"+dt);
            }
        }
        // Every point the host accepts must also pass the owner's native 3D distance re-check.
        foreach(byte action in new[]{TideRules.Bite,TideRules.Sweep,TideRules.Wave})
            for(float side=-8.4f;side<=8.4f;side+=.1f) for(float forward=-8.4f;forward<=8.4f;forward+=.1f)
                foreach(float height in new[]{-TideRules.HitHeight,-.9f,0,.9f,TideRules.HitHeight})
                    if(TideRules.InHit(action,side,forward,height))
                        Check(Math.Sqrt(side*side+forward*forward+height*height)+.2<=TideRules.Range(action),"native range covers "+action+" "+side+","+forward+","+height);
        Check(TideRules.ThrowDistances[0]==5 && Array.TrueForAll(TideRules.ThrowDistances,d=>d>=3), "throw lands clear of the thrower");
        Check(TideRules.Duration(TideRules.Summon)>2, "no immediate damage during throw/rise");
        Console.WriteLine("PASS: "+count+" Tidefork purchase/authority/geometry/timing rules");
    }
}
