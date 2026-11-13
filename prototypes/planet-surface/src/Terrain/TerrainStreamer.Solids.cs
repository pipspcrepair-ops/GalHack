using System.Linq;
using GalHack.Plants;
using System.Collections.Generic;
using System.Diagnostics;
using Planets.Gen;
using Planets.Look;
using Godot;
using PlanetSurface.World;
using Layers = GalHack.Physics.Layers;

namespace PlanetSurface.Terrain;

/// <summary>
/// Solid flora: tree trunks and boulders get colliders in the near rings only, exactly where the
/// ground has collision (<see cref="CollisionRadius"/> columns round the player). The level-0 far
/// chunks list their solid instances on the workers (<see cref="FarJob.Solids"/>); here each near
/// 16 m column gets one static physics body holding a cylinder or a box per solid, added and
/// dropped with the column. Shapes are shared between instances of one size. Felled trees lose
/// their collider in the frame they are felled, and stay gone (<see cref="PlanetWorld.Felled"/>).
/// </summary>
public partial class TerrainStreamer
{
    sealed class SolidColumn
    {
        public int Cx, Cz;
        public Rid Body;
        public readonly List<FloraSolid> Solids = new();
    }

    readonly Dictionary<(int, int), SolidColumn> _solidColumns = new();
    readonly Dictionary<(bool, int, int, int), Rid> _solidShapes = new();
    HashSet<long> _felledSnapshot = new();

    /// <summary>Solid colliders (trunks and boulders) in the near columns right now.</summary>
    public int SolidColliders { get; private set; }

    /// <summary>Main-thread time spent on the solid flora's bodies since the game began, ms (tests read the difference over a stretch of frames).</summary>
    public double SolidsTotalMs { get; private set; }

    /// <summary>Near columns with a solid-flora body, and the farthest of them from the focus (columns).</summary>
    public int SolidBodies { get; private set; }
    public int SolidReach { get; private set; }

    /// <summary>Every column within collision reach of the focus has its solid flora in (bodies built, or none to build).</summary>
    public bool SolidsReady { get; private set; }

    /// <summary>The focus column (frame coordinates) <see cref="SolidsReady"/> was last worked out for.</summary>
    public (int x, int z) SolidsFocus { get; private set; }

    void UpdateSolids(int fcx, int fcz, long frameStart)
    {
        // Out of reach: dropped, with a column of slack as the ground's collision has.
        List<(int, int)> gone = null;
        foreach (var (key, c) in _solidColumns)
            if (!CollisionEnabled || Mathf.Max(Mathf.Abs(c.Cx - fcx), Mathf.Abs(c.Cz - fcz)) > CollisionRadius + 1)
                (gone ??= new List<(int, int)>()).Add(key);
        if (gone != null)
            foreach (var key in gone)
            {
                FreeSolids(_solidColumns[key]);
                _solidColumns.Remove(key);
            }
        if (!CollisionEnabled)
        {
            SolidsReady = false;
            return;
        }

        // In reach: built from the level-0 far chunk's list once it is in, a few per frame within budget.
        bool ready = true;
        for (int dz = -CollisionRadius; dz <= CollisionRadius; dz++)
        for (int dx = -CollisionRadius; dx <= CollisionRadius; dx++)
        {
            var key = (fcx + dx, fcz + dz);
            if (_solidColumns.ContainsKey(key)) continue;
            ready = false;
            if (Stopwatch.GetElapsedTime(frameStart).TotalMilliseconds > UploadBudgetMs * 1.5) continue;
            if (!SolidsOfColumn(key.Item1, key.Item2, out List<FloraSolid> list)) continue;
            var column = new SolidColumn { Cx = key.Item1, Cz = key.Item2 };
            Build(column, list);
            _solidColumns[key] = column;
        }

        int colliders = 0, bodies = 0, reach = 0;
        foreach (SolidColumn c in _solidColumns.Values)
        {
            if (!c.Body.IsValid) continue;
            bodies++;
            colliders += c.Solids.Count;
            reach = Mathf.Max(reach, Mathf.Max(Mathf.Abs(c.Cx - fcx), Mathf.Abs(c.Cz - fcz)));
        }
        SolidColliders = colliders;
        SolidBodies = bodies;
        SolidReach = reach;
        SolidsReady = ready;
        SolidsFocus = (fcx, fcz);
    }

