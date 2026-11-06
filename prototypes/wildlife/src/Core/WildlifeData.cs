using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wildlife;

// Every tunable for fauna, combat, harvesting and the clock lives in data/wildlife.json.
// These classes only give the file a shape (its schema); the defaults here are a fallback
// so a missing field never crashes the game, and Validate() reports what looks wrong.

public enum BodyPlan { Walker, Crawler, Hopper, Flier, Floater, Swarm, Giant, Oddity, Voidborne }

/// <summary>
/// The small set of reliable rigs every body plan is drawn and animated with. Finned and
/// Clinger are the zero-g rigs of space fauna: swimmers that pulse, flap or undulate through
/// open space, and leeches that crawl on a hull. Crawler is the rig of things that live in
/// caves, tunnels and places: a body that lies against whatever surface it holds (floor, wall
/// or ceiling) on legs that plant on it.
/// </summary>
public enum RigKind { Legged, Chain, Hopper, Winged, GasBag, Swarm, Finned, Clinger, Crawler }

/// <summary>Where a species lives. Planet wildlife lives on the surface; space fauna in the rest.</summary>
public enum Habitat { Surface, Nebula, AsteroidField, GasGiant, Derelict, Void }

/// <summary>How a zero-g swimmer moves: a pulsing bell, flapping wings, or a body that undulates.</summary>
public enum SwimStyle { None, Pulse, Flap, Undulate }

/// <summary>The design's five-step resource ladder; space creatures drop only rare and up.</summary>
public enum Rarity { Common, Uncommon, Rare, Exotic, Unique }

public enum Behaviour { Grazer, Herd, Predator, PackHunter, Ambusher, Scavenger, Swarm, TerritorialGiant, HarmlessOddity }

/// <summary>How a creature reacts to you, independent of what it does all day. Odd pairs make surprises.</summary>
public enum Temperament { Skittish, Calm, Bold, Aggressive }

public enum LegStyle { Upright, Splayed, Stilt, Roots }

public enum Pattern { Plain, Countershade, Spots, Stripes, Bands, Rings, Mottle, Veins }

/// <summary>
/// How a signature attack winds up and lands. The wind-up is always the telegraph; the style
/// only changes what it looks and sounds like and how the lunge travels.
/// </summary>
public enum AttackStyle
{
    /// <summary>Rears back, then snaps forward.</summary>
    Lunge,
    /// <summary>Crouches low and coiled, then springs in an arc.</summary>
    Pounce,
    /// <summary>Drops its head and paws the ground, then charges a longer way.</summary>
    Charge,
    /// <summary>Rears right up (a giant, or a crawler's cobra coil), then slams down.</summary>
    Rear,
    /// <summary>Bursts up out of the ground it was hiding in.</summary>
    Burst,
    /// <summary>Climbs, then dives.</summary>
    Dive,
    /// <summary>A swarm pulls in tight and buzzes, then streams at you.</summary>
    Sting,
    /// <summary>Lets go of a ceiling and drops onto where you stood.</summary>
    Drop,
}

