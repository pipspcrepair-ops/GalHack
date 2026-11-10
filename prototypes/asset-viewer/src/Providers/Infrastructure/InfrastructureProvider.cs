using System.Collections.Generic;

namespace AssetViewer.Providers;

/// <summary>Roads, rail, bridges, power lines (patches of a real generated world) and the utility works.</summary>
public sealed partial class InfrastructureProvider : IAssetProvider
{
    public string Category => "Infrastructure";
    public IEnumerable<(string Prototype, string Folder)> Mounts => new[] { ("planet-surface", "shaders") };

    public IEnumerable<AssetEntry> Entries()
    {
        foreach (AssetEntry e in PlaceFamilies.Entries(PlaceData.Home.Infrastructure)) yield return e;
        foreach (InfraPatch.Feature f in InfraPatch.Features)
        {
            InfraPatch.Feature feature = f;
            yield return new AssetEntry
            {
                Id = "infra/" + f.Id, Group = f.Group, Name = f.Name, Randomisable = true, Options = InfraPatch.Options(),
                Build = r => InfraPatch.Build(feature, r),
            };
        }
    }
}
