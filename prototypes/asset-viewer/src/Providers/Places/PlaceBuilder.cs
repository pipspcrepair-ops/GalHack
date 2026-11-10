using System;
using System.Collections.Generic;
using System.Linq;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// Turns a family and the viewer's options into a placed, dressed, lived-in (or not) place, the way the game's own
/// spawn does: layout from the family's variant and a seed, then a spawn in a state, a world's tech and fall, and
/// whoever occupies it. The option lists come from the data (variants, biomes, the occupant enum), never a typed list.
/// </summary>
public static class PlaceBuilder
{
    public const string Authored = "As authored", Default = "Default";
    public static readonly string[] States = Enum.GetNames<PoiState>();
    public static readonly string[] Rarities = Enum.GetNames<Rarity>();
    public static readonly string[] Techs = { Authored, "Low", "Mid", "High" };

    /// <summary>How long ago and how hard the world fell, as the world rolls give it (RuinAge.Of).</summary>
    public static readonly (string Name, RuinAge Age)[] Falls =
    {
        ("A century, mostly neglect", RuinAge.Default),
        ("Just fallen", new RuinAge(0.04f, false, 0.1f, 0.35f)),
        ("Recent", new RuinAge(0.3f, false, 0.1f, 0.35f)),
        ("Long ago", new RuinAge(0.92f, false, 0.1f, 0.35f)),
        ("War-torn", new RuinAge(0.65f, false, 1f, 0.35f)),
        ("Drowned", new RuinAge(0.65f, false, 0.1f, 0.95f)),
        ("Falling now", new RuinAge(0.06f, true, 0.4f, 0.35f)),
    };

    public static readonly string[] Occupants = new[] { "As the state left it" }.Concat(Enum.GetNames<Occupant>()).ToArray();
    public static readonly string[] Views = { "Outside", "Walk in", "Cutaway" };

    public static AssetOption[] OptionsFor(FamilyDef f, bool withOccupants = true, bool withView = true)
    {
        var list = new List<AssetOption>();
        if (f.Variants.Count > 1)
            list.Add(new AssetOption("Layout", f.Variants.Select(v => v.Name).ToArray()) { Rollable = true });
        int state = Array.IndexOf(f.StateWeights, f.StateWeights.Max());
        list.Add(new AssetOption("State", States) { Default = Math.Max(0, state), Rollable = true });
        list.Add(new AssetOption("Rarity", Rarities) { Default = (int)f.Rarity, Rollable = true });
        if (f.Biomes.Count > 1) list.Add(new AssetOption("Biome", f.Biomes.Select(b => b.ToString()).ToArray()) { Rollable = true });
        list.Add(new AssetOption("Tech level", Techs));
        list.Add(new AssetOption("Fall", Falls.Select(x => x.Name).ToArray()));
        if (withOccupants) list.Add(new AssetOption("Occupied by", Occupants));
        if (withView) list.Add(new AssetOption("View", Views) { Default = f.Underground || f.Tags.Contains("underground") ? 2 : 0 });
        return list.ToArray();
    }

    public static PlaceAsset Build(FamilyDef f, BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        int variant = Math.Max(0, f.Variants.FindIndex(v => v.Name == r.Get("Layout")));
        PoiState state = Enum.TryParse(r.Get("State"), out PoiState s) ? s : PoiState.Abandoned;
        Rarity rarity = Enum.TryParse(r.Get("Rarity"), out Rarity ra) ? ra : f.Rarity;
        Biome biome = Enum.TryParse(r.Get("Biome"), out Biome b) ? b : f.Biomes.FirstOrDefault();
        TechLevel? tech = r.Get("Tech level") switch { "Low" => TechLevel.Low, "Mid" => TechLevel.Mid, "High" => TechLevel.High, _ => null };
        RuinAge ruin = Falls.FirstOrDefault(x => x.Name == r.Get("Fall")).Age;
        bool customFall = ruin != null && !ReferenceEquals(ruin, RuinAge.Default);
        ruin ??= RuinAge.Default;
        OccupancyRoll occupancy = Enum.TryParse(r.Get("Occupied by"), out Occupant who) ? new OccupancyRoll(who) : null;
        PlaceAsset.ViewMode view = r.Get("View") switch { "Walk in" => PlaceAsset.ViewMode.Inside, "Cutaway" => PlaceAsset.ViewMode.Cutaway, _ => PlaceAsset.ViewMode.Outside };

        WorldProfile world = tech == null && !customFall ? null : new WorldProfile { Before = tech ?? TechLevel.Mid, Now = tech ?? TechLevel.Mid, Ruin = ruin };
        LayoutHints hints = tech == null && !customFall ? null : new LayoutHints(Dir.North, tech, null, null, customFall ? ruin : null);
        Layout layout = PoiFactory.BuildLayout(lib, f, variant, rarity, r.Seed, hints);
        PoiInstance poi = PoiFactory.Spawn(lib, layout, state, Hash.Combine(r.Seed, 0x51ACEUL), world, occupancy);
        return new PlaceAsset(poi, biome, view);
    }
}
