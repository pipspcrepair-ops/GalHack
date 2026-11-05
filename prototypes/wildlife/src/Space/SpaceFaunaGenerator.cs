using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>The space fauna of one habitat in one region: a nebula, a belt, a giant's skies, a graveyard, the deep.</summary>
public sealed class SpaceFauna
{
    public ulong Seed;
    public Habitat Habitat;
    public string RegionName = "";
    public List<Species> Species = new();
}

/// <summary>
/// Rolls space fauna, built the way the planet generator builds wildlife: a form gives the body
/// plan, the rig and the job; swappable parts (heads, fins, backs, tails, tendrils) go on by the
/// same combination rules (roles, forms, excludes); then proportions, palette, stats, a signature
/// attack and a drop table. Pure C#, seeded: the same region gives the same creatures everywhere.
///
/// Every form living in a habitat gets one species per region, so a region's nebula has its own
/// drifters, its own leviathan (if one ever shows itself), and its graveyard its own leeches.
/// Creatures of space are cool and menacing, never cute: dark hides lit by their own glow,
/// eyeless domes, lamprey mouths and lures, ribbed wings and trailing stingers.
/// </summary>
public sealed class SpaceFaunaGenerator
{
    readonly SpaceFaunaData _t;
    readonly CombatRules _combat;

    public SpaceFaunaGenerator(SpaceFaunaData tables, CombatRules combat)
    {
        _t = tables;
        _combat = combat;
    }

    public SpaceFaunaData Tables => _t;

    /// <summary>Everything that lives in one habitat of one region.</summary>
    public SpaceFauna Generate(ulong regionSeed, Habitat habitat)
    {
        var fauna = new SpaceFauna { Seed = regionSeed, Habitat = habitat, RegionName = RegionName(regionSeed, habitat) };
        foreach (SpaceFormDef form in _t.Forms)
            if (Array.IndexOf(form.Habitats, habitat) >= 0) fauna.Species.Add(Roll(regionSeed, form.Id, habitat));
        for (int i = 0; i < fauna.Species.Count; i++) fauna.Species[i].Index = i;
        return fauna;
    }

    /// <summary>One form's species in a region: what an encounter card's creature kind turns into there.</summary>
    public Species Roll(ulong regionSeed, string formId, Habitat habitat)
    {
        SpaceFormDef form = _t.Form(formId) ?? throw new ArgumentException($"no space form '{formId}'");
        var rng = Rng.For(regionSeed, "space-species:" + form.Id, (long)habitat);
        var names = new NameGen(Rng.Mix(regionSeed, Rng.Hash("space-names")), _t.Syllables);
        var s = new Species
        {
            Plan = BodyPlan.Voidborne,
            Form = form.Id,
            Rig = form.Rig,
            Swim = form.Swim,
            Habitat = habitat,
            Behaviour = form.Behaviour,
            Flies = true,
            Seed = Rng.Mix(regionSeed, Rng.Hash(form.Id)),
            Id = $"space-{regionSeed:x}-{form.Id}",
            Name = names.Make(rng),
        };
        RollTemperament(rng, form, s);
        RollBody(rng, form, s);
        RollParts(rng, form, s);
        RollPalette(rng, form, s);
        RollStats(form, s);
        s.Drops.Materials.AddRange(form.Drops);
        s.CommonName = CommonName(form, s);
        s.Weirdness = 0.8f;
        return s;
    }

    static string RegionName(ulong seed, Habitat habitat)
    {
        string word = habitat switch
        {
            Habitat.Nebula => "Nebula",
            Habitat.AsteroidField => "Belt",
            Habitat.GasGiant => "upper skies",
            Habitat.Derelict => "Graveyard",
            _ => "Deep",
        };
        var rng = Rng.For(seed, "region-name");
        var names = new NameGen(Rng.Mix(seed, Rng.Hash("space-names")), new SyllableTables());
        return $"the {names.Make(rng)} {word}";
    }

    void RollTemperament(Rng rng, SpaceFormDef form, Species s)
    {
        var keys = new List<string>(form.Temperaments.Keys);
        keys.Sort(StringComparer.Ordinal);
        s.Temperament = keys.Count == 0 ? Temperament.Calm : Enum.Parse<Temperament>(rng.Weighted(keys, k => form.Temperaments[k]));
        s.GroupMin = form.Group[0];
        s.GroupMax = form.Group.Length > 1 ? form.Group[1] : form.Group[0];
    }

    static void RollBody(Rng rng, SpaceFormDef form, Species s)
    {
        Proportions b = s.Body;
        b.Size = rng.Range(form.Size);
        b.Width = b.Size * rng.Range(form.Width);
        b.Height = b.Size * rng.Range(form.Height);
        b.HeadSize = b.Size * rng.Range(form.HeadSize);
        b.TailLength = b.Size * rng.Range(form.Tail);
        b.Segments = rng.Int(form.Segments);
        b.LegThickness = b.Width * 0.1f;
    }

    void RollParts(Rng rng, SpaceFormDef form, Species s)
    {
        s.Parts.Clear();
        foreach (string slot in SpaceFaunaData.Slots)
        {
            if (form.FixedParts.TryGetValue(slot, out string fixedId))
            {
                s.Parts[slot] = fixedId;
                continue;
            }
            var options = _t.Parts[slot].FindAll(p => Allowed(p, slot, form, s));
            s.Parts[slot] = options.Count == 0 ? "none" : rng.Weighted(options, p => p.Id == "none" ? 0.35f : 1f).Id;
        }
    }

