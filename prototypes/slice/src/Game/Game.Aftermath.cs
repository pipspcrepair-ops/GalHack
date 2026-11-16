using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using Godot;
using Slice.Items;
using Slice.Places;
using Slice.Surface;
using Wildlife;

namespace Slice.Game;

/// <summary>
/// After the sleeper dies (owner, 16 Nov), and the goods people give up. Whoever was fighting stops:
/// every creature of every sim drops the sleeper (<see cref="Brain.TargetDied"/>) and every town stands
/// its fighters down while the body lies there. The body lies where it fell, kept by the wildlife as any
/// body is: carrion eaters find it, and people near may go through the motions of rummaging through the
/// bag beside it (taking nothing). A Husk that made the kill kneels on it and puts its worms into the
/// face (<see cref="Brain.Infest"/>), and the body rises a Husk in the sleeper's suit, and keeps about the
/// bag: the bag holds what rose from its owner, so it is met there however far off you went. And the
/// bundle a frightened one tosses down at gunpoint comes down as a bag in front of you. The respawn
/// itself is as it was.
/// </summary>
public partial class Game
{
    /// <summary>The sleeper's body from the last death, while the wildlife keeps it, and the sim it lies in (tests).</summary>
    public Creature Body { get; private set; }
    public WildSim BodyIn { get; private set; }

    /// <summary>Bags tossed down at gunpoint (tests).</summary>
    public int Offered { get; private set; }

    /// <summary>The creature that last hurt the sleeper (wildlife's), and on which physics frame: a death within a moment of it is theirs.</summary>
    Creature _hurtBy;
    ulong _hurtAt;
    /// <summary>What each one gave up at gunpoint: their bag on death holds only the rest.</summary>
    readonly Dictionary<Creature, Bill> _given = new();
    int _risenTick;

    void SetUpAftermath()
    {
        Zone.Loot.BagSize = Zone.Data.Rules.Containers.Bag;
        Zone.Loot.YourColour = BagColour(Slice.Player.CharacterModel.Sleeper.Figure);
        Zone.Wildlife.Offered += OnOffered;
        Zone.Wildlife.Turned += OnTurned;
    }

    /// <summary>Something of the wildlife hurt the sleeper (a blow, a hold): if they die of it, it was theirs.</summary>
    void HurtBy(Creature c)
    {
        _hurtBy = c;
        _hurtAt = Engine.GetPhysicsFrames();
    }

    /// <summary>
    /// The sleeper just died at <paramref name="feet"/>, going down toward <paramref name="fall"/>: the
    /// body is left lying there, in the sim of whatever killed them (the open country's if nothing did, or
    /// nothing if they died under a roof), and everyone stands down.
    /// </summary>
    void Aftermath(Vector3 feet, Vector3 fall)
    {
        WildlifePatch patch = Zone.Wildlife;
        if (patch?.Sim == null) return;
        Creature killer = _hurtBy != null && !_hurtBy.Removed && Engine.GetPhysicsFrames() - _hurtAt < 180 ? _hurtBy : null;
        _hurtBy = null;
        WildSim sim = null;
        Vector3 origin = patch.Origin;
        if (killer != null) sim = patch.SimOf(killer, out origin, out _);
        else if (!Sheltered(feet)) sim = patch.Sim;
        if (sim != null)
        {
            Species s = Slice.Player.CharacterModel.Corpse(Suit.Sim.Player.Gear.HelmetOn);
            Creature body = killer?.Space != null ? sim.SpawnIn(s, killer.Space, feet - origin) : sim.Spawn(s, feet - origin);
            body.Yaw = Survivor.Yaw;
            body.HitDir = fall;
            sim.Kill(body, killer?.AsTarget); // (a Husk's kill: it kneels on it, Brain.Infest)
            Body = body;
            BodyIn = sim;
        }
        // Nobody goes on fighting a body.
        Vector3 things = DeathBag?.Position ?? feet; // (what there is to go through)
        Brain.TargetDied(patch.Sim, patch.Player, patch.PatchLocal(things));
        foreach (DeepSite site in patch.Sites) Brain.TargetDied(site.Sim, site.Player, site.Local(things));
        foreach (Town t in _towns) t.StandDown();
    }

    /// <summary>
    /// One held at gunpoint tossed down a bundle of their goods to make you leave: a share of what they carry
    /// by how frightened they are, never all of it, thrown from their hands to land on the ground between you.
    /// </summary>
    void OnOffered(Creature c, ITarget to, Vector3 at, float fear)
    {
        WildlifePatch patch = Zone.Wildlife;
        if (c.Species.Figure is not { } f) return;
        Bill goods = BodyDrops(c.Species, Wildlife.Rng.For(c.Species.Seed, "drop", c.Id));
        int total = goods.Sum(kv => kv.Value);
        if (total == 0) return;
        int give = Math.Clamp((int)MathF.Ceiling(total * (0.25f + 0.5f * fear)), 1, Math.Max(1, total - 1));
        var bundle = new Bill();
        foreach (var (id, n) in goods)
        {
            int k = Math.Min(n, give);
            if (k <= 0) break;
            bundle.Add(id, k);
            give -= k;
        }
        if (!_given.TryGetValue(c, out Bill had)) _given[c] = had = new Bill();
        foreach (var (id, n) in bundle) had.Add(id, n);
        Vector3 hands = patch.WorldOf(c) + Vector3.Up * f.Height * 0.72f + c.Forward * 0.3f;
        Zone.Loot.Toss(bundle, hands, patch.FromPatch(at), "Bag", BagColour(f), c);
        Offered++;
    }

    /// <summary>The sleeper's body rose a Husk: its bag keeps it, to be met about it.</summary>
    void OnTurned(SeizeEvent e)
    {
        if (e.Turned == null || e.Victim is not CreatureTarget { Creature: { } was } || was != Body) return;
        Body = null;
        if (DeathBag == null) return;
        DeathBag.Risen = e.Turned.Species;
        DeathBag.RisenNow = e.Turned;
    }

    /// <summary>
    /// What rose from a bag's owner keeps about it: once a second, a bag whose Husk the open country's sim
    /// has lost (the patch moved off and back) has it rise again near it, wandering there. One killed is
    /// gone for good (<see cref="Laid"/>).
    /// </summary>
    void KeepRisen()
    {
        if (++_risenTick % 60 != 0 || Zone.Wildlife?.Sim is not { } sim) return;
        WildlifePatch patch = Zone.Wildlife;
        foreach (Lootable l in Zone.Loot.All)
        {
            if (l.Risen == null) continue;
            if (sim.Creatures.FirstOrDefault(c => c.Species == l.Risen && !c.Removed && !c.Dead) is { } now)
            {
                l.RisenNow = now; // (carried across a new patch: the same one)
                continue;
            }
            Vector3 bag = patch.PatchLocal(l.Position);
            if (!sim.Terrain.Inside(bag, 30f)) continue;
            var rng = new Wildlife.Rng(l.Risen.Seed ^ (ulong)_risenTick);
            float a = rng.Range(0f, MathF.Tau), r = rng.Range(4f, 14f);
            Creature h = sim.Spawn(l.Risen, sim.FreeSpot(bag + new Vector3(MathF.Cos(a), 0f, MathF.Sin(a)) * r, rng));
            h.Home = bag;
            l.RisenNow = h;
        }
    }

    /// <summary>A Husk that rose from a bag's owner was killed: nothing rises there again.</summary>
    void Laid(Species s)
    {
        foreach (Lootable l in Zone.Loot.All)
            if (l.Risen == s) { l.Risen = null; l.RisenNow = null; }
    }
}
