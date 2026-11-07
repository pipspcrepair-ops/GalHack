using System;
using System.Collections.Generic;

namespace Wildlife;

/// <summary>
/// What a creature does in a cave or a place (a POI): its job in that small ecosystem. None on
/// the open surface.
/// </summary>
public enum DeepRole
{
    None,
    /// <summary>Hunts cave walls and ceilings and drops onto prey.</summary>
    Crawler,
    /// <summary>Waits in a dark room and strikes when you come close.</summary>
    Lurker,
    /// <summary>Roams the corridors on a route.</summary>
    Prowler,
    /// <summary>Hides in a vent, on a ceiling, behind a door or in a crack, and gives itself away just before it strikes.</summary>
    Ambusher,
    /// <summary>Guards a nest in the deepest levels; worse the deeper it is.</summary>
    Nester,
    /// <summary>Grazes moss and fungus or picks at scraps, and flees: what the place's hunters eat.</summary>
    Subsister,
    /// <summary>Builds nests in a derelict ship's holds, ducts and engine rooms, and defends them.</summary>
    DerelictNester,
}

/// <summary>The kind of place a creature lives in, which sets its look.</summary>
public enum PoiTheme
{
    /// <summary>Caves and tunnels, mines dug into them: earthy, and pale and blind deep down.</summary>
    Natural,
    /// <summary>Garrisons, labs, mines and stations: armoured, oily, rust-dark.</summary>
    Industrial,
    /// <summary>Alien places, Precursor ruins, the Bloom's work: organic, glossy, glowing.</summary>
    Alien,
    /// <summary>Wrecked ships, crashed or drifting: armoured scrap-eaters.</summary>
    Derelict,
}

/// <summary>
/// How an ambush gives itself away before it strikes. Every ambush has at least one, starting
/// at least a second before anything can land: frightening, but fair.
/// </summary>
public enum TellKind
{
    /// <summary>A growl swelling through the wind-up (every attack's own telegraph).</summary>
    Growl,
    /// <summary>Claws scrabbling inside the walls, moving towards you.</summary>
    Scratch,
    /// <summary>A vent grille rattling as something shifts inside the duct.</summary>
    Rattle,
    /// <summary>Grit trickling down from the ceiling over your head.</summary>
    Dust,
    /// <summary>A shadow sliding over the floor from something on the ceiling.</summary>
    Shadow,
    /// <summary>Thumping and scraping behind a door.</summary>
    Thump,
    /// <summary>Slow breathing in a dark room, and eyes opening.</summary>
    Breath,
    /// <summary>Dry clicking: something blind finding you by sound.</summary>
    Click,
    /// <summary>A nest's guard rearing and hissing: back off.</summary>
    Hiss,
}

/// <summary>What a subsister lives on.</summary>
public enum Diet { None, Moss, Fungus, Scraps, Prey }

/// <summary>A role's tunables: which bodies it may have, how it notices and strikes, its tells.</summary>
public sealed class DeepRoleDef
{
    /// <summary>The behaviour its parts and signature attack are rolled for.</summary>
    public string Behaviour { get; set; } = "Predator";
    /// <summary>Form id to weight.</summary>
    public Dictionary<string, float> Forms { get; set; } = new();
    public string Activity { get; set; } = "Cathemeral";
    /// <summary>Tells it can give (TellKind names). Ambushers pick one by where they hide.</summary>
    public string[] Tells { get; set; } = Array.Empty<string>();
    /// <summary>How long its tell runs before the strike begins. Never under a second.</summary>
    public float TellSeconds { get; set; } = 1.2f;
    /// <summary>How close a player walking upright must come before it notices them (crouching shrinks this).</summary>
    public float Notice { get; set; } = 10f;
    /// <summary>An ambusher springs once its prey is this close.</summary>
    public float Strike { get; set; } = 4f;
    /// <summary>Nesters: a warning display inside this radius of the nest, an attack inside <see cref="Defend"/>.</summary>
    public float Warn { get; set; }
    public float Defend { get; set; }
    public int[] Group { get; set; } = { 1, 1 };
    public float HealthScale { get; set; } = 1f;
    public float DamageScale { get; set; } = 1f;
    public float SizeScale { get; set; } = 1f;
    public float SpeedScale { get; set; } = 1f;
    /// <summary>Eats the place's subsisters: the food chain a watching player can see.</summary>
    public bool Hunts { get; set; }
    /// <summary>Runs from players and hunters, into cracks if it can.</summary>
    public bool Flees { get; set; }
    public string Diet { get; set; } = "None";
}

/// <summary>A theme's look: hue, saturation and value ranges, glow, armour.</summary>
public sealed class ThemeDef
{
    public float Alienness { get; set; } = 0.5f;
    public float[] Hue { get; set; } = { 20f, 50f };
    public float[] Saturation { get; set; } = { 0.15f, 0.35f };
    public float[] Value { get; set; } = { 0.3f, 0.5f };
    /// <summary>Chance a species of this theme glows.</summary>
    public float Glow { get; set; } = 0.2f;
    /// <summary>Back parts it favours (armour plates in a wreck, blisters in an alien place); empty: as rolled.</summary>
    public string[] Backs { get; set; } = Array.Empty<string>();
    /// <summary>An accent for danger only (a stinger, a crest): never on things that can't hurt you.</summary>
    public string Warning { get; set; } = "";
}

