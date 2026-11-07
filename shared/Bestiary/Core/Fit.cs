using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>What a climate does to bodies: data/wildlife.json, "fit".</summary>
public sealed class FitTables
{
    /// <summary>Climate band to its rules.</summary>
    public Dictionary<string, ClimateFit> Climates { get; set; } = new();
    /// <summary>How much rarer (a share of the usual groups) something that has beaten vacuum is.</summary>
    public float VacuumScarcity { get; set; } = 0.12f;
    /// <summary>The chance a vacuum world has anything alive at all.</summary>
    public float VacuumLife { get; set; } = 0.12f;
    /// <summary>The chance an ordinary world has machines walking it.</summary>
    public float MachineWorld { get; set; } = 0.22f;
}

public sealed class ClimateFit
{
    /// <summary>Bodies that can't live here: cold-blooded things on ice, for one.</summary>
    public RigKind[] BanRigs { get; set; } = Array.Empty<RigKind>();
    public string[] BanForms { get; set; } = Array.Empty<string>();
    /// <summary>Coat to how often it's what a body here wears.</summary>
    public Dictionary<string, float> Coats { get; set; } = new();
    /// <summary>Girth (cold thickens a body) and limbs, neck and tail (cold shortens them, heat draws them out).</summary>
    public float Bulk { get; set; } = 1f;
    public float Limbs { get; set; } = 1f;
    /// <summary>Ears as radiators: a hot world's bodies shed heat through big thin ears, 0 to 1.</summary>
    public float Ears { get; set; }
    /// <summary>How far coats go to snow pale, or to sand.</summary>
    public float Pale { get; set; }
    public float Tan { get; set; }
}

/// <summary>
/// Bodies fit the world they live on. Nothing here names a creature or a world: cold bans the
/// cold-blooded and puts wool, fur and fat on what's left, thick-set and short in the limb; heat
/// gives scale and thin skin, long limbs and big ears; a dark place takes the eyes and gives ears
/// for them; vacuum takes everything that breathes. The rest is what those rules leave.
/// </summary>
public static class Fit
{
    static readonly string[] Arthropods = { "skitterer", "centipede", "cloud", "pack", "mothwing", "creeper", "scrapper" };

    static ClimateFit Rules(FitTables t, WorldConditions w) => t.Climates.TryGetValue(w.Climate.ToString(), out ClimateFit f) ? f : new ClimateFit();

    /// <summary>Whether a form can live on this world at all.</summary>
    public static bool Allows(FitTables t, WorldConditions w, FormDef form)
    {
        ClimateFit c = Rules(t, w);
        return Array.IndexOf(c.BanRigs, form.Rig) < 0 && Array.IndexOf(c.BanForms, form.Id) < 0;
    }

    /// <summary>Works a rolled species over for its world: its coat, its build, its colours, its senses.</summary>
    public static void Apply(FitTables t, WorldConditions w, Species s, Rng rng)
    {
        ClimateFit c = Rules(t, w);
        s.Coat = PickCoat(c, s, rng);
        Proportions b = s.Body;
        if (s.Rig is not (RigKind.Swarm or RigKind.GasBag))
        {
            float thick = s.Coat == Coat.Blubber ? c.Bulk * 1.14f : c.Bulk;
            b.Width *= thick;
            b.Height *= thick;
            if (s.Flight == FlightKind.None)
            {
                b.LegLength *= c.Limbs;
                b.LegThickness *= c.Bulk;
            }
            b.TailLength *= c.Limbs;
            b.NeckLength *= MathF.Sqrt(c.Limbs);
        }
        s.Stats.MaxHealth = MathF.Round(s.Stats.MaxHealth * (s.Coat == Coat.Blubber ? 1.25f : c.Bulk));
        if (s.Coat == Coat.Blubber) s.Stats.RunSpeed *= 0.85f;

        // Snow and sand: a coat goes the colour of what it hides against.
        var snow = new Color(0.86f, 0.87f, 0.9f);
        var sand = new Color(0.72f, 0.56f, 0.36f);
        s.Colors.Base = s.Colors.Base.Lerp(snow, c.Pale * rng.Range(0.7f, 1f)).Lerp(sand, c.Tan * rng.Range(0.5f, 1f));
        s.Colors.Belly = s.Colors.Belly.Lerp(snow, c.Pale).Lerp(sand.Lightened(0.25f), c.Tan);
        s.Colors.Accent = s.Colors.Accent.Lerp(snow.Darkened(0.5f), c.Pale * 0.6f);
        if (s.Coat is Coat.Fur or Coat.Wool or Coat.Blubber) s.Colors.Pattern = rng.Chance(0.5f) ? Pattern.Plain : Pattern.Countershade;
        if (s.Coat == Coat.Scale && s.Colors.Pattern == Pattern.Plain) s.Colors.Pattern = rng.Chance(0.5f) ? Pattern.Bands : Pattern.Mottle;
        s.EarSize = MathF.Max(s.EarSize, c.Ears * rng.Range(0.6f, 1f));
        Senses(s, rng);
        if (s.Coat is Coat.Fur or Coat.Wool) s.CommonName = $"{(s.Coat == Coat.Wool ? "woolly" : "shaggy")} {s.CommonName}";
        else if (s.Coat == Coat.Blubber) s.CommonName = $"fat-layered {s.CommonName}";
        else if (s.Coat == Coat.Scale) s.CommonName = $"scaled {s.CommonName}";
    }

