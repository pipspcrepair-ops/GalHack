using System.Collections.Generic;
using GalHack.Terrain;
using Godot;
using Planets.Gen;

namespace Slice.Surface;

/// <summary>
/// The suit's ore scanner: the nearest deposit of each common material (the planet generator
/// promises one of each within about 280 m of any walkable spot), at its ore body's centre.
/// Asked often, recomputed a few times a second.
/// </summary>
public sealed class DepositFinder
{
    public static readonly DepositKind[] Kinds = { DepositKind.Iron, DepositKind.Copper, DepositKind.Silicon, DepositKind.Carbon };

    readonly List<(string kind, Vector3 at)> _near = new();
    ulong _lastFrame;

    public IReadOnlyList<(string kind, Vector3 at)> Near(SurfaceZone zone, Vector3 from)
    {
        ulong frame = Engine.GetProcessFrames();
        if (frame - _lastFrame < 20 && _near.Count > 0) return _near;
        _lastFrame = frame;
        _near.Clear();
        foreach (DepositKind kind in Kinds)
            if (Nearest(zone, kind, from, 600f) is Vector3 at) _near.Add((kind.ToString().ToLowerInvariant(), at));
        return _near;
    }

    /// <summary>The nearest deposit of a kind (its ore body's centre, frame coordinates), or null.</summary>
    public static Vector3? Nearest(SurfaceZone zone, DepositKind kind, Vector3 from, float within)
    {
        var planet = zone.Planet;
        double x = Mathx.WrapX(from.X, planet.Width), z = Mathx.Wrap(from.Z, planet.Height);
        Vector3? best = null;
        float bestD = within;
        foreach (Deposit d in planet.Map.Deposits.InRect(x - within, z - within, x + within, z + within))
        {
            if (d.Kind != kind) continue;
            planet.Source.OreCentre(d, out double ox, out double oy, out double oz);
            Vector3 at = planet.NearestCopy(new Vector3((float)ox, (float)oy, (float)oz), from);
            float dist = new Vector2(at.X - from.X, at.Z - from.Z).Length();
            if (dist < bestD)
            {
                bestD = dist;
                best = at;
            }
        }
        return best;
    }
}
