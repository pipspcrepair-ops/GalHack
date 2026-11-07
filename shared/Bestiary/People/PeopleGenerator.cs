using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>Everything about how people look that is a number or a list: data/wildlife.json, "people".</summary>
public sealed class PeopleTables
{
    /// <summary>Mean and spread of height, m.</summary>
    public float[] HeightMale { get; set; } = { 1.76f, 0.075f };
    public float[] HeightFemale { get; set; } = { 1.63f, 0.07f };
    public float[] HeightChild { get; set; } = { 1.05f, 1.45f };
    public string[] HairColours { get; set; } = { "1d1612", "3a281a", "5a3d22", "8a6a3a", "9a4a22", "b9a06a" };
    public string[] Hair { get; set; } = { "short", "short", "long", "tied", "crop", "shaved" };
    /// <summary>Climate band to what everyone there wears.</summary>
    public Dictionary<string, ClimateDress> Climates { get; set; } = new();
    /// <summary>Air that won't keep you alive: a mask and a sealed coverall.</summary>
    public ClimateDress BadAir { get; set; } = new();
    /// <summary>No air at all: a suit outdoors.</summary>
    public ClimateDress Vacuum { get; set; } = new();
    /// <summary>Role to its look.</summary>
    public Dictionary<string, RoleLook> Roles { get; set; } = new();
    /// <summary>Gone wild: what is left of their clothes.</summary>
    public RoleLook Feral { get; set; } = new();
}

/// <summary>What a climate (or a hazard) dresses everyone in.</summary>
public sealed class ClimateDress
{
    public string Dress { get; set; } = "shirt";
    public float Padding { get; set; } = 1f;
    /// <summary>Their bodies, on average: cold builds them heavier, heat leaner.</summary>
    public float Build { get; set; }
    public Dictionary<string, float> Wear { get; set; } = new();
    public string[] Colours { get; set; } = Array.Empty<string>();
}

/// <summary>What a role wears on top of the climate's clothes, carries and looks like.</summary>
public sealed class RoleLook
{
    public Dictionary<string, float> Wear { get; set; } = new();
    public string[] Trim { get; set; } = Array.Empty<string>();
    public string Prop { get; set; } = "";
    /// <summary>Chance of carrying it, when they carry one.</summary>
    public float PropChance { get; set; } = 1f;
    public float Build { get; set; }
    public float Stoop { get; set; }
    public float Stagger { get; set; }
    public string Hair { get; set; } = "";
}

/// <summary>
/// Rolls a world's people. A person is a species of one, shaped by the same things as any animal
/// there: where they live sets their body and clothes, what they do sets their look, how that
/// world has treated them sets their mutation, and who holds the place decides whether they're
/// townsfolk, raiders or feral. Nothing here names a person or a town.
/// </summary>
public sealed class PeopleGenerator
{
    readonly PeopleTables _t;
    readonly WorldConditions _w;
    public readonly Folk Folk;

    public PeopleGenerator(PeopleTables tables, WorldConditions world)
    {
        _t = tables;
        _w = world;
        Folk = RollFolk(world);
    }

