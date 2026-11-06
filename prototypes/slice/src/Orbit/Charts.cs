using System;
using System.Collections.Generic;
using System.Linq;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.Poi;
using Slice.Vessels;

namespace Slice.Orbit;

/// <summary>A thing on a chart: a place found, a deposit, a wreck, a mark of yours.</summary>
public sealed record ChartMark(string Key, string Label, double X, double Z, string Kind);

/// <summary>A body or a place on the system map.</summary>
public sealed record SystemEntry(string Key, string Label, string Kind, float Size, float Orbit, float Bearing, string Note = "");

/// <summary>A star on the star map, in light years from home.</summary>
public sealed record StarEntry(string Name, StarClass Class, float X, float Y, float Light, bool Home);

/// <summary>
/// What is known, shared by everyone in the world (marks are shared, as the design has them): the
/// map levels show only this. Places on the planet are found by going near them, deposits by the
/// compass finding them, wrecks by flying within sight of them, and your own marks by making them.
/// The star map starts with the home star and the neighbours you could travel to (design: every
/// game begins on a border world at the galaxy's rim, and each jump reveals more): the hyperdrive
/// itself is out of scope, so that's all it holds for now.
/// </summary>
public sealed class Charts
{
    readonly Dictionary<string, ChartMark> _marks = new();
    readonly List<SystemEntry> _wrecks = new();
    readonly HashSet<string> _seenWrecks = new();
    int _nextMark;

    /// <summary>How near you must be to find a place on the planet, m, and to make out a wreck in space.</summary>
    public const float PlaceRange = 500f, WreckRange = 3000f;

    public IEnumerable<ChartMark> Marks => _marks.Values;
    public IEnumerable<ChartMark> OfKind(string kind) => _marks.Values.Where(m => m.Kind == kind);

    /// <summary>The wrecks found so far, on the system map.</summary>
    public IReadOnlyList<SystemEntry> Wrecks => _wrecks;

    /// <summary>The destination chosen on the system map (its key), with a route line to it, or null.</summary>
    public string Destination;

    /// <summary>Finds something on the world map. True the first time.</summary>
    public bool Find(string key, string label, double x, double z, string kind)
    {
        if (_marks.ContainsKey(key)) return false;
        _marks[key] = new ChartMark(key, label, x, z, kind);
        return true;
    }

    /// <summary>Your own mark on the map, shared with everyone in the world.</summary>
    public ChartMark Mark(double x, double z)
    {
        var m = new ChartMark($"mark{++_nextMark}", $"MARK {_nextMark}", x, z, "mark");
        _marks[m.Key] = m;
        return m;
    }

    /// <summary>Places on the planet come onto the chart as you come near them (map metres, torus distance).</summary>
    public void LookAround(IEnumerable<(string key, string label, double x, double z)> places, double x, double z, double width, double height)
    {
        foreach (var (key, label, px, pz) in places)
        {
            double dx = Math.Abs(px - x), dz = Math.Abs(pz - z);
            dx = Math.Min(dx, width - dx);
            dz = Math.Min(dz, height - dz);
            if (dx * dx + dz * dz <= PlaceRange * PlaceRange) Find(key, label, px, pz, "place");
        }
    }

    /// <summary>A wreck comes onto the system map once you are within sight of it.</summary>
    public bool SeeWreck(string key, string label, float distance, float orbit, float bearing, string kind)
    {
        if (distance > WreckRange || !_seenWrecks.Add(key)) return false;
        _wrecks.Add(new SystemEntry(key, label, kind, 0.02f, orbit, bearing));
        return true;
    }

