using System;
using System.Collections.Generic;
using System.Linq;
using Planets.Gen;
using Planets.Look;

namespace PlanetSurface.Skies;

public enum OrbiterKind { Star, Planet, GasGiant, Moon, Belt }

/// <summary>A position or direction in metres, in doubles: a system is far too wide for floats.</summary>
public readonly record struct D3(double X, double Y, double Z)
{
    public static D3 operator +(D3 a, D3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static D3 operator -(D3 a, D3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static D3 operator *(D3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);
    public double Dot(D3 b) => X * b.X + Y * b.Y + Z * b.Z;
    public D3 Cross(D3 b) => new(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
    public double Length => Math.Sqrt(Dot(this));
    public D3 Normalized => this * (1.0 / Length);
}

/// <summary>What a start system's bodies are marked as holding (the drive and ship materials; never a jump fuel). Landing and mining come later: this says where.</summary>
public static class Deposit
{
    // No marker for warp fuel or hyperspace fuel, ever: they are found by going there (DepositField.FuelKinds).
    public const string WarpDrive = "warp-drive-materials";
    public const string HyperdriveMaterials = "hyperdrive-materials";
    public const string CapitalMaterials = "capital-ship-materials";
}

/// <summary>One real thing in a system: a star, a world, a moon, a belt. Anything seen in any sky is one of these.</summary>
public sealed record Orbiter
{
    public string Name { get; init; } = "";
    public OrbiterKind Kind { get; init; }
    /// <summary>What it circles; null for the star.</summary>
    public Orbiter Parent { get; init; }
    /// <summary>Its own radius, m. A belt has none: it is a band of rock at <see cref="Orbit"/>.</summary>
    public double Radius { get; internal set; }
    /// <summary>Its own seed, from the system's: the same for everyone.</summary>
    public ulong Seed { get; internal set; }
    /// <summary>The world you would land on: its type read from its zone. Null for what has no ground (the star, giants, belts).</summary>
    public PlanetSpec Spec { get; internal set; }
    /// <summary>What the star and the giants have instead of ground: bands, storms, spots. Null for everything else.</summary>
    public Geography Geography { get; internal set; }
    /// <summary>The radius of its orbit round <see cref="Parent"/>, m.</summary>
    public double Orbit { get; init; }
    /// <summary>Days per orbit, in the home world's days.</summary>
    public double Period { get; init; } = 1.0;
    /// <summary>Days per turn on its own axis, in the home world's days.</summary>
    public double DayLength { get; init; } = 1.0;
    /// <summary>Where on its orbit it stood at day 0, in turns.</summary>
    public double Phase0 { get; init; }
    public double Inclination { get; init; }
    public double Node { get; init; }
    /// <summary>A belt's width, m.</summary>
    public double BeltWidth { get; init; }
    /// <summary>Its habitable distance: its true orbit over the square root of its star's brightness. Sunlight is 1 / h².</summary>
    public double Habitable { get; init; }
    /// <summary>What it holds: <see cref="Deposit"/>.</summary>
    public List<string> Deposits { get; } = new();
    /// <summary>Its temperature with no air, K, from its sunlight.</summary>
    public double Temperature { get; init; }
    /// <summary>Its zone, 1 (furnace) to 8 (deep): <see cref="StarSystem.ZoneAt"/>. 4 is the heart of the habitable band.</summary>
    public int Zone { get; init; }
    public Rgb Color { get; init; }
    public Rgb Band { get; init; }
    public bool Rings { get; init; }
    public Rgb RingColor { get; init; }
    /// <summary>The ring plane's normal, in the system's frame.</summary>
    public D3 RingNormal { get; init; } = new(0, 1, 0);
}

/// <summary>A point on a world, resolved upward: which world, where on it, and where that is in the system at this moment.</summary>
public readonly record struct Whereabouts(Orbiter World, double Longitude, double Colatitude, double Altitude, D3 InSystem);

/// <summary>A body as seen from one eye at one moment. Every direction is in the home globe's frame.</summary>
public readonly record struct Sighting(Orbiter Body, D3 Direction, double Distance, float Angular, D3 ToItsLight);

/// <summary>
/// The one source of truth for where everything in a system is: the star, every world, every moon,
/// each on a real orbit, at any moment. The surface's sky and space's sky both read it, so a body is
/// in the same direction, at the same size and in the same phase from any eye: only the air between
/// changes how it looks. Nothing here is a backdrop, and nothing is placed by a crossing.
///
/// Frames: the system's own is fixed to the stars with the star at the origin, orbits round its Y
/// axis. The home globe's is the one <c>GlobeMap</c> uses (the map's north pole +Y): it turns in the
/// system's frame with the day. <see cref="Orient"/> is the turn between them: it is what makes the
/// map's clock true, so the clock's noon is noon at <see cref="NoonLongitude"/>.
///
/// It is live, and has no state of its own: every position comes from one clock, days since the
/// system's day 0, whose fraction is the time of day. A save holds that one number. Planets and
/// moons move along their orbits as it runs, with years short enough (<see cref="TemperateYearDays"/>)
/// that the movement shows in play.
///
/// It is the middle of a hierarchy that works both ways: <see cref="StarKey"/> names this star to the
/// galaxy's catalogue, <see cref="Bodies"/>, <see cref="Under"/> and <see cref="Position"/> feed the
/// system and high-orbit maps, and <see cref="Up"/> and <see cref="Locate"/> take any point on a
/// world up to its orbit, its place in the system and its star.
///
/// The build follows docs/solar-systems.md: everything is worked in h, the habitable distance, then
/// squeezed once for the chart, and bodies keep the 1:2,500 ruler of the worlds. Not yet in: the
/// 40 world types read from the zones, trojans, the system exceptions and binaries, and a start star
/// that doesn't still suit the planet (<c>SkySystem.PickStar</c> runs the old way round). Add to
/// <see cref="Bodies"/> however the recipe grows, and every view picks them up.
/// </summary>
public sealed class StarSystem
{
    /// <summary>One chart AU, m. Distances have their own ruler: the worlds' would put it at 60,000 km.</summary>
    public const double AuMetres = 4e6;

    /// <summary>Under this a body would be less than a pixel or so: it is drawn this size, in every view, as a point of light is.</summary>
    public const float LeastAngular = 0.0011f;

    /// <summary>The least ratio between two neighbouring orbits round the star, in h: a chain's own smallest step.</summary>
    public const double LeastOrbitStep = 1.45;

    /// <summary>The least ratio between two moons' orbits round one parent.</summary>
    public const double LeastMoonStep = 1.25;

    /// <summary>
    /// Where a start world's thruster moon stands, centre to centre, in the world's radii. Empyrion's
    /// scenarios put a planet's moon 8.3 to 12.3 km from the planet in its orbit playfield
    /// (Default Akua-Omicron's Sectors.yaml: Akua Moon 11.5 km, Omicron Moon 11.8 km, Oscutune Moon
    /// 8.3 km); for a 16 km start world and an 8 km moon this range leaves that much open space
    /// between the top of the world's crossing band and the moon's ground: about 1 to 1.5 minutes at
    /// a small vessel's 130 m/s.
    /// </summary>
    public const double ThrusterMoonFrom = 5.5, ThrusterMoonTo = 7.0;

    /// <summary>The farthest the fuel moon may stand from a start world's centre, in its radii.</summary>
    public const double ThrusterReachRadii = ThrusterMoonTo;

    /// <summary>The radius of the biggest world you can land on (the largest width, <see cref="PlanetSpec.Widths"/>), m.</summary>
    public static readonly double LargestLandableRadius = PlanetSpec.Widths[^1] * 1000.0 / Math.Tau;

    /// <summary>A giant's radius: eight to eleven of the home world's, and always bigger than the biggest world you could land on (so the star, bigger still, and every giant out-size every world).</summary>
    static double GiantRadius(double homeRadius, float roll) => Math.Max(homeRadius * (8.0 + 3.0 * roll), LargestLandableRadius * 1.5);

    /// <summary>Surface gravity of a world with ground, m/s², given the start world's: it follows the world's size alone.</summary>
    public float GravityOf(Orbiter b, float homeGravity) => homeGravity * b.Spec.GravityScale / Home.Spec.GravityScale;

    /// <summary>The longitude whose solar time the game's clock tells, radians: the map's middle, where start sites stand.</summary>
    public const double NoonLongitude = Math.PI;

    /// <summary>A temperate world's year, game days: the one number that sets how fast the system runs (data: orbit.json).</summary>
    public static double TemperateYearDays = 24.0;

    /// <summary>
    /// How much bigger stars stand in the sky than at the chart's plain scale (data: orbit.json):
    /// a sun you notice, a sci-fi cover's. The one number every star's size in every sky comes
    /// from; its light is the world's own, never brighter for being bigger.
    /// </summary>
    public static double StarSkyScale = 3.0;

    /// <summary>The galaxy catalogue's key for this star.</summary>
    public ulong StarKey { get; private set; }

    public Orbiter Star { get; private set; }
    public Orbiter Home { get; private set; }
    /// <summary>The giant this world circles, if it is a moon of one.</summary>
    public Orbiter Neighbour { get; private set; }
    public IReadOnlyList<Orbiter> Bodies => _bodies;
    /// <summary>The moon a start can reach on thrusters alone, with warp fuel and what a warp drive takes.</summary>
    public Orbiter FuelMoon { get; private set; }
    public double Luminosity { get; private set; }
    /// <summary>The chart's spread for this star: chart distance = spread × h^0.7 AU.</summary>
    public double Spread { get; private set; }
    public double Tilt { get; private set; }

    /// <summary>The globe's spin axis in the system's frame.</summary>
    public D3 Axis { get; private set; } = new(0, 1, 0);

    readonly List<Orbiter> _bodies = new();

    /// <summary>Seen from a body other than the start world: its globe turns by its own spin (<see cref="Turned"/>).</summary>
    bool _spun;

    /// <summary>The system as its start world sees it (itself, unless this is <see cref="SeenFrom"/> another body).</summary>
    public StarSystem Origin { get; private set; }

    /// <summary>
    /// The same system, the same bodies on the same clock, seen from another of its bodies: that
    /// body is <see cref="Home"/>, and its globe's frame is its own map's (its axis, its spin). A
    /// moon's surface sky reads this, so from the moon the start world hangs where it really is.
    /// </summary>
    public StarSystem SeenFrom(Orbiter body)
    {
        if (body == null || body == Home) return this;
        StarSystem origin = Origin ?? this;
        if (body == origin.Home) return origin;
        var s = new StarSystem
        {
            StarKey = StarKey, Star = Star, Home = body, FuelMoon = FuelMoon, Luminosity = Luminosity, Spread = Spread, Tilt = Tilt,
            Neighbour = body.Parent?.Kind == OrbiterKind.GasGiant ? body.Parent : null, Axis = body.RingNormal, _spun = true, Origin = origin,
        };
        s._bodies.AddRange(_bodies);
        return s;
    }

    // ---- Where things are ----------------------------------------------------------------------

    /// <summary>A body's position in the system's frame, m.</summary>
    public D3 Position(Orbiter b, double days)
    {
        D3 p = new(0, 0, 0);
        for (; b?.Parent != null; b = b.Parent)
        {
            double a = (days / b.Period + b.Phase0) * Math.Tau;
            double x = Math.Cos(a) * b.Orbit, z = Math.Sin(a) * b.Orbit;
            double y = z * Math.Sin(b.Inclination);
            z *= Math.Cos(b.Inclination);
            double cn = Math.Cos(b.Node), sn = Math.Sin(b.Node);
            p += new D3(x * cn + z * sn, y, z * cn - x * sn);
        }
        return p;
    }

    /// <summary>A body, then what it circles, on up to the star.</summary>
    public IEnumerable<Orbiter> Up(Orbiter b)
    {
        for (; b != null; b = b.Parent) yield return b;
    }

    /// <summary>What circles a body directly.</summary>
    public IEnumerable<Orbiter> Under(Orbiter parent)
    {
        foreach (Orbiter b in _bodies)
            if (b.Parent == parent) yield return b;
    }

    /// <summary>A point on the home world at a moment, taken up into the system.</summary>
    public Whereabouts Locate(double longitude, double colatitude, double altitude, double days)
    {
        var (_, up, _) = FrameAt(longitude, colatitude);
        return new Whereabouts(Home, longitude, colatitude, altitude, Position(Home, days) + Orient(days).ToSystem(up * (Home.Radius + altitude)));
    }

    /// <summary>How far a body has turned on its axis at a moment, radians.</summary>
    public static double SpinOf(Orbiter b, double days) => days / b.DayLength * Math.Tau;

    /// <summary>A body's axis and the way its map's zero longitude faces at a moment, in the system's frame.</summary>
    public (D3 Axis, D3 Zero) Turned(Orbiter b, double days)
    {
        D3 axis = b.RingNormal;
        D3 side = Math.Abs(axis.Y) > 0.9 ? new D3(1, 0, 0) : new D3(0, 1, 0);
        side = (side - axis * side.Dot(axis)).Normalized;
        double spin = SpinOf(b, days);
        return (axis, side * Math.Cos(spin) + axis.Cross(side) * Math.Sin(spin));
    }

    /// <summary>The home globe's axes in the system's frame.</summary>
    public readonly record struct Orientation(D3 X, D3 Y, D3 Z)
    {
        public D3 ToSystem(D3 g) => X * g.X + Y * g.Y + Z * g.Z;
        public D3 ToGlobe(D3 s) => new(X.Dot(s), Y.Dot(s), Z.Dot(s));
    }

    /// <summary>
    /// How the globe stands at a moment. <paramref name="days"/> counts days from noon-less day 0:
    /// its fraction is the time of day at <see cref="NoonLongitude"/>, 0.5 being noon, so the turn is
    /// worked from where the star is rather than kept as a spin of its own.
    /// </summary>
    public Orientation Orient(double days)
    {
        if (_spun)
        {
            // Another body's own spin, as its surface map and every sky that draws it have it (Turned).
            var (axis, zero) = Turned(Home, days);
            return new Orientation(axis.Cross(zero), axis, zero);
        }
        D3 a = Axis, toStar = (Position(Star, days) - Position(Home, days)).Normalized;
        D3 sub = (toStar - a * toStar.Dot(a)).Normalized; // the sub-solar meridian
        double hour = (days - Math.Floor(days) - 0.5) * Math.Tau;
        D3 east = a.Cross(sub);
        D3 m = sub * Math.Cos(hour) + east * Math.Sin(hour);
        D3 e = a.Cross(m);
        D3 z = m * Math.Cos(NoonLongitude) - e * Math.Sin(NoonLongitude);
        return new Orientation(a.Cross(z), a, z);
    }

    /// <summary>
    /// The star's disc, its angular radius: its real radius over the world's distance from it.
    /// The one size every sky draws the sun at (SkyRig.ShowSun), on the ground, through the climb
    /// and in orbit, so it never changes size on the way up.
    /// </summary>
    public float SunAngular => See(Star, 0.0, new D3(0, Home.Radius, 0)).Angular;

    // ---- Eclipses -----------------------------------------------------------------------------------

    /// <summary>
    /// How much of a disc of angular radius <paramref name="a"/> a disc of radius <paramref name="b"/>
    /// hides, their centres <paramref name="c"/> apart (radians; small angles): 0 to 1. The two
    /// circles' overlap over the first's area.
    /// </summary>
    public static double Covered(double a, double b, double c)
    {
        if (a <= 0.0 || b <= 0.0 || c >= a + b) return 0.0;
        if (c <= Math.Abs(b - a)) return b >= a ? 1.0 : b * b / (a * a);
        double a2 = a * a, b2 = b * b, c2 = c * c;
        double lens = a2 * Math.Acos(Math.Clamp((c2 + a2 - b2) / (2.0 * c * a), -1.0, 1.0))
                    + b2 * Math.Acos(Math.Clamp((c2 + b2 - a2) / (2.0 * c * b), -1.0, 1.0))
                    - 0.5 * Math.Sqrt(Math.Max(0.0, (-c + a + b) * (c + a - b) * (c - a + b) * (c + a + b)));
        return Math.Clamp(lens / (Math.PI * a2), 0.0, 1.0);
    }

    /// <summary>
    /// An eclipse at a point of the system (its frame, m): how much of the star's disc the bodies
    /// between hide (0 none, 1 total), and the one that hides the most. <paramref name="except"/>
    /// is the body the point is on or in. The one answer every view takes its light from: the
    /// ground's sun and sky, the orbit's sun, a moon in a world's shadow, the shadow on a globe.
    /// </summary>
    public (double Covered, Orbiter By) EclipseAt(double days, D3 at, Orbiter except = null)
    {
        D3 toStar = Position(Star, days) - at;
        double starDistance = toStar.Length;
        if (starDistance <= Star.Radius) return (0.0, null);
        D3 sunDir = toStar * (1.0 / starDistance);
        double sun = Math.Asin(Math.Min(Star.Radius / starDistance, 1.0)), open = 1.0, most = 0.0;
        Orbiter by = null;
        foreach (Orbiter b in _bodies)
        {
            if (b.Kind is OrbiterKind.Star or OrbiterKind.Belt || b == except || b.Radius <= 0.0) continue;
            D3 to = Position(b, days) - at;
            double distance = to.Length;
            if (distance <= b.Radius || distance >= starDistance) continue;
            double size = Math.Asin(Math.Min(b.Radius / distance, 1.0));
            double apart = Math.Acos(Math.Clamp(to.Dot(sunDir) / distance, -1.0, 1.0));
            if (apart >= sun + size) continue;
            double f = Covered(sun, size, apart);
            open *= 1.0 - f;
            if (f > most) (most, by) = (f, b);
        }
        return (1.0 - open, by);
    }

    /// <summary>
    /// What can shadow a body now, for drawing its eclipses point by point (an umbra with a soft
    /// penumbra): the bodies between it and the star whose shadow can fall somewhere on it, nearest
    /// the sun's line first, into <paramref name="into"/>. Returns the star's angular radius from the
    /// body. The one answer every view shades a body by: the sky's moons, space's far looks, the globes.
    /// </summary>
    public double Casters(Orbiter body, double days, List<Orbiter> into)
    {
        into.Clear();
        _gaps.Clear();
        D3 at = Position(body, days), toStar = Position(Star, days) - at;
        double starDistance = toStar.Length, R = body.Radius;
        D3 sunDir = toStar * (1.0 / starDistance);
        double sun = Math.Asin(Math.Min(Star.Radius / starDistance, 1.0));
        foreach (Orbiter b in _bodies)
        {
            if (b.Kind is OrbiterKind.Star or OrbiterKind.Belt || b == body || b.Radius <= 0.0) continue;
            D3 to = Position(b, days) - at;
            double distance = to.Length;
            if (distance >= starDistance || distance <= R) continue;
            // Its shadow can reach somewhere on the body: within the sun's radius plus their two sizes from there.
            double gap = Math.Acos(Math.Clamp(to.Dot(sunDir) / distance, -1.0, 1.0)) - Math.Asin(Math.Min((b.Radius + R) / distance, 1.0));
            if (gap < sun) _gaps.Add((gap, b));
        }
        _gaps.Sort((a, b) => a.Gap.CompareTo(b.Gap));
        foreach (var (_, b) in _gaps) into.Add(b);
        return sun;
    }

    readonly List<(double Gap, Orbiter Body)> _gaps = new();

    /// <summary>The eclipse seen from an eye in the home globe's frame (m from its centre).</summary>
    public (double Covered, Orbiter By) Eclipse(double days, D3 eye) => EclipseAt(days, Position(Home, days) + Orient(days).ToSystem(eye), Home);

    /// <summary>Whether a body casts a shadow reddened by its air (a moon in it glows dull red): a world or a giant, not a bare moon.</summary>
    public static bool HasAir(Orbiter b) => b != null && b.Kind is OrbiterKind.GasGiant or OrbiterKind.Planet;

    /// <summary>How fierce the sun is at home, 0 to 1: 1 on a scorched world close in, 0 from a temperate world out (its corona, its glare, the shimmer under it).</summary>
    public float SunHeat => (float)Math.Clamp((0.95 - Home.Habitable) / 0.55, 0.0, 1.0);

    /// <summary>How a body looks from an eye, given in the home globe's frame in metres from its centre.</summary>
    public Sighting See(Orbiter b, double days, D3 eye, Orientation? turned = null)
    {
        Orientation o = turned ?? Orient(days);
        D3 at = Position(b, days), from = Position(Home, days) + o.ToSystem(eye);
        D3 to = at - from;
        double distance = to.Length;
        float angular = MathF.Max((float)Math.Asin(Math.Min(b.Radius / distance, 1.0)), LeastAngular); // a sphere's edge seen from outside
        D3 light = b.Kind == OrbiterKind.Star ? o.ToGlobe(to * (1.0 / distance)) : o.ToGlobe((Position(Star, days) - at).Normalized);
        return new Sighting(b, o.ToGlobe(to * (1.0 / distance)), distance, angular, light);
    }

    /// <summary>Toward the star from an eye, in the globe's frame.</summary>
    public D3 ToStar(double days, D3 eye) => See(Star, days, eye).Direction;

    /// <summary>The surface frame's east, up and south over a longitude and colatitude, in the globe's frame: as <c>GlobeMap.Frame</c>.</summary>
    public static (D3 East, D3 Up, D3 South) FrameAt(double longitude, double colatitude)
    {
        double sl = Math.Sin(longitude), cl = Math.Cos(longitude), sc = Math.Sin(colatitude), cc = Math.Cos(colatitude);
        return (new D3(cl, 0, -sl), new D3(sc * sl, cc, sc * cl), new D3(cc * sl, -sc, cc * cl));
    }

    // ---- The recipe (docs/solar-systems.md, sections 4 and 6) ------------------------------------

    static double LuminosityOf(StarClass s) => s switch
    {
        StarClass.RedDwarf => 0.03, StarClass.Orange => 0.25, StarClass.Yellow => 1.0, StarClass.White => 3.0, _ => 16.0,
    };

    static double StarRadiusOf(StarClass s) => s switch
    {
        StarClass.RedDwarf => 0.4, StarClass.Orange => 0.72, StarClass.Yellow => 1.0, StarClass.White => 1.35, _ => 1.7,
    };

    /// <summary>
    /// How big a star of this class looks from its habitable distance against a yellow one: a dim
    /// star stands close to its worlds, so it looks bigger, and a bright one smaller. Eased (the
    /// square root of the true ratio), so a red dwarf's lava world is never a wall of fire and a
    /// blue-white star's ice world never a dot.
    /// </summary>
    static double LooksAtHome(StarClass s) => Math.Sqrt(StarRadiusOf(s) / Math.Sqrt(LuminosityOf(s)));

    /// <summary>Slots past the frost line, the most a star has.</summary>
    static int OuterSlots(StarClass s, ref Rng rng) => s is StarClass.White or StarClass.BlueWhite ? 1 + rng.Next(2) : 1 + rng.Next(3);

    /// <summary>The zone at a habitable distance: 1 furnace, 4 temperate, 8 deep.</summary>
    public static int ZoneAt(double h) => h < 0.4 ? 1 : h < 0.75 ? 2 : h < 0.95 ? 3 : h < 1.35 ? 4 : h < 1.7 ? 5 : h < 2.7 ? 6 : h < 10.0 ? 7 : 8;

    /// <summary>A world's temperature with no air, K.</summary>
    public static double TemperatureAt(double h) => 278.0 / Math.Sqrt(h);

    /// <summary>Where a world of this type belongs: position decides type, so the given type picks the slot it can only have come from.</summary>
    static double HomeHabitable(Archetype type, float roll) => type switch
    {
        Archetype.Temperate => 0.97 + 0.36 * roll,
        Archetype.Arid => 0.45 + 0.48 * roll,
        Archetype.Ice => 1.75 + 0.9 * roll,
        Archetype.Lava => 0.22 + 0.16 * roll,
        _ => 0.8 + 0.85 * roll,
    };

    /// <summary>The system of a world: the same for everyone, from its spec.</summary>
    /// <param name="width">The map's width, m: its equator's length, so the home globe's size.</param>
    /// <param name="seed">The galaxy catalogue's seed for this star; 0: the world's own, where there is no galaxy.</param>
    public static StarSystem For(PlanetSpec spec, SkySystem sky, double width, ulong seed = 0)
    {
        if (seed == 0) seed = spec.Seed;
        var rng = new Rng(Hash.Of(seed, 0x535953, (long)spec.Archetype, spec.Variant));
        float next() => rng.NextFloat();
        double L = LuminosityOf(sky.Star), homeRadius = width / Math.Tau, spread = Math.Clamp(Math.Pow(L, 0.1), 0.75, 1.6);
        var s = new StarSystem { Luminosity = L, StarKey = seed, Spread = spread };
        double chart(double h) => spread * Math.Pow(h, 0.7) * AuMetres;
        double year(double h) => TemperateYearDays * Math.Pow(h, 1.5);
        double rockMonth(double radii) => 3.8 * Math.Pow(radii / 16.0, 1.5);
        double giantMonth(double radii) => 7.6 * Math.Pow(radii / 16.0, 1.5);
        double phase() => next();
        double tilted() => (next() - 0.5f) * 0.1; // within 3 degrees of the system's plane
        double node() => next() * Math.Tau;
        s.Tilt = (5.0 + 25.0 * next()) * Math.PI / 180.0;
        s.Axis = new D3(Math.Sin(s.Tilt), Math.Cos(s.Tilt), 0);
        // An orbit in the home globe's equator, near enough: within 5 degrees of it.
        double equator() => s.Tilt + (next() - 0.5f) * 0.17;
        double equatorNode() => -Math.PI / 2 + (next() - 0.5f) * 0.17;

        // Its size: a yellow star at a temperate world about 3 degrees across, closer worlds' suns bigger as their distances say.
        var star = new Orbiter { Name = "the star", Kind = OrbiterKind.Star, Radius = 0.0095 * StarSkyScale * LooksAtHome(sky.Star) * chart(1.0), Color = sky.StarColor };
        s.Star = star;
        s._bodies.Add(star);

        // The chains, in h. Starting from the home world's slot keeps the whole chain's spacing.
        double homeH = HomeHabitable(spec.Archetype, next());
        var inner = new List<double>();
        double ratio = 1.45 + 0.5 * next();
        for (double h = 0.28 + 0.14 * next(); h < 2.4; h *= ratio) inner.Add(h);
        int nearest = 0;
        for (int i = 1; i < inner.Count; i++)
            if (Math.Abs(Math.Log(inner[i] / homeH)) < Math.Abs(Math.Log(inner[nearest] / homeH))) nearest = i;
        double scale = homeH / inner[nearest];
        for (int i = 0; i < inner.Count; i++) inner[i] *= scale;
        inner[nearest] = homeH;
        var outer = new List<double>();
        // Past the home world, and with the belt that comes of it clear of any home outside the temperate band.
        double oh = Math.Max(3.4 + 2.1 * next(), homeH > 1.7 ? homeH * 2.75 : homeH * LeastOrbitStep);
        for (int i = 0, n = OuterSlots(sky.Star, ref rng); i < n && oh <= 18.0; i++, oh *= 1.5 + 0.7 * next()) outer.Add(oh);

        // The first giant out sets the rock belt, between its 4:1 and 2:1 resonances, clear of the temperate band.
        double beltFrom = outer[0] * 0.40, beltTo = outer[0] * 0.63;
        bool inBelt(double h) => h > beltFrom && h < beltTo && (h < 0.95 || h > 1.7);
        var rocks = new List<double>();
        for (int i = 0; i < inner.Count; i++)
            if (i != nearest && inner[i] >= 0.2 && inner[i] <= 3.0 && inner[i] * LeastOrbitStep <= outer[0] && !inBelt(inner[i])) rocks.Add(inner[i]);

        // A cold neighbour in reach, always: it takes its slot first, and the belt and the outer worlds stand past it.
        if (!rocks.Exists(h => ZoneAt(h) is 5 or 6))
        {
            double coldH = Math.Max(homeH * 1.5, 1.8);
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach (double h in rocks.Append(homeH))
                    if (coldH < h * LeastOrbitStep && h < coldH * LeastOrbitStep)
                    {
                        coldH = h * LeastOrbitStep * 1.01;
                        moved = true;
                    }
            }
            rocks.Add(coldH);
            if (coldH * 2.2 > outer[0])
            {
                double push = coldH * 2.2 / outer[0];
                for (int i = 0; i < outer.Count; i++) outer[i] *= push;
            }
            beltFrom = Math.Max(outer[0] * 0.40, coldH * 1.15);
            beltTo = Math.Max(outer[0] * 0.63, beltFrom * 1.25);
            rocks.RemoveAll(inBelt);
        }

        Orbiter host = null;
        SkyBody big = null;
        foreach (SkyBody b in sky.Bodies)
            if (b.Kind == BodyKind.GasGiant) big = b;
        if (big != null)
        {
            // A warm giant, whose moon this world is.
            double r = GiantRadius(homeRadius, next());
            host = new Orbiter
            {
                Name = big.Name, Kind = OrbiterKind.GasGiant, Parent = star, Radius = r, Habitable = homeH, Orbit = chart(homeH), Period = year(homeH),
                DayLength = 0.4 + 0.3 * next(), Phase0 = phase(), Inclination = tilted(), Node = node(), Temperature = TemperatureAt(homeH), Zone = ZoneAt(homeH),
                Color = big.Color, Band = big.Band, Rings = big.Rings, RingColor = big.RingColor,
                RingNormal = new D3(Math.Sin(big.RingTilt) * 0.6, Math.Cos(big.RingTilt), Math.Sin(big.RingTilt) * 0.8).Normalized,
            };
            s._bodies.Add(host);
            s.Neighbour = host;
        }
        double monthRadii = big != null ? 1.0 / Math.Tan(big.Radius) : 0.0;
        Orbiter home = new()
        {
            Name = "this world", Kind = host == null ? OrbiterKind.Planet : OrbiterKind.Moon, Parent = host ?? star, Radius = homeRadius, Habitable = homeH,
            Orbit = host == null ? chart(homeH) : host.Radius * monthRadii, Period = host == null ? year(homeH) : giantMonth(monthRadii), Phase0 = phase(),
            Inclination = host == null ? 0.0 : equator(), Node = host == null ? 0.0 : equatorNode(), Temperature = TemperatureAt(homeH), Zone = ZoneAt(homeH),
            Rings = ArchetypeLook.For(spec).Ring, Color = sky.Horizon,
        };
        s.Home = home;
        s._bodies.Add(home);

        // A clear place for a moon round a parent, out from the orbit asked for.
        double clearOrbit(Orbiter parent, double orbit)
        {
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach (Orbiter b in s._bodies)
                    if (b.Parent == parent && orbit < b.Orbit * LeastMoonStep && b.Orbit < orbit * LeastMoonStep)
                    {
                        orbit = b.Orbit * LeastMoonStep * 1.01;
                        moved = true;
                    }
            }
            return orbit;
        }

        // Its moons, S class. The first stands within reach of thrusters. A moon holds no moons (the
        // giant's pull would strip them): on a giant's moon these are its siblings, round the giant.
        Orbiter holder = host ?? home;
        bool first = true;
        foreach (SkyBody b in sky.Bodies)
        {
            if (b.Kind != BodyKind.Moon) continue;
            double r = homeRadius * 0.5;
            double wanted = host != null ? host.Radius * (3.0 + 27.0 * next())
                : first ? homeRadius * (ThrusterMoonFrom + (ThrusterMoonTo - ThrusterMoonFrom) * next()) : Math.Clamp(r / Math.Tan(b.Radius), homeRadius * 14.0, homeRadius * 40.0);
            double orbit = clearOrbit(holder, wanted), month = host != null ? giantMonth(orbit / host.Radius) : rockMonth(orbit / homeRadius);
            first = false;
            s._bodies.Add(new Orbiter
            {
                Name = b.Name, Kind = OrbiterKind.Moon, Parent = holder, Radius = r, Orbit = orbit, Period = month, DayLength = month,
                Phase0 = b.Phase0, Inclination = equator(), Node = equatorNode(), Color = b.Color, Band = b.Color, Habitable = homeH, Temperature = home.Temperature, Zone = home.Zone,
            });
        }

        int number = 1;
        Orbiter world(double h, bool giant, bool ice)
        {
            float tone = next();
            double t = TemperatureAt(h);
            Rgb color = giant ? (ice ? Rgb.Lerp(Rgb.Hex("8fb4d8"), Rgb.Hex("86c4b4"), tone) : Rgb.Lerp(Rgb.Hex("d6ab78"), Rgb.Hex("e3cd96"), tone))
                : t > 330 ? Rgb.Lerp(Rgb.Hex("a07058"), Rgb.Hex("d8c8a8"), tone)
                : t > 210 ? Rgb.Lerp(Rgb.Hex("b89a80"), Rgb.Hex("7898b8"), tone)
                : Rgb.Lerp(Rgb.Hex("c8d8e8"), Rgb.Hex("9098a8"), tone);
            bool rings = giant && next() < 0.45f;
            var w = new Orbiter
            {
                Name = $"world {++number}", Kind = giant ? OrbiterKind.GasGiant : OrbiterKind.Planet, Parent = star, Habitable = h, Orbit = chart(h), Period = year(h),
                Radius = giant ? GiantRadius(homeRadius, next()) : homeRadius * (0.5 + 1.0 * next()), DayLength = giant ? 0.4 + 0.3 * next() : 0.7 + 2.0 * next(),
                Phase0 = phase(), Inclination = tilted(), Node = node(), Temperature = t, Zone = ZoneAt(h), Color = color, Band = Rgb.Lerp(color, Rgb.Hex("5a4a3a"), 0.45f),
                Rings = rings, RingColor = Rgb.Lerp(Rgb.Hex("e8d8b8"), color, 0.3f),
            };
            s._bodies.Add(w);
            return w;
        }
        void moonOf(Orbiter parent, double radii, double period)
        {
            s._bodies.Add(new Orbiter
            {
                Name = $"a moon of {parent.Name}", Kind = OrbiterKind.Moon, Parent = parent, Radius = homeRadius * 0.5, Orbit = clearOrbit(parent, parent.Radius * radii), Period = period, DayLength = period,
                Phase0 = phase(), Inclination = tilted(), Node = node(), Habitable = parent.Habitable, Temperature = parent.Temperature, Zone = parent.Zone,
                Color = Rgb.Hex("b8b0a8"), Band = Rgb.Hex("b8b0a8"),
            });
        }
        void rockMoons(Orbiter parent)
        {
            if (parent.Zone == 1) return;
            int n = parent.Zone == 2 ? (next() < 0.25f ? 1 : 0) : (new[] { 0, 0, 1, 1, 2 })[rng.Next(5)];
            for (int m = 0; m < n; m++)
            {
                double radii = 12.0 + 28.0 * next();
                moonOf(parent, radii, rockMonth(radii));
            }
        }

        foreach (double h in rocks) rockMoons(world(h, false, false));
        bool hasGiant = false;
        foreach (double h in outer)
        {
            bool giant = !hasGiant || next() < 0.55f;
            hasGiant |= giant;
            Orbiter w = world(h, giant, h > 6.0 && next() < 0.6f);
            if (!giant)
            {
                rockMoons(w);
                continue;
            }
            double radii = 3.0 + next();
            for (int m = 0, n = 2 + rng.Next(4); m < n && radii <= 30.0; m++, radii *= 1.4 + 0.5 * next())
                moonOf(w, radii, giantMonth(radii));
        }
        void belt(string name, double from, double to)
        {
            // The band's true edges on the chart: its middle there isn't the middle of h.
            double orbit = (chart(from) + chart(to)) * 0.5, h = Math.Pow(orbit / chart(1.0), 1.0 / 0.7);
            s._bodies.Add(new Orbiter
            {
                Name = name, Kind = OrbiterKind.Belt, Parent = star, Habitable = h, Orbit = orbit, BeltWidth = chart(to) - chart(from), Period = year(h),
                Temperature = TemperatureAt(h), Zone = ZoneAt(h),
            });
        }
        belt("the rock belt", beltFrom, beltTo);
        if (next() < 0.6f) belt("the ice belt", outer[^1] * 1.31, outer[^1] * 1.59);

        // The nearest free orbit round the star from a place, outward.
        double freeOrbit(double h)
        {
            for (bool moved = true; moved;)
            {
                moved = false;
                foreach (Orbiter b in s._bodies)
                    if (b.Parent == star && b.Kind != OrbiterKind.Belt && h < b.Habitable * LeastOrbitStep && b.Habitable < h * LeastOrbitStep)
                    {
                        h = b.Habitable * LeastOrbitStep * 1.01;
                        moved = true;
                    }
            }
            return h;
        }

        // The guarantees: added, never rerolled.
        int worlds() => s._bodies.FindAll(b => b.Kind is OrbiterKind.Planet or OrbiterKind.GasGiant or OrbiterKind.Moon).Count;
        var hosts = s._bodies.FindAll(b => b.Kind == OrbiterKind.Planet && b.Habitable >= 0.75 && b != home);
        if (hosts.Count == 0) hosts.Add(holder);
        while (worlds() < 8)
        {
            double radii = 12.0 + 28.0 * next();
            moonOf(hosts[rng.Next(hosts.Count)], radii, rockMonth(radii));
        }

        // A moon in reach of thrusters alone. On a world that is itself a moon, a sibling sharing its orbit a little ahead.
        Orbiter near = null;
        if (host == null)
            foreach (Orbiter b in s._bodies)
                if (b.Parent == home && b.Orbit <= homeRadius * ThrusterReachRadii && (near == null || b.Orbit < near.Orbit)) near = b;
        if (near == null)
        {
            double radii = ThrusterMoonFrom + (ThrusterMoonTo - ThrusterMoonFrom) * next(), chord = homeRadius * radii;
            // A sister keeps one small arc ahead on the same orbit, so she is always in reach: the one shared orbit.
            near = host == null
                ? new Orbiter
                {
                    Name = "the near moon", Kind = OrbiterKind.Moon, Parent = home, Radius = homeRadius * 0.5, Orbit = chord, Period = rockMonth(radii), DayLength = rockMonth(radii),
                    Phase0 = phase(), Inclination = equator(), Node = equatorNode(), Habitable = homeH, Temperature = home.Temperature, Zone = home.Zone,
                    Color = Rgb.Hex("b8b0a8"), Band = Rgb.Hex("b8b0a8"),
                }
                : new Orbiter
                {
                    Name = "the sister moon", Kind = OrbiterKind.Moon, Parent = host, Radius = homeRadius * 0.5, Orbit = home.Orbit, Period = home.Period, DayLength = home.Period,
                    Phase0 = home.Phase0 + Math.Asin(chord * 0.5 / home.Orbit) / Math.PI, Inclination = home.Inclination, Node = home.Node, Habitable = homeH, Temperature = home.Temperature,
                    Zone = home.Zone, Color = Rgb.Hex("b8b0a8"), Band = Rgb.Hex("b8b0a8"),
                };
            s._bodies.Add(near);
        }
        s.FuelMoon = near;
        near.Deposits.Add(Deposit.WarpDrive);

        // The way on out of the system, a warp away: hyperdrive materials on the cold rocks, capital ship
        // materials in the rock belt. The jump fuels are no marker: warp fuel lies on every airless world
        // (the near moon always is one), hyperspace fuel on the dangerous high-gravity ones (Dress).
        Orbiter far(Func<Orbiter, bool> fits) => s._bodies.Find(b => b != home && b != host && b != near && b.Parent != home && fits(b));
        Orbiter cold = far(b => b.Kind == OrbiterKind.Planet && b.Zone >= 5) ?? world(freeOrbit(Math.Max(homeH * 1.5, 1.8)), false, false);
        cold.Deposits.Add(Deposit.HyperdriveMaterials);
        s._bodies.Find(b => b.Kind == OrbiterKind.Belt).Deposits.Add(Deposit.CapitalMaterials);
        s.Dress(seed, spec);
        return s;
    }

    /// <summary>
    /// Gives every body its seed, and every body with ground the world its zone makes it: one zone table, for
    /// what you see from afar and what you land on. A world's size sets its gravity (<see cref="GravityOf"/>)
    /// and its chance of having no air (<see cref="VacuumChance"/>); a system always holds one dangerous
    /// high-gravity world, made so if the rolls gave none. The star and the giants get their geography instead.
    /// </summary>
    void Dress(ulong seed, PlanetSpec home)
    {
        var widths = new Dictionary<Orbiter, int>();
        for (int i = 0; i < _bodies.Count; i++)
        {
            Orbiter b = _bodies[i];
            b.Seed = Hash.Of(seed, 0x424F4459, i);
            b.Geography = Geography.Of(b);
            if (b == Home)
            {
                b.Spec = home;
                continue;
            }
            if (b.Kind is not (OrbiterKind.Planet or OrbiterKind.Moon)) continue;
            widths[b] = b.Kind == OrbiterKind.Planet ? PlanetSpec.RollWidthKm(b.Seed)
                : b.Parent.Kind == OrbiterKind.GasGiant ? PlanetSpec.RollWidthKm(b.Seed, WorldKind.Moon)
                : b.Parent.Radius * Math.Tau > 20000.0 ? 16 : 8;
            b.Radius = widths[b] * 1000.0 / Math.Tau;
        }
        if (!home.HighGravity && !widths.Values.Any(w => w >= PlanetSpec.StartWidthKm * PlanetSpec.DangerousGravity))
        {
            // A heavy world every system: one of the rocks round the star grows to 32 km, now and then 64.
            var rocks = widths.Keys.Where(b => b.Kind == OrbiterKind.Planet).ToList();
            var rng = new Rng(Hash.Of(seed, 0x4856));
            if (rocks.Count > 0)
            {
                Orbiter heavy = rocks[rng.Next(rocks.Count)];
                widths[heavy] = rng.NextFloat() < 0.1f ? 64 : 32;
                heavy.Radius = widths[heavy] * 1000.0 / Math.Tau;
            }
        }
        foreach (var (b, width) in widths)
        {
            var (type, variant) = TypeOf(b);
            // Vacuum or not: small bodies can't hold air. Only the kinds that keep craters can be airless, so a
            // living world that lost its air is bare rock (or ice, out in the cold) and not a living world.
            bool vacuum = b == FuelMoon || new Rng(Hash.Of(b.Seed, 0x564143)).NextFloat() < VacuumChance(width);
            if (vacuum && !PlanetSpec.Craters(type)) type = b.Zone >= 5 ? Archetype.Ice : Archetype.Arid;
            PlanetSpec spec = PlanetSpec.Roll(b.Seed, type, variant, width);
            if (PlanetSpec.Craters(type)) spec = spec with { Modifiers = vacuum ? spec.Modifiers | Modifier.Cratered : spec.Modifiers & ~Modifier.Cratered };
            b.Spec = spec;
        }
    }

    /// <summary>The chance a world of a width (km) has no air: the smaller, the likelier. The start's thruster moon always has none.</summary>
    public static double VacuumChance(int widthKm) => widthKm switch { <= 8 => 0.9, <= 16 => 0.6, <= 32 => 0.25, _ => 0.05 };

    /// <summary>The type a world's place makes it, from the zone table in docs/solar-systems.md, in the types the generator has.</summary>
    public static (Archetype Type, int Variant) TypeOf(Orbiter b)
    {
        var rng = new Rng(Hash.Of(b.Seed, 0x54595045));
        float roll = rng.NextFloat();
        int variant = rng.NextFloat() < 0.5f ? 0 : 1;
        if (b.Kind == OrbiterKind.Moon)
        {
            if (b.Parent.Kind == OrbiterKind.GasGiant)
            {
                double radii = b.Orbit / b.Parent.Radius;
                if (radii < 6.0) return (Archetype.Lava, variant); // tides
                if (radii < 12.0) return (Archetype.Ice, 1); // warmed ice, geysers, hot springs
            }
            if (b.Zone <= 2) return (Archetype.Arid, 0);
            return b.Zone >= 6 ? (Archetype.Ice, 0) : (Archetype.Arid, 0); // too small to hold air
        }
        return b.Zone switch
        {
            1 => (Archetype.Lava, variant),
            2 => (Archetype.Arid, variant),
            3 => roll < 0.08f ? (Archetype.Alien, 1) : roll < 0.55f ? (Archetype.Arid, variant) : (Archetype.Temperate, 1),
            4 => roll < 0.08f ? (Archetype.Alien, variant) : (Archetype.Temperate, variant),
            5 => roll < 0.08f ? (Archetype.Alien, variant) : roll < 0.4f ? (Archetype.Temperate, 1) : (Archetype.Ice, 1),
            _ => (Archetype.Ice, 0),
        };
    }

    /// <summary>Why this can't be a start system, or null: what the design promises every start.</summary>
    public string StartFault()
    {
        if (FuelMoon?.Spec is not { Airless: true } || !FuelMoon.Deposits.Contains(Deposit.WarpDrive)) return "no vacuum moon holds warp fuel and the drive's materials";
        double reach = (Position(FuelMoon, 0.0) - Position(Home, 0.0)).Length;
        if (reach > Home.Radius * (ThrusterReachRadii + 1.0)) return $"the fuel moon stands {reach / 1000.0:F0} km off, past thrusters";
        foreach (string d in new[] { Deposit.HyperdriveMaterials, Deposit.CapitalMaterials })
            if (!_bodies.Exists(b => b != Home && b != FuelMoon && b.Deposits.Contains(d))) return $"no {d} anywhere a warp away";
        return null;
    }
}
