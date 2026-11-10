using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// Houses as the house rules build them (poi-generator Homes: data/homes.txt weights the parts of a house by climate, tech, era and
/// wealth): one house of a chosen family, or a street of one culture's houses, lot lines against each other and fronts to the road.
/// </summary>
public static class Houses
{
    static readonly string[] Wealths = { "Poor", "Modest", "Comfortable", "Rich" };
    static readonly float[] WealthValue = { 0.1f, 0.4f, 0.65f, 0.92f };
    static readonly string[] HouseStates = { "Active", "Abandoned", "Ruined" };

    static string[] ClimateNames => Enum.GetNames<Climate>();
    static string[] Techs => new[] { "Low", "Mid", "High" };

    public static IEnumerable<AssetEntry> Entries()
    {
        foreach (HouseFamily f in PlaceData.Library.Homes.Families)
        {
            HouseFamily fam = f;
            int tech = fam.Techs.Count > 0 ? (int)fam.Techs.Min() : 1;
            int climate = fam.Climates.Count > 0 ? (int)fam.Climates.Min() : 0;
            yield return new AssetEntry
            {
                Id = "house/" + f.Id, Group = fam.Odd ? "Houses/Odd ones out" : "Houses", Name = string.IsNullOrEmpty(f.Name) ? PlaceData.Title(f.Id) : f.Name, Randomisable = true,
                Options = new[]
                {
                    new AssetOption("State", HouseStates) { Default = 1, Rollable = true },
                    new AssetOption("Tech level", Techs) { Default = tech },
                    new AssetOption("Climate", ClimateNames) { Default = climate, Rollable = true },
                    new AssetOption("Wealth", Wealths) { Default = 1, Rollable = true },
                    new AssetOption("View", "Outside", "Walk in") { },
                },
                Build = r => House(fam, r),
            };
        }
        yield return Street("street/houses", "Street of houses", 6, false);
        yield return Street("street/village", "Village: a wider street", 9, true);
    }

    static int VariantFor(FamilyDef flavour, TechLevel tech)
    {
        string tag = WorldProfile.Tag(tech);
        int i = flavour.Variants.FindIndex(v => v.Tech == tag);
        return Math.Max(0, i);
    }

    static FamilyDef HouseFamilyDef => PlaceData.Library.Families.First(f => f.Id == "flavour_house");

    static TechLevel TechOf(string name) => name switch { "Low" => TechLevel.Low, "High" => TechLevel.High, _ => TechLevel.Mid };
    static Climate ClimateOf(string name) => Enum.TryParse(name, out Climate c) ? c : Climate.Temperate;
    static float WealthOf(string name) => WealthValue[Math.Max(0, Array.IndexOf(Wealths, name))];
    static PoiState StateOf(string name) => Enum.TryParse(name, out PoiState s) ? s : PoiState.Abandoned;

    static PlaceAsset House(HouseFamily fam, BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        FamilyDef flavour = HouseFamilyDef;
        TechLevel tech = TechOf(r.Get("Tech level", "Mid"));
        var home = new HomeHints((int)(r.Seed % 100000), ClimateOf(r.Get("Climate")), WealthOf(r.Get("Wealth")), fam.Id);
        Layout layout = PoiFactory.BuildLayout(lib, flavour, VariantFor(flavour, tech), Rarity.Common, r.Seed, new LayoutHints(Dir.North, tech, null, home));
        PoiInstance poi = PoiFactory.Spawn(lib, layout, StateOf(r.Get("State", "Abandoned")), Hash.Combine(r.Seed, 0x4005EUL));
        var view = r.Get("View") == "Walk in" ? PlaceAsset.ViewMode.Inside : PlaceAsset.ViewMode.Outside;
        PlaceAsset asset = new(poi, Biome.Steppe, view);
        HomeNote note = layout.Extras.Homes.FirstOrDefault();
        if (note != null)
        {
            asset.With("House", $"{note.Family}, {note.Silhouette.Split('/')[0]}");
            asset.With("Storeys / roof", $"{note.Storeys} / {note.Roof}");
            asset.With("Garage", note.Garage);
        }
        asset.With("Climate / wealth", $"{r.Get("Climate")} / {r.Get("Wealth")}");
        return asset;
    }

