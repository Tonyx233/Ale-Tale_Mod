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
        // Tony's 0.16.3 tuning: 1000 HP, movement x1.5 twice (search excluded), original windups.
        Check(TideRules.Health == 1000, "health 1000");
        Check(Near(TideRules.Speed,2.1*2.25) && Near(TideRules.Acceleration,7*2.25) && Near(TideRules.TurnSpeed,160*2.25), "movement x2.25");
        Check(Near(TideRules.StopDistance,1.65*2.25) && Near(TideRules.EngageDistance,2.15*2.25), "engage distances x2.25");
        Check(Near(TideRules.SearchRadius,30) && Near(TideRules.SearchHeight,4) && Near(TideRules.RepathInterval,.15), "original search, 0.15 s repath");
        Check(Near(TideRules.Windup(TideRules.Bite),.7) && Near(TideRules.Windup(TideRules.Wave),1.6), "original windups");
        Check(Near(TideRules.Duration(TideRules.Bite),1.7) && Near(TideRules.Duration(TideRules.Wave),3.4), "original durations");
        // Tony's 0.17.3 reaches: bite 10 m long and 3.3 m wide, wave 9 m; the tail sweep is gone.
        Check(Near(TideRules.BiteReach,10) && Near(TideRules.BiteHalfWidth,3.3/2) && Near(TideRules.WaveRadius,9), "bite 10 x 3.3 m, wave 9 m");
        Check(TideRules.Damage(TideRules.Bite)==12 && TideRules.Damage(TideRules.Wave)==20, "melee damage");
        Check(!TideRules.Melee(3) && TideRules.Windup(3)==0 && TideRules.Duration(3)==0 && TideRules.Damage(3)==0 && !TideRules.InHit(3,0,1,0), "tail sweep removed");
        Check(TideRules.Melee(TideRules.Bite) && TideRules.Melee(TideRules.Wave) && !TideRules.Melee(TideRules.Shot) && !TideRules.Melee(TideRules.Walk), "bite and wave are the melee attacks");
        // The idol must be inside its own attack range before it stops walking.
        Check(TideRules.StopDistance < TideRules.EngageDistance, "stops inside engage distance");
        Check(TideRules.EngageDistance <= TideRules.BiteReach && TideRules.EngageDistance <= TideRules.WaveRadius, "engaged targets are reachable");
        Check(TideRules.InHit(TideRules.Bite,0,TideRules.EngageDistance,0) && TideRules.InHit(TideRules.Wave,0,TideRules.EngageDistance,0), "chosen attack reaches the target ahead");
        // Melee starts only where its shapes land; targets on another floor are chased or shelled instead.
        Check(TideRules.CanEngage(TideRules.EngageDistance,0) && !TideRules.CanEngage(TideRules.EngageDistance+.01f,0), "engage distance");
        Check(TideRules.CanEngage(1,TideRules.HitHeight) && TideRules.CanEngage(1,-TideRules.HitHeight), "same floor engaged");
        Check(!TideRules.CanEngage(1,TideRules.HitHeight+.01f) && !TideRules.CanEngage(1,-TideRules.HitHeight-.01f) && !TideRules.CanEngage(0,6), "other floors never bitten");
        Check(!TideRules.CanEngage(Single.NaN,0) && !TideRules.CanEngage(1,Single.NaN) && !TideRules.CanEngage(Single.PositiveInfinity,0), "bad engage input rejected");
        for(float d=0;d<=TideRules.EngageDistance;d+=.05f) for(float h=-2;h<=2;h+=.05f)
            if(TideRules.CanEngage(d,h)) Check(TideRules.InHit(TideRules.Bite,0,d,h) && TideRules.InHit(TideRules.Wave,0,d,h), "engaged target is hit "+d+","+h);
        Check(TideRules.InHit(TideRules.Bite,0,4,0) && TideRules.InHit(TideRules.Bite,1.6f,8,0), "bite ahead");
        Check(!TideRules.InHit(TideRules.Bite,1.7f,2,0), "side dodge");
        Check(!TideRules.InHit(TideRules.Bite,0,-1,0), "bite cannot hit behind");
        Check(TideRules.InHit(TideRules.Bite,0,9.9f,0) && !TideRules.InHit(TideRules.Bite,0,10.2f,0), "bite range");
        Check(TideRules.InHit(TideRules.Wave,0,-8.5f,0), "wave hits behind");
        Check(!TideRules.InHit(TideRules.Wave,6.5f,6.5f,0), "outside circle safe");
        Check(!TideRules.InHit(TideRules.Wave,0,0,2), "different floor safe");
        Check(!TideRules.InHit(TideRules.Wave,Single.NaN,0,0), "bad coordinates rejected");
        Check(!TideRules.InHit(TideRules.Death,0,0,0), "dead cannot attack");
        for(int degrees=-180;degrees<=180;degrees++)
        {
            double radians=degrees*Math.PI/180;
            float x=(float)Math.Sin(radians),z=(float)Math.Cos(radians);
            Check(TideRules.InHit(TideRules.Wave,x*8.9f,z*8.9f,0), "wave inner ring "+degrees);
            Check(!TideRules.InHit(TideRules.Wave,x*9.2f,z*9.2f,0), "wave outer ring "+degrees);
        }
        foreach(byte action in new[]{TideRules.Bite,TideRules.Wave})
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
        foreach(byte action in new[]{TideRules.Bite,TideRules.Wave})
            for(float side=-10.4f;side<=10.4f;side+=.1f) for(float forward=-10.4f;forward<=10.4f;forward+=.1f)
                foreach(float height in new[]{-TideRules.HitHeight,-.9f,0,.9f,TideRules.HitHeight})
                    if(TideRules.InHit(action,side,forward,height))
                        Check(Math.Sqrt(side*side+forward*forward+height*height)+.2<=TideRules.Range(action),"native range covers "+action+" "+side+","+forward+","+height);
        Check(TideRules.ThrowDistances[0]==5 && Array.TrueForAll(TideRules.ThrowDistances,d=>d>=3), "throw lands clear of the thrower");
        Check(TideRules.Duration(TideRules.Summon)>2, "no immediate damage during throw/rise");
        // Tony's 潮彈: 0.17.0 lobbed shell with a -30% slow for 3 s; 0.17.1 a volley of 5 shells 1.5x faster, splash
        // and ring 30% wider; 0.17.3 9 damage, 3 s cooldown after the last throw, 4.84-29.9 m.
        Check(TideRules.Damage(TideRules.Shot)==9 && Near(TideRules.ShotCooldown,3) && Near(TideRules.FirstShotDelay,3), "shell damage and cooldown");
        Check(TideRules.ShotCount==5 && Near(TideRules.ShotInterval,.35), "five shells per volley, 0.35 s apart");
        Check(Near(TideRules.ShotMin,4.84) && Near(TideRules.ShotMax,29.9) && TideRules.ShotMax<TideRules.SearchRadius, "4.84-29.9 m band inside the search radius");
        Check(TideRules.ShotSlowPercent==30 && TideRules.ShotSlowSeconds==3, "slow -30% for 3 s");
        Check(Near(TideRules.Windup(TideRules.Shot),.7) && Near(TideRules.ShotRadius,2.2*1.3) && Near(TideRules.ShotRing,2.5*1.3), "0.7 s windup, splash and ring 30% wider");
        Check(TideRules.ShotRing>=TideRules.ShotRadius, "drawn ring never smaller than the splash");
        for(float d=0;d<=TideRules.ShotMax;d+=.5f) Check(Near(TideRules.ShotFlight(d),(.9+d/30.0)/1.5), "shells 1.5x faster at "+d);
        for(float d=0;d<TideRules.ShotMax;d+=.5f) Check(TideRules.ShotFlight(d+.5f)>TideRules.ShotFlight(d), "farther shells fly longer "+d);
        Check(!TideRules.InShotRange(4.83f,false) && TideRules.InShotRange(4.84f,false) && TideRules.InShotRange(29.9f,false) && !TideRules.InShotRange(30,false), "4.84-29.9 m band");
        Check(TideRules.InShotRange(0,true) && TideRules.InShotRange(3,true) && !TideRules.InShotRange(30,true), "any range up to 29.9 m when stranded or finishing a volley");
        Check(!TideRules.InShotRange(Single.NaN,true) && !TideRules.InShotRange(-1,true), "bad distance rejected");
        // The melee check runs first, so the reachable-target band starts right where the melee engage distance ends.
        Check(TideRules.ShotMin>TideRules.EngageDistance && TideRules.ShotMin-TideRules.EngageDistance<.01f, "shells start where melee stops");
        // Volley timeline: throws every 0.35 s after the windup; each shell is aimed when the previous one is thrown.
        for(int k=0;k<TideRules.ShotCount;k++)
        {
            Check(Near(TideRules.Release(k),.7+k*.35), "shell "+k+" thrown on schedule");
            Check(k==0 ? Near(TideRules.PlanAt(k),0) : Near(TideRules.PlanAt(k),TideRules.Release(k-1)), "shell "+k+" aimed when the previous one is thrown");
            Check(TideRules.Release(k)-TideRules.PlanAt(k)>=TideRules.ShotInterval-1e-4, "shell "+k+" ring shown before its throw");
        }
        float lastThrow=TideRules.Release(TideRules.ShotCount-1);
        Check(TideRules.Duration(TideRules.Shot)>lastThrow+TideRules.Strike(TideRules.Shot), "last throw has a follow-through");
        Check(TideRules.Duration(TideRules.Shot)<lastThrow+TideRules.ShotFlight(0), "walks again while the last shells fly");
        // One volley slot per idol: the cooldown starts at the last throw and outlasts every flight plus its splash.
        Check(TideRules.ShotFlight(TideRules.ShotMax)+TideRules.ShotSplash<TideRules.ShotCooldown, "volley lifetime fits the cooldown");
        Check(!TideRules.InHit(TideRules.Shot,0,0,0) && !TideRules.CrossedHit(TideRules.Shot,0,5), "shells never use melee shapes or strike timing");
        Check(TideRules.MuzzleHeight>2.6f, "launch point above the 2.6 m hitbox");
        Check(Near(TideRules.ArcLift(0,3),0) && Near(TideRules.ArcLift(1,3),0) && Near(TideRules.ArcLift(.5f,3),3), "arc apex at mid-flight");
        Check(TideRules.ShotApexes[0]>TideRules.ShotApexes[1] && TideRules.ShotApexes[1]>TideRules.ShotApexes[2] && TideRules.ShotApexes[2]>0, "highest arc tried first");
        Check(Near(TideRules.LeadSeconds(.35f,1),(.35+1)*.5) && TideRules.ShotLeadMax>0, "half lead, capped");
        Check(TideRules.InSplash(0,0,0) && TideRules.InSplash(2.85f,0,0) && !TideRules.InSplash(2.87f,0,0) && !TideRules.InSplash(2.1f,2.1f,0), "2.86 m splash");
        Check(TideRules.InSplash(0,0,TideRules.HitHeight) && !TideRules.InSplash(0,0,TideRules.HitHeight+.01f), "other floors safe from the splash");
        Check(!TideRules.InSplash(Single.NaN,0,0) && !TideRules.InSplash(0,Single.PositiveInfinity,0) && !TideRules.InSplash(0,0,Single.NaN), "bad splash coordinates rejected");
        for(float x=-3.1f;x<=3.1f;x+=.05f) for(float z=-3.1f;z<=3.1f;z+=.05f)
            foreach(float h in new[]{-TideRules.HitHeight,-.9f,0,.9f,TideRules.HitHeight})
                if(TideRules.InSplash(x,z,h))
                    Check(Math.Sqrt(x*x+z*z+h*h)+.2<=TideRules.Range(TideRules.Shot),"native range covers splash "+x+","+z+","+h);
        // Every shell of a volley splashes exactly once and in order, however the frames fall.
        foreach(double dt in new[]{.008,.017,.033,.1,.4,2.0})
        {
            int splashed=0,splashes=0,order=0;bool inOrder=true;
            for(double now=dt;now<10;now+=dt)
                for(int k=0;k<TideRules.ShotCount;k++)
                    if((splashed&(1<<k))==0&&TideRules.ShotDue(now,1.5,k,1f)){splashed|=1<<k;splashes++;inOrder&=k>=order;order=k;}
            Check(splashes==TideRules.ShotCount&&inOrder,"one splash per shell with stalled frames "+dt);
        }
        Check(!TideRules.ShotDue(100,0,0,1) && !TideRules.ShotDue(2.84,1.5,1,1f) && TideRules.ShotDue(2.86,1.5,1,1f), "shell 1 lands 0.35 s after shell 0");
        Check(!TideRules.ShotDue(Double.NaN,1.5,0,1f) && !TideRules.ShotDue(3,Double.PositiveInfinity,0,1f), "bad shell clock rejected");
        // Snapshot validation of the volley fields.
        Check(TideRules.ValidVolley(0,0,5,10) && !TideRules.ValidVolley(0,1,5,10), "no volley carries no shells");
        Check(TideRules.ValidVolley(12,1,5,10) && TideRules.ValidVolley(12,TideRules.ShotCount,5,10), "valid volley");
        Check(!TideRules.ValidVolley(12,0,5,10) && !TideRules.ValidVolley(12,TideRules.ShotCount+1,5,10), "shell count bounded");
        Check(!TideRules.ValidVolley(4,1,5,10) && !TideRules.ValidVolley(17,1,5,10), "volley time bounded by birth and clock");
        Check(!TideRules.ValidVolley(Double.NaN,1,5,10) && !TideRules.ValidVolley(Double.PositiveInfinity,1,5,10), "bad volley clock rejected");
        Check(TideRules.ValidApex(0) && TideRules.ValidApex(TideRules.ShotApexes[0]) && TideRules.ValidApex(TideRules.ShotApexes[2]), "skipped and real arcs accepted");
        Check(!TideRules.ValidApex(-1) && !TideRules.ValidApex(9) && !TideRules.ValidApex(Single.NaN) && !TideRules.ValidApex(Single.PositiveInfinity), "bad arcs rejected");
        Check(TideRules.Shot>TideRules.Death && TideRules.SceneNameMax>=10, "new action is last; the game's scene names fit");
        // Tony's 0.18.0 retaliation: the attacker is hunted for 15 s after each hit, at any distance and height, while
        // nobody is within the search radius. The 8 m reach for targets off the NavMesh is my pick.
        Check(Near(TideRules.AggroSeconds,15), "hunt lasts 15 s after the last hit");
        Check(!TideRules.Provoked(0,5) && TideRules.Provoked(10,10) && TideRules.Provoked(10,25) && !TideRules.Provoked(10,25.01), "hunt window");
        Check(!TideRules.Provoked(Double.NaN,10) && !TideRules.Provoked(10,Double.NaN) && !TideRules.Provoked(Double.PositiveInfinity,10) && !TideRules.Provoked(-1,5), "bad hunt clock rejected");
        Check(Near(TideRules.ReachRadius,8) && TideRules.ReachRadius<TideRules.ShotMax, "approach spots end inside volley range");
        Console.WriteLine("PASS: "+count+" Tidefork purchase/authority/geometry/timing rules");
    }
}
