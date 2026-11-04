using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wildlife;

// Every tunable for fauna, combat, harvesting and the clock lives in data/wildlife.json.
// These classes only give the file a shape (its schema); the defaults here are a fallback
// so a missing field never crashes the game, and Validate() reports what looks wrong.

public enum BodyPlan { Walker, Crawler, Hopper, Flier, Floater, Swarm, Giant, Oddity }

/// <summary>The small set of reliable rigs every body plan is drawn and animated with.</summary>
public enum RigKind { Legged, Chain, Hopper, Winged, GasBag, Swarm }

public enum Behaviour { Grazer, Herd, Predator, Ambusher, Scavenger, Swarm, TerritorialGiant, HarmlessOddity }

/// <summary>How a creature reacts to you, independent of what it does all day. Odd pairs make surprises.</summary>
public enum Temperament { Skittish, Calm, Bold, Aggressive }

public enum LegStyle { Upright, Splayed, Stilt, Roots }

public enum Pattern { Plain, Countershade, Spots, Stripes, Bands, Rings, Checker, Veins }

public sealed class WildlifeData
{
    public CombatRules Combat { get; set; } = new();
    public HarvestRules Harvest { get; set; } = new();
    public WorldRules World { get; set; } = new();
    public FaunaTables Fauna { get; set; } = new();

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
        foreach (Behaviour b in Enum.GetValues<Behaviour>())
            if (!Fauna.Behaviours.ContainsKey(b.ToString())) problems.Add($"no behaviour entry for {b}");
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
    public List<FormDef> Forms { get; set; } = new();
    public Dictionary<string, List<PartDef>> Parts { get; set; } = new();
    public List<PatternDef> Patterns { get; set; } = new();
    public Dictionary<string, BehaviourDef> Behaviours { get; set; } = new();
    public Dictionary<string, TemperamentDef> Temperaments { get; set; } = new();
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
    public float SpeedScale { get; set; } = 1f;
    public float HealthScale { get; set; } = 1f;
    /// <summary>Behaviour name to weight.</summary>
    public Dictionary<string, float> Behaviours { get; set; } = new();
    /// <summary>Slot to part id, for forms that only make sense one way (a boulder has no head).</summary>
    public Dictionary<string, string> FixedParts { get; set; } = new();
}

public sealed class PartDef
{
    public string Id { get; set; } = "none";
    public float Weirdness { get; set; }
    /// <summary>Rigs this part can be rolled on; empty means any.</summary>
    public RigKind[] Rigs { get; set; } = Array.Empty<RigKind>();
    /// <summary>Only ever set by a form's fixed parts, never rolled.</summary>
    public bool FixedOnly { get; set; }
    public string Adjective { get; set; } = "";
    public bool Glow { get; set; }
    public RareDef Rare { get; set; }

    public bool Fits(RigKind rig) => !FixedOnly && (Rigs.Length == 0 || Array.IndexOf(Rigs, rig) >= 0);
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
    /// <summary>Wind-up of the first, telegraphed attack in seconds; 0 uses the combat default.</summary>
    public float Telegraph { get; set; }
    public Dictionary<string, float> Temperaments { get; set; } = new();
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
