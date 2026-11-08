using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GalHack.Plants;
using Godot;

namespace PlanetSurface.Terrain;

/// <summary>
/// Ground cover near the player: one multimesh per cover species, following the focus, so it costs
/// a handful of draw calls wherever you stand, and no node churn at chunk load. Cells are laid out on
/// workers (<see cref="CoverBuilder"/>) and kept; when the focus crosses a cell, or new cells land, the
/// instances in reach are copied into each species' one buffer, the far cells giving only their
/// far-reaching instances.
/// </summary>
public partial class TerrainStreamer
{
    const int CoverRadius = (int)(CoverBuilder.FarReach / CoverBuilder.CellSize) + 1;

    /// <summary>The longest a finished cell waits before it's copied in, s: often enough, without doing it every frame.</summary>
    const double CoverRefresh = 0.2;

    sealed class CoverDraw
    {
        public MultiMeshInstance3D Node;
        public MultiMesh Mesh;
        public float[] Buffer = System.Array.Empty<float>();
        public int Filled, Capacity;
    }

    readonly Dictionary<(int, int), CoverCell> _coverCells = new();
    readonly HashSet<(int, int)> _coverPending = new();
    readonly ConcurrentQueue<CoverCell> _coverDone = new();
    readonly Dictionary<int, CoverDraw> _coverDraws = new();
    Node3D _coverRoot;
    ShaderMaterial _coverMaterial;
    int _coverVersion, _coverFocusX = int.MinValue, _coverFocusZ;
    bool _coverDirty;
    long _coverLast;
    int _coverEvict;

    /// <summary>Whether ground cover draws at all (a graphics option, and how the cost of it was measured).</summary>
    public bool CoverVisible
    {
        get => _coverRoot?.Visible ?? false;
        set { if (_coverRoot != null) _coverRoot.Visible = value; }
    }

    /// <summary>Cover instances being drawn right now, and the multimeshes holding them.</summary>
    public int CoverInstances { get; private set; }
    public int CoverDrawCalls { get; private set; }

    /// <summary>The ground cover within a radius of a point (frame coordinates), for tests: each tuft's species and where it roots.</summary>
    public IEnumerable<(int Plant, Vector3 Root)> CoverNear(Vector3 at, float radius)
    {
        foreach (var (plant, draw) in _coverDraws)
            for (int i = 0; i < draw.Filled; i++)
            {
                int o = i * 16;
                var p = new Vector3(draw.Buffer[o + 3], draw.Buffer[o + 7], draw.Buffer[o + 11]) + _coverRoot.Position;
                if (new Vector2(p.X - at.X, p.Z - at.Z).Length() <= radius) yield return (plant, p);
            }
    }

