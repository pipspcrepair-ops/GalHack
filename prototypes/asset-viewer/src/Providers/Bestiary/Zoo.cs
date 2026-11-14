using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// The game's data tables and the roll of one species from them, shared by the Bestiary and People
/// providers. Everything here asks the generators; nothing is a list of its own.
/// </summary>
static class Zoo
{
    static WildlifeData _data;
    static SpaceFaunaData _space;

    public static WildlifeData Data => _data ??= DataFiles.LoadWildlife();
    public static SpaceFaunaData Space => _space ??= DataFiles.LoadSpace();

    public static readonly string[] Alienness = { "Familiar", "Strange", "Alien" };
    public static float AliennessOf(string v) => v switch { "Strange" => 0.5f, "Alien" => 0.95f, _ => 0.1f };

    public static readonly string[] Climates = { "Mild", "Frozen", "Cold", "Hot", "Scorching" };
    public static float TempOf(string climate) => climate switch { "Frozen" => -25f, "Cold" => -2f, "Hot" => 34f, "Scorching" => 50f, _ => 15f };

    public static readonly string[] Mutations = { "None", "Half", "Full" };
    public static float MutationOf(string v) => v switch { "Half" => 0.5f, "Full" => 1f, _ => 0f };

    public static readonly string[] Eras = { "Mid", "Low", "High" };
    public static int EraOf(string v) => v switch { "Low" => 0, "High" => 2, _ => 1 };

    /// <summary>A world with the conditions an option asked for.</summary>
    public static WorldConditions World(ulong seed, float alienness, string climate = "Mild", int tech = 1, float mutagen = 0f, bool bloom = false)
    {
        return new WorldConditions
        {
            Seed = seed, Alienness = alienness, TempC = TempOf(climate), Bloom = bloom, Mutagen = mutagen,
            Before = tech, Now = tech, Peopled = true,
        };
    }

    public static string Title(string id)
    {
        if (string.IsNullOrEmpty(id)) return id;
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < id.Length; i++)
        {
            char c = id[i];
            if (c == '_' || c == '-') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(id[i - 1]) && id[i - 1] != ' ') sb.Append(' ');
            sb.Append(i == 0 ? char.ToUpperInvariant(c) : c);
        }
        return sb.ToString();
    }

    public static string PlanLabel(BodyPlan p) => p switch
    {
        BodyPlan.Walker => "Walkers", BodyPlan.Crawler => "Crawlers and slitherers", BodyPlan.Hopper => "Leapers", BodyPlan.Flier => "Fliers",
        BodyPlan.Floater => "Floaters", BodyPlan.Swarm => "Swarms", BodyPlan.Giant => "Giants", BodyPlan.Oddity => "Oddities", _ => "Voidborne",
    };

    static readonly MethodInfo RollActivity = typeof(FaunaGenerator).GetMethod("RollActivity", BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>
    /// A species of a form, rolled exactly as a planet rolls its own, then fitted to a climate, with
    /// the sex, young and Hollow growth a world gives it.
    /// </summary>
    public static Species Surface(string formId, ulong seed, Behaviour behaviour, float alienness, string climate, float mutation, bool hollow, bool young, bool otherSex)
    {
        WildlifeData data = Data;
        var gen = new FaunaGenerator(data.Fauna, data.Activity);
        Species s = gen.RollOne(seed, formId, behaviour, alienness);
        s.Id = $"{seed:x}-{formId}";
        s.Name = new NameGen(seed, data.Fauna.Syllables).Make(Rng.For(seed, "name"));
        s.CommonName = gen.NameFor(s);
        WorldConditions w = World(seed, alienness, climate);
        Fit.Apply(data.Fit, w, s, Rng.For(s.Seed, "fit"));
        Fit.Throat(s, Rng.For(s.Seed, "throat"));
        Fit.Dimorph(s, Rng.For(s.Seed, "dimorph"));
        try { RollActivity?.Invoke(gen, new object[] { s }); } catch (Exception) { /* activity stays any hour */ }
        if (hollow) gen.CorruptSpecies(s, seed);
        if (young && s.Behaviour == Behaviour.Herd)
        {
            Species y = Fit.Young(s, Rng.For(s.Seed, "young"));
            if (y != null) s = y;
        }
        if (otherSex && s.Mate != null) s = s.Mate;
        if (mutation > 0f) s = gen.Mutate(s, mutation);
        return s;
    }

    /// <summary>Behaviours a form is rolled with, most likely first.</summary>
    public static string[] BehavioursOf(FormDef f) =>
        f.Behaviours.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key).ToArray();

    public static Behaviour BehaviourOf(string name) => Enum.TryParse(name, out Behaviour b) ? b : Behaviour.Grazer;

    /// <summary>Short facts about a species for the viewer's list.</summary>
    public static IEnumerable<(string, string)> Facts(Species s)
    {
        yield return ("Common name", s.CommonName);
        if (!string.IsNullOrEmpty(s.Name) && s.Name != s.CommonName) yield return ("Proper name", s.Name);
        yield return ("Form", $"{s.FormLabel} ({s.Form}), {s.Rig} rig");
        yield return ("Size", $"{s.Body.Size:0.0} m long, {s.BodyHeight:0.0} m high, {s.Body.Legs} legs");
        yield return ("Behaviour", $"{s.BehaviourLabel}, {s.Temperament.ToString().ToLowerInvariant()}");
        if (s.Activity != Activity.Cathemeral || s.RestKind != RestKind.None)
            yield return ("Active", $"{ActivityCycle.Label(s.Activity)}; rests in {ActivityCycle.Label(s.RestKind)}");
        if (s.Domestic != DomesticKind.None) yield return ("Kept as", s.Domestic.ToString());
        if (s.Role != DeepRole.None) yield return ("Place role", $"{s.Role}, {s.Theme}, depth {s.Depth}");
        if (s.InSpace) yield return ("Lives in", s.Habitat.ToString());
        Stats st = s.Stats;
        yield return ("Health", $"{st.MaxHealth:0}");
        yield return ("Speed", $"walks {st.WalkSpeed:0.0}, runs {st.RunSpeed:0.0} m/s");
        if (st.Damage > 0f) yield return ("Attack", $"{s.Attack.Name} ({s.Attack.Style}), {st.Damage:0} damage, wind-up {s.Attack.Telegraph:0.0} s");
        else yield return ("Attack", "none");
        yield return ("Senses", s.Senses.ToString());
        if (s.Flies) yield return ("Flight", s.Flight == FlightKind.None ? "drifts" : s.Flight.ToString());
        if (s.Burrows) yield return ("Burrows", "yes");
        if (s.Climbs) yield return ("Climbs", "walls and ceilings");
        if (s.Corrupted) yield return ("Hollow", "taken by the fungus");
        if (s.Mutation > 0f) yield return ("Mutation", $"{s.Mutation:0.0}{(s.Monstrous ? ", monstrous" : "")}");
        if (s.Spit != SpitKind.None) yield return ("Spits", s.Spit.ToString());
        if (s.Breaker != BreakerKind.None) yield return ("Breaks blocks", s.Breaker.ToString());
        if (s.Coat != Coat.Hide) yield return ("Coat", s.Coat.ToString());
        if (s.Homes.Length > 0) yield return ("Home ground", string.Join(", ", s.Homes.Select(h => h.ToString())));
        yield return ("Weirdness", $"{s.Weirdness:0.00}");
    }
}
