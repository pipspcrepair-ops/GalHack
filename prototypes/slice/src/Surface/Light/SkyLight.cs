using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Vessels;
using TerrainDigging.Terrain;
using N = System.Numerics;
using PInt3 = PoiGenerator.Gen.Int3;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace Slice.Surface;

/// <summary>
/// Keeps a <see cref="SkyField"/> around the eye: what stands in the way (terrain as dug, the
/// planet's places, bases and landed vessels) is gathered and flooded on a worker, and the result
/// goes to the shaders as one small 3D texture. Nothing here holds the main thread for more than a
/// snapshot and an upload, and it only runs when something changed: the eye moved a few metres,
/// the ground was dug, a door moved, a structure changed.
///
/// <see cref="LightAt"/> is the one answer to "how lit is this spot" (creature senses, tests).
/// </summary>
public partial class SkyLight : Node
{
    public SurfaceZone Zone;

    /// <summary>Off in space: no field, and everything reads as open.</summary>
    public bool Active = true;

    /// <summary>Where the game finds its standing places (set by whoever owns them).</summary>
    public Func<IEnumerable<Place>> Places;

    /// <summary>The field now being shown (null before the first).</summary>
    public SkyField Field { get; private set; }

    /// <summary>The worker's last run and the upload's last and worst time, ms (tests and the report).</summary>
    public double LastFloodMs { get; private set; }
    public double WorstFloodMs { get; private set; }
    public double LastUploadMs { get; private set; }
    public double WorstUploadMs { get; private set; }
    public int Floods { get; private set; }

    /// <summary>How open the eye is, followed gently: 1 outdoors, 0 in the dark.</summary>
    public float EyeOpen { get; private set; } = 1f;

    const int MoveCells = 6;
    const double Spacing = 0.1, MovingSpacing = 0.3, Poll = 0.2;
    const int GroundTexels = 96, GroundCell = 16;

    SkyField _spare = new();
    Task<(SkyField Field, double Ms)> _job;
    Task<(float[] Heights, float X, float Z)> _groundJob;
    ImageTexture3D _texture;
    ImageTexture _groundTexture;
    Image _groundImage;
    Vector4 _groundRect;
    readonly Godot.Collections.Array<Image> _layers = new();
    readonly byte[] _layer = new byte[SkyField.SizeX * SkyField.SizeZ];
    bool _dirty = true, _nowhere;
    double _since, _sincePoll;
    long _key;
    float _groundX = float.MaxValue, _groundZ;

    /// <summary>Something that stops light changed: the field is redone soon.</summary>
    public void Touch() => _dirty = true;

    /// <summary>Everything in the zone just moved by a whole planet: the field goes with it, so a seam shows nothing.</summary>
    public void Shift(Vector3 by)
    {
        int dx = Mathf.RoundToInt(by.X), dz = Mathf.RoundToInt(by.Z);
        _groundX += by.X;
        _groundZ += by.Z;
        _groundRect += new Vector4(by.X, by.Z, 0f, 0f);
        _dirty = true;
        if (Field == null) return;
        Field.MinX += dx;
        Field.MinZ += dz;
        Place(new Vector3(Field.MinX, Field.MinY, Field.MinZ), _groundRect);
    }

    public override void _Process(double delta)
    {
        if (!Active)
        {
            if (!_nowhere) Place(SkyLit.Nowhere, new Vector4(SkyLit.Nowhere.X, SkyLit.Nowhere.Z, 1f, 1f));
            _nowhere = true;
            EyeOpen = 1f;
            return;
        }
        if (Zone?.Planet == null) return;
        _since += delta;
        _sincePoll += delta;
        Finish();
        Vector3 eye = Zone.Camera?.GlobalPosition ?? Zone.Focus();
        if (_sincePoll >= Poll)
        {
            _sincePoll = 0;
            long key = ChangeKey();
            if (key != _key) (_key, _dirty) = (key, true);
        }
        (int x, int y, int z) = Corner(eye);
        bool moved = Field == null || Math.Abs(x - Field.MinX) >= MoveCells || Math.Abs(y - Field.MinY) >= MoveCells || Math.Abs(z - Field.MinZ) >= MoveCells;
        if ((_dirty || moved) && _job == null && _since >= (_dirty ? Spacing : MovingSpacing)) Start(x, y, z);
        if (_groundJob == null && (MathF.Abs(eye.X - _groundX) > GroundCell * 4 || MathF.Abs(eye.Z - _groundZ) > GroundCell * 4)) StartGround(eye);
        EyeOpen += (Openness(eye) - EyeOpen) * Math.Clamp((float)delta * 4f, 0f, 1f);
    }

