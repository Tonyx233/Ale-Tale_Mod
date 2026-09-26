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
        // Tony's 0.16.3 tuning: 1000 HP, movement x1.5 twice (search excluded), original windups, custom reaches.
        Check(TideRules.Health == 1000, "health 1000");
        Check(Near(TideRules.Speed,2.1*2.25) && Near(TideRules.Acceleration,7*2.25) && Near(TideRules.TurnSpeed,160*2.25), "movement x2.25");
        Check(Near(TideRules.StopDistance,1.65*2.25) && Near(TideRules.EngageDistance,2.15*2.25) && Near(TideRules.SweepDistance,1.45*2.25), "engage distances x2.25");
        Check(Near(TideRules.SearchRadius,30) && Near(TideRules.SearchHeight,4) && Near(TideRules.RepathInterval,.15), "original search, 0.15 s repath");
        Check(Near(TideRules.Windup(TideRules.Bite),.7) && Near(TideRules.Windup(TideRules.Sweep),1) && Near(TideRules.Windup(TideRules.Wave),1.6), "original windups");
        Check(Near(TideRules.Duration(TideRules.Bite),1.7) && Near(TideRules.Duration(TideRules.Sweep),2.35) && Near(TideRules.Duration(TideRules.Wave),3.4), "original durations");
        Check(Near(TideRules.BiteReach,6) && Near(TideRules.BiteHalfWidth,1.3) && Near(TideRules.SweepRadius,3.5) && Near(TideRules.WaveRadius,6), "bite 6 m, sweep 3.5 m, wave 6 m");
        // The idol must be inside its own attack range before it stops walking.
        Check(TideRules.StopDistance < TideRules.EngageDistance && TideRules.SweepDistance < TideRules.EngageDistance, "stops inside engage distance");
        Check(TideRules.EngageDistance <= TideRules.BiteReach && TideRules.SweepDistance <= TideRules.SweepRadius, "engaged targets are reachable");
        Check(TideRules.InHit(TideRules.Bite,0,TideRules.EngageDistance,0) && TideRules.InHit(TideRules.Sweep,0,TideRules.SweepDistance,0), "chosen attack reaches the target ahead");
        Check(TideRules.InHit(TideRules.Bite,0,4,0), "bite ahead");
        Check(!TideRules.InHit(TideRules.Bite,1.4f,2,0), "side dodge");
        Check(!TideRules.InHit(TideRules.Bite,0,-1,0), "bite cannot hit behind");
        Check(TideRules.InHit(TideRules.Bite,0,5.9f,0) && !TideRules.InHit(TideRules.Bite,0,6.2f,0), "bite range");
        Check(TideRules.InHit(TideRules.Wave,0,-5.5f,0), "wave hits behind");
        Check(!TideRules.InHit(TideRules.Wave,6,6,0), "outside circle safe");
        Check(!TideRules.InHit(TideRules.Wave,0,0,2), "different floor safe");
        Check(!TideRules.InHit(TideRules.Wave,Single.NaN,0,0), "bad coordinates rejected");
        Check(!TideRules.InHit(TideRules.Death,0,0,0), "dead cannot attack");
        for(int degrees=-180;degrees<=180;degrees++)
        {
            double radians=degrees*Math.PI/180;
            float x=(float)Math.Sin(radians),z=(float)Math.Cos(radians);
            foreach(float r in new[]{1f,3.3f})
                if(Math.Abs(degrees)!=60) Check(TideRules.InHit(TideRules.Sweep,x*r,z*r,0)==(Math.Abs(degrees)<60), "sweep fan "+degrees+"@"+r);
            Check(!TideRules.InHit(TideRules.Sweep,x*3.7f,z*3.7f,0), "sweep outer edge "+degrees);
            Check(TideRules.InHit(TideRules.Wave,x*5.9f,z*5.9f,0), "wave inner ring "+degrees);
            Check(!TideRules.InHit(TideRules.Wave,x*6.2f,z*6.2f,0), "wave outer ring "+degrees);
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
        // Tony's 0.17.0 潮彈: lobbed shell, 16 damage, 5 s cooldown, 7-22 m, -30% move speed for 3 s.
        Check(TideRules.Damage(TideRules.Shot)==16 && Near(TideRules.ShotCooldown,5) && Near(TideRules.FirstShotDelay,3), "shell damage and cooldown");
        Check(Near(TideRules.ShotMin,7) && Near(TideRules.ShotMax,22) && TideRules.ShotMax<TideRules.SearchRadius, "shell band inside the search radius");
        Check(TideRules.ShotSlowPercent==30 && TideRules.ShotSlowSeconds==3, "slow -30% for 3 s");
        Check(Near(TideRules.Windup(TideRules.Shot),.7) && Near(TideRules.ShotRadius,2.2) && Near(TideRules.ShotRing,2.5), "0.7 s windup, 2.2 m splash, 2.5 m ring");
        Check(TideRules.ShotRing>=TideRules.ShotRadius, "drawn ring never smaller than the splash");
        Check(Near(TideRules.ShotFlight(7),.9+7/30.0) && Near(TideRules.ShotFlight(22),.9+22/30.0), "flight 1.13-1.63 s");
        for(float d=0;d<TideRules.ShotMax;d+=.5f) Check(TideRules.ShotFlight(d+.5f)>TideRules.ShotFlight(d), "farther shells fly longer "+d);
        Check(!TideRules.InShotRange(6.9f,false) && TideRules.InShotRange(7,false) && TideRules.InShotRange(22,false) && !TideRules.InShotRange(22.1f,false), "7-22 m band");
        Check(TideRules.InShotRange(0,true) && TideRules.InShotRange(3,true) && !TideRules.InShotRange(22.1f,true), "stranded idol fires at any range up to 22 m");
        Check(!TideRules.InShotRange(Single.NaN,true) && !TideRules.InShotRange(-1,true), "bad distance rejected");
        // The melee check runs first, so the reachable-target band starts beyond the melee engage distance.
        Check(TideRules.ShotMin>TideRules.EngageDistance, "shell starts where melee stops");
        // Stands still only for the throw: the chase resumes before even the shortest shell lands.
        Check(TideRules.Duration(TideRules.Shot)>TideRules.Windup(TideRules.Shot)+TideRules.Strike(TideRules.Shot), "throw has a follow-through");
        Check(TideRules.Duration(TideRules.Shot)<TideRules.Windup(TideRules.Shot)+TideRules.ShotFlight(0), "walks again while the shell flies");
        // One shell slot per idol: windup, the longest flight and the splash end before the next shot can start.
        Check(TideRules.Windup(TideRules.Shot)+TideRules.ShotFlight(TideRules.ShotMax)+TideRules.ShotSplash<TideRules.ShotCooldown, "shell lifetime fits the cooldown");
        Check(!TideRules.InHit(TideRules.Shot,0,0,0) && !TideRules.CrossedHit(TideRules.Shot,0,5), "shell never uses melee shapes or strike timing");
        Check(TideRules.MuzzleHeight>2.6f, "launch point above the 2.6 m hitbox");
        Check(Near(TideRules.ArcLift(0,3),0) && Near(TideRules.ArcLift(1,3),0) && Near(TideRules.ArcLift(.5f,3),3), "arc apex at mid-flight");
        Check(TideRules.ShotApexes[0]>TideRules.ShotApexes[1] && TideRules.ShotApexes[1]>TideRules.ShotApexes[2] && TideRules.ShotApexes[2]>0, "highest arc tried first");
        Check(Near(TideRules.LeadSeconds(1),(TideRules.Windup(TideRules.Shot)+1)*.5) && TideRules.ShotLeadMax>0, "half lead, capped");
        Check(TideRules.InSplash(0,0,0) && TideRules.InSplash(2.19f,0,0) && !TideRules.InSplash(2.21f,0,0) && !TideRules.InSplash(1.6f,1.6f,0), "2.2 m splash");
        Check(TideRules.InSplash(0,0,TideRules.HitHeight) && !TideRules.InSplash(0,0,TideRules.HitHeight+.01f), "other floors safe from the splash");
        Check(!TideRules.InSplash(Single.NaN,0,0) && !TideRules.InSplash(0,Single.PositiveInfinity,0) && !TideRules.InSplash(0,0,Single.NaN), "bad splash coordinates rejected");
        for(float x=-2.4f;x<=2.4f;x+=.05f) for(float z=-2.4f;z<=2.4f;z+=.05f)
            foreach(float h in new[]{-TideRules.HitHeight,-.9f,0,.9f,TideRules.HitHeight})
                if(TideRules.InSplash(x,z,h))
                    Check(Math.Sqrt(x*x+z*z+h*h)+.2<=TideRules.Range(TideRules.Shot),"native range covers splash "+x+","+z+","+h);
        // Exactly one splash however the frames fall, and none without a shell.
        foreach(double dt in new[]{.008,.017,.033,.1,.4,2.0})
        {
            bool armed=true;int splashes=0;
            for(double now=dt;now<8;now+=dt) if(armed&&TideRules.ShotDue(now,1.5,1.2f)){armed=false;splashes++;}
            Check(splashes==1,"one splash with stalled frames "+dt);
        }
        Check(!TideRules.ShotDue(100,0,1) && !TideRules.ShotDue(2.6,1.5,1.2f) && TideRules.ShotDue(2.8,1.5,1.2f), "splash only after landing");
        Check(!TideRules.ShotDue(Double.NaN,1.5,1.2f) && !TideRules.ShotDue(3,Double.PositiveInfinity,1.2f), "bad shell clock rejected");
        // Snapshot validation of the shell fields.
        Check(TideRules.ValidShot(0,Single.NaN,Single.NaN,5,10), "no shell needs no shell fields");
        Check(TideRules.ValidShot(12,TideRules.ShotFlight(TideRules.ShotMax),TideRules.ShotApexes[0],5,10), "valid shell");
        Check(!TideRules.ValidShot(4,1,2,5,10) && !TideRules.ValidShot(17,1,2,5,10), "shell time bounded by birth and clock");
        Check(!TideRules.ValidShot(8,0,2,5,10) && !TideRules.ValidShot(8,5,2,5,10) && !TideRules.ValidShot(8,1,9,5,10) && !TideRules.ValidShot(8,1,-1,5,10), "flight and apex bounded");
        Check(!TideRules.ValidShot(Double.NaN,1,2,5,10) && !TideRules.ValidShot(8,Single.NaN,2,5,10) && !TideRules.ValidShot(8,1,Single.NaN,5,10), "bad shell numbers rejected");
        Check(TideRules.Shot>TideRules.Death && TideRules.SceneNameMax>=10, "new action is last; the game's scene names fit");
        Console.WriteLine("PASS: "+count+" Tidefork purchase/authority/geometry/timing rules");
    }
}
