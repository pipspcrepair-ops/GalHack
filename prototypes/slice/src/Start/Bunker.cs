using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Player;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using TInt3 = TerrainDigging.Terrain.Int3;
using CellMaterial = TerrainDigging.Terrain.CellMaterial;
using Verdict = TerrainDigging.Terrain.Verdict;

namespace Slice.Start;

/// <summary>
/// Where every game starts: a small cryopod chamber buried at the start site, where the castaway
/// slept through the collapse, a century or two ago, sealed in ever since. Theirs is the only pod
/// that worked; the other sleepers died in theirs.
/// A stairwell, its top end fallen open to the sky, climbs from the chamber's door to the
/// surface. The chamber is a ruin: each pod has its numbered locker on the side walls (the working
/// pod's holds the starting kit; the others stay sealed until their sleepers wake, which on a co-op
/// world a joining player's will), but it is too small and
/// broken to live in as it is, but it is built of ordinary blocks: build onto it, make it part of
/// your first base, or take it apart for materials (each block refunds its bill).
///
/// It is a fixed small-grid structure (the slice's own blocks: steel walls, lights)
/// set into rooms dug out of the terrain through the terrain's edit API before any ground
/// streams in, so the floor, walls and stairs are always solid and the ground around them is
/// whole. The site is searched near the generator's landing site on any seed: dry, above the
/// sea, deep enough for earth over the roof, nothing in the way of the stairs, and the
/// stairwell turned to face a place in view from its top (a ruin, a wreck, an abandoned works) 300 to
/// 1,200 m off: a call to adventure.
/// </summary>
public sealed class Bunker
{
    // ---- The shape, in small-grid cells (0.5 m): x starboard, y up, -z out of the door ----------

    /// <summary>Room: interior x -6..6, y 1..6, z -4..4 (6.5 x 3 x 4.5 m); walls round it; the door in the -z wall.</summary>
    const int RoomX = 7, RoomZ = 5, RoomTop = 7;
    /// <summary>Stairwell interior x -1..1 (1.5 m), walls at ±2; each step 0.5 m up and 1 m deep.</summary>
    const int StairHalf = 2;
    const float Cell = 0.5f;
    /// <summary>How far the top tread stands over a whole metre (the ground dug level outside it lies on one).</summary>
    const float TreadAbove = 0.02f;

    /// <summary>Earth over the roof, at least, m.</summary>
    const float Cover = 1.75f;

    public Vessel Ruin { get; private set; }
    public int Steps { get; private set; }
    public float Heading { get; private set; }
    public N.Vector3 Origin { get; private set; }

    /// <summary>Where the castaway stands in their cryopod (feet), facing out.</summary>
    public Vector3 CryoStand => At(new Vector3(PodX, 0.25f, 1.75f));
    /// <summary>Just outside the pod, in the room.</summary>
    public Vector3 CryoFront => At(new Vector3(PodX, 0.25f, 0.6f));
    public Vector3 RoomCentre => At(new Vector3(0f, 0.25f, 0f));
    /// <summary>The working pod's locker (the middle of its door) and where to stand to open it.</summary>
    public Vector3 Locker => At(MineLocker.Front);
    public Vector3 LockerFront => At(MineLocker.Stand);
    public Vector3 Door => At(new Vector3(0f, 0.25f, -RoomZ * Cell));
    /// <summary>The top step, and the ground a few metres beyond it (outside).</summary>
    public Vector3 StairTop => At(new Vector3(0f, 0.25f + Steps * Cell, StepZ(Steps) - 0.25f));
    public Vector3 Outside => At(new Vector3(0f, 0.25f + Steps * Cell, StepZ(Steps) - 4f));
    /// <summary>The walk from the cryopod to the open air, waypoint by waypoint (and back in, reversed).</summary>
    public IReadOnlyList<Vector3> WayOut
    {
        get
        {
            var w = new List<Vector3> { CryoFront, At(new Vector3(0f, 0.25f, -1.2f)), Door, At(new Vector3(0f, 0.25f, -3.25f)) };
            for (int k = 4; k < Steps; k += 4) w.Add(At(new Vector3(0f, 0.25f + k * Cell, StepZ(k) - 0.25f)));
            w.Add(StairTop);
            w.Add(Outside);
            return w;
        }
    }

    /// <summary>Where a site's stairs come out: on the ground a few metres beyond the top step.</summary>
    public static Vector3 ExitOf(Site site) =>
        (site.Origin + MathX.Rotate(MathX.Yaw(site.Heading), new N.Vector3(0f, 0.25f + site.Steps * Cell, StepZ(site.Steps) - 4f))).ToGodot();

    /// <summary>Whether a point (at any height) is over the bunker's chamber or stairwell, or within a margin of them.</summary>
    public bool Over(Vector3 p, float margin) => OverSite(new Site(Origin, Heading, Steps, Landmark, 0f, Poi), p, margin);

    /// <summary>Whether a point is down in the bunker: its chamber or its stairwell, below the ground's surface.</summary>
    public bool Inside(Vector3 p, SurfaceZone zone)
    {
        N.Vector3 l = MathX.Unrotate(MathX.Yaw(Heading), p.ToNumerics() - Origin);
        bool room = MathF.Abs(l.X) <= RoomX * Cell + 0.25f && MathF.Abs(l.Z) <= RoomZ * Cell + 0.25f && l.Y > -0.5f && l.Y < RoomTop * Cell + 0.25f;
        bool stairs = MathF.Abs(l.X) <= StairHalf * Cell + 0.25f && l.Z < -RoomZ * Cell && l.Z > StepZ(Steps) - 0.5f && l.Y > -0.5f
                      && p.Y < zone.GroundAt(p.X, p.Z).Y - 0.8f;
        return room || stairs;
    }

    /// <summary>The place the stairs face, in view 300 to 1,200 m off, and the point on it they look at.</summary>
    public PlanetSurface.Poi.PoiSite Poi { get; private set; }
    public Vector3? Landmark { get; private set; }
    public CryoPod Mine { get; private set; }

    /// <summary>The four pods by slot, and their lockers by the same slot (a joining player's pod and locker come alive together).</summary>
    public readonly List<CryoPod> Pods = new();
    public readonly List<BunkerLocker> Lockers = new();

    /// <summary>The working pod's locker: unsealed, the starting kit in it.</summary>
    public BunkerLocker MineLocker { get; private set; }