/// <summary>How depth changes things: paler and blind further down, nests bigger and better guarded.</summary>
public sealed class DepthRules
{
    /// <summary>From this level down, creatures lose their colour.</summary>
    public int PaleFrom { get; set; } = 2;
    /// <summary>From this level down, they lose their eyes and find you by sound (they click).</summary>
    public int BlindFrom { get; set; } = 3;
    /// <summary>How much colour each level past <see cref="PaleFrom"/> takes away (0..1).</summary>
    public float Paleness { get; set; } = 0.35f;
    /// <summary>Nesters grow by this share of their size per level of depth.</summary>
    public float NestScale { get; set; } = 0.1f;
    /// <summary>Nesters at a nest: base count plus this many per level.</summary>
    public float[] Nesters { get; set; } = { 2f, 1.25f };
    /// <summary>Damage and health grow by this share per level.</summary>
    public float Toughness { get; set; } = 0.1f;
    public int MaxLevel { get; set; } = 6;
}

/// <summary>How a place is populated, by its state.</summary>
public sealed class PopulationRules
{
    /// <summary>State name to the chance a place is empty and silent.</summary>
    public Dictionary<string, float> Silent { get; set; } = new() { ["Active"] = 0.9f, ["Abandoned"] = 0.35f, ["Ruined"] = 0.4f, ["Infested"] = 0f };
    /// <summary>State name to how thickly it is populated (1 = an abandoned place).</summary>
    public Dictionary<string, float> Density { get; set; } = new() { ["Active"] = 0.15f, ["Abandoned"] = 0.55f, ["Ruined"] = 0.45f, ["Infested"] = 1.1f };
    /// <summary>Chance a dark room holds a lurker.</summary>
    public float Lurker { get; set; } = 0.55f;
    /// <summary>Chance a corridor, vent or door hides an ambusher.</summary>
    public float Ambusher { get; set; } = 0.35f;
    /// <summary>Prowlers per corridor.</summary>
    public float Prowler { get; set; } = 0.34f;
    /// <summary>Subsisters per room (they come in groups).</summary>
    public float Subsister { get; set; } = 0.6f;
    /// <summary>Food patches (moss, fungus, scraps) per room.</summary>
    public int[] Food { get; set; } = { 1, 3 };
    /// <summary>Cracks per room, where the small things go.</summary>
    public int[] Cracks { get; set; } = { 1, 3 };
    /// <summary>The most creatures in a room, one more per this many square metres of floor; and the most round a nest.</summary>
    public int RoomCap { get; set; } = 2;
    public float RoomArea { get; set; } = 60f;
    public int NestCap { get; set; } = 5;
    /// <summary>The most creatures in a place, by its state; one more per this many rooms, up to double.</summary>
    public Dictionary<string, int> PlaceCap { get; set; } = new() { ["Active"] = 3, ["Abandoned"] = 5, ["Ruined"] = 5, ["Infested"] = 9 };
    public int CapRooms { get; set; } = 10;
}

/// <summary>How things move on surfaces.</summary>
public sealed class CrawlRules
{
    /// <summary>A falling or dropping creature accelerates at this, m/s².</summary>
    public float Gravity { get; set; } = 12f;
    /// <summary>How fast a crawler's body turns to a new surface (per second): smooth over a corner.</summary>
    public float TurnRate { get; set; } = 9f;
    /// <summary>Most a crawler moves in one substep, metres.</summary>
    public float Substep { get; set; } = 0.08f;
    /// <summary>Floor walkers only accept ground whose normal is at least this far up.</summary>
    public float FloorUp { get; set; } = 0.6f;
    /// <summary>Hidden in the walls, it moves this much slower than it crawls.</summary>
    public float WallSpeed { get; set; } = 0.6f;
    /// <summary>How often scratching is heard from something moving inside the walls, seconds.</summary>
    public float ScratchEvery { get; set; } = 0.6f;
}

/// <summary>The fauna of caves, tunnels and places, as data (data/wildlife.json, "deep").</summary>
public sealed class DeepTables
{
    public Dictionary<string, DeepRoleDef> Roles { get; set; } = new();
    public Dictionary<string, ThemeDef> Themes { get; set; } = new();
    public DepthRules Depth { get; set; } = new();
    public PopulationRules Population { get; set; } = new();
    public CrawlRules Crawl { get; set; } = new();

    public DeepRoleDef Role(DeepRole r) => Roles.TryGetValue(r.ToString(), out var d) ? d : new DeepRoleDef();
    public ThemeDef Theme(PoiTheme t) => Themes.TryGetValue(t.ToString(), out var d) ? d : new ThemeDef();

    public List<string> Validate(FaunaTables fauna)
    {
        var problems = new List<string>();
        foreach (var (name, role) in Roles)
        {
            if (!Enum.TryParse<DeepRole>(name, out _)) problems.Add($"deep: unknown role {name}");
            if (!Enum.TryParse<Behaviour>(role.Behaviour, out _)) problems.Add($"deep role {name}: unknown behaviour {role.Behaviour}");
            if (!Enum.TryParse<Activity>(role.Activity, out _)) problems.Add($"deep role {name}: unknown activity {role.Activity}");
            if (!Enum.TryParse<Diet>(role.Diet, out _)) problems.Add($"deep role {name}: unknown diet {role.Diet}");
            if (role.Forms.Count == 0) problems.Add($"deep role {name}: no forms");
            foreach (string f in role.Forms.Keys)
                if (fauna.Forms.Find(x => x.Id == f) == null) problems.Add($"deep role {name}: unknown form {f}");
            foreach (string t in role.Tells)
                if (!Enum.TryParse<TellKind>(t, out _)) problems.Add($"deep role {name}: unknown tell {t}");
            // A tell shorter than a second is not a warning anyone can react to.
            if (role.Tells.Length > 0 && role.TellSeconds < 1f) problems.Add($"deep role {name}: tell {role.TellSeconds} s is under a second");
        }
        foreach (string t in Themes.Keys)
            if (!Enum.TryParse<PoiTheme>(t, out _)) problems.Add($"deep: unknown theme {t}");
        return problems;
    }
}
