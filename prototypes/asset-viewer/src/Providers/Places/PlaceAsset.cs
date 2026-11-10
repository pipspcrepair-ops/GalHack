using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator;
using PoiGenerator.Gen;

namespace AssetViewer.Providers;

/// <summary>
/// One generated place on the stage: the POI generator's own view of it (blocks, doors that slide open as the camera nears,
/// lights, figures), set on flat ground with its rock plug, or as a cutaway of the buried plan, or as a walk-in from its door.
/// </summary>
public sealed partial class PlaceAsset : ProviderAsset
{
    public enum ViewMode { Outside, Inside, Cutaway }

    public PoiInstance Poi { get; }
    public PoiView View { get; }
    readonly ViewMode _look;
    readonly Node3D _content = new() { Name = "Place" };
    Aabb _bounds;
    readonly List<(string, string)> _extraFacts = new();

    public PlaceAsset(PoiInstance poi, Biome biome, ViewMode look, float groundRadiusMin = 0f)
    {
        Poi = poi;
        _look = look;
        var ground = PoiGenerator.Look.ToColor(PoiGenerator.Look.Ground(biome));
        bool space = poi.Extras.InSpace;
        View = PoiView.Build(poi, biome, new PoiView.Options(Collision: false, Detail: true, Lights: true, Figures: true), (_, _) => ground);
        AddChild(_content);
        _content.AddChild(View);

        Aabb modules = View.SurfaceBounds();
        if (look == ViewMode.Cutaway)
        {
            // The whole plan with nothing round it: no rock, no ground, as the generator laid it out.
            RemoveBlocks();
            _content.AddChild(CutawayBlocks(poi, biome, space));
            var all = poi.Layout.Modules;
            Int3 mn = all.Select(m => m.Min).Aggregate(Int3.Min), mx = all.Select(m => m.Max).Aggregate(Int3.Max);
            modules = new Aabb(new Vector3(mn.X, mn.Y, mn.Z), new Vector3(mx.X - mn.X + 1, mx.Y - mn.Y + 1, mx.Z - mn.Z + 1));
            Stage.Ground = false;
            Stage.Background = space ? "space" : "sky";
        }
        else if (space)
        {
            Stage.Ground = false;
            Stage.Background = "space";
        }
        else
        {
            float r = Mathf.Clamp(Mathf.Max(modules.Size.X, modules.Size.Z) * 0.5f + 70f, Mathf.Max(90f, groundRadiusMin), 700f);
            AddGround(r, ground);
            Stage.Ground = false;
        }
        AddHulls(poi, ref modules);
        _bounds = modules;
        Stage.GroundRadius = Mathf.Max(modules.Size.X, modules.Size.Z) * 0.75f + 40f;
        if (look == ViewMode.Inside)
        {
            Stage.StartInside = true;
            Stage.MinDistanceShare = 0.002f;
            // Turn the place about the spot outside its door so that the door lies straight ahead (-z) of the camera.
            var (stand, yaw) = View.EntranceStand();
            Vector3 local = View.ToLocal(stand);
            _content.Rotation = new Vector3(0, -yaw + Mathf.Pi, 0);
            _content.Position = -(_content.Basis * local);
            _bounds = new Aabb(new Vector3(-8, 0, -8), new Vector3(16, 6, 16));
        }
    }


    readonly List<GalHack.Structures.Presentation.StructureView> _hulls = new();
    int _hullBlocks;

