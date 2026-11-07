using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Stars;
using Planets.Gen;
using PlanetSurface.Skies;
using PlanetSurface.Poi;
using Slice.Vessels;

namespace Slice.Orbit;

/// <summary>A thing on a chart: a place found, a deposit, a wreck, a mark of yours.</summary>
public sealed record ChartMark(string Key, string Label, double X, double Z, string Kind);

/// <summary>A body or a place on the system map.</summary>
public sealed record SystemEntry(string Key, string Label, string Kind, float Size, float Orbit, float Bearing, string Note = "");

/// <summary>A catalogue star on the galaxy map: where it lies from you on the map (ly, east along the disc and south), how far off the plane, how far in all, and its direction in the sky.</summary>
public sealed record StarEntry(int Id, string Name, StarKind Kind, float X, float Z, float Height, float Light, bool Home, System.Numerics.Vector3 Direction);

/// <summary>
/// What is known, shared by everyone in the world (marks are shared, as the design has them): the
/// map levels show only this. Places on the planet are found by going near them, deposits by the
/// compass finding them, wrecks by flying within sight of them, and your own marks by making them.
/// The galaxy map shows the stars charted: where you have been and the neighbours in jump range of
/// it, measured in true 3D (design: every game begins on a border world at the galaxy's rim, and
/// each jump reveals more). The sky shows every star; only a chart names them.
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

    /// <summary>The star picked on the galaxy map, marked in the sky by the visor, or -1.</summary>
    public int ChosenStar = -1;

    /// <summary>The stars been to, whose neighbours are charted.</summary>
    public readonly HashSet<int> Visited = new();

    /// <summary>Whether a star is charted: been to, or one jump from somewhere you have been.</summary>
    public bool Charted(Galaxy galaxy, int star)
    {
        foreach (int v in Visited)
            if (v == star || System.Numerics.Vector3.Distance(galaxy.Stars[v].Position, galaxy.Stars[star].Position) <= Galaxy.JumpT1) return true;
        return false;
    }

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

    /// <summary>Takes down one of your own marks (nothing else on the chart can be removed).</summary>
    public bool Unmark(string key) => _marks.TryGetValue(key, out ChartMark m) && m.Kind == "mark" && _marks.Remove(key);

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

    /// <summary>Every star on the galaxy map: those been to, and their neighbours in jump range.</summary>
    public List<int> Shown(Galaxy galaxy)
    {
        var shown = new HashSet<int>(Visited);
        foreach (int v in Visited) shown.UnionWith(galaxy.Neighbours(v));
        return shown.ToList();
    }

    /// <summary>A star and its real neighbours within one hyperdrive jump, nearest first, read from the catalogue and the sky seen from there.</summary>
    public static List<StarEntry> Stars(Galaxy galaxy, SkyView sky, int here)
    {
        Star home = galaxy.Stars[here];
        var list = new List<StarEntry> { new(here, galaxy.Name(here), home.Kind, 0f, 0f, 0f, 0f, true, default) };
        foreach (int id in galaxy.Neighbours(here))
        {
            System.Numerics.Vector3 v = galaxy.Stars[id].Position - home.Position;
            list.Add(new StarEntry(id, galaxy.Name(id), galaxy.Stars[id].Kind, v.X, v.Z, v.Y, v.Length(), false, sky.Direction[id]));
        }
        return list;
    }
}

/// <summary>
/// The galaxy map's view: a camera turning round a point in the galaxy, at a distance. Pure
/// arithmetic, so the map's picking and its tests need no engine.
/// </summary>
public sealed class GalaxyMapView
{
    public System.Numerics.Vector3 Focus;
    public double Yaw = 0.5, Pitch = 0.55;
    public double Distance = 96;

    /// <summary>Nearest and farthest the camera stands from what it turns round, ly: the last wheel step in opens the star.</summary>
    public const double Closest = 14, Widest = 1500;

    public bool AtClosest => Distance <= Closest * 1.001;

    /// <summary>Where a point of the galaxy falls, in pixels from the view's middle, with a focal length in pixels. False behind the camera.</summary>
    public bool Project(System.Numerics.Vector3 p, float focal, out System.Numerics.Vector2 at, out float depth)
    {
        System.Numerics.Vector3 v = p - Focus;
        float cy = (float)Math.Cos(Yaw), sy = (float)Math.Sin(Yaw), cp = (float)Math.Cos(Pitch), sp = (float)Math.Sin(Pitch);
        float x = cy * v.X - sy * v.Z, z = sy * v.X + cy * v.Z;
        float y = cp * v.Y - sp * z, toward = sp * v.Y + cp * z;
        depth = (float)Distance - toward;
        at = default;
        if (depth < 1f) return false;
        at = new System.Numerics.Vector2(focal * x / depth, -focal * y / depth);
        return true;
    }

    /// <summary>A drag: across turns it round the galaxy's axis, up and down tips it, never past the pole.</summary>
    public void Turn(float dx, float dy)
    {
        Yaw += dx * 0.006;
        Pitch = Math.Clamp(Pitch + dy * 0.006, -1.5, 1.5);
    }

    public void Zoom(int steps) => Distance = Math.Clamp(Distance * Math.Pow(1.25, steps), Closest, Widest);
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

    public bool AtClosest => MetresPerPixel <= Closest * 1.001;
    public bool AtWidest => MetresPerPixel >= _widest * 0.999;
}
