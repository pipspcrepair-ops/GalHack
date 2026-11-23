using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Items;
using GalHack.Physics.Engine;
using GalHack.Stars;
using GalHack.Structures;
using Godot;
using PlanetSurface.Skies;
using PlanetSurface.World;
using Slice.Items;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>
/// The fold drive: a jump from one star to another chosen on the galaxy map (docs/solar-systems.md section 8), carried by
/// the one jump sequence the warp drive uses too (<see cref="Jump"/>).
///
/// <para><b>The rules.</b> A powered fold drive block on a Flight structure (any that fits it and can power it, owner
/// 22 Nov), out in space with its shield down, a star within its best block's reach (T1 about 30 LY, T2 60), and fold
/// crystal in its core storage for the distance, about one a light year (power.json; the drive burns it as its spool
/// ends).</para>
///
/// <para><b>The destination</b> is the target star's system, from its catalogue entry by the start system's own recipe
/// (<see cref="StarProfile.Of"/>), and the space round its star (a groundless <see cref="OrbitZone"/>, as a warp to a
/// star has), with the galaxy's sky baked from there: all of it off the main thread from the moment the drive spools,
/// then set up a piece a tick. The tunnel lasts until it is there. It arrives in orbit of the target star, clear of it at
/// its high orbit on the side facing the star it came from, nose to it, the system's worlds where the one clock has them.</para>
///
/// <para><b>The system left.</b> The start system waits parked, as a world's surface waits under its orbit: every zone of
/// it out of processing and physics, dark, silent and hidden, exactly as left. Any other system's zones are freed whole
/// with its sky, and what was left in them waits in the ledger (<see cref="ZoneLedger.Placing"/>) to be put back on the
/// next visit.</para>
/// </summary>
public partial class Spaceflight
{
    /// <summary>The fold drive's function (power.json's drives, the blocks' function).</summary>
    public const string FoldDrive = "fold-drive";

    /// <summary>A system out among the stars (not the start's): its star, model, light, sky and the zones made in it.</summary>
    public sealed class Abroad
    {
        public int Star;
        public StarSystem System;
        public SkySystem Light;
        public SystemShare Share;
        public GalaxySky Stars;
        public BodySurfaces Surfaces;
        public readonly List<OrbitZone> Orbits = new();

        /// <summary>Every zone of it: its orbits and the surfaces in them.</summary>
        public IEnumerable<SurfaceZone> Zones => Orbits.SelectMany(o => o.Bodies.Select(c => c.Surface).Where(s => s != null).Prepend<SurfaceZone>(o));
    }

    /// <summary>The system you are in when it isn't the start's, or null at home.</summary>
    public Abroad Away { get; private set; }

    /// <summary>The system a fold under way is making, till it arrives or is called off.</summary>
    Abroad _coming;

    /// <summary>The galaxy's sky as it stands from where you are: the start world's at home, the star's own out there.</summary>
    public GalaxySky Stars => Away?.Stars ?? Game.Surface?.Sky?.Stars;

    /// <summary>The start's star in the catalogue.</summary>
    public int HomeStar => Game.Surface.Sky.Stars.Home;

    /// <summary>The start system: the start world's own.</summary>
    public StarSystem HomeSystem => Game.Surface.Planet.StarSystem;

    /// <summary>The star whose system you are in.</summary>
    public int StarHere => Away?.Star ?? HomeStar;

    /// <summary>The system you are in.</summary>
    public StarSystem SystemHere => Away?.System ?? HomeSystem;

    /// <summary>What the system you are in gives each body made in it, and the far looks its skies share.</summary>
    SystemShare ShareHere => Away?.Share ?? Game.Surface.Planet.Share();
    BodySurfaces SurfacesHere => Away != null ? Away.Surfaces : Game.Surface.Sky.Surfaces;

    /// <summary>The start system's zones wait parked while you are out among the stars.</summary>
    public bool HomeParked { get; private set; }

    /// <summary>Whether a zone is one of a system out among the stars (freed when that system is left).</summary>
    public bool IsAbroad(SurfaceZone z) => Away != null && Away.Zones.Contains(z);

