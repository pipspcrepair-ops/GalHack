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
///
/// Weird is fine; broken or silly is not. Behaviour is rolled before the body, so parts fit
/// the job (hunters get jaws and claws, grazers get horns and hooves); each part's combination
/// rules in the data rule out clashing pairs; and proportions stay inside bands where a body
/// still reads as an animal (<see cref="FaunaQuality"/> checks all of it).
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
        List<(FormDef form, Behaviour? role)> picks = PickForms(rng, Rng.For(planetSeed, "roles"), count, alienness);

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
    /// something that can herd and the second something that hunts, as a pack or alone: a
    /// planet needs both for the plains to feel alive.
    /// </summary>
    List<(FormDef, Behaviour?)> PickForms(Rng rng, Rng roles, int count, float alienness)
    {
        var picks = new List<(FormDef, Behaviour?)>();
        var usedPlans = new HashSet<BodyPlan>();
        var formUses = new Dictionary<string, int>();
        Behaviour hunter = roles.Chance(_t.Pack.RoleChance) ? Behaviour.PackHunter : Behaviour.Predator;

        for (int i = 0; i < count; i++)
        {
            Behaviour? role = i == 0 ? Behaviour.Herd : i == 1 ? hunter : null;
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
        // What it does comes first: the body is then built for the job.
        RollBehaviour(rng, form, role, alienness, s);
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
        RollStats(rng, form, s);
        RollDrops(s);
        return s;
    }

    void RollBody(Rng rng, FormDef form, float alienness, Proportions b)
    {
        float extremity = 0f;
        int rolls = 0;
        // Alien worlds push proportions towards the ends of each range and a little beyond.
        float Prop(float[] range)
        {
            float t = rng.Extreme(alienness);
            extremity += MathF.Abs(t - 0.5f) * 2f;
            rolls++;
            float span = range[1] - range[0];
            float lo = range[0] - span * _t.ProportionPush[0] * alienness, hi = range[1] + span * _t.ProportionPush[1] * alienness;
            return MathF.Max(range[0] * 0.75f, lo + (hi - lo) * t);
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

        b.Legs = rng.Int(form.Legs);
        // Odd leg counts only where the form is built round them (a tripod): a seven-legged
        // walker limps, and reads as broken rather than strange.
        if (b.Legs % 2 == 1 && form.Legs[0] != form.Legs[1]) b.Legs = b.Legs + 1 <= form.Legs[1] ? b.Legs + 1 : b.Legs - 1;
        if (b.LegStyle == LegStyle.Stilt) b.LegLength *= 1.35f;

        b.Segments = rng.Int(form.Segments);
        b.Wings = rng.Int(form.Wings);
        b.Bags = rng.Int(form.Bags);
        b.Members = rng.Int(form.Members);
        b.Extremity = rolls > 0 ? Math.Clamp(extremity / rolls, 0f, 1f) : 0f;
        FaunaQuality.Clamp(form, b);

        b.LegThickness = b.LegStyle switch
        {
            // Stilts carry real weight: thin poles under a giant read as broken, not menacing.
            LegStyle.Stilt => b.Width * (form.Plan == BodyPlan.Giant ? 0.2f : 0.13f),
            LegStyle.Splayed => b.Width * 0.11f,
            LegStyle.Roots => b.Width * 0.09f,
            _ => b.Width * 0.25f,
        };
    }

    void RollParts(Rng rng, FormDef form, float alienness, Species s)
    {
        s.Parts.Clear();
        foreach (string slot in FaunaTables.Slots)
        {
            if (form.FixedParts.TryGetValue(slot, out string fixedId))
            {
                s.Parts[slot] = fixedId;
                continue;
            }
            var options = _t.Parts[slot].FindAll(p => Allowed(p, slot, form, s));
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

    /// <summary>Whether a part may go on this creature, given its rig, its job and the parts it already has.</summary>
    bool Allowed(PartDef p, string slot, FormDef form, Species s) =>
        p.Fits(form.Rig) && p.Suits(s.Behaviour) && p.SuitsForm(form.Id) && (!p.NeedsNeck || s.Body.NeckLength > s.Body.Size * 0.25f) && FaunaQuality.Compatible(_t, p, slot, s);

    /// <summary>Last resort when rolling kept colliding: walk the free slots for any part that makes it new.</summary>
    void ForceUnique(FormDef form, Species s, HashSet<string> signatures)
    {
        foreach (string slot in FaunaTables.Slots)
        {
            if (form.FixedParts.ContainsKey(slot)) continue;
            string original = s.Part(slot);
            foreach (PartDef part in _t.Parts[slot])
            {
                s.Parts[slot] = original;
                if (part.Id == original || !Allowed(part, slot, form, s)) continue;
                s.Parts[slot] = part.Id;
                if (FaunaQuality.Problems(_t, s).Count == 0 && signatures.Add(s.PartSignature)) return;
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

        // Off-palette hues are common on alien worlds and rare on familiar ones.
        bool alienHue = rng.Chance(a * a);
        float[] familiar = p.FamiliarHues[rng.Int(0, p.FamiliarHues.Length - 1)];
        float hue = (alienHue ? rng.Range(0f, 360f) : rng.Range(familiar)) / 360f;
        float sat = Mathf.Lerp(rng.Range(p.FamiliarSaturation), rng.Range(p.AlienSaturation), a);
        float val = Mathf.Lerp(rng.Range(p.FamiliarValue), rng.Range(p.AlienValue), a);
        c.Base = Color.FromHsv(hue, sat, val);

        // The accent drives stripes, spots and plates. Temperate accents stay near the base hue;
        // alien ones jump to a complement or a triad.
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
            // A pack of giants is a wipe, not a surprise, and packs hunt on the ground.
            if (form.Plan == BodyPlan.Giant || form.Rig is not (RigKind.Legged or RigKind.Chain or RigKind.Hopper)) any.Remove(Behaviour.PackHunter);
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
        // A pack exists to hunt: it never comes out skittish.
        if (s.Behaviour == Behaviour.PackHunter && s.Temperament is Temperament.Skittish or Temperament.Calm) s.Temperament = Temperament.Bold;
        s.GroupMin = def.Group[0];
        s.GroupMax = def.Group.Length > 1 ? def.Group[1] : def.Group[0];
    }

    /// <summary>
    /// The species' signature attack: its behaviour's, adapted to the body. Fliers dive, crawlers
    /// coil and strike like a snake, swarms sting, floaters lash with their tendrils.
    /// </summary>
    void AssignAttack(Species s)
    {
        AttackDef a = _t.Behaviour(s.Behaviour).Attack;
        s.Attack = s.Rig switch
        {
            RigKind.Winged => a.With(AttackStyle.Dive, "Dive"),
            RigKind.Swarm => a.With(AttackStyle.Sting, "Sting cloud"),
            RigKind.Chain when a.Style != AttackStyle.Burst => a.With(AttackStyle.Rear, "Coil strike"),
            RigKind.GasBag => a.With(AttackStyle.Lunge, "Tendril lash"),
            RigKind.Hopper when a.Style is AttackStyle.Lunge or AttackStyle.Charge => a.With(AttackStyle.Pounce, "Leap"),
            _ => a,
        };
        s.Stats.Telegraph = s.Attack.Telegraph;
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
        AssignAttack(s);
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
            // The Bloom grows whatever tendrils this body can carry without them clashing.
            string[] wanted = s.Rig switch
            {
                RigKind.Legged or RigKind.Hopper => new[] { "back", "chin", "whiskers" },
                RigKind.Chain => new[] { "chin", "whiskers" },
                RigKind.Winged => new[] { "whiskers" },
                RigKind.GasBag => new[] { "curtain", "dangling" },
                _ => Array.Empty<string>(),
            };
            foreach (string id in wanted)
            {
                PartDef part = _t.FindPart("tendrils", id);
                if (part == null || !FaunaQuality.Compatible(_t, part, "tendrils", s)) continue;
                s.Parts["tendrils"] = id;
                break;
            }
        }

        s.Temperament = Temperament.Aggressive;
        if (s.Behaviour == Behaviour.HarmlessOddity && s.Rig != RigKind.GasBag)
        {
            s.Behaviour = Behaviour.Ambusher;
            AssignAttack(s);
            // The Bloom gives what was harmless a mouth to match: a grazer's snout becomes jaws.
            if (s.Part("head") == "snout")
            {
                s.Parts["head"] = "jaws";
                foreach (string slot in FaunaTables.Slots)
                    if (slot != "head" && _t.FindPart(slot, s.Part(slot)) is { } part && !FaunaQuality.Compatible(_t, part, slot, s)) s.Parts[slot] = "none";
            }
        }
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

    /// <summary>"fanged stalker": the most striking part plus what kind of thing it is.</summary>
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
            Pattern.Mottle => "mottled",
            Pattern.Veins => "veined",
            _ => s.Colors.GlowStrength > 0.4f ? "glowing" : s.Body.LegStyle == LegStyle.Stilt ? "stilt-legged" : "common",
        };
        return $"{adjective} {noun}";
    }

    static float Wrap(float h) => h - MathF.Floor(h);

    static string Roman(int n) => n switch
    {
        1 => "I", 2 => "II", 3 => "III", 4 => "IV", 5 => "V", 6 => "VI", 7 => "VII", 8 => "VIII", _ => "IX",
    };
}

/// <summary>
/// The rules that keep generated creatures looking like animals rather than accidents. The
/// generator clamps proportions with <see cref="Clamp"/> and checks part pairings with
/// <see cref="Compatible"/>; <see cref="Problems"/> checks a finished species against all of
/// it, so the tests can run thousands of planets through it.
/// </summary>
public static class FaunaQuality
{
    /// <summary>
    /// Proportion bands, as multiples of body length. Outside these a head is cartoon-big, a
    /// body floats on threads or a neck turns into a hose. Giants and floaters have their own.
    /// </summary>
    public static (float lo, float hi) HeadBand(RigKind rig) => rig switch
    {
        RigKind.Chain => (0.07f, 0.24f),
        RigKind.GasBag => (0.1f, 0.35f),
        RigKind.Swarm => (0f, 10f),
        _ => (0.13f, 0.36f),
    };

    /// <summary>
    /// Longest legs allowed, as a multiple of body length. Stilts may be long on a small body,
    /// but a giant on stilts that outreach its own length is a tiny body on poles.
    /// </summary>
    public static float MaxLeg(BodyPlan plan, LegStyle style) => style == LegStyle.Stilt ? (plan == BodyPlan.Giant ? 1.2f : 1.7f) : 1.25f;

    public static void Clamp(FormDef form, Proportions b)
    {
        float L = b.Size;
        (float lo, float hi) = HeadBand(form.Rig);
        b.HeadSize = Math.Clamp(b.HeadSize, lo * L, hi * L);
        if (form.Rig is RigKind.Legged or RigKind.Hopper)
        {
            b.Width = Math.Clamp(b.Width, 0.25f * L, 0.8f * L);
            b.Height = Math.Clamp(b.Height, 0.22f * L, 0.75f * L);
            b.LegLength = Math.Clamp(b.LegLength, 0.25f * L, MaxLeg(form.Plan, b.LegStyle) * L);
            b.NeckLength = Math.Min(b.NeckLength, 1.9f * L);
            b.TailLength = Math.Min(b.TailLength, 1.4f * L);
        }
    }

    /// <summary>Whether a part can join the parts a species already has, by both sides' rules.</summary>
    public static bool Compatible(FaunaTables t, PartDef p, string slot, Species s)
    {
        string key = $"{slot}/{p.Id}";
        foreach (var (otherSlot, otherId) in s.Parts)
        {
            if (otherSlot == slot || otherId == "none") continue;
            string other = $"{otherSlot}/{otherId}";
            if (Array.IndexOf(p.Excludes, other) >= 0) return false;
            PartDef od = t.FindPart(otherSlot, otherId);
            if (od != null && Array.IndexOf(od.Excludes, key) >= 0) return false;
        }
        if (p.Requires.Length == 0) return true;
        foreach (string need in p.Requires)
        {
            int cut = need.IndexOf('/');
            if (s.Part(need[..cut]) == need[(cut + 1)..]) return true;
        }
        return false;
    }

    /// <summary>Everything wrong with a finished species; empty when it is sound.</summary>
    public static List<string> Problems(FaunaTables t, Species s)
    {
        var problems = new List<string>();
        FormDef form = t.Forms.Find(f => f.Id == s.Form);
        if (form == null) return new List<string> { $"unknown form {s.Form}" };
        foreach (string slot in FaunaTables.Slots)
        {
            string id = s.Part(slot);
            PartDef p = t.FindPart(slot, id);
            if (p == null) { problems.Add($"unknown part {slot}/{id}"); continue; }
            bool fixedHere = form.FixedParts.TryGetValue(slot, out string fixedId) && fixedId == id;
            // The Bloom grows tendrils on whatever it takes over, so those may break the role rules.
            bool bloomGrowth = s.Corrupted && slot == "tendrils";
            if (id == "none" || fixedHere) continue;
            if (!p.Fits(s.Rig)) problems.Add($"{slot}/{id} does not fit a {s.Rig} rig");
            if (!p.SuitsForm(s.Form)) problems.Add($"{slot}/{id} does not belong on a {s.Form}");
            // Corruption can turn a harmless oddity into an ambusher after its parts were chosen.
            if (!p.Suits(s.Behaviour) && !bloomGrowth && !s.Corrupted) problems.Add($"{slot}/{id} is wrong for a {s.Behaviour}");
            if (!Compatible(t, p, slot, s)) problems.Add($"{slot}/{id} clashes with its other parts");
            if (p.NeedsNeck && s.Body.NeckLength <= s.Body.Size * 0.25f) problems.Add($"{slot}/{id} needs a neck");
        }
        Proportions b = s.Body;
        (float lo, float hi) = HeadBand(s.Rig);
        if (b.HeadSize < lo * b.Size * 0.999f || b.HeadSize > hi * b.Size * 1.001f) problems.Add($"head {b.HeadSize / b.Size:F2} of body length, outside [{lo}, {hi}]");
        if (s.Rig == RigKind.Legged)
        {
            if (b.Legs % 2 == 1 && form.Legs[0] != form.Legs[1]) problems.Add($"{b.Legs} legs on a form that should have an even number");
            if (b.Legs < 2) problems.Add("a walker with fewer than two legs");
            if (b.LegLength > MaxLeg(s.Plan, b.LegStyle) * b.Size * 1.001f) problems.Add($"legs {b.LegLength / b.Size:F2} of body length");
        }
        if (s.Colors.Pattern == Pattern.Veins && !s.Corrupted && !t.Patterns.Exists(x => x.Id == Pattern.Veins)) problems.Add("veins without the Bloom");
        if (!t.Patterns.Exists(x => x.Id == s.Colors.Pattern) && !s.Corrupted) problems.Add($"pattern {s.Colors.Pattern} is not in the data");
        if (s.Stats.Damage > 0f && (s.Attack == null || s.Attack.Name == "" || s.Attack.Telegraph < 0.6f)) problems.Add("an attacker without a signature attack");
        return problems;
    }
}
