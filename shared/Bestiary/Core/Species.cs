using System;
using System.Collections.Generic;
using Godot;
using Landform = Planets.Gen.Landform;
using LandformBlend = Planets.Gen.LandformBlend;

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

    /// <summary>Planet wildlife lives on the surface; space fauna in a nebula, a belt, a giant's skies, a wreck or the void.</summary>
    public Habitat Habitat = Habitat.Surface;
    /// <summary>Zero-g swimmers: how they move.</summary>
    public SwimStyle Swim;
    public bool InSpace => Habitat != Habitat.Surface;

    /// <summary>When it is out (by day, by night, at dawn and dusk, or any hour) and where it rests otherwise.</summary>
    public Activity Activity = Activity.Cathemeral;
    public RestKind RestKind;
    /// <summary>Clings to walls and ceilings (caves, tunnels, the inside of places); otherwise it keeps to floors.</summary>
    public bool Climbs;
    /// <summary>Its job in a cave or a place (lurker, prowler, ambusher...); None for the open surface's fauna.</summary>
    public DeepRole Role;
    /// <summary>The kind of place it lives in and how deep (0 at the way in), which set its look.</summary>
    public PoiTheme Theme;
    public int Depth;
    /// <summary>No eyes: it finds you by sound, and clicks as it hunts.</summary>
    public bool Blind;
    /// <summary>How it gives an ambush away, and how long the tell runs before it strikes.</summary>
    public TellKind[] Tells = Array.Empty<TellKind>();
    public float TellSeconds;
    /// <summary>How close a player walking upright gets before it notices them, and how close prey must come before an ambush springs.</summary>
    public float NoticeRadius, StrikeRadius;
    /// <summary>Nesters: the warning and defence radii round their nest.</summary>
    public float WarnRadius, DefendRadius;
    /// <summary>Hunts the place's subsisters; flees into cracks; what it eats.</summary>
    public bool HuntsPrey, FleesIntoCracks;
    /// <summary>Keeps to itself (a place's insects): crawls its walls, floors and ceilings and scatters from you; bites only when hurt.</summary>
    public bool Passive;
    public Diet Diet;
    public bool IsDeep => Role != DeepRole.None;
    /// <summary>How twisted it is by a mutagenic world (fallout, a biolab agent, the Bloom's touch): 0 not at all, 1 fully.</summary>
    public float Mutation;
    /// <summary>A settlement's own animal (stock, working beast or pet), or None for the wild.</summary>
    public DomesticKind Domestic;
    /// <summary>A person: their body and clothes. Null for everything else.</summary>
    public Figure Figure;
    /// <summary>Mutation has gone past growths: the body itself has changed shape.</summary>
    public bool Monstrous;
    /// <summary>A person taken by the worms (see <see cref="Husks"/>).</summary>
    public bool Husk => Figure?.Husk == true;
    /// <summary>Bodies one of these may be instead (each spawned is one of them): a world's Husks were many different people.</summary>
    public List<Species> Hosts;
    /// <summary>What covers it: fur on the cold worlds, scale and thin skin on the hot ones.</summary>
    public Coat Coat;
    /// <summary>How it knows what is around it.</summary>
    public SenseProfile Senses = new();
    /// <summary>A block-breaker, and of which kind; None for everything else.</summary>
    public BreakerKind Breaker;
    /// <summary>Its young, where it has them in a herd; on a young one, the grown species.</summary>
    public Species Young, Adult;
    /// <summary>The other sex, where a species shows one. Both are this species.</summary>
    public Species Mate;
    /// <summary>How big its ears stand, 0 to 1: how well it hears, and how much heat it sheds.</summary>
    public float EarSize;
    /// <summary>No lungs and no blood: it runs on what it was built with.</summary>
    public bool Mechanical;
    /// <summary>Alive, and sealed against vacuum.</summary>
    public bool Hardened;
    /// <summary>How many groups of it there are, as a share of the usual.</summary>
    public float Scarcity = 1f;
    /// <summary>What its throat throws at you, if anything.</summary>
    public SpitKind Spit;
    /// <summary>A machine's mounted weapon: a weapon item (shared/Arms), fired from its mount; "" for none (it strikes).</summary>
    public string Mount = "";

    /// <summary>
    /// The landforms it lives in: it spawns only where they hold (and near their borders, as
    /// much as they hold there). Empty for species rolled without landforms: those live anywhere.
    /// </summary>
    public Landform[] Homes = Array.Empty<Landform>();
    /// <summary>How its body is built for its home ground: a wader's stilts, a climber's grip, a sand-swimmer's sleek body.</summary>
    public Adaptation Adaptation;

    /// <summary>How much of a spot with this landform blend is its home ground, 0 to 1 (1 everywhere if it has no homes).</summary>
    public float HomeWeight(LandformBlend blend)
    {
        if (Homes.Length == 0) return 1f;
        float w = 0f;
        foreach (Landform l in Homes) w += blend.Weight(l);
        return Math.Min(1f, w);
    }

    /// <summary>
    /// The colour of its blood, the same every time: red in anything warm-blooded, from rust to
    /// crimson; a yellow-green ichor in crawlers and swarms; what lives by glow bleeds its glow;
    /// and what the Bloom has taken bleeds a sick dark violet.
    /// </summary>
    public Color Blood
    {
        get
        {
            var rng = Rng.For(Seed, "blood");
            if (Corrupted) return Color.FromHsv(rng.Range(0.78f, 0.86f), 0.7f, rng.Range(0.22f, 0.34f));
            if (Colors.GlowStrength > 0.4f) return Colors.Glow.Darkened(0.2f);
            if (Plan is BodyPlan.Crawler or BodyPlan.Swarm || Rig == RigKind.Crawler) return Color.FromHsv(rng.Range(0.16f, 0.26f), 0.75f, rng.Range(0.5f, 0.65f));
            return Color.FromHsv(Mathf.PosMod(rng.Range(-0.02f, 0.03f), 1f), 0.85f, rng.Range(0.34f, 0.48f));
        }
    }

    /// <summary>The largest winged body that can hold still in the air, m: past this it has to keep flying.</summary>
    public const float HoverMostSize = 0.55f;

    /// <summary>
    /// How it stays up, by its body alone: anything winged and small and light hovers, on rapid
    /// wings; everything winged and bigger glides and can never stop in the air. There is no third way.
    /// </summary>
    public FlightKind Flight => Rig != RigKind.Winged ? FlightKind.None : Body.Size <= HoverMostSize ? FlightKind.Hover : FlightKind.Glide;

    /// <summary>Nose to rump, m.</summary>
    public float BodyLength => Body.Size + Body.NeckLength * 0.5f + Body.HeadSize * 0.5f;
    /// <summary>Across the body and its splayed legs, m.</summary>
    public float BodyWidth => Body.Width + Body.LegLength * 0.2f;
    /// <summary>Feet to the top of its back, m.</summary>
    public float BodyHeight => Body.LegLength + Body.Height;
    /// <summary>
    /// The clear width it needs to turn round in, m: its own diagonal and a little. A chain bends and
    /// a climber goes round by the walls, so they need less; a swarm is a cloud and needs none.
    /// </summary>
    public float TurnCircle => Rig == RigKind.Swarm ? 0.5f
        : MathF.Sqrt(BodyLength * BodyLength + BodyWidth * BodyWidth) * (Rig == RigKind.Chain || Climbs ? 0.6f : 1f) + TurnMargin;
    public const float TurnMargin = 0.7f;

    /// <summary>
    /// A copy to change (a variant: deeper, mutated): every field, with its body, palette, stats
    /// and parts copied too, so changing the copy never touches the original.
    /// </summary>
    public Species Copy()
    {
        var s = (Species)MemberwiseClone();
        s.Parts = new Dictionary<string, string>(Parts);
        s.Homes = (Landform[])Homes.Clone();
        s.Body = Shallow(Body);
        s.Colors = Shallow(Colors);
        s.Stats = Shallow(Stats);
        return s;
    }

    static readonly System.Reflection.MethodInfo Clone = typeof(object).GetMethod("MemberwiseClone", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

    static T Shallow<T>(T o) where T : class => (T)Clone.Invoke(o, null);

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

    /// <summary>Its body form in words, for the discovery card and the scan: "walker", "giant", "voidborne".</summary>
    public string FormLabel => Plan switch
    {
        BodyPlan.Walker => "walker",
        BodyPlan.Crawler => "crawler",
        BodyPlan.Hopper => "leaper",
        BodyPlan.Flier => "flier",
        BodyPlan.Floater => "floater",
        BodyPlan.Swarm => "swarm",
        BodyPlan.Giant => "giant",
        BodyPlan.Voidborne => Swim switch { SwimStyle.Pulse => "drifter", SwimStyle.Flap => "flier", SwimStyle.Undulate => "void serpent", _ => "hull clinger" },
        _ => "oddity",
    };

    /// <summary>What it does, in words: "herd grazer", "pack hunter", "territorial giant".</summary>
    public string BehaviourLabel => Behaviour switch
    {
        Behaviour.Herd => InSpace ? "shoal grazer" : "herd grazer",
        Behaviour.Ambusher when Rig == RigKind.Clinger => "hull-clinging ambusher",
        Behaviour.PackHunter => "pack hunter",
        Behaviour.TerritorialGiant => "territorial",
        Behaviour.HarmlessOddity => "harmless",
        _ => Behaviour.ToString().ToLowerInvariant(),
    };

    public override string ToString() =>
        $"{Name} ({CommonName}) {Plan}/{Form} {Behaviour}+{Temperament} size {Body.Size:F1} m weird {Weirdness:F2}{(Corrupted ? " CORRUPTED" : "")}";
}

