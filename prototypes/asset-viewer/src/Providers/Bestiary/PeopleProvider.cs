using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;

namespace AssetViewer.Providers;

/// <summary>
/// The people of the game, derived from its tables: every job, the climates and their outfits, what a
/// world's story and era put on them, the alien folk by covering and the Vharn, the world's own kinds,
/// what they carry and how it is held, the Husks, and the castaway. They stand on the people's own rig
/// (see <see cref="PersonStage"/>), so the animations are the ones a town's people have.
/// </summary>
public sealed partial class PeopleProvider : IAssetProvider
{
    public string Category => "People";

    public IEnumerable<AssetEntry> Entries()
    {
        WildlifeData data = Zoo.Data;
        string[] stories = ZooPeople.Stories(data);
        string[] props = ZooPeople.Props(data);
        string[] held = new[] { "As dressed", "Nothing" }.Concat(props).ToArray();
        string[] cultures = ZooPeople.Cultures;
        PersonRole[] roles = Enum.GetValues<PersonRole>();

        // ---- Townsfolk, one entry per job ---------------------------------------------------------------
        foreach (PersonRole role in roles)
        {
            PersonRole r0 = role;
            yield return new AssetEntry
            {
                Id = $"role/{r0.ToString().ToLowerInvariant()}",
                Group = "Townsfolk",
                Name = Zoo.Title(r0.ToString()),
                Randomisable = true,
                Options = new[]
                {
                    Opt("Culture", cultures, true, 1),
                    Opt("Climate", Zoo.Climates, true),
                    Opt("Humidity", ZooPeople.Humidities, true),
                    Opt("Tech", Zoo.Eras, true),
                    Opt("Story", stories, true),
                    Opt("Standing", ZooPeople.Occupancies),
                    Opt("Mutation", Zoo.Mutations),
                    Opt("Holding", held),
                },
                Build = r => new PersonStage(new[] { Roll(r, r0) }),
            };
        }

        // ---- Outfits: one climate, four humidities side by side ------------------------------------------------
        foreach (ClimateBand band in Enum.GetValues<ClimateBand>())
        {
            ClimateBand b0 = band;
            yield return new AssetEntry
            {
                Id = $"outfits/{b0.ToString().ToLowerInvariant()}",
                Group = "Outfits by climate and humidity",
                Name = $"{b0}: mild, dry, humid, rainy",
                Randomisable = true,
                Options = new[] { Opt("Culture", cultures, true), Opt("Tech", Zoo.Eras, true), Opt("Story", stories), Opt("Job", roles.Select(x => x.ToString()).ToArray(), true) },
                Build = r =>
                {
                    Enum.TryParse(r.Get("Job", "Worker"), out PersonRole job);
                    string culture = r.Get("Culture", cultures[0]);
                    int tech = Zoo.EraOf(r.Get("Tech"));
                    // One folk and one person's own look for the whole row: only the region they are dressed for changes.
                    Species[] row = ZooPeople.Humidities.Select(h => ZooPeople.Person(r.Seed, job, culture, ClimateName(b0), h, tech, r.Get("Story", "None"), "Friendly", 0f, "As dressed", false)).ToArray();
                    return new PersonStage(row, new[] { "Climate|" + b0, "Humidities|" + string.Join(", ", ZooPeople.Humidities) }, 1.5f);
                },
            };
        }

        // ---- Alien peoples, by covering, and the Vharn -------------------------------------------------------------
        foreach (string covering in ZooPeople.Coverings.Append("vharn"))
        {
            string c0 = covering;
            yield return new AssetEntry
            {
                Id = $"peoples/{c0}",
                Group = "Alien peoples",
                Name = c0 == "vharn" ? "The Vharn (chitin, mandibles, antennae)" : $"A people with {c0}",
                Randomisable = true,
                Options = new[]
                {
                    Opt("Job", roles.Select(x => x.ToString()).ToArray(), true),
                    Opt("Climate", Zoo.Climates, true),
                    Opt("Tech", Zoo.Eras, true),
                    Opt("Standing", ZooPeople.Occupancies),
                },
                Build = r =>
                {
                    Enum.TryParse(r.Get("Job", "Worker"), out PersonRole job);
                    Species s = PeopleOf(r, c0, job);
                    return new PersonStage(new[] { s });
                },
            };
        }
        yield return new AssetEntry
        {
            Id = "peoples/lineup",
            Group = "Alien peoples",
            Name = "A world's folk side by side",
            Randomisable = true,
            Options = new[] { Opt("Climate", Zoo.Climates, true), Opt("Tech", Zoo.Eras, true) },
            Build = r =>
            {
                // Everyone of the world's own mix of folk, one of each job: what stands in a town.
                WorldLife life = ZooPeople.World(r.Seed, "Alien folk", r.Get("Climate", "Mild"), Zoo.EraOf(r.Get("Tech")), "None", out PeopleGenerator people);
                var row = new List<Species>();
                string[] names = { "Worker", "Guard", "Trader", "Elder", "Child", "Scavenger" };
                for (int i = 0; i < names.Length; i++)
                {
                    Enum.TryParse(names[i], out PersonRole role);
                    row.Add(people.Person(Rng.Mix(r.Seed, (ulong)i + 1), role, Occupancy.Friendly));
                }
                return new PersonStage(row.ToArray());
            },
        };

        // ---- The world's own kinds that are people ------------------------------------------------------------------------
        foreach (var (id, kind) in data.Signature.Kinds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (kind.Body != "person") continue;
            string kid = id;
            SignatureKind sk = kind;
            yield return new AssetEntry
            {
                Id = $"kind/{kid}",
                Group = "World's own kinds",
                Name = string.IsNullOrEmpty(sk.Name) ? Zoo.Title(kid) : Zoo.Title(sk.Name),
                Randomisable = true,
                Options = new[] { Opt("Culture", cultures, true, 1), Opt("Tech", Zoo.Eras, true), Opt("Climate", Zoo.Climates, true) },
                Build = r =>
                {
                    WorldLife life = ZooPeople.World(r.Seed, r.Get("Culture", cultures[1]), r.Get("Climate", "Mild"), Zoo.EraOf(r.Get("Tech")), "None", out PeopleGenerator people);
                    Species lead = life.MakeKind(kid, sk, 0) ?? throw new InvalidOperationException("no people for " + kid);
                    Species[] who = (lead.Hosts ?? new List<Species> { lead }).Take(4).ToArray();
                    return new PersonStage(who, new[] { "Kind|" + (sk.Name == "" ? kid : sk.Name), "Behaviour|" + lead.BehaviourLabel });
                },
            };
        }

        // ---- What they carry, and how it is held -----------------------------------------------------------------------------
        foreach (string prop in props)
        {
            string p0 = prop;
            HoldKind hold = Weapons.HoldOf(p0);
            yield return new AssetEntry
            {
                Id = $"held/{p0}",
                Group = hold == HoldKind.None ? "Carried things" : "Held weapons and grips",
                Name = Zoo.Title(p0) + (hold == HoldKind.None ? "" : $" ({hold})"),
                Randomisable = true,
                Options = new[] { Opt("Culture", cultures, true), Opt("Tech", Zoo.Eras), Opt("Job", new[] { "Guard", "Raider", "Worker", "Farmer", "Scavenger", "Drunk", "Elder" }, true) },
                Build = r =>
                {
                    Enum.TryParse(r.Get("Job", "Guard"), out PersonRole job);
                    Species s = ZooPeople.Person(r.Seed, job, r.Get("Culture", cultures[0]), "Mild", "Mild", Zoo.EraOf(r.Get("Tech")), "None", "Friendly", 0f, p0, false);
                    return new PersonStage(new[] { s }, new[] { "Holds|" + p0 + " (" + hold + ")", "Ranged|" + (Weapons.Ranged(p0) ? "yes" : "no") });
                },
            };
        }

        // ---- Husks: people taken by the worms, in the sim --------------------------------------------------------------------------
        foreach (PersonRole role in roles)
        {
            if (role == PersonRole.Child) continue;
            PersonRole r0 = role;
            yield return new AssetEntry
            {
                Id = $"husk/{r0.ToString().ToLowerInvariant()}",
                Group = "Husks",
                Name = $"Husk of a {Zoo.Title(r0.ToString()).ToLowerInvariant()}",
                Randomisable = true,
                Options = new[] { Opt("Culture", cultures, true, 1), Opt("Climate", Zoo.Climates, true), Opt("Tech", Zoo.Eras, true), Opt("Story", stories), Opt("Mutation", Zoo.Mutations) },
                Build = r =>
                {
                    Species s = ZooPeople.Person(r.Seed, r0, r.Get("Culture", cultures[1]), r.Get("Climate", "Mild"), "Mild", Zoo.EraOf(r.Get("Tech")), r.Get("Story", "None"), "Friendly", Zoo.MutationOf(r.Get("Mutation")), "As dressed", true);
                    return new CreatureStage(s, false, new[] { "Wrongs|" + s.Figure.Wrongs, "Maggots|from the mouth, nose, eyes and ears" });
                },
            };
        }

        // ---- The castaway -------------------------------------------------------------------------------------------------------------
        yield return new AssetEntry { Id = "castaway", Group = "Survivors", Name = "The castaway", Build = _ => new CastawayStage() };
    }

