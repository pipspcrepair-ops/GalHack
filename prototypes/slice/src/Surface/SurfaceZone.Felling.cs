using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.Terrain;
using Slice.Player;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// The one way a plant goes for good, whatever takes it (the survival tool, a structure coming down on it, PLACE
/// clearing a site), and the planet's trees as the shared physics sees them: a vessel breaks one whose trunk can't
/// take it (<see cref="PhysicsWorld.ResolveContacts"/>), and it goes as a harvested tree goes.
/// </summary>
public partial class SurfaceZone : IFloraField
{
    /// <summary>How far round a felled plant anything resting on it is woken, and its stand-in looked for, m: a crown's reach.</summary>
    const float TreeReach = 16f;

    /// <summary>What felled trees leave: stumps, and their trunks and crowns falling.</summary>
    public TreeFx Trees
    {
        get
        {
            if (_trees == null) AddChild(_trees = new TreeFx());
            return _trees;
        }
    }

    TreeFx _trees;

    /// <summary>
    /// A plant gone for good: on the planet's felled list (kept through streaming and saves), its colliders gone this
    /// frame and its far chunk rebuilt without it, and whatever rested on it woken (a craft landed on a crown comes
    /// down). Nothing falls: what PLACE and a test's clearing want. False if it was already gone.
    /// </summary>
    public bool Fell(long id, Vector3 at) => Fell(id, at, broken: false);

    /// <summary>
    /// <paramref name="broken"/>: broken by a vessel in this physics step. A heavy ship can break a dozen trees in one tick, and
    /// each streamer call walks every column (most of a millisecond): they all go in one call at the end of the tick
    /// (<see cref="TakeBroken"/>, after the physics host), before the next step. Until then each is felled to the rules all
    /// the same (<see cref="TreeAt"/>), so nothing is held up by it.
    /// </summary>
    bool Fell(long id, Vector3 at, bool broken)
    {
        if (!Planet.Felled.Add(id)) return false;
        _goneIds.Add(id);
        _goneAt.Add(at);
        if (!broken) TakeBroken();
        return true;
    }

    readonly System.Collections.Generic.List<long> _goneIds = new();
    readonly System.Collections.Generic.List<Vector3> _goneAt = new();

