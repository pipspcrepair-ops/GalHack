using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>Rolling one person (or a row of them) from the people generator, as a world rolls its own.</summary>
static class ZooPeople
{
    public static readonly string[] Cultures = { "Human", "Mixed world", "Alien folk", "Vharn" };
    public static readonly string[] Coverings = { "skin", "fur", "feathers", "scales", "chitin" };
    public static readonly string[] Humidities = { "Mild", "Dry", "Humid", "Rainy" };
    public static float HumidityOf(string v) => v switch { "Dry" => 0.2f, "Humid" => 0.7f, "Rainy" => 0.9f, _ => 0.5f };
    public static readonly string[] Occupancies = { "Friendly", "Raider", "Feral" };

    /// <summary>The stories a world can have lived through (falls and themes that dress its people), from the data.</summary>
    public static string[] Stories(WildlifeData d)
    {
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string k in d.Signature.Folk.Keys) keys.Add(k);
        foreach (string k in d.Signature.Themes.Keys) keys.Add(k);
        var list = new List<string> { "None" };
        list.AddRange(keys);
        return list.ToArray();
    }

    /// <summary>Everything a person can carry: what the roles carry, what each era arms its fighters with, and the kinds' own.</summary>
    public static string[] Props(WildlifeData d)
    {
        var props = new SortedSet<string>(StringComparer.Ordinal);
        foreach (RoleLook l in d.People.Roles.Values) if (l.Prop != "") props.Add(l.Prop);
        foreach (RoleLook l in d.Signature.Armed.Values) if (l.Prop != "") props.Add(l.Prop);
        foreach (RoleLook l in d.Signature.Folk.Values) if (l.Prop != "") props.Add(l.Prop);
        foreach (SignatureKind k in d.Signature.Kinds.Values) if (k.Look.Prop != "") props.Add(k.Look.Prop);
        var rng = new Rng(1);
        for (int era = 0; era < 3; era++)
            for (int i = 0; i < 400; i++) props.Add(Weapons.ForEra(era, era, rng));
        return props.ToArray();
    }

    /// <summary>The world these people live in: its climate, tech, story and mix of folk. Culture bends the mix.</summary>
    public static WorldLife World(ulong seed, string culture, string climate, int tech, string story, out PeopleGenerator people)
    {
        WildlifeData data = Zoo.Data;
        WorldConditions w = Zoo.World(seed, culture == "Human" ? 0.1f : culture == "Mixed world" ? 0.5f : 0.85f, climate, tech);
        if (story != "None" && story != "")
        {
            w.Fall = story;
            w.Theme = story;
        }
        WorldLife life = Wildlife.Bestiary.Roll(data, w);
        people = life.People;
        if (culture == "Human") Force(people, new Folk());
        else if (culture == "Alien folk") Force(people, Peoples.Roll(Rng.For(seed, "culture"), w, "alien"));
        return life;
    }

    public static void Force(PeopleGenerator people, Folk f)
    {
        people.Folks.Clear();
        people.Folks.Add((f, 1f));
    }

    /// <summary>One person: their folk, climate dress, era, story, standing and what they hold.</summary>
    public static Species Person(ulong seed, PersonRole role, string culture, string climate, string humidity, int tech, string story, string occupancy, float mutation, string held, bool husk, Folk folk = null)
    {
        WorldLife life = World(seed, culture, climate, tech, story, out PeopleGenerator people);
        if (folk != null) Force(people, folk);
        bool vharn = culture == "Vharn";
        Founders founders = people.FoundersOf(seed, false, vharn);
        founders.Locale = new Locale { TempC = Zoo.TempOf(climate), Humidity = HumidityOf(humidity) };
        Enum.TryParse(occupancy, out Occupancy occ);
        Species s = people.Person(seed, role, occ, mutation, husk, false, founders);
        if (!husk && held != "As dressed") s.Figure.Prop = held == "Nothing" ? "" : held;
        return s;
    }
}