    /// <summary>A sealed locker along a look (bunker-world points), within reach.</summary>
    public BunkerLocker SealedAlong(Vector3 eye, Vector3 dir, float reach)
    {
        foreach (BunkerLocker l in Lockers)
        {
            if (!l.Sealed) continue;
            Vector3 c = At(l.Front);
            float along = (c - eye).Dot(dir);
            if (along < 0f || along > reach || (eye + dir * along - c).Length() > 0.75f) continue;
            return l;
        }
        return null;
    }
    public readonly List<CryoPod> Dead = new();

    const float PodX = 0.75f;

    /// <summary>The z of a step's middle (step 0 is the landing at the door).</summary>
    static float StepZ(int k) => k == 0 ? -(RoomZ + 1) * Cell : -(RoomZ + 2) * Cell - (k - 1) * 2 * Cell - 0.25f;

    /// <summary>A point in the bunker's own frame (metres; x starboard, y up from the floor block's centre, -z out of the door) in the world.</summary>
    public Vector3 At(Vector3 local) => (Origin + MathX.Rotate(MathX.Yaw(Heading), local.ToNumerics())).ToGodot();

    /// <summary>A point on the ground beyond the way out: this far on from it, and to starboard.</summary>
    public Vector3 Ahead(float metres, float side = 0f) => At(new Vector3(side, 0.25f + Steps * Cell, StepZ(Steps) - 4f - metres));

    // ---- Finding a site --------------------------------------------------------------------------

    /// <summary>A site found: where the room's floor centre goes, which way the stairs climb, and how many steps.</summary>
    public readonly record struct Site(N.Vector3 Origin, float Heading, int Steps, Vector3? Landmark, float FromLanding, PlanetSurface.Poi.PoiSite Poi = null);

    /// <summary>
    /// Where a start belongs: the nearest temperate country to a point, with plants and trees
    /// about (a planet has tundra and desert too). The point itself on a world without any.
    /// </summary>
    public static Vector3 TemperateNear(SurfaceZone zone, Vector3 near)
    {
        PlanetSurface.World.PlanetWorld planet = zone.Planet;
        bool Green(float x, float z)
        {
            double wx = Planets.Gen.Mathx.WrapX(x, planet.Width), wz = Planets.Gen.Mathx.Wrap(z, planet.Height);
            return Planets.Look.BiomeLook.FloraOf(planet.Palette.BiomeAt(wx, wz)).Any(r => r.Density >= 0.25f && r.Kind is Planets.Look.Flora.Broadleaf or Planets.Look.Flora.JungleTree);
        }
        for (int ring = 0; ring <= 100; ring++)
        {
            float r = ring * 40f;
            int around = ring == 0 ? 1 : 8 + ring * 2;
            for (int i = 0; i < around; i++)
            {
                float a = i * MathF.Tau / around;
                float x = near.X + MathF.Cos(a) * r, z = near.Z + MathF.Sin(a) * r;
                if (!Green(x, z)) continue;
                int green = 0;
                for (int k = 0; k < 8; k++)
                    if (Green(x + MathF.Cos(k * MathF.Tau / 8f) * 90f, z + MathF.Sin(k * MathF.Tau / 8f) * 90f)) green++;
                if (green >= 6) return new Vector3(x, near.Y, z);
            }
        }
        return near;
    }

    /// <summary>
    /// Searches outward from a point (the generator's landing site) for a place to dig the
    /// bunker: the first ring that has one wins, and on it the stairs that face a place in view best.
    /// </summary>
    public static Site? FindSite(SurfaceZone zone, Vector3 near, int maxRings = 130)
    {
        // Strict first (deep under even ground, well above the sea); then, on low wet worlds
        // (a swamp), shallower under rougher ground.
        _climb = 1.2f;
        Site? site = Search(zone, near, maxRings, strict: true) ?? Search(zone, near, maxRings, strict: false) ?? AroundPlaces(zone, near);
        if (site != null) return site;
        // Rough country all round: walkable ways that climb up to 38 degrees (still well inside
        // what a person walks up).
        _climb = 1.6f;
        site = Search(zone, near, maxRings, strict: false) ?? AroundPlaces(zone, near);
        if (site != null) { _climb = 1.2f; return site; }
        // Knife-edge country: better ways along steep sides than no start at all.
        _alongSides = true;
        site = Search(zone, near, maxRings, strict: false) ?? AroundPlaces(zone, near);
        _alongSides = false;
        _climb = 1.2f;
        return site;
    }

    /// <summary>The steepest rise or fall a walkable way may have over 2 m, m (31 degrees; 38 on rough worlds).</summary>
    [ThreadStatic] static float _climb;

    /// <summary>The last resort: a way may run along a slope too steep to stand on.</summary>
    [ThreadStatic] static bool _alongSides;

    /// <summary>
    /// The last resort, for a world whose places are all out of sight from round the landing
    /// site: round each place itself (nearest the landing site first), 400 to 1,100 m off it, on
    /// any side, wherever a bunker fits with that place in view.
    /// </summary>
    static Site? AroundPlaces(SurfaceZone zone, Vector3 near)
    {
        foreach (var (poi, target) in PoisNear(zone, near, 0f, 12000f))
            for (float r = 350f; r <= 1150f; r += 100f)
            for (int i = 0; i < 36; i++)
            {
                float a = i * MathF.Tau / 36f;
                var p = new Vector3(target.X + MathF.Cos(a) * r, 0f, target.Z + MathF.Sin(a) * r);
                float toward = MathF.Atan2(-(target.X - p.X), -(target.Z - p.Z));
                foreach (float turn in new[] { 0f, 0.6f, -0.6f })
                {
                    if (Fit(zone, p, toward + turn, strict: false) is not (N.Vector3 origin, int steps)) continue;
                    var site = new Site(origin, toward + turn, steps, target, new Vector2(p.X - near.X, p.Z - near.Z).Length(), poi);
                    Vector3 exit = ExitOf(site);
                    float d = new Vector2(target.X - exit.X, target.Z - exit.Z).Length();
                    if (d >= 300f && d <= 1200f && LineOfSight(zone, exit + Vector3.Up * 1.6f, target) && Diggable(zone, site)
                        && WalkableWays(zone, site, out _) >= 3) return site;
                }
            }
        return null;
    }