    static AssetOption Opt(string name, string[] values, bool rollable = false, int def = 0) => new() { Name = name, Values = values, Default = def, Rollable = rollable };

    static string ClimateName(ClimateBand b) => b.ToString();

    static Species Roll(BuildRequest r, PersonRole role)
    {
        Zoo.Data.People.Roles.TryGetValue(role.ToString(), out RoleLook _);
        return ZooPeople.Person(r.Seed, role, r.Get("Culture", "Mixed world"), r.Get("Climate", "Mild"), r.Get("Humidity", "Mild"), Zoo.EraOf(r.Get("Tech")), r.Get("Story", "None"),
            r.Get("Standing", "Friendly"), Zoo.MutationOf(r.Get("Mutation")), r.Get("Holding", "As dressed"), false);
    }

    /// <summary>A person of a people with this covering (or the Vharn), dressed for a climate and an era.</summary>
    static Species PeopleOf(BuildRequest r, string covering, PersonRole job)
    {
        WorldConditions w = Zoo.World(r.Seed, 0.85f, r.Get("Climate", "Mild"), Zoo.EraOf(r.Get("Tech")));
        Folk folk = covering == "vharn" ? null : Peoples.Roll(Rng.For(r.Seed, "culture"), w, "people-" + covering, null, covering);
        return ZooPeople.Person(r.Seed, job, covering == "vharn" ? "Vharn" : "Alien folk", r.Get("Climate", "Mild"), "Mild", Zoo.EraOf(r.Get("Tech")), "None", r.Get("Standing", "Friendly"), 0f, "As dressed", false, folk);
    }
}
