using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Wildlife;

// The space fauna's tables live in data/space_fauna.json; these classes give the file its
// shape. Parts reuse the planet fauna's PartDef, attacks its AttackDef, names its syllables.

public sealed class SpaceFaunaData
{
    /// <summary>The design's rarity ladder: which materials sit on each step.</summary>
    public Dictionary<Rarity, string[]> Ladder { get; set; } = new();
    public List<SpaceFormDef> Forms { get; set; } = new();
    /// <summary>Slot to swappable parts. Slots: head, fins, back, tail, tendrils.</summary>
    public Dictionary<string, List<PartDef>> Parts { get; set; } = new();
    public SyllableTables Syllables { get; set; } = new();
    /// <summary>The longest a unique drop's chance may be: unique things only turn up sometimes.</summary>
    public float MaxUniqueChance { get; set; } = 0.25f;

    public static readonly string[] Slots = { "head", "fins", "back", "tail", "tendrils" };

    static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static SpaceFaunaData FromJson(string json)
    {
        var data = JsonSerializer.Deserialize<SpaceFaunaData>(json, Options) ?? throw new InvalidOperationException("empty space fauna data");
        var problems = data.Validate();
        if (problems.Count > 0) throw new InvalidOperationException("space fauna data: " + string.Join("; ", problems));
        return data;
    }

    public PartDef FindPart(string slot, string id)
    {
        if (!Parts.TryGetValue(slot, out var list)) return null;
        foreach (PartDef p in list) if (p.Id == id) return p;
        return null;
    }

    public SpaceFormDef Form(string id) => Forms.Find(f => f.Id == id);

    /// <summary>The rarity a material sits at on the ladder, or null if it isn't on it.</summary>
    public Rarity? RarityOf(string material)
    {
        foreach (var (rarity, materials) in Ladder)
            if (Array.IndexOf(materials, material) >= 0) return rarity;
        return null;
    }

    public List<string> Validate()
    {
        var problems = new List<string>();
        if (Forms.Count == 0) problems.Add("no forms");
        foreach (string slot in Slots)
            if (!Parts.ContainsKey(slot)) problems.Add($"no parts for slot {slot}");
        foreach (SpaceFormDef f in Forms)
        {
            string at = $"form {f.Id}";
            if (f.Habitats.Length == 0 || Array.IndexOf(f.Habitats, Habitat.Surface) >= 0) problems.Add($"{at}: needs space habitats");
            if (f.Rig is not (RigKind.Finned or RigKind.Clinger)) problems.Add($"{at}: space fauna use the Finned or Clinger rig");
            if (f.Rig == RigKind.Finned && f.Swim == SwimStyle.None) problems.Add($"{at}: a swimmer needs a swim style");
            if (f.Size.Length != 2 || f.Size[0] <= 0f || f.Size[0] > f.Size[1]) problems.Add($"{at}: size must be [min, max]");
            if (f.Damage > 0f && (f.Attack == null || f.Attack.Name == "" || f.Attack.Telegraph < 0.6f)) problems.Add($"{at}: an attacker needs a signature attack with a telegraph of at least 0.6 s");
            foreach (var (slot, part) in f.FixedParts)
                if (FindPart(slot, part) == null) problems.Add($"{at}: unknown fixed part {slot}/{part}");
            // Space creatures only ever drop rare and better, always something, unique only sometimes.
            bool guaranteed = false;
            foreach (MaterialDrop d in f.Drops)
            {
                Rarity? onLadder = RarityOf(d.Material);
                if (onLadder == null) problems.Add($"{at}: {d.Material} is not on the rarity ladder");
                else if (onLadder != d.Rarity) problems.Add($"{at}: {d.Material} is {onLadder} on the ladder, not {d.Rarity}");
                if (d.Rarity < Rarity.Rare) problems.Add($"{at}: drops {d.Material} ({d.Rarity}); space fauna drop rare and better only");
                if (d.Rarity == Rarity.Unique && d.Chance > MaxUniqueChance) problems.Add($"{at}: unique {d.Material} at {d.Chance:P0} is more than sometimes");
                if (d.Amount.Length != 2 || d.Amount[0] < 1 || d.Amount[0] > d.Amount[1]) problems.Add($"{at}: {d.Material} amount must be [min, max] >= 1");
                guaranteed |= d.Chance >= 1f;
            }
            if (!guaranteed) problems.Add($"{at}: every kill must drop something (one drop at chance 1)");
        }
        foreach (var (slot, list) in Parts)
            foreach (PartDef p in list)
            {
                foreach (string role in p.Roles)
                    if (!Enum.TryParse<Behaviour>(role, out _)) problems.Add($"part {slot}/{p.Id}: unknown role {role}");
                foreach (string form in p.Forms)
                    if (Form(form) == null) problems.Add($"part {slot}/{p.Id}: unknown form {form}");
                foreach (string other in p.Excludes)
                    if (Find(other) == null) problems.Add($"part {slot}/{p.Id}: excludes unknown part {other}");
            }
        return problems;
    }

    PartDef Find(string slotAndId)
    {
        int cut = slotAndId.IndexOf('/');
        return cut < 0 ? null : FindPart(slotAndId[..cut], slotAndId[(cut + 1)..]);
    }
}

/// <summary>
/// One kind of space creature: where it lives, how it moves, what it does, the ranges its
/// body rolls in, its signature attack and what it drops. Lengths are metres; proportions are
/// multiples of body length.
/// </summary>
public sealed class SpaceFormDef
{
    public string Id { get; set; } = "";
    public string Noun { get; set; } = "creature";
    public Habitat[] Habitats { get; set; } = Array.Empty<Habitat>();
    public RigKind Rig { get; set; } = RigKind.Finned;
    public SwimStyle Swim { get; set; }
    public Behaviour Behaviour { get; set; } = Behaviour.Herd;
    public Dictionary<string, float> Temperaments { get; set; } = new();
    public float[] Size { get; set; } = { 2f, 4f };
    public float[] Width { get; set; } = { 0.4f, 0.6f };
    public float[] Height { get; set; } = { 0.3f, 0.5f };
    public float[] HeadSize { get; set; } = { 0.15f, 0.25f };
    public float[] Tail { get; set; } = { 0.5f, 1f };
    public int[] Segments { get; set; } = { 1, 1 };
    public int[] Group { get; set; } = { 1, 1 };
    /// <summary>Cruising and running speed in m/s.</summary>
    public float[] Speed { get; set; } = { 2f, 6f };
    /// <summary>Health and damage across the size range (smallest to largest).</summary>
    public float[] Health { get; set; } = { 50f, 80f };
    public float Damage { get; set; }
    public float Perception { get; set; } = 40f;
    /// <summary>Intruders inside this distance of a territorial giant's home are driven off.</summary>
    public float Territory { get; set; } = 0f;
    public AttackDef Attack { get; set; } = new();
    public Dictionary<string, string> FixedParts { get; set; } = new();
    public List<MaterialDrop> Drops { get; set; } = new();
    /// <summary>The colours its kind comes in: hue ranges (degrees), saturation and value, and its glows.</summary>
    public SpacePalette Palette { get; set; } = new();
}

public sealed class SpacePalette
{
    public float[][] Hues { get; set; } = { new[] { 200f, 260f } };
    public float[] Saturation { get; set; } = { 0.3f, 0.6f };
    public float[] Value { get; set; } = { 0.2f, 0.45f };
    public string[] Glow { get; set; } = { "#7cf9ff" };
    public float[] GlowStrength { get; set; } = { 0.6f, 1f };
    public Pattern[] Patterns { get; set; } = { Pattern.Plain };
}
