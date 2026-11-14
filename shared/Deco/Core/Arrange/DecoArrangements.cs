using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>An arrangement template (deco-arrangements.json): a kind, where it goes, and the roles of its pieces.</summary>
public sealed class DecoTemplate
{
    public string Id, Kind, Where;
    public string[] Anchor = Array.Empty<string>(), Members = Array.Empty<string>(), Front = Array.Empty<string>(), Top = Array.Empty<string>(),
        Beside = Array.Empty<string>(), Screen = Array.Empty<string>(), Ends = Array.Empty<string>(), Focus = Array.Empty<string>(),
        Side = Array.Empty<string>(), Other = Array.Empty<string>(), Middle = Array.Empty<string>();
    public int CountMin = 1, CountMax = 1;
    /// <summary>The most uses of a template in one room (0: as many as fit).</summary>
    public int Max;
    public float Gap = 0.05f, Spacing = 0.3f, TopChance = 0.5f;
    /// <summary>Rows: the aisle down the middle.</summary>
    public float Aisle = 1.3f;
    public bool OnWall => Where == "wall";
    public override string ToString() => Id;
}

/// <summary>A set's (or a named recipe's) mix: wall and island templates with weights, and the roles that fill the walls.</summary>
public sealed class DecoSetPlan
{
    public string Set;
    public List<(string Template, float Weight)> Wall = new(), Island = new();
    public string[] Fill = Array.Empty<string>();
    /// <summary>The roles of the fitting hung in the middle of the ceiling (a room of a home: its age's lamp), if any.</summary>
    public string[] Light = Array.Empty<string>();
}

public sealed class DecoSiteDef
{
    public string Id, Blurb;
    public float Width, Length, Lane;
}

/// <summary>What a POI state leaves behind: shares of pieces knocked over, scattered, broken and so on.</summary>
public sealed class DecoDisorderRules
{
    public float Knock, Scatter, Push, Break, Burn, Overgrow, Missing, Powered, StillPowered;
    public int TouchesMin, TouchesMax;
    public DecoCover Growth = DecoCover.Vines;
}

/// <summary>Loads deco-arrangements.json: templates, set mixes, recipes, sites, POI mappings and disorder rules.</summary>
public sealed class DecoArrangements
{
    public const string FileName = "deco-arrangements.json";
    public readonly Dictionary<string, DecoTemplate> Templates = new(StringComparer.Ordinal);
    public readonly Dictionary<string, DecoSetPlan> Sets = new(StringComparer.Ordinal);
    public readonly Dictionary<string, DecoSetPlan> Recipes = new(StringComparer.Ordinal);
    public readonly Dictionary<string, DecoSiteDef> Sites = new(StringComparer.Ordinal);
    public readonly Dictionary<string, string> Rooms = new(StringComparer.Ordinal), Kits = new(StringComparer.Ordinal), Tags = new(StringComparer.Ordinal),
        Roles = new(StringComparer.Ordinal), Spaces = new(StringComparer.Ordinal);
    /// <summary>The tags in the file's order: the first a place has wins.</summary>
    public readonly List<string> TagOrder = new();
    /// <summary>What a room the rest say nothing of is furnished as.</summary>
    public string Fallback = "storeroom";
    public readonly Dictionary<string, (string[] Roles, string Fallback)> Props = new(StringComparer.Ordinal);
    public readonly Dictionary<string, DecoDisorderRules> Disorder = new(StringComparer.Ordinal);
    /// <summary>What a room of an id is arranged round (its focal point), by the roles of the pieces that make it: a kitchen its stove, a bedroom its bed.</summary>
    public readonly Dictionary<string, string[]> Focus = new(StringComparer.Ordinal);
    public static readonly string[] Kinds = { "around", "station", "bay", "row", "line", "rows", "cluster", "conveyor", "single" };

    public static DecoArrangements LoadDirectory(string dir, DecoCatalogue cat) => Load(File.ReadAllText(Path.Combine(dir, FileName)), cat);

