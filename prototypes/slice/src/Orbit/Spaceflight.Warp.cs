using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>The warp drive's trips (data/warp.json).</summary>
public sealed class WarpRules
{
    public double TripSecondsPerAu { get; set; } = 6;
    public double LeastTripSeconds { get; set; } = 2;
    public float ShortRangeSpeed { get; set; } = 30;
    public float Inside { get; set; } = 300;
    public double HighOrbitAcross { get; set; } = 45;
    public float Clearance { get; set; } = 1500;
    public float WreckClearance { get; set; } = 450;
    public float WreckStandOff { get; set; } = 350;
    public float RecentreFrom { get; set; } = 20000;

    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    static WarpRules _shared;
    public static WarpRules Shared => _shared ??= JsonSerializer.Deserialize<WarpRules>(File.ReadAllText(GameData.Own("data/warp.json")), Json)
        ?? throw new InvalidDataException("empty warp.json");
}

/// <summary>
/// A warp's route as the console shows it: to what, short-range (free, in the orbit you are in) or not, its chart AU, the
/// warp cells it burns, its seconds (spool and trip), and what stops it (null: nothing).
/// </summary>
public sealed record WarpRoute(Orbiter Body, Vessel Wreck, string Name, bool Short, double Au, int Cells, double SpoolSeconds, double TripSeconds, string Refusal)
{
    public double Seconds => SpoolSeconds + TripSeconds;
}

/// <summary>
/// The warp drive (DESIGN.md travel, solar-systems.md section 8): any Flight structure carrying a powered warp drive, with
/// warp cells in its core storage and its shield down, jumps to any world, moon, giant, star or belt of the system, about a
/// cell a chart AU, and arrives at rest in the destination's highest orbit (a giant's and the star's high orbit), clear of
/// bodies and wrecks, facing it, the sky as the pilot left it. A body or wreck of the orbit you are in is a short-range warp:
/// free, from 30 m/s. One <see cref="Jump"/> carries it, shared with the fold drive.
/// </summary>
public partial class Spaceflight
{
    public const string WarpDrive = "warp-drive";

    public WarpRules Warping = WarpRules.Shared;

    /// <summary>The structure a warp would carry: the Flight structure you pilot, or null.</summary>
    Vessel WarpCarrier => Game.Survivor?.Seated is { Flight: not null, IsDerelict: false } v ? v : null;

    /// <summary>Where you are in the system's frame: your eye in orbit, else the body you are on.</summary>
    D3 HereInSystem()
    {
        StarSystem model = Game.Surface.Planet.StarSystem;
        if (Game.InOrbit) return ToSystem(Orbit, Game.Camera.GlobalPosition.ToNumerics());
        return model.Position(Game.Zone.Body ?? model.Home, Game.Surface.Sky.Days);
    }

    /// <summary>A warp's route to a body of the system from where you are now.</summary>
    public WarpRoute Route(Orbiter body) => Route(body, null);

    /// <summary>A short-range warp's route to a wreck of the orbit you are in.</summary>
    public WarpRoute Route(Vessel wreck) => Route(null, wreck);

    WarpRoute Route(Orbiter body, Vessel wreck)
    {
        Vessel v = WarpCarrier;
        StarSystem model = Game.Surface.Planet.StarSystem;
        double days = Game.Surface.Sky.Days;
        bool inOrbit = Game.InOrbit;
        bool isShort = wreck != null || inOrbit && CelestialOf(Orbit, body) != null;
        D3 at = wreck != null ? ToSystem(Orbit, wreck.Body.Position) : model.Position(body, days);
        double au = (at - HereInSystem()).Length / StarSystem.AuMetres;
        DriveFuel fuel = v?.Power.Rules.Drives.GetValueOrDefault(WarpDrive);
        double per = fuel?.Per ?? 1, spool = fuel?.Spool ?? 6;
        int cells = isShort ? 0 : (int)Math.Ceiling(au * per - 1e-9);
        double trip = Math.Max(Warping.LeastTripSeconds, isShort ? 0 : au * Warping.TripSecondsPerAu);
        string name = wreck != null ? wreck.Name : body == model.Home ? "Your world" : Charts.Tidy(body.Name);
        return new WarpRoute(body, wreck, name, isShort, au, cells, spool, trip, Refusal(v, body, wreck, isShort, cells));
    }

    /// <summary>What stops a warp now, as the console shows it, or null.</summary>
    string Refusal(Vessel v, Orbiter body, Vessel wreck, bool isShort, int cells)
    {
        if (Jumping?.Running == true) return Jumping.Holding ? "IN WARP" : "SPOOLING";
        if (v == null) return "NOT PILOTING";
        if (v.Power.Range(WarpDrive) is not { } range) return "NOT FITTED";
        if (v.Structure.Mode != CoreMode.Flight) return "NOT IN FLIGHT";
        if (!Game.InOrbit || Band < 1f) return "NOT IN SPACE";
        if (wreck == null && Orbit.Anchor.Body == body && v.Body.Position.ToGodot().DistanceTo(Orbit.Anchor.Centre) < HighOrbit(Orbit, Orbit.Anchor) + 500f) return "HERE";
        if (isShort && v.Body.Velocity.Length() < Warping.ShortRangeSpeed) return $"UNDER {Warping.ShortRangeSpeed:F0} M/S";
        if (v.Power.ShieldUp) return "SHIELDS UP";
        if (range.Reach < cells) return "NO FUEL";
        if (v.Power.Supply <= 0) return "NO POWER";
        return null;
    }