public sealed class WildlifeData
{
    public CombatRules Combat { get; set; } = new();
    public HarvestRules Harvest { get; set; } = new();
    public WorldRules World { get; set; } = new();
    public SpawnRules Spawning { get; set; } = new();
    public FaunaTables Fauna { get; set; } = new();
    /// <summary>When each kind of animal is out, and where it rests the other hours.</summary>
    public ActivityRules Activity { get; set; } = new();
    /// <summary>The fauna of caves, tunnels and places: roles, themes, tells and how places are populated.</summary>
    public DeepTables Deep { get; set; } = new();

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Parses the tables. Pure: the caller reads the file (Godot's res:// lives in the .pck when exported).</summary>
    public static WildlifeData FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<WildlifeData>(json, Options) ?? throw new InvalidOperationException("empty wildlife data");
        var problems = data.Validate();
        if (problems.Count > 0) throw new InvalidOperationException("wildlife data: " + string.Join("; ", problems));
        return data;
    }

    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Fauna.Forms.Count == 0) problems.Add("no body forms");
        foreach (var form in Fauna.Forms)
        {
            if (form.Size.Length != 2) problems.Add($"form {form.Id}: size needs [min, max]");
            if (form.Behaviours.Count == 0) problems.Add($"form {form.Id}: no behaviours");
            foreach (string b in form.Behaviours.Keys)
                if (!Enum.TryParse<Behaviour>(b, out _)) problems.Add($"form {form.Id}: unknown behaviour {b}");
            foreach (var (slot, part) in form.FixedParts)
                if (Fauna.FindPart(slot, part) == null) problems.Add($"form {form.Id}: unknown fixed part {slot}/{part}");
        }
        foreach (string slot in FaunaTables.Slots)
            if (!Fauna.Parts.ContainsKey(slot)) problems.Add($"no parts for slot {slot}");
        foreach (var (slot, list) in Fauna.Parts)
        foreach (PartDef part in list)
        {
            foreach (string role in part.Roles)
                if (!Enum.TryParse<Behaviour>(role, out _)) problems.Add($"part {slot}/{part.Id}: unknown role {role}");
            foreach (string other in part.Excludes)
                if (Fauna.FindPart(other) == null) problems.Add($"part {slot}/{part.Id}: excludes unknown part {other}");
            foreach (string other in part.Requires)
                if (Fauna.FindPart(other) == null) problems.Add($"part {slot}/{part.Id}: requires unknown part {other}");
        }
        if (Spawning.BaseClearance < 0f) problems.Add("spawning: baseClearance must not be negative");
        if (Spawning.RespawnSeconds <= 0f || Spawning.CheckSeconds <= 0f) problems.Add("spawning: respawnSeconds and checkSeconds must be positive");
        if (Spawning.Attempts < 1) problems.Add("spawning: attempts must be at least 1");
        foreach (var (name, _) in Activity.Windows)
            if (!Enum.TryParse<Activity>(name, out _)) problems.Add($"activity: unknown cycle {name}");
        foreach (var (name, def) in Fauna.Behaviours)
        foreach (string a in def.Activity.Keys)
            if (!Enum.TryParse<Activity>(a, out _)) problems.Add($"behaviour {name}: unknown activity {a}");
        foreach (var form in Fauna.Forms)
            if (form.Rest != "" && !Enum.TryParse<RestKind>(form.Rest, out _)) problems.Add($"form {form.Id}: unknown rest site {form.Rest}");
        problems.AddRange(Deep.Validate(Fauna));
        foreach (Behaviour b in Enum.GetValues<Behaviour>())
        {
            if (!Fauna.Behaviours.TryGetValue(b.ToString(), out BehaviourDef def)) problems.Add($"no behaviour entry for {b}");
            else if (def.Attack == null || def.Attack.Name == "") problems.Add($"behaviour {b}: no signature attack");
            // A telegraph shorter than this is not a warning anyone can react to.
            else if (def.Attack.Telegraph < 0.6f) problems.Add($"behaviour {b}: telegraph {def.Attack.Telegraph} s is too short to react to");
        }
        return problems;
    }
}

/// <summary>
/// Fairness comes from warning, not from softening damage: a creature's first attack on a
/// player is always a telegraphed lunge with a clear wind-up, aimed at where you stood when it
/// committed, so stepping aside dodges it. Only then does it attack at its normal rate.
/// Damage is never capped.
/// </summary>
public sealed class CombatRules
{
    /// <summary>Wind-up of a first attack when the behaviour gives none (behaviours set their own).</summary>
    public float TelegraphWindup { get; set; } = 1.1f;
    /// <summary>Wind-up of later attacks, once the creature has shown its hand.</summary>
    public float NormalWindup { get; set; } = 0.45f;
    /// <summary>Share of a wind-up after which the creature commits to its aim; moving after that dodges.</summary>
    public float CommitFraction { get; set; } = 0.65f;
    public float LungeSeconds { get; set; } = 0.25f;
    /// <summary>How far past its reach a lunge carries, in metres.</summary>
    public float LungeReach { get; set; } = 1.2f;
    /// <summary>A lunge connects only if the target is within this distance of where it lands.</summary>
    public float ContactRadius { get; set; } = 1.0f;
    /// <summary>After this long without attacking you, a creature must telegraph again.</summary>
    public float TelegraphMemorySeconds { get; set; } = 20f;
    public float AttackCooldown { get; set; } = 1.4f;
    public float PlayerMaxHealth { get; set; } = 100f;
}