    static Coat PickCoat(ClimateFit c, Species s, Rng rng)
    {
        if (Array.IndexOf(Arthropods, s.Form) >= 0 || s.Rig == RigKind.Swarm) return Coat.Chitin;
        if (s.Form is "boulder" or "crystalback") return Coat.Shell;
        if (s.Rig == RigKind.GasBag) return Coat.ThinSkin;
        if (s.Form == "walkingplant") return Coat.Hide;
        var options = new List<(Coat coat, float weight)>();
        foreach (var (name, weight) in c.Coats)
        {
            if (!Enum.TryParse(name, out Coat coat)) continue;
            // Fliers can't carry fat or a fleece.
            if (s.Flies && coat is Coat.Blubber or Coat.Wool) continue;
            options.Add((coat, weight));
        }
        return options.Count == 0 ? Coat.Hide : rng.Weighted(options, o => o.weight).coat;
    }

    /// <summary>
    /// A species' senses, from its hours, its job and its body. Night makes eyes for the dark;
    /// where eyes have gone hearing takes over, and some start to click; what hunts on the ground
    /// has a nose; prey has ears. Big ears are what good hearing looks like.
    /// </summary>
    public static void Senses(Species s, Rng rng)
    {
        var p = new SenseProfile();
        p.DarkVision = s.Activity switch { Activity.Nocturnal => rng.Range(0.7f, 1f), Activity.Crepuscular => rng.Range(0.35f, 0.65f), Activity.Cathemeral => rng.Range(0.2f, 0.5f), _ => rng.Range(0f, 0.2f) };
        p.Hearing = s.IsHunter ? rng.Range(0.9f, 1.2f) : rng.Range(1.1f, 1.45f);
        p.Scent = s.Rig switch
        {
            RigKind.GasBag or RigKind.Swarm => 0f,
            RigKind.Winged => s.IsHunter ? 0.4f : 0.1f,
            _ => s.IsHunter ? rng.Range(0.8f, 1.5f) : rng.Range(0.3f, 0.7f),
        };
        if (s.Activity == Activity.Nocturnal) { p.Hearing *= 1.15f; p.Scent *= 1.2f; }
        // What lives by night on the wing finds its way by its voice.
        if (s.Flies && s.Rig == RigKind.Winged && s.Activity == Activity.Nocturnal && rng.Chance(0.5f)) p.Echo = true;
        if (s.IsDeep) p.DarkVision = MathF.Max(p.DarkVision, 0.75f);
        if (s.Blind)
        {
            p.Sight = 0f;
            p.DarkVision = 0f;
            p.Hearing = rng.Range(1.5f, 2f);
            p.Scent = MathF.Max(p.Scent, 0.8f);
            p.Echo = s.IsHunter || s.Role is DeepRole.Crawler or DeepRole.Prowler || rng.Chance(0.5f);
        }
        s.Senses = p;
        s.EarSize = MathF.Max(s.EarSize, Math.Clamp((p.Hearing - 1.15f) * 1.1f, 0f, 1f));
        if (p.Echo) s.EarSize = MathF.Max(s.EarSize, 0.9f);
        // Eyes built for the dark catch a light.
        if (p.DarkVision > 0.6f && s.Colors.GlowStrength < 0.16f && !s.Blind && !s.IsDeep)
        {
            s.Colors.GlowStrength = 0.16f;
            s.Colors.Glow = s.Colors.Glow.A > 0f && s.Colors.Glow.Luminance > 0.1f ? s.Colors.Glow : new Color(0.85f, 0.7f, 0.3f);
        }
    }

