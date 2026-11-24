using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Planets.Gen;
using PlanetSurface.World;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// The planet's seas and rivers as the physics module's water volume: exactly the water that is
/// drawn, decided by the generator alone (digging never moves it). A world's liquid can be lava:
/// that is no water to swim in, but what stands over each column is asked the same way
/// (<see cref="LiquidAt"/>). Answers are kept per 2 m column, since bodies and the camera ask
/// every tick and a column never changes.
/// </summary>
public sealed class PlanetWater : IWaterField
{
    const float Cell = 2f;
    const int MostKept = 8192;
    const float BelowBed = 1.5f;

    readonly PlanetWorld _planet;
    readonly Dictionary<(int, int), (float surface, float ground)> _columns = new();

    public PlanetWater(PlanetWorld planet) => _planet = planet;

    /// <summary>This world's liquid is lava.</summary>
    public bool IsLava => _planet.Look.Lava;

    /// <summary>The liquid's surface over a column (frame coordinates), water or lava, if it holds any, and the natural ground there.</summary>
    public bool LiquidAt(float x, float z, out float surface, out float ground)
    {
        double wx = Mathx.WrapX(x, _planet.Width), wz = Mathx.Wrap(z, _planet.Height);
        var key = ((int)(wx / Cell), (int)(wz / Cell));
        if (!_columns.TryGetValue(key, out var column))
        {
            column = (_planet.Source.WaterSurfaceAt(wx, wz, out float s) ? s : float.NegativeInfinity, _planet.GroundHeight(wx, wz));
            if (_columns.Count >= MostKept) _columns.Clear();
            _columns[key] = column;
        }
        surface = column.surface;
        ground = column.ground;
        return surface > float.NegativeInfinity;
    }

    public bool LiquidAt(float x, float z, out float surface) => LiquidAt(x, z, out surface, out _);

    /// <summary>Water is what lies over the ground: a cave or a bunker under a sea stays dry.</summary>
    public bool SurfaceAt(N.Vector3 point, out float surface)
    {
        surface = 0f;
        return !IsLava && LiquidAt(point.X, point.Z, out surface, out float ground) && point.Y > ground - BelowBed;
    }

    /// <summary>A point lies in the water (never lava, never under the bed), and how far down.</summary>
    public bool Under(Godot.Vector3 p, out float depth)
    {
        depth = 0f;
        if (!SurfaceAt(p.ToNumerics(), out float surface) || p.Y >= surface) return false;
        depth = surface - p.Y;
        return true;
    }
}
