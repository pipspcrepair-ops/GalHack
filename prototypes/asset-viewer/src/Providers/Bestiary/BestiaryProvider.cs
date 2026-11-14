using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Wildlife;
using Landform = Planets.Gen.Landform;

namespace AssetViewer.Providers;

/// <summary>
/// Every kind of creature the generators can make, listed from the game's own tables: each body form
/// of the wildlife data, the domestic animals, the cave and place roles by theme and depth, the
/// landforms' niche species, the space fauna forms, the block-breakers, machines and signature kinds.
/// New rows in the data show up here by themselves. Each is rolled again from its seed, and lives in
/// the real simulation (see <see cref="CreatureStage"/>).
/// </summary>
public sealed partial class BestiaryProvider : IAssetProvider
{
    public string Category => "Bestiary";

    public IEnumerable<AssetEntry> Entries()
    {
        WildlifeData data = Zoo.Data;
        foreach (AssetEntry e in Forms(data)) yield return e;
        foreach (AssetEntry e in Domestic()) yield return e;
        foreach (AssetEntry e in Places(data)) yield return e;
        foreach (AssetEntry e in Landforms()) yield return e;
        foreach (AssetEntry e in Kept(data)) yield return e;
        foreach (AssetEntry e in SpaceFauna()) yield return e;
    }

    static AssetOption Opt(string name, string[] values, bool rollable = false, int def = 0) => new() { Name = name, Values = values, Default = def, Rollable = rollable };

    // ---- Surface and cave body forms --------------------------------------------------------------

    static IEnumerable<AssetEntry> Forms(WildlifeData data)
    {
        foreach (FormDef f in data.Fauna.Forms)
        {
            string id = f.Id;
            bool deep = f.Deep;
            string[] behaviours = Zoo.BehavioursOf(f);
            yield return new AssetEntry
            {
                Id = $"form/{id}",
                Group = deep ? "Caves and places/Body forms" : $"Surface/{Zoo.PlanLabel(f.Plan)}",
                Name = $"{Zoo.Title(id)} ({f.Rig.ToString().ToLowerInvariant()})",
                Randomisable = true,
                Options = new[]
                {
                    Opt("Behaviour", behaviours, true),
                    Opt("Alienness", Zoo.Alienness, true),
                    Opt("Climate", Zoo.Climates, true),
                    Opt("Mutation", Zoo.Mutations, true),
                    Opt("Fungus", new[] { "None", "Hollow" }),
                    Opt("Age", new[] { "Adult", "Young" }),
                    Opt("Sex", new[] { "As rolled", "Other sex" }),
                },
                Build = r =>
                {
                    Species s = Zoo.Surface(id, r.Seed, Zoo.BehaviourOf(r.Get("Behaviour", behaviours[0])), Zoo.AliennessOf(r.Get("Alienness")), r.Get("Climate", "Mild"),
                        Zoo.MutationOf(r.Get("Mutation")), r.Get("Fungus") == "Hollow", r.Get("Age") == "Young", r.Get("Sex") == "Other sex");
                    return new CreatureStage(s, deep || s.Climbs);
                },
            };
        }
    }

    // ---- Settlements' animals -------------------------------------------------------------------------

    static IEnumerable<AssetEntry> Domestic()
    {
        foreach (DomesticKind kind in Enum.GetValues<DomesticKind>())
        {
            if (kind == DomesticKind.None) continue;
            DomesticKind k = kind;
            yield return new AssetEntry
            {
                Id = $"domestic/{k.ToString().ToLowerInvariant()}",
                Group = "Domestic",
                Name = k switch { DomesticKind.Stock => "Stock (kept in pens)", DomesticKind.Working => "Working beast (draught)", _ => "Pet" },
                Randomisable = true,
                Options = new[] { Opt("Alienness", Zoo.Alienness, true) },
                Build = r =>
                {
                    Species s = new FaunaGenerator(Zoo.Data.Fauna, Zoo.Data.Activity).RollDomestic(r.Seed, k, Zoo.AliennessOf(r.Get("Alienness")));
                    return new CreatureStage(s, false);
                },
            };
        }
    }