    /// <summary>The star's bodies that show on the system map: the star, the world you are on, its moons and any giant, their sizes as its sky shows them.</summary>
    public static List<SystemEntry> Bodies(SkySystem sky, string world)
    {
        var list = new List<SystemEntry>
        {
            new("star", $"{sky.Star} star", "star", 0.5f, 0f, 0f),
            new("world", world, "world", 0.12f, 0.55f, 0.6f, "you started here"),
        };
        int moon = 0;
        foreach (SkyBody b in sky.Bodies)
        {
            bool giant = b.Kind == PlanetSurface.Skies.BodyKind.GasGiant;
            string name = string.IsNullOrEmpty(b.Name) ? (giant ? "Giant" : $"Moon {++moon}") : b.Name;
            list.Add(new SystemEntry(name.ToLowerInvariant().Replace(' ', '-'), name, giant ? "giant" : "moon", Math.Clamp(b.Radius * 2f, 0.03f, 0.3f), giant ? 0.85f : 0.12f + 0.05f * moon, b.Azimuth));
        }
        return list;
    }

    /// <summary>The home star and its neighbours within one hyperdrive jump: seeded, so a world always has the same sky.</summary>
    public static List<StarEntry> Stars(ulong seed, SkySystem sky)
    {
        var rng = new Random(unchecked((int)(seed ^ (seed >> 32))));
        var list = new List<StarEntry> { new("Home", sky.Star, 0f, 0f, 0f, true) };
        int n = 4 + rng.Next(0, 4);
        for (int i = 0; i < n; i++)
        {
            double a = (i + rng.NextDouble() * 0.7) * Math.Tau / n, r = 8 + rng.NextDouble() * 22; // the rim: a jump is about 30 LY
            var cls = (StarClass)rng.Next(0, Enum.GetValues<StarClass>().Length);
            string name = $"{(char)('A' + rng.Next(0, 26))}{(char)('A' + rng.Next(0, 26))}-{rng.Next(100, 999)}";
            list.Add(new StarEntry(name, cls, (float)(Math.Cos(a) * r), (float)(Math.Sin(a) * r), (float)r, false));
        }
        return list;
    }
}

/// <summary>
/// The world map's view of the torus: where the middle of the panel is on the map, and how many
/// metres one pixel covers. Dragging moves it, the wheel zooms it, and it wraps both ways, so a
/// point near the seam shows beside what is across it.
/// </summary>
public sealed class WorldMapView
{
    public readonly double Width, Height;
    public double CentreX, CentreZ;

    /// <summary>Metres a pixel, from showing the whole map to a close look.</summary>
    public double MetresPerPixel;

    public const double Closest = 2.0;

    public WorldMapView(double width, double height, double fitMetresPerPixel)
    {
        Width = width;
        Height = height;
        MetresPerPixel = _widest = fitMetresPerPixel;
    }

    readonly double _widest;

    static double Wrap(double v, double span)
    {
        double r = v % span;
        return r < 0 ? r + span : r;
    }

    static double Nearest(double d, double span)
    {
        d = Wrap(d, span);
        return d > span / 2 ? d - span : d;
    }

    /// <summary>A map point in panel pixels from the panel's centre: the copy of it nearest the centre.</summary>
    public (double px, double py) ToView(double x, double z) => (Nearest(x - CentreX, Width) / MetresPerPixel, Nearest(z - CentreZ, Height) / MetresPerPixel);

    /// <summary>The map point under a pixel from the panel's centre.</summary>
    public (double x, double z) FromView(double px, double py) => (Wrap(CentreX + px * MetresPerPixel, Width), Wrap(CentreZ + py * MetresPerPixel, Height));

    /// <summary>Drags the map by pixels.</summary>
    public void Drag(double px, double py)
    {
        CentreX = Wrap(CentreX - px * MetresPerPixel, Width);
        CentreZ = Wrap(CentreZ - py * MetresPerPixel, Height);
    }

    /// <summary>Zooms in (a step of the wheel up) or out, holding the middle.</summary>
    public void Zoom(int steps) => MetresPerPixel = Math.Clamp(MetresPerPixel * Math.Pow(1.25, steps), Closest, _widest);
}
