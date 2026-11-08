using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The kinds that hold places, out of what the world already has: its own beasts taken by the
/// Hollow growth, its predators twisted, its machines, and people grafted to them. Rolled once
/// each from the world's seed, from the world's own species where it has them, so a place's
/// holders are the same animals and machines you meet outside, not a table of their own.
/// </summary>
public sealed partial class WorldLife
{
    List<Species> _hollows, _twisted, _machines, _cyborgs;

    /// <summary>Kinds that walk and fit indoors: no giants, fliers, swarms, burrowers or rooted things, nothing kept, no people.</summary>
    static bool Walks(Species s) =>
        !s.Mechanical && s.Figure == null && s.Domestic == DomesticKind.None && s.Breaker == BreakerKind.None
        && !s.Flies && !s.Burrows && !s.Rooted && s.Plan is not (BodyPlan.Giant or BodyPlan.Swarm or BodyPlan.Floater or BodyPlan.Flier or BodyPlan.Voidborne);

    /// <summary>
    /// The Hollows: the world's own beasts, taken by the fungus. On a Hollow world they are its
    /// wildlife already; anywhere else the growth's places take them from the local kinds.
    /// </summary>
    public IReadOnlyList<Species> Hollows => _hollows ??= MakeHollows();

    List<Species> MakeHollows()
    {
        var list = new List<Species>();
        foreach (Species s in Fauna.Species)
            if (s.Corrupted && Walks(s) && list.Count < 3) list.Add(s);
        var hosts = Fauna.Species.FindAll(s => !s.Corrupted && Walks(s));
        // The hunters first: what the growth makes of a predator is what holds a place.
        hosts.Sort((a, b) => Hunter(b).CompareTo(Hunter(a)));
        for (int i = 0; i < hosts.Count && list.Count < 3; i++)
        {
            Species h = hosts[i].Copy();
            h.Mate = null;
            h.Young = null;
            h.Index = 300 + hosts[i].Index;
            h.Id = hosts[i].Id + "-hollow";
            _generator.CorruptSpecies(h, Rng.Mix(hosts[i].Seed, 0x4011));
            Aggressive(h, 8f);
            list.Add(h);
        }
        return list;
    }

    static int Hunter(Species s) => s.Behaviour is Behaviour.Predator or Behaviour.PackHunter or Behaviour.Ambusher ? 2 : s.Behaviour is Behaviour.Scavenger ? 1 : 0;

    /// <summary>Twisted beasts: the world's own predators changed past what they were (its twisted stalker first, where its fall left one).</summary>
    public IReadOnlyList<Species> Twisted => _twisted ??= MakeTwisted();

    List<Species> MakeTwisted()
    {
        var list = new List<Species>();
        foreach (Species s in Fauna.Species)
            if (s.Id.EndsWith("-twisted", StringComparison.Ordinal) && Walks(s)) list.Add(s);
        var hosts = Fauna.Species.FindAll(s => Walks(s) && !s.Corrupted && !s.Id.EndsWith("-twisted", StringComparison.Ordinal));
        hosts.Sort((a, b) => Hunter(b).CompareTo(Hunter(a)));
        float k = Math.Clamp(0.6f + 0.4f * MathF.Max(World.Mutagen, World.Predators), 0.6f, 1f);
        for (int i = 0; i < hosts.Count && list.Count < 2; i++)
        {
            Species t = _generator.Mutate(hosts[i], k);
            if (ReferenceEquals(t, hosts[i])) t = t.Copy();
            t.Mate = null;
            t.Young = null;
            t.Id = hosts[i].Id + "-denned";
            t.Index = 540 + i;
            Aggressive(t, 10f);
            list.Add(t);
        }
        return list;
    }

    /// <summary>The world's machines: those already walking it, or the old security's frames where none do.</summary>
    public IReadOnlyList<Species> Machines => _machines ??= MakeMachines();

    List<Species> MakeMachines()
    {
        var list = Fauna.Species.FindAll(s => s.Mechanical && s.Figure?.Dress != "grafted");
        if (list.Count == 0)
        {
            list.Add(Robots.Roll(World, 2));
            list.Add(Robots.Roll(World, 0));
        }
        return list;
    }

    /// <summary>
    /// Cyborgs: the world's own people with machine grafted on. Plate over the body where clothes
    /// were, a sealed head with a lit visor, one arm a longer metal one; hard to put down, never
    /// friendly.
    /// </summary>
    public IReadOnlyList<Species> Cyborgs => _cyborgs ??= MakeCyborgs();

    List<Species> MakeCyborgs()
    {
        var list = new List<Species>();
        for (int i = 0; i < 2; i++)
        {
            ulong seed = Rng.Mix(Seed, 0xC7B0 + (ulong)i);
            var rng = Rng.For(seed, "graft");
            Species s = People.Person(seed, i == 0 ? PersonRole.Guard : PersonRole.Raider, Occupancy.Raider);
            Figure f = s.Figure;
            Color plate = new Color(0.36f, 0.37f, 0.38f).Lerp(Color.FromHsv(rng.Range(0f, 1f), 0.3f, 0.4f), rng.Range(0.1f, 0.4f));
            f.Dress = "suit";
            f.Cloth = plate;
            f.Trim = plate.Darkened(0.35f);
            f.Metal = plate.Lightened(0.2f);
            f.Wear.RemoveWhere(w => HumanBody.Slot(w) is "head" or "mouth" or "eyes" or "over");
            f.Wear.Add("suit_helmet");
            f.Accent = Color.FromHsv(rng.Pick(new[] { 0f, 0.08f, 0.5f }), 0.9f, 1f);
            f.LongArm = rng.Int(0, 1);
            f.ArmStretch = rng.Range(1.12f, 1.24f);
            f.Wrongs |= Wrong.Arm;
            f.Measure();
            s.Id = $"cyborg-{seed:x}";
            s.Index = 330 + i;
            s.CommonName = "cyborg";
            s.Mechanical = true;
            s.Coat = Coat.Plating;
            s.Colors.Glow = f.Accent;
            s.Colors.GlowStrength = 0.8f;
            s.Senses = new SenseProfile { Sight = 1.1f, DarkVision = 1f, Hearing = 0.9f, Scent = 0f };
            s.Stats.MaxHealth = MathF.Round(s.Stats.MaxHealth * 2.2f);
            s.Stats.Damage = MathF.Max(s.Stats.Damage, 18f);
            s.Stats.FleeRadius = 0f;
            s.Attack = new AttackDef { Name = "Strike", Style = AttackStyle.Lunge, Telegraph = 1.1f, Sound = "servo" };
            list.Add(s);
        }
        return list;
    }

    static void Aggressive(Species s, float damage)
    {
        s.Behaviour = s.Behaviour is Behaviour.PackHunter or Behaviour.Ambusher ? s.Behaviour : Behaviour.Predator;
        s.Temperament = Temperament.Aggressive;
        s.Stats.Hunts = true;
        s.Stats.Retaliates = true;
        s.Stats.FleeRadius = 0f;
        s.Stats.Damage = MathF.Max(s.Stats.Damage, damage);
    }
}