    static Site? Search(SurfaceZone zone, Vector3 near, int maxRings, bool strict)
    {
        for (int ring = 0; ring < maxRings; ring++)
        {
            float r = ring * 14f;
            int around = ring == 0 ? 1 : 8 + ring * 2;
            Site? best = null;
            float bestScore = float.MaxValue;
            for (int i = 0; i < around; i++)
            {
                float a = i * MathF.Tau / around + ring * 0.37f;
                var p = new Vector3(near.X + MathF.Cos(a) * r, 0f, near.Z + MathF.Sin(a) * r);
                // A call to adventure: a place (no faction holds anything on a start world) in
                // view from the way out, 300 to 1,200 m off. The stairs face it, or turn from it a little.
                foreach (var (poi, target) in PoisNear(zone, p, 260f, 1260f))
                {
                    float toward = MathF.Atan2(-(target.X - p.X), -(target.Z - p.Z));
                    Site? found = null;
                    foreach (float turn in new[] { 0f, 0.6f, -0.6f, 1.3f, -1.3f })
                    {
                        float heading = toward + turn;
                        if (Fit(zone, p, heading, strict) is not (N.Vector3 origin, int steps)) continue;
                        var site = new Site(origin, heading, steps, target, r, poi);
                        Vector3 exit = ExitOf(site), eye = exit + Vector3.Up * 1.6f;
                        float d = new Vector2(target.X - exit.X, target.Z - exit.Z).Length();
                        if (d < 300f || d > 1200f || !LineOfSight(zone, eye, target) || !Diggable(zone, site)) continue;
                        // Somewhere to go from the way out: walkable 60 m in at least three directions.
                        if (WalkableWays(zone, site, out bool towardPlace) < 3) continue;
                        float score = steps + MathF.Abs(turn) * 6f + (towardPlace ? 0f : 8f);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = site;
                        }
                        found = site;
                        break;
                    }
                    if (found != null) break;
                }
            }
            if (best != null) return best;
        }
        return null;
    }

    /// <summary>The ruins, wrecks and abandoned places within a distance band of a point, nearest first, each with the point to look at (most of the way up it).</summary>
    static IEnumerable<(PlanetSurface.Poi.PoiSite poi, Vector3 target)> PoisNear(SurfaceZone zone, Vector3 p, float from, float to)
    {
        var found = new List<(PlanetSurface.Poi.PoiSite, Vector3, float)>();
        foreach (PlanetSurface.Poi.PoiSite site in zone.Planet.Pois)
        {
            if (site.Instance?.State == PoiGenerator.Gen.PoiState.Active) continue; // nobody holds anything on a start world
            var lo = site.Footprint.Min;
            var hi = site.Footprint.Max;
            float tall = MathF.Max(4f, hi.Y - site.Origin.Y);
            var c = new Vector3((lo.X + hi.X) * 0.5f, site.Origin.Y + tall * 0.6f, (lo.Z + hi.Z) * 0.5f);
            c = zone.Planet.NearestCopy(c, p);
            float d = new Vector2(c.X - p.X, c.Z - p.Z).Length();
            if (d >= from && d <= to) found.Add((site, c, d));
        }
        foreach (var (site, c, _) in found.OrderBy(f => f.Item3)) yield return (site, c);
    }

    /// <summary>How far a walk out from a way out must reach in a direction to count, m.</summary>
    public const float WalkOut = 60f;

    /// <summary>
    /// In how many of eight directions a person can walk 60 m out from a site's way out: over
    /// ground that is really there (no cave mouth), no slope or step too steep to walk up and no
    /// drop to fall down (31 degrees over 2 m, along the way and to either side of it: a line
    /// level along a cliff's face is no path), no sea or lava, no water deeper than wading, and
    /// not back over the bunker. Also whether one of them heads within 50 degrees of the place in view.
    /// </summary>
    public static int WalkableWays(SurfaceZone zone, Site site, out bool towardPlace) => WalkableWays(zone, site, out towardPlace, null);

    /// <summary>The directions from the way out a person can walk 60 m (see <see cref="WalkableWays(SurfaceZone, Site, out bool)"/>).</summary>
    public readonly List<Vector3> Ways = new();

    static int WalkableWays(SurfaceZone zone, Site site, out bool towardPlace, List<Vector3> ways)
    {
        towardPlace = false;
        Vector3 exit = ExitOf(site);
        int count = 0;
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8f;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            if (!Walkable(zone, site, exit, dir)) continue;
            count++;
            ways?.Add(dir);
            if (site.Landmark is Vector3 l)
            {
                Vector3 to = l - exit;
                to.Y = 0f;
                if (to.LengthSquared() > 1f && dir.Dot(to.Normalized()) > MathF.Cos(50f * MathF.PI / 180f)) towardPlace = true;
            }
        }
        return count;
    }

    static bool Walkable(SurfaceZone zone, Site site, Vector3 exit, Vector3 dir)
    {
        var store = zone.Planet.Terrain.Store;
        float last = exit.Y, climb = _climb > 0f ? _climb : 1.2f;
        var side = new Vector3(-dir.Z, 0f, dir.X) * 2f;
        for (float d = 2f; d <= WalkOut; d += 2f)
        {
            Vector3 p = exit + dir * d;
            float h = zone.GroundAt(p.X, p.Z).Y;
            if (h < 1.5f || MathF.Abs(h - last) > climb) return false;
            if (!_alongSides && (MathF.Abs(zone.GroundAt(p.X + side.X, p.Z + side.Z).Y - h) > climb || MathF.Abs(zone.GroundAt(p.X - side.X, p.Z - side.Z).Y - h) > climb)) return false;
            if (zone.Planet.Source.WaterSurfaceAt(Planets.Gen.Mathx.WrapX(p.X, zone.Planet.Width), Planets.Gen.Mathx.Wrap(p.Z, zone.Planet.Height), out float surface) && surface - h > 0.5f) return false;
            var under = new TInt3((int)MathF.Floor(p.X), (int)MathF.Floor(h - 0.6f), (int)MathF.Floor(p.Z));
            if (store.MaterialAt(under) == CellMaterial.Air) return false; // the ground isn't really there
            if (OverSite(site, p, 2f)) return false;
            last = h;
        }
        return true;
    }

    /// <summary>Whether a point (at any height) is over a site's chamber or stairwell, or within a margin of them.</summary>
    static bool OverSite(Site site, Vector3 p, float margin)
    {
        N.Vector3 l = MathX.Unrotate(MathX.Yaw(site.Heading), p.ToNumerics() - site.Origin);
        bool room = MathF.Abs(l.X) <= RoomX * Cell + 0.25f + margin && MathF.Abs(l.Z) <= RoomZ * Cell + 0.25f + margin;
        bool stairs = MathF.Abs(l.X) <= StairHalf * Cell + 0.25f + margin && l.Z <= -RoomZ * Cell && l.Z >= StepZ(site.Steps) - 0.5f - margin;
        return room || stairs;
    }

    /// <summary>Whether the ground leaves a clear line from an eye to a point (the last stretch, round the place itself, aside).</summary>
    public static bool LineOfSight(SurfaceZone zone, Vector3 eye, Vector3 target)
    {
        Vector3 d = target - eye;
        float len = d.Length();
        for (float t = 3f; t < len - 20f; t += 5f)
        {
            Vector3 q = eye + d * (t / len);
            if (zone.GroundAt(q.X, q.Z).Y > q.Y - 0.5f) return false;
        }
        return true;
    }

    /// <summary>The place the way out looks at is still in view from it.</summary>
    public bool SeesPoi(SurfaceZone zone) => Landmark is Vector3 l && LineOfSight(zone, Outside + Vector3.Up * 1.6f, l);

    /// <summary>The room's floor height and the steps to the surface for a spot and a heading, if the ground there will do.</summary>
    static (N.Vector3 origin, int steps)? Fit(SurfaceZone zone, Vector3 p, float heading, bool strict)
    {
        float cover = strict ? Cover : 1.2f, aboveSea = strict ? 2f : 0.6f, uneven = strict ? 3f : 4.5f;
        N.Quaternion turn = MathX.Yaw(heading);
        Vector3 At(float x, float z) => p + MathX.Rotate(turn, new N.Vector3(x, 0f, z)).ToGodot();
        // The ground over the room: no lower than the sea allows, not too uneven.
        float lo = float.MaxValue, hi = float.MinValue;
        for (float x = -RoomX * Cell; x <= RoomX * Cell + 0.01f; x += 1f)
        for (float z = -RoomZ * Cell; z <= RoomZ * Cell + 0.01f; z += 1f)
        {
            Vector3 g = At(x, z);
            float h = zone.GroundAt(g.X, g.Z).Y;
            lo = MathF.Min(lo, h);
            hi = MathF.Max(hi, h);
        }
        if (hi - lo > uneven) return null;
        float floor = lo - cover - RoomTop * Cell - 0.25f; // the floor block's centre
        if (floor - 0.25f < aboveSea) return null; // above the sea
        // Steps up until the tread is at the ground's height where it stands.
        for (int k = 1; k <= 30; k++)
        {
            Vector3 g = At(0f, StepZ(k));
            float ground = zone.GroundAt(g.X, g.Z).Y;
            float tread = floor + 0.25f + k * Cell;
            if (ground < 1.5f) return null; // the stairs would come up in water
            if (tread < ground - 0.3f) continue;
            // The top tread a hair over a whole metre: the terrain's cells are whole metres, so
            // the ground dug level outside it meets it flush (a tread between two cell levels
            // left a lip or a slot in front of it). Raised or lowered, whichever still fits.
            foreach (float top in new[] { MathF.Round(tread) + TreadAbove, MathF.Round(tread) + (MathF.Round(tread) + TreadAbove > tread ? -1f : 1f) + TreadAbove })
            {
                float shifted = floor + (top - tread);
                if (shifted - 0.25f < aboveSea || top < ground - 0.3f) continue;
                // Out: the ground beyond the top is open, not a wall of earth, and not a drop.
                bool level = true;
                for (float d = 1f; d <= 4f && level; d += 1f)
                {
                    Vector3 b = At(0f, StepZ(k) - d);
                    float beyond = zone.GroundAt(b.X, b.Z).Y;
                    // Level with the top step, so you walk out, and back in, without a climb.
                    level = MathF.Abs(beyond - top) <= 0.5f && beyond >= 1.5f;
                }
                if (!level) continue;
                Vector3 c = At(0f, 0f);
                return (new N.Vector3(c.X, shifted, c.Z), k);
            }
            return null;
        }
        return null;
    }

    /// <summary>No bedrock, anchor or world's edge anywhere the bunker is dug.</summary>
    static bool Diggable(SurfaceZone zone, Site site)
    {
        GalHack.Terrain.PlanetTerrain terrain = zone.Planet.Terrain;
        foreach (TInt3 c in CellsFor(zone, site))
        {
            Verdict v = terrain.Check(c, TerrainDigging.Terrain.EditKind.Tool);
            if (v is Verdict.Bedrock or Verdict.OutsideWorld) return false;
        }
        return true;
    }


    // ---- Building it -----------------------------------------------------------------------------

    /// <summary>The bunker as blueprint text (the slice's blueprint format), for a number of steps.</summary>
    public static string Blueprint(int steps, Func<int, bool> buried)
    {
        var b = new StringBuilder();
        b.AppendLine("schema galhack.blueprint");
        b.AppendLine("version 1");
        b.AppendLine("name \"Cryo Bunker\"");
        b.AppendLine("grid small");
        b.AppendLine("mode capital");
        b.AppendLine("gravity off");
        b.AppendLine("paint wall base=5c677d grime=3");
        b.AppendLine("paint floor base=33415c grime=3");
        b.AppendLine("paint band base=0a9396 accent=1b263b pattern=stripes grime=3");
        b.AppendLine("paint hazard base=f4a100 accent=2b2d42 pattern=hazard grime=3");

        // Floor, with the dead power core at its centre.
        b.AppendLine($"block -{RoomX}..-1 0 -{RoomZ}..{RoomZ} steel paint=floor");
        b.AppendLine($"block 1..{RoomX} 0 -{RoomZ}..{RoomZ} steel paint=floor");
        b.AppendLine($"block 0 0 -{RoomZ}..-1 steel paint=floor");
        b.AppendLine($"block 0 0 1..{RoomZ} steel paint=floor");
        b.AppendLine("block 0 0 0 core");
        // Walls and roof.
        b.AppendLine($"block -{RoomX} 1..{RoomTop - 1} -{RoomZ - 1}..{RoomZ - 1} steel paint=wall");
        b.AppendLine($"block {RoomX} 1..{RoomTop - 1} -{RoomZ - 1}..{RoomZ - 1} steel paint=wall");
        b.AppendLine($"block -{RoomX}..{RoomX} 1..{RoomTop - 1} {RoomZ} steel paint=band");
        b.AppendLine($"block -{RoomX}..-{StairHalf} 1..{RoomTop - 1} -{RoomZ} steel paint=wall");
        b.AppendLine($"block {StairHalf}..{RoomX} 1..{RoomTop - 1} -{RoomZ} steel paint=wall");
        b.AppendLine($"block -{StairHalf - 1}..{StairHalf - 1} 6 -{RoomZ} steel paint=hazard");
        b.AppendLine($"block -{RoomX}..{RoomX} {RoomTop} -{RoomZ}..{RoomZ} steel paint=wall");

        // The stairwell: a landing at the door, then a step up each metre. Under the open sky its
        // roof has fallen in; below ground it is roofed over.
        for (int k = 0; k <= steps; k++)
        {
            int z0 = k == 0 ? -(RoomZ + 1) : -(RoomZ + 2) - (k - 1) * 2, z1 = k == 0 ? z0 : z0 - 1;
            string zs = z0 == z1 ? $"{z0}" : $"{z1}..{z0}";
            b.AppendLine($"block -{StairHalf}..{StairHalf} 0..{k} {zs} steel paint={(k % 2 == 0 ? "floor" : "wall")}");
            bool roofed = buried(k);
            int wallTop = roofed ? k + 5 : k + 2;
            b.AppendLine($"block -{StairHalf} {k + 1}..{wallTop} {zs} steel paint=wall");
            b.AppendLine($"block {StairHalf} {k + 1}..{wallTop} {zs} steel paint=wall");
            if (roofed) b.AppendLine($"block -{StairHalf}..{StairHalf} {k + 6} {zs} steel paint=wall");
            // Each riser is a short ramp, on the last half metre of the step below: walked up, not
            // stepped over (a half-metre riser is at the edge of what a walker steps up).
            if (k >= 1) b.AppendLine($"block -{StairHalf - 1}..{StairHalf - 1} {k} {z0 + 1} steel slope rot=+y+z paint={(k % 2 == 0 ? "wall" : "floor")}");
        }
        // A ramp off the top step's front edge, sunk into the ground outside: the way out meets
        // the ground on a slope you walk up, never a riser to step over.
        int ramp = -(RoomZ + 2) - (steps - 1) * 2 - 2;
        b.AppendLine($"block -{StairHalf}..{StairHalf} {steps} {ramp} steel slope paint=floor");
        return b.ToString();
    }

    /// <summary>
    /// Digs the bunker's rooms out of the ground at a site and sets its structure into them,
    /// before any ground streams in. Throws if the blueprint is bad.
    /// </summary>
    public static Bunker Dig(SurfaceZone zone, Site site)
    {
        var bunker = new Bunker { Origin = site.Origin, Heading = site.Heading, Steps = site.Steps, Landmark = site.Landmark, Poi = site.Poi };
        Structure s = GalHack.Structures.Blueprint.Load(Blueprint(site.Steps, k => Buried(zone, site, k)), zone.Data.Catalogue, out List<string> errors);
        if (errors.Count > 0 || s == null) throw new InvalidOperationException("the bunker's blueprint: " + string.Join("; ", errors));
        s.DirtyChunks.Clear();
        var store = zone.Planet.Terrain.Store;
        var solid = CellsFor(zone, site).Where(c => store.MaterialAt(c) != CellMaterial.Air).ToList();
        if (solid.Count > 0) zone.Edits.DigCells(solid);
        bunker.Ruin = Vessel.SpawnBase(zone, s, site.Origin, site.Heading);
        // Its ramps are walked on: the stuck guard asks a field that knows them for what they are.
        int field = zone.Physics.StuckFields.IndexOf(bunker.Ruin.Field);
        if (field >= 0) zone.Physics.StuckFields[field] = new RampField(bunker.Ruin.Field);
        bunker.Ruin.IsRuin = true;
        WalkableWays(zone, site, out _, bunker.Ways);
        bunker.Furnish(zone);
        return bunker;
    }

    static float GroundOver(SurfaceZone zone, Site site, int k)
    {
        Vector3 g = (site.Origin + MathX.Rotate(MathX.Yaw(site.Heading), new N.Vector3(0f, 0f, StepZ(k)))).ToGodot();
        return zone.GroundAt(g.X, g.Z).Y;
    }

    /// <summary>A step is roofed while there is earth over its roof; nearer the top its roof has fallen in.</summary>
    static bool Buried(SurfaceZone zone, Site site, int k) => site.Origin.Y + (k + 6) * Cell + 0.25f + 0.6f < GroundOver(zone, site, k) - 0.5f;

    /// <summary>
    /// The terrain cells the bunker takes: the structure's whole volume (walls too) and a margin
    /// round it, so no ground pokes into a room; over the open steps and the way out, everything
    /// up to the sky.
    /// </summary>
    static HashSet<TInt3> CellsFor(SurfaceZone zone, Site site)
    {
        N.Quaternion turn = MathX.Yaw(site.Heading);
        var cells = new HashSet<TInt3>();
        void Box(N.Vector3 lo, N.Vector3 hi)
        {
            float r = (hi - lo).Length() * 0.5f + 1f;
            N.Vector3 mid = site.Origin + MathX.Rotate(turn, (lo + hi) * 0.5f);
            for (int z = (int)MathF.Floor(mid.Z - r); z <= mid.Z + r; z++)
            for (int y = (int)MathF.Floor(mid.Y - r); y <= mid.Y + r; y++)
            for (int x = (int)MathF.Floor(mid.X - r); x <= mid.X + r; x++)
            {
                N.Vector3 local = MathX.Unrotate(turn, new N.Vector3(x + 0.5f, y + 0.5f, z + 0.5f) - site.Origin);
                if (local.X < lo.X || local.Y < lo.Y || local.Z < lo.Z || local.X > hi.X || local.Y > hi.Y || local.Z > hi.Z) continue;
                cells.Add(new TInt3(x, y, z));
            }
        }
        // A hair round the structure only: the walls (half a metre thick) take any ground that
        // rounds into them, and no slot is left beside them to fall into.
        const float m = 0.05f;
        float floorY = site.Origin.Y, outer = StairHalf * Cell + 0.25f;
        Box(new N.Vector3(-RoomX * Cell - 0.25f - m, -0.25f - m, -RoomZ * Cell - 0.25f - m), new N.Vector3(RoomX * Cell + 0.25f + m, RoomTop * Cell + 0.25f + m, RoomZ * Cell + 0.25f + m));
        for (int k = 0; k <= site.Steps; k++)
        {
            float zc = StepZ(k), half = k == 0 ? 0.25f : 0.5f;
            bool roofed = Buried(zone, site, k);
            float top = roofed ? (k + 6) * Cell + 0.25f : (k + 2) * Cell + 0.25f;
            // The top step's front meets the ground outside: no margin there, or the cells just
            // in front of its riser would be dug out to the floor and leave a slot to catch a foot.
            // The top step: dug only above its tread. Under it the ground can stay (the step is solid
            // to the floor), and cells dug there that reach past its riser left a slot in front of it.
            bool last = k == site.Steps;
            float bottom = last ? k * Cell + 0.25f : -0.25f - m;
            Box(new N.Vector3(-outer - m, bottom, zc - half - (last ? 0f : m)), new N.Vector3(outer + m, top + m, zc + half + m));
            // Under the open sky, the way up: clear above the step, the walls' width, to the sky.
            if (!roofed) Box(new N.Vector3(-outer, k * Cell + 0.25f, zc - half), new N.Vector3(outer, GroundOver(zone, site, k) - floorY + 3f, zc + half));
        }
        // The way out: clear above the top step's height for a few metres beyond it.
        float tread = site.Steps * Cell + 0.25f;
        Box(new N.Vector3(-outer, tread, StepZ(site.Steps) - 5f), new N.Vector3(outer, GroundOver(zone, site, site.Steps) - floorY + 3f, StepZ(site.Steps)));
        return cells;
    }

    /// <summary>The cells the dig could not take (bedrock, anchors): the bunker's rooms must be clear of all of them.</summary>
    public int Blocked(SurfaceZone zone)
    {
        var store = zone.Planet.Terrain.Store;
        int blocked = 0;
        foreach (Vector3 p in WayOut.Append(CryoStand).Append(RoomCentre))
            for (float up = 0.5f; up < 1.9f; up += 0.6f)
            {
                var c = new TInt3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y + up), (int)MathF.Floor(p.Z));
                if (store.MaterialAt(c) != CellMaterial.Air) blocked++;
            }
        return blocked;
    }

    // ---- The chamber's insides: cryopods and light --------------------------------------------

    void Furnish(SurfaceZone zone)
    {
        Node3D at = Ruin.Node;
        // Four pods along the back wall, numbered 1 to 4 from port; the castaway's is the third.
        float[] pods = { -2.25f, -0.75f, PodX, 2.25f };
        for (int slot = 0; slot < pods.Length; slot++)
        {
            var pod = new CryoPod { Name = pods[slot] == PodX ? "YourCryopod" : "Cryopod", Alive = pods[slot] == PodX, Slot = slot };
            at.AddChild(pod);
            pod.Position = new Vector3(pods[slot], 0.25f, 1.8f);
            Pods.Add(pod);
            if (pod.Alive) Mine = pod;
            else Dead.Add(pod);
        }
        // Their lockers flank the room, two against each side wall; each is tied to its pod by slot.
        for (int slot = 0; slot < pods.Length; slot++)
        {
            bool port = slot < 2;
            var locker = new BunkerLocker { Name = $"Locker {slot + 1}", Slot = slot, Port = port };
            at.AddChild(locker);
            float side = port ? -1f : 1f, z = slot % 2 == 0 ? -0.75f : 0.25f;
            float wall = RoomX * Cell - 0.25f; // the inner face of the side wall
            locker.Position = new Vector3(side * (wall - BunkerLocker.Depth / 2f - 0.01f), 0.25f + 0.01f, z);
            locker.RotationDegrees = new Vector3(0f, port ? -90f : 90f, 0f);
            float front = wall - BunkerLocker.Depth - 0.04f;
            locker.Front = new Vector3(side * front, 0.25f + 1f, z);
            locker.Stand = new Vector3(side * (front - 1f), 0.25f, z);
            Lockers.Add(locker);
        }
        MineLocker = Lockers[Mine.Slot];
        // The working pod's locker answers; the others stay sealed until their sleepers wake.
        int[] size = zone.Data.Rules.Containers.Corpse;
        MineLocker.Unseal(zone.Loot.Create(Slice.Items.LootKind.Cache, $"Locker {MineLocker.Slot + 1}", At(MineLocker.Front) + Vector3.Down * 0.25f, Math.Max(4, size[0]), Math.Max(3, size[1])));
        // Long sealed: dust lying on the floor in drifts, and motes hanging in the lamplight.
        var dustNoise = new NoiseTexture2D { Width = 256, Height = 256, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.02f, Seed = 11 } };
        var dust = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.52f, 0.47f, 0.40f, 0.55f), AlbedoTexture = dustNoise, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 1f, Uv1Scale = new Vector3(2f, 2f, 1f),
        };
        foreach (var (x, z, w, d) in new[] { (-1.6f, -0.6f, 3.2f, 2.2f), (1.9f, 0.6f, 2.2f, 1.6f), (0f, -2.0f, 1.6f, 0.6f), (-2.6f, 1.6f, 1.4f, 1.0f) })
            at.AddChild(new MeshInstance3D { Name = "Dust", Mesh = new PlaneMesh { Size = new Vector2(w, d) }, Position = new Vector3(x, 0.268f, z), MaterialOverride = dust, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        at.AddChild(new CpuParticles3D
        {
            Name = "Motes", Position = new Vector3(0f, 1.6f, 0f), Amount = 60, Lifetime = 12f, Preprocess = 12f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(3f, 1.3f, 2f),
            Gravity = new Vector3(0f, -0.01f, 0f), InitialVelocityMin = 0.01f, InitialVelocityMax = 0.04f, Spread = 180f,
            Mesh = new QuadMesh { Size = new Vector2(0.012f, 0.012f), Material = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.85f, 0.75f, 0.5f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled } },
        });
        // Dim emergency light: red at the door, the working pod's cold glow, a lamp over the locker.
        at.AddChild(new OmniLight3D { Name = "Emergency", Position = new Vector3(0f, 2.6f, -2f), LightColor = new Color(1f, 0.25f, 0.18f), LightEnergy = 1.2f, OmniRange = 7f, ShadowEnabled = true });
        at.AddChild(new OmniLight3D { Name = "Lamp", Position = new Vector3(-2.2f, 2.8f, 0f), LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.9f, OmniRange = 6f });
        at.AddChild(new OmniLight3D { Name = "Stairs", Position = new Vector3(0f, 2.2f, StepZ(Math.Max(1, Steps / 2))), LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 0.7f, OmniRange = 6f });
    }
}

