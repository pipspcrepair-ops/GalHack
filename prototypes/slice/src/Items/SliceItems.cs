using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalHack.Items;

namespace Slice.Items;

/// <summary>What using a consumable does: food and oxygen as a share of full, health in points.</summary>
public sealed record ConsumableEffect(double Food, double Oxygen, double Health);

/// <summary>
/// shared/Items' catalogue as the slice plays it: read where it lives, with the slice's own
/// additions (data/items.json) appended as it loads, so nothing in shared/Items is copied or
/// changed on disk. The additions are the survival tool, the suit fabricator and what it makes,
/// core items, and what creatures leave behind.
/// </summary>
public static class SliceItems
{
    public const string Schema = "galhack.slice-items";

    /// <summary>The catalogue: shared/Items' data folder, then the slice's overlay file.</summary>
    public static ItemCatalogue Load(string sharedData, string overlayFile, out Dictionary<string, ConsumableEffect> consumables)
    {
        JsonNode overlay = JsonNode.Parse(File.ReadAllText(overlayFile), documentOptions: DataJson.Options);
        if (overlay?["schema"]?.GetValue<string>() != Schema) throw new InvalidDataException($"{overlayFile}: not a {Schema} file");
        consumables = ReadConsumables(overlay["consumables"]);
        return ItemCatalogue.Load(name =>
        {
            JsonNode root = JsonNode.Parse(File.ReadAllText(Path.Combine(sharedData, name)), documentOptions: DataJson.Options);
            switch (name)
            {
                case ItemCatalogue.ItemsFile:
                    Append(root["items"].AsArray(), overlay["items"]);
                    break;
                case ItemCatalogue.RecipesFile:
                    JsonArray recipes = root["recipes"].AsArray();
                    var replaced = (overlay["replaceRecipes"]?.AsArray() ?? new JsonArray()).Select(r => r["make"].GetValue<string>()).ToHashSet();
                    foreach (JsonNode r in recipes.ToList())
                        if (replaced.Contains(r["make"].GetValue<string>())) recipes.Remove(r);
                    Append(recipes, overlay["replaceRecipes"]);
                    Append(recipes, overlay["recipes"]);
                    break;
                case ItemCatalogue.MachinesFile:
                    // The suit fabricator goes first: the lowest tier.
                    JsonArray constructors = root["constructors"].AsArray();
                    var extra = overlay["constructors"]?.AsArray().Select(c => c.DeepClone()).ToList() ?? new List<JsonNode>();
                    for (int i = extra.Count - 1; i >= 0; i--) constructors.Insert(0, extra[i]);
                    break;
            }
            return root.ToJsonString();
        });
    }

    static void Append(JsonArray to, JsonNode from)
    {
        if (from == null) return;
        foreach (JsonNode n in from.AsArray()) to.Add(n.DeepClone());
    }

    static Dictionary<string, ConsumableEffect> ReadConsumables(JsonNode node)
    {
        var effects = new Dictionary<string, ConsumableEffect>(StringComparer.Ordinal);
        if (node is not JsonObject o) return effects;
        foreach (var (id, e) in o)
            effects[id] = new ConsumableEffect(Num(e, "food"), Num(e, "oxygen"), Num(e, "health"));
        return effects;
    }

    static double Num(JsonNode e, string name) => e?[name] is JsonValue v ? v.GetValue<double>() : 0;

    /// <summary>The raw item a planet generator deposit kind gives when dug ("Iron" to iron-ore), or null.</summary>
    public static ItemDef OreFor(ItemCatalogue items, string depositKind) =>
        items.Items.FirstOrDefault(i => i.IsRaw && string.Equals(i.DepositKind, depositKind, StringComparison.OrdinalIgnoreCase));
}