    public static DecoArrangements Load(string json, DecoCatalogue cat)
    {
        var a = new DecoArrangements();
        JsonElement root = DataJson.Parse(json, "galhack.deco-arrangements");
        static string[] Roles(JsonElement e, string name) => e.TryGetProperty(name, out JsonElement v) ? v.EnumerateArray().Select(x => x.GetString()).ToArray() : Array.Empty<string>();
        foreach (JsonProperty p in root.GetProperty("templates").EnumerateObject())
        {
            JsonElement e = p.Value;
            var t = new DecoTemplate
            {
                Id = p.Name, Kind = DataJson.Str(e, "kind"), Where = DataJson.Str(e, "where", "wall"),
                Anchor = Roles(e, "anchor"), Members = Roles(e, "members"), Front = Roles(e, "front"), Top = Roles(e, "top"),
                Beside = Roles(e, "beside"), Screen = Roles(e, "screen"), Ends = Roles(e, "ends"), Focus = Roles(e, "focus"),
                Side = Roles(e, "side"), Other = Roles(e, "other"), Middle = Roles(e, "middle"),
                Gap = (float)DataJson.Num(e, "gap", 0.05), Spacing = (float)DataJson.Num(e, "spacing", 0.3), TopChance = (float)DataJson.Num(e, "topChance", 0.5),
                Max = DataJson.Int(e, "max", 0), Aisle = (float)DataJson.Num(e, "aisle", 1.3),
            };
            if (e.TryGetProperty("count", out JsonElement c)) { var v = c.EnumerateArray().Select(x => x.GetInt32()).ToArray(); t.CountMin = v[0]; t.CountMax = v[1]; }
            if (!Kinds.Contains(t.Kind)) throw new InvalidDataException($"template '{t.Id}': unknown kind '{t.Kind}'");
            if (t.Anchor.Length == 0) throw new InvalidDataException($"template '{t.Id}': it needs anchor roles");
            a.Templates[t.Id] = t;
        }
        DecoSetPlan ReadPlan(string set, JsonElement e)
        {
            var sp = new DecoSetPlan { Set = set, Fill = Roles(e, "fill"), Light = Roles(e, "light") };
            foreach (var (key, list) in new[] { ("wall", sp.Wall), ("island", sp.Island) })
                if (e.TryGetProperty(key, out JsonElement w))
                    foreach (JsonProperty q in w.EnumerateObject())
                    {
                        if (!a.Templates.ContainsKey(q.Name)) throw new InvalidDataException($"set '{set}': unknown template '{q.Name}'");
                        list.Add((q.Name, (float)q.Value.GetDouble()));
                    }
            return sp;
        }
        foreach (JsonProperty p in root.GetProperty("sets").EnumerateObject())
        {
            if (!cat.HasSet(p.Name)) throw new InvalidDataException($"arrangements: unknown set '{p.Name}'");
            a.Sets[p.Name] = ReadPlan(p.Name, p.Value);
        }
        foreach (JsonProperty p in root.GetProperty("recipes").EnumerateObject())
        {
            string set = DataJson.Str(p.Value, "set");
            if (!cat.HasSet(set)) throw new InvalidDataException($"recipe '{p.Name}': unknown set '{set}'");
            a.Recipes[p.Name] = ReadPlan(set, p.Value);
        }
        foreach (JsonProperty p in root.GetProperty("sites").EnumerateObject())
            a.Sites[p.Name] = new DecoSiteDef { Id = p.Name, Blurb = DataJson.Str(p.Value, "blurb", ""), Width = (float)DataJson.Num(p.Value, "width"), Length = (float)DataJson.Num(p.Value, "length"), Lane = (float)DataJson.Num(p.Value, "lane") };
        foreach (var (key, map) in new[] { ("rooms", a.Rooms), ("kits", a.Kits), ("tags", a.Tags), ("roles", a.Roles), ("spaces", a.Spaces) })
            foreach (JsonProperty p in root.GetProperty(key).EnumerateObject())
            {
                string set = p.Value.GetString();
                if (!cat.HasSet(set) && !a.Recipes.ContainsKey(set)) throw new InvalidDataException($"arrangements {key}: '{p.Name}' maps to unknown set or recipe '{set}'");
                map[p.Name] = set;
                if (key == "tags") a.TagOrder.Add(p.Name);
            }
        a.Fallback = DataJson.Str(root, "fallback", "storeroom");
        if (!cat.HasSet(a.Fallback) && !a.Recipes.ContainsKey(a.Fallback)) throw new InvalidDataException($"arrangements: unknown fallback '{a.Fallback}'");
        foreach (JsonProperty p in root.GetProperty("props").EnumerateObject())
        {
            string fb = DataJson.Str(p.Value, "fallback");
            if (!cat.TryGet(fb, out _)) throw new InvalidDataException($"prop '{p.Name}': unknown fallback piece '{fb}'");
            a.Props[p.Name] = (Roles(p.Value, "roles"), fb);
        }
        foreach (JsonProperty p in root.GetProperty("disorder").EnumerateObject())
        {
            JsonElement e = p.Value;
            var touches = e.GetProperty("touches").EnumerateArray().Select(x => x.GetInt32()).ToArray();
            a.Disorder[p.Name] = new DecoDisorderRules
            {
                Knock = (float)DataJson.Num(e, "knock"), Scatter = (float)DataJson.Num(e, "scatter"), Push = (float)DataJson.Num(e, "push"),
                Break = (float)DataJson.Num(e, "break"), Burn = (float)DataJson.Num(e, "burn"), Overgrow = (float)DataJson.Num(e, "overgrow"),
                Missing = (float)DataJson.Num(e, "missing"), Powered = (float)DataJson.Num(e, "powered"), StillPowered = (float)DataJson.Num(e, "stillPowered"),
                TouchesMin = touches[0], TouchesMax = touches[1],
                Growth = DataJson.Str(e, "growth", "vines") == "bloom" ? DecoCover.Bloom : DecoCover.Vines,
            };
        }
        if (root.TryGetProperty("focus", out JsonElement focus))
            foreach (JsonProperty p in focus.EnumerateObject()) a.Focus[p.Name] = p.Value.EnumerateArray().Select(x => x.GetString()).ToArray();
        foreach (string s in new[] { "active", "abandoned", "infested", "ruined" })
            if (!a.Disorder.ContainsKey(s)) throw new InvalidDataException($"arrangements: no disorder rules for '{s}'");
        return a;
    }

