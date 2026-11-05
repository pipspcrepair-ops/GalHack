using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>
/// Renders one view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/terrain-digging res://tests/Screenshot.tscn -- --view=pit --out=shot.png
/// Views: landscape, pit, crater, block. "map" writes a top-down layout map (works headless).
/// Extras: --debug=1 shows normals, --debug=2 unlit albedo; --noshadow turns the sun's shadows off.
/// </summary>
public partial class Screenshot : Node
{
    const int SettleFrames = 45;

    Main _main;
    string _view = "landscape";
    string _out = "shot.png";
    int _stage, _frames;
    Action _beforeCapture;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
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

            case "block":
            {
                // Blocks pushed into a slope: each one sits in the notch it cleared.
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