    /// <summary>
    /// Something that has beaten vacuum: sealed in a shell, feeling the ground, the colour of the
    /// rock it lives on. There's almost nothing like it.
    /// </summary>
    public static void Harden(FitTables t, Species s, Rng rng)
    {
        s.Coat = Coat.Shell;
        s.Hardened = true;
        s.Scarcity = t.VacuumScarcity;
        s.Blind = true;
        s.GroupMin = s.GroupMax = 1;
        s.Colors.Base = s.Colors.Base.Lerp(new Color(0.27f, 0.25f, 0.24f), 0.85f);
        s.Colors.Belly = s.Colors.Base.Lightened(0.08f);
        s.Colors.Pattern = Pattern.Plain;
        s.Colors.GlowStrength = 0f;
        s.Stats.WalkSpeed *= 0.6f;
        s.Stats.RunSpeed *= 0.6f;
        s.Stats.MaxHealth *= 1.8f;
        s.CommonName = $"sealed {s.CommonName}";
        s.Senses = new SenseProfile { Sight = 0f, Hearing = 1.4f, Scent = 0f };
        s.EarSize = 0f;
    }

    /// <summary>
    /// Some throats throw: a mutated one brings up blood or bile, more often the worse it is, and
    /// a big-mouthed hunter may spit bile by nature. Same attack either way.
    /// </summary>
    public static void Throat(Species s, Rng rng)
    {
        if (s.Mechanical || s.Rig is RigKind.Swarm or RigKind.GasBag || s.Flies) return;
        float k = s.Mutation;
        bool mouth = s.IsHunter && s.Part("head") is "maw" or "jaws" or "lantern";
        if (k > 0.3f && rng.Chance(k * 0.8f)) s.Spit = rng.Chance(0.5f) ? SpitKind.Blood : SpitKind.Bile;
        else if (mouth && k <= 0.3f && rng.Chance(0.12f)) s.Spit = SpitKind.Bile;
        if (s.Spit != SpitKind.None) s.Attack = s.Attack.With(AttackStyle.Lunge, "Retch");
        if (s.Spit != SpitKind.None) s.Attack.Sound = "retch";
    }

    /// <summary>
    /// How much of a sex shows on a species: nothing, or one sex bigger, heavier-headed or marked.
    /// Both are the same species; <see cref="Species.Mate"/> is the other half.
    /// </summary>
    public static void Dimorph(Species s, Rng rng)
    {
        if (s.Rig != RigKind.Legged && s.Rig != RigKind.Hopper || s.Domestic != DomesticKind.None || !rng.Chance(0.55f)) return;
        Species m = s.Copy();
        float k = rng.Range(0.08f, 0.26f);
        Proportions b = m.Body;
        b.Size *= 1f + k; b.Width *= 1f + k; b.Height *= 1f + k; b.LegLength *= 1f + k * 0.6f; b.LegThickness *= 1f + k; b.HeadSize *= 1f + k * 0.8f; b.NeckLength *= 1f + k * 0.6f; b.TailLength *= 1f + k * 0.6f;
        m.Stats.MaxHealth = MathF.Round(m.Stats.MaxHealth * (1f + k * 1.5f));
        m.Stats.Damage *= 1f + k;
        m.Stats.Radius *= 1f + k * 0.8f;
        // One sex carries the colour, or the horns, or both.
        if (rng.Chance(0.6f)) m.Colors.Accent = m.Colors.Accent.Lightened(0.15f).Lerp(m.Colors.Glow.Luminance > 0.1f ? m.Colors.Glow : m.Colors.Accent, 0.3f);
        if (rng.Chance(0.5f)) m.Colors.Base = m.Colors.Base.Darkened(rng.Range(0.1f, 0.25f));
        if (s.Part("horns") != "none" && rng.Chance(0.6f)) s.Parts["horns"] = "none";
        m.Mate = s;
        s.Mate = m;
    }
}
