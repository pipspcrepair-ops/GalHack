using System;
using System.Collections.Generic;
using System.Globalization;

namespace Wildlife;

/// <summary>
/// A community's culture as its people show it (docs/research/culture.md): the traits its values add up
/// to, which sex is side A, and which side each role group falls to. Rolled with the world's layers
/// (one for the world, one per town and faction); this is the plain data the people generator and the
/// towns read. Nothing here names a value: an empty culture is today's behaviour.
/// </summary>
public sealed class Culture
{
    public static readonly Culture Default = new();

    /// <summary>Axis to value, for readouts and tests only: nothing decides by these.</summary>
    public readonly Dictionary<string, string> Values = new();
    public readonly Dictionary<string, string> Traits = new();
    public Sex Lead = Sex.Female;
    /// <summary>Each role group's side ("fight", "trade", "make", "care", "lead", and "hot"): true for side A.</summary>
    public readonly Dictionary<string, bool> SideA = new();
    /// <summary>Who wears the decency piece: "all", "a" or "b".</summary>
    public string CoverFor = "all";
    /// <summary>How desperate the world is, 0 to 1: it arms the young.</summary>
    public float Desperation;
    public ulong Seed;

    public float Trait(string key, float otherwise = 0f) => Traits.TryGetValue(key, out string v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : otherwise;
    public string Word(string key) => Traits.GetValueOrDefault(key) ?? "";
    public bool Has(string key) => Trait(key) > 0f;

    public override string ToString() => string.Join(" ", Values.Values) + $" lead {Lead} desperation {Desperation:F2}";

    /// <summary>The role group a job belongs to (empty: none, as chance gives).</summary>
    public static string GroupOf(PersonRole role) => role switch
    {
        PersonRole.Guard or PersonRole.Lawman or PersonRole.Raider => "fight",
        PersonRole.Trader => "trade",
        PersonRole.Worker or PersonRole.Mechanic or PersonRole.Farmer => "make",
        PersonRole.Medic => "care",
        PersonRole.Elder => "lead",
        _ => "",
    };

    public static Sex Other(Sex s) => s == Sex.Female ? Sex.Male : Sex.Female;

    /// <summary>Whether a sex is side A here.</summary>
    public bool A(Sex s) => s == Lead;

    /// <summary>The sex of a side.</summary>
    public Sex SexOf(bool a) => a ? Lead : Other(Lead);

    /// <summary>Whether a group is side A (rolled per world; a group nobody rolled is A).</summary>
    public bool GroupA(string group) => !SideA.TryGetValue(group, out bool a) || a;

    /// <summary>
    /// The sex of someone doing a job: half and half where the culture says nothing, else the job's group
    /// mostly from its side by the culture's share. One draw, whatever the culture (today's rolls stand).
    /// </summary>
    public Sex SexFor(PersonRole role, float draw)
    {
        string g = GroupOf(role);
        float share = Trait("share", 0.5f);
        if (g == "" || share <= 0.5f) return draw < 0.5f ? Sex.Female : Sex.Male;
        Sex side = SexOf(GroupA(g));
        return draw < share ? side : Other(side);
    }

    /// <summary>The caste a job falls in (0 the highest; 0 where the culture has none).</summary>
    public int CasteOf(PersonRole role) => Trait("castes", 1f) <= 1f ? 0 : (int)Trait("c_" + (GroupOf(role) is { Length: > 0 } g ? g : "make"));

    /// <summary>Two people who may chat outdoors: mixed sexes as the culture allows, castes mostly among their own.</summary>
    public float MixChance(Sex a, Sex b, int casteA, int casteB, bool atWork)
    {
        float k = a == b ? 1f : Trait(atWork ? "crew" : "mix", 1f);
        if (casteA != casteB) k *= 1f - Trait("same_caste");
        return k;
    }

    /// <summary>
    /// The chance someone here carries a weapon: a fighter by the culture's say (the law by its own), any other
    /// adult by their side's, and a child by the culture's base and how desperate the world is, scaled by its arms.
    /// </summary>
    public float ArmedChance(PersonRole role, Sex sex, bool fighter, bool child)
    {
        if (child) return Math.Clamp(Trait("kids") + Trait("young_kids") + 0.4f * Desperation * Trait("kids_k", 1f), 0f, 0.9f);
        if (fighter) return role == PersonRole.Lawman && Traits.ContainsKey("law") ? Trait("law") : Trait("fight", 1f);
        string side = A(sex) ? "armed_a" : "armed_b";
        return Traits.ContainsKey(side) ? Trait(side) : Trait("armed");
    }

    /// <summary>The chance someone here runs hot (bold or aggressive): their side's, the young's, or everyone's.</summary>
    public float HotChance(Sex sex, float age)
    {
        float hot = Trait("hot");
        if (Traits.ContainsKey("hot_a")) hot = A(sex) == GroupA("hot") ? Trait("hot_a") : Trait("hot_b");
        if (age >= 16f && age <= 25f) hot = MathF.Max(hot, Trait("hot_young"));
        return hot;
    }

    /// <summary>Whether this person wears the decency piece: adults, of the side it's for.</summary>
    public bool Covers(Sex sex, bool child) => !child && Word("wear") != "" && (CoverFor == "all" || CoverFor == "a" == A(sex));
}
