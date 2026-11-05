using System;
using GalHack.Physics;
using GalHack.Terrain;
using Planets.Gen;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using TerrainDigging.Terrain;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// Keeps bodies out of the planet's ground (stuck-safety), with terrain-digging's own resolver
/// and density field: the same zero surface the mesh is drawn from, never the collision mesh.
///
/// Only ground the streamer has already built is asked about: reading anywhere else would
/// generate terrain on the main thread, and nothing can be stuck where nothing is loaded (there
/// is no collision there either). The planet has no edge, so the only way out of the world is
/// down through the floor, which counts as stuck for anything below the top of the atmosphere.
/// Riders out on a hull are checked too (their cabin world holds no terrain); riders inside are
/// their hull's to keep safe.
/// </summary>
public sealed class PlanetGround : IStuckField
{
    readonly PlanetWorld _planet;
    readonly TerrainStreamer _streamer;
    readonly ChunkStore _store;

    public float Ceiling = float.PositiveInfinity;

    /// <summary>Lowest cell any planet has: below it is out of the world.</summary>
    static readonly float Floor = PlanetTerrain.MinChunkY * PlanetTerrain.S;

    public PlanetGround(PlanetWorld planet, TerrainStreamer streamer)
    {
        _planet = planet;
        _streamer = streamer;
        _store = planet.Terrain.Store;
    }

    public string Name => "ground";
    public Body Owner => null;

    public bool Applies(Body body) => body.Frame == null || body.OnOuterHull;

    static BodyShape Of(StuckShape s) => new(s.Height, s.Radius);

    /// <summary>The chunk holding this point, and the ones its density reads, are streamed in.</summary>
    bool Loaded(N.Vector3 p) => _streamer.IsReady(ChunkStore.ChunkOf(Int3.Floor(p)));

    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape)
    {
        if (feet.Y < Floor && feet.Y < Ceiling) return true;
        if (!Loaded(feet) || !Loaded(feet + N.Vector3.UnitY * shape.Height)) return false;
        if (body.Motion == Motion.Free)
            return _store.SampleDensity(feet + N.Vector3.UnitY * shape.FeetBelowOrigin) > 0f;
        return StuckResolver.IsInsideTerrain(_store, feet, Of(shape));
    }

    public bool Fits(Body body, N.Vector3 feet, StuckShape shape) =>
        feet.Y > Floor && (!Loaded(feet) || StuckResolver.Fits(_store, feet, Of(shape)));

    public bool IsSolid(Body body, N.Vector3 point) =>
        point.Y < Floor || (Loaded(point) && _store.SampleDensity(point) > StuckResolver.InsideThreshold);

    public bool Supports(Body body, N.Vector3 feet)
    {
        N.Vector3 below = feet - N.Vector3.UnitY * 0.3f;
        return Loaded(below) && _store.IsSolid(Int3.Floor(below));
    }

    /// <summary>The natural surface: digging only ever lowers the ground, so nothing solid lies above it.</summary>
    public float TopAt(Body body, float x, float z) =>
        _planet.GroundHeight(Mathx.WrapX(x, _planet.Width), Mathx.Wrap(z, _planet.Height)) + 0.5f;

    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet)
    {
        if (feet.Y < Floor)
        {
            // Fallen out of the world: back on top of the ground straight above.
            openFeet = new N.Vector3(feet.X, TopAt(body, feet.X, feet.Z) + 0.6f, feet.Z);
            return true;
        }
        if (!Loaded(feet))
        {
            openFeet = default;
            return false;
        }
        OpenSpace space = StuckResolver.FindOpenSpace(_store, feet, Of(shape));
        openFeet = space.Fallback ? new N.Vector3(feet.X, TopAt(body, feet.X, feet.Z) + 0.6f, feet.Z) : space.Feet;
        return true;
    }
}