    static (int, int, int) Corner(Vector3 eye) => (
        Mathf.FloorToInt(eye.X) - SkyField.SizeX / 2,
        Mathf.FloorToInt(eye.Y) - SkyField.SizeY / 2,
        Mathf.FloorToInt(eye.Z) - SkyField.SizeZ / 2);

    /// <summary>Thins the haze inside: air you can't see the sky from isn't lit by it. Runs after the sky has set it.</summary>
    public void DimFog(Godot.Environment env)
    {
        float k = Mathf.SmoothStep(0f, 0.2f, EyeOpen);
        env.FogDensity *= k;
        env.FogHeightDensity *= k;
    }

    // ---- What changed ---------------------------------------------------------------------------

    /// <summary>A number that moves when a door opens or a structure changes.</summary>
    long ChangeKey()
    {
        long key = 0;
        foreach (Vessel v in Zone.Vessels)
            if (Stands(v)) key = key * 31 + v.Structure.Version + 1;
        foreach (Place p in Standing())
            foreach (Place.Door d in p.Doors)
                key = key * 31 + (d.Open > 0.3f ? 1 : 0);
        return key;
    }

    IEnumerable<Place> Standing() => Places?.Invoke() ?? Array.Empty<Place>();

    /// <summary>Stood still in the world: a flying vessel moves faster than the field does, and carries its own dark (<see cref="StructureSky"/>).</summary>
    static bool Stands(Vessel v) => v.IsBase || v.Body.Velocity.LengthSquared() < 0.25f;

    // ---- The worker -----------------------------------------------------------------------------

    sealed class Snapshot
    {
        public int X, Y, Z;
        public BaseCache Base;
        public PlanetSurface.World.PlanetWorld World;
        public readonly Dictionary<TInt3, ulong[]> Masks = new();
        public readonly List<(BlockGrid Grid, CellMask Occupied, int X, int Y, int Z)> Sites = new();
        public readonly List<(int X, int Y, int Z)> OpenDoors = new();
        public readonly List<(N.Vector3[] Cells, Transform3D At, float Size)> Structures = new();
    }

