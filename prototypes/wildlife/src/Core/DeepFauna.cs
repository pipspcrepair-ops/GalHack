using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// The fauna of a planet's caves and places, rolled one role at a time from the planet's seed:
/// for each role (lurker, prowler, ambusher, nester, subsister, wreck nester, cave crawler), each
/// kind of place (cave, industrial, alien, derelict) and each depth band, one species, the same
/// on every machine. Bodies come from <see cref="FaunaGenerator.RollOne"/>, so every rule that
/// keeps surface animals looking like animals holds here too; then the place themes them:
/// - deep down they go pale, and deeper still blind (sensing pits for eyes; they click);
/// - in industrial places and wrecks they are armoured, oily and rust-dark;
/// - in alien places glossy, saturated and glowing;
/// - in an infested place, taken by the Bloom.
/// Warning colours go only on things that can hurt you. Names are in the planet's own flavour.
/// </summary>
public sealed class DeepFauna
{
    readonly WildlifeData _data;
    readonly FaunaGenerator _gen;
    readonly ulong _seed;
    readonly float _alienness;
    readonly NameGen _names;
    readonly HashSet<string> _taken = new();
    readonly Dictionary<string, Species> _cache = new();

    public string PlanetName { get; }
    public IEnumerable<Species> Rolled => _cache.Values;

    public DeepFauna(WildlifeData data, PlanetFauna surface) : this(data, surface.Seed, surface.Alienness)
    {
        PlanetName = surface.PlanetName;
        foreach (Species s in surface.Species) _taken.Add(s.Name);
    }

    public DeepFauna(WildlifeData data, ulong planetSeed, float alienness)
    {
        _data = data;
        _gen = new FaunaGenerator(data.Fauna, data.Activity);
        _seed = planetSeed;
        _alienness = alienness;
        _names = new NameGen(planetSeed, data.Fauna.Syllables);
        PlanetName ??= "";
    }

    /// <summary>The depth band a level falls in: 0 near the way in, 1 pale, 2 pale and blind.</summary>
    public int Band(int depth) => depth < _data.Deep.Depth.PaleFrom ? 0 : depth < _data.Deep.Depth.BlindFrom ? 1 : 2;

    /// <summary>
    /// The species that fills a role in this kind of place at this depth. Nesters (and wreck
    /// nesters) grow with depth within a band: the same species, bigger and tougher further down.
    /// </summary>
    public Species For(DeepRole role, PoiTheme theme, int depth, bool infested = false)
    {
        DepthRules dr = _data.Deep.Depth;
        depth = Math.Clamp(depth, 0, dr.MaxLevel);
        int band = Band(depth);
        bool scales = role is DeepRole.Nester or DeepRole.DerelictNester;
        string key = $"{role}/{theme}/{band}/{(infested ? 1 : 0)}/{(scales ? depth : -1)}";
        if (_cache.TryGetValue(key, out Species cached)) return cached;
        int floor = BandFloor(band);
        Species s = scales && depth != floor ? Deeper(For(role, theme, floor, infested), depth) : Roll(role, theme, band, scales ? floor : depth, infested);
        _cache[key] = s;
        return s;
    }

    int BandFloor(int band) => band == 0 ? 0 : band == 1 ? _data.Deep.Depth.PaleFrom : _data.Deep.Depth.BlindFrom;

