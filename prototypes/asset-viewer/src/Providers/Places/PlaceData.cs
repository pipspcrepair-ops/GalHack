using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// The POI generator's data, read straight from its folder (the viewer is its own project, so res://data is not it),
/// and the rules that sort a family into one of the viewer's categories. Nothing here is a list of families: the data
/// says what exists, and a new family appears in the browser by itself.
/// </summary>
public static class PlaceData
{
    static PoiLibrary _library;
    static Dictionary<string, string> _source;

    public static string DataFolder => Path.Combine(ResourceMount.PrototypesRoot, "poi-generator", "data");

    public static PoiLibrary Library => _library ??= Load();

    static PoiLibrary Load()
    {
        var files = new List<(string, string)>();
        foreach (string path in Directory.EnumerateFiles(DataFolder, "*", SearchOption.AllDirectories))
            if (path.EndsWith(".kit", StringComparison.Ordinal) || path.EndsWith(".txt", StringComparison.Ordinal))
                files.Add((Path.GetFileName(path), File.ReadAllText(path)));
        return PoiLibrary.Load(files);
    }

    /// <summary>The data file (without extension) each family is written in: architect-families, city-families, utility-families...</summary>
    public static string SourceOf(string familyId)
    {
        if (_source == null)
        {
            _source = new Dictionary<string, string>();
            foreach (string path in Directory.EnumerateFiles(DataFolder, "*families.txt"))
                foreach (string line in File.ReadLines(path))
                {
                    string t = line.Trim();
                    if (t.StartsWith("family ", StringComparison.Ordinal)) _source[t[7..].Trim()] = Path.GetFileNameWithoutExtension(path);
                }
        }
        return _source.GetValueOrDefault(familyId, "");
    }

    public enum Home { Places, Settlements, Infrastructure }

    /// <summary>Which category a family belongs in, and the folder inside it, from its tags, tier and data file.</summary>
    public static (Home Category, string Group) Classify(FamilyDef f)
    {
        bool Has(string t) => f.Tags.Contains(t);
        string source = SourceOf(f.Id);
        if (source == "city-families") return (Home.Settlements, "City buildings");
        if (source == "flavour-families" || Has("flavour")) return (Home.Settlements, "Roadside ruins");
        if (Has("utility") || Has("rail") || Has("port") || Has("power") && !Has("settlement")) return (Home.Infrastructure, "Utilities and works");
        if (Has("settlement") || Has("civilian") || Has("evacuation") || Has("exodus") || Has("scrappy")) return (Home.Settlements, f.Tier == PoiTier.Camp ? "Camps" : "Towns and markets");
        if (Has("farm")) return (Home.Settlements, "Farms");
        if (f.Tier == PoiTier.Camp) return (Home.Settlements, "Camps");
        if (Has("city")) return (Home.Settlements, "City buildings");
        return (Home.Places, TierFolder(f));
    }

    static string TierFolder(FamilyDef f)
    {
        if ((f.Style ?? "").StartsWith("alien", StringComparison.Ordinal) || f.Tags.Contains("precursor")) return "Alien and Precursor";
        if (f.Tags.Contains("underground") || f.Generator == Generator.Kit && f.Underground) return "Underground";
        if (f.Tags.Contains("military") || f.Tags.Contains("fort") || f.Tags.Contains("prison")) return "Military and faction";
        if (f.Tags.Contains("science") || f.Tags.Contains("medical")) return "Science and medical";
        if (f.Tags.Contains("mine") || f.Tags.Contains("industry")) return "Mines and industry";
        if (f.Tags.Contains("wreck") || f.Tags.Contains("radiation") || f.Tags.Contains("aviation")) return "Wrecks, craters and fields";
        return "Other places";
    }

    /// <summary>"old_farm" to "Old Farm" when the data gives no name.</summary>
    public static string Title(string id) => string.Join(' ', id.Split('_', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