    void Start(int x, int y, int z)
    {
        _dirty = false;
        _since = 0;
        ChunkStore store = Zone.Planet.Terrain.Store;
        var snap = new Snapshot { X = x, Y = y, Z = z, Base = store.Base, World = Zone.Planet };
        for (int cy = y >> ChunkStore.Shift; cy <= (y + SkyField.SizeY - 1) >> ChunkStore.Shift; cy++)
        for (int cz = z >> ChunkStore.Shift; cz <= (z + SkyField.SizeZ - 1) >> ChunkStore.Shift; cz++)
        for (int cx = x >> ChunkStore.Shift; cx <= (x + SkyField.SizeX - 1) >> ChunkStore.Shift; cx++)
        {
            var c = new TInt3(cx, cy, cz);
            if (store.RemovedMask(c) is { } mask) snap.Masks[c] = mask;
        }
        var near = new Vector3(x + SkyField.SizeX / 2, 0f, z + SkyField.SizeZ / 2);
        foreach (PlanetSurface.Poi.PoiSite s in Zone.Planet.Pois)
        {
            BlockGrid g = s.Instance?.Blocks;
            if (g == null) continue;
            Vector3 at = Zone.Planet.NearestCopy(new Vector3(s.Origin.X, s.Origin.Y, s.Origin.Z), near);
            int ox = Mathf.RoundToInt(at.X), oy = s.Origin.Y, oz = Mathf.RoundToInt(at.Z);
            if (ox + g.Max.X < x || ox + g.Min.X >= x + SkyField.SizeX || oz + g.Max.Z < z || oz + g.Min.Z >= z + SkyField.SizeZ) continue;
            snap.Sites.Add((g, s.Instance.Layout.Occupied, ox, oy, oz));
        }
        foreach (Place p in Standing())
        {
            Vector3 o = p.GlobalPosition;
            foreach (Place.Door d in p.Doors)
                if (d.Open > 0.3f && d.Cells != null)
                    foreach (PInt3 c in d.Cells) snap.OpenDoors.Add((c.X + Mathf.RoundToInt(o.X), c.Y + Mathf.RoundToInt(o.Y), c.Z + Mathf.RoundToInt(o.Z)));
        }
        foreach (Vessel v in Zone.Vessels)
            if (Stands(v)) snap.Structures.Add((StructureSky.Stops(v.Structure), v.Node.GlobalTransform, v.Structure.Grid.CellSize));
        SkyField spare = _spare;
        _spare = null;
        _job = Task.Run(() =>
        {
            long t = System.Diagnostics.Stopwatch.GetTimestamp();
            Fill(spare, snap);
            spare.Flood();
            return (spare, (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
        });
    }

    static void Fill(SkyField f, Snapshot s)
    {
        f.Clear(s.X, s.Y, s.Z);
        FillTerrain(f, s);
        FillSites(f, s);
        foreach (var (cells, at, size) in s.Structures) FillStructure(f, cells, at, size);
        foreach (var (x, y, z) in s.OpenDoors)
            if (f.Holds(x, y, z)) f.Solid[SkyField.Index(x - f.MinX, y - f.MinY, z - f.MinZ)] = 0;
        // The land over the grid's top, every few metres: a shaft that deep is dark at the bottom anyway.
        const int Every = 4;
        int top = f.MinY + SkyField.SizeY;
        for (int z = 0; z < SkyField.SizeZ; z += Every)
        for (int x = 0; x < SkyField.SizeX; x += Every)
        {
            if (s.World.GroundHeight(Wrap(f.MinX + x + Every * 0.5, s.World.Width), Wrap(f.MinZ + z + Every * 0.5, s.World.Height)) <= top + 1f) continue;
            for (int dz = 0; dz < Every; dz++) Array.Fill(f.Roofed, true, (z + dz) * SkyField.SizeX + x, Every);
        }
    }

    static double Wrap(double v, double span) => (v % span + span) % span;

    static void FillTerrain(SkyField f, Snapshot s)
    {
        int x0 = f.MinX, y0 = f.MinY, z0 = f.MinZ;
        int x1 = x0 + SkyField.SizeX, y1 = y0 + SkyField.SizeY, z1 = z0 + SkyField.SizeZ;
        for (int cy = y0 >> ChunkStore.Shift; cy <= (y1 - 1) >> ChunkStore.Shift; cy++)
        for (int cz = z0 >> ChunkStore.Shift; cz <= (z1 - 1) >> ChunkStore.Shift; cz++)
        for (int cx = x0 >> ChunkStore.Shift; cx <= (x1 - 1) >> ChunkStore.Shift; cx++)
        {
            var c = new TInt3(cx, cy, cz);
            BaseChunk chunk = s.Base.Get(c);
            s.Masks.TryGetValue(c, out ulong[] mask);
            int ox = cx * ChunkStore.Size, oy = cy * ChunkStore.Size, oz = cz * ChunkStore.Size;
            int ax = Math.Max(0, x0 - ox), bx = Math.Min(ChunkStore.Size, x1 - ox);
            int ay = Math.Max(0, y0 - oy), by = Math.Min(ChunkStore.Size, y1 - oy);
            int az = Math.Max(0, z0 - oz), bz = Math.Min(ChunkStore.Size, z1 - oz);
            for (int ly = ay; ly < by; ly++)
            for (int lz = az; lz < bz; lz++)
            for (int lx = ax; lx < bx; lx++)
            {
                int i = lx | (lz << ChunkStore.Shift) | (ly << (2 * ChunkStore.Shift));
                if (chunk.DensityAt(i) <= 0f || (mask != null && (mask[i >> 6] & (1ul << (i & 63))) != 0)) continue;
                f.Solid[SkyField.Index(ox + lx - x0, oy + ly - y0, oz + lz - z0)] = 1;
            }
        }
    }

    /// <summary>Stops light: whole blocks and shut doors. Glass, panes, posts and frames let it by.</summary>
    static bool Stops(in Block b) => b.Type == BlockType.Door
        ? b.Variant != DoorState.Open
        : BlockInfo.IsFullCube(b.Type) && !BlockInfo.IsTransparent(b.Material);

    static void FillSites(SkyField f, Snapshot s)
    {
        int top = f.MinY + SkyField.SizeY;
        foreach (var (g, occupied, ox, oy, oz) in s.Sites)
        {
            int x0 = Math.Max(g.Min.X + ox, f.MinX), x1 = Math.Min(g.Max.X + ox, f.MinX + SkyField.SizeX - 1);
            int y0 = Math.Max(g.Min.Y + oy, f.MinY), y1 = Math.Min(g.Max.Y + oy, top - 1);
            int z0 = Math.Max(g.Min.Z + oz, f.MinZ), z1 = Math.Min(g.Max.Z + oz, f.MinZ + SkyField.SizeZ - 1);
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                var c = new PInt3(x - ox, y - oy, z - oz);
                Block b = g.Get(c);
                int i = SkyField.Index(x - f.MinX, y - f.MinY, z - f.MinZ);
                if (Stops(b)) f.Solid[i] = 1;
                else if (occupied[c]) f.Solid[i] = 0; // the place's own air: its rooms are what the terrain was cut for
            }
            // A tower taller than the grid: what stands over the grid's top still roofs the columns under it.
            if (g.Max.Y + oy < top) continue;
            for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
                for (int y = top; y <= g.Max.Y + oy; y++)
                    if (Stops(g.Get(new PInt3(x - ox, y - oy, z - oz))))
                    {
                        f.Roofed[(z - f.MinZ) * SkyField.SizeX + x - f.MinX] = true;
                        break;
                    }
        }
    }

    /// <summary>A structure's stopping blocks, into the metre cells they touch.</summary>
    static void FillStructure(SkyField f, N.Vector3[] blocks, Transform3D at, float size)
    {
        int n = Math.Max(1, Mathf.CeilToInt(size * 2f));
        float step = size / n;
        foreach (N.Vector3 b in blocks)
        {
            Vector3 c = at * new Vector3(b.X, b.Y, b.Z);
            if (!f.Holds(Mathf.FloorToInt(c.X), Mathf.FloorToInt(c.Y), Mathf.FloorToInt(c.Z))) continue;
            for (int k = 0; k < n; k++)
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                Vector3 p = at * new Vector3(b.X + (i + 0.5f) * step - size * 0.5f, b.Y + (j + 0.5f) * step - size * 0.5f, b.Z + (k + 0.5f) * step - size * 0.5f);
                int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y), z = Mathf.FloorToInt(p.Z);
                if (f.Holds(x, y, z)) f.Solid[SkyField.Index(x - f.MinX, y - f.MinY, z - f.MinZ)] = 1;
            }
        }
    }

    void Finish()
    {
        if (_groundJob is { IsCompleted: true })
        {
            var (heights, gx, gz) = _groundJob.Result;
            _groundJob = null;
            ShowGround(heights, gx, gz);
        }
        if (_job is not { IsCompleted: true }) return;
        var (field, ms) = _job.Result;
        _job = null;
        LastFloodMs = ms;
        WorstFloodMs = Math.Max(WorstFloodMs, ms);
        Floods++;
        long t = System.Diagnostics.Stopwatch.GetTimestamp();
        Show(field);
        LastUploadMs = (System.Diagnostics.Stopwatch.GetTimestamp() - t) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        WorstUploadMs = Math.Max(WorstUploadMs, LastUploadMs);
        _spare = Field ?? new SkyField();
        Field = field;
    }

    // ---- The shaders' copy ----------------------------------------------------------------------

    void Show(SkyField f)
    {
        const int W = SkyField.SizeX, H = SkyField.SizeZ, Layer = W * H;
        for (int y = 0; y < SkyField.SizeY; y++)
        {
            Buffer.BlockCopy(f.Open, y * Layer, _layer, 0, Layer);
            if (_layers.Count <= y) _layers.Add(Image.CreateFromData(W, H, false, Image.Format.R8, _layer));
            else _layers[y].SetData(W, H, false, Image.Format.R8, _layer);
        }
        if (_texture == null)
        {
            _texture = new ImageTexture3D();
            _texture.Create(Image.Format.R8, W, H, SkyField.SizeY, false, _layers);
            RenderingServer.GlobalShaderParameterSet("sky_field", _texture.GetRid());
        }
        else _texture.Update(_layers);
        _nowhere = false;
        Place(new Vector3(f.MinX, f.MinY, f.MinZ), _groundRect);
    }

    static void Place(Vector3 field, Vector4 ground)
    {
        RenderingServer.GlobalShaderParameterSet("sky_field_min", field);
        RenderingServer.GlobalShaderParameterSet("sky_ground_rect", ground);
    }

    /// <summary>The land under the wider view, low-biased: only what is well under the ground reads as buried.</summary>
    void StartGround(Vector3 eye)
    {
        float x0 = eye.X - GroundTexels * GroundCell / 2f, z0 = eye.Z - GroundTexels * GroundCell / 2f;
        PlanetSurface.World.PlanetWorld w = Zone.Planet;
        _groundX = eye.X;
        _groundZ = eye.Z;
        _groundJob = Task.Run(() =>
        {
            var heights = new float[GroundTexels * GroundTexels];
            for (int z = 0; z < GroundTexels; z++)
            for (int x = 0; x < GroundTexels; x++)
            {
                double px = x0 + (x + 0.5) * GroundCell, pz = z0 + (z + 0.5) * GroundCell;
                float low = float.MaxValue;
                for (int s = 0; s < 5; s++)
                {
                    double ax = s == 1 ? 8 : s == 2 ? -8 : 0, az = s == 3 ? 8 : s == 4 ? -8 : 0;
                    low = MathF.Min(low, w.GroundHeight(Wrap(px + ax, w.Width), Wrap(pz + az, w.Height)));
                }
                heights[z * GroundTexels + x] = low;
            }
            return (heights, x0, z0);
        });
    }

    void ShowGround(float[] heights, float x0, float z0)
    {
        var bytes = new byte[heights.Length * 4];
        Buffer.BlockCopy(heights, 0, bytes, 0, bytes.Length);
        if (_groundImage == null)
        {
            _groundImage = Image.CreateFromData(GroundTexels, GroundTexels, false, Image.Format.Rf, bytes);
            _groundTexture = ImageTexture.CreateFromImage(_groundImage);
            RenderingServer.GlobalShaderParameterSet("sky_ground", _groundTexture.GetRid());
        }
        else
        {
            _groundImage.SetData(GroundTexels, GroundTexels, false, Image.Format.Rf, bytes);
            _groundTexture.Update(_groundImage);
        }
        _groundRect = new Vector4(x0, z0, GroundTexels * GroundCell, GroundTexels * GroundCell);
        if (!_nowhere) RenderingServer.GlobalShaderParameterSet("sky_ground_rect", _groundRect);
    }

    // ---- Questions ------------------------------------------------------------------------------

    /// <summary>How open to the sky a point in the world is, 0 to 1, whatever the hour.</summary>
    public float Openness(Vector3 p)
    {
        if (Field?.At(Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z)) is { } v) return v;
        PlanetSurface.World.PlanetWorld w = Zone?.Planet;
        return w == null || p.Y >= w.GroundHeight(Wrap(p.X, w.Width), Wrap(p.Z, w.Height)) - 4f ? 1f : 0f;
    }

    /// <summary>What open sky is worth as light now: 1 at noon, a little at night.</summary>
    float SkyPower => 0.06f + 0.94f * (Zone?.Sky?.Daylight ?? 1f);

    /// <summary>
    /// How lit a point is, roughly 0 (pitch dark) to 1 (noon in the open): the daylight that reaches
    /// it plus every real light in reach with a clear line to it (lamps, fires, the head torch).
    /// For a few calls a frame, no more: it walks each light's line.
    /// </summary>
    public float LightAt(Vector3 p)
    {
        float open = Openness(p);
        float total = open * open * SkyPower;
        foreach (Light3D l in SkyLit.Lights)
        {
            if (!IsInstanceValid(l) || !l.IsVisibleInTree()) continue;
            Vector3 at = l.GlobalPosition;
            float range = l switch { OmniLight3D o => o.OmniRange, SpotLight3D s => s.SpotRange, _ => 0f };
            float d = at.DistanceTo(p);
            if (d >= range) continue;
            if (l is SpotLight3D spot)
            {
                Vector3 forward = -l.GlobalTransform.Basis.Z;
                if (d > 0.01f && forward.Dot((p - at) / d) < MathF.Cos(Mathf.DegToRad(spot.SpotAngle))) continue;
            }
            if (Blocked(p, at)) continue;
            float fall = 1f - d / range;
            total += l.LightEnergy * fall * fall;
        }
        return total;
    }

    /// <summary>Whether something solid stands between two points (the light's own last metre aside: lamps sit in their walls).</summary>
    bool Blocked(Vector3 a, Vector3 b)
    {
        if (Field == null) return false;
        float d = a.DistanceTo(b);
        for (float t = 0.5f; t < d - 1f; t += 0.5f)
        {
            Vector3 q = a.Lerp(b, t / d);
            if (Field.IsSolid(Mathf.FloorToInt(q.X), Mathf.FloorToInt(q.Y), Mathf.FloorToInt(q.Z))) return true;
        }
        return false;
    }
}
