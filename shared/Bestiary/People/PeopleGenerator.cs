using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>What a rainy region adds to anyone's wear, whatever the band.</summary>
    public ClimateDress Rain { get; set; } = new();
    /// <summary>Breathable but poisoned (radiation, a mutagen): a hazard suit with a filter mask.</summary>
    public ClimateDress Hazard { get; set; } = new();
    /// <summary>How warm each garment keeps you (clo-like units); a dress's own base counts too.</summary>
    public Dictionary<string, float> Garments { get; set; } = new();
    /// <summary>Role to its look.</summary>
    public Dictionary<string, RoleLook> Roles { get; set; } = new();
    /// <summary>Gone wild: what is left of their clothes.</summary>
    public RoleLook Feral { get; set; } = new();
}

/// <summary>
/// A place's own spot on its world: the temperature and humidity of its region (the world map's),
/// which dress its people. Two regions of one world dress differently; a visitor keeps their own.
/// </summary>
public sealed class Locale
{
    /// <summary>Below this humidity a region is dry, above the next humid, above the last rainy (0 to 1, the map's).</summary>
    public const float Dry = 0.33f, Humid = 0.62f, Rainy = 0.78f;
    public float TempC = 15f, Humidity = 0.5f;

    public ClimateBand Band => new WorldConditions { TempC = TempC }.Climate;
}

/// <summary>What a climate (or a hazard) dresses everyone in.</summary>
public sealed class ClimateDress
{
    /// <summary>(Empty in a humidity variant: the band's own dress stands.)</summary>
    public string Dress { get; set; } = "";
    public float Padding { get; set; } = 1f;
    /// <summary>Their bodies, on average: cold builds them heavier, heat leaner.</summary>
    public float Build { get; set; }
    public Dictionary<string, float> Wear { get; set; } = new();
    public string[] Colours { get; set; } = Array.Empty<string>();
    /// <summary>How warm they need to be here, and the layers they put on to get there, innermost first: base, insulation, wind, shell.</summary>
    public float Warmth { get; set; }
    public Dictionary<string, string[]> Layers { get; set; } = new();
    /// <summary>The same band in a humid region and in a dry one: their own dress, wear and colours over the band's.</summary>
    public ClimateDress Humid { get; set; }
    public ClimateDress Dry { get; set; }
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
    /// <summary>What their body says when you come up to them peacefully, when you aim at them, and when you make them jump (a <see cref="BodyAct"/> name).</summary>
    public string Approach { get; set; } = "Greet";
    public string Aimed { get; set; } = "HandsUp";
    public string Startled { get; set; } = "Startle";
    /// <summary>The hue range (0 to 1) of the one saturated mark the job wears.</summary>
    public float[] Accent { get; set; } = Array.Empty<float>();
    /// <summary>
    /// Their day: when they are out and about (a share of the day, from and to; to past from wraps
    /// past midnight; 0 to 1 is always), the spots they keep to (by tag), and how they get about
    /// there: stay, beat, patrol, work, visit, sit, play, roam, leave, or wander.
    /// </summary>
    public float[] Out { get; set; } = { 0.26f, 0.78f };
    public string[] Haunts { get; set; } = Array.Empty<string>();
    public string Moves { get; set; } = "wander";
}

/// <summary>
/// Where a settlement's people came from: the look its founders had, which most of them still
/// share with natural variation, and how many are from elsewhere. Rolled from the world and the
/// place, never from anyone's job, side or temper.
/// </summary>
public sealed class Founders
{
    public float SkinHue, SkinSat, SkinValue;
    public string[] Hair = Array.Empty<string>();
    /// <summary>A shared cast of face: brow and jaw, as small offsets.</summary>
    public float Brow, Jaw;
    /// <summary>The people's lean in build: longer or shorter in the leg, broader or narrower, as small offsets.</summary>
    public float Leg, Shoulder, Hip, Build;
    /// <summary>The share who are outsiders and travellers: more in trade hubs and spaceports.</summary>
    public float Outsiders = 0.08f;
    /// <summary>The folk who founded the place, and in a mixed place another folk and its share.</summary>
    public Folk Folk, Second;
    public float SecondShare;
    /// <summary>Where on its world the place stands: the region that dresses its people (null: the world's mean).</summary>
    public Locale Locale;
}