/// <summary>
/// The bunker's stuck field: the structure's own, except that its ramps (the slopes on the
/// stairs' risers and off the top step) are walked on, not stood inside. A body counts as inside
/// only where a whole block is.
/// </summary>
sealed class RampField : IStuckField
{
    readonly StructureField _inner;

    public RampField(StructureField inner) => _inner = inner;

    public string Name => _inner.Name;
    public Body Owner => _inner.Owner;
    public bool Applies(Body body) => _inner.Applies(body);

    public bool IsSolid(Body body, N.Vector3 point)
    {
        Structure s = _inner.Structure;
        N.Vector3 local = _inner.Body.ToLocal(point);
        return s.TryGet(StructureBridge.CellAt(s, local), out Block b) && s.IsSolid(b) && s.ShapeOf(b).Id == "cube";
    }

    public bool IsInside(Body body, N.Vector3 feet, StuckShape shape)
    {
        float bottom = shape.Radius, top = MathF.Max(shape.Height - shape.Radius, shape.Radius);
        int steps = Math.Max(1, (int)MathF.Ceiling((top - bottom) / 0.25f));
        for (int i = 0; i <= steps; i++)
            if (IsSolid(body, feet + N.Vector3.UnitY * (bottom + (top - bottom) * i / steps))) return true;
        return false;
    }

