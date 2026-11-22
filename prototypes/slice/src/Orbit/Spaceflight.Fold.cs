using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Stars;
using GalHack.Structures;
using Godot;
using PlanetSurface.Skies;
using Slice.Items;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Orbit;

/// <summary>A fold, as it happened (tests read these): where from and to, how far, what it burnt and what it cost the frame.</summary>
public sealed record FoldMade(int From, int To, double Ly, int Burnt, double WaitedMs, double MoveMs, IReadOnlyList<string> Audit);

/// <summary>
/// The fold drive: a jump from one star to another chosen on the galaxy map (docs/solar-systems.md section 8).
///
/// <para><b>The rules.</b> A powered fold drive block on a Flight structure (any that fits it and can power it, owner
/// 22 Nov), its shield down, a star within its best block's reach (T1 about 30 LY, T2 60), and fold crystal in its core
/// storage for the distance, about one a light year (power.json; the drive itself burns it when its spool ends).</para>
///
/// <para><b>The destination</b> is the target star's system, from its catalogue entry (<see cref="StarProfile.Of"/>, the
/// start system's own recipe), made from the moment the drive spools: the zone round its star
/// (<see cref="StarZone"/>) and its sky from there, on worker threads and then a piece a frame. The jump arrives once it
/// is all there, however long that takes (hidden seams: a slower machine waits longer, never a freeze), in orbit of the
/// target star, clear of it at its high orbit, with its worlds where the one clock has them.</para>
///
/// <para><b>The system left.</b> The start system waits parked as a world's surface waits under its orbit: its zones out
/// of the scene's processing and physics, dark, silent and hidden, exactly as left, so going back costs no reload. Any
/// other system's zones are freed whole, and what was left in them waits in the ledger (<see cref="ZoneLedger"/>) for the
/// next visit, when it is put back where it was.</para>
/// </summary>
public partial class Spaceflight
{
    /// <summary>The fold drive's function (power.json's drives, the blocks' function).</summary>
    public const string FoldDrive = "fold-drive";

    /// <summary>The zone round a star you are in, or null.</summary>
    public StarZone AtStar => Game.Zone as StarZone;

    /// <summary>The galaxy's sky as it stands from where you are: the start world's at home, the star zone's out there.</summary>
    public GalaxySky Stars => AtStar?.Stars ?? Game.Surface?.Sky?.Stars;

    /// <summary>The start's star in the catalogue.</summary>
    public int HomeStar => Game.Surface.Sky.Stars.Home;

    /// <summary>The start system: the start world's own.</summary>
    public StarSystem HomeSystem => Game.Surface.Planet.StarSystem;

    /// <summary>The star whose system you are in.</summary>
    public int StarHere => AtStar?.StarId ?? HomeStar;

    /// <summary>The system you are in.</summary>
    public StarSystem SystemHere => AtStar?.System ?? HomeSystem;

    /// <summary>The start system's zones wait parked while you are out among the stars.</summary>
    public bool HomeParked { get; private set; }

    /// <summary>Every fold so far, in order.</summary>
    public readonly List<FoldMade> Folds = new();

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

    /// <summary>What you fold in: the Flight structure you sit in or ride, or null.</summary>
    public Vessel Folder() => Carrier();

    /// <summary>
    /// Why a structure can't fold to a star now, as the console shows it, or null if it can: NO SHIP, NOT FLIGHT, NOT FITTED,
    /// HERE, FOLDING, then the drive's own (SHIELDS UP, OUT OF RANGE, NO FUEL, NO POWER).
    /// </summary>
    public string FoldFault(Vessel v, int star)
    {
        if (v == null) return "NO SHIP";
        if (v.Structure.Mode != CoreMode.Flight || v.Flight == null) return "NOT FLIGHT";
        if (v.Power.Range(FoldDrive) == null) return "NOT FITTED";
        if (star < 0 || star == StarHere) return "HERE";
        if (_fold != null || v.Power.Spooling != null) return "FOLDING";
        if (v.Power.ShieldUp) return "SHIELDS UP";
        float ly = LightYearsTo(star);
        if (v.Power.JumpReach(FoldDrive) < ly) return "OUT OF RANGE";
        if (v.Power.Range(FoldDrive)?.Reach < ly) return "NO FUEL";
        if (!v.Powered) return "NO POWER";
        return null;
    }