public sealed class HarvestRules
{
    public int StorageCapacity { get; set; } = 400;
    public float TreeHealth { get; set; } = 60f;
    public float BushHealth { get; set; } = 14f;
    public int WoodPerTree { get; set; } = 24;
    public int OrganicsPerTree { get; set; } = 6;
    public int OrganicsPerBush { get; set; } = 8;
    public float RegrowSeconds { get; set; } = 45f;
    /// <summary>A felled plant only regrows while no player is this close.</summary>
    public float RegrowBlockRadius { get; set; } = 70f;
    public float BladeDps { get; set; } = 110f;
    public float BladeCreatureDps { get; set; } = 160f;
}

/// <summary>
/// Where and when wildlife may appear. No creature spawns or respawns within
/// <see cref="BaseClearance"/> of any base (a structure whose core is in base mode), measured
/// from the structure's footprint edge, so bigger bases get bigger zones. Creatures already
/// outside may still wander in, and nothing here stops anything reaching a base. Respawns also
/// keep away from players and out of their view, so nothing appears where anyone can see it.
/// </summary>
public sealed class SpawnRules
{
    /// <summary>The share of a species' natural groups a world holds at its most livable (the world's livability scales it down).</summary>
    public float Density { get; set; } = 0.5f;
    /// <summary>Metres from a base's footprint edge inside which nothing spawns or respawns.</summary>
    public float BaseClearance { get; set; } = 80f;
    /// <summary>How long a species that has fallen short waits before a group comes back.</summary>
    public float RespawnSeconds { get; set; } = 90f;
    /// <summary>How often the spawner counts heads.</summary>
    public float CheckSeconds { get; set; } = 5f;
    /// <summary>Respawns land at least this far from every player.</summary>
    public float MinPlayerDistance { get; set; } = 90f;
    /// <summary>Respawns within this distance of a player must also be outside their view cone.</summary>
    public float ViewDistance { get; set; } = 220f;
    public float ViewConeDegrees { get; set; } = 65f;
    /// <summary>Nobody within this of where they were this many seconds ago sees anything come back.</summary>
    public float StayPutRadius { get; set; } = 30f;
    public float StayPutSeconds { get; set; } = 60f;
    /// <summary>Cracks don't let anything out within this of a player, or in their line of sight within <see cref="ViewDistance"/>.</summary>
    public float ColonyClearance { get; set; } = 25f;
    /// <summary>The nearest a cave's creatures are put to someone already there.</summary>
    public float CaveClearance { get; set; } = 12f;
    /// <summary>Spots tried for one group before it waits for the next check.</summary>
    public int Attempts { get; set; } = 24;

    /// <summary>Whole groups for a wanted number of them: the fraction left over is a roll.</summary>
    public static int Groups(float wanted, Rng rng)
    {
        int n = (int)wanted;
        return n + (rng.Chance(wanted - n) ? 1 : 0);
    }
}

public sealed class WorldRules
{
    public float DayLengthSeconds { get; set; } = 360f;
    public float NightStart { get; set; } = 0.78f;
    public float NightEnd { get; set; } = 0.22f;
}

public sealed class FaunaTables
{
    public static readonly string[] Slots = { "head", "horns", "back", "tail", "limbs", "tendrils" };

    public int[] SpeciesCount { get; set; } = { 6, 10 };
    /// <summary>The first picks on a planet never repeat a body plan, so every world has range.</summary>
    public int DistinctPlans { get; set; } = 5;
    /// <summary>Chance of an off-script behaviour or temperament, at alienness 0 and 1.</summary>
    public float[] Surprise { get; set; } = { 0.04f, 0.35f };
    /// <summary>How sharply a world's alienness prefers forms and parts of matching weirdness.</summary>
    public float WeirdnessFocus { get; set; } = 0.32f;
    /// <summary>
    /// How far past a form's proportion ranges an alien world may push (as a share of the range,
    /// below and above, at alienness 1). Small on purpose: past this, bodies stop reading as
    /// animals and start reading as broken.
    /// </summary>
    public float[] ProportionPush { get; set; } = { 0.15f, 0.35f };
    public List<FormDef> Forms { get; set; } = new();
    public Dictionary<string, List<PartDef>> Parts { get; set; } = new();
    public List<PatternDef> Patterns { get; set; } = new();
    public Dictionary<string, BehaviourDef> Behaviours { get; set; } = new();
    public Dictionary<string, TemperamentDef> Temperaments { get; set; } = new();
    public PackRules Pack { get; set; } = new();
    public PaletteRules Palette { get; set; } = new();
    public CorruptionRules Corruption { get; set; } = new();
    public SyllableTables Syllables { get; set; } = new();
    public WeirdnessWeights Weirdness { get; set; } = new();

