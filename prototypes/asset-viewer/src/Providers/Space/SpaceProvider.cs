using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;
using PoiGenerator.Gen;
using N = System.Numerics;

namespace AssetViewer.Providers;

/// <summary>
/// Places in space from the POI generator's orbit data (data/orbit.txt): a derelict capital ship, a drifting hulk, a wreck field,
/// escape pods, a broken station or a held outpost, built of the player's own vessel blocks as the slice sets them adrift; and
/// every capital-hull site (data/sites.txt) composed from its role, size and crash by CapitalHull.
/// </summary>
public sealed partial class SpaceProvider : IAssetProvider
{
    public string Category => "Space";
    public IEnumerable<(string Prototype, string Folder)> Mounts => System.Array.Empty<(string, string)>();

    static readonly string[] States = { "Abandoned", "Ruined", "Infested", "Active" };
    static readonly string[] Techs = { "Low", "Mid", "High" };

    public IEnumerable<AssetEntry> Entries()
    {
        PoiLibrary lib = PlaceData.Library;
        foreach (SpaceSiteDef s in lib.SpaceSites)
        {
            SpaceSiteDef site = s;
            string group = site.Hull != null ? "Capital ships" : site.Kind switch { "field" => "Wreck fields", "broken" => "Stations", "station" or "outpost" => "Stations and outposts", _ => "Wrecks" };
            yield return new AssetEntry
            {
                Id = "space/" + site.Id, Group = group, Name = string.IsNullOrEmpty(site.Name) ? PlaceData.Title(site.Id) : site.Name, Randomisable = true,
                Options = new[]
                {
                    new AssetOption("State", States) { Default = site.IsWreck ? 0 : 1, Rollable = true },
                    new AssetOption("Tech level", Techs) { Default = 1 },
                },
                Build = r => Site(site, r),
            };
        }
        // Every capital hull of the sites file, and the one composed from the rules.
        var roles = Words("role"); var sizes = Words("size"); var crashes = Words("crash");
        yield return new AssetEntry
        {
            Id = "hull/composed", Group = "Capital hulks", Name = "Capital hulk from rules", Randomisable = true,
            Options = new[]
            {
                new AssetOption("Role", roles) { Rollable = true },
                new AssetOption("Size", sizes) { Default = Math.Min(1, sizes.Length - 1), Rollable = true },
                new AssetOption("Crash", crashes) { Rollable = true },
                new AssetOption("State", States) { Rollable = true },
                new AssetOption("Tech level", Techs) { Default = 1, Rollable = true },
                new AssetOption("Livery", lib.Kits.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray()) { Default = Math.Max(0, Array.IndexOf(lib.Kits.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(), "meridian")) },
            },
            Build = Composed,
        };
        foreach (var (id, site) in lib.Sites.Where(kv => kv.Value.Parts.Any(p => p.Kind == "grand" && p.Arg("what") == "capital")).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            string siteId = id; SiteDef def = site;
            yield return new AssetEntry
            {
                Id = "hull/" + siteId, Group = "Capital hulks", Name = $"{def.Name}: {siteId.Replace('_', ' ')}", Randomisable = true,
                Options = new[] { new AssetOption("State", States) { Rollable = true } },
                Build = r => HullSite(def, siteId, r),
            };
        }
    }

    /// <summary>The words a data comment lists for a capital hull part: <c>role &lt;a|b|c&gt;</c>, so a new role added to the data shows up here.</summary>
    static string[] Words(string key)
    {
        string text = System.IO.File.ReadAllText(System.IO.Path.Combine(PlaceData.DataFolder, "sites.txt"));
        var found = new SortedSet<string>(StringComparer.Ordinal);
        Match m = Regex.Match(text, key + @" <([a-z|]+)>");
        if (m.Success) foreach (string w in m.Groups[1].Value.Split('|')) found.Add(w);
        foreach (var site in PlaceData.Library.Sites.Values)
            foreach (var p in site.Parts.Where(p => p.Kind == "grand" && p.Arg("what") == "capital")) found.Add(p.Arg(key));
        string[] order = key switch { "size" => new[] { "medium", "large", "huge" }, _ => found.ToArray() };
        return key == "size" ? order.Where(found.Contains).Concat(found.Except(order)).ToArray() : found.ToArray();
    }

    static PoiState StateOf(string name) => Enum.TryParse(name, out PoiState s) ? s : PoiState.Abandoned;
    static TechLevel TechOf(string name) => name switch { "Low" => TechLevel.Low, "High" => TechLevel.High, _ => TechLevel.Mid };