    /// <summary>A fold under way: the ship, the target, how far, and the destination being made.</summary>
    sealed class Underway
    {
        public Vessel Ship;
        public int To;
        public double Ly;
        public StarZone Zone;
        public bool Jumped;
        public int Burnt;
        public long JumpedAt;
        public Action<string, double> Handler;
    }

    Underway _fold;

    /// <summary>A fold is spooling or in its tunnel.</summary>
    public bool Folding => _fold != null;

    /// <summary>The drive has jumped and the destination is still being made: the tunnel.</summary>
    public bool InTunnel => _fold is { Jumped: true };

    /// <summary>
    /// Starts a fold to a star: the drive spools on its power (its fold crystal burns when it jumps), and the destination
    /// starts being made at once. Null if it started, else why not (<see cref="FoldFault"/>).
    /// </summary>
    public string Fold(int star)
    {
        Vessel v = Folder();
        string fault = FoldFault(v, star);
        if (fault != null) return fault;
        float ly = LightYearsTo(star);
        string refused = v.Power.Engage(FoldDrive, ly);
        if (refused != null) return refused;
        var fold = new Underway { Ship = v, To = star, Ly = ly, Zone = MakeStarZone(star, StarHere) };
        fold.Handler = (function, _) =>
        {
            if (function != FoldDrive) return;
            fold.Jumped = true;
            fold.Burnt = v.Power.LastJump?.Burnt ?? 0;
            fold.JumpedAt = Stopwatch.GetTimestamp();
        };
        v.Power.Jumped += fold.Handler;
        _fold = fold;
        GD.Print($"[fold: spooling for {Stars.Galaxy.Name(star)}, {ly:F1} LY]");
        return null;
    }

    /// <summary>The zone round a star, with its sky, made off the main thread from now: it enters the scene parked once its sky is baked.</summary>
    StarZone MakeStarZone(int star, int from)
    {
        StarSystem system = SystemOf(star);
        Galaxy galaxy = Stars.Galaxy;
        // At home its light is the start world's sky's; elsewhere its anchor world's, under this star.
        SkySystem light = star == HomeStar ? Game.Surface.Planet.Sky : SkySystem.For(system.Home.Spec, SkyRig.ClassOf(galaxy.Stars[star].Kind));
        ulong frame = star == HomeStar ? Game.Surface.Spec.Seed : galaxy.SystemSeed(star);
        return new StarZone(Game.Surface.Data, Rules, galaxy, star, system, light, frame, from, DaysHere)
        {
            ProcessMode = ProcessModeEnum.Disabled, Visible = false,
        };
    }

    /// <summary>The one clock where you are, days.</summary>
    public double DaysHere => AtStar?.Days ?? Below.Sky.Days;

    /// <summary>Each tick of a fold: the destination is made a piece at a time; once the drive has jumped and it is all there, you arrive.</summary>
    void FoldTick()
    {
        Underway f = _fold;
        if (f == null) return;
        StarZone to = f.Zone;
        if (!to.IsInsideTree()) Game.AddChild(to);
        to.BuildStep();
        if (f.Ship.Power.Spooling == null && !f.Jumped)
        {
            // The spool wound down (power off): nothing burnt, the destination let go of.
            Abandon("SPOOL LOST");
            return;
        }
        if (!f.Jumped || !to.Ready || Ledger.Committing) return;
        if (_foldCommit)
        {
            _foldCommit = false;
            if (!Ledger.Finish()) { Refused(); Abandon("REFUSED"); return; }
            Arrive(f);
            return;
        }
        string id = f.Ship != null ? IdOf(f.Ship) : ZoneLedger.Sleeper;
        _foldTotals = TotalsKept();
        Ledger.Kept = zone => IsStarZone(zone) && zone != ZoneName(Game.Zone);
        if (Ledger.Begin(Holders(), id, ZoneName(to))) _foldCommit = true;
        else { Refused(); Abandon("REFUSED"); }
    }