    Species Roll(DeepRole role, PoiTheme theme, int band, int depth, bool infested)
    {
        DeepTables t = _data.Deep;
        DeepRoleDef rd = t.Role(role);
        ThemeDef td = t.Theme(theme);
        ulong seed = Rng.Mix(_seed, Rng.Hash($"deep/{role}/{theme}/{band}/{(infested ? "bloom" : "")}"));
        var rng = Rng.For(seed, "deep");

        var forms = new List<string>(rd.Forms.Keys);
        forms.Sort(StringComparer.Ordinal);
        string form = rng.Weighted(forms, f => rd.Forms[f]);
        Behaviour behaviour = Enum.Parse<Behaviour>(rd.Behaviour);
        float alienness = Mathf.Lerp(_alienness, td.Alienness, 0.6f);
        Species s = _gen.RollOne(seed, form, behaviour, alienness);
        FormDef fd = _data.Fauna.Forms.Find(f => f.Id == form);

        s.Role = role;
        s.Theme = theme;
        s.Depth = depth;
        s.Climbs = fd?.Climbs ?? false;
        s.Activity = Enum.TryParse(rd.Activity, out Activity a) ? a : Activity.Cathemeral;
        s.RestKind = RestKind.None;
        s.Blind = band >= 2;
        s.TellSeconds = MathF.Max(1f, rd.TellSeconds);
        var tells = new List<TellKind>();
        foreach (string k in rd.Tells) if (Enum.TryParse(k, out TellKind tk)) tells.Add(tk);
        if (s.Blind && !tells.Contains(TellKind.Click)) tells.Add(TellKind.Click);
        s.Tells = tells.ToArray();
        s.NoticeRadius = rd.Notice * (s.Blind ? 1.15f : 1f);
        s.StrikeRadius = rd.Strike;
        s.WarnRadius = rd.Warn + depth * 0.5f;
        s.DefendRadius = rd.Defend + depth * 0.25f;
        s.HuntsPrey = rd.Hunts;
        s.FleesIntoCracks = rd.Flees;
        s.Diet = role == DeepRole.Subsister
            ? theme switch { PoiTheme.Industrial or PoiTheme.Derelict => Diet.Scraps, PoiTheme.Alien => Diet.Fungus, _ => band > 0 ? Diet.Fungus : Diet.Moss }
            : Enum.TryParse(rd.Diet, out Diet d) ? d : Diet.None;

        ThemeBack(s, td, rng);
        Look(s, td, band, rng);
        Size(s, rd.SizeScale);
        Stats(s, rd, depth);
        Attack(s, role, form);

        s.Name = _names.MakeUnique(rng, _taken);
        s.CommonName = Prefix(s, band) + _gen.NameFor(s);
        s.Id = $"{_seed:x}-deep-{role}-{theme}-{band}";
        s.Index = 100 + (int)role * 10 + (int)theme;
        // Overrun by the Bloom: the same thing, darker, veined and angrier ("Hollow ..."; its id gains "-hollow").
        if (infested) _gen.CorruptSpecies(s, seed);
        return s;
    }

    /// <summary>The same species further down: bigger and tougher, as nests get worse with depth.</summary>
    Species Deeper(Species b, int depth)
    {
        DepthRules dr = _data.Deep.Depth;
        int over = depth - b.Depth;
        Species s = b.Copy();
        s.Depth = depth;
        float grow = 1f + dr.NestScale * over;
        ScaleBody(s.Body, grow);
        s.Stats.MaxHealth = MathF.Round(b.Stats.MaxHealth * (1f + dr.Toughness * over) * grow);
        s.Stats.Damage = MathF.Round(b.Stats.Damage * (1f + dr.Toughness * over));
        s.Stats.Radius = b.Stats.Radius * grow;
        s.Stats.AttackRange = b.Stats.AttackRange * grow;
        s.WarnRadius = b.WarnRadius + over * 0.5f;
        s.DefendRadius = b.DefendRadius + over * 0.25f;
        s.Seed = Rng.Mix(b.Seed, (ulong)depth);
        return s;
    }

    /// <summary>Armour in a wreck, blisters in an alien place: a back part the place favours, if it fits the body and the job.</summary>
    void ThemeBack(Species s, ThemeDef td, Rng rng)
    {
        if (td.Backs.Length == 0 || Array.IndexOf(td.Backs, s.Part("back")) >= 0) return;
        FormDef form = _data.Fauna.Forms.Find(f => f.Id == s.Form);
        if (form != null && form.FixedParts.ContainsKey("back")) return;
        var options = new List<string>(td.Backs);
        rng.Shuffle(options);
        string original = s.Part("back");
        foreach (string id in options)
        {
            PartDef p = _data.Fauna.FindPart("back", id);
            if (p == null || !p.Fits(s.Rig) || !p.Suits(s.Behaviour) || !p.SuitsForm(s.Form)) continue;
            s.Parts["back"] = "none";
            if (!FaunaQuality.Compatible(_data.Fauna, p, "back", s)) continue;
            s.Parts["back"] = id;
            return;
        }
        s.Parts["back"] = original;
    }