    /// <summary>
    /// Who lives on this world: mostly humans, sometimes a human-like folk, and more of those the
    /// stranger the world. A folk's body answers to the world's heat and light however it started.
    /// </summary>
    static Folk RollFolk(WorldConditions w)
    {
        var rng = Rng.For(w.Seed, "folk");
        var f = new Folk();
        float dim = w.Climate is ClimateBand.Frozen or ClimateBand.Cold ? 0.7f : 0.3f;
        if (rng.Chance(0.12f + 0.55f * w.Alienness))
        {
            f.Id = "kin";
            f.Human = false;
            f.HeightScale = rng.Range(0.94f, 1.12f);
            f.LimbScale = rng.Range(0.96f, 1.1f);
            f.BrowRidge = rng.Range(0.2f, 1f);
            f.Dimorphism = rng.Range(0.2f, 1.7f);
            f.Hairless = rng.Chance(0.4f);
            f.DarkVision = rng.Chance(dim) ? rng.Range(0.5f, 1f) : rng.Range(0f, 0.3f);
            // Their skin is their own: ashen, slate, ochre or a pale green-grey.
            float[][] hues = { new[] { 0.09f, 0.12f }, new[] { 0.2f, 0.36f }, new[] { 0.54f, 0.64f }, new[] { 0.95f, 1.0f } };
            f.SkinHue = rng.Pick(hues);
            f.SkinSat = new[] { 0.08f, 0.3f };
            f.SkinVal = new[] { 0.34f, 0.7f };
            f.EyeShine = Color.FromHsv(rng.Range(0.05f, 0.55f), 0.6f, 0.9f);
        }
        // Sun darkens a people, cold lightens them; cold thickens them, heat draws them out.
        f.BuildBias = w.Climate switch { ClimateBand.Frozen => 0.14f, ClimateBand.Cold => 0.08f, ClimateBand.Hot => -0.06f, ClimateBand.Scorching => -0.1f, _ => 0f };
        float shift = w.Climate switch { ClimateBand.Frozen or ClimateBand.Cold => 0.1f, ClimateBand.Hot or ClimateBand.Scorching => -0.14f, _ => 0f };
        f.SkinVal = new[] { Math.Clamp(f.SkinVal[0] + shift, 0.2f, 0.9f), Math.Clamp(f.SkinVal[1] + shift, 0.3f, 0.92f) };
        return f;
    }

    static float Normal(Rng rng, float mean, float spread) => mean + spread * ((rng.Float() + rng.Float() + rng.Float()) / 1.5f - 1f) * 1.7f;

    static Color Hex(string s) => new("#" + s.TrimStart('#'));

    ClimateDress Dress => _w.Vacuum ? _t.Vacuum : _w.BadAir ? _t.BadAir
        : _t.Climates.TryGetValue(_w.Climate.ToString(), out ClimateDress d) ? d : new ClimateDress();