    void StartCover()
    {
        _coverRoot = new Node3D { Name = "Cover", PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(_coverRoot);
        _coverMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/flora_cover.gdshader") };
    }

    void StreamCover()
    {
        if (_coverRoot == null || World.Flora.Book.Barren) return;
        int width = World.Terrain.Width / CoverBuilder.CellSize, height = World.Terrain.Height / CoverBuilder.CellSize;
        int fx = Mathf.FloorToInt(Focus.X / CoverBuilder.CellSize), fz = Mathf.FloorToInt(Focus.Z / CoverBuilder.CellSize);

        while (_coverDone.TryDequeue(out CoverCell done))
        {
            _coverPending.Remove((done.Cx, done.Cz));
            if (done.Version != _coverVersion) continue;
            _coverCells[(done.Cx, done.Cz)] = done;
            _coverDirty = true;
            NoteCover(done);
        }

        // Nearest missing cells first, a couple at a time: the workers are the far rings' too. A moving
        // focus asks round the point it will be at in a second and a half, so cover is in before it is in reach.
        int asked = 0, most = Heading.LengthSquared() > 25f ? 3 : 2;
        Vector3 lead = new Vector3(Heading.X, 0f, Heading.Z) * 1.5f;
        lead = lead.LimitLength(CoverBuilder.FarReach * 0.75f);
        int ax = fx + Mathf.RoundToInt(lead.X / CoverBuilder.CellSize), az = fz + Mathf.RoundToInt(lead.Z / CoverBuilder.CellSize);
        for (int r = 0; r <= CoverRadius && asked < most && _coverPending.Count < most * 2; r++)
        for (int dz = -r; dz <= r && asked < most; dz++)
        for (int dx = -r; dx <= r && asked < most; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
            var key = (Mod(ax + dx, width), Mod(az + dz, height));
            if (_coverCells.ContainsKey(key) || !_coverPending.Add(key)) continue;
            asked++;
            int cx = key.Item1, cz = key.Item2, version = _coverVersion;
            PlanetSurface.World.PlanetWorld world = World;
            HashSet<long> dug = _dugSnapshot;
            Task.Run(() =>
            {
                CoverCell cell = CoverBuilder.Build(cx, cz, world.Source, world.Palette, dug);
                cell.Version = version;
                _coverDone.Enqueue(cell);
            });
        }

        bool moved = fx != _coverFocusX || fz != _coverFocusZ;
        if (moved || (_coverDirty && Stopwatch.GetElapsedTime(_coverLast).TotalSeconds > CoverRefresh)) FillCover(fx, fz, width, height);

        // Cells well behind the player go, so a long walk doesn't keep them all.
        if (++_coverEvict < 120) return;
        _coverEvict = 0;
        List<(int, int)> stale = null;
        foreach (CoverCell c in _coverCells.Values)
        {
            int dx = Math.Abs(Mathf.PosMod(c.Cx - fx + width / 2, width) - width / 2), dz = Math.Abs(Mathf.PosMod(c.Cz - fz + height / 2, height) - height / 2);
            if (Math.Max(dx, dz) > CoverRadius + 6) (stale ??= new List<(int, int)>()).Add((c.Cx, c.Cz));
        }
        if (stale != null) foreach (var key in stale) _coverCells.Remove(key);
    }

    /// <summary>Copies every cell in reach into its species' buffer, placed round the focus, and hands them over.</summary>
    void FillCover(int fx, int fz, int width, int height)
    {
        _coverFocusX = fx;
        _coverFocusZ = fz;
        _coverDirty = false;
        _coverLast = Stopwatch.GetTimestamp();
        foreach (CoverDraw d in _coverDraws.Values) d.Filled = 0;
        Vector3 origin = new(fx * CoverBuilder.CellSize, 0f, fz * CoverBuilder.CellSize);
        Vector2 focus = new(Focus.X, Focus.Z);
        for (int dz = -CoverRadius; dz <= CoverRadius; dz++)
        for (int dx = -CoverRadius; dx <= CoverRadius; dx++)
        {
            if (!_coverCells.TryGetValue((Mod(fx + dx, width), Mod(fz + dz, height)), out CoverCell cell)) continue;
            float ox = dx * CoverBuilder.CellSize, oz = dz * CoverBuilder.CellSize;
            // Grown in a while since: no longer marked (else the shader's 400 s clock would come round to it again).
            if (cell.Born != 0 && Mathf.PosMod(CoverClock() - cell.Born, 4000) > 20) cell.Born = 0;
            float away = focus.DistanceTo(new Vector2(origin.X + ox + CoverBuilder.CellSize * 0.5f, origin.Z + oz + CoverBuilder.CellSize * 0.5f)) - CoverBuilder.CellSize * 0.75f;
            foreach (var (plant, buffer, count) in cell.Plants)
            {
                int take = 0;
                while (take < count && CoverBuilder.ReachOf(buffer[take * 16 + 15]) >= away) take++;
                if (take == 0) continue;
                CoverDraw d = DrawOf(plant, take);
                int to = d.Filled * 16;
                Array.Copy(buffer, 0, d.Buffer, to, take * 16);
                for (int i = 0; i < take; i++)
                {
                    d.Buffer[to + i * 16 + 3] += ox;
                    d.Buffer[to + i * 16 + 11] += oz;
                    d.Buffer[to + i * 16 + 15] += cell.Born;
                }
                if (cell.Born != 0) _coverDirty = true; // filled again once it has grown, unmarked
                d.Filled += take;
            }
        }
        _coverRoot.Position = origin;
        int instances = 0, calls = 0;
        foreach (CoverDraw d in _coverDraws.Values)
        {
            if (d.Mesh.InstanceCount != d.Capacity) d.Mesh.InstanceCount = d.Capacity;
            d.Mesh.VisibleInstanceCount = d.Filled;
            if (d.Filled > 0) d.Mesh.Buffer = d.Buffer;
            d.Node.Visible = d.Filled > 0;
            instances += d.Filled;
            if (d.Filled > 0) calls++;
        }
        CoverInstances = instances;
        CoverDrawCalls = calls;
    }

    /// <summary>A species' multimesh, with room for this many more.</summary>
    CoverDraw DrawOf(int plant, int more)
    {
        if (!_coverDraws.TryGetValue(plant, out CoverDraw d))
        {
            d = new CoverDraw { Mesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = PlantMesh(plant, 0) } };
            d.Node = new MultiMeshInstance3D
            {
                Multimesh = d.Mesh, MaterialOverride = _coverMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                CustomAabb = new Aabb(new Vector3(-400f, -600f, -400f), new Vector3(800f, 1800f, 800f)),
            };
            _coverRoot.AddChild(d.Node);
            _coverDraws[plant] = d;
        }
        if (d.Filled + more > d.Capacity)
        {
            d.Capacity = Math.Max(256, (d.Filled + more) * 3 / 2);
            System.Array.Resize(ref d.Buffer, d.Capacity * 16);
        }
        return d;
    }