    readonly Dictionary<int, StarSystem> _systems = new();

    /// <summary>The system of any catalogue star: the start world's at the start's, the start system's recipe elsewhere (kept once made).</summary>
    public StarSystem SystemOf(int star)
    {
        if (star == HomeStar) return HomeSystem;
        if (!_systems.TryGetValue(star, out StarSystem s)) _systems[star] = s = StarProfile.Of(Stars.Galaxy, star);
        return s;
    }

    /// <summary>A star's distance from where you are, LY.</summary>
    public float LightYearsTo(int star) => N.Vector3.Distance(Stars.Galaxy.Stars[star].Position, Stars.Galaxy.Stars[StarHere].Position);

    /// <summary>The fold crystal a jump to a star burns (whole crystals, about one a light year).</summary>
    public int CrystalTo(int star) => Game.Zone.Data.Power.Drives[FoldDrive].Burn(LightYearsTo(star));

    /// <summary>What you fold in: the Flight structure you pilot, or null.</summary>
    public Vessel Folder() => WarpCarrier;

    /// <summary>
    /// Why a structure can't fold to a star now, as the console shows it, or null if it can: SPOOLING or IN FOLD, NOT PILOTING,
    /// NOT FITTED, NOT IN FLIGHT, NOT IN SPACE, HERE, SHIELDS UP, OUT OF RANGE, NO FUEL, NO POWER.
    /// </summary>
    public string FoldFault(Vessel v, int star)
    {
        if (Jumping?.Running == true) return Jumping.Holding ? "IN FOLD" : "SPOOLING";
        if (v == null) return "NOT PILOTING";
        if (v.Power.Range(FoldDrive) is not { } range) return "NOT FITTED";
        if (v.Structure.Mode != CoreMode.Flight) return "NOT IN FLIGHT";
        if (!Game.InOrbit || Band < 1f) return "NOT IN SPACE";
        if (star < 0 || star == StarHere) return "HERE";
        if (v.Power.ShieldUp) return "SHIELDS UP";
        float ly = LightYearsTo(star);
        if (v.Power.JumpReach(FoldDrive) < ly) return "OUT OF RANGE";
        if (range.Reach < ly) return "NO FUEL";
        if (v.Power.Supply <= 0) return "NO POWER";
        return null;
    }

    /// <summary>Starts a fold to a star: the drive spools and the destination starts being made. Null once it spools; else what stops it.</summary>
    public string Fold(int star)
    {
        Vessel v = Folder();
        string fault = FoldFault(v, star);
        if (fault != null) return fault;
        return StartJump(v, FoldDrive, LightYearsTo(star), Rules.Fold.Tunnel, new FoldTo(this, star));
    }

    /// <summary>Every fold arrived so far (tests read these).</summary>
    public IEnumerable<Jump> Folds => Jumps.Where(j => j.Drive == FoldDrive && j.Now == Jump.Stage.Done);

    /// <summary>A fold's destination: the target star's system and the space round its star, made on the spool.</summary>
    sealed class FoldTo : IJumpDestination
    {
        readonly Spaceflight _f;
        readonly int _star, _from;
        Task<StarSystem> _system;
        Abroad _away;
        OrbitZone _zone;
        bool _arrived, _made;

        public FoldTo(Spaceflight flight, int star)
        {
            _f = flight;
            _star = star;
            _from = flight.StarHere;
        }

        public string Name => _f.Stars.Galaxy.Name(_star);

        public void Begin(Spaceflight flight)
        {
            Galaxy galaxy = flight.Stars.Galaxy;
            int star = _star;
            _system = star == flight.HomeStar ? Task.FromResult(flight.HomeSystem)
                : flight._systems.TryGetValue(star, out StarSystem known) ? Task.FromResult(known) : Task.Run(() => StarProfile.Of(galaxy, star));
        }

