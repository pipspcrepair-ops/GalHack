using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>The fauna of one planet.</summary>
public sealed class PlanetFauna
{
    public ulong Seed;
    public float Alienness;
    public bool Bloom;
    public string PlanetName = "";
    public List<Species> Species = new();
}

/// <summary>
/// Rolls a planet's wildlife from its seed, how alien it is (0..1) and whether the Hollow Bloom
/// has taken it. Pure C#: no nodes, no engine calls, no clock, so the same inputs give the same
/// species on every machine, in tests and in the game alike.
///
/// Alienness pulls every roll (form, parts, pattern, colours, proportions) towards options of
/// matching weirdness, so temperate worlds get near-familiar animals and alien worlds get
/// bizarre ones, while either can still throw up the odd surprise. A Bloom world rolls the
/// same local fauna first and then corrupts it, so a Hollow strider is recognisably the
/// strider from the clean world next door.
/// </summary>
public sealed class FaunaGenerator
{
    readonly FaunaTables _t;

    public FaunaGenerator(FaunaTables tables) => _t = tables;

    public PlanetFauna Generate(ulong planetSeed, float alienness, bool bloom)
    {
        alienness = Math.Clamp(alienness, 0f, 1f);
        var rng = Rng.For(planetSeed, "fauna");
        var names = new NameGen(planetSeed, _t.Syllables);
        var fauna = new PlanetFauna { Seed = planetSeed, Alienness = alienness, Bloom = bloom };
        fauna.PlanetName = names.Make(Rng.For(planetSeed, "planet-name")) + " " + Roman(Rng.For(planetSeed, "planet-number").Int(2, 9));

        int count = rng.Int(_t.SpeciesCount);
        List<(FormDef form, Behaviour? role)> picks = PickForms(rng, count, alienness);

        var takenNames = new HashSet<string>();
        var signatures = new HashSet<string>();
        for (int i = 0; i < picks.Count; i++)
        {
            var srng = Rng.For(planetSeed, "species", i);
            Species s = Roll(srng, picks[i].form, picks[i].role, alienness, signatures);
            s.Index = i;
            s.Seed = Rng.Mix(planetSeed, (ulong)i);
            s.Id = $"{planetSeed:x}-{i}";
            s.Name = names.MakeUnique(srng, takenNames);
            s.CommonName = CommonName(s);
            if (bloom) Corrupt(Rng.For(planetSeed, "bloom", i), s);
            s.Weirdness = Score(s, picks[i].form);
            fauna.Species.Add(s);
        }
        return fauna;
    }

    // ---- Forms ------------------------------------------------------------------------

    /// <summary>
    /// The first picks never repeat a body plan, so every planet has range. The first is always
    /// something that can herd and the second something that can hunt: a planet needs both for
    /// the plains to feel alive.
    /// </summary>
    List<(FormDef, Behaviour?)> PickForms(Rng rng, int count, float alienness)
    {
        var picks = new List<(FormDef, Behaviour?)>();
        var usedPlans = new HashSet<BodyPlan>();
        var formUses = new Dictionary<string, int>();

        for (int i = 0; i < count; i++)
        {
            Behaviour? role = i == 0 ? Behaviour.Herd : i == 1 ? Behaviour.Predator : null;
            bool distinct = i < _t.DistinctPlans;
            var options = _t.Forms.FindAll(f =>
                (!distinct || !usedPlans.Contains(f.Plan))
                && (role == null || f.Behaviours.ContainsKey(role.Value.ToString()))
                && formUses.GetValueOrDefault(f.Id) < MaxUses(f));
            if (options.Count == 0) options = _t.Forms.FindAll(f => formUses.GetValueOrDefault(f.Id) < MaxUses(f));
            FormDef form = rng.Weighted(options, f => f.Weight * Affinity(f.Weirdness, alienness));
            usedPlans.Add(form.Plan);
            formUses[form.Id] = formUses.GetValueOrDefault(form.Id) + 1;
            picks.Add((form, role));
        }
        return picks;
    }

    /// <summary>
    /// A form can appear twice on a planet, unless most of its parts are fixed (a living
    /// boulder is always a boulder): two of those would be the same build in different colours.
    /// </summary>
    static int MaxUses(FormDef f) => f.FixedParts.Count > 2 ? 1 : 2;

    /// <summary>How well an option of this weirdness suits a world of this alienness (a bell curve).</summary>
    float Affinity(float weirdness, float alienness)
    {
        float d = weirdness - alienness;
        return MathF.Exp(-d * d / (2f * _t.WeirdnessFocus * _t.WeirdnessFocus)) + 0.01f;
    }