    // ---- Caves, tunnels and places -----------------------------------------------------------------------

    static IEnumerable<AssetEntry> Places(WildlifeData data)
    {
        string[] themes = Enum.GetValues<PoiTheme>().Select(t => t.ToString()).Where(t => data.Deep.Themes.ContainsKey(t)).ToArray();
        if (themes.Length == 0) themes = Enum.GetNames<PoiTheme>();
        string[] depths = Enumerable.Range(0, data.Deep.Depth.MaxLevel + 1).Select(i => i.ToString()).ToArray();
        foreach (string name in data.Deep.Roles.Keys.OrderBy(n => Enum.TryParse(n, out DeepRole r) ? (int)r : 99))
        {
            if (!Enum.TryParse(name, out DeepRole role) || role == DeepRole.None) continue;
            DeepRole rr = role;
            yield return new AssetEntry
            {
                Id = $"place/{name.ToLowerInvariant()}",
                Group = "Caves and places",
                Name = rr switch
                {
                    DeepRole.Crawler => "Cave crawler", DeepRole.Lurker => "Lurker", DeepRole.Prowler => "Prowler", DeepRole.Ambusher => "Ambusher",
                    DeepRole.Nester => "Nester", DeepRole.Subsister => "Subsister", DeepRole.DerelictNester => "Wreck nester", _ => name,
                },
                Randomisable = true,
                Options = new[]
                {
                    Opt("Theme", themes, true),
                    Opt("Depth", depths, true),
                    Opt("Alienness", Zoo.Alienness, true, 1),
                    Opt("Infested", new[] { "No", "Bloom" }),
                    Opt("Variant", new[] { "0", "1", "2" }, true),
                },
                Build = r =>
                {
                    var deep = new DeepFauna(Zoo.Data, r.Seed, Zoo.AliennessOf(r.Get("Alienness")))
                    {
                        Variant = int.TryParse(r.Get("Variant", "0"), out int v) ? v : 0,
                    };
                    Enum.TryParse(r.Get("Theme", themes[0]), out PoiTheme theme);
                    Species s = deep.For(rr, theme, int.TryParse(r.Get("Depth", "0"), out int d) ? d : 0, r.Get("Infested") == "Bloom");
                    return new CreatureStage(s, true);
                },
            };
        }
    }

    // ---- Landform niches --------------------------------------------------------------------------------------

    static IEnumerable<AssetEntry> Landforms()
    {
        string[] which = { "First", "Second", "Third" };
        foreach (Landform l in Enum.GetValues<Landform>())
        {
            Landform lf = l;
            yield return new AssetEntry
            {
                Id = $"landform/{lf.ToString().ToLowerInvariant()}",
                Group = "Landforms",
                Name = Zoo.Title(lf.ToString()),
                Randomisable = true,
                Options = new[] { Opt("Species", which, true), Opt("Alienness", Zoo.Alienness, true), Opt("Fungus", new[] { "None", "Hollow" }) },
                Build = r =>
                {
                    var gen = new FaunaGenerator(Zoo.Data.Fauna, Zoo.Data.Activity);
                    PlanetFauna fauna = gen.Generate(r.Seed, Zoo.AliennessOf(r.Get("Alienness")), r.Get("Fungus") == "Hollow", new[] { lf });
                    var own = fauna.Species.Where(s => s.Homes.Contains(lf)).ToList();
                    if (own.Count == 0) own = fauna.Species;
                    Species s = own[Math.Min(r.IndexOf("Species", which), own.Count - 1)];
                    return new CreatureStage(s, s.Climbs, new[] { "Adaptation|" + s.Adaptation });
                },
            };
        }
    }

    // ---- What a world keeps: breakers, machines, signature kinds, holders ------------------------------------------