    /// <summary>The trees felled since the streamer last heard: their colliders and far chunks go in one call, and whatever rested on them wakes.</summary>
    void TakeBroken()
    {
        if (_goneIds.Count == 0) return;
        Streamer.Fell(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_goneIds));
        foreach (Vector3 at in _goneAt) Physics.NotifyChanged(Region.Around(at.ToNumerics(), TreeReach));
        _goneIds.Clear();
        _goneAt.Clear();
    }

    /// <summary>Broken trees still to fall, one a frame (<see cref="Topple"/>): a dozen trunks and leaf bursts made at once overran the frame.</summary>
    public int Toppling => _toppling.Count;

    readonly System.Collections.Generic.Queue<(ulong Frame, System.Action Fall)> _toppling = new();

    /// <summary>The next broken tree's trunk and crown fall, once a frame, from the frame after the one that broke it (which has the breaking to do).</summary>
    void Topple()
    {
        if (_toppling.TryPeek(out var next) && next.Frame < Engine.GetProcessFrames()) _toppling.Dequeue().Fall();
    }

    /// <summary>
    /// A plant felled where it stands (cut through by the tool, broken by a structure): it goes (<see cref="Fell(long, Vector3)"/>),
    /// and a tree's trunk and crown topple away along <paramref name="away"/> as itself, a stump left. False if already gone.
    /// </summary>
    public bool Fell(in TerrainStreamer.PlantSighting s, Vector3 away) => Fell(s, away, broken: false);

    bool Fell(in TerrainStreamer.PlantSighting s, Vector3 away, bool broken)
    {
        // Its own trunk's size, read while it still stands.
        float size = Mathf.Clamp(s.Scale, 0.5f, 3f), height = 3.2f * size, radius = 0.28f * size;
        long id = s.Id;
        foreach (var (solid, _) in Streamer.SolidsNear(s.Root, 2.5f))
            if (solid.Id == id && solid.Fellable) { height = Mathf.Max(2f, solid.Extents.Y * 2f); radius = solid.Extents.X; }
        if (!Fell(id, s.Root, broken)) return false;
        if (!TreeFx.IsTree(s.Kind)) return true;
        GalHack.Plants.Plant species = SpeciesOf(s.Plant);
        Color leaf = TreeFx.LeafOf(s.Kind, species);
        Color bark = species == null ? new Color(0.36f, 0.25f, 0.16f) : new Color(species.Bark.R, species.Bark.G, species.Bark.B);
        Vector3 root = s.Root;
        Basis turn = s.Turn;
        Color tint = s.OwnTint;
        Mesh mesh = Streamer.MeshOf(s);
        void Fall()
        {
            Trees.Fell(root, away, height, radius, leaf, crown: true, bark, mesh, turn, custom: tint);
            Trees.Leaves(root + Vector3.Up * height, leaf, 30, 1.8f);
        }
        if (broken) _toppling.Enqueue((Engine.GetProcessFrames(), Fall));
        else Fall();
        return true;
    }

    /// <summary>The species a plant is, in the world's herbarium, or null for a rock.</summary>
    public GalHack.Plants.Plant SpeciesOf(int index)
    {
        var plants = Planet.Flora.Book.Plants;
        return index >= 0 && index < plants.Count ? plants[index] : null;
    }

    public bool TreeAt(object part, out long tree, out float trunkRadius)
    {
        tree = 0;
        trunkRadius = 0f;
        if (Planet == null || part is not (Rid body, int shape)) return false;
        bool standing = Streamer.SolidHit(body, shape, out FloraSolid solid);
        // A crown with no trunk under it (a shrub's): it gives way to any structure.
        if (solid.Extents == Vector3.Zero) return CrownOnly(body, shape, out tree);
        tree = solid.Id;
        if (!standing) return true; // felled, its collider not yet gone: nothing left to hold
        if (solid.Fellable)
        {
            trunkRadius = solid.Extents.X;
            return true;
        }
        if (FarBuilder.Solids?.Of(solid.Kind) is not { Wood: > 0 }) return false; // rock, a crystal's shard: never gives way
        // A limb: the tree is its trunk, looked up once per tree (feet and hulls meet limbs every tick). Not found while its
        // far chunk is being rebuilt (another tree felled there): it holds this tick, and is looked up again next.
        if (_trunks.TryGetValue(tree, out trunkRadius)) return true;
        foreach (var (s, _) in Streamer.SolidsNear(Planet.NearestCopy(Planet.Canonical(solid.Center), Focus()), TreeReach))
            if (s.Id == tree && s.Fellable)
            {
                _trunks[tree] = trunkRadius = s.Extents.X;
                return true;
            }
        return false;
    }

    readonly System.Collections.Generic.Dictionary<long, float> _trunks = new();
    readonly System.Collections.Generic.Dictionary<(ulong, int), long> _crowns = new();

    /// <summary>
    /// Which plant a trunkless crown's shape is: the one whose crown stands where the shape does (planet coordinates, as the
    /// column holds them). Not found while its far chunk is being rebuilt: it holds this tick, and is looked up again next.
    /// </summary>
    bool CrownOnly(Rid body, int shape, out long tree)
    {
        if (_crowns.TryGetValue((body.Id, shape), out tree)) return true;
        Vector3 at = PhysicsServer3D.BodyGetShapeTransform(body, shape).Origin;
        foreach (var (s, _) in Streamer.SolidsNear(Planet.NearestCopy(at, Focus()), 2f, canopies: true))
            if (s.IsCanopy && s.ShapeTransform.Origin.IsEqualApprox(at))
            {
                if (_crowns.Count > 4096) _crowns.Clear(); // columns are rebuilt with new bodies: never grows without end
                _crowns[(body.Id, shape)] = tree = s.Id;
                return true;
            }
        return false;
    }

    public void Fell(long tree, N.Vector3 point, N.Vector3 away)
    {
        if (Planet.Felled.Contains(tree)) return; // broken already this step (its trunk and crown both met), or gone
        Vector3 at = point.ToGodot();
        TerrainStreamer.PlantSighting? found = null;
        foreach (TerrainStreamer.PlantSighting s in Streamer.FloraNear(at, TreeReach))
        {
            if (s.Id != tree) continue;
            found = s;
            if (s.Variant >= 0) break; // its near self (its own variant and tint) over its far stand-in
        }
        if (found is { } plant) Fell(plant, away.ToGodot(), broken: true);
        else Fell(tree, at, broken: true);
    }
}