    /// <summary>Starts a warp to a body of the system. Null once it spools; else what stops it.</summary>
    public string Warp(Orbiter body) => Warp(Route(body));

    /// <summary>Starts a short-range warp to a wreck of the orbit you are in.</summary>
    public string Warp(Vessel wreck) => Warp(Route(wreck));

    string Warp(WarpRoute r)
    {
        if (r.Refusal != null) return r.Refusal;
        return StartJump(WarpCarrier, WarpDrive, r.Short ? 0 : r.Au, r.TripSeconds, new WarpTo(this, r, HereInSystem()));
    }

    /// <summary>
    /// The radius of a body's highest orbit, from its centre (m): for a world, just inside the edge of the space where it is
    /// the frame you fly in (its crossing, the band, and as far again as a moon takes over within); for a giant or the star, as
    /// far out as it spans <see cref="WarpRules.HighOrbitAcross"/> degrees, past any rings; a belt's frame is a point in it.
    /// </summary>
    public float HighOrbit(OrbitZone o, Celestial c)
    {
        if (o.HasGround || !c.IsHome)
            return c.GlobeRadius + c.Ceiling + Rules.Crossing.Band + Rules.Space.TakesOverWithin - Warping.Inside;
        if (c.Body.Kind is not (OrbiterKind.GasGiant or OrbiterKind.Star)) return 0f;
        // As far out as it stands HighOrbitAcross degrees across (it fills the view), and clear of its rings.
        double r = c.Body.Radius / Math.Sin(Warping.HighOrbitAcross * Math.PI / 360.0);
        if (c.Body.Rings) r = Math.Max(r, c.Body.Radius * SpaceSky.RingOuter * 1.05);
        return (float)r;
    }

    /// <summary>The warp parks the orbit it left as it was (its environment and sun off), to come back to.</summary>
    void LeaveOrbit(SurfaceZone from)
    {
        if (from is OrbitZone o)
        {
            o.Space.Show(false, 0f);
            o.Space.Light(0f);
            foreach (Celestial c in o.Bodies)
                if (c.Globe != null) c.Globe.Visible = false; // its globes hang outside the zone (the far land from its surfaces): none stays in another orbit's view
        }
        Park(from);
    }

    /// <summary>A warp's destination: the orbit holding the body (made for the jump if there is none yet) and the point in its highest orbit.</summary>
    sealed class WarpTo : IJumpDestination
    {
        readonly Spaceflight _f;
        WarpRoute _route;
        readonly D3 _from;
        OrbitZone _zone;
        SurfaceZone _home;
        IEnumerator<bool> _making;
        bool _made;

        public WarpTo(Spaceflight flight, WarpRoute route, D3 from)
        {
            _f = flight;
            _route = route;
            _from = from;
        }

        public string Name => _route.Name;

        /// <summary>The orbit it arrives in (once known).</summary>
        public OrbitZone Zone => _zone;
        SurfaceZone IJumpDestination.Zone => _zone;

        public void Begin(Spaceflight flight)
        {
            if (_route.Wreck != null) _zone = (OrbitZone)_route.Wreck.Zone;
            else _zone = flight.OrbitOf(_route.Body);
            if (_zone != null)
            {
                // A moon's surface starts being made now, so it is there when you come down.
                if (CelestialOf(_zone, _route.Body)?.Surface is { Making: false } s) s.Make();
                return;
            }
            _made = true;
            _making = flight.MakeOrbit(RootOf(_route.Body), _route.Body, (o, h) => (_zone, _home) = (o, h)).GetEnumerator();
        }

        public bool Step(Spaceflight flight)
        {
            if (_making == null) return _zone.Ready;
            return _making.MoveNext() && _making.Current;
        }

