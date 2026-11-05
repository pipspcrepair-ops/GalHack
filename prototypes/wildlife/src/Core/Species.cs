using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// One generated species: everything the runtime needs to build, animate and run it. Plain data,
/// produced by <see cref="FaunaGenerator"/> and never changed afterwards, so every individual of
/// a species (and every player's copy of the planet) agrees.
/// </summary>
public sealed class Species
{
    public string Id = "";
    public int Index;
    public ulong Seed;
    public string Name = "";        // the proper name the codex gives it: "Vashkari"
    public string CommonName = "";  // what it looks like: "crowned strider"

    public BodyPlan Plan;
    public string Form = "";
    public RigKind Rig;

    /// <summary>Slot ("head", "horns", ...) to part id. Every slot is present, "none" if empty.</summary>
    public Dictionary<string, string> Parts = new();

    public Proportions Body = new();
    public Palette Colors = new();

    public Behaviour Behaviour;
    public Temperament Temperament;
    /// <summary>Its signature attack: what the telegraph looks and sounds like, and how long it is.</summary>
    public AttackDef Attack = new();
    public int GroupMin = 1, GroupMax = 1;

    public Stats Stats = new();
    public Drops Drops = new();

    public float Weirdness;
    public bool Corrupted;
    public bool Burrows, Flies, Tumbles, Rooted;

    public string Part(string slot) => Parts.TryGetValue(slot, out var id) ? id : "none";
    public bool Has(string slot) => Part(slot) != "none";

    /// <summary>Everything that decides how it looks, for spotting two species built the same.</summary>
    public string PartSignature
    {
        get
        {
            var sb = new System.Text.StringBuilder(Form);
            foreach (string slot in FaunaTables.Slots) sb.Append('|').Append(Part(slot));
            sb.Append('|').Append(Body.Legs).Append('|').Append(Body.LegStyle).Append('|').Append(Body.Segments)
              .Append('|').Append(Body.Wings).Append('|').Append(Body.Bags);
            return sb.ToString();
        }
    }

    /// <summary>Seconds of wind-up before this species' first, telegraphed attack on a player.</summary>
    public float TelegraphWindup(CombatRules rules) => Stats.Telegraph > 0f ? Stats.Telegraph : rules.TelegraphWindup;

    /// <summary>Pack hunters, lone predators and ambushers: the ones built to kill.</summary>
    public bool IsHunter => Behaviour is Behaviour.Predator or Behaviour.PackHunter or Behaviour.Ambusher;

    public override string ToString() =>
        $"{Name} ({CommonName}) {Plan}/{Form} {Behaviour}+{Temperament} size {Body.Size:F1} m weird {Weirdness:F2}{(Corrupted ? " CORRUPTED" : "")}";
}

/// <summary>Body measurements in metres (already scaled to the species' size).</summary>
public sealed class Proportions
{
    public float Size;          // body length
    public float Width, Height; // body cross-section
    public float LegLength;     // hip to ground when standing straight
    public float LegThickness;
    public float NeckLength, HeadSize, TailLength;
    public int Legs;
    public LegStyle LegStyle;
    public int Segments, Wings, Bags, Members;
    /// <summary>0 for textbook proportions, up to 1 for absurd ones (stilts under a tiny body).</summary>
    public float Extremity;
}

public sealed class Palette
{
    public Color Base, Accent, Belly, Glow;
    public Pattern Pattern;
    public float PatternScale = 1f;
    /// <summary>0 = no glow at all; 1 = eyes, spots and glowing parts blaze at night.</summary>
    public float GlowStrength;
}

public sealed class Stats
{
    public float MaxHealth, WalkSpeed, RunSpeed, Damage, AttackRange, Perception, FleeRadius;
    public float Territory, AmbushRadius;
    /// <summary>Wind-up of the first attack on a player, from the behaviour's data (0 = combat default).</summary>
    public float Telegraph;
    public bool Retaliates, Hunts;
    /// <summary>Rough radius for hits, collisions and the harvester.</summary>
    public float Radius;
}

public sealed class Drops
{
    public int Meat;
    public string RarePart = "";
    public float RareChance;
    public int RareValue;
}