    /// <summary>
    /// One person, the same every time for a seed. <paramref name="mutation"/> is how hard this
    /// world has twisted them, 0 to 1 (see <see cref="Mutation"/>).
    /// </summary>
    public Species Person(ulong seed, PersonRole role, Occupancy occupancy = Occupancy.Friendly, float mutation = 0f, bool husk = false)
    {
        var rng = Rng.For(seed, "person");
        var look = occupancy == Occupancy.Feral ? _t.Feral
            : _t.Roles.TryGetValue(role.ToString(), out RoleLook l) ? l : new RoleLook();
        var f = new Figure { Role = role, Occupancy = occupancy, Folk = Folk, Husk = husk };
        f.Sex = rng.Chance(0.5f) ? Sex.Female : Sex.Male;
        f.Stage = role == PersonRole.Child ? LifeStage.Child : role == PersonRole.Elder ? LifeStage.Elder : LifeStage.Adult;
        f.Age = f.Stage switch { LifeStage.Child => rng.Range(7f, 13f), LifeStage.Elder => rng.Range(62f, 84f), _ => rng.Range(18f, 58f) };

        // Height: the sexes differ by as much as this folk's bodies do; age takes a little back.
        float[] h = f.Sex == Sex.Male ? _t.HeightMale : _t.HeightFemale;
        float mid = (_t.HeightMale[0] + _t.HeightFemale[0]) * 0.5f;
        float mean = mid + (h[0] - mid) * Folk.Dimorphism;
        f.Height = f.Stage == LifeStage.Child ? rng.Range(_t.HeightChild) : Normal(rng, mean, h[1]) * Folk.HeightScale;
        if (f.Stage == LifeStage.Elder) f.Height *= 0.97f;
        f.Build = Math.Clamp(rng.Range(0.1f, 0.75f) + Folk.BuildBias + look.Build + (f.Age > 40f ? 0.08f : 0f) - (f.Stage == LifeStage.Child ? 0.2f : 0f), 0f, 1f);
        f.Stoop = look.Stoop * rng.Range(0.7f, 1.2f) + (f.Age > 55f ? 0.1f : 0f);
        f.Stagger = look.Stagger * rng.Range(0.7f, 1.2f);
        if (husk) { f.Build = MathF.Min(f.Build, 0.2f); f.Stoop = rng.Range(0.3f, 0.6f); f.Stagger = 0.25f; }

        // Colours: their skin; the climate's cloth; a role's trim. A town's fashions are its own rolls.
        float value = rng.Range(Folk.SkinVal);
        f.Skin = Color.FromHsv(Wrap(rng.Range(Folk.SkinHue)), rng.Range(Folk.SkinSat), value);
        if (!Folk.Hairless) f.Hair = Hex(rng.Pick(_t.HairColours));
        if (f.Stage == LifeStage.Elder || f.Age > 50f && rng.Chance(0.5f)) f.Hair = new Color(0.62f, 0.6f, 0.58f).Lerp(f.Hair, 0.2f);
        ClimateDress dress = Dress;
        f.Dress = occupancy == Occupancy.Feral ? "rags" : dress.Dress;
        f.Padding = occupancy == Occupancy.Feral ? 1f : dress.Padding;
        f.Cloth = dress.Colours.Length > 0 ? Hex(rng.Pick(dress.Colours)) : new Color(0.42f, 0.4f, 0.36f);
        f.Cloth = f.Cloth.Darkened(rng.Range(0f, 0.2f));
        f.Trim = look.Trim.Length > 0 ? Hex(rng.Pick(look.Trim)) : f.Cloth.Darkened(0.35f);
        f.Leather = new Color(0.27f, 0.19f, 0.13f).Lightened(rng.Range(0f, 0.15f));
        f.Metal = new Color(0.34f, 0.34f, 0.36f);
        if (occupancy == Occupancy.Feral) f.Cloth = f.Cloth.Lerp(new Color(0.22f, 0.2f, 0.17f), 0.6f);

        f.HairStyle = Folk.Hairless ? "bald" : look.Hair.Length > 0 ? look.Hair : rng.Pick(_t.Hair);
        if (f.Stage == LifeStage.Elder && rng.Chance(0.4f)) f.HairStyle = "bald";
        if (f.HairStyle == "long" && f.Sex == Sex.Male && rng.Chance(0.6f)) f.HairStyle = "short";
        if (f.Sex == Sex.Female && f.HairStyle == "short" && rng.Chance(0.5f)) f.HairStyle = rng.Chance(0.5f) ? "long" : "tied";
        f.Beard = f.Sex == Sex.Male && f.Stage != LifeStage.Child && !Folk.Hairless && rng.Chance(0.4f);

        // What they wear goes on hazard first, then the job, then the weather: whatever filled a slot stays.
        if (occupancy != Occupancy.Feral)
        {
            if (_w.BadAir) Put(f, rng, dress.Wear);
            Put(f, rng, look.Wear);
            if (!_w.BadAir) Put(f, rng, dress.Wear);
        }
        else Put(f, rng, look.Wear);
        if (rng.Chance(look.PropChance)) f.Prop = look.Prop;
        // Raiders on a day's cold world keep their crest under nothing: spikes and a mohawk are how they're known.
        f.Measure();

        var s = new Species
        {
            Id = $"person-{seed:x}",
            Seed = seed,
            Plan = BodyPlan.Walker,
            Form = "person",
            Rig = RigKind.Humanoid,
            Figure = f,
            Name = Folk.Id,
            CommonName = husk ? "gaunt figure" : occupancy == Occupancy.Feral ? "wild figure" : occupancy == Occupancy.Raider ? "raider" : role.ToString().ToLowerInvariant(),
            Behaviour = occupancy == Occupancy.Friendly ? Behaviour.Grazer : Behaviour.Predator,
            Temperament = occupancy == Occupancy.Friendly ? Temperament.Calm : Temperament.Aggressive,
            Activity = Activity.Cathemeral,
            RestKind = RestKind.Den,
            GroupMin = 1,
            GroupMax = occupancy == Occupancy.Friendly ? 1 : 4,
        };
        foreach (string slot in FaunaTables.Slots) s.Parts[slot] = "none";
        Proportions b = s.Body;
        b.Size = f.Torso;
        b.Width = f.ShoulderW * 2f;
        b.Height = f.Torso;
        b.LegLength = f.LegLength;
        b.LegThickness = 0.09f * f.Height / 1.72f;
        b.HeadSize = f.HeadRadius / 0.6f;
        b.NeckLength = f.Neck;
        b.Legs = 2;
        s.Colors.Base = f.Skin;
        s.Colors.Accent = f.Cloth;
        s.Colors.Belly = f.Skin.Lightened(0.05f);
        s.Colors.Pattern = Pattern.Plain;
        if (Folk.DarkVision > 0.5f)
        {
            s.Colors.Glow = Folk.EyeShine;
            s.Colors.GlowStrength = 0.2f;
        }
        Stats st = s.Stats;
        float mass = f.Mass / 75f;
        st.MaxHealth = MathF.Round(60f * Math.Clamp(mass, 0.5f, 1.4f));
        st.WalkSpeed = f.Stage == LifeStage.Elder ? 1.0f : 1.3f;
        st.RunSpeed = f.Stage == LifeStage.Elder ? 2.2f : 3.6f;
        st.Damage = occupancy == Occupancy.Friendly ? 0f : 12f + 8f * mass;
        st.AttackRange = 1.3f;
        st.Perception = 40f;
        st.FleeRadius = 10f;
        st.Radius = 0.35f;
        st.Retaliates = occupancy != Occupancy.Friendly;
        st.Hunts = occupancy != Occupancy.Friendly;
        s.Attack = new AttackDef { Name = "Rush", Style = AttackStyle.Lunge, Telegraph = 1.1f, Sound = "snarl" };
        s.Drops.Meat = 0;
        s.Mutation = Math.Clamp(mutation, 0f, 1f);
        if (mutation > 0f) MutateFigure(s, rng, mutation);
        return s;
    }