        public JumpArrival Arrival(Spaceflight flight, Vessel carrier)
        {
            OrbitZone o = _zone;
            o.Move(); // its bodies where the clock has them now
            StarSystem model = o.Home.Seen;
            double days = o.Days();
            Celestial c = _route.Wreck != null ? null : CelestialOf(o, _route.Body);
            N.Vector3 centre = _route.Wreck != null ? _route.Wreck.Body.Position : c?.Centre.ToNumerics() ?? FromSystem(o, model.Position(_route.Body, days));
            float radius = _route.Wreck != null ? flight.Warping.WreckStandOff : flight.HighOrbit(o, c);
            N.Quaternion toWorld = N.Quaternion.Conjugate(WorldToSystem(o));
            D3 back = _from - ToSystem(o, centre);
            N.Vector3 away = back.Length > 1 ? N.Vector3.Normalize(N.Vector3.Transform(new N.Vector3((float)back.X, (float)back.Y, (float)back.Z), toWorld)) : N.Vector3.UnitY;
            N.Vector3 at = flight.ClearOf(o, centre, away, radius, c);
            if (at.Length() > flight.Warping.RecentreFrom)
            {
                // Far out in its zone (the star's high orbit, a giant's far moon): that point becomes the zone's origin.
                o.Recentre(at);
                centre -= at;
                at = N.Vector3.Zero;
            }
            // The structure keeps its turn in the system's frame (the sky as it was), turned the least way for its pilot to face what it went to.
            N.Quaternion turn = N.Quaternion.Normalize(toWorld * WorldToSystem(flight.Orbit) * carrier.Body.Rotation);
            N.Vector3 nose = N.Vector3.Transform((carrier.PilotBasis * Vector3.Forward).ToNumerics(), turn), want = N.Vector3.Normalize(centre - at);
            if (radius < 1f) want = N.Vector3.Normalize(-away);
            turn = N.Quaternion.Normalize(Between(nose, want) * turn);
            return new JumpArrival(o, at, turn);
        }

        public void Left(Spaceflight flight, SurfaceZone from)
        {
            flight.LeaveOrbit(from);
            LetGo();
        }

        public void Dropped(Spaceflight flight)
        {
            _making?.Dispose();
            if (_made) flight.Unmake(_zone, _home);
            LetGo();
        }

        /// <summary>Done with: the jump's record keeps its name and no hold on a zone, a wreck or the system's model.</summary>
        void LetGo()
        {
            (_zone, _home, _making) = (null, null, null);
            _route = _route with { Body = null, Wreck = null };
        }
    }

    /// <summary>The least turn taking one direction to another.</summary>
    static N.Quaternion Between(N.Vector3 from, N.Vector3 to)
    {
        float d = N.Vector3.Dot(from, to);
        if (d > 0.99999f) return N.Quaternion.Identity;
        N.Vector3 axis = N.Vector3.Cross(from, to);
        if (axis.LengthSquared() < 1e-8f) axis = MathF.Abs(from.X) < 0.9f ? N.Vector3.Cross(from, N.Vector3.UnitX) : N.Vector3.Cross(from, N.Vector3.UnitY);
        return N.Quaternion.CreateFromAxisAngle(N.Vector3.Normalize(axis), MathF.Acos(Math.Clamp(d, -1f, 1f)));
    }

    /// <summary>
    /// The arrival point round <paramref name="centre"/> at <paramref name="radius"/>: on the side it was come from, or the
    /// nearest way round from there that is clear of every other body's air and of wrecks, with a clear line to the centre.
    /// </summary>
    N.Vector3 ClearOf(OrbitZone o, N.Vector3 centre, N.Vector3 away, float radius, Celestial self)
    {
        N.Vector3 side = N.Vector3.Normalize(MathF.Abs(away.Y) < 0.9f ? N.Vector3.Cross(away, N.Vector3.UnitY) : N.Vector3.Cross(away, N.Vector3.UnitX));
        for (int k = 0; k < 97; k++)
        {
            N.Vector3 d = away;
            if (k > 0)
            {
                float tilt = MathF.PI / 12f * ((k + 7) / 8), round = k * 2.39996f; // rings of 8, 15 degrees apart, a golden angle round
                N.Quaternion q = N.Quaternion.CreateFromAxisAngle(away, round) * N.Quaternion.CreateFromAxisAngle(side, tilt);
                d = N.Vector3.Transform(away, q);
            }
            N.Vector3 p = centre + d * radius;
            if (Clear(o, p, centre, self)) return p;
        }
        return centre + away * radius;
    }

    bool Clear(OrbitZone o, N.Vector3 p, N.Vector3 centre, Celestial self)
    {
        foreach (Celestial b in o.Bodies)
        {
            if (b == self) continue;
            N.Vector3 bc = b.Centre.ToNumerics();
            if (N.Vector3.Distance(p, bc) < b.AtmosphereRadius + b.Ceiling + Warping.Clearance || Crosses(p, centre, bc, b.GlobeRadius + 200f)) return false;
        }
        foreach (var (body, shell) in o.OtherSolids)
        {
            N.Vector3 bc = shell.Position.ToNumerics();
            if (N.Vector3.Distance(p, bc) < (float)body.Radius + Warping.Clearance || Crosses(p, centre, bc, (float)body.Radius + 200f)) return false;
        }
        foreach (Vessel w in o.Wrecks)
            if (N.Vector3.Distance(p, w.Body.Position) < Warping.WreckClearance) return false;
        return true;
    }

    /// <summary>The segment a to b passes within r of c (its ends excepted: b is what it faces).</summary>
    static bool Crosses(N.Vector3 a, N.Vector3 b, N.Vector3 c, float r)
    {
        N.Vector3 ab = b - a;
        float t = Math.Clamp(N.Vector3.Dot(c - a, ab) / MathF.Max(ab.LengthSquared(), 1e-6f), 0f, 1f);
        return t > 0f && t < 1f && N.Vector3.Distance(a + ab * t, c) < r;
    }
}