    bool Allowed(PartDef p, string slot, SpaceFormDef form, Species s) =>
        !p.FixedOnly && p.Fits(form.Rig) && p.Suits(s.Behaviour) && p.SuitsForm(form.Id) && FaunaQuality.Compatible(_t.FindPart, p, slot, s);

    static void RollPalette(Rng rng, SpaceFormDef form, Species s)
    {
        SpacePalette p = form.Palette;
        Palette c = s.Colors;
        float[] hues = p.Hues[rng.Int(0, p.Hues.Length - 1)];
        float hue = rng.Range(hues) / 360f, sat = rng.Range(p.Saturation), val = rng.Range(p.Value);
        c.Base = Color.FromHsv(Wrap(hue), sat, val);
        // Accents run darker and richer: bands, plates and the leading edges of fins.
        c.Accent = Color.FromHsv(Wrap(hue + rng.Range(-0.06f, 0.06f)), Math.Min(1f, sat + 0.2f), val * rng.Range(0.45f, 0.7f));
        c.Belly = Color.FromHsv(Wrap(hue), sat * 0.6f, Math.Min(1f, val * 1.5f + 0.08f));
        c.Glow = Color.FromHtml(p.Glow[rng.Int(0, p.Glow.Length - 1)]);
        c.GlowStrength = rng.Range(p.GlowStrength);
        c.Pattern = p.Patterns[rng.Int(0, p.Patterns.Length - 1)];
        c.PatternScale = rng.Range(0.7f, 1.4f);
    }

    void RollStats(SpaceFormDef form, Species s)
    {
        Proportions b = s.Body;
        Stats st = s.Stats;
        float t = form.Size[1] > form.Size[0] ? (b.Size - form.Size[0]) / (form.Size[1] - form.Size[0]) : 0.5f;
        st.MaxHealth = MathF.Round(Mathf.Lerp(form.Health[0], form.Health[1], t));
        st.WalkSpeed = form.Speed[0];
        st.RunSpeed = form.Speed[1];
        st.Damage = form.Damage;
        st.Perception = form.Perception;
        st.Territory = form.Territory;
        st.FleeRadius = s.Temperament switch { Temperament.Skittish => 60f, Temperament.Calm => 25f, Temperament.Bold => 8f, _ => 0f } * Math.Clamp(b.Size / 5f, 0.6f, 3f);
        st.Retaliates = s.Temperament is Temperament.Bold or Temperament.Aggressive;
        st.Hunts = s.Temperament == Temperament.Aggressive;
        st.Radius = s.Rig switch
        {
            RigKind.Clinger => Math.Max(b.Width, 0.15f) * 0.6f,
            _ => s.Swim switch
            {
                SwimStyle.Flap => Math.Max(b.Height, b.Size * 0.18f),
                SwimStyle.Undulate => b.Width * 0.55f,
                _ => Math.Max(b.Width, b.Height) * 0.5f,
            },
        };
        st.AttackRange = st.Radius + 0.9f;
        s.Attack = form.Attack;
        st.Telegraph = form.Attack.Telegraph;
    }

    /// <summary>"lantern-jawed leviathan": the most striking part plus what kind of thing it is.</summary>
    string CommonName(SpaceFormDef form, Species s)
    {
        PartDef best = null;
        foreach (string slot in SpaceFaunaData.Slots)
        {
            PartDef p = _t.FindPart(slot, s.Part(slot));
            if (p == null || p.Adjective == "" || form.FixedParts.ContainsKey(slot)) continue;
            if (best == null || p.Weirdness > best.Weirdness) best = p;
        }
        return best != null ? $"{best.Adjective} {form.Noun}" : form.Noun;
    }

    static float Wrap(float h) => h - MathF.Floor(h);
}

/// <summary>The rules that keep space fauna looking like animals, checked over thousands of rolls by the tests.</summary>
public static class SpaceQuality
{
    public static List<string> Problems(SpaceFaunaData t, Species s)
    {
        var problems = new List<string>();
        SpaceFormDef form = t.Form(s.Form);
        if (form == null) return new List<string> { $"unknown form {s.Form}" };
        foreach (string slot in SpaceFaunaData.Slots)
        {
            string id = s.Part(slot);
            PartDef p = t.FindPart(slot, id);
            if (p == null) { problems.Add($"unknown part {slot}/{id}"); continue; }
            if (id == "none" || (form.FixedParts.TryGetValue(slot, out string fixedId) && fixedId == id)) continue;
            if (!p.Fits(s.Rig)) problems.Add($"{slot}/{id} does not fit a {s.Rig} rig");
            if (!p.SuitsForm(s.Form)) problems.Add($"{slot}/{id} does not belong on a {s.Form}");
            if (!p.Suits(s.Behaviour)) problems.Add($"{slot}/{id} is wrong for a {s.Behaviour}");
            if (!FaunaQuality.Compatible(t.FindPart, p, slot, s)) problems.Add($"{slot}/{id} clashes with its other parts");
        }
        if (s.Stats.Damage > 0f && (s.Attack == null || s.Attack.Name == "" || s.Attack.Telegraph < 0.6f)) problems.Add("an attacker without a signature attack");
        if (s.Drops.Materials.Count == 0) problems.Add("drops nothing");
        return problems;
    }
}

/// <summary>What a space creature leaves when it dies: rolled from its drop table, rare and better only.</summary>
public static class SpaceDrops
{
    public readonly record struct Loot(string Material, Rarity Rarity, int Amount);

    public static List<Loot> Roll(Species s, Rng rng)
    {
        var loot = new List<Loot>();
        foreach (MaterialDrop d in s.Drops.Materials)
            if (rng.Chance(d.Chance)) loot.Add(new Loot(d.Material, d.Rarity, rng.Int(d.Amount)));
        return loot;
    }
}
