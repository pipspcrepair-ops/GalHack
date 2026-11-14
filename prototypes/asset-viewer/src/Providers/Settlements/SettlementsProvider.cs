using System.Collections.Generic;

namespace AssetViewer.Providers;

/// <summary>Cities, towns, camps, farms, roadside ruins and single buildings.</summary>
public sealed partial class SettlementsProvider : IAssetProvider
{
    public string Category => "Settlements";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    public IEnumerable<AssetEntry> Entries()
    {
        // Every family of the game's data that is a settlement, a farm, a camp, a roadside ruin or a city building.
        foreach (AssetEntry e in PlaceFamilies.Entries(PlaceData.Home.Settlements)) yield return e;
        foreach (AssetEntry e in Houses.Entries()) yield return e;
        // Whole patches of city, from the planner's rules.
        foreach (string district in CityPatch.Districts)
            yield return new AssetEntry
            {
                Id = "city/" + district.ToLowerInvariant().Replace(' ', '-'), Group = "Cities",
                Name = district == "Mixed transect" ? "City, downtown to the edge" : "City: " + district,
                Randomisable = true, Options = CityPatch.Options(district), Build = CityPatch.Build,
            };
    }
}
