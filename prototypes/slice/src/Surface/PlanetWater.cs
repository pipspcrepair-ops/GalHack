using GalHack.Physics;
using PlanetSurface.World;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// The planet's seas and rivers for the shared physics module: a volume bodies go into, exactly
/// where the water is drawn (shared/Terrain's <c>WaterSurfaceAt</c>: the generator's sea level and
/// river channels, never moved by digging). Zone coordinates are the planet's, whole planets apart.
/// </summary>
public sealed class PlanetWater : IWaterField
{
    readonly PlanetWorld _planet;

    public PlanetWater(PlanetWorld planet) => _planet = planet;

    public bool SurfaceAt(N.Vector3 point, out float surface) => _planet.Source.WaterSurfaceAt(point.X, point.Z, out surface);
}
