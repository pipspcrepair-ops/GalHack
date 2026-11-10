using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Skies;
using PlanetSurface.Terrain;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using Flora = Planets.Look.Flora;
using Biome = Planets.Gen.Biome;

namespace AssetViewer.Providers.Nature;

/// <summary>
/// Every plant the flora generator makes, by family, on every type of world and under every history the
/// world layers give a world's growth (overgrown, burnt, dead, swollen, fungal, sparse, stranger); and the
/// stands and ground cover each biome grows, laid out as the game's scatter lays them.
/// </summary>
public sealed partial class FloraProvider : IAssetProvider
{
    public string Category => "Flora";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    /// <summary>The world layers that decide what grows: every layer of the flora table, read off the layers file.</summary>
    public static IReadOnlyList<WorldLayer> Histories => _histories ??= LoadHistories();
    static List<WorldLayer> _histories;

    static List<WorldLayer> LoadHistories()
    {
        string path = NatureWorlds.Prototypes + "poi-generator/data/world-layers.txt";
        if (!File.Exists(path)) return new List<WorldLayer>();
        return LayerParser.Parse(File.ReadAllText(path), "world-layers.txt").Where(l => l.Kind == LayerKind.Flora && !l.Off).ToList();
    }

    static readonly string[] Species = { "1", "2", "3", "4", "5", "6" };

    static AssetOption WorldOption(int defaultIndex) =>
        new("World", NatureWorlds.Types().Select(t => t.Label).ToArray()) { Default = defaultIndex };

    static AssetOption HistoryOption() => new AssetOption("History", new[] { "None" }.Concat(Histories.Select(l => l.Id)).ToArray());

    /// <summary>The kind of world a family is at home on: only where the picker opens (any world that grows it will do).</summary>
    static int HomeWorld(PlantFamily f)
    {
        string name = f switch
        {
            PlantFamily.Conifer => "Temperate meadow", PlantFamily.Broadleaf => "Temperate meadow", PlantFamily.Jungle => "Temperate rainforest",
            PlantFamily.Palm => "Temperate rainforest", PlantFamily.Shrub => "Temperate meadow", PlantFamily.Cactus => "Arid dunes",
            PlantFamily.Fungus => "Alien fungal", PlantFamily.GiantFungus => "Alien fungal", PlantFamily.Crystal => "Alien crystal",
            PlantFamily.Ice => "Ice glacier", PlantFamily.Grass => "Temperate meadow", PlantFamily.Forb => "Temperate meadow",
            PlantFamily.Twig => "Arid canyon", PlantFamily.Reed => "Temperate rainforest", PlantFamily.Moss => "Ice glacier",
            PlantFamily.Pins => "Alien fungal", _ => "Temperate meadow",
        };
        string[] labels = NatureWorlds.Types().Select(t => t.Label).ToArray();
        return Math.Max(0, Array.FindIndex(labels, l => l.Equals(name, StringComparison.OrdinalIgnoreCase)));
    }

    public IEnumerable<AssetEntry> Entries()
    {
        foreach (PlantFamily family in Enum.GetValues<PlantFamily>())
        {
            PlantFamily f = family;
            bool cover = f is >= PlantFamily.Grass and <= PlantFamily.Pins;
            string group = f is PlantFamily.Conifer or PlantFamily.Broadleaf or PlantFamily.Jungle or PlantFamily.Palm ? "Plants/Trees"
                : cover ? "Plants/Ground cover" : f == PlantFamily.Crop ? "Plants/Crops" : "Plants/Shrubs and growths";
            yield return new AssetEntry
            {
                Id = "family-" + f, Group = group, Name = f.ToString(), Randomisable = true,
                Options = new[]
                {
                    WorldOption(HomeWorld(f)), HistoryOption(),
                    new AssetOption("Species", Species), new AssetOption("Layout", "Single", "Stand", "Patch") { Default = cover ? 2 : 0 },
                    new AssetOption("Size", "Smallest", "Typical", "Largest") { Default = 1 },
                },
                Build = r => new FloraView(r, f, null),
            };
        }
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            Biome b = biome;
            if (BiomeLook.FloraOf(b).Length == 0 && PlanetFlora.CoverOf(b).Equals(default(CoverMix))) continue;
            yield return new AssetEntry
            {
                Id = "stand-" + b, Group = "Stands/" + FamilyOf(b), Name = b.ToString(), Randomisable = true,
                Options = new[] { new AssetOption("World", new[] { "Any" }.Concat(NatureWorlds.Types().Select(t => t.Label)).ToArray()), HistoryOption() },
                Build = r => new FloraView(r, null, b),
            };
        }
    }

    /// <summary>Which kind of world a biome belongs to, by where the enum lists it.</summary>
    static string FamilyOf(Biome b) => b switch
    {
        <= Biome.FrozenSea => "Seas",
        <= Biome.CloudForest => "Temperate",
        <= Biome.Oasis => "Arid",
        <= Biome.Sinter => "Ice",
        <= Biome.Sulfur => "Lava",
        _ => "Alien",
    };
}