    /// <summary>
    /// The solids standing in a column (frame key), if its level-0 far chunk is in: from that chunk and its neighbours,
    /// since a tree's limb can reach over the edge of the chunk that rolled the tree (its middle in the next one).
    /// </summary>
    bool SolidsOfColumn(int cx, int cz, out List<FloraSolid> list)
    {
        list = null;
        int ccx = Mod(cx, World.Terrain.ChunksX), ccz = Mod(cz, World.Terrain.ChunksZ);
        int per = FarBuilder.Size / S;
        int fx = ccx / per, fz = ccz / per; // the far grid tiles the planet, a chunk cut short before a seam included
        if (!_far.TryGetValue((fx, fz), out FarChunk home) || home.Lod != 0 || home.Solids == null) return false;
        list = new List<FloraSolid>();
        float x0 = ccx * S, z0 = ccz * S;
        int farX = (World.Terrain.ChunksX + per - 1) / per, farZ = (World.Terrain.ChunksZ + per - 1) / per;
        var seen = new HashSet<(int, int)>();
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var key = (Mod(fx + dx, farX), Mod(fz + dz, farZ));
                if (!seen.Add(key) || !_far.TryGetValue(key, out FarChunk f) || f.Lod != 0 || f.Solids == null) continue;
                foreach (FloraSolid s in f.Solids)
                    if (s.Center.X >= x0 && s.Center.X < x0 + S && s.Center.Z >= z0 && s.Center.Z < z0 + S && !World.Felled.Contains(s.Id))
                        list.Add(s);
            }
        return true;
    }

    /// <summary>One static body for a column's solids, placed at the column's copy in the frame.</summary>
    void Build(SolidColumn c, List<FloraSolid> solids)
    {
        c.Solids.Clear();
        c.Solids.AddRange(solids);
        if (solids.Count == 0) return;
        Rid body = PhysicsServer3D.BodyCreate();
        PhysicsServer3D.BodySetMode(body, PhysicsServer3D.BodyMode.Static);
        PhysicsServer3D.BodySetCollisionLayer(body, Layers.Flora);
        PhysicsServer3D.BodySetCollisionMask(body, 0);
        foreach (FloraSolid s in solids) PhysicsServer3D.BodyAddShape(body, ShapeOf(s), s.ShapeTransform);
        PhysicsServer3D.BodySetState(body, PhysicsServer3D.BodyState.Transform, new Transform3D(Basis.Identity, ColumnOffset(c)));
        PhysicsServer3D.BodySetSpace(body, GetWorld3D().Space);
        c.Body = body;
    }

    /// <summary>Solids are kept in planet coordinates; a column's copy in the frame is whole planets away.</summary>
    Vector3 ColumnOffset(SolidColumn c) => new(
        (c.Cx - Mod(c.Cx, World.Terrain.ChunksX)) * S, 0f, (c.Cz - Mod(c.Cz, World.Terrain.ChunksZ)) * S);

    readonly Dictionary<(int, int, int), Rid> _canopyShapes = new();

    /// <summary>A canopy part's convex shape at scale 1 (an instance's transform scales it).</summary>
    public static Rid MakeCanopyShape(Plant plant, int variant, int part)
    {
        var shape = PhysicsServer3D.ConvexPolygonShapeCreate();
        PhysicsServer3D.ShapeSetData(shape, plant.Canopy[variant][part].Select(p => new Vector3(p.X, p.Y, p.Z)).ToArray());
        return shape;
    }

    /// <summary>Shared by every tree of the species and mesh.</summary>
    Rid CanopyShape(FloraSolid s)
    {
        var key = (s.Plant, s.Variant, s.Part);
        if (!_canopyShapes.TryGetValue(key, out Rid shape)) _canopyShapes[key] = shape = MakeCanopyShape(World.Flora.Book.Plants[s.Plant], s.Variant, s.Part);
        return shape;
    }

    /// <summary>
    /// A shape shared by every solid of about one size: widths to 4 cm, heights to 20 cm, so a
    /// planet's trees need a few dozen shapes, not one each.
    /// </summary>
    Rid ShapeOf(FloraSolid s)
    {
        if (s.IsCanopy) return CanopyShape(s);
        var key = (s.IsTrunk, Mathf.RoundToInt(s.Extents.X * 25f), Mathf.RoundToInt(s.Extents.Y * 5f), Mathf.RoundToInt(s.Extents.Z * 25f));
        if (_solidShapes.TryGetValue(key, out Rid shape)) return shape;
        var e = new Vector3(key.Item2 / 25f, key.Item3 / 5f, key.Item4 / 25f);
        if (s.IsTrunk)
        {
            shape = PhysicsServer3D.CylinderShapeCreate();
            PhysicsServer3D.ShapeSetData(shape, new Godot.Collections.Dictionary { ["radius"] = e.X, ["height"] = e.Y * 2f });
        }
        else
        {
            shape = PhysicsServer3D.BoxShapeCreate();
            PhysicsServer3D.ShapeSetData(shape, e);
        }
        _solidShapes[key] = shape;
        return shape;
    }

    void FreeSolids(SolidColumn c)
    {
        if (c.Body.IsValid) PhysicsServer3D.FreeRid(c.Body);
        c.Body = default;
        c.Solids.Clear();
    }

    /// <summary>A level-0 far chunk's flora was rebuilt (dug ground, a felled tree): its near columns, and its neighbours', take the new list at once.</summary>
    void SolidsChanged(int fx, int fz)
    {
        int per = FarBuilder.Size / S;
        int farX = (World.Terrain.ChunksX + per - 1) / per, farZ = (World.Terrain.ChunksZ + per - 1) / per;
        foreach (SolidColumn c in _solidColumns.Values)
        {
            // Its own columns, and its neighbours' (its trees' limbs can reach into them).
            int ox = Mod(Mod(c.Cx, World.Terrain.ChunksX) / per - fx + 1, farX), oz = Mod(Mod(c.Cz, World.Terrain.ChunksZ) / per - fz + 1, farZ);
            if (ox > 2 || oz > 2) continue;
            FreeSolids(c);
            if (SolidsOfColumn(c.Cx, c.Cz, out List<FloraSolid> list)) Build(c, list);
        }
    }

    void ShiftSolids(int dcx, int dcz)
    {
        var columns = new List<SolidColumn>(_solidColumns.Values);
        _solidColumns.Clear();
        foreach (SolidColumn c in columns)
        {
            c.Cx += dcx;
            c.Cz += dcz;
            _solidColumns[(c.Cx, c.Cz)] = c;
            if (c.Body.IsValid) PhysicsServer3D.BodySetState(c.Body, PhysicsServer3D.BodyState.Transform, new Transform3D(Basis.Identity, ColumnOffset(c)));
        }
    }

    void ClearSolids()
    {
        foreach (SolidColumn c in _solidColumns.Values) FreeSolids(c);
        _solidColumns.Clear();
        _felledSnapshot = new HashSet<long>();
        foreach (Rid shape in _canopyShapes.Values) PhysicsServer3D.FreeRid(shape);
        _canopyShapes.Clear();
        SolidColliders = SolidBodies = SolidReach = 0;
        SolidsReady = false;
    }

    public override void _ExitTree()
    {
        if (World != null) ClearSolids();
        foreach (Rid shape in _solidShapes.Values) PhysicsServer3D.FreeRid(shape);
        _solidShapes.Clear();
        foreach (Rid shape in _canopyShapes.Values) PhysicsServer3D.FreeRid(shape);
        _canopyShapes.Clear();
    }

    // ---- Felling, hits and queries ----------------------------------------------------------

    /// <summary>
    /// A tree was felled (its id is already in <see cref="PlanetWorld.Felled"/>): its collider goes
    /// this frame, and its far chunk is rebuilt without it (the mesh, at every level of detail).
    /// </summary>
    public void Fell(long id)
    {
        foreach (SolidColumn c in _solidColumns.Values)
        {
            for (int i = 0; i < c.Solids.Count; i++)
            {
                if (c.Solids[i].Id != id || !c.Body.IsValid) continue;
                PhysicsServer3D.BodySetShapeDisabled(c.Body, i, true);
            }
        }
        _felledSnapshot = new HashSet<long>(World.Felled);
        var (fx, fz) = FarBuilder.ChunkOfScatter(id);
        if (_far.TryGetValue((fx, fz), out FarChunk f))
        {
            f.Solids?.RemoveAll(s => s.Id == id);
            f.Lod = -1;
            f.Version++;
        }
    }

    /// <summary>Everything felled changed at once (a load): every far chunk's flora is rebuilt.</summary>
    public void RefreshFelled()
    {
        _felledSnapshot = new HashSet<long>(World.Felled);
        foreach (FarChunk f in _far.Values)
        {
            f.Lod = -1;
            f.Version++;
            f.Solids?.RemoveAll(s => World.Felled.Contains(s.Id));
        }
        foreach (SolidColumn c in _solidColumns.Values) FreeSolids(c);
        _solidColumns.Clear();
        // Every column builds again: not ready until a pass has built them, so nobody reads the
        // last pass's "ready" for columns that are gone.
        SolidsReady = false;
    }

    /// <summary>The solid a ray hit (by the body and shape index the physics server reports), if it is one.</summary>
    public bool SolidHit(Rid body, int shape, out FloraSolid solid)
    {
        solid = default;
        foreach (SolidColumn c in _solidColumns.Values)
        {
            if (c.Body != body || shape < 0 || shape >= c.Solids.Count) continue;
            FloraSolid hit = c.Solids[shape];
            long id = hit.Id;
            // A canopy is its tree: what you hit through it is the trunk, so the tool still aims at (and fells) the tree.
            solid = hit.IsCanopy ? c.Solids.Find(s => s.Id == id && !s.IsCanopy) : hit;
            return !World.Felled.Contains(solid.Id);
        }
        return false;
    }

    /// <summary>The loaded solids near a point (frame coordinates, returned in the frame), for tests and stuck-safety.</summary>
    public IEnumerable<(FloraSolid solid, Vector3 offset)> SolidsNear(Vector3 p, float radius, bool canopies = false)
    {
        double w = World.Terrain.Width, h = World.Terrain.Height;
        foreach (FarChunk f in _far.Values)
        {
            if (f.Lod != 0 || f.Solids == null) continue;
            var offset = new Vector3(
                (float)(NearestCopy(f.Fx * FarBuilder.Size, FarBuilder.Size, p.X, w) - f.Fx * FarBuilder.Size), 0f,
                (float)(NearestCopy(f.Fz * FarBuilder.Size, FarBuilder.Size, p.Z, h) - f.Fz * FarBuilder.Size));
            foreach (FloraSolid s in f.Solids)
            {
                Vector3 c = s.Center + offset;
                if (s.IsCanopy && !canopies || Mathf.Abs(c.X - p.X) > radius || Mathf.Abs(c.Z - p.Z) > radius || World.Felled.Contains(s.Id)) continue;
                yield return (s, offset);
            }
        }
    }

    /// <summary>
    /// Whether a trunk or a boulder stands within <paramref name="clearance"/> of a point (frame
    /// coordinates): what a navigation grid marks blocked, so walkers path round them. Answers from
    /// the finest far rings' data, so it reaches beyond the colliders.
    /// </summary>
    public bool BlockedAt(Vector3 p, float clearance)
    {
        foreach (var (s, offset) in SolidsNear(p, 8f))
        {
            Vector3 l = s.Basis.Transposed() * (p - offset - s.Center);
            float dx = s.IsTrunk ? Mathf.Max(0f, new Vector2(l.X, l.Z).Length() - s.Extents.X) : Mathf.Max(0f, Mathf.Abs(l.X) - s.Extents.X);
            float dz = s.IsTrunk ? 0f : Mathf.Max(0f, Mathf.Abs(l.Z) - s.Extents.Z);
            if (dx * dx + dz * dz <= clearance * clearance) return true;
        }
        return false;
    }

    /// <summary>Scatter instances of one kind near a point, as drawn at the finest level (frame coordinates), for tests.</summary>
    public IEnumerable<Transform3D> FloraNear(Flora kind, Vector3 p, float radius)
    {
        foreach (FarChunk f in _far.Values)
        {
            if (f.Lod != 0 || f.Node == null || f.Flora == null) continue;
            foreach (FloraBatch b in f.Flora)
            {
                if (b.Kind != kind) continue;
                float[] m = b.Buffer;
                for (int i = 0; i < b.Count; i++)
                {
                    int o = i * 16;
                    var t = new Transform3D(new Basis(new Vector3(m[o], m[o + 4], m[o + 8]), new Vector3(m[o + 1], m[o + 5], m[o + 9]), new Vector3(m[o + 2], m[o + 6], m[o + 10])), new Vector3(m[o + 3], m[o + 7], m[o + 11]) + f.Node.Position);
                    if (Mathf.Abs(t.Origin.X - p.X) <= radius && Mathf.Abs(t.Origin.Z - p.Z) <= radius) yield return t;
                }
            }
        }
    }

    /// <summary>
    /// Stuck-safety for solid flora: if a body's core (its axis, from <paramref name="radius"/> above its
    /// feet to <paramref name="radius"/> below its top) is inside a trunk or a boulder, where its feet
    /// should go instead: beside it, clear by its radius. Null when it isn't inside one. Only a solid whose
    /// collider stands there counts (the engine is asked): one still waiting for its collider can't have
    /// trapped anyone.
    /// </summary>
    public Vector3? OutOfSolids(Vector3 feet, float height, float radius)
    {
        if (World == null) return null;
        // Trunks and boulders stand on the ground: a room dug under one is no reason to be thrown out onto it.
        if (feet.Y < World.GroundHeight(Mathx.WrapX(feet.X, World.Width), Mathx.Wrap(feet.Z, World.Height)) - 2.5f) return null;
        foreach (var (s, offset) in SolidsNear(feet, 8f))
        {
            for (int k = 0; k <= 2; k++)
            {
                Vector3 p = feet + Vector3.Up * Mathf.Lerp(radius, Mathf.Max(radius, height - radius), k / 2f) - offset;
                if (!s.Contains(p) || !FloraAt(p + offset)) continue;
                Vector3 beside = s.Beside(p, radius + 0.05f) + offset;
                float ground = World.GroundHeight(Mathx.WrapX(beside.X, World.Width), Mathx.Wrap(beside.Z, World.Height));
                return new Vector3(beside.X, Mathf.Max(feet.Y, ground + 0.05f), beside.Z);
            }
        }
        return null;
    }

    PhysicsPointQueryParameters3D _floraPoint;

    /// <summary>A flora collider holds this point (frame coordinates).</summary>
    bool FloraAt(Vector3 p)
    {
        _floraPoint ??= new PhysicsPointQueryParameters3D { CollisionMask = Layers.Flora };
        _floraPoint.Position = p;
        return GetWorld3D()?.DirectSpaceState.IntersectPoint(_floraPoint, 1).Count > 0;
    }
}