/// <summary>
/// Rolls a world's people. A person is a species of one, shaped by the same things as any animal
/// there: where they live sets their body and clothes, what they do sets their look, how that
/// world has treated them sets their mutation, and who holds the place decides whether they're
/// townsfolk, raiders or feral. Nothing here names a person or a town.
/// </summary>
public sealed partial class PeopleGenerator
{
    readonly PeopleTables _t;
    readonly WorldConditions _w;
    /// <summary>The world's first folk: most of its places were founded by them.</summary>
    public readonly Folk Folk;
    /// <summary>Every folk of the world, with how many of its places each founded (the first leads).</summary>
    public readonly List<(Folk Folk, float Share)> Folks = new();
    /// <summary>How everyone here speaks, more or less.</summary>
    public readonly Dialect Dialect;
    /// <summary>How the worms take someone (the world's data sets it).</summary>
    public HuskRules HuskRules = new();

    /// <summary>
    /// What the world's story and era put on its people, everyone not gone wild (set by the world's
    /// life from its signature tables): masks on a plague world, festival pieces on a parade world;
    /// what the era dresses them in (and the dress it makes, by chance); and what it arms its
    /// fighters with (a club in a low-tech world, a rifle in a mid one, a sealed helmet too in a high one).
    /// </summary>
    public RoleLook Flavour, Era, Armed;
    public Dictionary<string, float> EraDress;
    public Dictionary<string, string> EraSwap;
    Folk _vharn;

    public PeopleGenerator(PeopleTables tables, WorldConditions world)
    {
        _t = tables;
        _w = world;
        RollFolks(world);
        Folk = Folks[0].Folk;
        Dialect = Dialect.For(world.Seed);
    }

    /// <summary>The Vharn as they are on this world: rolled from the same parts, leaning to chitin (their places only).</summary>
    public Folk Vharn => _vharn ??= Settle(Peoples.RollVharn(Rng.For(_w.Seed, "vharn"), _w), _w);

    /// <summary>
    /// Who lives on this world: one to three folk, humans among them more often than not, and more
    /// alien folk the stranger the world. A world's founders roll its mix; no two of its folk share
    /// an outline (docs/alien-peoples.md 1.6). A folk's body answers to the world's heat and light
    /// however it started.
    /// </summary>
    void RollFolks(WorldConditions w)
    {
        var rng = Rng.For(w.Seed, "folk");
        float alien = 0.12f + 0.55f * w.Alienness;
        int count = 1 + (rng.Chance(0.2f + 0.45f * w.Alienness) ? 1 : 0);
        if (count == 2 && rng.Chance(0.15f + 0.3f * w.Alienness)) count = 3;
        bool humans = false;
        for (int i = 0; i < count; i++)
        {
            Folk f;
            // Humans are among a world's folk more often than not, at most once.
            if (!humans && !rng.Chance(i == 0 ? alien : 0.6f)) { f = new Folk(); humans = true; }
            else
            {
                f = Peoples.Roll(rng, w, $"folk{i}");
                // Two folk of one world never share an outline: rolled again until they differ.
                for (int tries = 0; tries < 40 && Folks.Exists(o => !o.Folk.Human && Peoples.Shared(o.Folk, f) > 2); tries++) f = Peoples.Roll(rng, w, $"folk{i}");
                if (Folks.Exists(o => !o.Folk.Human && Peoples.Shared(o.Folk, f) > 2)) continue; // none different enough: one folk fewer
            }
            Folks.Add((Settle(f, w), i == 0 ? 1f : rng.Range(0.15f, 0.4f)));
        }
    }

    /// <summary>Sun darkens a people, cold lightens them; cold thickens them, heat draws them out.</summary>
    static Folk Settle(Folk f, WorldConditions w)
    {
        if (f.Human) f.BuildBias = w.Climate switch { ClimateBand.Frozen => 0.14f, ClimateBand.Cold => 0.08f, ClimateBand.Hot => -0.06f, ClimateBand.Scorching => -0.1f, _ => 0f };
        float shift = w.Climate switch { ClimateBand.Frozen or ClimateBand.Cold => 0.1f, ClimateBand.Hot or ClimateBand.Scorching => -0.14f, _ => 0f };
        f.SkinVal = new[] { Math.Clamp(f.SkinVal[0] + shift, 0.2f, 0.9f), Math.Clamp(f.SkinVal[1] + shift, 0.3f, 0.92f) };
        return f;
    }

    /// <summary>One of the world's folk by their shares of its places.</summary>
    Folk PickFolk(Rng rng, Folk not = null)
    {
        float total = 0f;
        foreach (var (f, share) in Folks) if (f != not) total += share;
        float r = rng.Float() * total;
        foreach (var (f, share) in Folks)
        {
            if (f == not) continue;
            if ((r -= share) <= 0f) return f;
        }
        return Folk;
    }

