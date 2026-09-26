using System;
using TonyMods;
internal static class TideRulesTests
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static void Main()
    {
        Check(TideRules.Price == 1 && TideRules.ItemId == 47940, "merchant contract");
        Check(TideRules.CanUse(true,true,true,47940,1,7,1), "last available capacity");
        Check(!TideRules.CanUse(false,true,true,47940,1,0,2), "client cannot create");
        Check(!TideRules.CanUse(true,false,true,47940,1,0,2), "dead player cannot create");
        Check(!TideRules.CanUse(true,true,false,47940,1,0,2), "foreign inventory rejected");
        Check(!TideRules.CanUse(true,true,true,47920,1,0,2), "horse item never consumed");
        Check(!TideRules.CanUse(true,true,true,47940,0,0,2), "empty item rejected");
        Check(!TideRules.CanUse(true,true,true,47940,1,8,2), "capacity counts summoning monsters");
        Check(!TideRules.CanUse(true,true,true,47940,1,0,.99), "duplicate use throttled");
        Check(!TideRules.CanUse(true,true,true,47940,1,0,Double.NaN), "bad time rejected");
        Check(!TideRules.CanUse(true,true,true,47940,1,0,Double.PositiveInfinity), "infinite time rejected");
        Check(TideRules.InHit(TideRules.Bite,0,2,0), "bite ahead");
        Check(!TideRules.InHit(TideRules.Bite,.7f,2,0), "side dodge");
        Check(!TideRules.InHit(TideRules.Bite,0,-1,0), "bite cannot hit behind");
        Check(!TideRules.InHit(TideRules.Bite,0,3,0), "bite range");
        Check(TideRules.InHit(TideRules.Wave,0,-3,0), "wave hits behind");
        Check(!TideRules.InHit(TideRules.Wave,3,3,0), "outside circle safe");
        Check(!TideRules.InHit(TideRules.Wave,0,0,2), "different floor safe");
        Check(!TideRules.InHit(TideRules.Wave,Single.NaN,0,0), "bad coordinates rejected");
        Check(!TideRules.InHit(TideRules.Death,0,0,0), "dead cannot attack");
        for(int degrees=-180;degrees<=180;degrees++)
        {
            double radians=degrees*Math.PI/180;
            float x=(float)Math.Sin(radians)*2,z=(float)Math.Cos(radians)*2;
            if(Math.Abs(degrees)!=60) Check(TideRules.InHit(TideRules.Sweep,x,z,0)==(Math.Abs(degrees)<60), "sweep fan "+degrees);
            Check(TideRules.InHit(TideRules.Wave,x,z,0), "wave inner ring "+degrees);
            Check(!TideRules.InHit(TideRules.Wave,x*2.1f,z*2.1f,0), "wave outer ring "+degrees);
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
            for(float side=-4.2f;side<=4.2f;side+=.05f) for(float forward=-4.2f;forward<=4.2f;forward+=.05f)
                foreach(float height in new[]{-1.8f,-.9f,0,.9f,1.8f})
                    if(TideRules.InHit(action,side,forward,height))
                        Check(Math.Sqrt(side*side+forward*forward+height*height)+.2<=TideRules.Range(action),"native range covers "+action+" "+side+","+forward+","+height);
        Check(TideRules.ThrowDistances[0]==5 && Array.TrueForAll(TideRules.ThrowDistances,d=>d>=3), "throw lands clear of the thrower");
        Check(TideRules.Duration(TideRules.Summon)>2, "no immediate damage during throw/rise");
        Check(TideRules.Lifetime==120 && TideRules.Limit==8,"bounded encounter");
        Console.WriteLine("PASS: "+count+" Tidefork purchase/authority/geometry/timing rules");
    }
}
