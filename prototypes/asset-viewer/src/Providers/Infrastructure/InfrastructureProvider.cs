using System.Collections.Generic;

namespace AssetViewer.Providers;

/// <summary>Roads, rail, power, utility works and the pieces that dress them.</summary>
public sealed partial class InfrastructureProvider : IAssetProvider
{
    public string Category => "Infrastructure";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    public IEnumerable<AssetEntry> Entries() => PlaceFamilies.Entries(PlaceData.Home.Infrastructure);
}