    static float Normal(Rng rng, float mean, float spread) => mean + spread * ((rng.Float() + rng.Float() + rng.Float()) / 1.5f - 1f) * 1.7f;

    static Color Hex(string s) => new("#" + s.TrimStart('#'));

    /// <summary>
    /// What the weather where they live dresses them in: their region's temperature band, then its
    /// humidity (dry, or humid; very wet regions add the rain's wear on top); a suit where there is
    /// no air, and where the air is bad but the weather mild, a sealed coverall. With no region,
    /// the world's own mean.
    /// </summary>
    ClimateDress DressFor(Locale region)
    {
        ClimateBand band = region?.Band ?? _w.Climate;
        if (_w.Vacuum) return _t.Vacuum;
        if (_w.BadAir && band == ClimateBand.Mild) return _t.BadAir;
        ClimateDress d = _t.Climates.TryGetValue(band.ToString(), out ClimateDress c) ? c : new ClimateDress();
        float wet = region?.Humidity ?? 0.5f;
        ClimateDress over = wet < Locale.Dry ? d.Dry : wet > Locale.Humid ? d.Humid : null;
        bool rain = wet > Locale.Rainy && band is not ClimateBand.Frozen;
        if (over == null && !rain) return d;
        // The region's own on top of the band's: its dress, its wear first (a slot it fills stays), its colours, its warmth.
        var m = new ClimateDress
        {
            Dress = over?.Dress is { Length: > 0 } dd ? dd : d.Dress,
            Padding = over != null && over.Padding != 1f ? over.Padding : d.Padding,
            Build = d.Build,
            Colours = over?.Colours is { Length: > 0 } cc ? cc : d.Colours,
            Warmth = d.Warmth + (over?.Warmth ?? 0f),
            Layers = over?.Layers is { Count: > 0 } ll ? ll : d.Layers,
        };
        if (rain) foreach (var (id, p) in _t.Rain.Wear) m.Wear[id] = p;
        if (over != null) foreach (var (id, p) in over.Wear) m.Wear.TryAdd(id, p);
        foreach (var (id, p) in d.Wear) m.Wear.TryAdd(id, p);
        return m;
    }

    /// <summary>A few places' regions across the world: where an outsider comes from (a game with a map sets them; else the world's mean).</summary>
    public List<Locale> Regions = new();

    /// <summary>
    /// One person, the same every time for a seed. <paramref name="mutation"/> is how hard this
    /// world has twisted them, 0 to 1 (see <see cref="Mutation"/>).
    /// </summary>
    /// <summary>
    /// The founders of a place, from the world's seed and the place's: neighbouring places of
    /// one world lean the same way more than not, and a busier place has more outsiders.
    /// </summary>
    public Founders FoundersOf(ulong place, bool crossroads = false, bool vharn = false)
    {
        var world = Rng.For(_w.Seed, "founders");
        var here = Rng.For(Rng.Mix(_w.Seed, place), "founders");
        float lean = world.Float();
        // Who founded it: one of the world's folk by their shares (the Vharn hold their own places);
        // about a third of places on a world of several folk are mixed, and crossroads mix more.
        var who = Rng.For(Rng.Mix(_w.Seed, place), "founding folk");
        Folk folk = vharn ? Vharn : PickFolk(who);
        Folk second = null;
        float share = 0f;
        if (!vharn && Folks.Count > 1 && who.Chance(crossroads ? 0.8f : 0.33f))
        {
            second = PickFolk(who, folk);
            share = crossroads ? who.Range(0.25f, 0.45f) : who.Range(0.15f, 0.3f);
        }
        float v = Mathf.Lerp(folk.SkinVal[0], folk.SkinVal[1], Math.Clamp(lean + here.Range(-0.35f, 0.35f), 0f, 1f));
        var hair = new string[2];
        for (int i = 0; i < hair.Length; i++) hair[i] = here.Pick(_t.HairColours);
        return new Founders
        {
            SkinHue = here.Range(folk.SkinHue), SkinSat = here.Range(folk.SkinSat), SkinValue = v, Hair = hair,
            Brow = here.Range(-0.15f, 0.15f), Jaw = here.Range(-0.08f, 0.08f),
            Leg = here.Range(-0.03f, 0.03f), Shoulder = here.Range(-0.04f, 0.04f), Hip = here.Range(-0.04f, 0.04f), Build = here.Range(-0.1f, 0.1f),
            Outsiders = vharn ? 0.02f : crossroads ? here.Range(0.25f, 0.45f) : here.Range(0.03f, 0.12f),
            Folk = folk, Second = second, SecondShare = share,
        };
    }