        public bool Step(Spaceflight flight)
        {
            if (_zone == null)
            {
                if (!_system.IsCompleted) return false;
                if (_system.IsFaulted) throw new InvalidOperationException($"{Name}'s system failed to build", _system.Exception);
                MakeSpace(flight, _system.Result); // a piece a tick: its sky, its far looks' array, its zone
                return false;
            }
            _away?.Stars.Poll();
            _away?.Surfaces?.Pump();
            if (!_zone.IsInsideTree())
            {
                if (!_zone.Prepared) return false;
                flight.Game.AddChild(_zone);
                return false;
            }
            if (!_zone.Ready)
            {
                _zone.BuildStep();
                return false;
            }
            return _away?.Stars.Ready ?? true;
        }

        /// <summary>The space round the star: at home the start system's own (a warp's to the star), out there a new system's.</summary>
        void MakeSpace(Spaceflight flight, StarSystem system)
        {
            flight._systems[_star] = system;
            if (_star == flight.HomeStar)
            {
                _zone = flight.OrbitOf(system.Star);
                if (_zone == null)
                {
                    _made = true;
                    _zone = flight.StarSpace(system.Star, null);
                }
                return;
            }
            Galaxy galaxy = flight.Stars.Galaxy;
            if (_away == null)
            {
                SkySystem light = SkySystem.For(system.Home.Spec, SkyRig.ClassOf(galaxy.Stars[_star].Kind));
                _away = new Abroad
                {
                    Star = _star, System = system, Light = light, Share = new SystemShare(galaxy, _star, galaxy.DepthOf(_star), system, light),
                    Stars = new GalaxySky(galaxy, _star, galaxy.SystemSeed(_star)), // baked on a worker from now
                };
                flight._coming = _away;
                return;
            }
            if (_away.Surfaces == null)
            {
                _away.Surfaces = new BodySurfaces(system.SeenFrom(system.Star), new D3(0, system.Star.Radius * 3.0, 0));
                return;
            }
            _zone = flight.StarSpace(system.Star, _away);
        }

        public JumpArrival Arrival(Spaceflight flight, Vessel carrier)
        {
            OrbitZone o = _zone;
            o.Move();
            Celestial star = o.Home;
            // On the side facing the star it came from: its direction in the catalogue, turned into this system's frame, then this zone's world.
            Galaxy galaxy = flight.Stars.Galaxy;
            N.Vector3 along = N.Vector3.Normalize(galaxy.Stars[_from].Position - galaxy.Stars[_star].Position);
            N.Vector3 inSystem = N.Vector3.Transform(along, N.Quaternion.Conjugate(SkyFrame.Tilt(flight.FrameSeed(_star))));
            N.Vector3 away = N.Vector3.Normalize(N.Vector3.Transform(inSystem, N.Quaternion.Conjugate(WorldToSystem(o))));
            N.Vector3 centre = star.Centre.ToNumerics();
            N.Vector3 at = flight.ClearOf(o, centre, away, flight.HighOrbit(o, star), star);
            N.Quaternion turn = N.Quaternion.Normalize(Between(N.Vector3.Transform(-N.Vector3.UnitZ, carrier.Body.Rotation), N.Vector3.Normalize(centre - at)) * carrier.Body.Rotation);
            o.Recentre(at); // where it arrives is the zone's origin
            return new JumpArrival(o, N.Vector3.Zero, turn);
        }

        public void Left(Spaceflight flight, SurfaceZone from)
        {
            _arrived = true;
            Abroad left = flight.Away;
            flight.Away = _away;
            flight._coming = null;
            if (left != null) flight.FreeAbroad(left); // a star's system out there: freed whole
            if (left == null) flight.ParkHome(); // the start system: parked as it was
            if (_away == null) flight.HomeParked = false; // home's star: the start system's zones in play again (each still parked till you go to it)
            flight.Restore(flight.Away);
            flight.Charts.Visited.Add(_star);
            flight.Game.Progress.Discover("fold", 200, "folded to another star");
        }

        public void Dropped(Spaceflight flight)
        {
            if (_arrived) return;
            flight._coming = null;
            if (_away != null) flight.FreeAbroad(_away);
            else if (_made) flight.Unmake(_zone, null);
        }
    }

