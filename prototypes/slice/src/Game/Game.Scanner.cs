using System.Collections.Generic;
using Godot;
using Planets.Gen;
using Slice.Surface;

namespace Slice.Game;

/// <summary>
/// The handheld scanner (free from the suit fabricator): a click pings what lies far off, the nearest
/// deposit of each ore and the places nobody has found yet, and the compass marks each ping for ten
/// seconds while it fades. Nothing stays marked from a ping: a deposit or a place stays on the
/// compass and the map only once you have been near it.
/// </summary>
public partial class Game
{
    /// <summary>A ping on the compass: what it is (an ore's name, or null for a place not yet found), where, and until when.</summary>
    public readonly record struct Ping(string Label, Vector3 At, double Until, bool Place);

    /// <summary>How long a ping stays on the compass, seconds, and how far the handheld scanner reaches, m.</summary>
    public const double PingSeconds = 10;
    public const float ScanRange = 600f;

    /// <summary>The seconds between pings.</summary>
    public const double ScanCooldown = 2;

    readonly List<Ping> _pings = new();
    double _scanReady;

    /// <summary>The pings showing now.</summary>
    public IReadOnlyList<Ping> Pings => _pings;

    /// <summary>How strongly a ping shows: full, then fading out over its last seconds.</summary>
    public float PingAlpha(Ping p) => Mathf.Clamp((float)((p.Until - Stats.Seconds) / 3.0), 0f, 1f);

    /// <summary>Seconds until the scanner can ping again (0: ready).</summary>
    public double ScanWait => System.Math.Max(0, _scanReady - Stats.Seconds);

    /// <summary>A ping: every ore's nearest deposit and the unfound places within reach go on the compass for a while.</summary>
    public bool Scan()
    {
        if (ScanWait > 0 || InOrbit || Zone.Planet == null) return false;
        _scanReady = Stats.Seconds + ScanCooldown;
        _pings.Clear();
        Vector3 feet = Survivor.WorldFeet;
        double until = Stats.Seconds + PingSeconds;
        foreach (var kind in DepositFinder.Kinds)
            if (DepositFinder.Nearest(Zone, kind, feet, ScanRange) is Vector3 at)
                _pings.Add(new Ping(kind == Planets.Gen.DepositKind.Water ? "ice" : kind.ToString().ToLowerInvariant(), at, until, false));
        var planet = Zone.Planet;
        for (int i = 0; i < planet.Pois.Count; i++)
        {
            var o = planet.Pois[i].Origin;
            Vector3 at = planet.NearestCopy(new Vector3(o.X, feet.Y, o.Z), feet);
            if (new Vector2(at.X - feet.X, at.Z - feet.Z).Length() > ScanRange) continue;
            if (Spaceflight.Charts.Has($"poi{i}")) continue; // found already: it is on the compass for good
            _pings.Add(new Ping(null, at, until, true));
        }
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, Survivor.WorldEye(), 1.4f, -10f);
        return true;
    }

    void TickPings() => _pings.RemoveAll(p => p.Until < Stats.Seconds);
}
