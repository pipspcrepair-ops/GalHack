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
/// slept through the collapse. Theirs is the only pod that worked; the other sleepers are dead.
/// A stairwell, its top end fallen open to the sky, climbs from the chamber's door to the
/// surface. The chamber is a ruin: its locker holds the starting kit, but it is too small and
/// broken to live in, so nothing can be built onto it and it never shows as a core.
///
/// It is a fixed small-grid structure (the slice's own blocks: steel walls, a locker, lights)
/// set into rooms dug out of the terrain through the terrain's edit API before any ground
/// streams in, so the floor, walls and stairs are always solid and the ground around them is
/// whole. The site is searched near the generator's landing site on any seed: dry, above the
/// sea, deep enough for earth over the roof, nothing in the way of the stairs, and the
/// stairwell turned to face the most striking skyline within 300 to 1,200 m (a call to adventure).
/// </summary>
public sealed class Bunker
{
    // ---- The shape, in small-grid cells (0.5 m): x starboard, y up, -z out of the door ----------

    /// <summary>Room: interior x -6..6, y 1..6, z -4..4 (6.5 x 3 x 4.5 m); walls round it; the door in the -z wall.</summary>
    const int RoomX = 7, RoomZ = 5, RoomTop = 7;
    /// <summary>Stairwell interior x -1..1 (1.5 m), walls at ±2; each step 0.5 m up and 1 m deep.</summary>
    const int StairHalf = 2;
    const float Cell = 0.5f;
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
    /// <summary>The locker (a container block on the room's port wall) and where to stand to open it.</summary>
    public Vector3 Locker => At(new Vector3(-(RoomX - 1) * Cell, 1 * Cell, 0f));
    public Vector3 LockerFront => At(new Vector3(-(RoomX - 3) * Cell, 0.25f, 0f));
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

    /// <summary>Whether a point is down in the bunker: its chamber or its stairwell, below the ground's surface.</summary>
    public bool Inside(Vector3 p, SurfaceZone zone)
    {
        N.Vector3 l = MathX.Unrotate(MathX.Yaw(Heading), p.ToNumerics() - Origin);
        bool room = MathF.Abs(l.X) <= RoomX * Cell + 0.25f && MathF.Abs(l.Z) <= RoomZ * Cell + 0.25f && l.Y > -0.5f && l.Y < RoomTop * Cell + 0.25f;
        bool stairs = MathF.Abs(l.X) <= StairHalf * Cell + 0.25f && l.Z < -RoomZ * Cell && l.Z > StepZ(Steps) - 0.5f && l.Y > -0.5f
                      && p.Y < zone.GroundAt(p.X, p.Z).Y - 0.8f;
        return room || stairs;
    }

    /// <summary>The skyline the stairs face, if one was found (a peak or a ridge 300 to 1,200 m off).</summary>
    public Vector3? Landmark { get; private set; }
    public CryoPod Mine { get; private set; }
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
    public readonly record struct Site(N.Vector3 Origin, float Heading, int Steps, Vector3? Landmark, float FromLanding);

