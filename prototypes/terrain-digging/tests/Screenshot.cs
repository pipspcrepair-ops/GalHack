using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/terrain-digging res://tests/Screenshot.tscn -- --view=pit --out=shot.png
/// Views: landscape, pit, crater, block, row (blocks of every size and angle set into a slope),
/// corner (a base's corner set into a slope). "map" writes a top-down layout map (works headless).
/// Extras: --debug=1 shows normals, --debug=2 unlit albedo; --noshadow turns the sun's shadows off;
/// --legacy clears the ground for row and corner the way the slice's BuildTool used to (the
/// block plus 0.1 m, sampled every 0.5 m and floored to cells), for before-and-after pictures.
/// </summary>
public partial class Screenshot : Node
{
    const int SettleFrames = 45;

    Main _main;
    string _view = "landscape";
    string _out = "shot.png";
    bool _legacy;
    int _stage, _frames;
    Action _beforeCapture;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg == "--legacy") _legacy = true;
        }
        _main = new Main { Interactive = false };
        AddChild(_main);
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--debug=")) _main.View.Material.SetShaderParameter("debug_view", int.Parse(arg["--debug=".Length..]));
            if (arg == "--noshadow")
                foreach (Node n in _main.GetChildren())
                    if (n is DirectionalLight3D sun) sun.ShadowEnabled = false;
        }
    }

    public override void _Process(double delta)
    {
        if (!_main.IsReady) return;
        switch (_stage)
        {
            case 0:
                if (_view == "map")
                {
                    SaveMap();
                    return;
                }
                // The camera is placed by hand; the player stays parked out of shot.
                _main.Player.ProcessMode = ProcessModeEnum.Disabled;
                _main.Tool.ShowCursor = false;
                _main.Hud.ToggleHelp();
                Setup();
                _stage = 1;
                break;
            case 1:
                if (_main.View.IsIdle && ++_frames >= SettleFrames)
                {
                    _beforeCapture?.Invoke();
                    _stage = 2;
                    _frames = 0;
                }
                break;
            case 2:
                if (++_frames == 8)
                {
                    _stage = 3;
                    Capture();
                }
                break;
        }
    }

    void Setup()
    {
        ChunkStore store = _main.Store;
        switch (_view)
        {
            case "landscape":
                Look(Ground(6f, 34f) + new Vector3(0f, 11f, 0f), Ground(-2f, -40f) + new Vector3(0f, 4f, 0f));
                break;

            case "pit":
            {
                Int3 c = RichestOreSpot(store, SimpleTerrainSource.Spawn + new System.Numerics.Vector2(10f, -12f), 14);
                DigPit(store, c, 6.5f, 10);
                var center = new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f);
                Look(center + new Vector3(2f, 10f, 9f), center + new Vector3(0f, -5f, -2f));

                // Fling debris off the far wall as the shot is taken: digging in action.
                _beforeCapture = () =>
                {
                    Int3 wall = new(c.X, c.Y - 4, c.Z - 5);
                    while (!store.IsSolid(wall) && wall.Z > c.Z - 10) wall -= new Int3(0, 0, 1);
                    EditResult r = store.Dig(wall, GatherTool.DigRadius);
                    _main.View.MarkDirty(r.DirtyChunks);
                    _main.Debris.Burst(r.Removed, center + new Vector3(0f, 2f, 6f), 1.2f);
                };
                break;
            }

            case "crater":
            {
                var at = new Vector2(-12f, -6f);
                Vector3 ground = Ground(at.X, at.Y);
                Look(ground + new Vector3(7f, 5f, 10f), ground + new Vector3(-0.5f, -1.5f, -0.5f));

                // Caught mid-blast: the crater is cut, debris and dust are in the air.
                _beforeCapture = () =>
                {
                    EditResult r = store.Crater(new System.Numerics.Vector3(ground.X, ground.Y + GatherTool.CraterLift, ground.Z), GatherTool.CraterRadius);
                    _main.View.MarkDirty(r.DirtyChunks);
                    _main.Debris.Burst(r.Removed, ground + Vector3.Down * 2f, 2.4f);
                    for (int i = 0; i < 6; i++) _main.Debris.Puff(ground + Vector3.Up * i * 0.6f, Palette.Dust, 3.5f);
                    _main.Flash(ground, new Color("ffb070"));
                };
                break;
            }

            case "row":
            {
                // A row of blocks stepping down a slope, each sunk half into the ground under it:
                // 2 m blocks square, turned and tilted, and 0.5 m ones in front.
                (Vector3 at, Vector3 downhill) = SlopeSpot(store, new System.Numerics.Vector2(-30f, 10f), 40, 22f);
                Vector3 across = downhill.Cross(Vector3.Up).Normalized();
                GD.Print($"row: slope at {at}, downhill {downhill}");
                var blocks = new List<(Vector3 at, float yaw, float pitch, float size)>
                {
                    (at - downhill * 4.5f, 0f, 0f, 2f),
                    (at - downhill * 1.5f, 20f, 0f, 2f),
                    (at + downhill * 1.5f, 45f, 6f, 2f),
                    (at + downhill * 4.5f, 70f, 0f, 2f),
                    (at - downhill * 3f + across * 2.4f, 0f, 0f, 0.5f),
                    (at - downhill * 1f + across * 2.4f, 33f, 0f, 0.5f),
                    (at + downhill * 1f + across * 2.4f, 33f, 8f, 0.5f),
                    (at + downhill * 3f + across * 2.4f, 61f, 0f, 0.5f),
                };
                var volumes = new List<StructureVolume>();
                foreach ((Vector3 p, float yaw, float pitch, float size) in blocks)
                {
                    float ground = GroundMesh(store, p.X, p.Z);
                    var q = System.Numerics.Quaternion.CreateFromYawPitchRoll(Mathf.DegToRad(yaw), Mathf.DegToRad(pitch), 0f);
                    volumes.Add(new StructureVolume(new System.Numerics.Vector3(p.X, ground, p.Z), q, new System.Numerics.Vector3(size)));
                }
                // From the side the small blocks are on, level with the row, looking across the slope
                // (framed on the untouched ground, so before and after match).
                Vector3 eye = at + across * 9.5f - downhill * 1.0f;
                eye.Y = GroundMesh(store, eye.X, eye.Z) + 2.4f;
                Vector3 target = at + across * 1.0f;
                target.Y = GroundMesh(store, target.X, target.Z) + 0.3f;
                SetInto(store, volumes, Palette.Block);
                Look(eye, target);
                break;
            }

            case "corner":
            {
                // A base's corner on a slope: a 3 x 3 floor of 2 m blocks turned 30°, its top level
                // with the highest ground under it (as a builder sets a floor, so the room is clear),
                // and two walls two blocks high meeting at the corner.
                (Vector3 at, Vector3 downhill) = SlopeSpot(store, new System.Numerics.Vector2(-40f, 2f), 30, 12f);
                var q = System.Numerics.Quaternion.CreateFromYawPitchRoll(Mathf.DegToRad(30f), 0f, 0f);
                var ax = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, q) * 2f;
                var az = System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, q) * 2f;
                float ground = float.MinValue;
                for (float i = -1.5f; i <= 1.5f; i += 0.25f)
                    for (float k = -1.5f; k <= 1.5f; k += 0.25f)
                    {
                        var p = new System.Numerics.Vector3(at.X, 0f, at.Z) + ax * i + az * k;
                        ground = MathF.Max(ground, GroundMesh(store, p.X, p.Z));
                    }
                var origin = new System.Numerics.Vector3(at.X, ground - 1f, at.Z);
                var volumes = new List<StructureVolume>();
                for (int i = -1; i <= 1; i++)
                    for (int k = -1; k <= 1; k++)
                        volumes.Add(new StructureVolume(origin + ax * i + az * k, q, new System.Numerics.Vector3(2f)));
                for (int up = 1; up <= 2; up++)
                {
                    var lift = System.Numerics.Vector3.UnitY * 2f * up;
                    for (int i = -1; i <= 1; i++) volumes.Add(new StructureVolume(origin + ax * i - az + lift, q, new System.Numerics.Vector3(2f)));
                    for (int k = 0; k <= 1; k++) volumes.Add(new StructureVolume(origin - ax + az * k + lift, q, new System.Numerics.Vector3(2f)));
                }
                SetInto(store, volumes, new Color("c9b48a"));
                // From outside the open corner, low, so the line where floor and ground meet shows.
                var c = new Vector3(origin.X, origin.Y, origin.Z);
                var outward = new Vector3(ax.X + az.X, 0f, ax.Z + az.Z).Normalized();
                Vector3 eye = c + outward * 8f + new Vector3(-outward.Z, 0f, outward.X) * 2.5f;
                eye.Y = GroundMesh(store, eye.X, eye.Z) + 2.3f;
                Look(eye, c + Vector3.Up * 0.6f);
                break;
            }

            case "block":
            {
                // Blocks pushed into a slope: each meets the ground flush, with nothing cut round it.
                Int3 slope = SteepSpot(store, SimpleTerrainSource.Spawn + new System.Numerics.Vector2(-4f, 14f), 16);
                var placed = new List<CellBox>();
                foreach (int dx in new[] { -3, 0 })
                {
                    int x = slope.X + dx, z = slope.Z;
                    int floor = (int)MathF.Floor(store.Source.SurfaceHeight(x + 1f, z + 2.5f));
                    var box = CellBox.FromSize(new Int3(x, floor, z - 1), new Int3(2, 2, 2));
                    if (_main.PlaceStructureAt(box).Verdict == PlaceVerdict.Placed) placed.Add(box);
                }
                GD.Print($"block: placed {placed.Count} blocks near {slope}");
                Vector3 target = placed.Count > 0
                    ? new Vector3(placed.Average(b => b.CenterPosition.X), placed.Average(b => b.CenterPosition.Y), placed.Average(b => b.CenterPosition.Z))
                    : new Vector3(slope.X, slope.Y, slope.Z);
                Look(target + new Vector3(5.5f, 2.2f, 4.5f), target + new Vector3(-0.5f, -0.4f, -0.5f));
                break;
            }
        }
    }

    Vector3 Ground(float x, float z) => new(x, _main.Store.Source.SurfaceHeight(x, z), z);

    /// <summary>The height of the drawn ground at a point (the mesh, not the generator's smooth height).</summary>
    static float GroundMesh(ChunkStore store, float x, float z)
    {
        float top = store.TopSolid((int)MathF.Floor(x), (int)MathF.Floor(z)) + 2f;
        for (float y = top; y > top - 6f; y -= 0.01f)
            if (store.SampleDensity(new System.Numerics.Vector3(x, y, z)) > 0f) return y;
        return store.Source.SurfaceHeight(x, z);
    }

    /// <summary>Blocks set into the ground: through ClearForStructure, or with --legacy the old way.</summary>
    void SetInto(ChunkStore store, List<StructureVolume> volumes, Color color)
    {
        for (int i = 0; i < volumes.Count; i++)
        {
            var key = new VolumeKey(1, i, 0, 0);
            if (_legacy) _main.Structures.Add(key, volumes[i], color);
            else _main.PlaceOriented(key, volumes[i], color);
        }
        if (_legacy)
        {
            int outside = DigTest.LegacyClear(store, volumes);
            _main.View.MarkDirty(store.EditedChunks.SelectMany(c => new[] { c }).ToList());
            GD.Print($"legacy clearing: {store.RemovedCount} cells, {outside} of them outside the blocks");
        }
        else GD.Print($"flush: {store.RemovedCount} cells cleared, {store.Volumes.Count} volumes");
    }

    /// <summary>A point near here whose ground slopes at about the given angle, and its downhill direction.</summary>
    static (Vector3 at, Vector3 downhill) SlopeSpot(ChunkStore store, System.Numerics.Vector2 near, int radius, float degrees)
    {
        Vector3 best = default, bestDown = Vector3.Forward;
        float bestOff = float.MaxValue;
        for (int z = -radius; z <= radius; z += 2)
            for (int x = -radius; x <= radius; x += 2)
            {
                float px = near.X + x, pz = near.Y + z;
                if (store.IsWaterColumn((int)px, (int)pz)) continue;
                float gx = (store.Source.SurfaceHeight(px + 1f, pz) - store.Source.SurfaceHeight(px - 1f, pz)) * 0.5f;
                float gz = (store.Source.SurfaceHeight(px, pz + 1f) - store.Source.SurfaceHeight(px, pz - 1f)) * 0.5f;
                float grade = MathF.Sqrt(gx * gx + gz * gz);
                // Even ground: the grade a few metres off should match, no mesa lips.
                float g2x = (store.Source.SurfaceHeight(px + 5f, pz) - store.Source.SurfaceHeight(px + 3f, pz)) * 0.5f;
                float g2z = (store.Source.SurfaceHeight(px, pz + 5f) - store.Source.SurfaceHeight(px, pz + 3f)) * 0.5f;
                float spread = MathF.Abs(MathF.Sqrt(g2x * g2x + g2z * g2z) - grade);
                foreach ((float dx, float dz) in new[] { (-4f, 0f), (0f, -4f), (4f, 4f), (-4f, 4f), (4f, -4f), (-4f, -4f) })
                {
                    float hx = (store.Source.SurfaceHeight(px + dx + 1f, pz + dz) - store.Source.SurfaceHeight(px + dx - 1f, pz + dz)) * 0.5f;
                    float hz = (store.Source.SurfaceHeight(px + dx, pz + dz + 1f) - store.Source.SurfaceHeight(px + dx, pz + dz - 1f)) * 0.5f;
                    spread = MathF.Max(spread, MathF.Abs(hx - gx) + MathF.Abs(hz - gz));
                }
                float off = MathF.Abs(Mathf.RadToDeg(MathF.Atan(grade)) - degrees) + 40f * spread;
                if (off < bestOff && grade > 1e-3f)
                {
                    bestOff = off;
                    best = new Vector3(px, store.Source.SurfaceHeight(px, pz), pz);
                    bestDown = new Vector3(-gx, 0f, -gz).Normalized();
                }
            }
        return (best, bestDown);
    }

    void Look(Vector3 from, Vector3 at)
    {
        _main.Camera.LookAtFromPosition(from, at, Vector3.Up);
        _main.Camera.ResetPhysicsInterpolation();
    }

    /// <summary>The column near a point with the most ore within 9 m of the surface.</summary>
    static Int3 RichestOreSpot(ChunkStore store, System.Numerics.Vector2 near, int radius)
    {
        Int3 best = default;
        int bestCount = -1;
        for (int z = -radius; z <= radius; z += 2)
            for (int x = -radius; x <= radius; x += 2)
            {
                int cx = (int)near.X + x, cz = (int)near.Y + z;
                int top = (int)MathF.Floor(store.Source.SurfaceHeight(cx + 0.5f, cz + 0.5f));
                if (store.IsWaterColumn(cx, cz)) continue;
                int count = 0;
                for (int dz = -4; dz <= 4; dz++)
                    for (int dx = -4; dx <= 4; dx++)
                        for (int dy = 1; dy < 9; dy++)
                            if (store.MaterialAt(new Int3(cx + dx, top - dy, cz + dz)) == CellMaterial.Ore) count++;
                if (count > bestCount)
                {
                    bestCount = count;
                    best = new Int3(cx, top, cz);
                }
            }
        return best;
    }

    /// <summary>A spot where the ground rises steeply towards -z, for setting blocks into.</summary>
    static Int3 SteepSpot(ChunkStore store, System.Numerics.Vector2 near, int radius)
    {
        Int3 best = default;
        float bestRise = -1f;
        for (int z = -radius; z <= radius; z++)
            for (int x = -radius; x <= radius; x++)
            {
                float px = near.X + x, pz = near.Y + z;
                float rise = store.Source.SurfaceHeight(px, pz - 1.5f) - store.Source.SurfaceHeight(px, pz + 1.5f);
                if (rise > bestRise && rise < 5f && !store.IsWaterColumn((int)px, (int)pz))
                {
                    bestRise = rise;
                    best = new Int3((int)px, (int)store.Source.SurfaceHeight(px, pz), (int)pz);
                }
            }
        return best;
    }

    /// <summary>A stepped pit, dug with the same rules as the tool.</summary>
    void DigPit(ChunkStore store, Int3 center, float radius, int depth)
    {
        var cells = new List<Int3>();
        for (int d = 0; d < depth + 4; d++)
        {
            float r = radius - Math.Max(0, d - 3) * 0.55f;
            int y = center.Y + 3 - d;
            for (int z = -(int)radius; z <= radius; z++)
                for (int x = -(int)radius; x <= radius; x++)
                    if (x * x + z * z <= r * r)
                        cells.Add(new Int3(center.X + x, y, center.Z + z));
        }
        EditResult result = store.DigCells(cells);
        _main.View.MarkDirty(result.DirtyChunks);
        foreach (RemovedCell r in result.Removed) _main.Tool.Gathered[(int)r.Material]++; // as if the player dug it
        GD.Print($"pit: removed {result.Removed.Count} cells, {result.Removed.Count(c => c.Material == CellMaterial.Ore)} ore");
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    /// <summary>Top-down layout map: height shading, water, ore near the surface, caves, the POI.</summary>
    void SaveMap()
    {
        ChunkStore store = _main.Store;
        CellBox w = ChunkStore.WorldCells;
        int size = w.Size.X;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgb8);
        for (int z = 0; z < size; z++)
            for (int x = 0; x < size; x++)
            {
                int wx = w.Min.X + x, wz = w.Min.Z + z;
                float h = store.Source.SurfaceHeight(wx + 0.5f, wz + 0.5f);
                float shade = Mathf.Clamp((h + 5f) / 70f, 0f, 1f);
                Color c = Palette.Soil.Darkened(0.6f).Lerp(Palette.Dust, shade);
                if (store.IsWaterColumn(wx, wz)) c = Palette.WaterShallow;
                int top = (int)MathF.Floor(h);
                int ore = 0, cave = 0;
                for (int d = 0; d < 10; d++)
                {
                    CellMaterial m = store.MaterialAt(new Int3(wx, top - d, wz));
                    if (m == CellMaterial.Ore) ore++;
                    if (m == CellMaterial.Air && d > 1) cave++;
                }
                if (ore > 0) c = c.Lerp(Palette.Ore, Mathf.Min(1f, ore / 3f));
                if (cave > 0) c = c.Lerp(Colors.Black, 0.6f);
                if (x % 16 == 0 || z % 16 == 0) c = c.Darkened(0.15f);
                image.SetPixel(x, z, c);
            }
        foreach (PoiSite poi in store.Source.PointsOfInterest)
            for (int z = poi.Footprint.Min.Z; z < poi.Footprint.Max.Z; z++)
                for (int x = poi.Footprint.Min.X; x < poi.Footprint.Max.X; x++)
                    image.SetPixel(x - w.Min.X, z - w.Min.Z, Palette.BeaconRed);
        var spawn = SimpleTerrainSource.Spawn;
        image.SetPixel((int)spawn.X - w.Min.X, (int)spawn.Y - w.Min.Z, Colors.White);
        image.Resize(size * 3, size * 3, Image.Interpolation.Nearest);
        image.SavePng(_out);
        GD.Print($"saved {_out}");
        GetTree().Quit(0);
    }
}