/// <summary>What comes up a throat: nothing, blood (its own colour), bile, or a Husk's maggots.</summary>
public enum SpitKind { None, Blood, Bile, Maggots }

/// <summary>What covers a body.</summary>
public enum Coat { Hide, Fur, Wool, Blubber, Scale, ThinSkin, Chitin, Shell, Plating }

/// <summary>A species' senses: how it answers to light, sound and scent. Numbers are shares of the plain case.</summary>
public sealed class SenseProfile
{
    /// <summary>How far it sees in good light, as a share of its perception (0 blind).</summary>
    public float Sight { get; set; } = 1f;
    /// <summary>0 to 1: how well it sees with no light at all.</summary>
    public float DarkVision { get; set; }
    /// <summary>How far it hears.</summary>
    public float Hearing { get; set; } = 1f;
    /// <summary>It clicks and listens: sees the shape of things in the dark, a short way, and the clicks give it away.</summary>
    public bool Echo { get; set; }
    /// <summary>How far downwind it smells you, as a share of its perception (0 none).</summary>
    public float Scent { get; set; }

    public override string ToString() => $"sight {Sight:F1} dark {DarkVision:F1} hearing {Hearing:F1} scent {Scent:F1}{(Echo ? " echo" : "")}";
}

/// <summary>Body measurements in metres (already scaled to the size of the species).</summary>
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
    /// <summary>Experience a kill is worth to a game with character levels: it grows with how hard the animal is to bring down (health) and how dangerous it is (damage, hunting).</summary>
    public int Xp;
}

public sealed class Drops
{
    public int Meat;
    public string RarePart = "";
    public float RareChance;
    public int RareValue;
    /// <summary>Space fauna: materials on the rarity ladder, each with its own chance.</summary>
    public List<MaterialDrop> Materials = new();
}

/// <summary>One line of a drop table: a ladder material, how many, and how likely.</summary>
public sealed class MaterialDrop
{
    public string Material { get; set; } = "";
    public Rarity Rarity { get; set; } = Rarity.Rare;
    public int[] Amount { get; set; } = { 1, 1 };
    public float Chance { get; set; } = 1f;
}