    public PartDef FindPart(string slot, string id)
    {
        if (!Parts.TryGetValue(slot, out var list)) return null;
        foreach (var part in list) if (part.Id == id) return part;
        return null;
    }

    /// <summary>A part by "slot/id", as the combination rules name them.</summary>
    public PartDef FindPart(string slotAndId)
    {
        int cut = slotAndId.IndexOf('/');
        return cut < 0 ? null : FindPart(slotAndId[..cut], slotAndId[(cut + 1)..]);
    }

    public BehaviourDef Behaviour(Behaviour b) => Behaviours[b.ToString()];
    public TemperamentDef Temperament(Temperament t) => Temperaments.TryGetValue(t.ToString(), out var d) ? d : new TemperamentDef();
}

/// <summary>A body plan variant: which rig it uses and the ranges its proportions roll in.</summary>
public sealed class FormDef
{
    public string Id { get; set; } = "";
    public BodyPlan Plan { get; set; }
    public RigKind Rig { get; set; }
    public float Weight { get; set; } = 1f;
    public float Weirdness { get; set; }
    public string Noun { get; set; } = "creature";
    /// <summary>Body length in metres.</summary>
    public float[] Size { get; set; } = { 1f, 2f };
    public int[] Legs { get; set; } = { 0, 0 };
    public LegStyle[] LegStyles { get; set; } = { LegStyle.Upright };
    // Proportions as multiples of body length.
    public float[] LegLength { get; set; } = { 0.5f, 0.8f };
    public float[] Neck { get; set; } = { 0.2f, 0.6f };
    public float[] Tail { get; set; } = { 0.3f, 0.8f };
    public float[] Width { get; set; } = { 0.35f, 0.5f };
    public float[] Height { get; set; } = { 0.35f, 0.5f };
    public float[] HeadSize { get; set; } = { 0.26f, 0.38f };
    public int[] Segments { get; set; } = { 0, 0 };
    public int[] Wings { get; set; } = { 0, 0 };
    public int[] Bags { get; set; } = { 0, 0 };
    public int[] Members { get; set; } = { 0, 0 };
    public bool Burrows { get; set; }
    public bool Flies { get; set; }
    public bool Tumbles { get; set; }
    /// <summary>Walking plants: put down roots by day.</summary>
    public bool Rooted { get; set; }
    /// <summary>
    /// A form of caves, tunnels and places, never rolled for the open surface (the deep fauna
    /// generator picks these by role).
    /// </summary>
    public bool Deep { get; set; }
    /// <summary>Clings to walls and ceilings as readily as floors.</summary>
    public bool Climbs { get; set; }
    /// <summary>Where it rests outside its hours (Den, Burrow, Roost, Nest); empty: worked out from its body and job.</summary>
    public string Rest { get; set; } = "";
    public float SpeedScale { get; set; } = 1f;
    public float HealthScale { get; set; } = 1f;
    /// <summary>Behaviour name to weight.</summary>
    public Dictionary<string, float> Behaviours { get; set; } = new();
    /// <summary>Slot to part id, for forms that only make sense one way (a boulder has no head).</summary>
    public Dictionary<string, string> FixedParts { get; set; } = new();
}