    // ---- One species ------------------------------------------------------------------

    Species Roll(Rng rng, FormDef form, Behaviour? role, float alienness, HashSet<string> signatures)
    {
        var s = new Species
        {
            Plan = form.Plan,
            Form = form.Id,
            Rig = form.Rig,
            Burrows = form.Burrows,
            Flies = form.Flies,
            Tumbles = form.Tumbles,
            Rooted = form.Rooted,
        };
        RollBody(rng, form, alienness, s.Body);

        // Parts, rerolled until no other species on this planet is built the same way.
        bool unique = false;
        for (int attempt = 0; attempt < 40 && !unique; attempt++)
        {
            RollParts(rng, form, alienness, s);
            unique = signatures.Add(s.PartSignature);
            if (!unique && attempt % 8 == 7) RollBody(rng, form, alienness, s.Body); // leg counts are part of the build too
        }
        if (!unique) ForceUnique(form, s, signatures);

        RollPalette(rng, form, alienness, s);
        RollBehaviour(rng, form, role, alienness, s);
        RollStats(rng, form, s);
        RollDrops(s);
        return s;
    }

    void RollBody(Rng rng, FormDef form, float alienness, Proportions b)
    {
        float extremity = 0f;
        int rolls = 0;
        // Alien worlds push proportions to the ends of each range and a little beyond.
        float Prop(float[] range)
        {
            float t = rng.Extreme(alienness);
            extremity += MathF.Abs(t - 0.5f) * 2f;
            rolls++;
            float span = range[1] - range[0];
            float lo = range[0] - span * 0.3f * alienness, hi = range[1] + span * 0.6f * alienness;
            return MathF.Max(range[0] * 0.6f, lo + (hi - lo) * t);
        }

        b.Size = rng.Range(form.Size);
        b.LegStyle = form.LegStyles[rng.Int(0, form.LegStyles.Length - 1)];
        b.Width = b.Size * Prop(form.Width);
        b.Height = b.Size * Prop(form.Height);
        b.LegLength = b.Size * Prop(form.LegLength);
        b.NeckLength = b.Size * Prop(form.Neck);
        b.HeadSize = b.Size * Prop(form.HeadSize);
        b.TailLength = b.Size * Prop(form.Tail);
        if (form.Neck[1] <= 0f) b.NeckLength = 0f;
        if (form.Tail[1] <= 0f) b.TailLength = 0f;

        b.LegThickness = b.LegStyle switch
        {
            LegStyle.Stilt => b.Width * 0.1f,
            LegStyle.Splayed => b.Width * 0.11f,
            LegStyle.Roots => b.Width * 0.09f,
            _ => b.Width * 0.2f,
        };
        if (b.LegStyle == LegStyle.Stilt) b.LegLength *= 1.35f;

        b.Legs = rng.Int(form.Legs);
        // Odd leg counts are for strange worlds and strange forms.
        if (form.Rig == RigKind.Legged && b.Legs % 2 == 1 && form.Weirdness < 0.7f && !rng.Chance(alienness * 0.5f))
            b.Legs = Math.Min(b.Legs + 1, Math.Max(2, form.Legs[1] - form.Legs[1] % 2));
        if (b.Legs % 2 == 1) extremity += 0.6f;

        b.Segments = rng.Int(form.Segments);
        b.Wings = rng.Int(form.Wings);
        b.Bags = rng.Int(form.Bags);
        b.Members = rng.Int(form.Members);
        b.Extremity = rolls > 0 ? Math.Clamp(extremity / rolls, 0f, 1f) : 0f;
    }

    void RollParts(Rng rng, FormDef form, float alienness, Species s)
    {
        foreach (string slot in FaunaTables.Slots)
        {
            if (form.FixedParts.TryGetValue(slot, out string fixedId))
            {
                s.Parts[slot] = fixedId;
                continue;
            }
            var options = _t.Parts[slot].FindAll(p => p.Fits(form.Rig));
            if (options.Count == 0)
            {
                s.Parts[slot] = "none";
                continue;
            }
            // Extra limbs and tendrils stay rare even on alien worlds, or everything is a hedge of feelers.
            float noneFloor = slot is "limbs" or "tendrils" ? 0.45f : 0.12f;
            PartDef part = rng.Weighted(options, p =>
                p.Id == "none" ? MathF.Max(Affinity(0f, alienness), noneFloor) : Affinity(p.Weirdness, alienness));
            s.Parts[slot] = part.Id;
        }
    }