    bool _foldCommit;
    (int Items, int Blocks) _foldTotals;

    void Abandon(string why)
    {
        Underway f = _fold;
        _fold = null;
        f.Ship.Power.Jumped -= f.Handler;
        FreeStarZone(f.Zone);
        Game.Say($"FOLD · {why}", Slice.Game.Tones.Warning);
    }

    /// <summary>The jump lands: what crosses moves into the zone round the target star, at its high orbit facing it, and the system left goes.</summary>
    void Arrive(Underway f)
    {
        var watch = Stopwatch.StartNew();
        _fold = null;
        f.Ship.Power.Jumped -= f.Handler;
        double waited = Stopwatch.GetElapsedTime(f.JumpedAt).TotalMilliseconds;
        SurfaceZone from = Game.Zone;
        StarZone to = f.Zone;
        int fromStar = StarHere;
        Vessel v = f.Ship;
        Survivor s = Game.Survivor;
        // At rest at the arrival point, nose to the star.
        Vector3 star = to.StarCentre;
        N.Quaternion facing = Facing(star, Vector3.Up);
        Wake(to);
        Handover.Move(from, to, s, v, N.Vector3.Zero, facing, N.Vector3.Zero);
        Game.EnterZone(to);
        to.Show(true);
        Restore(to);
        Leave(from);
        Game.Camera.Far = Rules.Space.CameraFar;
        Charts.Visited.Add(to.StarId);
        Folds.Add(new FoldMade(fromStar, to.StarId, f.Ly, f.Burnt, waited, watch.Elapsed.TotalMilliseconds, Ledger.Audit(Holders(), _foldTotals, TotalsKept())));
        foreach (string problem in Folds[^1].Audit) GD.PrintErr($"fold audit: {problem}");
        Game.Progress.Discover("fold", 200, "folded to another star");
        GD.Print($"[fold: arrived at {Stars.Galaxy.Name(to.StarId)} in {watch.Elapsed.TotalMilliseconds:F1} ms, {waited:F0} ms after the jump]");
    }

    /// <summary>A rotation with its nose (-Z) toward a point and its top as near <paramref name="up"/> as that allows.</summary>
    static N.Quaternion Facing(Vector3 toward, Vector3 up)
    {
        Basis b = Basis.LookingAt(toward.Normalized(), MathF.Abs(toward.Normalized().Dot(up)) > 0.99f ? Vector3.Back : up);
        Quaternion q = b.GetRotationQuaternion();
        return N.Quaternion.Normalize(new N.Quaternion(q.X, q.Y, q.Z, q.W));
    }

    /// <summary>
    /// The system left behind: the start system parked, exactly as it was (dark, silent, out of the physics, hidden); a
    /// star's zone freed whole, what was left there kept in the ledger.
    /// </summary>
    void Leave(SurfaceZone from)
    {
        if (from is StarZone star)
        {
            FreeStarZone(star);
            return;
        }
        ParkHome();
    }

    /// <summary>Every zone of the start system out of play: as a world's surface waits under its orbit, but its orbit and moons too.</summary>
    void ParkHome()
    {
        if (HomeParked) return;
        HomeParked = true;
        _pending = null;
        if (Orbit != null)
        {
            Orbit.Space.Show(false, 0f);
            Orbit.Space.Light(0f);
            Orbit.Space.ShowBodies(false);
            foreach (Celestial c in Orbit.Bodies)
                if (c.Globe != null) c.Globe.Visible = false;
        }
        ShowGround(false);
        ShowSurface(false);
        foreach (SurfaceZone z in HomeZones())
        {
            Park(z);
            Mute(z);
            Silence(z);
        }
    }

    /// <summary>The start system's zones: its world's surface, its orbit and every moon's surface made for it.</summary>
    IEnumerable<SurfaceZone> HomeZones()
    {
        if (Game.Surface != null) yield return Game.Surface;
        if (Orbit == null) yield break;
        yield return Orbit;
        foreach (Celestial c in Orbit.Moons)
            if (c.Surface != null) yield return c.Surface;
    }