    /// <summary>The set a POI room is furnished from (see <see cref="Resolve"/>).</summary>
    public string SetFor(string roomId, IEnumerable<string> tags = null, string kit = null) => Resolve(roomId, tags, kit).Set;

    /// <summary>
    /// How a POI room is furnished: a set, and the recipe (a named mix of it) when one fits. The room's
    /// own role comes first (its id, then a word of it: a foreman's office is an office wherever it
    /// stands), then what the place is for (the first of its tags in the file's order: a mine's drifts
    /// are a mine's, an airfield's hangar a hangar), then what the room is as a space, then a plain
    /// storeroom. A culture's kit furnishes everything it builds. Office furniture goes only where a
    /// room says it is an office.
    /// </summary>
    public (string Set, string Recipe) Resolve(string roomId, IEnumerable<string> tags = null, string kit = null)
    {
        if (kit != null && Kits.TryGetValue(kit, out string k)) return As(k);
        string[] words = Words(roomId);
        if (roomId != null && Rooms.TryGetValue(roomId, out string r)) return As(r);
        foreach (string w in words) if (Roles.TryGetValue(w, out string role)) return As(role);
        if (tags != null)
        {
            var has = new HashSet<string>(tags, StringComparer.Ordinal);
            foreach (string t in TagOrder) if (has.Contains(t)) return As(Tags[t]);
        }
        if (roomId != null && Spaces.TryGetValue(roomId, out string sp)) return As(sp);
        foreach (string w in words) if (Spaces.TryGetValue(w, out string space)) return As(space);
        return As(Fallback);
    }

    /// <summary>A name in the maps as a set and recipe: a recipe furnishes from its own set.</summary>
    (string Set, string Recipe) As(string name) => Recipes.TryGetValue(name, out DecoSetPlan recipe) ? (recipe.Set, name) : (name, null);

    /// <summary>A room id's words: "foreman's office" is foreman and office, "pump-hall" pump and hall.</summary>
    static string[] Words(string id) => id == null ? Array.Empty<string>() :
        id.ToLowerInvariant().Replace("'s", "").Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
}