    /// <summary>Last resort when rolling kept colliding: walk the free slots for any part that makes it new.</summary>
    void ForceUnique(FormDef form, Species s, HashSet<string> signatures)
    {
        foreach (string slot in FaunaTables.Slots)
        {
            if (form.FixedParts.ContainsKey(slot)) continue;
            string original = s.Part(slot);
            foreach (PartDef part in _t.Parts[slot])
            {
                if (!part.Fits(form.Rig)) continue;
                s.Parts[slot] = part.Id;
                if (signatures.Add(s.PartSignature)) return;
            }
            s.Parts[slot] = original;
        }
        signatures.Add(s.PartSignature);
    }

    void RollPalette(Rng rng, FormDef form, float alienness, Species s)
    {
        PaletteRules p = _t.Palette;
        Palette c = s.Colors;
        float a = Math.Clamp(alienness + rng.Range(-0.2f, 0.2f), 0f, 1f);

        bool alienHue = rng.Chance(a);
        float[] familiar = p.FamiliarHues[rng.Int(0, p.FamiliarHues.Length - 1)];
        float hue = (alienHue ? rng.Range(0f, 360f) : rng.Range(familiar)) / 360f;
        float sat = Mathf.Lerp(rng.Range(p.FamiliarSaturation), rng.Range(p.AlienSaturation), a);
        float val = Mathf.Lerp(rng.Range(p.FamiliarValue), rng.Range(p.AlienValue), a);
        c.Base = Color.FromHsv(hue, sat, val);

        // The accent drives stripes, spots and checks: a 1970s paperback wants it bold. Temperate
        // accents stay near the base hue; alien ones jump to a complement or a triad.
        float[] jumps = { 0.5f, 0.33f, -0.33f, 0.25f, 0.42f };
        float shift = Mathf.Lerp(rng.Range(-0.08f, 0.08f), jumps[rng.Int(0, jumps.Length - 1)], a);
        float accentVal = val > 0.5f ? val * rng.Range(0.3f, 0.5f) : Math.Min(1f, val * rng.Range(1.6f, 2.1f));
        c.Accent = Color.FromHsv(Wrap(hue + shift), Math.Min(1f, sat + 0.15f), accentVal);
        c.Belly = Color.FromHsv(hue, sat * 0.45f, Math.Min(1f, val * 1.35f + 0.1f));

        float glowChance = Mathf.Lerp(p.GlowChance[0], p.GlowChance[1], alienness);
        bool glowPart = HasGlowPart(s);
        c.GlowStrength = glowPart || rng.Chance(glowChance) ? rng.Range(0.5f, 1f) * (0.55f + 0.45f * alienness) : 0f;
        if (glowPart) c.GlowStrength = MathF.Max(c.GlowStrength, 0.6f);
        c.Glow = Color.FromHtml(p.GlowColors[rng.Int(0, p.GlowColors.Length - 1)]);

        var patterns = _t.Patterns.FindAll(x => x.Id != Pattern.Veins || alienness > 0.6f);
        c.Pattern = rng.Weighted(patterns, x => Affinity(x.Weirdness, alienness * 0.9f)).Id;
        c.PatternScale = rng.Range(0.7f, 1.5f);
    }

    bool HasGlowPart(Species s)
    {
        foreach (string slot in FaunaTables.Slots)
            if (_t.FindPart(slot, s.Part(slot)) is { Glow: true }) return true;
        return false;
    }

    void RollBehaviour(Rng rng, FormDef form, Behaviour? role, float alienness, Species s)
    {
        float surprise = Mathf.Lerp(_t.Surprise[0], _t.Surprise[1], alienness);
        if (form.Plan == BodyPlan.Swarm) s.Behaviour = Behaviour.Swarm;
        else if (role != null) s.Behaviour = role.Value;
        else if (rng.Chance(surprise))
        {
            // Off script: a herd of drifters, a territorial hopper, a scavenging giant.
            var any = new List<Behaviour>(Enum.GetValues<Behaviour>());
            any.Remove(Behaviour.Swarm);
            s.Behaviour = rng.Pick(any);
        }
        else
        {
            var keys = new List<string>(form.Behaviours.Keys);
            keys.Sort(StringComparer.Ordinal); // dictionary order is not part of the data
            s.Behaviour = Enum.Parse<Behaviour>(rng.Weighted(keys, k => form.Behaviours[k]));
        }

        BehaviourDef def = _t.Behaviour(s.Behaviour);
        if (rng.Chance(surprise)) s.Temperament = (Temperament)rng.Int(0, 3); // a skittish giant, a herd that hunts
        else
        {
            var keys = new List<string>(def.Temperaments.Keys);
            keys.Sort(StringComparer.Ordinal);
            s.Temperament = keys.Count == 0 ? Temperament.Calm : Enum.Parse<Temperament>(rng.Weighted(keys, k => def.Temperaments[k]));
        }
        s.GroupMin = def.Group[0];
        s.GroupMax = def.Group.Length > 1 ? def.Group[1] : def.Group[0];
    }