/// <summary>
/// One swappable part. Besides which rigs it fits, a part carries combination rules, so the
/// generator can't assemble something broken or silly: <see cref="Roles"/> limits it to the
/// behaviours it makes sense on (antlers on grazers, scythes on hunters), and
/// <see cref="Excludes"/> and <see cref="Requires"/> rule out pairings that clash (horns on a
/// lamprey's mouth, feelers on top of antennae).
/// </summary>
public sealed class PartDef
{
    public string Id { get; set; } = "none";
    public float Weirdness { get; set; }
    /// <summary>Rigs this part can be rolled on; empty means any.</summary>
    public RigKind[] Rigs { get; set; } = Array.Empty<RigKind>();
    /// <summary>Behaviours this part can be rolled for; empty means any.</summary>
    public string[] Roles { get; set; } = Array.Empty<string>();
    /// <summary>Parts ("slot/id") it never appears with, whichever is rolled first.</summary>
    public string[] Excludes { get; set; } = Array.Empty<string>();
    /// <summary>Parts ("slot/id") of which at least one must already be on the creature.</summary>
    public string[] Requires { get; set; } = Array.Empty<string>();
    /// <summary>Only on a body with a real neck (two heads on no neck is a mess).</summary>
    public bool NeedsNeck { get; set; }
    /// <summary>Forms it belongs on; empty means any. Insect heads go on insect bodies.</summary>
    public string[] Forms { get; set; } = Array.Empty<string>();
    /// <summary>Only ever set by a form's fixed parts, never rolled.</summary>
    public bool FixedOnly { get; set; }
    public string Adjective { get; set; } = "";
    public bool Glow { get; set; }
    public RareDef Rare { get; set; }

    public bool Fits(RigKind rig) => !FixedOnly && (Rigs.Length == 0 || Array.IndexOf(Rigs, rig) >= 0);

    public bool Suits(Behaviour b) => Roles.Length == 0 || Array.IndexOf(Roles, b.ToString()) >= 0;

    public bool SuitsForm(string form) => Forms.Length == 0 || Array.IndexOf(Forms, form) >= 0;
}

public sealed class RareDef
{
    public string Name { get; set; } = "";
    public float Chance { get; set; }
    public int Value { get; set; }
}

public sealed class PatternDef
{
    public Pattern Id { get; set; }
    public float Weirdness { get; set; }
}

/// <summary>
/// A behaviour's signature attack. Every attacking creature opens with it, telegraphed: the
/// wind-up is at least <see cref="Telegraph"/> seconds long and shows and sounds like the
/// style (a pounce crouches, a charge lowers its head, a giant rears and roars).
/// </summary>
public sealed class AttackDef
{
    public string Name { get; set; } = "";
    public AttackStyle Style { get; set; } = AttackStyle.Lunge;
    /// <summary>Wind-up of the first, telegraphed attack in seconds.</summary>
    public float Telegraph { get; set; } = 1.1f;
    /// <summary>How far past its reach the lunge carries, in metres; 0 uses the combat default.</summary>
    public float Reach { get; set; }
    /// <summary>How long the lunge itself takes; 0 uses the combat default.</summary>
    public float Lunge { get; set; }
    /// <summary>Which synthesised wind-up sound it makes: growl, snarl, roar, bellow, hiss, chitter, buzz, grunt.</summary>
    public string Sound { get; set; } = "growl";

    public AttackDef With(AttackStyle style, string name) => new()
    {
        Name = name, Style = style, Telegraph = Telegraph, Reach = Reach, Lunge = Lunge, Sound = Sound,
    };
}

public sealed class BehaviourDef
{
    public int[] Group { get; set; } = { 1, 1 };
    public float Perception { get; set; } = 24f;
    /// <summary>Damage of a normal hit, against a player with 100 health, before size scaling.</summary>
    public float Damage { get; set; }
    public float Speed { get; set; } = 1f;
    public float Territory { get; set; } = 30f;
    /// <summary>Ambushers strike at anything that comes this close at night.</summary>
    public float AmbushRadius { get; set; } = 7f;
    public AttackDef Attack { get; set; } = new();
    public Dictionary<string, float> Temperaments { get; set; } = new();
    /// <summary>Activity cycle name (Diurnal, Nocturnal, Crepuscular, Cathemeral) to weight: when this kind of animal is out.</summary>
    public Dictionary<string, float> Activity { get; set; } = new();
}