    /// <summary>The seed the system's frame stands against the galaxy by (<see cref="SkyFrame.Tilt"/>): the start world's at home, the star's own elsewhere.</summary>
    public ulong FrameSeed(int star) => star == HomeStar ? Game.Surface.Spec.Seed : Stars.Galaxy.SystemSeed(star);

    /// <summary>
    /// The space round a body with no ground (a giant, the star, a belt) of the start system, or of a system out among the
    /// stars (<paramref name="away"/>): made to enter once it is set up, waiting parked till then.
    /// </summary>
    internal OrbitZone StarSpace(Orbiter body, Abroad away)
    {
        var orbit = new OrbitZone(body, away?.System ?? HomeSystem, away?.Light ?? Game.Surface.Planet.Sky, Game.Surface.Data, Rules)
        {
            Name = "Orbit" + (away == null ? "" : $"Star{away.Star}") + Charts.Tidy(body.Name).Replace(" ", ""),
            ProcessMode = ProcessModeEnum.Disabled, Visible = false, Surfaces = away?.Surfaces ?? Game.Surface.Sky.Surfaces,
        };
        orbit.Days = () => Below.Sky.Days;
        if (_orbits.Count == 0) _orbits.Add(Orbit); // the start world's first, before the orbit you fly in is ever another
        if (away != null) away.Orbits.Add(orbit);
        else _orbits.Add(orbit);
        return orbit;
    }

    // ---- The system left ---------------------------------------------------------------------

    /// <summary>Every zone of the start system out of play: as a world's surface waits under its orbit, its orbits and moons too.</summary>
    void ParkHome()
    {
        HomeParked = true;
        // No weather out there: the start world's wind and rain on every material stop (its weather sets them again once you are down).
        foreach (string w in new[] { "weather_wind", "weather_ground", "weather_flash" }) RenderingServer.GlobalShaderParameterSet(w, Vector4.Zero);
        foreach (SurfaceZone z in HomeZones())
        {
            if (z is OrbitZone o)
            {
                o.Space.Show(false, 0f);
                o.Space.Light(0f);
                o.Space.ShowBodies(false);
                foreach (Celestial c in o.Bodies)
                    if (c.Globe != null) c.Globe.Visible = false;
            }
            Park(z);
            Mute(z);
            Silence(z);
        }
    }

    /// <summary>The start system's zones: its world's surface, and every orbit made in it with the surfaces in them.</summary>
    IEnumerable<SurfaceZone> HomeZones()
    {
        yield return Game.Surface;
        foreach (OrbitZone o in Orbits)
        {
            if (Away != null && Away.Orbits.Contains(o)) continue;
            yield return o;
            foreach (Celestial c in o.Bodies)
                if (c.Surface != null && c.Surface != Game.Surface) yield return c.Surface;
        }
    }

    /// <summary>A system out among the stars, let go of whole: its zones out of the scene and freed, its sky's textures and far looks with them.</summary>
    void FreeAbroad(Abroad a)
    {
        foreach (SurfaceZone z in a.Zones.ToList())
        {
            Silence(z);
            if (z is OrbitZone o && o.Space != null) o.Space.Show(false, 0f);
            foreach (object dead in _ids.Keys.Where(k => k is Vessel w && w.Zone == z || k is Lootable l && z.Loot != null && z.Loot.All.Contains(l)).ToList()) _ids.Remove(dead);
            if (z.IsInsideTree()) z.GetParent().RemoveChild(z);
            Free(z);
        }
        a.Orbits.Clear();
        a.Stars.Release();
        a.Surfaces?.Release();
    }

    /// <summary>Every sound under a zone stopped: nothing of a system left plays on.</summary>
    static void Silence(Node zone)
    {
        foreach (Node n in zone.FindChildren("*", "", true, false))
            switch (n)
            {
                case AudioStreamPlayer p: p.Stop(); break;
                case AudioStreamPlayer2D p: p.Stop(); break;
                case AudioStreamPlayer3D p: p.Stop(); break;
            }
    }