    /// <summary>Who fights for their people: guards, the law, raiders, and anyone of a raiding band (never the wild).</summary>
    public static bool Fighter(PersonRole role, Occupancy occupancy) =>
        occupancy != Occupancy.Feral && (role is PersonRole.Guard or PersonRole.Raider or PersonRole.Lawman || occupancy == Occupancy.Raider);

    /// <summary>
    /// The one place a fighter is armed for the world's era (<see cref="Armed"/>, from the signature
    /// tables' "armed" by era): its gear on (with <paramref name="clear"/>, off first whatever held those
    /// slots, keeping air gear where the air is bad), its weapon in hand, its trim. Era weapons extend
    /// the tables' props here and nowhere else.
    /// </summary>
    public void Arm(Figure f, Rng rng, bool clear)
    {
        if (Armed == null) return;
        if (clear)
            foreach (string g in Armed.Wear.Keys)
            {
                string slot = HumanBody.Slot(g);
                if (slot != "" && slot != "mouth") f.Wear.RemoveWhere(w => HumanBody.Slot(w) == slot && !(_w.BadAir && w is "suit_helmet" or "air_tank"));
            }
        Put(f, rng, Armed.Wear);
        if (Armed.Prop != "") f.Prop = Armed.Prop;
    }

    public Species Person(ulong seed, PersonRole role, Occupancy occupancy = Occupancy.Friendly, float mutation = 0f, bool husk = false, bool young = false, Founders founders = null)
    {
        var rng = Rng.For(seed, "person");
        var look = occupancy == Occupancy.Feral ? _t.Feral
            : _t.Roles.TryGetValue(role.ToString(), out RoleLook l) ? l : new RoleLook();
        // Their folk: the place's founders', or in a mixed place now and then the other folk there;
        // an outsider is any folk of the world. One person is one folk: no blends.
        var kin = Rng.For(seed, "kin");
        bool local = founders != null && !kin.Chance(founders.Outsiders);
        var whose = Rng.For(seed, "folk");
        Folk folk = founders == null ? PickFolk(whose) : !local ? (whose.Chance(0.6f) ? founders.Folk ?? Folk : PickFolk(whose))
            : founders.Second != null && whose.Chance(founders.SecondShare) ? founders.Second : founders.Folk ?? Folk;
        // A place's lean in skin is its own folk's: one of the other folk there takes their own people's range.
        if (founders != null && folk != (founders.Folk ?? Folk)) local = false;
        var f = new Figure { Role = role, Occupancy = occupancy, Folk = folk };
        f.Sex = rng.Chance(0.5f) ? Sex.Female : Sex.Male;
        f.Stage = role == PersonRole.Child || young ? LifeStage.Child : role == PersonRole.Elder ? LifeStage.Elder : LifeStage.Adult;
        // A child doing a grown-up's job carries it like one: upright, kitted out, no play in them.
        f.Hardened = young && role != PersonRole.Child;
        f.Age = f.Stage switch { LifeStage.Child => rng.Range(7f, 13f), LifeStage.Elder => rng.Range(62f, 84f), _ => rng.Range(18f, 58f) };

        // Height: the sexes differ by as much as this folk's bodies do; age takes a little back.
        float[] h = f.Sex == Sex.Male ? _t.HeightMale : _t.HeightFemale;
        float mid = (_t.HeightMale[0] + _t.HeightFemale[0]) * 0.5f;
        float mean = mid + (h[0] - mid) * folk.Dimorphism;
        f.Height = f.Stage == LifeStage.Child ? MathF.Min(rng.Range(_t.HeightChild) * MathF.Sqrt(folk.HeightScale), _t.HeightChild[1]) /* a child is a child, whatever their folk */ : Normal(rng, mean, h[1]) * folk.HeightScale;
        if (f.Stage == LifeStage.Elder) f.Height *= 0.97f;
        f.Build = Math.Clamp(rng.Range(0.1f, 0.75f) + folk.BuildBias + look.Build + (f.Age > 40f ? 0.08f : 0f) - (f.Stage == LifeStage.Child ? 0.2f : 0f), 0f, 1f);
        f.Stoop = f.Hardened ? 0f : MathF.Max(0f, look.Stoop * rng.Range(0.7f, 1.2f) + (f.Age > 55f ? 0.1f : 0f) + (f.Stage == LifeStage.Child ? 0f : folk.StoopBias));
        f.Stagger = look.Stagger * rng.Range(0.7f, 1.2f);

        // Colours: their skin; the climate's cloth; a role's trim. A town's fashions are its own rolls.
        // Their looks come from where their people settled, on a stream of their own: nothing about
        // their job, side or temper reaches it.
        f.Skin = local
            ? Color.FromHsv(Wrap(founders.SkinHue + kin.Range(-0.01f, 0.01f)), Math.Clamp(founders.SkinSat + kin.Range(-0.05f, 0.05f), 0f, 1f), Math.Clamp(founders.SkinValue + kin.Range(-1f, 1f) * MathF.Min(0.07f, 0.3f * (folk.SkinVal[1] - folk.SkinVal[0])), 0.15f, 0.95f)) /* a place's spread is a share of its folk's */
            : Color.FromHsv(Wrap(kin.Range(folk.SkinHue)), kin.Range(folk.SkinSat), kin.Range(folk.SkinVal));
        if (!folk.Hairless) f.Hair = Hex(local && kin.Chance(0.85f) ? kin.Pick(founders.Hair) : kin.Pick(_t.HairColours));
        f.Brow = local ? founders.Brow + kin.Range(-0.05f, 0.05f) : kin.Range(-0.15f, 0.15f);
        f.Jaw = local ? founders.Jaw + kin.Range(-0.03f, 0.03f) : kin.Range(-0.08f, 0.08f);
        // Each proportion on its own, round their people's lean: builds vary inside a place and lean a little by people.
        float Around(float lean, float spread) => 1f + (local ? lean : 0f) + (kin.Float() + kin.Float() - 1f) * spread;
        f.LegK = Around(founders?.Leg ?? 0f, 0.05f);
        f.ArmK = Around(0f, 0.045f);
        f.NeckK = Around(0f, 0.18f);
        f.HeadK = Around(0f, 0.05f);
        f.HandK = Around(0f, 0.12f);
        f.FootK = Around(0f, 0.1f);
        f.HipK = Around(founders?.Hip ?? 0f, 0.1f);
        f.ShoulderK = Around(founders?.Shoulder ?? 0f, 0.09f);
        if (local) f.Build = Math.Clamp(f.Build + founders.Build, 0f, 1f);
        if (f.Stage == LifeStage.Elder || f.Age > 50f && rng.Chance(0.5f)) f.Hair = new Color(0.62f, 0.6f, 0.58f).Lerp(f.Hair, 0.2f);
        // Dressed for where they live: the place's region, or for a visitor their own region's.
        Locale region = local || founders == null ? founders?.Locale : Regions.Count > 0 ? Regions[(int)(Rng.For(seed, "from").Float() * Regions.Count) % Regions.Count] : founders.Locale;
        f.Home = region;
        ClimateDress dress = DressFor(region);
        f.Dress = occupancy == Occupancy.Feral ? "rags" : dress.Dress is { Length: > 0 } worn ? worn : "shirt";
        f.Padding = occupancy == Occupancy.Feral ? 1f : dress.Padding;
        f.Cloth = dress.Colours.Length > 0 ? Hex(rng.Pick(dress.Colours)) : new Color(0.42f, 0.4f, 0.36f);
        f.Cloth = f.Cloth.Darkened(rng.Range(0f, 0.2f));
        f.Trim = look.Trim.Length > 0 ? Hex(rng.Pick(look.Trim)) : f.Cloth.Darkened(0.35f);
        // Against the ground they stand on: darker figures on bright sand and snow, lighter on dark ground.
        f.Cloth = Apart(f.Cloth, _w.Backdrop);
        f.Trim = Apart(f.Trim, _w.Backdrop);
        // One saturated mark by their job, never the hue of the ground.
        f.Accent = AccentOf(look, rng, _w.Backdrop);
        f.Leather = new Color(0.27f, 0.19f, 0.13f).Lightened(rng.Range(0f, 0.15f));
        f.Metal = new Color(0.34f, 0.34f, 0.36f);
        if (occupancy == Occupancy.Feral) f.Cloth = f.Cloth.Lerp(new Color(0.22f, 0.2f, 0.17f), 0.6f);

        f.HairStyle = look.Hair == "mohawk" ? "mohawk" : folk.Hairless ? "bald" : look.Hair.Length > 0 ? look.Hair : rng.Pick(_t.Hair); // a raider's crest is made, not grown
        if (f.Stage == LifeStage.Elder && rng.Chance(0.4f)) f.HairStyle = "bald";
        if (f.HairStyle == "long" && f.Sex == Sex.Male && rng.Chance(0.6f)) f.HairStyle = "short";
        if (f.Sex == Sex.Female && f.HairStyle == "short" && rng.Chance(0.5f)) f.HairStyle = rng.Chance(0.5f) ? "long" : "tied";
        f.Beard = f.Sex == Sex.Male && f.Stage != LifeStage.Child && !folk.Hairless && rng.Chance(0.4f);

        // What they wear goes on hazard first, then the job, then the weather: whatever filled a slot stays.
        if (occupancy != Occupancy.Feral)
        {
            // Bad air comes first: a mask whatever else they have on.
            if (_w.BadAir) Put(f, rng, _w.Vacuum ? _t.Vacuum.Wear : _t.BadAir.Wear);
            // Air you can breathe but shouldn't: a filter mask and a hazard hood, over a hazard suit where it's mild enough.
            else if (HazardWorld)
            {
                var hz = Rng.For(seed, "hazard");
                Put(f, hz, _t.Hazard.Wear);
                if ((region?.Band ?? _w.Climate) is ClimateBand.Mild or ClimateBand.Hot && _t.Hazard.Dress.Length > 0) f.Dress = _t.Hazard.Dress;
            }
            Put(f, rng, look.Wear);
            // The world's story, then its era, before the weather: whatever filled a slot stays.
            if (Flavour != null) Put(f, Rng.For(seed, "flavour"), Flavour.Wear);
            if (Era != null) Put(f, Rng.For(seed, "era"), Era.Wear);
            if (Fighter(role, occupancy)) Arm(f, Rng.For(seed, "armed"), clear: false);
            if (EraDress != null && f.Dress is "shirt" or "light" or "coat")
            {
                var made = Rng.For(seed, "era-dress");
                foreach (var (d, chance) in EraDress) if (made.Chance(chance)) { f.Dress = d; break; }
            }
            if (!_w.Vacuum) Put(f, rng, dress.Wear);
            if (!_w.Vacuum) Layer(f, dress, Rng.For(seed, "layers"));
            // What the era makes in place of what another would: a head wrap for a helmet, a sealed helmet for one.
            if (EraSwap != null)
                foreach (var (from, to) in EraSwap)
                    if (f.Wear.Remove(from) && to != "") f.Wear.Add(to);
        }
        else Put(f, rng, look.Wear);
        Age(f, occupancy, Rng.For(seed, "worn"));
        FitParts(f);
        if (rng.Chance(look.PropChance)) f.Prop = look.Prop;
        // The era arms its fighters: what they carry is what their world could make, whatever their job's tool.
        if (Fighter(role, occupancy) && Armed is { Prop.Length: > 0 } a) f.Prop = a.Prop;
        if (occupancy != Occupancy.Feral && Flavour is { Prop.Length: > 0 } fl && f.Prop == "" && Rng.For(seed, "carry").Chance(fl.PropChance)) f.Prop = fl.Prop;
        // A fighter's weapon is whatever the world's era makes: spears, bows and clubs low, rifles mid, energy weapons too high.
        if (Weapons.Armed(f.Prop)) f.Prop = Weapons.ForEra(_w.Now, _w.Before, Rng.For(seed, "weapon"));
        // In a rainy region, a free hand holds an umbrella now and then.
        if (f.Prop == "" && (region?.Humidity ?? 0f) > Locale.Rainy && f.Stage != LifeStage.Child && Rng.For(seed, "umbrella").Chance(0.35f) && occupancy == Occupancy.Friendly) f.Prop = "umbrella";
        f.Routine = look;
        f.Approach = Enum.TryParse(look.Approach, out BodyAct approach) ? approach : BodyAct.Greet;
        f.Aimed = Enum.TryParse(look.Aimed, out BodyAct aimed) ? aimed : BodyAct.HandsUp;
        f.Startled = Enum.TryParse(look.Startled, out BodyAct startled) ? startled : BodyAct.Startle;
        f.Measure();
        f.Voice = Voice.For(f, Dialect, seed);

        var s = new Species
        {
            Id = $"person-{seed:x}",
            Seed = seed,
            Plan = BodyPlan.Walker,
            Form = "person",
            Rig = RigKind.Humanoid,
            Figure = f,
            Name = folk.Id,
            CommonName = occupancy == Occupancy.Feral ? "wild figure" : occupancy == Occupancy.Raider ? "raider" : role.ToString().ToLowerInvariant(),
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
        if (folk.DarkVision > 0.5f)
        {
            s.Colors.Glow = folk.EyeShine;
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
        // Wide shoulders, a shell or a thick tail need more room to pass: at most half a metre, well inside a doorway.
        st.Radius = Math.Clamp(0.35f * MathF.Max(1f, f.ShoulderW / (0.235f * f.Height / 1.72f)) + (folk.Tail is "thick" or "bushy" ? 0.06f : 0f) + (folk.Back == "plates" ? 0.04f : 0f), 0.35f, 0.5f);
        st.Retaliates = occupancy != Occupancy.Friendly;
        st.Hunts = occupancy != Occupancy.Friendly;
        s.Attack = new AttackDef { Name = "Rush", Style = AttackStyle.Lunge, Telegraph = 1.1f, Sound = "snarl" };
        s.Drops.Meat = 0;
        s.Mutation = Math.Clamp(mutation, 0f, 1f);
        if (mutation > 0f) MutateFigure(s, rng, mutation);
        if (occupancy != Occupancy.Friendly) Fit.Throat(s, rng);
        // Taken by the worms: the same person, in their own clothes, torn (see Husks).
        if (husk) Husks.Take(s, Rng.For(seed, "husk"), Dialect, HuskRules);
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

    /// <summary>A world whose air keeps you alive but poisons you: radiation or a mutagen.</summary>
    bool HazardWorld => !_w.BadAir && (_w.Radiation >= 2f || _w.Mutagen >= 0.5f);

    /// <summary>What a dress keeps you warm by itself, before any layer.</summary>
    static float DressWarmth(string dress) => dress switch { "furs" => 1.6f, "coat" => 0.9f, "suit" => 3f, "hazard" => 0.8f, "wraps" => 0.15f, "rags" => 0.1f, _ => 0.3f };

    /// <summary>How warm everything they have on keeps them.</summary>
    public float WarmthOf(Figure f)
    {
        float w = DressWarmth(f.Dress);
        foreach (string g in f.Wear) w += _t.Garments.GetValueOrDefault(g);
        foreach (string g in f.Layered) w += _t.Garments.GetValueOrDefault(g);
        return w;
    }

    /// <summary>
    /// Dressing for the weather in layers, innermost first (base, insulation, wind, shell), each
    /// put on only while they are still colder than the climate asks; a shell already worn
    /// counts. Every layer adds bulk.
    /// </summary>
    void Layer(Figure f, ClimateDress dress, Rng rng)
    {
        foreach (string layer in new[] { "base", "insulation", "wind", "shell" })
        {
            if (WarmthOf(f) >= dress.Warmth) break;
            if (!dress.Layers.TryGetValue(layer, out string[] choices) || choices.Length == 0) continue;
            string g = rng.Pick(choices);
            string slot = HumanBody.Slot(g);
            if (slot != "" && f.Wear.Any(w => HumanBody.Slot(w) == slot)) continue; // a coat or a robe is already the shell
            if (layer == "shell") f.Wear.Add(g);
            else f.Layered.Add(g);
            f.Padding += layer == "base" ? 0.02f : 0.05f;
        }
        f.Warmth = WarmthOf(f);
    }

    /// <summary>
    /// How worn their clothes are: older people's are older, a raider's and a wild one's worse,
    /// and everyone's some. Worn cloth fades toward grey; the patches are drawn (<see cref="HumanBody"/>).
    /// </summary>
    static void Age(Figure f, Occupancy occupancy, Rng rng)
    {
        float age = Math.Clamp((f.Age - 16f) / 60f, 0f, 1f);
        f.Worn = Math.Clamp(rng.Range(0f, 0.45f) + 0.45f * age + (occupancy == Occupancy.Raider ? 0.25f : occupancy == Occupancy.Feral ? 0.5f : 0f), 0f, 1f);
        var grey = new Color(0.5f, 0.49f, 0.46f);
        f.Cloth = f.Cloth.Lerp(grey, 0.28f * f.Worn);
        f.Trim = f.Trim.Lerp(grey, 0.2f * f.Worn);
        if (f.Warmth <= 0f) f.Warmth = DressWarmth(f.Dress);
    }

    /// <summary>
    /// A folk's parts against their clothes (docs/alien-peoples.md 6): a tall crown leaves no room
    /// for a cap, a helmet or a hat (horns pierce a hood); a back part takes the back slot, a pack
    /// becoming a satchel; a thick tail means no long coat. Nothing here knows what a folk is.
    /// </summary>
    static void FitParts(Figure f)
    {
        Folk k = f.Folk;
        if (k == null || k.Human) return;
        if (NoHatFits(f))
            foreach (string hat in Hats) f.Wear.Remove(hat);
        if (k.Back != "" && f.Dress != "suit")
        {
            if (f.Wear.Remove("backpack") && !f.Wear.Any(w => HumanBody.Slot(w) == "side")) f.Wear.Add("satchel");
            f.Wear.Remove("air_tank");
        }
        if (k.Tail is "thick" or "bushy") { f.Wear.Remove("coat"); f.Wear.Remove("poncho"); }
    }

    static readonly string[] Hats = { "helmet", "cap", "wide_hat", "straw_hat" };

    /// <summary>A tall crown or tall ears: no cap, helmet or hat sits on that head.</summary>
    public static bool NoHatFits(Figure f) => f.Folk is { Human: false } k && (k.CrownRise > 0.9f || k.Ears is "tall");

    /// <summary>Puts on what a table says, each at its chance; a slot already filled stays as it is. A hat their head can't take never fills the slot (a head wrap after it still can).</summary>
    static void Put(Figure f, Rng rng, Dictionary<string, float> table)
    {
        foreach (var (id, chance) in table)
        {
            if (!rng.Chance(chance)) continue;
            if (Array.IndexOf(Hats, id) >= 0 && NoHatFits(f)) continue;
            string slot = HumanBody.Slot(id);
            bool taken = false;
            if (slot != "")
                foreach (string w in f.Wear) if (HumanBody.Slot(w) == slot) { taken = true; break; }
            if (!taken) f.Wear.Add(id);
        }
    }

    static float Wrap(float h) => h - MathF.Floor(h);

    /// <summary>A colour moved far enough in value from the backdrop to read against it.</summary>
    static Color Apart(Color c, Color backdrop)
    {
        float gap = c.V - backdrop.V;
        if (MathF.Abs(gap) >= 0.22f) return c;
        return backdrop.V > 0.5f ? c.Darkened(Math.Clamp((0.22f - gap) / MathF.Max(0.05f, c.V), 0f, 0.7f)) : c.Lightened(Math.Clamp((0.22f + gap) * 1.2f, 0f, 0.5f));
    }

    static Color AccentOf(RoleLook look, Rng rng, Color backdrop)
    {
        float hue = look.Accent.Length > 0 ? rng.Range(look.Accent) : rng.Float();
        backdrop.ToHsv(out float bh, out float bs, out _);
        float d = MathF.Abs(Wrap(hue - bh + 0.5f) - 0.5f);
        if (bs > 0.15f && d < 0.12f) hue = Wrap(hue + 0.5f);
        return Color.FromHsv(hue, rng.Range(0.65f, 0.85f), rng.Range(0.55f, 0.75f));
    }

    /// <summary>The look of a town's job, as the place's own data calls it, with a roll for who the idle people are.</summary>
    /// <summary>
    /// How likely a child is to be doing a grown-up's job here: rare, and only where the world
    /// forces it: a harsh world, a raider place, too few adults to go round.
    /// </summary>
    public float HardenedChance(bool raiders, int adults) =>
        Math.Clamp(0.01f + (_w.BadAir || _w.Climate is ClimateBand.Frozen or ClimateBand.Scorching ? 0.04f : 0f) + _w.Mutagen * 0.05f + (raiders ? 0.08f : 0f) + (adults < 6 ? 0.08f : 0f), 0f, 0.25f);

    public static PersonRole RoleFor(string townRole, bool farm, Rng rng) => townRole switch
    {
        "vendor" => PersonRole.Trader,
        // Some of a place's guards are its law: they walk the streets and keep a checkpoint at the way in.
        "guard" => rng.Chance(0.35f) ? PersonRole.Lawman : PersonRole.Guard,
        "worker" => farm ? PersonRole.Farmer : rng.Float() switch { < 0.15f => PersonRole.Medic, < 0.38f => PersonRole.Mechanic, _ => PersonRole.Worker },
        "rusher" => rng.Chance(0.3f) ? PersonRole.Medic : PersonRole.Scavenger,
        _ => rng.Float() switch { < 0.28f => PersonRole.Elder, < 0.5f => PersonRole.Child, < 0.62f => PersonRole.Drunk, < 0.82f => PersonRole.Scavenger, _ => PersonRole.Worker },
    };
}
