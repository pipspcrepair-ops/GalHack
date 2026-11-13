using System.Collections.Generic;
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

    readonly List<long> _goneIds = new();
    readonly List<Vector3> _goneAt = new();

    /// <summary>The trees felled since the streamer last heard: their colliders and far chunks go in one call, and whatever rested on them wakes.</summary>
    void TakeBroken()
    {
        if (_goneIds.Count == 0) return;
        foreach (Vector3 at in _goneAt) Read(ChunkAt(Planet.Canonical(at))); // known through the rebuild the felling starts
        Streamer.Fell(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_goneIds));
        foreach (Vector3 at in _goneAt) Physics.NotifyChanged(Region.Around(at.ToNumerics(), TreeReach));
        _goneIds.Clear();
        _goneAt.Clear();
    }

    /// <summary>Broken trees still to fall, one a frame (<see cref="Topple"/>): a dozen trunks and leaf bursts made at once overran the frame.</summary>
    public int Toppling => _toppling.Count;

    readonly Queue<(ulong Frame, System.Action Fall)> _toppling = new();

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
        Vector3 at = solid.Extents == Vector3.Zero ? PhysicsServer3D.BodyGetShapeTransform(body, shape).Origin : solid.Center;
        // The first flora met in a chunk (by a foot's ray, mostly, long before the hull is near): all its trees are read
        // then, not in the tick something breaks one.
        Read(ChunkAt(at));
        if (solid.Extents == Vector3.Zero)
        {
            // A crown with nothing else of its tree in its column: its tree by where it stands, and that tree's trunk if it
            // has one (a crown spilling over from the next column holds as its tree does; a shrub's gives way to any structure).
            if (!CrownAt(at, out tree)) return false;
            trunkRadius = Planet.Felled.Contains(tree) ? 0f : _trunks.GetValueOrDefault(tree);
            return true;
        }
        tree = solid.Id;
        if (!standing) return true; // felled, its collider not yet gone: nothing left to hold
        if (solid.Fellable)
        {
            trunkRadius = solid.Extents.X;
            return true;
        }
        if (FarBuilder.Solids?.Of(solid.Kind) is not { Wood: > 0 }) return false; // rock, a crystal's shard: never gives way
        return TrunkOf(tree, solid.Center, out trunkRadius); // a limb: the tree is its trunk
    }

    /// <summary>
    /// The trees' trunks (id: radius) and crown shapes (where each stands: its tree) of every far chunk a lookup has needed,
    /// read from the chunk while it is in. Feet and hulls meet limbs and crowns every tick, and a chunk being rebuilt (a tree
    /// felled in it) lists nothing for a moment: what a tree is never changes, so it is read once and kept.
    /// </summary>
    readonly Dictionary<long, float> _trunks = new();
    readonly Dictionary<(int, int, int), long> _crowns = new();
    readonly HashSet<(int, int)> _read = new();

    /// <summary>A crown shape's place (planet coordinates, as its column holds it), to the centimetre.</summary>
    static (int, int, int) Key(Vector3 p) => (Mathf.RoundToInt(p.X * 100f), Mathf.RoundToInt(p.Y * 100f), Mathf.RoundToInt(p.Z * 100f));

    // A tree's trunk, and the tree a crown shape is, from what is read; not there, the far chunk round the place (planet
    // coordinates) is read again (it may have been read while still coming in). False if still unknown: it holds this tick.
    bool TrunkOf(long tree, Vector3 at, out float radius) => _trunks.TryGetValue(tree, out radius) || Reread(at) && _trunks.TryGetValue(tree, out radius);

    bool CrownAt(Vector3 at, out long tree) => _crowns.TryGetValue(Key(at), out tree) || Reread(at) && _crowns.TryGetValue(Key(at), out tree);

    static (int, int) ChunkAt(Vector3 at) => (Mathf.FloorToInt(at.X / FarBuilder.Size), Mathf.FloorToInt(at.Z / FarBuilder.Size));

    bool Reread(Vector3 at)
    {
        _read.Remove(ChunkAt(at));
        return Read(ChunkAt(at));
    }

    /// <summary>
    /// Reads a far chunk's trees, with a crown's reach round it (a limb's trunk, a crown's tree, may stand in the next one).
    /// False if it was read already. Done before a chunk is rebuilt for a felling, so its trees are known through the rebuild.
    /// </summary>
    bool Read((int x, int z) chunk)
    {
        if (!_read.Add(chunk)) return false;
        if (_read.Count > 256)
        {
            // A long journey: what is far behind is read again if ever needed.
            _read.Clear();
            _trunks.Clear();
            _crowns.Clear();
            _read.Add(chunk);
        }
        var middle = new Vector3((chunk.x + 0.5f) * FarBuilder.Size, 0f, (chunk.z + 0.5f) * FarBuilder.Size);
        foreach (var (s, _) in Streamer.SolidsNear(Planet.NearestCopy(middle, Focus()), FarBuilder.Size * 0.5f + TreeReach, canopies: true))
            if (s.IsCanopy) _crowns[Key(s.ShapeTransform.Origin)] = s.Id;
            else if (s.Fellable) _trunks[s.Id] = s.Extents.X;
        return true;
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
