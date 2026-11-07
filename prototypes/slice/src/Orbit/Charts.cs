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
public sealed record SystemEntry(string Key, string Label, string Kind, float Size, float Orbit, float Bearing, string Note = "", string Parent = null, float Width = 0f);

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

    /// <summary>
    /// Everything in a system at a moment, from the model on the shared clock: the star, every
    /// world, belt and moon. A body round the star stands at its true bearing, its distance out
    /// eased so the inner worlds don't all sit on the star, and <see cref="SystemEntry.Width"/> is
    /// a belt's width on the same scale. A moon names what it circles (<see cref="SystemEntry.Parent"/>):
    /// its true bearing round that, and for <see cref="SystemEntry.Orbit"/> its place out, nearest first.
    /// </summary>
    public static List<SystemEntry> Bodies(StarSystem system, string world, double days, string star = "Star")
    {
        double farthest = 0;
        foreach (Orbiter b in system.Bodies)
            if (b.Parent == system.Star) farthest = Math.Max(farthest, b.Orbit + b.BeltWidth * 0.5);
        float Out(double r) => (float)Math.Sqrt(Math.Max(r, 0.0) / farthest) * 0.95f;
        var keys = new Dictionary<Orbiter, string> { [system.Star] = "star", [system.Home] = "world" };
        foreach (Orbiter b in system.Bodies)
            if (!keys.ContainsKey(b)) keys[b] = $"body{keys.Count}";
        var list = new List<SystemEntry> { new("star", star, "star", 0.5f, 0f, 0f, $"{system.Luminosity:0.##} suns, {system.Bodies.Count - 1} bodies") };
        foreach (Orbiter b in system.Bodies)
        {
            if (b.Kind == OrbiterKind.Star) continue;
            string kind = b == system.Home ? "world" : b.Kind switch { OrbiterKind.GasGiant => "giant", OrbiterKind.Moon => "moon", OrbiterKind.Belt => "belt", _ => "planet" };
            string label = b == system.Home ? world : Tidy(b.Name);
            float size = (float)Math.Clamp(b.Radius / 12000.0, 0.03, 0.3);
            if (b.Parent == system.Star)
            {
                D3 p = system.Position(b, days);
                float out0 = Out(Math.Sqrt(p.X * p.X + p.Z * p.Z));
                float width = b.Kind == OrbiterKind.Belt ? Out(b.Orbit + b.BeltWidth * 0.5) - Out(b.Orbit - b.BeltWidth * 0.5) : 0f;
                list.Add(new SystemEntry(keys[b], label, kind, size, out0, (float)Math.Atan2(p.Z, p.X), Facts(b, system), null, width));
            }
            else
            {
                D3 p = system.Position(b, days) - system.Position(b.Parent, days);
                int place = 0;
                foreach (Orbiter o in system.Under(b.Parent))
                    if (o.Orbit < b.Orbit) place++;
                list.Add(new SystemEntry(keys[b], label, kind, size, place, (float)Math.Atan2(p.Z, p.X), Facts(b, system), keys[b.Parent]));
            }
        }
        return list;
    }

    /// <summary>A body's name for a chart: the model's "moon 2 of 3" is just Moon 2 there.</summary>
    public static string Tidy(string name)
    {
        string n = name;
        foreach (string lead in new[] { "the ", "a " })
            if (n.StartsWith(lead, StringComparison.Ordinal)) n = n[lead.Length..];
        int of = n.IndexOf(" of ", StringComparison.Ordinal);
        if (of > 0 && !n.StartsWith("moon of", StringComparison.Ordinal)) n = n[..of];
        return char.ToUpperInvariant(n[0]) + n[1..];
    }

    /// <summary>What the model knows of a body, for the side of the map.</summary>
    static string Facts(Orbiter b, StarSystem system)
    {
        string where = b.Parent == system.Star ? $"{b.Orbit / StarSystem.AuMetres:0.0} AU, {b.Period:0}-day year" : $"{b.Orbit / 1000.0:0} km round {Tidy(b.Parent.Name)}, {b.Period:0.0}-day month";
        string held = b.Deposits.Count == 0 ? "" : ", " + string.Join(", ", b.Deposits.ConvertAll(d => d.Replace('-', ' ')));
        return $"{where}, {b.Temperature:0} K{held}";
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