    /// <summary>
    /// Searches outward from a point (the generator's landing site) for a place to dig the
    /// bunker: the first ring that has one wins, and on it the stairs that face the skyline best.
    /// </summary>
    public static Site? FindSite(SurfaceZone zone, Vector3 near, int maxRings = 90)
    {
        // Strict first (deep under even ground, well above the sea); then, on low wet worlds
        // (a swamp), shallower under rougher ground.
        return Search(zone, near, maxRings, strict: true) ?? Search(zone, near, maxRings, strict: false);
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
                Vector3? landmark = Skyline(zone, p);
                float toward = landmark is Vector3 l ? MathF.Atan2(-(l.X - p.X), -(l.Z - p.Z)) : 0f;
                // The stairs facing the skyline first, then turned away from it bit by bit.
                foreach (float turn in new[] { 0f, 0.6f, -0.6f, 1.3f, -1.3f, 2.1f, -2.1f, MathF.PI })
                {
                    float heading = toward + turn;
                    if (Fit(zone, p, heading, strict) is not (N.Vector3 origin, int steps)) continue;
                    var site = new Site(origin, heading, steps, landmark, r);
                    if (!Diggable(zone, site)) continue;
                    float score = steps + MathF.Abs(turn) * 6f;
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = site;
                    }
                    break;
                }
            }
            if (best != null) return best;
        }
        return null;
    }

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
            // Out: the ground beyond the top is open, not a wall of earth, and not a drop.
            for (float d = 1f; d <= 4f; d += 1f)
            {
                Vector3 b = At(0f, StepZ(k) - d);
                float beyond = zone.GroundAt(b.X, b.Z).Y;
                // Level with the top step, so you walk out, and back in, without a climb.
                if (MathF.Abs(beyond - tread) > 0.5f || beyond < 1.5f) return null;
            }
            Vector3 c = At(0f, 0f);
            return (new N.Vector3(c.X, floor, c.Z), k);
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
            if (v is Verdict.Bedrock or Verdict.Anchored or Verdict.OutsideWorld) return false;
        }
        return true;
    }

    /// <summary>
    /// The most striking thing on the skyline from a spot, 300 to 1,200 m off: the ground that
    /// rises highest above the eye for its distance. Null on dead-flat ground.
    /// </summary>
    static Vector3? Skyline(SurfaceZone zone, Vector3 p)
    {
        float eye = zone.GroundAt(p.X, p.Z).Y + 1.6f, best = 0.02f;
        Vector3? found = null;
        for (int i = 0; i < 24; i++)
        {
            float a = i * MathF.Tau / 24f;
            for (float d = 300f; d <= 1200f; d += 150f)
            {
                var q = new Vector3(p.X + MathF.Cos(a) * d, 0f, p.Z + MathF.Sin(a) * d);
                float h = zone.GroundAt(q.X, q.Z).Y;
                float rise = (h - eye) / d;
                if (rise > best)
                {
                    best = rise;
                    found = new Vector3(q.X, h, q.Z);
                }
            }
        }
        return found;
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
        b.AppendLine("paint locker base=e85d04 grime=2");
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
        // The locker on the port wall.
        b.AppendLine($"block -{RoomX - 1} 1 0 container paint=locker");
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
        }
        return b.ToString();
    }

    /// <summary>
    /// Digs the bunker's rooms out of the ground at a site and sets its structure into them,
    /// before any ground streams in. Throws if the blueprint is bad.
    /// </summary>
    public static Bunker Dig(SurfaceZone zone, Site site)
    {
        var bunker = new Bunker { Origin = site.Origin, Heading = site.Heading, Steps = site.Steps, Landmark = site.Landmark };
        Structure s = GalHack.Structures.Blueprint.Load(Blueprint(site.Steps, k => Buried(zone, site, k)), zone.Data.Catalogue, out List<string> errors);
        if (errors.Count > 0 || s == null) throw new InvalidOperationException("the bunker's blueprint: " + string.Join("; ", errors));
        s.DirtyChunks.Clear();
        var store = zone.Planet.Terrain.Store;
        var solid = CellsFor(zone, site).Where(c => store.MaterialAt(c) != CellMaterial.Air).ToList();
        if (solid.Count > 0) zone.Edits.DigCells(solid);
        bunker.Ruin = Vessel.SpawnBase(zone, s, site.Origin, site.Heading);
        bunker.Ruin.IsRuin = true;
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
            Box(new N.Vector3(-outer - m, -0.25f - m, zc - half - m), new N.Vector3(outer + m, top + m, zc + half + m));
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
        // Four pods along the back wall; the castaway's is the second from starboard.
        foreach (float x in new[] { -2.25f, -0.75f, PodX, 2.25f })
        {
            var pod = new CryoPod { Name = x == PodX ? "YourCryopod" : "Cryopod", Alive = x == PodX };
            at.AddChild(pod);
            pod.Position = new Vector3(x, 0.25f, 1.8f);
            if (pod.Alive) Mine = pod;
            else Dead.Add(pod);
        }
        // Dim emergency light: red at the door, the working pod's cold glow, a lamp over the locker.
        at.AddChild(new OmniLight3D { Name = "Emergency", Position = new Vector3(0f, 2.6f, -2f), LightColor = new Color(1f, 0.25f, 0.18f), LightEnergy = 1.2f, OmniRange = 7f, ShadowEnabled = true });
        at.AddChild(new OmniLight3D { Name = "Lamp", Position = new Vector3(-2.2f, 2.8f, 0f), LightColor = new Color(1f, 0.9f, 0.75f), LightEnergy = 0.9f, OmniRange = 6f });
        at.AddChild(new OmniLight3D { Name = "Stairs", Position = new Vector3(0f, 2.2f, StepZ(Math.Max(1, Steps / 2))), LightColor = new Color(1f, 0.6f, 0.3f), LightEnergy = 0.7f, OmniRange = 6f });
    }
}

/// <summary>
/// A cryopod: an upright capsule against the bunker's back wall, its glass front a lid that
/// swings open. The castaway's works (cold blue light, a lid that opens with F); the others are
/// dark, frosted, with their sleepers slumped inside.
/// </summary>
public partial class CryoPod : Node3D
{
    public bool Alive;
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
        AddChild(Box(new Vector3(0.95f, 2.2f, 0.06f), new Vector3(0f, 1.1f, 0.42f), shell));
        AddChild(Box(new Vector3(0.06f, 2.2f, 0.9f), new Vector3(-0.47f, 1.1f, 0f), shell));
        AddChild(Box(new Vector3(0.06f, 2.2f, 0.9f), new Vector3(0.47f, 1.1f, 0f), shell));
        AddChild(Box(new Vector3(0.95f, 0.12f, 0.9f), new Vector3(0f, 2.2f, 0f), dark));
        AddChild(Box(new Vector3(0.95f, 0.06f, 0.9f), new Vector3(0f, 0.03f, 0f), dark));
        var body = new StaticBody3D { Name = "Shell", CollisionLayer = Layers.Props, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.06f, 2.2f, 0.9f) }, Position = new Vector3(-0.47f, 1.1f, 0f) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.06f, 2.2f, 0.9f) }, Position = new Vector3(0.47f, 1.1f, 0f) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.95f, 0.12f, 0.9f) }, Position = new Vector3(0f, 2.2f, 0f) });
        _lidCollider = new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(0.9f, 2.1f, 0.06f) }, Position = new Vector3(0f, 1.1f, -0.45f) };
        body.AddChild(_lidCollider);
        AddChild(body);
        // The lid hinges on the port side.
        _lid = new Node3D { Name = "Lid", Position = new Vector3(-0.45f, 0f, -0.45f) };
        _lid.AddChild(Box(new Vector3(0.9f, 2.1f, 0.04f), new Vector3(0.45f, 1.1f, 0f), glass));
        AddChild(_lid);
        if (Alive)
        {
            AddChild(new OmniLight3D { Name = "Glow", Position = new Vector3(0f, 1.9f, -0.1f), LightColor = new Color(0.45f, 0.8f, 1f), LightEnergy = 1.4f, OmniRange = 3.5f });
            AddChild(Box(new Vector3(0.5f, 0.06f, 0.02f), new Vector3(0f, 2.05f, -0.46f), new StandardMaterial3D { AlbedoColor = new Color("48cae4"), EmissionEnabled = true, Emission = new Color("48cae4"), EmissionEnergyMultiplier = 2f }));
        }
        else
        {
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