    static AssetEntry Street(string id, string name, int count, bool wide) => new()
    {
        Id = id, Group = "Streets", Name = name, Randomisable = true,
        Options = new[]
        {
            new AssetOption("State", HouseStates) { Default = 1, Rollable = true },
            new AssetOption("Tech level", Techs) { Default = 1, Rollable = true },
            new AssetOption("Climate", ClimateNames) { Rollable = true },
            new AssetOption("Wealth", Wealths) { Default = 2, Rollable = true },
            new AssetOption("Houses", "3", "6", "9", "12") { Default = Array.IndexOf(new[] { "3", "6", "9", "12" }, count.ToString()) },
        },
        Build = r => StreetOf(r, wide),
    };

    /// <summary>One culture's houses in a row (or a row to each side of the road), lot line against lot line, fronts to the road.</summary>
    static PlaceGroup StreetOf(BuildRequest r, bool wide)
    {
        PoiLibrary lib = PlaceData.Library;
        FamilyDef flavour = HouseFamilyDef;
        TechLevel tech = TechOf(r.Get("Tech level", "Mid"));
        PoiState state = StateOf(r.Get("State", "Abandoned"));
        int count = int.TryParse(r.Get("Houses"), out int n) ? n : 6;
        int culture = (int)(r.Seed % 100000);
        var rng = new Rng(Hash.Combine(r.Seed, 0x57EEUL));
        var home = new HomeHints(culture, ClimateOf(r.Get("Climate")), WealthOf(r.Get("Wealth")));
        Color grass = new("5d6e41"), road = new("303134"), path = new("8c8a82");
        var group = new PlaceGroup(new Aabb(Vector3.Zero, Vector3.One));
        int built = 0;
        float length = 0, depthN = 20, depthS = 0;
        var families = new HashSet<string>();
        int rows = wide ? 2 : 1;
        for (int side = 0; side < rows; side++)
        {
            float at = 0;
            int share = count / rows + (side == 0 ? count % rows : 0);
            for (int k = 0; k < share; k++)
            {
                ulong seed = rng.NextU64();
                Layout l = PoiFactory.BuildLayout(lib, flavour, VariantFor(flavour, tech), Rarity.Common, seed, new LayoutHints(Dir.North, tech, null, home));
                PoiInstance poi = PoiFactory.Spawn(lib, l, state, Hash.Combine(seed, 0x57A7EUL));
                var view = PoiView.Build(poi, Biome.Steppe, new PoiView.Options(Collision: false, Detail: true, Lights: state == PoiState.Active, Figures: false), (_, _) => grass);
                HomeNote h = l.Extras.Homes.FirstOrDefault();
                bool lot = h != null && h.LotMax.X > h.LotMin.X;
                float x0 = lot ? h.LotMin.X : l.Occupied.Min.X, z0 = lot ? h.LotMin.Z : l.Occupied.Min.Z;
                float w = lot ? h.LotMax.X - h.LotMin.X + 1 : l.Occupied.Size.X - 6;
                float depth = lot ? h.LotMax.Z - h.LotMin.Z + 1 : l.Occupied.Size.Z;
                // The road runs along x at z 0. A house's front looks at -z: the north row stands past the road as built, the south row is turned about.
                if (side == 0) { group.Add(view, new Vector3(at - x0, 0, 6 - z0)); depthN = Math.Max(depthN, depth); }
                else { view.RotationDegrees = new Vector3(0, 180, 0); group.Add(view, new Vector3(at + w + x0, 0, z0 - 6)); depthS = Math.Max(depthS, depth); }
                if (h != null) families.Add(h.Family);
                at += w;
                built++;
            }
            length = Math.Max(length, at);
        }
        length = Math.Max(length, 30f);
        float top = 6 + depthN + 14, bottom = (wide ? -(6 + depthS + 14) : -14f);
        var flat = new Flat();
        flat.Quad(-20, bottom, length + 20, top, -0.03f, grass);
        flat.Box(-20, 0f, -6f, length + 20, 0.14f, -4f, path);
        flat.Box(-20, 0f, 4f, length + 20, 0.14f, 6f, path);
        flat.Strip(-20, 0f, 1, 0, length + 40, 8f, 0.02f, road);
        group.AddChild(flat.ToMesh("Road"));
        var land = new Flat();
        land.Quad(-30000, -30000, 30000, 30000, -0.06f, new Color("5d6e41").Darkened(0.2f));
        group.AddChild(land.ToMesh("Land"));
        group.Fact("Houses", built.ToString());
        group.Fact("Families", string.Join(", ", families));
        group.Fact("Street", $"{length:0} m long, culture {culture}");
        group.Fact("Climate / wealth", $"{r.Get("Climate")} / {r.Get("Wealth")}");
        return group.WithBounds(new Aabb(new Vector3(-10, 0, bottom), new Vector3(length + 20, 20, top - bottom)));
    }
}
