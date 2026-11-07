using System;
using System.Collections.Generic;
using Godot;
using Landform = Planets.Gen.Landform;
using LandformBlend = Planets.Gen.LandformBlend;
using LandformHabitat = Planets.Gen.LandformHabitat;
using LandformInfo = Planets.Gen.LandformInfo;

namespace Wildlife;

/// <summary>How a body is built for its ground (design doc rev 423, end of "Landforms").</summary>
public enum Adaptation
{
    None,
    /// <summary>Long-legged and light: herds and their pursuit hunters on open plains and steppe.</summary>
    Runner,
    /// <summary>Splayed, thick-legged and low, with grip: ledges, scree and cliff faces.</summary>
    Climber,
    /// <summary>Rides the updraughts off cliffs and ridges.</summary>
    Glider,
    /// <summary>Digs in and waits under the sand.</summary>
    Burrower,
    /// <summary>A sleek body that swims through loose sand.</summary>
    SandSwimmer,
    /// <summary>Stilt legs and a long neck for wading the shallows and marsh.</summary>
    Wader,
    /// <summary>Plated against heat, rock and claws.</summary>
    Armoured,
}

/// <summary>
/// The landforms a planet's wildlife adapts to: each region's creatures are built for its
/// ground, so crossing into new country means meeting new creatures.
///
/// Every landform's ground offers a few niches (herds and their hunters on plains and steppe,
/// climbers and cliff gliders in the mountains, burrowers and sand-swimmers in dune seas, waders
/// and ambushers in deltas and marshes...). A planet's species list grows from its landform mix:
/// each landform gets two or three species of its own, built for its niches, and landforms of
/// the same kind (plains and steppe, say) share a ranging species. Each species knows its home
/// landforms, and spawning picks from the species whose home includes the ground at the spot,
/// overlapping only near borders, where the landforms blend.
/// </summary>
public static class LandformFauna
{
    /// <summary>One niche of a landform's ground: which bodies can fill it, doing what, built how.</summary>
    public readonly record struct Niche(string[] Forms, Behaviour Behaviour, Adaptation Adaptation);

    /// <summary>The niches a habitat offers, most important first: the first two are always filled.</summary>
    public static Niche[] Niches(LandformHabitat habitat) => habitat switch
    {
        LandformHabitat.Open => new[]
        {
            new Niche(new[] { "strider", "longneck", "springer", "crystalback" }, Behaviour.Herd, Adaptation.Runner),
            new Niche(new[] { "stalker", "strider", "skitterer" }, Behaviour.PackHunter, Adaptation.Runner),
            new Niche(new[] { "colossus", "titanstilt" }, Behaviour.TerritorialGiant, Adaptation.None),
        },
        LandformHabitat.Steep => new[]
        {
            new Niche(new[] { "skitterer", "strider", "crystalback" }, Behaviour.Grazer, Adaptation.Climber),
            new Niche(new[] { "glider", "mothwing" }, Behaviour.Predator, Adaptation.Glider),
            new Niche(new[] { "stalker", "skitterer" }, Behaviour.Ambusher, Adaptation.Climber),
        },
        LandformHabitat.Sand => new[]
        {
            new Niche(new[] { "burrower" }, Behaviour.Ambusher, Adaptation.Burrower),
            new Niche(new[] { "serpent", "centipede" }, Behaviour.Predator, Adaptation.SandSwimmer),
            new Niche(new[] { "springer", "skitterer" }, Behaviour.Grazer, Adaptation.Runner),
        },
        LandformHabitat.Wet => new[]
        {
            new Niche(new[] { "longneck", "strider", "tripod" }, Behaviour.Grazer, Adaptation.Wader),
            new Niche(new[] { "serpent", "centipede" }, Behaviour.Ambusher, Adaptation.None),
            new Niche(new[] { "glider", "mothwing", "gasbag" }, Behaviour.Scavenger, Adaptation.None),
        },
        _ => new[]
        {
            new Niche(new[] { "strider", "crystalback", "springer" }, Behaviour.Grazer, Adaptation.None),
            new Niche(new[] { "stalker", "strider" }, Behaviour.Predator, Adaptation.None),
            new Niche(new[] { "centipede", "skitterer" }, Behaviour.Scavenger, Adaptation.Armoured),
        },
    };

