using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using PlanetSurface.Terrain;
using Slice.Player;
using Layers = GalHack.Physics.Layers;
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
    public bool Fell(long id, Vector3 at)
    {
        if (!Planet.Felled.Add(id)) return false;
        Streamer.Fell(id);
        Physics.NotifyChanged(Region.Around(at.ToNumerics(), TreeReach));
        return true;
    }

    /// <summary>
    /// A plant felled where it stands (cut through by the tool, broken by a structure): it goes (<see cref="Fell(long, Vector3)"/>),
    /// and a tree's trunk and crown topple away along <paramref name="away"/> as itself, a stump left. False if already gone.
    /// </summary>
    public bool Fell(in TerrainStreamer.PlantSighting s, Vector3 away)
    {
        // Its own trunk's size, read while it still stands.
        float size = Mathf.Clamp(s.Scale, 0.5f, 3f), height = 3.2f * size, radius = 0.28f * size;
        long id = s.Id;
        foreach (var (solid, _) in Streamer.SolidsNear(s.Root, 2.5f))
            if (solid.Id == id && solid.Fellable) { height = Mathf.Max(2f, solid.Extents.Y * 2f); radius = solid.Extents.X; }
        if (!Fell(id, s.Root)) return false;
        if (!TreeFx.IsTree(s.Kind)) return true;
        GalHack.Plants.Plant species = SpeciesOf(s.Plant);
        Color leaf = TreeFx.LeafOf(s.Kind, species);
        Color bark = species == null ? new Color(0.36f, 0.25f, 0.16f) : new Color(species.Bark.R, species.Bark.G, species.Bark.B);
        Trees.Fell(s.Root, away, height, radius, leaf, crown: true, bark, Streamer.MeshOf(s), s.Turn, custom: s.OwnTint);
        Trees.Leaves(s.Root + Vector3.Up * height, leaf, 30, 1.8f);
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
        if (solid.Extents == Vector3.Zero) return false; // not one of the solids
        tree = solid.Id;
        if (!standing) return true; // felled, its collider not yet gone: nothing left to hold
        if (!solid.Fellable && solid.Plant >= 0)
            foreach (var (s, _) in Streamer.SolidsNear(Planet.NearestCopy(Planet.Canonical(solid.Center), Focus()), TreeReach))
                if (s.Id == tree && s.Fellable) { solid = s; break; } // a limb: the tree is its trunk
        if (!solid.Fellable) return false; // rock: never gives way
        trunkRadius = solid.Extents.X;
        return true;
    }
    public void Fell(long tree, N.Vector3 point, N.Vector3 away)
    {
        Vector3 at = point.ToGodot();
        TerrainStreamer.PlantSighting? found = null;
        foreach (TerrainStreamer.PlantSighting s in Streamer.FloraNear(at, TreeReach))
        {
            if (s.Id != tree) continue;
            found = s;
            if (s.Variant >= 0) break; // its near self (its own variant and tint) over its far stand-in
        }
        if (found is { } plant) Fell(plant, away.ToGodot());
        else Fell(tree, at);
    }
}
