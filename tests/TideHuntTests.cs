using System;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;
namespace TonyMods
{
    internal static class TideHuntTests
    {
        static int checks;
        static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
        static CreatureHostile Enemy(float x,bool natural)
        {
            var go=new GameObject();go.transform.position=new Vector3(x,0,0);
            var c=go.Add<CreatureHostile>();go.Add<Vulnerable>();var s=go.Add<Spawnable>();if(natural)TideHuntBuilding.Natural.Add(s);return c;
        }
        static TideHunter Setup(out TideCreature body,out TideHuntHome home,out NavMeshAgent agent)
        {
            TideSummons.Now=100;TideHuntBuilding.Natural.Clear();CollectibleManager.Instance=new CollectibleManager();
            body=new GameObject().Add<TideCreature>();agent=body.gameObject.Add<NavMeshAgent>();home=new GameObject().Add<TideHuntHome>();return new TideHunter(body,home);
        }
        static void Main()
        {
            TideCreature body;TideHuntHome home;NavMeshAgent agent;
            var h=Setup(out body,out home,out agent); CreatureHostile wild=Enemy(50,true),quest=Enemy(40,false);
            Check(h.Allowed(wild,true),"natural wolf allowed");Check(!h.Allowed(quest,true),"quest/manual spawn excluded");
            wild.transform.position=new Vector3(500,0,0);Check(h.Allowed(wild,true),"expanded 100-to-1000 metre territory included");
            wild.transform.position=new Vector3(1000,0,0);Check(h.Allowed(wild,true),"1000 metre edge included");
            wild.transform.position=new Vector3(1000.01f,0,0);Check(!h.Allowed(wild,true),"outside edge excluded");
            wild.transform.position=new Vector3(20,0,0);wild.GetComponent<Spawnable>().type=Spawnable.Species.SpiderBoss;
            Check(!h.Allowed(wild,true),"boss excluded even if natural");wild.GetComponent<Spawnable>().type=Spawnable.Species.Wolf;
            wild.gameObject.Add<PaddockAnimal>();Check(!h.Allowed(wild,true),"domestic animal excluded");
            wild=Enemy(30,true);wild.lootItems.Add(new LootItem{itemData=new ItemData{quest=true}});
            Check(!h.Allowed(wild,true),"quest loot excludes entire prey");wild.lootItems.Clear();wild.gameObject.Add<QuestGiver>();
            Check(!h.Allowed(wild,true),"NPC excluded");
            h=Setup(out body,out home,out agent);CreatureHostile a=Enemy(10,true),b=Enemy(20,true);
            UnityEngine.Random.Pick=1;Check(h.Target()==b,"random selection can choose farther prey");
            b.transform.position=new Vector3(1001,0,0);h.Hit(b,20);Check(b.GetComponent<Vulnerable>().hits==0,"impact rechecks leash");
            a.GetComponent<Vulnerable>().invinsible.Value=true;Check(!h.Allowed(a,true),"invulnerable excluded");
            h=Setup(out body,out home,out agent);wild=Enemy(2,true);agent.reachable=false;
            Check(h.Target()==null,"unreachable prey rejected");agent.reachable=true;
            Check(h.Target()==null,"unreachable prey has retry backoff");
            h=Setup(out body,out home,out agent);wild=Enemy(2,true);
            ushort left;home.Cargo.AddNewItem(new Item(new ItemData()){amount=29},out left,false);
            Check(h.Target()==wild,"29 items still hunts");home.Cargo.AddNewItem(new Item(new ItemData()){amount=1},out left,false);
            Check(h.Target()==null,"30 items stops hunting");h.Hit(wild,9);Check(wild.GetComponent<Vulnerable>().hits==0,"in-flight impact stops after threshold");
            h.Move(agent);Check(agent.destination.x==5,"return path heads to tavern delivery");
            body.transform.position=home.delivery;TideSummons.Now+=1;h.Move(agent);
            Check(home.Count==0 && CollectibleManager.Instance.drops.Sum(d=>d.item.Value.amount)==30,"delivery conserves exactly 30 items");
            TideSummons.Now+=3;Check(h.Target()==wild,"resumes after delivery");
            h=Setup(out body,out home,out agent);wild=Enemy(1,true);
            wild.lootItems.Add(new LootItem{amountOverride=3});h.Hit(wild,100);Check(wild.GetComponent<Vulnerable>().hp.Value==0,"hunter kills prey");
            Check(h.Target()==null,"corpse collection precedes more hunts");h.Move(agent);
            Check(home.Count==3 && CollectibleManager.Instance.drops.Count==0,"native out argument is remaining, not added; no duplicate ground items");
            Check(home.Cargo.data[0].rarity==ItemData.Rarity.Rare && home.Cargo.data[0].charge==7,"loot preserves quality and item properties");
            TideSummons.Now++;h.Move(agent);Check(home.Count==3,"corpse is collected once");
            h=Setup(out body,out home,out agent);wild=Enemy(1,true);wild.lootItems.Add(new LootItem{amountOverride=5});home.Cargo.Capacity=2;
            h.Hit(wild,100);h.Target();h.Move(agent);
            Check(home.Count==2 && CollectibleManager.Instance.drops.Single().item.Value.amount==3,"partial insert leaves only remainder on corpse");
            h=Setup(out body,out home,out agent);wild=Enemy(1,true);wild.lootItems.Add(new LootItem{amountOverride=4});
            h.Hit(wild,100);wild.isLootAvailable.Value=false;h.Target();h.Move(agent);
            Check(home.Count==0,"waits for native death animation loot flag");TideSummons.Now++;wild.isLootAvailable.Value=true;h.Target();h.Move(agent);Check(home.Count==4,"collects delayed corpse");
            h=Setup(out body,out home,out agent);home.House.isHelperActive.Value=false;wild=Enemy(1,true);Check(h.Target()==null,"disabled house never selects prey");
            home.Cargo.AddNewItem(new Item(new ItemData()){amount=7,rarity=ItemData.Rarity.Rare},out left,false);
            CollectibleManager.Instance.Reject=true;home.DropAt(home.delivery);Check(home.Count==7,"failed spawn retains cargo");
            CollectibleManager.Instance.Reject=false;home.DropAt(home.delivery);home.DropAt(home.delivery);
            Check(home.Count==0 && CollectibleManager.Instance.drops.Count==1,"retry delivers once");
            home.Cargo.AddNewItem(new Item(new ItemData()){amount=6},out left,false);home.House.IsServer=false;home.DropAt(home.delivery);Check(home.Count==6,"client cannot deliver");
            home.House.IsServer=true;Master.Instance.Joining=true;home.DropAt(home.delivery);Check(home.Count==6,"joining player defers delivery");Master.Instance.Joining=false;
            home.House.isHelperActive.Value=true;h.Died();Check(home.Count==0&&!home.Active,"death drops cargo and stops work");
            Check(TideHuntRules.Within(600,800)&&!TideHuntRules.Within(float.NaN,0),"radial and finite boundary");
            Check(!TideHuntRules.Species("SkeletonBoss")&&!TideHuntRules.Species("FutureUnknown"),"allowlist fails closed");
            h=Setup(out body,out home,out agent);home.Cargo.AddNewItem(new Item(new ItemData()){amount=30},out left,false);
            TideSummons.Now+=100;h.Target();h.Move(agent);Check(body.transform.position.x==0,"time spent fighting is not return-path stuck time");
            TideSummons.Now+=21;h.Move(agent);Check(body.transform.position.x==5,"blocked delivery path recovers after actual timeout");
            CollectibleManager.Instance.ThrowAfterSpawn=true;home.DropAt(home.delivery);Check(home.Count==0&&CollectibleManager.Instance.drops.Count==1,"post-spawn exception commits once");
            home.Cargo.AddNewItem(new Item(new ItemData()){amount=3},out left,false);
            CollectibleManager.Instance.ThrowAfterSpawn=false;CollectibleManager.Instance.ThrowBeforeSpawn=true;
            try{home.DropAt(home.delivery);}catch(Exception){}
            Check(home.Count==3&&CollectibleManager.Instance.drops.Count==1,"failed partial spawn rolls back, retains cargo");
            CollectibleManager.Instance.ThrowBeforeSpawn=false;home.DropAt(home.delivery);Check(home.Count==0&&CollectibleManager.Instance.drops.Sum(d=>d.item.Value.amount)==33,"retry does not duplicate partial delivery");
            h=Setup(out body,out home,out agent);wild=Enemy(1,true);wild.lootItems.Add(new LootItem{amountOverride=4});
            home.Cargo.AddNewItem(new Item(new ItemData()){amount=29},out left,false);
            h.Hit(wild,100);h.Target();h.Move(agent);Check(home.Count==33,"whole corpse batch may cross 30 without discarding excess");
            TideSummons.Now++;Check(h.Target()==null,"batch crossing threshold returns before selecting another prey");
            Console.WriteLine("PASS: "+checks+" production hunting, exclusion, collection and delivery checks");
        }
    }
}