    // ---- What was left out among the stars --------------------------------------------------------

    /// <summary>Whether a ledger zone is one of a system out among the stars.</summary>
    public static bool IsStarZone(string zone) => zone != null && zone.StartsWith(ZoneLedger.Star, StringComparison.Ordinal);

    /// <summary>The ledger's zone name prefix for a system out among the stars.</summary>
    static string AbroadPrefix(int star) => ZoneLedger.StarZoneOf(star) + "/";

    /// <summary>The ledger keeps the records of every system out among the stars but the one you are in.</summary>
    bool KeptInLedger(string zone) => IsStarZone(zone) && (Away == null || !zone.StartsWith(AbroadPrefix(Away.Star), StringComparison.Ordinal));

    /// <summary>The totals of what is held: in play now, and kept in the ledger for systems out of play.</summary>
    (int Items, int Blocks) TotalsKept()
    {
        var (items, blocks) = ZoneLedger.Totals(Holders());
        var (keptItems, keptBlocks) = Ledger.KeptTotals();
        return (items + keptItems, blocks + keptBlocks);
    }

    /// <summary>
    /// What the ledger kept of a system's zones put back where it was, in each zone made so far: each structure from its
    /// blueprint at its place and turn with what it held, each container with its contents, each under its record's id.
    /// </summary>
    void Restore(Abroad a)
    {
        if (a == null) return;
        foreach (SurfaceZone zone in a.Zones.ToList())
        foreach (var (id, type, kept, items) in Ledger.KeptIn(ZoneName(zone)).ToList())
        {
            if (_ids.ContainsValue(id)) continue; // there already
            // Its place and turn were kept in its zone's frame body's own (an orbit's world moves when it is entered again).
            N.Vector3 kat = new(kept.At[0], kept.At[1], kept.At[2]);
            Vector3 at = (zone is OrbitZone o ? o.Home.ToWorld(kat) : kat).ToGodot();
            if (type == "structure" && kept.Blueprint != null)
            {
                Structure s = Blueprint.Load(kept.Blueprint, zone.Data.Catalogue, out List<string> errors);
                if (s == null || errors.Count > 0) { GD.PrintErr($"fold: {id} didn't come back: {string.Join("; ", errors)}"); continue; }
                Vessel w = Vessel.Spawn(zone, s, at.ToNumerics(), 0f, landed: false);
                var kturn = new N.Quaternion(kept.Turn[0], kept.Turn[1], kept.Turn[2], kept.Turn[3]);
                w.Body.Rotation = zone is OrbitZone ot ? N.Quaternion.Normalize(ot.Home.Turn * kturn) : kturn;
                w.Binding.Teleport();
                w.Storage.AddAll(items);
                _ids[w] = id;
            }
            else if (type == "container" && zone.Loot.Spill(items, at) is { } l) _ids[l] = id;
        }
    }

    /// <summary>
    /// Where a holder in a system out among the stars is, to be put back there: a structure's blueprint and turn, a
    /// container's place; in an orbit, in its frame body's own frame (the zone's world is laid out afresh each visit).
    /// </summary>
    static ZoneLedger.Placing PlacingOf(object thing, SurfaceZone zone)
    {
        OrbitZone o = zone as OrbitZone;
        N.Vector3 Here(N.Vector3 p) => o != null ? o.Home.ToGlobe(p) : p;
        N.Quaternion Turned(N.Quaternion q) => o != null ? N.Quaternion.Normalize(N.Quaternion.Conjugate(o.Home.Turn) * q) : q;
        return thing switch
        {
            Vessel v when Turned(v.Body.Rotation) is var t => new ZoneLedger.Placing(Blueprint.Save(v.Structure), Floats(Here(v.Body.Position)), new[] { t.X, t.Y, t.Z, t.W }),
            Lootable l => new ZoneLedger.Placing(null, Floats(Here(l.Position.ToNumerics())), null),
            _ => null,
        };
    }

    static float[] Floats(N.Vector3 p) => new[] { p.X, p.Y, p.Z };
}
