using System.Collections.Generic;
using Godot;
using Planets.Gen;
using PoiGenerator.Gen;
using Slice.Items;
using Slice.Surface;

namespace Slice.Game;

/// <summary>
/// The handheld scanner (free from the suit fabricator): a click pings what lies far off. The nearest deposit of
/// each ore within <see cref="ScanRange"/> goes on the compass for ten seconds; every place within the scanner's
/// scan range (the item's data) is marked for thirty, then the mark times out: a scan is a snapshot, not a standing
/// map. A city, or an airfield of many parts, is one place. Nothing stays marked from a ping: a deposit or a place stays on the compass and the
/// map only once you have been near it.
/// </summary>
public partial class Game
{
    /// <summary>A ping: an ore's name (null for a place), where, until when, a place's icon (null for an ore), and a place's chart key.</summary>
    public readonly record struct Ping(string Label, Vector3 At, double Until, string Place = null, string Key = null);

    /// <summary>How long an ore's ping and a place's mark last, seconds, and how far the handheld scanner finds ore, m.</summary>
    public const double PingSeconds = 10, MarkSeconds = 30;
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

    /// <summary>How far the held scanner's scan marks places, m (0: none held).</summary>
    public float PlaceScanRange =>
        Pack.Hand == HandKind.Scanner && Zone?.Data?.Items.TryGet(Pack.HandItem, out var item) == true ? item.Scan : 0f;

    /// <summary>A ping: every ore's nearest deposit and every place within reach go on the compass for a while.</summary>
    public bool Scan()
    {
        if (ScanWait > 0 || InOrbit || Zone.Planet == null) return false;
        _scanReady = Stats.Seconds + ScanCooldown;
        _pings.Clear();
        Vector3 feet = Survivor.WorldFeet;
        foreach (var kind in DepositFinder.Kinds)
            if (DepositFinder.Nearest(Zone, kind, feet, ScanRange) is Vector3 at)
                _pings.Add(new Ping(kind == Planets.Gen.DepositKind.Water ? "ice" : kind.ToString().ToLowerInvariant(), at, Stats.Seconds + PingSeconds));
        _pings.AddRange(PlacesInScan(feet, PlaceScanRange, Stats.Seconds + MarkSeconds));
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, Survivor.WorldEye(), 1.4f, -10f);
        return true;
    }

    /// <summary>The places within <paramref name="range"/> of <paramref name="feet"/> across the ground, as marks lasting until <paramref name="until"/>: each site, a typed place's parts as one mark on its anchor, the city's buildings as one at its middle.</summary>
    public List<Ping> PlacesInScan(Vector3 feet, float range, double until)
    {
        var marks = new List<Ping>();
        var planet = Zone.Planet;
        if (range <= 0f || planet == null) return marks;
        var city = planet.Infra.City;
        void Add(float x, float z, string icon, string key)
        {
            Vector3 at = planet.NearestCopy(new Vector3(x, feet.Y, z), feet);
            if (new Vector2(at.X - feet.X, at.Z - feet.Z).Length() <= range) marks.Add(new Ping(null, at, until, icon, key));
        }
        for (int i = 0; i < planet.Pois.Count; i++)
        {
            var p = planet.Pois[i];
            if (p.Family.Space || city?.Sites.Contains(p) == true || p.Whole != null && p != (p.Whole.Anchor ?? p.Whole.Parts[0])) continue;
            Add(p.Origin.X, p.Origin.Z, p.Family.Tier == PoiTier.Wreck ? "wreck" : p.State == PoiState.Ruined ? "ruin" : "place", $"poi{i}");
        }
        if (city != null) Add((float)city.CenterX, (float)city.CenterZ, "place", "city");
        return marks;
    }

    void TickPings() => _pings.RemoveAll(p => p.Until < Stats.Seconds);
}