    /// <summary>The parts of mutation that belong to a person: skin gone wrong, posture, and at the top end something else entirely.</summary>
    static void MutateFigure(Species s, Rng rng, float k)
    {
        Figure f = s.Figure;
        if (k > 0.15f) f.Skin = f.Skin.Lerp(Color.FromHsv(rng.Range(0f, 1f), 0.4f, f.Skin.V * rng.Range(0.7f, 1f)), 0.45f * k);
        s.Colors.Base = f.Skin;
        f.Stoop = MathF.Max(f.Stoop, 0.5f * k * rng.Range(0.3f, 1f));
        s.Monstrous = k > 0.7f;
        if (k > 0.8f && !f.Has("suit_helmet")) f.HairStyle = "bald";
    }

    /// <summary>Puts on what a table says, each at its chance; a slot already filled stays as it is.</summary>
    static void Put(Figure f, Rng rng, Dictionary<string, float> table)
    {
        foreach (var (id, chance) in table)
        {
            if (!rng.Chance(chance)) continue;
            string slot = HumanBody.Slot(id);
            bool taken = false;
            if (slot != "")
                foreach (string w in f.Wear) if (HumanBody.Slot(w) == slot) { taken = true; break; }
            if (!taken) f.Wear.Add(id);
        }
    }

    static float Wrap(float h) => h - MathF.Floor(h);

    /// <summary>The look of a town's job, as the place's own data calls it, with a roll for who the idle people are.</summary>
    public static PersonRole RoleFor(string townRole, bool farm, Rng rng) => townRole switch
    {
        "vendor" => PersonRole.Trader,
        "guard" => PersonRole.Guard,
        "worker" => farm ? PersonRole.Farmer : rng.Chance(0.15f) ? PersonRole.Medic : PersonRole.Worker,
        "rusher" => rng.Chance(0.3f) ? PersonRole.Medic : PersonRole.Scavenger,
        _ => rng.Float() switch { < 0.28f => PersonRole.Elder, < 0.5f => PersonRole.Child, < 0.62f => PersonRole.Drunk, < 0.82f => PersonRole.Scavenger, _ => PersonRole.Worker },
    };
}
