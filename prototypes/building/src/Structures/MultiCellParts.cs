using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GalHack.Structures;

/// <summary>
/// Real parts spanning several cells, for checking the anchor-and-filler rules before the 1 m
/// grid's data lands (docs/one-metre-grid.md, section 2). Each is a copy of today's part from
/// components.json with a size, nothing else changed: the large door as a 2×2×1 door, the large
/// container as a 2×2×2 container, and the thruster drawn out to 1×1×2 (a footprint that isn't
/// symmetric, so every turn shows). The game's own data doesn't have them yet: phase 2 adds the
/// sized parts to the data, as real items.
/// </summary>
public static class MultiCellParts
{
    public const string Door = "door-2x2x1", Container = "container-2x2x2", Thruster = "thruster-1x1x2";

    static readonly (string from, string id, int[] size)[] Parts =
    {
        ("door", Door, new[] { 2, 2, 1 }),
        ("container", Container, new[] { 2, 2, 2 }),
        ("thruster", Thruster, new[] { 1, 1, 2 }),
    };

    /// <summary>The catalogue as <see cref="Catalogue.Load"/> reads it, plus the sized copies.</summary>
    public static Catalogue Load(Func<string, string> readFile) =>
        Catalogue.Load(name => name == Catalogue.ComponentsFile ? WithSizedCopies(readFile(name)) : readFile(name));

    /// <summary>components.json text with the sized copies appended.</summary>
    public static string WithSizedCopies(string componentsJson)
    {
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        JsonNode root = JsonNode.Parse(componentsJson, documentOptions: options);
        JsonArray list = root["components"].AsArray();
        foreach (var (from, id, size) in Parts)
        {
            JsonNode source = list.FirstOrDefault(c => (string)c["id"] == from) ?? throw new InvalidOperationException($"no '{from}' in components.json");
            JsonNode copy = source.DeepClone();
            copy["id"] = id;
            copy["name"] = $"{(string)source["name"]} {size[0]}×{size[1]}×{size[2]}";
            copy["size"] = new JsonArray(size.Select(n => (JsonNode)n).ToArray());
            list.Add(copy);
        }
        return root.ToJsonString();
    }
}