    public bool Fits(Body body, N.Vector3 feet, StuckShape shape) => _inner.Fits(body, feet, shape);
    public bool Supports(Body body, N.Vector3 feet) => _inner.Supports(body, feet);
    public float TopAt(Body body, float x, float z) => _inner.TopAt(body, x, z);
    public bool TryFindOpen(Body body, N.Vector3 feet, StuckShape shape, out N.Vector3 openFeet) => _inner.TryFindOpen(body, feet, shape, out openFeet);
}

/// <summary>
/// A cryopod: an upright capsule against the bunker's back wall, its glass front a lid that
/// swings open. The castaway's works (cold blue light, a lid that opens with F); the others are
/// dark, frosted, with their sleepers slumped inside.
/// </summary>
public partial class CryoPod : Node3D
{
    public bool Alive;

    /// <summary>Its place in the row (0 to 3): the number on its glass less one, and its locker's slot.</summary>
    public int Slot;
    public bool Open { get; private set; }
    Node3D _lid;
    CollisionShape3D _lidCollider;
    float _swing;

    public override void _Ready()
    {
        var shell = new StandardMaterial3D { AlbedoColor = new Color("8d99ae"), Roughness = 0.5f, Metallic = 0.5f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.7f };
        var glass = new StandardMaterial3D
        {
            AlbedoColor = Alive ? new Color(0.55f, 0.85f, 1f, 0.22f) : new Color(0.8f, 0.85f, 0.9f, 0.7f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = Alive ? 0.05f : 0.8f,
        };
        // Back, sides, roof and base (the pod's local z: +z against the wall, -z the glass front).
        // Every part stands clear of its neighbours' faces: the roof and base lip out past the
        // sides, the back sits between them, nothing shares a plane with the floor or the glass.
        AddChild(Box(new Vector3(0.86f, 2.1f, 0.06f), new Vector3(0f, 1.1f, 0.41f), shell));
        AddChild(Box(new Vector3(0.06f, 2.16f, 0.9f), new Vector3(-0.47f, 1.1f, 0f), shell));
        AddChild(Box(new Vector3(0.06f, 2.16f, 0.9f), new Vector3(0.47f, 1.1f, 0f), shell));
        AddChild(Box(new Vector3(1.04f, 0.12f, 0.98f), new Vector3(0f, 2.2f, 0f), dark));
        AddChild(Box(new Vector3(1.04f, 0.05f, 0.98f), new Vector3(0f, 0.04f, 0f), dark));
        var body = new StaticBody3D { Name = "Shell", CollisionLayer = Layers.Props, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.06f, 2.2f, 0.9f) }, Position = new Vector3(-0.47f, 1.1f, 0f) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.06f, 2.2f, 0.9f) }, Position = new Vector3(0.47f, 1.1f, 0f) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.95f, 0.12f, 0.9f) }, Position = new Vector3(0f, 2.2f, 0f) });
        _lidCollider = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 2.1f, 0.06f) }, Position = new Vector3(0f, 1.1f, -0.45f) };
        body.AddChild(_lidCollider);
        AddChild(body);
        // The lid hinges on the port side.
        _lid = new Node3D { Name = "Lid", Position = new Vector3(-0.45f, 0f, -0.45f) };
        _lid.AddChild(Box(new Vector3(0.86f, 2.04f, 0.03f), new Vector3(0.45f, 1.1f, 0f), glass));
        // Its number, stencilled on the glass and standing just off it.
        _lid.AddChild(new Label3D
        {
            Name = "Number", Text = (Slot + 1).ToString(), Font = DiegeticUi.Theme.UiTheme.Display, FontSize = 96, OutlineSize = 0, PixelSize = 0.0022f,
            Position = new Vector3(0.45f, 1.72f, -0.024f), RotationDegrees = new Vector3(0f, 180f, 0f),
            Modulate = Alive ? new Color("cfefff") : new Color(0.25f, 0.28f, 0.32f), Shaded = false, DoubleSided = false,
        });
        AddChild(_lid);
        if (Alive)
        {
            AddChild(new OmniLight3D { Name = "Glow", Position = new Vector3(0f, 1.9f, -0.1f), LightColor = new Color(0.45f, 0.8f, 1f), LightEnergy = 1.4f, OmniRange = 3.5f });
            AddChild(Box(new Vector3(0.5f, 0.05f, 0.016f), new Vector3(0f, 2.2f, -0.5f), new StandardMaterial3D { AlbedoColor = new Color("48cae4"), EmissionEnabled = true, Emission = new Color("48cae4"), EmissionEnergyMultiplier = 2f }));
        }
        else
        {
            // A failed pod: its status lamp dead red, its glass furred with dust and frost.
            AddChild(Box(new Vector3(0.5f, 0.05f, 0.016f), new Vector3(0f, 2.2f, -0.5f), new StandardMaterial3D { AlbedoColor = new Color("6a040f"), EmissionEnabled = true, Emission = new Color("9d0208"), EmissionEnergyMultiplier = 0.4f }));
            // Its sleeper, slumped against the back (the castaway's own figure, in grey).
            Node3D sleeper = Slice.Player.CharacterModel.Build();
            sleeper.Name = "Sleeper";
            sleeper.Position = new Vector3(0f, 0.06f, 0.1f);
            sleeper.Rotation = new Vector3(0.12f, Mathf.Pi, 0.08f);
            sleeper.Scale = Vector3.One * 0.95f;
            Grey(sleeper);
            AddChild(sleeper);
        }
    }

    /// <summary>Opens the lid (it swings aside over a second); the way out is clear at once.</summary>
    public void OpenLid()
    {
        if (Open) return;
        Open = true;
        _lidCollider.Disabled = true;
    }

    public override void _Process(double delta)
    {
        if (!Open || _swing >= 1f) return;
        _swing = MathF.Min(1f, _swing + (float)delta);
        _lid.Rotation = new Vector3(0f, -1.9f * (1f - MathF.Pow(1f - _swing, 3f)), 0f);
    }

    static MeshInstance3D Box(Vector3 size, Vector3 at, Material m) =>
        new() { Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = m };

    /// <summary>A dead sleeper: every part grey and on the main render layer (the castaway's own figure is hidden in first person).</summary>
    static void Grey(Node n)
    {
        if (n is GeometryInstance3D g)
        {
            g.Layers = 1;
            g.MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.32f, 0.34f, 0.38f), Roughness = 0.9f };
        }
        foreach (Node c in n.GetChildren()) Grey(c);
    }
}