    /// <summary>
    /// Back in the start system's orbit, at a point over its world (the far side of a warp to it, or a test's): its zones
    /// wake as they were, the zone you are in let go of, and space's sky is the orbit's again.
    /// </summary>
    public void EnterHomeOrbit(N.Vector3 at, N.Quaternion rotation, N.Vector3 velocity)
    {
        SurfaceZone from = Game.Zone;
        Vessel v = Carrier();
        StartOrbit();
        Orbit.BuildAll();
        HomeParked = false;
        foreach (SurfaceZone z in HomeZones()) if (z != Orbit) Park(z); // they wait as under any orbit; the seam shows them as you come down
        Wake(Orbit);
        Handover.Move(from, Orbit, Game.Survivor, v, at, rotation, velocity);
        Game.EnterZone(Orbit);
        _aligned = false;
        _band = 0f;
        AllSpace();
        _band = 1f;
        if (from is StarZone star) FreeStarZone(star);
    }

    /// <summary>A star's zone, gone: out of the scene and freed, its sky's textures and far looks let go of now.</summary>
    void FreeStarZone(StarZone z)
    {
        if (z == null || !IsInstanceValid(z)) return;
        Silence(z);
        z.Show(false);
        z.Release();
        if (z.IsInsideTree()) z.GetParent().RemoveChild(z);
        foreach (var dead in _ids.Where(kv => kv.Key is Vessel w && w.Zone == z || kv.Key is Lootable l && z.Loot.All.Contains(l)).Select(kv => kv.Key).ToList()) _ids.Remove(dead);
        z.QueueFree();
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

    // ---- What was left in a star's zone --------------------------------------------------------

    /// <summary>Whether a ledger zone is a star's (not the start system's surface, orbit or moons).</summary>
    public static bool IsStarZone(string zone) => zone != null && zone.StartsWith(ZoneLedger.Star, StringComparison.Ordinal);

    /// <summary>The totals of what is held: in play now, and kept in the ledger for systems out of play.</summary>
    (int Items, int Blocks) TotalsKept()
    {
        var (items, blocks) = ZoneLedger.Totals(Holders());
        var (keptItems, keptBlocks) = Ledger.KeptTotals();
        return (items + keptItems, blocks + keptBlocks);
    }

    /// <summary>
    /// What the ledger kept of a star's zone put back where it was: each structure from its blueprint, at its place and
    /// turn, with what it held; each container with its contents. Each keeps its record's id.
    /// </summary>
    void Restore(StarZone zone)
    {
        string name = ZoneName(zone);
        foreach (var (id, type, kept, items) in Ledger.KeptIn(name).ToList())
        {
            Vector3 at = new(kept.At[0], kept.At[1], kept.At[2]);
            if (type == "structure" && kept.Blueprint != null)
            {
                Structure s = Blueprint.Load(kept.Blueprint, zone.Data.Catalogue, out List<string> errors);
                if (s == null || errors.Count > 0) { GD.PrintErr($"fold: {id} didn't come back: {string.Join("; ", errors)}"); continue; }
                Vessel w = Vessel.Spawn(zone, s, at.ToNumerics(), 0f, landed: false);
                w.Body.Rotation = new N.Quaternion(kept.Turn[0], kept.Turn[1], kept.Turn[2], kept.Turn[3]);
                w.Binding.Teleport();
                w.Storage.AddAll(items);
                _ids[w] = id;
            }
            else if (type == "container")
            {
                Lootable l = zone.Loot.Spill(items, at);
                if (l != null) _ids[l] = id;
            }
        }
    }

    /// <summary>Where a holder in a star's zone is, to be put back there: a structure's blueprint and turn, a container's place.</summary>
    static ZoneLedger.Placing PlacingOf(object thing) => thing switch
    {
        Vessel v => new ZoneLedger.Placing(Blueprint.Save(v.Structure), Floats(v.Body.Position), new[] { v.Body.Rotation.X, v.Body.Rotation.Y, v.Body.Rotation.Z, v.Body.Rotation.W }),
        Lootable l => new ZoneLedger.Placing(null, Floats(l.Position.ToNumerics()), null),
        _ => null,
    };

    static float[] Floats(N.Vector3 p) => new[] { p.X, p.Y, p.Z };
}