    /// <summary>
    /// A cell of cover landed: if any of it is in sight already (the shader shrinks each tuft in by
    /// its own reach), it grows in from now rather than appearing (<see cref="CoverCell.Born"/>).
    /// </summary>
    void NoteCover(CoverCell cell)
    {
        Vector3 eye = Eye();
        float x0 = (float)NearestCopy(cell.Cx * CoverBuilder.CellSize, CoverBuilder.CellSize, eye.X, World.Terrain.Width);
        float z0 = (float)NearestCopy(cell.Cz * CoverBuilder.CellSize, CoverBuilder.CellSize, eye.Z, World.Terrain.Height);
        float ex = MathF.Max(0f, MathF.Max(x0 - eye.X, eye.X - x0 - CoverBuilder.CellSize)), ez = MathF.Max(0f, MathF.Max(z0 - eye.Z, eye.Z - z0 - CoverBuilder.CellSize));
        if (ex * ex + ez * ez > CoverBuilder.FarReach * CoverBuilder.FarReach) return; // none of it within reach
        float most = 0f, nearest = float.MaxValue;
        foreach (var (_, buffer, count) in cell.Plants)
            for (int i = 0; i < count; i++)
            {
                int o = i * 16;
                float away = new Vector3(x0 + buffer[o + 3] - eye.X, buffer[o + 7] - eye.Y, z0 + buffer[o + 11] - eye.Z).Length();
                float reach = CoverBuilder.ReachOf(buffer[o + 15]);
                float keep = 1f - Mathf.SmoothStep(reach * 0.72f, reach, away);
                if (keep <= 0f) continue;
                most = MathF.Max(most, keep);
                nearest = MathF.Min(nearest, away);
            }
        if (most <= 0f) return;
        cell.Born = CoverClock();
        Fx.PopLog.Note("cover", "cell lands in sight, grows in", nearest, 0.6f, most / (60f * CoverGrowSeconds));
    }

    /// <summary>How long cover that lands in sight takes to grow in, s (flora_cover.gdshader's GROW).</summary>
    const float CoverGrowSeconds = 0.6f;

    /// <summary>The shaders' clock (TIME, which rolls over each hour) in tenths of a second round 400 s, from 1.</summary>
    static int CoverClock()
    {
        int k = (int)(Time.GetTicksMsec() / 100 % 4000);
        return k > 0 ? k : 1;
    }

    /// <summary>A far chunk's ground changed: its cover is laid out afresh.</summary>
    void CoverDug(int fx, int fz)
    {
        int per = FarBuilder.Size / CoverBuilder.CellSize;
        for (int j = 0; j < per; j++)
        for (int i = 0; i < per; i++)
            _coverCells.Remove((fx * per + i, fz * per + j));
        _coverDirty = true;
    }

    void ClearCover()
    {
        _coverVersion++;
        _coverCells.Clear();
        _coverPending.Clear();
        _coverDraws.Clear();
        _coverRoot?.QueueFree();
        _coverRoot = null;
        _coverFocusX = int.MinValue;
        CoverInstances = CoverDrawCalls = 0;
    }
}
