using System;
using TonyMods;
using UnityEngine;
using UnityEngine.AI;

internal static class TideFriendTests
{
    private static int count;
    private static void Check(bool value, string message) { count++; if (!value) throw new Exception(message); }
    private static CreatureHostile Enemy(PlayerNet chasing)
    {
        var enemy = new CreatureHostile { chased = chasing };
        enemy.Attach(new Vulnerable());
        return enemy;
    }
    private static void Main()
    {
        PlayerManager.Instance = new PlayerManager();
        var host = new PlayerNet(); var client = new PlayerNet();
        PlayerManager.Instance.players.Add(0, host); PlayerManager.Instance.players.Add(7, client);
        var body = new TideCreature(); body.State.friendly = true; body.State.summoner = 7;
        var friend = new TideFriend(body);
        Check(friend.ResolveOwner() && friend.Master == client, "client 7 remains the owner, not host 0");
        var danger = Enemy(client); var otherPlayerDanger = Enemy(host); var calm = Enemy(null);
        var allied = Enemy(client); var alliedBody = new TideCreature(); alliedBody.State.friendly = true; allied.Attach(alliedBody);
        var livestock = Enemy(client); livestock.Attach(new CreaturePaddockAnimal());
        var pet = Enemy(client); pet.Attach(new PetBase());
        var dead = Enemy(client); dead.GetComponent<Vulnerable>().hp.Value = 0;
        var invincible = Enemy(client); invincible.GetComponent<Vulnerable>().invinsible.Value = true;
        Physics.colliders = new Collider[] { danger, otherPlayerDanger, calm, allied, livestock, pet, dead, invincible };
        TideSummons.Now = 100;
        Check(friend.Target() == danger, "only owner's attacker selected");
        foreach (var safe in new[] { otherPlayerDanger, calm, allied, livestock, pet, dead, invincible })
        {
            Check(!friend.Eligible(safe), "unrelated/protected target excluded");
            friend.Hit(safe, 20);
            Check(safe.GetComponent<Vulnerable>().hits == 0, "splash cannot harm excluded target");
        }
        friend.Hit(danger, 20);
        Check(danger.GetComponent<Vulnerable>().hp.Value == 80, "authorized monster takes native damage");
        friend.Attacked(calm.GetComponent<Vulnerable>());
        Check(friend.Eligible(calm), "owner's hit authorizes a monster not yet chasing owner");
        friend.Attacked(allied.GetComponent<Vulnerable>()); friend.Attacked(livestock.GetComponent<Vulnerable>());
        Check(!friend.Eligible(allied) && !friend.Eligible(livestock), "owner hit never authorizes allies or livestock");
        // Once the original threat switches to someone else, an already-flying shell must leave it alone.
        danger.chased = host;
        int hits = danger.GetComponent<Vulnerable>().hits;
        friend.Hit(danger, 9);
        Check(danger.GetComponent<Vulnerable>().hits == hits, "impact rechecks changed combat ownership");
        TideSummons.Now = 114.9;
        Check(friend.Eligible(calm), "recent owner attack retained");
        TideSummons.Now = 115;
        Check(!friend.Eligible(calm) && friend.Target() == null, "combat expires and unrelated neighbours stay safe");
        TideSummons.Now = 200; friend.Attacked(calm.GetComponent<Vulnerable>());
        friend.Pause(20); TideSummons.Now = 220;
        Check(friend.Eligible(calm), "solo pause preserves assist window");
        calm.transform.position = new Vector3(36, 0, 0);
        Check(!friend.Eligible(calm), "monster beyond owner's leash is abandoned");
        calm.transform.position = new Vector3(1, 0, 0);
        body.transform.position = new Vector3(36, 0, 0);
        Check(friend.Target() == null, "distant companion returns before fighting");
        var agent = new NavMeshAgent();
        friend.Follow(agent);
        Check(agent.destinations == 1 && !agent.isStopped, "idle companion follows owner");
        Check(Vector3.Distance(agent.lastDestination, client.transform.position) == 0, "follow stop distance is measured from owner, not offset twice");
        TideSummons.Now += .05; friend.Follow(agent);
        Check(!agent.isStopped && agent.destinations == 1, "throttled repath leaves movement running");
        TideSummons.Now += 1; body.transform.position = new Vector3(0, 0, 0); friend.Follow(agent);
        Check(agent.isStopped, "companion rests beside owner");
        TideSummons.Now += 1; body.transform.position = new Vector3(60, 0, 0); friend.Follow(agent);
        Check(agent.warps == 1, "far companion catches up like native pets");
        client.hp.Value = 0; Check(!friend.ResolveOwner(), "dead owner causes companion cleanup");
        client.hp.Value = 100; client.isDespawning = true; Check(!friend.ResolveOwner(), "disconnecting owner causes cleanup");
        client.isDespawning = false; PlayerManager.Instance.players.Remove(7);
        Check(!friend.ResolveOwner(), "missing owner is not replaced by another player");
        Check(!TideFriendRules.Authorized(false, false, true, true, 0, 1), "non-monsters never eligible");
        Check(!TideFriendRules.Authorized(true, true, true, true, 0, 1), "protection wins over combat");
        Check(!TideFriendRules.Authorized(true, false, true, false, Double.NaN, 1), "invalid attack time rejected");
        Console.WriteLine("PASS: " + count + " production companion targeting, collateral, follow and owner lifecycle checks");
    }
}