/// <summary>
/// How packs hunt: stalk together, spread round the prey, one feints at the front while the
/// others close from the flanks, and break off together when the leader falls or too many
/// are hurt.
/// </summary>
public sealed class PackRules
{
    /// <summary>Chance a planet's second species is a pack rather than a lone predator.</summary>
    public float RoleChance { get; set; } = 0.6f;
    /// <summary>The pack starts its approach (spreading out) inside this distance of the prey.</summary>
    public float SurroundRadius { get; set; } = 20f;
    /// <summary>How far from the prey the members circle while they take up their places.</summary>
    public float RingRadius { get; set; } = 7.5f;
    /// <summary>Flankers take places this far either side of the feinter, in degrees.</summary>
    public float FlankAngle { get; set; } = 110f;
    /// <summary>Longest they spend getting into place before closing in anyway.</summary>
    public float SurroundSeconds { get; set; } = 7f;
    /// <summary>A member counts as hurt below this share of its health.</summary>
    public float HurtBelow { get; set; } = 0.7f;
    /// <summary>The pack breaks off when this share of it is hurt (or dead), or the leader dies.</summary>
    public float RetreatShare { get; set; } = 0.5f;
    /// <summary>How long a retreat runs before the pack regroups, and how long before it hunts again.</summary>
    public float RetreatSeconds { get; set; } = 8f;
    public float RegroupSeconds { get; set; } = 25f;
    /// <summary>How far apart members keep while roaming.</summary>
    public float Spacing { get; set; } = 2.4f;
}

public sealed class TemperamentDef
{
    /// <summary>Runs from players closer than this.</summary>
    public float Flee { get; set; } = 10f;
    /// <summary>Fights back when hurt.</summary>
    public bool Retaliates { get; set; }
    /// <summary>Goes after players unprovoked, even in a herd.</summary>
    public bool Hunts { get; set; }
}

public sealed class PaletteRules
{
    /// <summary>Hue ranges in degrees for near-familiar animals: ochres, browns, olives, slate.</summary>
    public float[][] FamiliarHues { get; set; } = { new[] { 18f, 48f } };
    public float[] FamiliarSaturation { get; set; } = { 0.3f, 0.6f };
    public float[] FamiliarValue { get; set; } = { 0.35f, 0.7f };
    public float[] AlienSaturation { get; set; } = { 0.55f, 0.95f };
    public float[] AlienValue { get; set; } = { 0.45f, 0.9f };
    public string[] GlowColors { get; set; } = { "#7cf9ff" };
    /// <summary>Chance of glowing parts at alienness 0 and 1.</summary>
    public float[] GlowChance { get; set; } = { 0.08f, 0.85f };
}

public sealed class CorruptionRules
{
    public string Prefix { get; set; } = "Hollow";
    public string Base { get; set; } = "#2a1230";
    public string Veins { get; set; } = "#ff3cac";
    public string Glow { get; set; } = "#ff4fd8";
    /// <summary>How far the body colour is pulled towards the Bloom's.</summary>
    public float Mix { get; set; } = 0.6f;
    public float WeirdnessBonus { get; set; } = 0.25f;
    public float HealthScale { get; set; } = 1.3f;
    public float DamageScale { get; set; } = 1.3f;
    public float SpeedScale { get; set; } = 1.1f;
    public RareDef Rare { get; set; } = new() { Name = "Hollow node", Chance = 0.5f, Value = 220 };
}

public sealed class SyllableTables
{
    public string[] Onsets { get; set; } = { "k", "v", "sh", "m", "l", "r", "t" };
    public string[] Vowels { get; set; } = { "a", "e", "i", "o", "u" };
    public string[] Codas { get; set; } = { "n", "r", "k" };
    public int OnsetsPerPlanet { get; set; } = 7;
    public int VowelsPerPlanet { get; set; } = 4;
    public int CodasPerPlanet { get; set; } = 4;
    public float CodaChance { get; set; } = 0.45f;
    public int[] Count { get; set; } = { 2, 3 };
}

/// <summary>How much each feature counts towards a species' weirdness score (they sum to 1).</summary>
public sealed class WeirdnessWeights
{
    public float Form { get; set; } = 0.3f;
    public float Parts { get; set; } = 0.35f;
    public float Proportions { get; set; } = 0.15f;
    public float Colour { get; set; } = 0.1f;
    public float Pattern { get; set; } = 0.1f;
}