    /// <summary>Colours for the place and the depth: the theme's palette, paler and paler further down.</summary>
    void Look(Species s, ThemeDef td, int band, Rng rng)
    {
        DepthRules dr = _data.Deep.Depth;
        Palette c = s.Colors;
        float hue = rng.Range(td.Hue) / 360f, sat = rng.Range(td.Saturation), val = rng.Range(td.Value);
        if (band > 0)
        {
            // Deep down colour goes: bone, grey-pink, the white of things that never see light.
            float pale = Math.Clamp(dr.Paleness * band * 1.3f + 0.1f, 0f, 0.92f);
            sat *= 1f - pale;
            val = Mathf.Lerp(val, 0.82f, pale);
            hue = Mathf.Lerp(hue, 0.04f, pale * 0.5f);
        }
        c.Base = Color.FromHsv(hue, sat, val);
        c.Belly = Color.FromHsv(hue, sat * 0.5f, MathF.Min(1f, val * 1.25f + 0.08f));
        bool dangerous = s.Role != DeepRole.Subsister;
        if (dangerous && td.Warning != "")
        {
            // A warning colour, but only on what can hurt you, and only as bands on dark armour.
            c.Accent = Color.FromHtml(td.Warning).Lerp(c.Base, 0.55f + 0.2f * band);
            c.Pattern = Pattern.Bands;
        }
        else
        {
            c.Accent = Color.FromHsv(hue, MathF.Min(1f, sat + 0.1f), val * 0.6f);
            c.Pattern = band > 0 ? Pattern.Mottle : rng.Chance(0.5f) ? Pattern.Countershade : Pattern.Mottle;
        }
        c.PatternScale = rng.Range(0.8f, 1.3f);
        bool glows = rng.Chance(td.Glow) || (band >= 2 && rng.Chance(0.5f));
        c.GlowStrength = glows ? rng.Range(0.5f, 0.9f) : 0f;
        if (band >= 2) c.Glow = new Color("9fe8ff"); // pale, cold light from things with no eyes to see it
        else if (s.Theme == PoiTheme.Derelict) c.Glow = new Color("ffb03b");
    }

    static void Size(Species s, float scale)
    {
        if (MathF.Abs(scale - 1f) < 1e-3f) return;
        ScaleBody(s.Body, scale);
        s.Stats.Radius *= scale;
        s.Stats.AttackRange *= scale;
        s.Stats.MaxHealth = MathF.Round(s.Stats.MaxHealth * scale * scale);
    }

    static void ScaleBody(Proportions b, float k)
    {
        b.Size *= k;
        b.Width *= k;
        b.Height *= k;
        b.LegLength *= k;
        b.LegThickness *= k;
        b.NeckLength *= k;
        b.HeadSize *= k;
        b.TailLength *= k;
    }

    void Stats(Species s, DeepRoleDef rd, int depth)
    {
        DepthRules dr = _data.Deep.Depth;
        Stats st = s.Stats;
        float tough = 1f + dr.Toughness * depth * (s.Role is DeepRole.Nester or DeepRole.DerelictNester ? 1f : 0.5f);
        st.MaxHealth = MathF.Round(st.MaxHealth * rd.HealthScale * tough);
        st.Damage = MathF.Round(st.Damage * rd.DamageScale * tough);
        st.WalkSpeed *= rd.SpeedScale;
        st.RunSpeed *= rd.SpeedScale;
        if (s.Rig == RigKind.Crawler)
        {
            st.Radius = MathF.Max(0.15f, s.Body.Width * 0.6f);
            st.AttackRange = st.Radius + 0.7f + s.Body.LegLength * 0.3f;
        }
        if (s.Role == DeepRole.Subsister)
        {
            // Something to eat, not something to fear: it runs.
            s.Temperament = Temperament.Skittish;
            st.Damage = 0f;
            st.Retaliates = false;
            st.Hunts = false;
            st.FleeRadius = 6f;
        }
        else
        {
            s.Temperament = Temperament.Aggressive;
            st.Retaliates = true;
            st.Hunts = true;
            st.FleeRadius = 0f;
            if (st.Damage < 6f) st.Damage = 6f;
        }
        st.Perception = MathF.Max(st.Perception, s.NoticeRadius);
    }

    /// <summary>The signature attack, for the body and the job: creepers drop from ceilings, eels burst out of vents.</summary>
    void Attack(Species s, DeepRole role, string form)
    {
        AttackDef a = s.Attack;
        s.Attack = role switch
        {
            DeepRole.Subsister => a.With(AttackStyle.Lunge, "Nip"),
            DeepRole.Crawler or DeepRole.Ambusher when form == "creeper" => a.With(AttackStyle.Drop, "Ceiling drop"),
            DeepRole.Crawler or DeepRole.Ambusher => a.With(AttackStyle.Burst, "Burst"),
            DeepRole.Lurker => a.With(AttackStyle.Pounce, "Lunge from the dark"),
            DeepRole.Prowler => a.With(AttackStyle.Pounce, "Pounce"),
            DeepRole.Nester or DeepRole.DerelictNester => a.With(AttackStyle.Lunge, "Nest guard's bite"),
            _ => a,
        };
        if (s.Attack.Telegraph < 0.9f) s.Attack.Telegraph = 0.9f;
        s.Stats.Telegraph = s.Attack.Telegraph;
    }

    string Prefix(Species s, int band) => band >= 2 ? "blind " : band == 1 ? "pale " : s.Theme switch
    {
        PoiTheme.Industrial => "rust ",
        PoiTheme.Derelict => "wreck ",
        PoiTheme.Alien => "",
        _ => "cave ",
    };
}