    void RollStats(Rng rng, FormDef form, Species s)
    {
        Proportions b = s.Body;
        float size = b.Size;
        BehaviourDef def = _t.Behaviour(s.Behaviour);
        TemperamentDef temper = _t.Temperament(s.Temperament);
        Stats st = s.Stats;

        (float health, float walk, float run) = s.Rig switch
        {
            RigKind.Legged => (22f * MathF.Pow(size, 1.5f), Math.Clamp(b.LegLength * 1.5f, 0.6f, 3f), 0f),
            RigKind.Chain => (18f * MathF.Pow(size, 1.3f), Math.Clamp(0.8f + size * 0.3f, 0.8f, 2.4f), 0f),
            RigKind.Hopper => (16f * MathF.Pow(size, 1.5f), 1.6f + size * 0.6f, 0f),
            RigKind.Winged => (14f * MathF.Pow(size, 1.4f), 4f, 9f),
            RigKind.GasBag => (20f * size, 1.0f, 1.8f),
            _ => (Math.Max(20f, b.Members * 1.2f), 2.6f, 6.5f),
        };
        if (run <= 0f) run = Math.Clamp(walk * 2.5f, walk, 8.5f);
        st.MaxHealth = MathF.Round(health * form.HealthScale);
        st.WalkSpeed = walk * form.SpeedScale;
        st.RunSpeed = run * def.Speed * form.SpeedScale;
        float sizeScale = s.Rig == RigKind.Swarm ? 1f : Math.Clamp(MathF.Sqrt(size / 1.5f), 0.6f, 1.6f);
        st.Damage = MathF.Round(def.Damage * sizeScale);
        if (st.Damage <= 0f && temper.Retaliates) st.Damage = 5f; // a harmless thing that bites back when cornered
        st.Perception = def.Perception * rng.Range(0.85f, 1.15f) * (s.Plan == BodyPlan.Giant ? 1.3f : 1f);
        st.FleeRadius = temper.Flee;
        st.Retaliates = temper.Retaliates;
        st.Hunts = temper.Hunts;
        st.Territory = def.Territory * Math.Clamp(size / 6f, 0.7f, 1.5f);
        st.AmbushRadius = def.AmbushRadius;
        st.Telegraph = def.Telegraph;
        st.Radius = s.Rig switch
        {
            RigKind.Chain => Math.Max(b.Width, 0.15f) * 1.2f,
            RigKind.GasBag => b.Width * 0.6f,
            RigKind.Swarm => 1.5f,
            _ => Math.Max(b.Width, b.Size * 0.5f) * 0.7f,
        };
        st.AttackRange = st.Radius + 0.9f + (s.Rig == RigKind.Legged ? b.NeckLength * 0.4f : 0f);
    }

    void RollDrops(Species s)
    {
        float size = s.Body.Size;
        s.Drops.Meat = s.Rig == RigKind.Swarm ? 4 : Math.Clamp((int)MathF.Round(2f + 3f * MathF.Pow(size, 1.5f)), 1, 90);
        if (s.Form == "walkingplant" || s.Form == "boulder") s.Drops.Meat = Math.Max(1, s.Drops.Meat / 3); // mostly not meat
        RareDef best = null;
        foreach (string slot in FaunaTables.Slots)
            if (_t.FindPart(slot, s.Part(slot))?.Rare is { } rare && (best == null || rare.Value > best.Value)) best = rare;
        if (best == null && s.Colors.GlowStrength > 0.7f) best = new RareDef { Name = "Glow gland", Chance = 0.2f, Value = 80 };
        if (best != null)
        {
            s.Drops.RarePart = best.Name;
            s.Drops.RareChance = best.Chance;
            s.Drops.RareValue = best.Value;
        }
    }

    // ---- The Hollow Bloom -------------------------------------------------------------