    static SpaceAsset Site(SpaceSiteDef site, BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        PoiState state = StateOf(r.Get("State"));
        var rng = new Random(unchecked((int)(r.Seed ^ (r.Seed >> 32))));
        var asset = new SpaceAsset(site.Name);
        if (site.Hull != null && lib.Sites.TryGetValue(site.Hull, out SiteDef hull))
        {
            SitePart part = hull.Parts[0];
            AddHull(asset, lib.Kits[hull.Style], TechOf(r.Get("Tech level", "Mid")), CapitalHull.SpecOf(part), r.Seed, state, site.Name);
            asset.Fact("Hull from", site.Hull);
        }
        else if (site.Kind == "field" && site.Id.Contains("pod")) asset.AddAll(SpaceKits.Pods(rng, site.Name));
        else if (site.Kind == "field") asset.AddAll(SpaceKits.Field(rng, site.Name));
        else asset.AddAll(SpaceKits.Station(rng, site.Name));
        asset.Fact("Kind", site.Kind + (site.Held ? ", held by " + site.Faction : ""));
        asset.Fact("Danger / salvage", $"{site.Danger} / {site.Salvage}");
        asset.Fact("Found", string.Join(", ", site.Zones));
        asset.Fact("State", state.ToString());
        return asset;
    }

    static SpaceAsset Composed(BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        var spec = new CapitalHull.Spec(r.Get("Role", "freighter"), r.Get("Size", "large"), r.Get("Crash", "broken"));
        string kit = lib.Kits.ContainsKey(r.Get("Livery")) ? r.Get("Livery") : lib.Kits.Keys.First();
        var asset = new SpaceAsset($"{spec.Role} {spec.Size}");
        AddHull(asset, lib.Kits[kit], TechOf(r.Get("Tech level", "Mid")), spec, r.Seed, StateOf(r.Get("State")), $"{spec.Role} {spec.Size}");
        asset.Fact("Composed from", $"{spec.Role}, {spec.Size}, crash {spec.Crash}");
        return asset;
    }

    static SpaceAsset HullSite(SiteDef def, string id, BuildRequest r)
    {
        PoiLibrary lib = PlaceData.Library;
        SitePart part = def.Parts.First(p => p.Kind == "grand" && p.Arg("what") == "capital");
        var asset = new SpaceAsset(def.Name);
        AddHull(asset, lib.Kits[def.Style], WorldProfile.Parse(part.Arg("tech", "mid")), CapitalHull.SpecOf(part), r.Seed, StateOf(r.Get("State")), def.Name);
        asset.Fact("Site", id);
        return asset;
    }

    static void AddHull(SpaceAsset asset, Kit kit, TechLevel tech, CapitalHull.Spec spec, ulong seed, PoiState state, string name)
    {
        CapitalHull.HullPlan plan = CapitalHull.Plan(kit, tech, spec, seed);
        N.Vector3 first = plan.Pieces[0].Origin;
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece piece = plan.Pieces[i];
            var gone = piece.Damage(state, seed + (ulong)i);
            GalHack.Structures.Structure s = SpaceKits.Build(piece.Text(name, gone), name + piece.Name);
            asset.Add(new SpaceKits.Piece(s, piece.Origin - first, piece.Rotation, string.IsNullOrEmpty(piece.Name) ? "Hull" : piece.Name.Trim()));
        }
        asset.Fact("Tech / kit", $"{tech} / {kit.Name}");
    }
}

/// <summary>A few structures adrift in black space, each as the building system draws it (frozen: nothing moves or falls).</summary>
public sealed partial class SpaceAsset : ProviderAsset
{
    readonly List<StructureView> _views = new();
    readonly List<(string, string)> _facts = new();
    readonly string _name;
    Aabb _bounds = new(new Vector3(-5, -5, -5), new Vector3(10, 10, 10));
    int _blocks;

    public SpaceAsset(string name)
    {
        _name = name;
        Stage.Ground = false;
        Stage.Background = "space";
    }

    public void AddAll(IEnumerable<SpaceKits.Piece> pieces) { foreach (var p in pieces) Add(p); }

    public void Add(SpaceKits.Piece piece)
    {
        StructureView view = StructureView.Create(piece.Structure);
        var q = piece.Rotation;
        view.Transform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), new Vector3(piece.Position.X, piece.Position.Y, piece.Position.Z));
        AddChild(view);
        _views.Add(view);
        _blocks += piece.Structure.Count;
        // Where it lies, from its blocks (its meshes are not there until the next frame).
        var (min, max) = piece.Structure.Bounds();
        float cell = piece.Structure.Grid.CellSize;
        var local = new Aabb(new Vector3(min.X, min.Y, min.Z) * cell, new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * cell);
        Aabb world = view.Transform * local;
        _bounds = _views.Count == 1 ? world : _bounds.Merge(world);
    }

    public override void _Ready()
    {
        // The views freeze themselves (a wreck "falls"); in the viewer nothing moves it.
        foreach (StructureView v in _views) FrozenViews.Freeze(v);
    }

    public SpaceAsset Fact(string label, string value) { _facts.Add((label, value)); return this; }
    public override Aabb? FrameBounds => _bounds;

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        yield return ("Pieces", _views.Count.ToString());
        yield return ("Blocks", _blocks.ToString("N0"));
        yield return ("Extent", $"{_bounds.Size.X:0} x {_bounds.Size.Y:0} x {_bounds.Size.Z:0} m");
        foreach (var f in _facts) yield return f;
    }
}