/// <summary>
/// A pod's locker: a tall, narrow steel locker on the chamber's side wall, its door with vents
/// near the top and a handle, its number stencilled on the door and a small lamp saying whether
/// it is sealed (red) or open to you (green). It is tied to its pod by <see cref="Slot"/>: a
/// locker stays sealed until its sleeper wakes, so on a co-op world a joining player's pod and
/// locker come alive together. Local -z faces the room; every part stands clear of the next.
/// </summary>
public partial class BunkerLocker : Node3D
{
    public const float Width = 0.5f, Height = 1.9f, Depth = 0.42f;

    public int Slot;
    public bool Port;
    /// <summary>Where its door's middle is (chamber local), and where to stand to open it.</summary>
    public Vector3 Front, Stand;

    public bool Sealed => Loot == null;
    public Slice.Items.Lootable Loot { get; private set; }
    public int Number => Slot + 1;

    Node3D _hinge;
    MeshInstance3D _lamp;
    float _swing, _target;

    static StandardMaterial3D Lamp(Color c) => new() { AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 1.8f };

    public override void _Ready()
    {
        var steel = new StandardMaterial3D { AlbedoColor = new Color("5f666d"), Roughness = 0.55f, Metallic = 0.6f };
        var door = new StandardMaterial3D { AlbedoColor = new Color("737b83"), Roughness = 0.5f, Metallic = 0.55f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("15181c"), Roughness = 0.8f };
        AddChild(Box(new Vector3(Width, Height, Depth), new Vector3(0f, Height / 2f, 0f), steel));
        const float doorW = Width - 0.04f, doorH = Height - 0.06f, doorT = 0.02f;
        float face = -Depth / 2f - 0.004f - doorT; // the door's front, 4 mm of shadow gap off the body
        _hinge = new Node3D { Name = "Door", Position = new Vector3(-doorW / 2f, 0f, -Depth / 2f - 0.004f) };
        AddChild(_hinge);
        void OnDoor(MeshInstance3D part) => _hinge.AddChild(part);
        OnDoor(Box(new Vector3(doorW, doorH, doorT), new Vector3(doorW / 2f, Height / 2f, -doorT / 2f), door));
        // Vents near the top: dark slats standing a few millimetres proud of the door.
        for (int i = 0; i < 5; i++)
            OnDoor(Box(new Vector3(doorW * 0.56f, 0.012f, 0.006f), new Vector3(doorW / 2f, Height - 0.2f - i * 0.032f, -doorT - 0.005f), dark));
        // The handle, and the small lamp above it.
        OnDoor(Box(new Vector3(0.03f, 0.16f, 0.03f), new Vector3(doorW - 0.07f, Height * 0.5f, -doorT - 0.016f), dark));
        _lamp = Box(new Vector3(0.05f, 0.025f, 0.008f), new Vector3(doorW - 0.07f, Height * 0.5f + 0.13f, -doorT - 0.006f), Lamp(Sealed ? new Color("c1121f") : new Color("38b000")));
        OnDoor(_lamp);
        // Its number, stencilled on the door, standing just off it.
        _hinge.AddChild(new Label3D
        {
            Name = "Number", Text = Number.ToString(), Font = DiegeticUi.Theme.UiTheme.Display, FontSize = 96, OutlineSize = 0, PixelSize = 0.002f,
            Position = new Vector3(doorW / 2f, Height * 0.72f, -doorT - 0.006f), RotationDegrees = new Vector3(0f, 180f, 0f),
            Modulate = new Color(0.9f, 0.88f, 0.8f), Shaded = true, DoubleSided = false,
        });
        var body = new StaticBody3D { Name = "Body", CollisionLayer = Layers.Props, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(Width, Height, Depth + 0.06f) }, Position = new Vector3(0f, Height / 2f, -0.03f) });
        AddChild(body);
    }

    /// <summary>Its sleeper woke: the locker answers now, holding this container.</summary>
    public void Unseal(Slice.Items.Lootable loot)
    {
        Loot = loot;
        if (_lamp != null) _lamp.MaterialOverride = Lamp(new Color("38b000"));
    }

    /// <summary>The door swings open (it stays open once it has been).</summary>
    public void Swing() => _target = 1f;

    public override void _Process(double delta)
    {
        if (_swing >= _target) return;
        _swing = MathF.Min(_target, _swing + (float)delta * 1.6f);
        _hinge.Rotation = new Vector3(0f, -1.7f * (1f - MathF.Pow(1f - _swing, 3f)), 0f);
    }

    static MeshInstance3D Box(Vector3 size, Vector3 at, Material m) =>
        new() { Mesh = new BoxMesh { Size = size }, Position = at, MaterialOverride = m };
}