    /// <summary>
    /// Corrupts a local species: same body underneath, now dark, veined and glowing pink, with
    /// tendrils where it had none, angrier and harder to kill.
    /// </summary>
    void Corrupt(Rng rng, Species s)
    {
        CorruptionRules c = _t.Corruption;
        s.Corrupted = true;
        s.Id += "-hollow";
        s.Name = $"{c.Prefix} {s.Name}";
        s.CommonName = $"hollow {s.CommonName}";

        Color bloomBase = Color.FromHtml(c.Base);
        s.Colors.Base = s.Colors.Base.Lerp(bloomBase, c.Mix);
        s.Colors.Belly = s.Colors.Belly.Lerp(bloomBase, c.Mix * 0.8f);
        s.Colors.Accent = Color.FromHtml(c.Veins);
        s.Colors.Glow = Color.FromHtml(c.Glow);
        s.Colors.GlowStrength = Math.Max(s.Colors.GlowStrength, rng.Range(0.8f, 1f));
        s.Colors.Pattern = Pattern.Veins;

        if (!s.Has("tendrils"))
        {
            string tendrils = s.Rig switch
            {
                RigKind.Legged or RigKind.Hopper => "back",
                RigKind.Chain => "chin",
                RigKind.Winged => "whiskers",
                RigKind.GasBag => "curtain",
                _ => "none",
            };
            s.Parts["tendrils"] = tendrils;
        }

        s.Temperament = Temperament.Aggressive;
        if (s.Behaviour == Behaviour.HarmlessOddity && s.Rig != RigKind.GasBag) s.Behaviour = Behaviour.Ambusher;
        TemperamentDef angry = _t.Temperament(Temperament.Aggressive);
        s.Stats.FleeRadius = angry.Flee;
        s.Stats.Retaliates = true;
        s.Stats.Hunts = true;
        s.Stats.MaxHealth = MathF.Round(s.Stats.MaxHealth * c.HealthScale);
        s.Stats.Damage = MathF.Round(Math.Max(8f, s.Stats.Damage * c.DamageScale));
        if (s.Rig == RigKind.Swarm) s.Stats.Damage = MathF.Round(_t.Behaviour(Behaviour.Swarm).Damage * c.DamageScale);
        s.Stats.WalkSpeed *= c.SpeedScale;
        s.Stats.RunSpeed *= c.SpeedScale;

        if (c.Rare.Chance * c.Rare.Value > s.Drops.RareChance * s.Drops.RareValue)
        {
            s.Drops.RarePart = c.Rare.Name;
            s.Drops.RareChance = c.Rare.Chance;
            s.Drops.RareValue = c.Rare.Value;
        }
    }

    // ---- Weirdness and names ----------------------------------------------------------

    /// <summary>0 for an Earth-like animal, 1 for the unnameable. Corruption adds on top.</summary>
    float Score(Species s, FormDef form)
    {
        WeirdnessWeights w = _t.Weirdness;
        float parts = 0f;
        foreach (string slot in FaunaTables.Slots) parts += _t.FindPart(slot, s.Part(slot))?.Weirdness ?? 0f;
        parts /= FaunaTables.Slots.Length;

        Palette c = s.Colors;
        c.Base.ToHsv(out _, out float sat, out _);
        float colour = Math.Clamp(Math.Max(0f, sat - 0.5f) * 1.2f + c.GlowStrength * 0.6f, 0f, 1f);
        float pattern = _t.Patterns.Find(p => p.Id == c.Pattern)?.Weirdness ?? 0f;

        float score = w.Form * form.Weirdness + w.Parts * parts * 1.6f + w.Proportions * s.Body.Extremity
                      + w.Colour * colour + w.Pattern * pattern;
        if (s.Corrupted) score += _t.Corruption.WeirdnessBonus;
        return Math.Clamp(score, 0f, 1f);
    }

    /// <summary>"crowned strider": the most striking part plus what kind of thing it is.</summary>
    string CommonName(Species s)
    {
        string noun = _t.Forms.Find(f => f.Id == s.Form)?.Noun ?? "creature";
        PartDef best = null;
        foreach (string slot in FaunaTables.Slots)
        {
            PartDef part = _t.FindPart(slot, s.Part(slot));
            if (part == null || part.Adjective == "" || s.Form is "walkingplant" or "boulder" or "crystalback") continue;
            if (best == null || part.Weirdness > best.Weirdness) best = part;
        }
        string adjective = best?.Adjective ?? s.Colors.Pattern switch
        {
            Pattern.Stripes => "striped",
            Pattern.Spots => "spotted",
            Pattern.Bands => "banded",
            Pattern.Rings => "ringed",
            Pattern.Checker => "chequered",
            Pattern.Veins => "veined",
            _ => s.Colors.GlowStrength > 0.4f ? "glowing" : s.Body.LegStyle == LegStyle.Stilt ? "stilt-legged" : "common",
        };
        if (s.Body.Legs % 2 == 1 && s.Rig == RigKind.Legged && s.Form != "tripod") adjective = $"{s.Body.Legs}-legged";
        return $"{adjective} {noun}";
    }

    static float Wrap(float h) => h - MathF.Floor(h);

    static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => "IX",
    };
}