    /// <summary>
    /// A crashed capital ship's hull is a vessel, not part of the place (the place keeps the hollow it lies in, its ramps and yard): the
    /// slice builds each piece of its plan of the player's blocks, damaged as the place's state and the crash say, and sets it down
    /// there. The viewer does the same, so the wreck is whole in its pit.
    /// </summary>
    void AddHulls(PoiInstance poi, ref Aabb modules)
    {
        CapitalHull.HullPlan plan = CapitalHull.Of(poi.Layout);
        if (plan == null) return;
        ulong seed = poi.Layout.Seed;
        for (int i = 0; i < plan.Pieces.Count; i++)
        {
            CapitalHull.HullPiece piece = plan.Pieces[i];
            var gone = piece.Damage(poi.State, seed + (ulong)i * 7919UL);
            GalHack.Structures.Structure s = SpaceKits.Build(piece.Text(poi.Family.Name, gone), poi.Family.Name + piece.Name);
            var view = GalHack.Structures.Presentation.StructureView.Create(s);
            var q = piece.Rotation;
            view.Transform = new Transform3D(new Basis(new Quaternion(q.X, q.Y, q.Z, q.W)), new Vector3(piece.Origin.X, piece.Origin.Y, piece.Origin.Z));
            _content.AddChild(view);
            _hulls.Add(view);
            _hullBlocks += s.Count;
            var (min, max) = s.Bounds();
            float cell = s.Grid.CellSize;
            modules = modules.Merge(view.Transform * new Aabb(new Vector3(min.X, min.Y, min.Z) * cell, new Vector3(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * cell));
        }
        _extraFacts.Add(("Hull", $"{plan.Pieces.Count} piece(s), {_hullBlocks:N0} blocks (vessel)"));
    }

    public override void _Ready()
    {
        foreach (var v in _hulls) FrozenViews.Freeze(v);
    }

    void RemoveBlocks()
    {
        var blocks = View.GetNodeOrNull("Blocks");
        if (blocks != null) { View.RemoveChild(blocks); blocks.Free(); }
    }

    static Node3D CutawayBlocks(PoiInstance poi, Biome biome, bool space)
    {
        var root = new Node3D { Name = "Blocks" };
        float glow = poi.State switch { PoiState.Active => 2.4f, PoiState.Infested => 2.0f, _ => 1.6f };
        var materials = new BlockMaterials(glow);
        var options = new MeshOptions { Doors = false, Collision = false, Detail = true, Rock = false, Mottle = 0.3f, GroundColor = PoiGenerator.Look.Ground(biome), RockColor = PoiGenerator.Look.Rock(biome) };
        foreach (var chunk in Mesher.Build(poi.Blocks, poi.Layout.Occupied, options))
            root.AddChild(new MeshInstance3D { Mesh = MeshBuild.ToMesh(chunk, materials.BySurface), Name = $"Chunk {chunk.Chunk}" });
        if (MeshBuild.Water(poi) is { } water) root.AddChild(water);
        // The ground's level as a faint pane, so the buried plan reads as buried.
        if (!space)
        {
            Int3 mn = poi.Layout.Modules.Select(m => m.Min).Aggregate(Int3.Min), mx = poi.Layout.Modules.Select(m => m.Max).Aggregate(Int3.Max);
            var pane = new Flat();
            Color g = PoiGenerator.Look.ToColor(PoiGenerator.Look.Ground(biome));
            pane.Quad(mn.X - 6, mn.Z - 6, mx.X + 7, mx.Z + 7, -0.04f, new Color(g.R, g.G, g.B, 0.28f));
            root.AddChild(pane.ToMesh("Ground level", 1f, true));
        }
        return root;
    }

    void AddGround(float radius, Color colour)
    {
        int tile = PoiView.Tile;
        int min = -(int)Math.Ceiling(radius / tile) * tile + (View.PlugMin.X + View.PlugMax.X) / 2 / tile * tile;
        int minZ = -(int)Math.Ceiling(radius / tile) * tile + (View.PlugMin.Y + View.PlugMax.Y) / 2 / tile * tile;
        int tiles = (int)Math.Ceiling(2 * radius / tile);
        var hole = new Rect2I(View.PlugMin, View.PlugMax - View.PlugMin + Vector2I.One);
        _content.AddChild(PoiGenerator.Terrain.Build(new Vector2I(min, minZ), new Vector2I(tiles, tiles), (_, _) => colour, new List<Rect2I> { hole }, false, 0f));
        // Past the tiles, one plain plane out to the horizon, a hair under the ground so nothing is coplanar.
        var plane = new Flat();
        plane.Quad(-30000, -30000, 30000, 30000, -0.06f, colour);
        _content.AddChild(plane.ToMesh("Land"));
    }

    public override Aabb? FrameBounds => _bounds;
    public override bool Spins => _look != ViewMode.Inside;

    public override IEnumerable<(string Label, string Value)> Facts()
    {
        var l = Poi.Layout;
        Aabb b = PoiView.ModuleBoundsOf(Poi);
        yield return ("Family", Poi.Family.Name);
        yield return ("Layout", l.Variant?.Name ?? l.Name);
        yield return ("State / rarity", $"{Poi.State} / {Poi.Rarity}");
        yield return ("Footprint", $"{b.Size.X:0} x {b.Size.Z:0} m");
        int above = l.Extras.Levels.Count(x => x.Storey >= 0), below = l.Extras.Levels.Count(x => x.Storey < 0);
        yield return ("Floors", l.Extras.Levels.Count == 0 ? $"{Math.Max(1, (int)(b.Size.Y / 4))} (estimated)" : $"{above} above, {below} below ground");
        yield return ("Rooms", $"{l.Modules.Count} spaces, {l.Doorways.Count} doorways");
        yield return ("Blocks", Poi.Blocks.CountBlocks().ToString("N0"));
        yield return ("Kit", l.Kit?.Name ?? "");
        if (l.Tech is { } tech) yield return ("Built at tech", tech.ToString());
        yield return ("Caches / terminals", $"{Poi.Caches.Count} / {Poi.Terminals.Count}");
        yield return ("Enemies / lights", $"{Poi.Enemies.Count} / {Poi.Lights.Count}");
        yield return ("Core", Poi.HasCore ? "held by its faction" : "none");
        if (Poi.Occupancy != null) yield return ("Occupied by", Poi.Occupancy.ToString());
        yield return ("Age", Poi.Age.ToString());
        foreach (var f in _extraFacts) yield return f;
    }

    public PlaceAsset With(string label, string value) { _extraFacts.Add((label, value)); return this; }

    public override void _Process(double delta)
    {
        if (View != null && IsInstanceValid(View)) View.Watcher = GetViewport()?.GetCamera3D();
    }
}
