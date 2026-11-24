using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalHack.Structures;
using Godot;

namespace BlockBuilding;

/// <summary>Test helpers: building's catalogue with the precursor plating the slice appends to it.</summary>
public static class PlatingKit
{
    /// <summary>The six plated surfaces, bare polymer first.</summary>
    public static readonly string[] Platings = { "base-block", "iron-plated", "hardened-plated", "combat-plated", "concrete-plated", "precursor-plated" };

    public static Catalogue WithPrecursor()
    {
        string data = ProjectSettings.GlobalizePath("res://data");
        string extra = Path.GetFullPath(Path.Combine(data, "..", "..", "..", "shared", "Items", "data", "building-materials.json"));
        var options = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        return Catalogue.Load(name =>
        {
            string text = File.ReadAllText(Path.Combine(data, name));
            if (name != Catalogue.MaterialsFile) return text;
            JsonNode root = JsonNode.Parse(text, documentOptions: options);
            JsonArray list = root["materials"].AsArray();
            foreach (JsonNode m in JsonNode.Parse(File.ReadAllText(extra), documentOptions: options)["materials"].AsArray().ToList()) list.Add(m.DeepClone());
            return root.ToJsonString();
        });
    }
}