    /// <summary>
    /// The colour of a landform's ground, which its creatures take on: sand on dunes, grey rock on
    /// mountains, mud and reed on marshes. A planet can pass its own (from its biomes) instead.
    /// </summary>
    public static Color Ground(Landform l) => l switch
    {
        Landform.Plains or Landform.Hills or Landform.RiverValleys => new Color("8a8a4a"),
        Landform.Steppe => new Color("b0a060"),
        Landform.Mountains or Landform.Fjords or Landform.CoastalCliffs => new Color("7a7470"),
        Landform.Plateau or Landform.Mesas or Landform.Canyonlands or Landform.Badlands => new Color("b06a40"),
        Landform.DuneSea => new Color("d8b070"),
        Landform.RockyDesert => new Color("b8a088"),
        Landform.Wetlands or Landform.Delta or Landform.LakeDistrict => new Color("5a6038"),
        Landform.IcePlain => new Color("dfe6ee"),
        Landform.Archipelago => new Color("c8b080"),
        Landform.VolcanicField or Landform.BasaltPlains => new Color("3a3436"),
        Landform.Karst => new Color("a8b0a0"),
        Landform.ShardFields or Landform.Ringlands => new Color("8a60b0"),
        Landform.DomeFields => new Color("c06838"),
        _ => new Color("808070"),
    };

    /// <summary>A word for the adaptation, for common names: "wading longneck", "sand-swimming slitherer".</summary>
    public static string Adjective(Adaptation a) => a switch
    {
        Adaptation.Runner => "running",
        Adaptation.Climber => "climbing",
        Adaptation.Glider => "cliff-gliding",
        Adaptation.Burrower => "burrowing",
        Adaptation.SandSwimmer => "sand-swimming",
        Adaptation.Wader => "wading",
        Adaptation.Armoured => "armoured",
        _ => "",
    };

    /// <summary>
    /// A species for a spot with this landform blend: first a landform, by how much it holds
    /// there, then one of the species at home in it (each by how strongly its homes hold). Deep
    /// in a region that is always one of the region's own; near a border, either side's. Species
    /// with no homes (rolled without landforms) live everywhere. Null if none can live here.
    /// </summary>
    public static Species PickFor(PlanetFauna fauna, LandformBlend blend, Rng rng, Func<Species, bool> allow = null)
    {
        // A landform's last few percent at a border's far edge is not enough to live on.
        float Weight(Species s)
        {
            float w = s.HomeWeight(blend);
            return w < MinHome ? 0f : w;
        }
        float total = 0f;
        foreach (Species s in fauna.Species)
            if (allow == null || allow(s)) total += Weight(s);
        if (total <= 0f) return null;
        float r = rng.Float() * total;
        Species last = null;
        foreach (Species s in fauna.Species)
        {
            if (allow != null && !allow(s)) continue;
            float w = Weight(s);
            if (w <= 0f) continue;
            last = s;
            r -= w;
            if (r <= 0f) return s;
        }
        return last;
    }

    /// <summary>How much a species' home must hold at a spot for it to spawn there.</summary>
    public const float MinHome = 0.05f;

    /// <summary>The species at home in a landform (homes include it), or everywhere.</summary>
    public static List<Species> At(PlanetFauna fauna, Landform l)
    {
        var list = new List<Species>();
        foreach (Species s in fauna.Species)
            if (s.Homes.Length == 0 || Array.IndexOf(s.Homes, l) >= 0) list.Add(s);
        return list;
    }

    /// <summary>Distinct landforms in order, each once.</summary>
    internal static List<Landform> Distinct(IReadOnlyList<Landform> landforms)
    {
        var list = new List<Landform>();
        foreach (Landform l in landforms) if (!list.Contains(l)) list.Add(l);
        return list;
    }

    internal static LandformHabitat HabitatOf(Landform l) => LandformInfo.Habitat(l);
}