    static IEnumerable<AssetEntry> Kept(WildlifeData data)
    {
        string[] which = { "First", "Second", "Third" };
        foreach (BreakerKind kind in Enum.GetValues<BreakerKind>())
        {
            if (kind == BreakerKind.None) continue;
            BreakerKind k = kind;
            yield return new AssetEntry
            {
                Id = $"breaker/{k.ToString().ToLowerInvariant()}",
                Group = "Block-breakers",
                Name = k switch { BreakerKind.Ram => "Ram", BreakerKind.Burrower => "Burrower", BreakerKind.Corroder => "Corroder", _ => "Siege beast" },
                Randomisable = true,
                Options = new[] { Opt("Alienness", Zoo.Alienness, true), Opt("Climate", Zoo.Climates, true) },
                Build = r =>
                {
                    WorldConditions w = Zoo.World(r.Seed, Zoo.AliennessOf(r.Get("Alienness")), r.Get("Climate", "Mild"));
                    WorldLife life = Wildlife.Bestiary.Roll(Zoo.Data, w);
                    Species s = life.Breaker(k, Rng.For(r.Seed, "breaker"));
                    return new CreatureStage(s, false);
                },
            };
        }
        for (int era = 0; era < 3; era++)
        {
            int e = era;
            yield return new AssetEntry
            {
                Id = $"machine/{e}",
                Group = "Machines",
                Name = e switch { 0 => "Low-tech machines (clockwork)", 1 => "Mid-tech machines", _ => "High-tech machines" },
                Randomisable = true,
                Options = new[] { Opt("Frame", which, true) },
                Build = r =>
                {
                    Species s = Robots.Roll(Zoo.World(r.Seed, 0.4f), r.IndexOf("Frame", which), e);
                    return new CreatureStage(s, false);
                },
            };
        }
        foreach (var (id, kind) in data.Signature.Kinds.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (kind.Body == "person") continue;
            string kid = id;
            SignatureKind sk = kind;
            yield return new AssetEntry
            {
                Id = $"kind/{kid}",
                Group = "World's own kinds",
                Name = string.IsNullOrEmpty(sk.Name) ? Zoo.Title(kid) : Zoo.Title(sk.Name),
                Randomisable = true,
                Options = new[] { Opt("Alienness", Zoo.Alienness, true), Opt("Tech", Zoo.Eras, true) },
                Build = r =>
                {
                    WorldConditions w = Zoo.World(r.Seed, Zoo.AliennessOf(r.Get("Alienness")), "Mild", Zoo.EraOf(r.Get("Tech")));
                    WorldLife life = Wildlife.Bestiary.Roll(Zoo.Data, w);
                    Species s = life.MakeKind(kid, sk, 0) ?? throw new InvalidOperationException("no body for " + kid);
                    return new CreatureStage(s, false);
                },
            };
        }
        foreach (string holder in new[] { "Hollows", "Twisted", "Machines", "Cyborgs" })
        {
            string h = holder;
            yield return new AssetEntry
            {
                Id = $"holder/{h.ToLowerInvariant()}",
                Group = "Holders of places",
                Name = h switch { "Hollows" => "Hollows (beasts taken by the fungus)", "Twisted" => "Twisted beasts", "Machines" => "Old security machines", _ => "Cyborgs" },
                Randomisable = true,
                Options = new[] { Opt("Kind", which, true), Opt("Alienness", Zoo.Alienness, true) },
                Build = r =>
                {
                    WorldConditions w = Zoo.World(r.Seed, Zoo.AliennessOf(r.Get("Alienness")));
                    w.Predators = 1f;
                    w.Mutagen = 0.5f;
                    WorldLife life = Wildlife.Bestiary.Roll(Zoo.Data, w);
                    IReadOnlyList<Species> list = h switch { "Hollows" => life.Hollows, "Twisted" => life.Twisted, "Machines" => life.Machines, _ => life.Cyborgs };
                    if (list.Count == 0) throw new InvalidOperationException("this world holds none");
                    return new CreatureStage(list[Math.Min(r.IndexOf("Kind", which), list.Count - 1)], false);
                },
            };
        }
    }

}
