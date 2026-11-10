using System.Collections.Generic;
using System.Linq;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>Every POI family of the game's data that is a place in its own right (the rest are Settlements, Infrastructure and Space).</summary>
public sealed partial class PlacesProvider : IAssetProvider
{
    public string Category => "Places";

    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    public IEnumerable<AssetEntry> Entries() => PlaceFamilies.Entries(PlaceData.Home.Places);
}

/// <summary>Turns the families of one category into entries.</summary>
public static class PlaceFamilies
{
    public static IEnumerable<AssetEntry> Entries(PlaceData.Home home)
    {
        foreach (FamilyDef f in PlaceData.Library.Families)
        {
            var (category, group) = PlaceData.Classify(f);
            if (category != home) continue;
            FamilyDef family = f;
            yield return new AssetEntry
            {
                Id = "family/" + f.Id, Group = group, Name = string.IsNullOrEmpty(f.Name) ? PlaceData.Title(f.Id) : f.Name,
                Randomisable = true, Options = PlaceBuilder.OptionsFor(f), Build = r => PlaceBuilder.Build(family, r),
            };
        }
    }
}
