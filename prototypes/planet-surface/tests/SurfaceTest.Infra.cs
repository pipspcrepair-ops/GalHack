using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using PlanetSurface.Infra;
using PlanetSurface.Terrain;
using PlanetSurface.World;
using PoiGenerator.Gen;
using Archetype = Planets.Gen.Archetype;
using PoiSite = PlanetSurface.Poi.PoiSite;

namespace PlanetSurface;

/// <summary>The world around the places: the profile, and the old world's infrastructure.</summary>
public partial class SurfaceTest
{
    // ---- the world profile ------------------------------------------------------------------

    async Task ProfileChecks()
    {
        // The same seed rolls the same profile; seeds differ widely.
        int same = 0, inhabited = 0, start = 0, rolls = 0;
        var before = new Dictionary<TechLevel, int>();
        var now = new Dictionary<TechLevel, int>();
        int lower = 0, higher = 0;
        for (ulong seed = 1; seed <= 300; seed++)
        {
            var spec = PlanetSpec.Roll(seed, (Archetype)(seed % 5), (int)(seed % 2));
            WorldProfile p = PlanetWorld.ProfileFor(spec), q = PlanetWorld.ProfileFor(spec), s = PlanetWorld.ProfileFor(spec, startWorld: true);
            rolls++;
            if (p == q) same++;
            if (p.Inhabited) inhabited++;
            if (s.Inhabited && s.Now <= s.Before) start++;
            if (p.Inhabited) before[p.Before] = before.GetValueOrDefault(p.Before) + 1;
            now[p.Now] = now.GetValueOrDefault(p.Now) + 1;
            if (p.Inhabited && p.Now < p.Before) lower++;
            if (p.Inhabited && p.Now > p.Before) higher++;
        }
        Check(same == rolls, $"the same seed rolls the same world profile ({same} of {rolls})");
        Check(start == rolls, $"start worlds were always inhabited, and no faction brings newer tech to them ({start} of {rolls})");
        Check(inhabited > rolls * 0.6 && inhabited < rolls, $"most worlds were inhabited, some never were ({inhabited} of {rolls})");
        Check(before.Count == 3 && now.Count == 3 && before.Values.Min() > inhabited / 6 && lower > inhabited / 4 && higher > rolls / 20,
            $"tech levels spread (before the fall {string.Join(", ", before.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}; now {string.Join(", ", now.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value}"))}); survivors below their ruins on {lower} worlds, newer tech on older ruins on {higher}");
        await Frames(1);
    }

    // ---- the infrastructure -----------------------------------------------------------------

    /// <summary>Not part of the gate (--only=InfraScan): lists seeds' networks, to find views for screenshots.</summary>
    /// <summary>
    /// Nothing floats, on fresh worlds: what stands off a place's footprint sits on the real ground, ruins'
    /// yards too; every ore tell has its rim in the ground; scattered rocks and stems keep their feet in it.
    /// </summary>
    async Task GroundedChecks()
    {
        int blocks = 0, hovering = 0, sunk = 0, tells = 0, tellsOff = 0, flora = 0, floraOff = 0;
        string first = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        var rand = new Random();
        var kinds = new[] { Archetype.Temperate, Archetype.Arid };
        foreach (Archetype kind in kinds)
        {
            ulong seed = (ulong)rand.NextInt64();
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(seed, kind, 0), Main.Catalog);
            float Ground(double x, double z) => w.GroundHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
            void Blocks(PoiInstance inst, double ox, int oy, double oz, string name)
            {
                foreach (var (c, b) in inst.Blocks.Blocks())
                {
                    if (inst.Layout.Occupied[c] || c.Y < -1 || !BlockInfo.Collides(b.Type) && b.Type != BlockType.Plant) continue;
                    // A gantry's members are held by each other (Structure.Floating has those): here, what meets the ground.
                    if (c.Y > 0 && b.Role is not (Role.Debris or Role.Prop)) continue;
                    if (b.Role is Role.Loot or Role.Core or Role.Terminal or Role.Door or Role.Decor or Role.Growth) continue;
                    if (inst.Blocks.Get(c + PoiGenerator.Gen.Int3.Down).Type != BlockType.Air) continue; // on what's below it
                    if (inst.Layout.Occupied[c + PoiGenerator.Gen.Int3.Down]) continue;
                    blocks++;
                    float over = oy + c.Y - Ground(ox + c.X + 0.5, oz + c.Z + 0.5);
                    if (over > 0.1f) { hovering++; Note($"{name}: {b} {over:F2} m over the ground at {c}"); }
                    if (over < -2.2f) sunk++;
                }
            }
            foreach (PoiSite p in w.Pois) Blocks(p.Instance, p.Origin.X, p.Origin.Y, p.Origin.Z, p.Name);
            foreach (PlanetSurface.Infra.FlavourRuin r in (w.Infra?.Ruins ?? new List<PlanetSurface.Infra.FlavourRuin>()).Take(16))
                Blocks(r.Spawn(w.Profile, w.Source), r.OriginX, r.OriginY, r.OriginZ, r.Family.Id);
            // Ore tells: seated on the slope, no rim over the ground.
            var b = new PlanetSurface.Infra.TileBuilder(w, 0, 0);
            foreach (Deposit d in w.Map.Deposits.InRect(0, 0, w.Width, w.Height))
            {
                if (tells >= 800 * (Array.IndexOf(kinds, kind) + 1)) break;
                if (w.Source.StampWeight(d.X, d.Y) > 0f || w.Source.WaterSurfaceAt(d.X, d.Y, out _)) continue;
                float r = PlanetTerrainSource.OreRadius(d);
                var seat = b.Seat(d.X, d.Y, r);
                tells++;
                for (int k = 0; k < 8; k++)
                {
                    var rim = new System.Numerics.Vector3(MathF.Cos(k * MathF.PI / 4f) * r, 0, MathF.Sin(k * MathF.PI / 4f) * r);
                    var at = System.Numerics.Vector3.Transform(rim, seat);
                    double wx = at.X + b.OriginX, wz = at.Z + b.OriginZ;
                    if (at.Y > Ground(wx, wz) + 0.1f) { tellsOff++; Note($"an ore tell's rim {at.Y - Ground(wx, wz):F2} m over the ground at {wx:F0}, {wz:F0}"); break; }
                }
            }
            // Scatter: a far chunk's worth at a time, in rough ground.
            int before = flora;
            for (int k = 0; k < 120 && (k < 24 || flora - before < 400); k++)
            {
                var job = new FarJob { Fx = rand.Next(FarBuilder.Count(w.Terrain.Width) - 1), Fz = rand.Next(FarBuilder.Count(w.Terrain.Height) - 1), Lod = 0 };
                FarBuilder.Build(job, w.Source, w.Palette, null, null);
                foreach (var (kindOf, buffer, count) in job.Flora)
                    for (int i = 0; i < count; i++)
                    {
                        int o = i * 16;
                        double x = job.Fx * (double)FarBuilder.Size + buffer[o + 3], z = job.Fz * (double)FarBuilder.Size + buffer[o + 11];
                        float scale = new Vector3(buffer[o + 1], buffer[o + 5], buffer[o + 9]).Length(), y = buffer[o + 7];
                        float foot = FarBuilder.FootRadius(kindOf) * scale * 0.7f, low = float.MaxValue;
                        bool rock = kindOf is Planets.Look.Flora.Rock or Planets.Look.Flora.LavaRock;
                        // A rock lies along the slope: its middle stays in the ground.
                        if (rock) low = Ground(x, z) + 0.2f;
                        else
                            for (int a = 0; a < 4; a++) low = MathF.Min(low, Ground(x + MathF.Cos(a * MathF.PI / 2f) * foot, z + MathF.Sin(a * MathF.PI / 2f) * foot));
                        flora++;
                        if (y > low) { floraOff++; Note($"{kindOf} {y - low:F2} m over the ground at {x:F0}, {z:F0}"); }
                    }
            }
            await Frames(1);
        }
        Check(hovering == 0 && blocks > 20, $"what stands off a place's footprint sits on the real ground: {blocks} blocks, {hovering} over it, {sunk} buried deep{first}");
        Check(tellsOff == 0 && tells > 500, $"ore tells lie on the slope with no rim over the ground: {tells} tells, {tellsOff} off it{first}");
        Check(floraOff == 0 && flora > 500, $"rocks and stems keep their feet in the ground on a slope: {flora} checked, {floraOff} off it{first}");
    }

    /// <summary>
    /// Ground meets a wall flush: the surface a hand's width off a wall stands where it does a stride off,
    /// on every wall that meets the ground (half-buried ruins' too). A dip along the foot of a wall is
    /// the open ground slumping toward a cell carved out of it.
    /// </summary>
    async Task FlushChecks()
    {
        int walls = 0, dips = 0, banked = 0;
        float worst = 0f;
        string first = "";
        var rand = new Random();
        foreach (Archetype kind in new[] { Archetype.Temperate, Archetype.Arid })
        {
            ulong seed = (ulong)rand.NextInt64();
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(seed, kind, 0), Main.Catalog);
            TerrainDigging.Terrain.ChunkStore store = w.Terrain.Store;
            List<PlanetSurface.Infra.FlavourRuin> ruins = w.Infra?.Ruins?.ToList() ?? new();
            float Top(float x, float z, float from)
            {
                for (float y = from + 4f; y > from - 6f; y -= 0.1f)
                    if (store.SampleDensity(new System.Numerics.Vector3(x, y, z)) > 0f)
                    {
                        // To a couple of centimetres: a slope is judged from the difference of two of these.
                        while (store.SampleDensity(new System.Numerics.Vector3(x, y + 0.02f, z)) > 0f) y += 0.02f;
                        return y;
                    }
                return float.NegativeInfinity;
            }
            void Walls(PoiInstance inst, int ox, int oy, int oz, string name)
            {
                int taken = 0;
                foreach (var (c, b) in inst.Layout.Blocks.Blocks())
                {
                    if (taken >= 60) break;
                    if (!BlockInfo.IsFullCube(b.Type) || !BlockInfo.IsFullCube(inst.Layout.Blocks.Get(c + PoiGenerator.Gen.Int3.Up).Type) || c.Y != 0) continue;
                    foreach (Dir d in Dirs.Horizontal)
                    {
                        var v = d.Vec();
                        PoiGenerator.Gen.Int3 n = c + v;
                        if (inst.Layout.Occupied[n] || !inst.Blocks.Get(n).IsAir || !inst.Blocks.Get(n + PoiGenerator.Gen.Int3.Down).IsAir) continue;
                        if (inst.Layout.Occupied[c + PoiGenerator.Gen.Int3.Up * 2 + v] || Dirs.Horizontal.Any(e => n + e.Vec() != c && inst.Layout.Occupied[n + e.Vec()])) continue; // an inside corner
                        float fx = ox + c.X + 0.5f + v.X * 0.5f, fz = oz + c.Z + 0.5f + v.Z * 0.5f;
                        float near = Top(fx + v.X * 0.3f, fz + v.Z * 0.3f, oy), far = Top(fx + v.X * 1.5f, fz + v.Z * 1.5f, oy);
                        if (float.IsInfinity(near) || float.IsInfinity(far)) continue;
                        walls++;
                        taken++;
                        float dip = far - near;
                        // Street lots stand two metres apart at their own levels: the bank up to the next lot's pad can lean on a wall, and
                        // that is a slope, no slump. A next lot's blend reaches 24 m, so that excuses no wall outright: on a bank the last
                        // of the slope is held to the slope just before it, and ground that steepens into the wall's foot is a slump all the same.
                        double sx = fx + v.X * 1.5f, sz = fz + v.Z * 1.5f;
                        if (ruins.Any(o => o.OriginY != oy && o.Stamp.Weight(sx, sz) > 0.05f))
                        {
                            float mid = Top(fx + v.X * 0.7f, fz + v.Z * 0.7f, oy), back = Top(fx + v.X * 1.1f, fz + v.Z * 1.1f, oy);
                            if (float.IsInfinity(mid) || float.IsInfinity(back)) continue;
                            dip = mid + (mid - back) - near;
                            banked++;
                        }
                        worst = MathF.Max(worst, dip);
                        if (dip > 0.25f)
                        {
                            dips++;
                            if (first.Length == 0) first = $" (first: {name} at {c}, its origin ({ox}, {oy}, {oz}), wall face {d}: {dip:F2} m; seed {seed}, {kind})";
                        }
                        break;
                    }
                }
            }
            foreach (PoiSite p in w.Pois) Walls(p.Instance, p.Origin.X, p.Origin.Y, p.Origin.Z, p.Name);
            foreach (PlanetSurface.Infra.FlavourRuin r in (w.Infra?.Ruins ?? new List<PlanetSurface.Infra.FlavourRuin>()).Take(16))
                Walls(r.Spawn(w.Profile, w.Source), r.OriginX, r.OriginY, r.OriginZ, r.Family.Id);
            await Frames(1);
        }
        Check(dips == 0 && walls > 200, $"ground meets walls flush: {walls} walls ({banked} on a bank up to the next lot, held to its slope), {dips} with a dip along the foot, the worst {worst:F2} m{first}");
    }

    /// <summary>
    /// Nothing underground breaks the surface by accident, on fresh worlds: the top of the ground is solid
    /// over caves, sewers and tunnels everywhere but at a mouth; and mouths stand where the land calls
    /// for them, open to the air, roofed, and leading somewhere you can walk.
    /// </summary>
    async Task CaveChecks()
    {
        int columns = 0, holes = 0, mouths = 0, shut = 0, unroofed = 0, open = 0;
        var kinds = new Dictionary<string, int>();
        string first = "";
        var rand = new Random();
        // Some worlds have few mouths: fresh ones until there are enough to judge.
        Archetype[] sorts = { Archetype.Temperate, Archetype.Arid, Archetype.Ice };
        for (int roll = 0; roll < 12 && (roll < sorts.Length || mouths < 12); roll++)
        {
            Archetype kind = sorts[roll % sorts.Length];
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), kind, 0), Main.Catalog);
            PlanetTerrainSource s = w.Source;
            foreach (CaveMouth m in s.CaveMouths(0, 0, w.Width, w.Height))
            {
                mouths++;
                kinds[m.Kind] = kinds.GetValueOrDefault(m.Kind) + 1;
                var inner = m.Path[^2];
                double ix = m.X + inner.X, iz = m.Z + inner.Z;
                if (s.CellDensity((int)Math.Floor(m.X + m.DirX * 3), (int)Math.Floor(m.Y + 1f), (int)Math.Floor(m.Z + m.DirZ * 3)) > 0f
                    || s.CellDensity((int)Math.Floor(ix), (int)Math.Floor(m.Y + inner.Y), (int)Math.Floor(iz)) > 0f) { shut++; if (first.Length == 0) first = $" (first: a {m.Kind} mouth shut at {m.X:F0}, {m.Z:F0})"; }
                if (s.SurfaceHeight(ix, iz) < m.Y + inner.Y + inner.R + 1f) unroofed++;
            }
            for (int k = 0; k < 6000; k++)
            {
                // A cell's middle: on a cliff the ground a metre away is metres off.
                double x = Math.Floor(rand.NextDouble() * w.Width) + 0.5, z = Math.Floor(rand.NextDouble() * w.Height) + 0.5;
                float h = s.SurfaceHeight(x, z);
                if (h < 3f || s.StampWeight(x, z) > 0f || s.WaterSurfaceAt(x, z, out _)) continue;
                int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z), y = (int)Math.Floor(h - 1.2f);
                columns++;
                if (s.CellDensity(cx, y, cz) > 0f) continue;
                bool atMouth = false;
                foreach (CaveMouth m in s.CaveMouths(x - 70, z - 70, x + 70, z + 70))
                    foreach (var p in m.Path)
                        atMouth |= Math.Abs(Mathx.WrapDelta(x - m.X - p.X, w.Width)) < p.R + 5 && Math.Abs(Mathx.WrapDelta(z - m.Z - p.Z, w.Height)) < p.R + 5;
                if (atMouth) { open++; continue; }
                holes++;
                if (first.Length == 0) first = $" (first: an open hole at {x:F0}, {z:F0}, ground {h:F1})";
            }
            // What runs under a town is covered too, where it isn't meant to see the sky.
            if (w.Infra?.Town?.Under?.Built is { } built)
                foreach (PoiGenerator.Gen.Int3 c in built.Open)
                {
                    if (built.Daylight.Contains(c)) continue;
                    columns++;
                    if (s.SurfaceHeight(Mathx.WrapX(c.X + 0.5, w.Width), Mathx.Wrap(c.Z + 0.5, w.Height)) < c.Y + 1.5f)
                    {
                        holes++;
                        if (first.Length == 0) first = $" (first: the underground breaks the surface at {c})";
                    }
                }
            await Frames(1);
        }
        Check(holes == 0 && columns > 3000, $"nothing underground breaks the surface by accident: {columns} columns, {holes} holes, {open} at mouths{first}");
        Check(shut == 0 && unroofed == 0 && mouths >= 12 && kinds.Count >= 2, $"cave mouths in sensible spots: {mouths} ({string.Join(", ", kinds.Select(k => $"{k.Value} {k.Key}"))}), {shut} shut, {unroofed} without a roof{first}");
    }

    /// <summary>What stands on the land is built as far out as the land's far rings show its pads, corners included.</summary>
    Task ReachChecks()
    {
        float rings = (TerrainStreamer.FarRadius + 1) * FarBuilder.Size * MathF.Sqrt(2f);
        Check(PlanetSurface.Infra.InfraLayer.Radius + PlanetSurface.Infra.InfraLayer.TileSize >= rings, $"buildings load before their pads show: tiles reach {PlanetSurface.Infra.InfraLayer.Radius:F0} m, the far rings {rings:F0} m");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Railways on fresh worlds sweep: no curve tighter than the rule, no line over itself, a gentle grade,
    /// straights the longest part of them, and each runs from a place to a place.
    /// </summary>
    async Task RailChecks()
    {
        int lines = 0, sharp = 0, crossed = 0, steep = 0, unended = 0, worlds = 0;
        var drops = new Dictionary<string, int>();
        double metres = 0, straight = 0, tightest = double.MaxValue;
        string first = "";
        var rand = new Random();
        for (int k = 0; k < 8 && lines < 6; k++)
        {
            var kind = k % 2 == 0 ? Archetype.Temperate : Archetype.Arid;
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), kind, 0), Main.Catalog);
            worlds++;
            if (w.Infra != null) foreach (string why in w.Infra.RailDrops) drops[why] = drops.GetValueOrDefault(why) + 1;
            foreach (InfraRoute r in (w.Infra?.Routes ?? new List<InfraRoute>()).Where(r => r.Kind == RouteKind.Rail))
            {
                lines++;
                if (r.From == null || r.To == null) unended++;
                metres += r.Length;
                for (int i = 10; i + 10 < r.Count; i++)
                {
                    // The circle through three points 40 m apart.
                    double ax = r.X[i - 10], az = r.Z[i - 10], bx = r.X[i], bz = r.Z[i], cx = r.X[i + 10], cz = r.Z[i + 10];
                    double area = Math.Abs((bx - ax) * (cz - az) - (bz - az) * (cx - ax)) * 0.5;
                    double ab = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)), bc = Math.Sqrt((cx - bx) * (cx - bx) + (cz - bz) * (cz - bz)), ca = Math.Sqrt((ax - cx) * (ax - cx) + (az - cz) * (az - cz));
                    double radius = area < 1e-4 ? 1e9 : ab * bc * ca / (4 * area);
                    tightest = Math.Min(tightest, radius);
                    if (radius > 2000) straight += InfraNetwork.Spacing;
                    if (radius < InfraNetwork.RailRadiusLeast * 0.9)
                    {
                        sharp++;
                        if (first.Length == 0) first = $" (first: {radius:F0} m radius at {bx:F0}, {bz:F0})";
                    }
                }
                for (int i = 0; i + 1 < r.Count; i++)
                    if (Math.Abs(r.Y[i + 1] - r.Y[i]) > InfraNetwork.Spacing * 0.042f) steep++;
                var cells = new Dictionary<(int, int), int>();
                for (int i = 0; i < r.Count; i += 2)
                {
                    var key = ((int)Math.Floor(r.X[i] / 8), (int)Math.Floor(r.Z[i] / 8));
                    if (cells.TryGetValue(key, out int seen) && i - seen > 16) { crossed++; break; }
                    cells[key] = i;
                }
            }
            await Frames(1);
        }
        Check(lines > 0 && sharp == 0 && crossed == 0 && steep == 0 && unended == 0 && straight > metres * 0.4,
            $"railways sweep: {lines} lines on {worlds} worlds, {metres / 1000:F1} km, {straight / Math.Max(1, metres):P0} straight, tightest curve {Math.Min(tightest, 1e6):F0} m, {sharp} sharp points, {crossed} over themselves, {steep} steep stretches, {unended} not ending at a place; dropped: {string.Join(", ", drops.Select(d => $"{d.Value} {d.Key}"))}{first}");
    }

    /// <summary>Pylons on fresh worlds: 30 to 50 m tall, striding on their own straight legs rather than along a road, and pylons and poles alike solid.</summary>
    async Task PylonChecks()
    {
        int pylons = 0, poles = 0, wrongHeight = 0, byRoad = 0, turns = 0, stretches = 0, hollow = 0, spanned = 0, legs = 0, floating = 0, feetChecked = 0;
        int standing = 0, onRoad = 0, paved = 0, tight = 0;
        double pavedKm = 0, tightest = double.MaxValue;
        string firstOnRoad = "", firstTight = ""; var tightHow = new Dictionary<string, int>();
        int sharp = 0, sweepWorlds = 0, elbows = 0, elbowRoutes = 0, elbowsAtEnds = 0;
        int tracks = 0, switchbacks = 0, flatBacks = 0, graded = 0;
        double trackKm = 0;
        string firstFlatBack = "";
        var elbowNotes = new List<string>();
        double worstElbow = 0;
        string firstElbow = "";
        double sharpest = 0;
        string firstSharp = "";
        float worst = 0;
        int gridWorlds = 0, powered = 0, dropped = 0, unfed = 0, strungDrops = 0, downDrops = 0, wallsChecked = 0, offWall = 0, dropLow = 0, substations = 0, plantsSeen = 0;
        string firstUnserved = "", firstUnfed = "", firstOffWall = "", firstLow = "";
        var unservedWhy = new Dictionary<string, int>();
        var rand = new Random();
        // Power comes from a world's networks layer, and only some build pylons: fresh worlds until enough have stood (ten at least, for the roads).
        for (int k = 0; k < 16 && (pylons < 60 || k < 10); k++)
        {
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), Archetype.Temperate, 0), Main.Catalog);
            sweepWorlds++;
            // Paved roads bend no tighter than their class allows (tracks may switch back); the first and last
            // stretch, 60 m or the class's least radius, is the swing out of a gate or into a junction.
            // No hard elbow anywhere, town streets and tracks included: over any 8 m a road turns no more than a
            // town corner's curve does (a switchback's hairpin is still a curve of 9 m or more).
            foreach (InfraRoute r in (w.Infra?.Routes ?? new List<InfraRoute>()).Where(r => r.Kind != RouteKind.Rail))
            {
                elbowRoutes++;
                for (int i = 3; i + 4 < r.Count; i++)
                {
                    double a0 = Math.Atan2(r.Z[i] - r.Z[i - 2], r.X[i] - r.X[i - 2]), a1 = Math.Atan2(r.Z[i + 2] - r.Z[i], r.X[i + 2] - r.X[i]);
                    double turn = Math.Abs(Math.IEEERemainder(a1 - a0, Math.PI * 2)) * 180 / Math.PI;
                    worstElbow = Math.Max(worstElbow, turn);
                    if (turn <= 60) continue;
                    elbows++;
                    if (i < 10 || i > r.Count - 11) elbowsAtEnds++;
                    if (elbowNotes.Count < 6) elbowNotes.Add($"{r.Kind}{(r.Laid.Length == 0 ? " street" : "")} {i}/{r.Count} {turn:F0}");
                    if (firstElbow.Length == 0) firstElbow = $" (first: a {r.Kind}{(r.Laid.Length == 0 ? " street" : "")}, point {i} of {r.Count}, at {r.X[i]:F0},{r.Z[i]:F0}, {turn:F0} degrees)";
                    i += 4;
                }
            }
            // A track switches back only on steep ground: anywhere it turns about over 40 m, the land within 30 m climbs.
            float Ground(double x, double z) => w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
            foreach (InfraRoute r in (w.Infra?.Routes ?? new List<InfraRoute>()).Where(r => r.Kind == RouteKind.Track))
            {
                tracks++;
                trackKm += r.Length / 1000;
                // (Not in the first and last 48 m: the way out of a gate and into a junction keep their own line.)
                for (int i = 12; i + 12 < r.Count; i++)
                {
                    double a0 = Math.Atan2(r.Z[i - 3] - r.Z[i - 5], r.X[i - 3] - r.X[i - 5]), a1 = Math.Atan2(r.Z[i + 5] - r.Z[i + 3], r.X[i + 5] - r.X[i + 3]);
                    double turn = Math.Abs(Math.IEEERemainder(a1 - a0, Math.PI * 2)) * 180 / Math.PI;
                    if (turn <= 100) continue;
                    switchbacks++;
                    double steepest = 0;
                    for (int dx = -30; dx <= 30; dx += 10)
                    for (int dz = -30; dz <= 30; dz += 10)
                    {
                        double x = r.X[i] + dx, z = r.Z[i] + dz;
                        float c = Ground(x, z);
                        steepest = Math.Max(steepest, Math.Max(Math.Abs(Ground(x + 6, z) - c), Math.Abs(Ground(x, z + 6) - c)) / 6);
                    }
                    if (steepest < InfraNetwork.TrackHill)
                    {
                        flatBacks++;
                        if (firstFlatBack.Length == 0) firstFlatBack = $" (first: point {i} of {r.Count}, at {r.X[i]:F0},{r.Z[i]:F0}, {turn:F0} degrees over 40 m, steepest ground near {steepest:F3})";
                    }
                    i += 8;
                }
            }
            graded += (w.Infra?.Routes ?? new List<InfraRoute>()).Count(r => r.Graded);
            foreach (InfraRoute r in (w.Infra?.Routes ?? new List<InfraRoute>()).Where(r => r.Kind is RouteKind.Road or RouteKind.Highway))
            {
                paved++;
                pavedKm += r.Length / 1000;
                double least = r.Laid.Length == 0 ? InfraNetwork.TownRadius(r.Kind) : InfraNetwork.LeastRadius(r.Kind);
                int ends = (int)(Math.Max(60, least) / InfraNetwork.Spacing);
                // Outside a town's grid a paved road sweeps: no 50 m of it turns more than its class allows.
                if (r.Laid.Length > 0)
                    for (int i = ends; i + ends + 12 < r.Count; i++)
                    {
                        double a0 = Math.Atan2(r.Z[i + 1] - r.Z[i - 1], r.X[i + 1] - r.X[i - 1]), a1 = Math.Atan2(r.Z[i + 13] - r.Z[i + 11], r.X[i + 13] - r.X[i + 11]);
                        double turn = Math.Abs(Math.IEEERemainder(a1 - a0, Math.PI * 2)) * 180 / Math.PI;
                        sharpest = Math.Max(sharpest, turn);
                        if (turn <= InfraNetwork.MostTurn50(r.Kind)) continue;
                        sharp++;
                        if (firstSharp.Length == 0) firstSharp = $" (first: a {r.Kind}, {r.Laid}, point {i} of {r.Count}, at {r.X[i]:F0},{r.Z[i]:F0}, {turn:F0} degrees)";
                        i += 12;
                    }
                for (int i = ends; i + ends < r.Count; i++)
                {
                    double radius = InfraNetwork.Radius((r.X[i - 5], r.Z[i - 5]), (r.X[i], r.Z[i]), (r.X[i + 5], r.Z[i + 5]));
                    tightest = Math.Min(tightest, radius);
                    if (radius < least * 0.8)
                    {
                        tight++;
                        if (firstTight.Length == 0) firstTight = $" (first: a {r.Kind}, {r.Laid}, point {i} of {r.Count}, at {r.X[i]:F0},{r.Z[i]:F0}, {radius:F0} m)";
                        tightHow[r.Laid] = tightHow.GetValueOrDefault(r.Laid) + 1;
                        i += 10;
                    }
                }
            }
            PowerLines power = w.Infra?.Power;
            if (power == null) continue;
            // The grid's hierarchy: every house that would have had power has its drop, from a pole fed through a
            // substation from a plant; a strung drop meets a wall that stands, and every drop clears or lies on the ground.
            if (power.Poles.Any(p => p.Role == PowerRole.Plant))
            {
                gridWorlds++;
                plantsSeen += power.Poles.Count(p => p.Role == PowerRole.Plant);
                substations += power.Poles.Count(p => p.Role == PowerRole.Substation);
                bool[] fed = power.Fed();
                foreach (var (why, n) in power.UnservedWhy) unservedWhy[why] = unservedWhy.GetValueOrDefault(why) + n;
                var byHouse = power.Drops.ToDictionary(d => d.House);
                foreach (FlavourRuin house in w.Infra.Ruins.Where(PowerLines.Powered))
                {
                    powered++;
                    if (!byHouse.TryGetValue(house, out ServiceDrop d))
                    {
                        if (firstUnserved.Length == 0) firstUnserved = $" (first: a {house.Family.Id} at {house.Center.X:F0},{house.Center.Z:F0})";
                        continue;
                    }
                    dropped++;
                    if (!fed[d.Pole]) { unfed++; if (firstUnfed.Length == 0) firstUnfed = $" (first: a {house.Family.Id} at {house.Center.X:F0},{house.Center.Z:F0}, pole {d.Pole})"; }
                    if (d.State == DropState.Strung) strungDrops++; else downDrops++;
                    List<System.Numerics.Vector3> path = power.DropPath(d, Ground);
                    for (int i = 1; i + 1 < path.Count; i++)
                    {
                        float g = Ground(path[i].X, path[i].Z);
                        if (d.State == DropState.Strung ? path[i].Y < g + 0.5f : path[i].Y < g - 0.05f)
                        {
                            dropLow++;
                            if (firstLow.Length == 0) firstLow = $" (first: a {d.State} drop at {path[i].X:F0},{path[i].Z:F0}, {path[i].Y - g:F2} m off the ground)";
                            break;
                        }
                    }
                    // Where the drop holds at the house, the wall it is fixed to stands in the house as built and decayed.
                    if (d.State is DropState.Strung or DropState.FromHouse && wallsChecked < gridWorlds * 10)
                    {
                        wallsChecked++;
                        var inst = house.Spawn(w.Profile, w.Source);
                        var cell = new PoiGenerator.Gen.Int3(d.Cell.X - house.OriginX, d.Cell.Y - house.OriginY, d.Cell.Z - house.OriginZ);
                        if (inst.Blocks.Get(cell).IsAir)
                        {
                            offWall++;
                            if (firstOffWall.Length == 0) firstOffWall = $" (first: a {house.Family.Id}, {house.State}, cell {cell.X},{cell.Y},{cell.Z})";
                        }
                    }
                }
            }
            // Nothing that stands on the ground stands on a road bed: poles and every pylon foot (wires may cross over).
            foreach (PowerPole p in power.Poles.Where(p => !p.Fallen && p.Role != PowerRole.Plant))
            {
                standing++;
                var feet = p.Pylon ? p.FeetXZ() : new[] { (p.X, p.Z) };
                foreach (var (fx, fz) in feet)
                    if (w.Infra.Nearest(fx, fz, 40, out _, out double d) is { } road && d < road.HalfWidth + 0.5)
                    {
                        onRoad++;
                        if (firstOnRoad.Length == 0) firstOnRoad = $" (first: a {(p.Pylon ? "pylon foot" : "pole")} {d:F1} m from a {road.Kind}'s line, {road.HalfWidth:F1} m half wide)";
                        break;
                    }
            }
            foreach (PowerPole p in power.Poles)
            {
                if (!p.Pylon)
                {
                    poles++;
                    continue;
                }
                pylons++;
                if (p.Tall < 30f || p.Tall > 50f) wrongHeight++;
                if (w.Infra.Nearest(p.X, p.Z, 40, out _, out _) != null) byRoad++;
                // Every foot reaches the ground under it (its lower end a little below, never above or far under).
                foreach (var f in PlanetSurface.Infra.InfraLayer.PylonFeet(p, (x, z) => w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height))))
                {
                    float g = w.Source.SurfaceHeight(Mathx.WrapX(f.X, w.Width), Mathx.Wrap(f.Z, w.Height));
                    feetChecked++;
                    if (f.Y > g + 0.1f || f.Y < g - 2f) { floating++; worst = Math.Max(worst, f.Y - g); }
                }
            }
            foreach (var (first, last) in power.Legs)
            {
                legs++;
                for (int i = first + 1; i < last; i++)
                {
                    PowerPole a = power.Poles[i - 1], b = power.Poles[i], c = power.Poles[i + 1];
                    double ux = b.X - a.X, uz = b.Z - a.Z, vx = c.X - b.X, vz = c.Z - b.Z;
                    double cos = (ux * vx + uz * vz) / Math.Max(1e-6, Math.Sqrt(ux * ux + uz * uz) * Math.Sqrt(vx * vx + vz * vz));
                    stretches++;
                    if (cos < Math.Cos(Mathf.DegToRad(20))) turns++;
                }
            }
            spanned += power.Wires.Count(wire => wire.Spanned && power.Poles[wire.A].Pylon);
            // Solid: each emits collision into its tile.
            var pieces = PlanetSurface.Infra.InfraLayer.Plan(w);
            int tested = 0;
            foreach (var piece in pieces.Values.SelectMany(l => l).Where(p => p.GetType().Name == "PolePiece"))
            {
                if (tested++ >= 40) break;
                var b = new PlanetSurface.Infra.TileBuilder(w, 0, 0);
                piece.Emit(b);
                if (b.Collision.Count == 0) hollow++;
            }
            await Frames(1);
        }
        Check(paved > 0 && tight == 0, $"paved roads sweep: {paved} roads and highways, {pavedKm:F1} km, none tighter than its class's least (road {InfraNetwork.LeastRadius(RouteKind.Road)} m, highway {InfraNetwork.LeastRadius(RouteKind.Highway)} m; tightest {Math.Min(tightest, 1e6):F0} m), {tight} too tight ({string.Join(", ", tightHow.Select(k => $"{k.Value} {(k.Key.Length > 0 ? k.Key : "unlaid")}"))}){firstTight}");
        Check(elbows == 0, $"no road has a hard elbow: {elbowRoutes} roads, streets and tracks on {sweepWorlds} fresh worlds, none turning more than 60 degrees over 8 m (a town corner's curve); sharpest {worstElbow:F0}, {elbows} over ({elbowsAtEnds} within 40 m of an end: {string.Join("; ", elbowNotes)}){firstElbow}");
        Check(tracks > 0 && flatBacks == 0, $"tracks follow the land, switching back only on steep ground: {tracks} tracks, {trackKm:F1} km on {sweepWorlds} fresh worlds, {switchbacks} switchbacks, {flatBacks} on flat ground{firstFlatBack}; {graded} graded dirt roads");
        Check(sharp == 0, $"paved roads turn gently: on {sweepWorlds} fresh worlds no 50 m of road outside a town turns more than {InfraNetwork.MostTurn50(RouteKind.Road):F0} degrees (highway {InfraNetwork.MostTurn50(RouteKind.Highway):F0}); sharpest {sharpest:F0}, {sharp} over{firstSharp}");
        Check(standing > 100 && onRoad == 0, $"poles and pylons stand off the roads: {standing} standing, {onRoad} on a road or railway bed{firstOnRoad}");
        Check(gridWorlds > 0 && powered > 0 && dropped == powered && unfed == 0 && plantsSeen > 0 && substations >= gridWorlds,
            $"every powered house has its drop back to a plant: {gridWorlds} worlds with a grid, {plantsSeen} plants, {substations} substations; {powered} houses that would have had power, {dropped} with a drop ({strungDrops} strung, {downDrops} down){firstUnserved} [{string.Join(", ", unservedWhy.Select(k => $"{k.Value} {k.Key}"))}], {unfed} on a pole not fed through a substation from a plant{firstUnfed}");
        Check(wallsChecked > 0 && offWall == 0 && dropLow == 0,
            $"service drops are anchored: {wallsChecked} fixings checked against the house as spawned, {offWall} not on a standing wall{firstOffWall}; {dropLow} drops through or under the ground{firstLow}");
        Check(feetChecked >= 120 && floating == 0, $"every pylon foot meets the ground: {feetChecked} feet, {floating} floating or buried (worst {worst:F2} m above)");
        Check(pylons >= 30 && wrongHeight == 0 && byRoad < pylons * 0.4 && spanned > 30 && hollow == 0 && turns <= legs + stretches * 0.12,
            $"pylons stride on their own: {pylons} on {legs} lines,{wrongHeight} outside 30 to 50 m, {byRoad} within 40 m of a road, {turns} turns in {stretches} spans (a line may bend once), {spanned} wires strung; {poles} poles; {hollow} without collision");
    }

    /// <summary>Every place people built and every ruin or cluster of them is on a road, on fresh worlds, and the network has its highways and bridges.</summary>
    async Task ConnectedChecks()
    {
        int places = 0, cutOff = 0, ruins = 0, roadless = 0, bridges = 0, highways = 0, sea = 0;
        double km = 0;
        string first = "";
        var rand = new Random();
        for (int k = 0; k < 4; k++)
        {
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll((ulong)rand.NextInt64(), k % 2 == 0 ? Archetype.Temperate : Archetype.Arid, 0), Main.Catalog);
            InfraNetwork net = w.Infra;
            if (net?.Routes == null || net.Routes.Count == 0) continue;
            foreach (PoiSite p in net.Places)
            {
                places++;
                if (net.Isolated.Contains(p)) { sea++; continue; }
                if (!net.Routes.Any(r => r.From == p || r.To == p))
                {
                    cutOff++;
                    if (first.Length == 0) first = $" (first: {p.Name})";
                }
            }
            foreach (PlanetSurface.Infra.FlavourRuin r in net.Ruins)
            {
                ruins++;
                var (x, z) = r.Center;
                if (r.Route != null || r.Drive != null || r.Tracked) continue;
                if (net.Ruins.Any(o => o != r && (o.Route != null || o.Drive != null || o.Tracked) && Dist(w, o.Center.X, o.Center.Z, x, z) < 60)) continue;
                // Over water from anything, or nothing near to reach: the sea keeps some corners of a world.
                if (net.Nearest(x, z, 2400, out _, out _) == null) { sea++; continue; }
                roadless++;
                if (first.Length == 0) first = $" (first: {r.Family.Id} at {x:F0}, {z:F0})";
            }
            bridges += net.Structures.Count(s => s.Kind == RoadSpan.Bridge);
            highways += net.Routes.Count(r => r.Kind == RouteKind.Highway);
            km += net.Routes.Where(r => r.Kind != RouteKind.Rail).Sum(r => r.Length) / 1000;
            await Frames(1);
        }
        Check(cutOff == 0 && roadless == 0 && places > 8 && ruins > 8, $"every place and ruin is on a road: {places} places ({cutOff} cut off), {ruins} ruins ({roadless} without), {sea} over the sea; {km:F0} km of road, {highways} highways, {bridges} bridges{first}");
    }

    /// <summary>
    /// The deco audit: a car is a car against a 1.8 m person, a century keeps the main part of everything
    /// but what really rots away (a mattress, hay, a rug), and a hulk still stands on its tyres. Every
    /// shape's every triangle faces out, so no end of a wheel or a drum is open.
    /// </summary>
    Task DecoChecks()
    {
        var forge = PlanetSurface.Infra.InfraDeco.Poi.Forge;
        int wrongSize = 0, lost = 0, bald = 0, hulks = 0, inward = 0, parts = 0;
        string first = "";
        void Note(string s) { if (first.Length == 0) first = $" (first: {s})"; }
        foreach (string id in new[] { "hatchback", "sedan", "pickup", "van" })
        {
            var size = forge.Mesh(id).Size;
            if (size.X is < 1.5f or > 2.1f || size.Y is < 1.2f or > 2.6f || size.Z is < 3.4f or > 5.6f) { wrongSize++; Note($"{id} is {size}"); }
        }
        string[] rots = { "floor-rug", "floor-mattress", "hay-bales", "feed-sacks", "parasol", "pallet-stack", "office-partition", "privacy-curtain", "farm-trailer" };
        foreach (var piece in forge.Catalogue.Pieces)
        {
            var fresh = forge.ShapeOf(piece.Id, GalHack.Items.DecoState.Intact, GalHack.Items.DecoFall.None);
            var aged = forge.ShapeOf(piece.Id, GalHack.Items.DecoState.Intact, GalHack.Items.DecoFall.None, false, GalHack.Deco.DecoAge.OldWorld, true);
            float was = fresh.Model.Parts.Sum(x => x.Volume), now = aged.Model.Parts.Sum(x => x.Volume);
            if (now < was * 0.14f && !rots.Contains(piece.Id)) { lost++; Note($"{piece.Id} keeps {now / was:P0}"); }
            if (fresh.Model.Parts.Any(x => x.Is(GalHack.Deco.PartFlags.Tyre)))
            {
                hulks++;
                // On its rims: round, and wide enough to look like wheels.
                if (!aged.Model.Parts.Any(x => x.Kind == GalHack.Deco.DecoPrim.Cylinder && x.Segments >= 12)) { bald++; Note($"{piece.Id} has no wheels left"); }
            }
            foreach (GalHack.Deco.DecoPart part in fresh.Model.Parts)
            {
                parts++;
                var mesh = new GalHack.Deco.DecoMeshData();
                GalHack.Deco.DecoEmit.Part(mesh, part, (_, _, _) => new GalHack.Deco.DecoRgb(1, 1, 1), _ => GalHack.Deco.DecoSurface.Matte);
                System.Numerics.Vector3 centre = System.Numerics.Vector3.Zero;
                int corners = 0;
                foreach (var (a, b, c) in mesh.AllTriangles()) { centre += a + b + c; corners += 3; }
                centre /= Math.Max(1, corners);
                foreach (var (a, b, c) in mesh.AllTriangles())
                    if (System.Numerics.Vector3.Cross(b - a, c - a).Length() > 1e-5f && System.Numerics.Vector3.Dot(System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(b - a, c - a)), (a + b + c) / 3f - centre) < -1e-3f) { inward++; Note($"{piece.Id}: a {part.Kind} faces in"); break; }
            }
        }
        Check(wrongSize == 0 && lost == 0 && bald == 0 && inward == 0 && hulks > 8 && parts > 1000, $"deco audit: cars at real size, {wrongSize} wrong; {lost} pieces lost to the weather; {hulks} wheeled pieces, {bald} without wheels; {parts} parts, {inward} open or facing in{first}");
        return Task.CompletedTask;
    }

    async Task InfraScan()
    {
        foreach (int a in new[] { 0, 1 })
        for (ulong seed = 61; seed < 69; seed++)
        {
            var spec = PlanetSpec.Roll(seed, (Archetype)a, 0);
            WorldProfile p = PlanetWorld.ProfileFor(spec);
            if (!p.Inhabited || p.Before == TechLevel.Low) { GD.Print($"INFO  {(Archetype)a} {seed}: {p}"); continue; }
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            InfraNetwork net = w.Infra;
            GD.Print($"INFO  {(Archetype)a} {seed}: {p}; {string.Join(", ", net.Routes.GroupBy(r => r.Kind).Select(g => $"{g.Count()} {g.Key} {g.Sum(r => r.Length) / 1000:F1} km"))}; " +
                     $"{net.Structures.Count(s => s.Kind == RoadSpan.Bridge)} bridges, {net.Structures.Count(s => s.Kind == RoadSpan.Tunnel)} tunnels; places {string.Join(" ", w.Pois.Select(x => x.Family.Id))}");
            await Frames(1);
        }
    }

    async Task InfraChecks()
    {
        int worlds = 0, places = 0, joined = 0, routes = 0, unrouted = 0, separated = 0, never = 0, neverRoutes = 0;
        int stretches = 0, withinGrade = 0, unwalkable = 0, bridges = 0, longViaducts = 0, lowOverWater = 0, tunnels = 0, longTunnels = 0;
        int deckSamples = 0, offDeck = 0, seamSamples = 0, seamMismatch = 0, tilted = 0;
        string firstGround = "", firstBlocked = "";
        int wreckSites = 0, wreckPieces = 0, blockedLanes = 0, wreckOnPad = 0;
        var wreckKinds = new Dictionary<string, int>();
        var stopKinds = new Dictionary<string, int>();
        double ruinRoadKm = 0;
        int ruinsUnjoined = 0, fuelOnLow = 0, houses = 0, housesInStreets = 0;
        int airless = 0, openWorlds = 0, ruins = 0, ruinWorlds = 0, ruinOnRoad = 0, ruinOnPad = 0, ruinsTouching = 0, ruinsYielding = 0, ruinsSpawned = 0, fewRuins = 0, blastedRuins = 0;
        var ruinKinds = new Dictionary<string, int>();
        int tells = 0, glowingTells = 0, tellsChecked = 0, planted = 0, strangers = 0, unrooted = 0, clipped = 0, unplaced = 0;
        int poles = 0, fallen = 0, spans = 0, hanging = 0, unanchored = 0, throughGround = 0, offRoute = 0, onBed = 0, lowWorldLines = 0;
        double planBest = double.MaxValue, planWorst = 0;
        bool same = true;
        var notes = new List<string>();
        var kinds = new Dictionary<RouteKind, double>();
        for (int a = 0; a < 6; a++)
        {
            // Five archetypes, and a modern temperate world (it has highways).
            var spec = a < 5 ? PlanetSpec.Roll((ulong)(900 + a * 11), (Archetype)a, 0) : PlanetSpec.Roll(61, Archetype.Temperate, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            InfraNetwork net = w.Infra;
            worlds++;
            planBest = Math.Min(planBest, net.PlanMs);
            planWorst = Math.Max(planWorst, net.PlanMs);
            if (!w.Profile.Inhabited)
            {
                never++;
                neverRoutes += net.Routes.Count;
                continue;
            }
            routes += net.Routes.Count;
            unrouted += net.Unrouted;
            separated += net.SeaSeparated;
            foreach (InfraRoute r in net.Routes) kinds[r.Kind] = kinds.GetValueOrDefault(r.Kind) + r.Length / 1000;

            // Every place the network serves is reached at one of its gates: a route ends on the gate's cell.
            bool Near(double x, double z, PoiSite p) => p.Gates.Any(g =>
                Math.Abs(Mathx.WrapDelta(g.Cell.X + 0.5 - x, w.Width)) < 1.0 && Math.Abs(Mathx.WrapDelta(g.Cell.Z + 0.5 - z, w.Height)) < 1.0);
            foreach (PoiSite p in net.Places)
            {
                if (net.Isolated.Contains(p)) continue;   // the sea cuts it off: no route was asked of a world that never bridged it
                places++;
                bool at = net.Routes.Any(r => (r.From == p && Near(r.X[0], r.Z[0], p)) || (r.To == p && Near(r.X[^1], r.Z[^1], p)));
                if (at) joined++;
                else if (notes.Count < 4) notes.Add($"{p.Name} on {spec.Archetype} has no route at a gate");
            }

            foreach (InfraRoute r in net.Routes)
            {
                // Grades: within the kind's limit, except a frontier road following the land where it could
                // not span a valley, and short ramps to a gate or junction; walkable everywhere.
                for (int i = 0; i + 1 < r.Count; i++)
                {
                    if (r.Span[i] != RoadSpan.Ground) continue;
                    float g = Math.Abs(r.Y[i + 1] - r.Y[i]) / InfraNetwork.Spacing;
                    stretches++;
                    if (g <= InfraNetwork.MaxGrade(r.Kind) * 1.05f) withinGrade++;
                    if (g > 0.6f) { unwalkable++; if (notes.Count < 8) notes.Add($"{r.Kind} {r.Id} on {spec.Archetype} climbs {g:F2} at point {i}"); }
                }
                // The deck is what you walk on, level across: on the centre line and near both edges, away
                // from junctions (another route's deck may cross there), places' pads and the ends.
                for (int i = 8; i + 8 < r.Count; i += 9)
                {
                    if (r.Span[i - 1] != RoadSpan.Ground || r.Span[i] != RoadSpan.Ground) continue;
                    if (net.Routes.Any(o => o != r && o.Count > 1 && Math.Min(Dist(w, o.X[0], o.Z[0], r.X[i], r.Z[i]), Dist(w, o.X[^1], o.Z[^1], r.X[i], r.Z[i])) < 30)) continue;
                    if (net.Routes.Any(o => o != r && o.Kind != r.Kind && Crosses(w, o, r.X[i], r.Z[i], o.HalfWidth + r.HalfWidth + 30))) continue;
                    if (w.Pois.Any(p => p.Stamps.Any(t => t.Weight(Mathx.WrapX(r.X[i], w.Width), Mathx.Wrap(r.Z[i], w.Height)) > 0f))) continue;
                    double dx = r.X[i + 1] - r.X[i - 1], dz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
                    double sx = -dz / len, sz = dx / len;
                    float centre = w.Source.SurfaceHeight(Mathx.WrapX(r.X[i], w.Width), Mathx.Wrap(r.Z[i], w.Height));
                    deckSamples++;
                    if (Math.Abs(centre - r.Y[i]) > 0.35f + (r.Kept ? 0 : 0.25f))
                    {
                        offDeck++;
                        if (offDeck <= 6)
                        {
                            double px = Mathx.WrapX(r.X[i], w.Width), pz = Mathx.Wrap(r.Z[i], w.Height);
                            var near = w.Source.StampsAt(px, pz).Select(k => w.Source.Stamps[k]).Where(t => t.Weight(px, pz) > 0 || t.Shape == StampShape.Road && t.NearestOnPath(px, pz, out float d, out _, out _) && d < 40)
                                .Select(t => $"{t.Name}/{t.Shape}{(t.Shape == StampShape.Road && t.NearestOnPath(px, pz, out float d2, out float y2, out RoadSpan sp2) ? $" d{d2:F1} y{y2:F1} {sp2}" : "")}");
                            GD.Print($"INFO    off deck: {r.Kind} {r.Id} point {i} at {px:F0},{pz:F0}: ground {centre:F2}, deck {r.Y[i]:F2}, natural {w.Source.NaturalHeight(px, pz):F2}; stamps here: {string.Join("; ", near)}");
                        }
                    }
                    foreach (double side in new[] { -0.8, 0.8 })
                    {
                        double ex = r.X[i] + sx * r.HalfWidth * side, ez = r.Z[i] + sz * r.HalfWidth * side;
                        double qx = Mathx.WrapX(ex, w.Width), qz = Mathx.Wrap(ez, w.Height);
                        float edge = w.Source.SurfaceHeight(qx, qz);
                        // Where another road's bed is nearer (a switchback's other leg, a road alongside), that road's deck rules.
                        int nearest = TerrainStamp.NearestRoad(w.Source.Stamps, w.Source.StampsAt(qx, qz), qx, qz);
                        bool own = nearest >= 0 && w.Source.Stamps[nearest].NearestOnPath(qx, qz, out _, out float ny, out _) && Math.Abs(ny - r.Y[i]) < 0.6f;
                        if (own && Math.Abs(edge - centre) > 0.5f) tilted++;
                        // Seam-safe: the same ground from every copy of the planet.
                        float copy = w.Source.SurfaceHeight(Mathx.WrapX(ex, w.Width) + w.Width, Mathx.Wrap(ez, w.Height) - w.Height);
                        seamSamples++;
                        if (Math.Abs(copy - edge) > 1e-4f) seamMismatch++;
                    }
                }
            }
            foreach (InfraStructure st in net.Structures)
            {
                InfraRoute r = st.Route;
                if (st.Kind == RoadSpan.Bridge)
                {
                    bridges++;
                    bool wet = false;
                    for (int i = st.First; i <= st.Last; i++)
                    {
                        double x = Mathx.WrapX(r.X[i], w.Width), z = Mathx.Wrap(r.Z[i], w.Height);
                        if (w.Source.WaterSurfaceAt(x, z, out float surface))
                        {
                            wet = true;
                            if (r.Y[i] < surface + 1.2f) lowOverWater++;
                        }
                    }
                    // A dry viaduct no longer than its builders could span (plus the reach to its abutments).
                    if (!st.OverWater && !r.Maglev && st.Length > InfraNetwork.MaxViaduct(r.Kind, r.Tech) + 64) { longViaducts++; if (notes.Count < 10) notes.Add($"a {st.Length:F0} m dry bridge on {r.Kind} {r.Id} ({spec.Archetype})"); }
                }
                else
                {
                    tunnels++;
                    if (st.Length > InfraNetwork.MaxTunnel(r.Kind, r.Tech) + 64) longTunnels++;
                }
            }
            // Power lines: every wire anchored (strung between two standing poles, or hanging from the one
            // that holds it), never through the ground; poles only beside the roads they follow, never on a
            // road bed or a place's pad; on a frontier world only out to mines and works.
            PowerLines power = net.Power;
            foreach (PowerPole pole in power.Poles)
            {
                poles++;
                if (pole.Fallen) fallen++;
                double px = Mathx.WrapX(pole.X, w.Width), pz = Mathx.Wrap(pole.Z, w.Height);
                if (w.Source.StampWeight(px, pz) > 0.05f) onBed++;
            }
            foreach (var (route, firstPole, lastPole) in power.Runs)
            {
                if (route.Kind is RouteKind.Track or RouteKind.Rail) offRoute++;
                if (w.Profile.Before == TechLevel.Low && !new[] { route.From, route.To }.Any(e => e != null && (e.Family.Tags.Contains("mine") || e.Family.Tags.Contains("industry")))) lowWorldLines++;
                for (int k = firstPole; k <= lastPole; k++)
                {
                    PowerPole pole = power.Poles[k];
                    double best = double.MaxValue;
                    for (int i = 0; i < route.Count; i++) best = Math.Min(best, Dist(w, route.X[i], route.Z[i], pole.X, pole.Z));
                    if (best > route.HalfWidth + (pole.Pylon ? 18f : 3.5f) + InfraNetwork.Spacing) offRoute++;
                }
            }
            foreach (PowerWire wire in power.Wires)
            {
                PowerPole pa = power.Poles[wire.A], pb = power.Poles[wire.B];
                int n = power.WiresOn(pa);
                var anchor = pa.Anchor(wire.K, n);
                if (pa.Fallen || wire.Spanned && pb.Fallen) unanchored++;
                List<System.Numerics.Vector3> pts = power.Path(wire, (x, z) => w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height)));
                if (System.Numerics.Vector3.Distance(pts[0], anchor) > 1e-3f) unanchored++;
                if (wire.Spanned)
                {
                    spans++;
                    if (System.Numerics.Vector3.Distance(pts[^1], pb.Anchor(wire.K, n)) > 1e-3f) unanchored++;
                }
                else hanging++;
                // Clear of the ground all along (a hanging wire lies on it at the pole's foot).
                foreach (var q in pts.Skip(1).SkipLast(1))
                {
                    float g = w.Source.SurfaceHeight(Mathx.WrapX(q.X, w.Width), Mathx.Wrap(q.Z, w.Height));
                    if (q.Y < g - 0.01f || !wire.Spanned && q.Y > g + 0.2f && q != pts[1])
                    {
                        throughGround++;
                        if (firstGround.Length == 0) firstGround = $" (first: a {(wire.Spanned ? "strung" : "hanging")} wire at {q.X:F0}, {q.Y:F1}, {q.Z:F0} over ground at {g:F1})";
                        break;
                    }
                }
            }
            // Abandoned vehicles: each site dressed by the library keeps its lane open end to end (a walker
            // or driver of the lane's width gets through), and none stands on a place's pad.
            foreach (WreckSite site in net.Wrecks)
            {
                wreckSites++;
                wreckKinds[site.Site] = wreckKinds.GetValueOrDefault(site.Site) + 1;
                for (float sz = 0; sz <= site.Length; sz += 4)
                for (float sx = 0; sx <= site.Width; sx += 4)
                {
                    var (px, pz) = site.World(sx, sz);
                    double cx = Mathx.WrapX(px, w.Width), cz = Mathx.Wrap(pz, w.Height);
                    if (TerrainStamp.OnPad(w.Source.Stamps, w.Source.StampsAt(cx, cz), cx, cz)) { wreckOnPad++; goto nextCheck; }
                }
                nextCheck:
                if (wreckSites > 40) continue;   // dressing every site is slow: the first forty are enough
                GalHack.Deco.DecoPlan plan = InfraDeco.DressSite(site.Site, site.Seed, site.Tech);
                if (plan == null) continue;
                wreckPieces += plan.Placements.Count;
                if (!LaneOpen(plan, site)) { blockedLanes++; if (firstBlocked.Length == 0) firstBlocked = $" (first: a {site.Site} on {spec.Archetype})"; }
            }
            // Flavour ruins: plenty of them, none on a road bed, a place's pad or another ruin; each spawns,
            // and yields salvage.
            ruinWorlds++;
            ruinRoadKm += net.Routes.Where(r => r.Kind != RouteKind.Rail).Sum(r => r.Count * InfraNetwork.Spacing) / 1000.0;
            // Where the air was never fit to build under, nothing open stands (the layers' air rule); elsewhere, plenty.
            if (net.Profile.Barred.Contains("open")) airless += net.Ruins.Count(r => r.Family.Is.Contains("open"));
            else if (++openWorlds > 0 && net.Ruins.Count < 10) { fewRuins++; notes.Add($"{net.Ruins.Count} ruins on {spec.Archetype} {spec.Seed}"); }
            foreach (FlavourRuin ruin in net.Ruins)
            {
                ruins++;
                ruinKinds[ruin.Family.Id] = ruinKinds.GetValueOrDefault(ruin.Family.Id) + 1;
                if (ruin.Blast > 0.05f) blastedRuins++;
                // Every ruin by a road is joined to it, and a house stands with others; no fuel on a world that never had engines.
                if (ruin.Route != null && ruin.Drive == null) ruinsUnjoined++;
                else if (ruin.Route != null)
                {
                    var (dx, dz, _) = ruin.Drive.PathPoint(ruin.Drive.PathCount - 1);
                    double ex = Mathx.WrapX(ruin.Drive.CenterX + dx, w.Width), ez = Mathx.Wrap(ruin.Drive.CenterZ + dz, w.Height);
                    if (net.Nearest(ex, ez, 12, out _, out double off) == null || off > 6) ruinsUnjoined++;
                }
                if (ruin.Family.Tags.Contains("wayside") && ruin.Layout.Variant.Tech != PoiGenerator.Gen.WorldProfile.Tag(w.Profile.Before) && ruin.Layout.Variant.Tech != "unique") fuelOnLow++;
                if (ruin.Family.Tags.Contains("wayside")) stopKinds[ruin.Layout.Variant.Name] = stopKinds.GetValueOrDefault(ruin.Layout.Variant.Name) + 1;
                if (ruin.Family.Tags.Contains("house") && ruin.Blast < 0.05f)
                {
                    houses++;
                    if (net.Ruins.Any(o => o != ruin && Math.Abs(Mathx.WrapDelta(o.Center.X - ruin.Center.X, w.Width)) + Math.Abs(Mathx.WrapDelta(o.Center.Z - ruin.Center.Z, w.Height)) < 90)) housesInStreets++;
                }
                TerrainStamp t = ruin.Stamp;
                for (double z = t.MinZ - 2; z <= t.MaxZ + 2; z += 3)
                for (double x = t.MinX - 2; x <= t.MaxX + 2; x += 3)
                {
                    double cx = Mathx.WrapX(x, w.Width), cz = Mathx.Wrap(z, w.Height);
                    var r = net.Nearest(cx, cz, 40, out int ri, out double rd);
                    if (r != null && rd < r.HalfWidth + 1.5) { ruinOnRoad++; goto checkedRoad; }
                }
                checkedRoad:
                foreach (PoiSite p in w.Pois)
                    if (p.Stamp.Weight(t.CenterX, t.CenterZ) > 0f) { ruinOnPad++; break; }
                foreach (FlavourRuin o in net.Ruins)
                    if (o != ruin && o.Stamp.Weight(t.CenterX, t.CenterZ) >= 1f) { ruinsTouching++; break; }
            }
            foreach (FlavourRuin ruin in net.Ruins.Take(3))
            {
                PoiInstance inst = ruin.Spawn(w.Profile);
                ruinsSpawned++;
                var y = FlavourRuin.Yields(inst);
                if (y.GetValueOrDefault("rubble") > 0 && y.Values.Sum() > 5) ruinsYielding++;
            }
            // The trees through its ruins: each the biome's own kind, standing on what's under it, clear of
            // every wall, and a real scatter instance with its collider; none on ground that grows no trees.
            foreach (var (id, p) in w.Palette.Planted ?? new Dictionary<long, PlantedFlora>())
            {
                planted++;
                if (!w.Flora.RulesOf(w.Palette.BiomeAt(p.X, p.Z)).Any(r => r.Kind == p.Kind)) { strangers++; notes.Add($"a {p.Kind} in {w.Palette.BiomeAt(p.X, p.Z)}"); }
                
                if (p.Foot < w.Source.SurfaceHeight(p.X, p.Z) - 1.1f) { unrooted++; notes.Add($"a tree under the land at {p.X:F0}, {p.Z:F0}"); }
                PoiSite site = w.Pois.FirstOrDefault(s => s.Footprint.Contains(new TerrainDigging.Terrain.Int3((int)p.X, (int)p.Foot + 1, (int)p.Z)));
                if (site != null && !RuinGrowth.Fits(site.Instance.Blocks, new PoiGenerator.Gen.Int3((int)p.X - site.Origin.X, (int)Math.Ceiling(p.Foot) - site.Origin.Y, (int)p.Z - site.Origin.Z), p.Species, p.Scale)) clipped++;
            }
            if ((w.Palette.Planted?.Count ?? 0) > 0)
            {
                var (id, tree) = w.Palette.Planted.First();
                var (fx, fz) = PlanetSurface.Terrain.FarBuilder.ChunkOfScatter(id);
                PlanetSurface.Terrain.FarBuilder.Solids ??= PlanetSurface.Terrain.FloraSolidRules.Load(ProjectSettings.GlobalizePath("res://data/flora-solids.json"));
                var job = new PlanetSurface.Terrain.FarJob { Fx = fx, Fz = fz, Lod = 0 };
                PlanetSurface.Terrain.FarBuilder.Build(job, w.Source, w.Palette, new HashSet<long>());
                bool drawn = job.Flora.Any(f => f.Kind == tree.Kind && Enumerable.Range(0, f.Count).Any(i => Math.Abs(f.Buffer[i * 16 + 3] - (tree.X - fx * PlanetSurface.Terrain.FarBuilder.Size)) < 0.01 && Math.Abs(f.Buffer[i * 16 + 11] - (tree.Z - fz * PlanetSurface.Terrain.FarBuilder.Size)) < 0.01));
                if (!drawn || job.Solids?.Any(s => s.Id == id) != true) { unplaced++; GD.PrintErr($"a planted {tree.Kind} at {tree.X:F1}, {tree.Z:F1}: drawn {drawn}, collider {job.Solids?.Any(s => s.Id == id)}, error {job.Error?.Message}"); }
            }
            // Ore tells: a formation over every deposit not under a pad, a road or water; veins matte, never neon.
            var tellPieces = PlanetSurface.Infra.InfraLayer.OrePieces(w).ToList();
            tells += tellPieces.Count;
            foreach (Deposit d in w.Map.Deposits.InRect(0, 0, w.Width, w.Height).Take(12))
            {
                tellsChecked++;
                var mesh = GalHack.Deco.DecoOreTells.Build(PlanetSurface.Infra.InfraLayer.TellOf(d), new GalHack.Deco.DecoRgb(1, 0, 0), Planets.Look.DepositLook.Glow(d.Kind) * 0.3f, false, 7, 2f);
                if (mesh.Surfaces[(int)GalHack.Deco.DecoSurface.Glow].Positions.Count > 0) glowingTells++;
            }
            if (a == 0)
            {
                // The same planet plans the same network, point for point.
                PlanetWorld again = PlanetWorld.Build(spec, Main.Catalog);
                same = again.Infra.Routes.Count == net.Routes.Count && again.Infra.Routes.Zip(net.Routes).All(p =>
                    p.First.Count == p.Second.Count && p.First.Kind == p.Second.Kind && p.First.X.SequenceEqual(p.Second.X) && p.First.Y.SequenceEqual(p.Second.Y) && p.First.Span.SequenceEqual(p.Second.Span));
            }
            await Frames(1);
        }
        string km = string.Join(", ", kinds.OrderBy(k => k.Key).Select(k => $"{k.Key} {k.Value:F0} km"));
        Check(never > 0 && neverRoutes == 0, $"a world never inhabited has no infrastructure ({never} such, {neverRoutes} routes)");
        Check(unrouted == 0 && joined == places && routes > 40,
            $"the network reaches every place it serves at one of its gates: {joined} of {places} places, {routes} routes ({km}), {unrouted} links unrouted, {separated} across a sea never bridged{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        Check(withinGrade >= stretches * 0.9 && unwalkable == 0,
            $"grades: {withinGrade} of {stretches} stretches within their kind's limit (the rest frontier roads following the land, or ramps to gates and junctions), {unwalkable} steeper than a walkable 31 degrees{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(4)) : "")}");
        Check(longViaducts == 0 && lowOverWater == 0 && longTunnels == 0,
            $"bridges and tunnels within what their builders could do: {bridges} bridges ({longViaducts} dry ones too long, {lowOverWater} points too low over water), {tunnels} tunnels ({longTunnels} too long){(notes.Count > 3 ? "; " + string.Join("; ", notes.Skip(3).Take(3)) : "")}");
        Check(offDeck <= deckSamples / 50 && tilted <= deckSamples / 25,
            $"roads are walkable decks: the ground is at the deck on {deckSamples - offDeck} of {deckSamples} centre-line samples and level across on {2 * deckSamples - tilted} of {2 * deckSamples} edge samples");
        Check(seamMismatch == 0 && seamSamples > 100, $"roads are seam-safe: {seamSamples} road samples read the same from another copy of the planet ({seamMismatch} differ)");
        Check(same, "the same planet plans the same network, point for point");
        Check(wreckSites > 10 && wreckPieces > 100 && blockedLanes == 0 && wreckOnPad == 0,
            $"abandoned vehicles keep the way open: {wreckSites} sites ({string.Join(", ", wreckKinds.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"))}), {wreckPieces} pieces in the forty dressed; {blockedLanes} with the lane blocked, {wreckOnPad} on a place's pad{firstBlocked}");
        Check(airless == 0, $"no open houses or farms stand on a world whose air was never fit to build under ({airless} did)");
        Check(ruins >= openWorlds * 10 && openWorlds > 0 && fewRuins == 0 && ruinOnRoad == 0 && ruinOnPad == 0 && ruinsTouching == 0 && ruinsYielding == ruinsSpawned,
            $"flavour ruins: {ruins} on {ruinWorlds} inhabited worlds ({string.Join(", ", ruinKinds.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"))}; {blastedRuins} flattened by a blast), every world with ten or more ({fewRuins} with fewer); {ruinOnRoad} on a road bed, {ruinOnPad} on a place's pad, {ruinsTouching} on another ruin; {ruinsYielding} of {ruinsSpawned} spawned yield salvage");
        Check(ruins <= ruinWorlds * 50 && ruinsUnjoined == 0 && fuelOnLow == 0 && housesInStreets * 2 >= houses,
            $"flavour ruins have reasons: {ruins / (float)ruinWorlds:F0} a world (once 150), {ruinRoadKm / Math.Max(1, ruins):F1} km of road each; {ruinsUnjoined} not joined to their road, {housesInStreets} of {houses} houses in a street, {fuelOnLow} stops of the wrong age ({string.Join(", ", stopKinds.OrderBy(k => k.Key).Select(k => $"{k.Value} {k.Key}"))})");
        Check(tells > 1000 && glowingTells == 0, $"ore tells: {tells} formations over the deposits, their veins matte ({glowingTells} of {tellsChecked} sampled glow)");
        Check(planted > 0 && strangers == 0 && unrooted == 0 && clipped == 0 && unplaced == 0, $"trees through ruins are the world's own flora: {planted} planted, {strangers} not of their biome, {unrooted} off the ground, {clipped} into a wall, {unplaced} not in the scatter");
        Check(poles > 100 && spans > 100 && unanchored == 0 && throughGround == 0,
            $"power lines are anchored: {poles} poles and pylons ({fallen} fallen), {spans} wires strung between standing poles, {hanging} hanging from the pole that holds them; {unanchored} unanchored, {throughGround} through the ground or lying off it{firstGround}");
        Check(offRoute == 0 && onBed == 0 && lowWorldLines == 0,
            $"power lines only where they make sense: every pole beside the road or highway it follows ({offRoute} not), none on a road bed or a pad ({onBed}), and on frontier worlds only out to mines and works ({lowWorldLines} elsewhere)");
        GD.Print($"INFO  planning took {planBest:F0} to {planWorst:F0} ms a world");
    }

    /// <summary>
    /// Whether a dressed site's lane runs open end to end: every piece's footprint (its mesh's box,
    /// turned and placed) marked on a half-metre raster, grown by half the lane, and a way found from
    /// the north opening to the south one. Pieces you walk over (rails, scars, litter) don't count.
    /// </summary>
    static bool LaneOpen(GalHack.Deco.DecoPlan plan, WreckSite site)
    {
        const float Cell = 0.5f;
        int nx = (int)Math.Ceiling(site.Width / Cell), nz = (int)Math.Ceiling(site.Length / Cell);
        var blocked = new bool[nx * nz];
        var forge = InfraDeco.Poi.Forge;
        foreach (var pl in plan.Placements)
        {
            var piece = forge.Catalogue.Piece(pl.Piece);
            if (piece.WalkOver) continue;
            var mesh = forge.Mesh(pl.Look);
            if (mesh.Max.Y - mesh.Min.Y < 0.35f) continue;
            var t = pl.Transform;
            var c = new[] { new System.Numerics.Vector2(mesh.Min.X, mesh.Min.Z), new(mesh.Max.X, mesh.Min.Z), new(mesh.Max.X, mesh.Max.Z), new(mesh.Min.X, mesh.Max.Z) }
                .Select(v => { var p = System.Numerics.Vector3.Transform(new System.Numerics.Vector3(v.X, 0, v.Y), t); return new System.Numerics.Vector2(p.X, p.Z); }).ToArray();
            for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                var q = new System.Numerics.Vector2((i + 0.5f) * Cell, (j + 0.5f) * Cell);
                bool inside = true;
                for (int e = 0; e < 4 && inside; e++)
                {
                    var a0 = c[e];
                    var a1 = c[(e + 1) % 4];
                    float cross = (a1.X - a0.X) * (q.Y - a0.Y) - (a1.Y - a0.Y) * (q.X - a0.X);
                    inside = cross >= 0;
                }
                if (!inside)
                {
                    inside = true;
                    for (int e = 0; e < 4 && inside; e++)
                    {
                        var a0 = c[e];
                        var a1 = c[(e + 1) % 4];
                        float cross = (a1.X - a0.X) * (q.Y - a0.Y) - (a1.Y - a0.Y) * (q.X - a0.X);
                        inside = cross <= 0;
                    }
                }
                if (inside) blocked[j * nx + i] = true;
            }
        }
        // Grow by half the lane: the walker's centre must keep that far from anything.
        int grow = (int)Math.Ceiling(site.Lane / 2 / Cell - 0.01f);
        var open = new bool[nx * nz];
        for (int j = 0; j < nz; j++)
        for (int i = 0; i < nx; i++)
        {
            bool ok = i >= grow - 1 && i < nx - grow + 1;
            for (int dj = -grow + 1; dj < grow && ok; dj++)
            for (int di = -grow + 1; di < grow && ok; di++)
            {
                int x = i + di, z = j + dj;
                if (x < 0 || x >= nx) { ok = false; break; }
                if (z < 0 || z >= nz) continue;
                ok = !blocked[z * nx + x];
            }
            open[j * nx + i] = ok;
        }
        var queue = new Queue<int>();
        var seen = new bool[nx * nz];
        int laneCell = (int)(site.LaneX / Cell);
        for (int i = Math.Max(0, laneCell - 2); i <= Math.Min(nx - 1, laneCell + 2); i++)
            if (open[i]) { queue.Enqueue(i); seen[i] = true; }
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            int i = k % nx, j = k / nx;
            if (j == nz - 1) return true;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int x = i + di, z = j + dj;
                if (x < 0 || x >= nx || z < 0 || z >= nz) continue;
                int m = z * nx + x;
                if (seen[m] || !open[m]) continue;
                seen[m] = true;
                queue.Enqueue(m);
            }
        }
        return false;
    }

    static double Dist(PlanetWorld w, double ax, double az, double bx, double bz)
    {
        double dx = Mathx.WrapDelta(ax - bx, w.Width), dz = Mathx.WrapDelta(az - bz, w.Height);
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>Whether a route passes within a distance of a point.</summary>
    static bool Crosses(PlanetWorld w, InfraRoute o, double x, double z, double within)
    {
        for (int k = 0; k < o.Count; k += 2)
            if (Dist(w, o.X[k], o.Z[k], x, z) < within) return true;
        return false;
    }

    // ---- roads as the ground draws them ---------------------------------------------------------

    /// <summary>
    /// The terrain shader paints roads from their own lines (RoadField), not the mesh's vertices: on fresh
    /// worlds, every ground stretch of every road is found from the cell it lies in, its middle within a few
    /// centimetres of the line and its whole width on the deck, at any angle; bridges and tunnels are not
    /// painted on the ground.
    /// </summary>
    async Task RoadLineChecks()
    {
        var rand = new Random();
        int worlds = 0, samples = 0, offLine = 0, narrow = 0, missing = 0, segments = 0, points = 0, spans = 0, spanPainted = 0;
        var notes = new List<string>();
        foreach (Archetype kind in new[] { Archetype.Temperate, Archetype.Arid })
        {
            var spec = PlanetSpec.Roll((ulong)rand.NextInt64(), kind, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            if (!w.Profile.Inhabited) continue;
            worlds++;
            RoadField field = RoadField.Build(w.Source, w.Width, w.Height);
            segments += field.SegmentCount;
            foreach (TerrainStamp t in w.Source.Stamps)
            {
                if (t.Shape != StampShape.Road) continue;
                for (int i = 0; i + 1 < t.PathCount; i++)
                {
                    points++;
                    var (ax, az, ay) = t.PathPoint(i);
                    var (bx, bz, by) = t.PathPoint(i + 1);
                    float f = (float)rand.NextDouble(), ex = bx - ax, ez = bz - az, len = MathF.Max(1e-4f, MathF.Sqrt(ex * ex + ez * ez));
                    double x = t.CenterX + ax + ex * f, z = t.CenterZ + az + ez * f;
                    float y = ay + (by - ay) * f;
                    if (t.PathSpan(i) != RoadSpan.Ground)
                    {
                        // Only a long way over the ground: a bridge's deck over a gorge, a tunnel deep in a hill.
                        float ground = w.Source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
                        if (Math.Abs(ground - y) < 8f) continue;
                        spans++;
                        if (field.EdgeAt(x, z, ground, 1.2f, out _) < -0.5f) spanPainted++;
                        continue;
                    }
                    samples++;
                    float edge = field.EdgeAt(x, z, y, 1.2f, out float across);
                    if (float.IsInfinity(edge)) { missing++; if (notes.Count < 3) notes.Add($"no road found on {t.Name} at {x:F0}, {z:F0}"); continue; }
                    // Its middle is as deep in a deck as its own line puts it (a wider road it joins may claim it).
                    if (edge > 0.06f - t.HalfWidth) { offLine++; if (notes.Count < 3) notes.Add($"{t.Name} at {x:F0}, {z:F0} is {edge + t.HalfWidth:F2} m off its line ({across:F2} m from the nearest)"); }
                    // Just inside each edge, square to the line: still on a deck.
                    float nx = -ez / len, nz = ex / len, side = t.HalfWidth - 0.25f;
                    if (field.EdgeAt(x + nx * side, z + nz * side, y, 1.2f, out _) >= 0f || field.EdgeAt(x - nx * side, z - nz * side, y, 1.2f, out _) >= 0f) narrow++;
                }
            }
            await Frames(1);
        }
        Check(worlds > 0 && samples > 1000 && missing == 0 && offLine <= samples / 2000 && narrow <= samples / 200,
            $"roads are drawn from their lines: {segments} segments for {points} road points on {worlds} fresh worlds; of {samples} points on the ground {missing} not found, {offLine} off the line by over 6 cm, {narrow} narrower than the deck{(notes.Count > 0 ? "; " + string.Join("; ", notes) : "")}");
        Check(spanPainted <= spans / 50,
            $"no road painted on the ground far under a bridge or over a tunnel: {spanPainted} of {spans} points");
    }

    // ---- the start: the edge of a ruined town -----------------------------------------------------

    async Task StartTownChecks()
    {
        bool was = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = true;
        int worlds = 0, towns = 0, houses = 0, streets = 0, jams = 0, settlementsNear = 0, landmarks = 0, walkable = 0;
        var notes = new List<string>();
        try
        {
            foreach (var (a, seed) in new[] { (0, 31UL), (1, 32UL), (2, 33UL), (0, 34UL) })
            {
                var spec = PlanetSpec.Roll(seed, (Archetype)a, 0);
                PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
                worlds++;
                StartTown town = w.StartTown;
                if (town == null) { notes.Add($"no town on {spec.Archetype} {seed}"); continue; }
                towns++;
                houses += town.Houses.Count;
                streets += town.Streets.Count;
                if (w.Infra.Wrecks.Any(k => town.Streets.Contains(k.Route))) jams++;
                // No living settlement within 1.5 km of the town's edge (where the bunker goes).
                foreach (PoiSite p in w.Pois)
                    if (p.Family.Tags.Contains("settlement") && Dist(w, p.Center.X, p.Center.Z, town.EdgeX, town.EdgeZ) < StartTown.NoSettlement) settlementsNear++;
                // A place on the horizon, nobody's, 300 to 1,200 m from the edge.
                double ld = Dist(w, town.Landmark.Center.X, town.Landmark.Center.Z, town.EdgeX, town.EdgeZ);
                if (town.Landmark.Instance.State != PoiState.Active && ld >= 300 && ld <= 1200) landmarks++;
                else notes.Add($"the landmark on {spec.Archetype} {seed} is {ld:F0} m off");
                // Walkable 60 m out from the edge in at least three of eight directions (the slice's rule: dry, no rise over 1.2 m in 2 m).
                int ways = 0;
                for (int d = 0; d < 8; d++)
                {
                    double ang = d * Math.PI / 4;
                    bool ok = true;
                    float prev = w.GroundHeight(town.EdgeX, town.EdgeZ);
                    for (double s = 2; s <= 60 && ok; s += 2)
                    {
                        double x = Mathx.WrapX(town.EdgeX + Math.Cos(ang) * s, w.Width), z = Mathx.Wrap(town.EdgeZ + Math.Sin(ang) * s, w.Height);
                        float g = w.GroundHeight(x, z);
                        ok = Math.Abs(g - prev) <= 1.2f && g > 1.5f && !w.Source.WaterSurfaceAt(x, z, out _);
                        prev = g;
                    }
                    if (ok) ways++;
                }
                if (ways >= 3) walkable++;
                else notes.Add($"only {ways} walkable ways from the edge on {spec.Archetype} {seed}");
                await Frames(1);
            }
        }
        finally
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = was;
        }
        Check(towns == worlds && houses >= towns * 6 && streets >= towns * 2 && jams == towns,
            $"start worlds begin at a ruined town by the landing site: {towns} of {worlds} worlds, {houses} abandoned houses on {streets} streets, rusted cars on the main street of {jams}{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        Check(settlementsNear == 0 && landmarks == towns && walkable == towns,
            $"the town's edge keeps the start's rules: no living settlement within {StartTown.NoSettlement:F0} m ({settlementsNear}), a place nobody holds in view 300 to 1,200 m off ({landmarks} of {towns}), walkable 60 m in three of eight directions ({walkable} of {towns})");
    }

    // ---- ports, and the people who tried to leave ---------------------------------------------------

    /// <summary>
    /// On real worlds: the main starport is seated and is the network's main node, with roads leading to
    /// it; an exodus world's jams thicken toward its ports; a survivor town's clearing has a spot to land on.
    /// </summary>
    async Task PortChecks()
    {
        int worlds = 0, withPort = 0, seated = 0, served = 0, highways = 0, exodus = 0, scrappy = 0, thicker = 0, spots = 0, spotsOnGround = 0;
        double nearKm = 0, farKm = 0;
        int nearJams = 0, farJams = 0;
        var notes = new List<string>();
        var seeds = new List<(int A, ulong Seed)>();
        // Worlds with what we're after: rolled until there are enough of each.
        for (ulong s = 200; s < 900 && seeds.Count < 6; s++)
        {
            var spec = PlanetSpec.Roll(s, (Archetype)0, 0);
            WorldProfile p = PlanetWorld.ProfileFor(spec, false);
            if (p.Exodus && p.Before != TechLevel.Low && seeds.Count(x => x.A == 1) < 3) seeds.Add((1, s));
            else if (!p.Exodus && p.Starport >= Starport.C && seeds.Count(x => x.A == 0) < 3) seeds.Add((0, s));
        }
        foreach (var (kind, seed) in seeds)
        {
            // Each world gets a survivor town, for its clearing.
            string wasForced = PlanetSurface.Poi.PoiSiting.ForceFamily;
            PlanetSurface.Poi.PoiSiting.ForceFamily = "survivor_town";
            PlanetWorld w;
            try { w = PlanetWorld.Build(PlanetSpec.Roll(seed, (Archetype)0, 0), Main.Catalog); }
            finally { PlanetSurface.Poi.PoiSiting.ForceFamily = wasForced; }
            InfraNetwork net = w.Infra;
            worlds++;
            if (w.Profile.Starport != Starport.None) withPort++;
            PoiSite main = w.Pois.FirstOrDefault(p => p.Primary);
            if (main != null)
            {
                seated++;
                var routes = net.Routes.Where(r => r.From == main || r.To == main).ToList();
                if (routes.Count > 0) served++; else notes.Add($"the main port on {seed} has no road");
                if (routes.Any(r => r.Kind == RouteKind.Highway) || w.Profile.Before == TechLevel.Low) highways++;
            }
            foreach (LandingSpot l in net.LandingSpots)
            {
                spots++;
                if (Math.Abs(w.GroundHeight(Mathx.WrapX(l.X, w.Width), Mathx.Wrap(l.Z, w.Height)) - l.Y) < 1.5f) spotsOnGround++;
            }
            if (w.Profile.Exodus)
            {
                exodus++;
                var ports = w.Pois.Where(Wrecks.IsPort).ToList();
                scrappy += ports.Count(p => p.Family.Tags.Contains("scrappy"));
                // Jams per kilometre of road, near a port and further out.
                foreach (InfraRoute r in net.Routes.Where(r => r.Kind != RouteKind.Rail))
                    for (int i = 0; i < r.Count; i += 5)
                    {
                        double d = ports.Count == 0 ? 1e9 : ports.Min(p => Dist(w, p.Center.X, p.Center.Z, r.X[i], r.Z[i]));
                        if (d < 1000) nearKm += 0.02; else farKm += 0.02;
                    }
                foreach (WreckSite j in net.Wrecks.Where(k => k.Site == "evacuation-jam"))
                {
                    double d = ports.Count == 0 ? 1e9 : ports.Min(p => Dist(w, p.Center.X, p.Center.Z, j.X, j.Z));
                    if (d < 1000) nearJams++; else farJams++;
                }
            }
            await Frames(1);
        }
        double nearRate = nearJams / Math.Max(0.01, nearKm), farRate = farJams / Math.Max(0.01, farKm);
        Check(seated == withPort && served == seated && highways == seated, $"the main starport is the network's main node: {seated} of {withPort} seated on {worlds} worlds, {served} with roads, {highways} with a highway{(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(3)) : "")}");
        Check(exodus > 0 && scrappy >= exodus * 3 && nearRate > farRate * 2, $"exodus jams thicken toward the ports: {exodus} worlds, {scrappy} scrappy ports seated, {nearRate:F1} jams a km within 1 km of a port against {farRate:F1} further out");
        Check(spots > 0 && spots == spotsOnGround, $"{spots} landing clearings by survivor towns, each flat at its spot ({spotsOnGround} on the ground)");
    }

    // ---- no stitches on the hillsides --------------------------------------------------------------

    /// <summary>
    /// Stamped ground has no cliffs of its own making. Round every road's banks and every tunnel mouth,
    /// on several worlds, every two metres: a bank may be as steep as it must, but the ground never
    /// jumps in one step where the slope on either side of the step doesn't.
    /// </summary>
    async Task StitchChecks()
    {
        const float Allowed = 4f;
        int worlds = 0, samples = 0, stitches = 0, mouths = 0, mouthStitches = 0;
        float worst = 0f, worstMouth = 0f;
        var notes = new List<string>();
        foreach (var (a, seed) in new[] { (0, 61UL), (0, 900UL), (1, 62UL), (2, 63UL) })
        {
            PlanetWorld w = PlanetWorld.Build(PlanetSpec.Roll(seed, (Archetype)a, 0), Main.Catalog);
            PlanetTerrainSource source = w.Source;
            worlds++;
            float H(double x, double z) => source.SurfaceHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
            float N(double x, double z) => source.NaturalHeight(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height));
            // A stitch is a sudden step: a slope, however steep, runs on either side of itself; a step stands alone.
            bool Step(double x, double z, double dx, double dz, out float size)
            {
                float a = H(x - dx, z - dz), b = H(x, z), c = H(x + dx, z + dz), d = H(x + dx * 2, z + dz * 2);
                float st = MathF.Abs(c - b), nat = MathF.Abs(N(x + dx, z + dz) - N(x, z));
                size = st - nat;
                return st > Allowed && st > nat * 1.5f + 1f && st > 3f * MathF.Max(MathF.Abs(b - a), MathF.Abs(d - c));
            }
            foreach (InfraStructure st in w.Infra.Structures.Where(k => k.Kind == RoadSpan.Tunnel))
                foreach (bool first in new[] { true, false })
                {
                    InfraRoute r = st.Route;
                    int at = first ? st.First : st.Last, outward = first ? -1 : 1;
                    int o = Math.Clamp(at + outward, 0, r.Count - 1);
                    if (o == at) continue;
                    double ox = r.X[o] - r.X[at], oz = r.Z[o] - r.Z[at], len = Math.Sqrt(ox * ox + oz * oz);
                    ox /= len;
                    oz /= len;
                    double sx = -oz, sz = ox;
                    float inner = r.HalfWidth + 10f;
                    mouths++;
                    bool bad = false;
                    for (double u = -44; u <= 44; u += 2)
                        for (double v = -36; v <= 44; v += 2)
                        {
                            // What the portal's walls and box stand in, and the road itself.
                            if (Math.Abs(u) <= inner && v >= -12 && v <= 34) continue;
                            double x = r.X[at] + ox * v + sx * u, z = r.Z[at] + oz * v + sz * u;
                            // Nor the cutting: its banks draw in to the portal's wing walls and end steep by design.
                            if (w.Infra.Nearest(Mathx.WrapX(x, w.Width), Mathx.Wrap(z, w.Height), inner + 4, out _, out double fromRoad) != null && fromRoad < inner + 4) continue;
                            samples += 2;
                            foreach (var (dx, dz) in new[] { (2.0, 0.0), (0.0, 2.0) })
                                if (Step(x, z, dx, dz, out float size))
                                {
                                    bad = true;
                                    if (size > worstMouth) { worstMouth = size; notes.Add($"{size:F1} m step by a mouth at {x:F0},{z:F0} on {seed}"); }
                                }
                        }
                    if (bad) mouthStitches++;
                }
            await Frames(1);
            foreach (InfraRoute r in w.Infra.Routes)
                for (int i = 4; i + 4 < r.Count; i += 6)
                {
                    if (r.Span[i] != RoadSpan.Ground) continue;
                    double ox = r.X[i + 1] - r.X[i - 1], oz = r.Z[i + 1] - r.Z[i - 1], len = Math.Sqrt(ox * ox + oz * oz);
                    if (len < 1e-6) continue;
                    double sx = -oz / len, sz = ox / len;
                    for (double u = -30; u <= 30; u += 2)
                    {
                        if (Math.Abs(u) <= r.HalfWidth + 2) continue;
                        double x = r.X[i] + sx * u, z = r.Z[i] + sz * u;
                        samples++;
                        if (Step(x, z, sx * 2, sz * 2, out float size))
                        {
                            stitches++;
                            if (size > worst) { worst = size; notes.Add($"{size:F1} m step by {r.Kind} {r.Id} at {x:F0},{z:F0} on {seed}"); }
                        }
                    }
                }
            await Frames(1);
        }
        // What's left is at the inside of bends beside a mouth, where the nearest point of the road jumps: small, and in a cutting's shadow.
        Check(mouthStitches <= mouths / 4 && worstMouth <= 12f, $"tunnel mouths sit in their slopes: {mouths} mouths, {mouthStitches} with a step in the ground round them (worst {worstMouth:F1} m; at most 12 m, on a quarter of them){(worstMouth > 12f ? "; " + string.Join("; ", notes.TakeLast(4)) : "")}");
        Check(stitches <= samples / 1000, $"road banks run smooth: {samples} points, {stitches} steps of the road's making (at most one in a thousand, all at the drawn-in banks by a portal; worst {worst:F1} m)");
    }

    // ---- water sits in ground that contains it -----------------------------------------------------

    /// <summary>
    /// Water never hangs in the air. On several worlds, round every road, pad and river: the water
    /// volume (what the game swims in) meets ground wherever it ends, and the sheets actually drawn
    /// have their edges at or under the same stamped ground, at every level of detail; so has a
    /// flooded tunnel's water, which must be in its bore. A water edge may stand a hand's width proud.
    /// </summary>
    async Task WaterChecks()
    {
        const float Margin = 0.3f;
        int worlds = 0, columns = 0, volumeEnds = 0, volumeFloating = 0, sheetEdges = 0, sheetFloating = 0, flood = 0, floodFloating = 0;
        float worstVolume = 0f, worstSheet = 0f, worstFlood = 0f;
        var notes = new List<string>();
        var tally = new Dictionary<string, int>();
        void Note(string what) => tally[what] = tally.GetValueOrDefault(what) + 1;
        string NearBy(PlanetTerrainSource source, double x, double z)
        {
            foreach (int i in source.StampsAt(x, z))
            {
                TerrainStamp t = source.Stamps[i];
                if (t.Weight(x, z) > 0.02f) return t.Shape + ":" + t.Name;
            }
            return "natural";
        }
        // A flooded tunnel's water: in the bore, with ground over it.
        void Flooded(PlanetWorld w)
        {
            PlanetTerrainSource source = w.Source;
            // Any tunnel may flood, and few on one world do: every tunnel of these throwaway worlds is judged as if it had.
            foreach (InfraStructure st in w.Infra.Structures.Where(k => k.Kind == RoadSpan.Tunnel))
                for (int k = st.First; st.Flooded = true && k < st.Last; k++)
                {
                    if (!InfraLayer.FloodLevel(st, k, source, out float level)) continue;
                    flood++;
                    float g = source.SurfaceHeight(Mathx.WrapX(st.Route.X[k], w.Width), Mathx.Wrap(st.Route.Z[k], w.Height));
                    if (g >= level + PlanetTerrainSource.TunnelRoof) continue;
                    floodFloating++;
                    Note("flooded tunnel");
                    worstFlood = Math.Max(worstFlood, level - g);
                }
        }
        foreach (var (a, seed, start) in new[] { (0, 61UL, false), (0, 900UL, false), (1, 62UL, false), (2, 63UL, false), (0, 31UL, true), (0, 34UL, true) })
        {
            bool was = PlanetSurface.Poi.PoiSiting.StartWorld;
            PlanetSurface.Poi.PoiSiting.StartWorld = start;
            PlanetWorld w;
            try { w = PlanetWorld.Build(PlanetSpec.Roll(seed, (Archetype)a, 0), Main.Catalog); }
            finally { PlanetSurface.Poi.PoiSiting.StartWorld = was; }
            PlanetTerrainSource source = w.Source;
            worlds++;

            // The patches to look at: far chunks with a road, pad or river's water in them.
            var chunks = new HashSet<(int X, int Z)>();
            void Mark(double x, double z) => chunks.Add(((int)(Mathx.WrapX(x, w.Width) / FarBuilder.Size), (int)(Mathx.Wrap(z, w.Height) / FarBuilder.Size)));
            foreach (TerrainStamp t in source.Stamps)
                if (t.Shape != StampShape.Road && t.Shape != StampShape.Buried) Mark(t.CenterX, t.CenterZ);
            foreach (InfraRoute r in w.Infra.Routes)
                for (int i = 0; i < r.Count; i += 8) Mark(r.X[i], r.Z[i]);
            var near = chunks.Where(c =>
            {
                for (int z = 0; z < FarBuilder.Size; z += 16)
                    for (int x = 0; x < FarBuilder.Size; x += 16)
                        if (source.Sampler.Sample(c.X * FarBuilder.Size + x, c.Z * FarBuilder.Size + z, 4).InRiver) return true;
                return false;
            }).OrderBy(c => c.X).ThenBy(c => c.Z).ToList();
            if (near.Count > 90) near = near.Where((_, i) => i % (near.Count / 90 + 1) == 0).ToList();

            // The volume: wherever water stops, the ground beside it stands to its surface.
            foreach (var (cx, cz) in near)
            {
                const int Step = 3;
                for (int z = 0; z < FarBuilder.Size; z += Step)
                    for (int x = 0; x < FarBuilder.Size; x += Step)
                    {
                        double px = Mathx.WrapX(cx * FarBuilder.Size + x + 0.5, w.Width), pz = Mathx.Wrap(cz * FarBuilder.Size + z + 0.5, w.Height);
                        columns++;
                        if (!source.WaterSurfaceAt(px, pz, out float surface)) continue;
                        foreach (var (dx, dz) in new[] { (Step, 0), (-Step, 0), (0, Step), (0, -Step) })
                        {
                            double qx = Mathx.WrapX(px + dx, w.Width), qz = Mathx.Wrap(pz + dz, w.Height);
                            // Water going on next door, however steeply it falls there, is a river running on, not an end.
                            if (source.WaterSurfaceAt(qx, qz, out _)) continue;
                            volumeEnds++;
                            float over = surface - source.SurfaceHeight(qx, qz);
                            if (over <= Margin) continue;
                            volumeFloating++;
                            string body = source.Sampler.Sample(px, pz, 1).InRiver ? "river" : "sea";
                            Note($"{body} volume " + NearBy(source, qx, qz));
                            if (over > worstVolume) { worstVolume = over; notes.Add($"{body} volume {over:F1} m proud at {qx:F0},{qz:F0} ({NearBy(source, qx, qz)})"); }
                        }
                    }
                await Frames(0);
            }

            // The sheets: every edge vertex of a far chunk's river water, at each level of detail.
            foreach (var (cx, cz) in near)
                for (int lod = 0; lod <= 2; lod++)
                {
                    var job = new FarJob { Fx = cx, Fz = cz, Lod = lod, SpanX = FarBuilder.Span(cx, (int)w.Width), SpanZ = FarBuilder.Span(cz, (int)w.Height) };
                    FarBuilder.Build(job, source, w.Palette, new HashSet<long>());
                    if (job.Error != null) throw job.Error;
                    if (job.WaterVertices == null) continue;
                    // A vertex on the rim of the sheet: a grid square beside it has no water.
                    var uses = new Dictionary<int, int>();
                    foreach (int v in job.WaterIndices) uses[v] = uses.GetValueOrDefault(v) + 1;
                    for (int v = 0; v < job.WaterVertices.Length; v++)
                    {
                        Vector3 p = job.WaterVertices[v];
                        // Six triangle corners round a full interior vertex; the rim of the chunk carries on next door.
                        if (uses.GetValueOrDefault(v) >= 6 || p.X <= 0 || p.Z <= 0 || p.X >= job.SpanX || p.Z >= job.SpanZ) continue;
                        double px = Mathx.WrapX(cx * (double)FarBuilder.Size + p.X, w.Width), pz = Mathx.Wrap(cz * (double)FarBuilder.Size + p.Z, w.Height);
                        sheetEdges++;
                        float over = p.Y - source.Ground(px, pz, FarBuilder.Step(lod)).Height;
                        if (over <= Margin) continue;
                        sheetFloating++;
                        Note($"sheet lod{lod} " + NearBy(source, px, pz));
                        if (over > worstSheet) { worstSheet = over; notes.Add($"sheet {over:F1} m proud, lod {lod}, at {px:F0},{pz:F0} ({NearBy(source, px, pz)})"); }
                    }
                }

            Flooded(w);
            await Frames(1);
        }
        // Tunnels are few: fresh worlds until there are some to judge.
        var fresh = new Random();
        for (int k = 0; k < 12 && flood == 0; k++)
        {
            Flooded(PlanetWorld.Build(PlanetSpec.Roll((ulong)fresh.NextInt64(), Archetype.Temperate, 0), Main.Catalog));
            await Frames(1);
        }
        string kinds = string.Join(", ", tally.OrderByDescending(k => k.Value).Take(10).Select(k => $"{k.Key} {k.Value}"));
        // The volume is what you swim in, not what you see: water on a rapid falls a metre in seven, so two columns 3 m apart can differ by half a metre, and no more.
        Check(worstVolume <= 1f && volumeFloating <= volumeEnds / 100, $"the water volume meets ground wherever it ends: {volumeEnds} ends over {worlds} worlds and {columns} columns, {volumeFloating} more than {Margin} m proud (worst {worstVolume:F1} m; at most 1 m, on 1% of ends){(notes.Count > 0 ? "; " + string.Join("; ", notes.Where(n => n.Contains(" volume ")).TakeLast(4)) : "")}; {string.Join(", ", tally.Where(k => k.Key.Contains(" volume ")).OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} {k.Value}"))}");
        Check(sheetFloating == 0, $"the water drawn has its edge in the ground: {sheetEdges} edge points at three levels of detail, {sheetFloating} more than {Margin} m proud (worst {worstSheet:F1} m){(kinds.Length > 0 ? "; " + kinds : "")}{(notes.Count > 0 ? "; " + string.Join("; ", notes.TakeLast(3)) : "")}");
        Check(floodFloating == 0 && flood > 0, $"flooded tunnels hold their water in the bore: {flood} stretches, {floodFloating} out in the air");
    }

    // ---- what runs under a town ------------------------------------------------------------------

    /// <summary>
    /// The networks under real towns: the terrain opens every cell they take and none of the ground
    /// over them changes; they keep their cover; every way in reaches the sky; walking from the first
    /// way in reaches all the others and every chamber; the metro only where the world had one.
    /// </summary>
    async Task UnderChecks()
    {
        bool was = PlanetSurface.Poi.PoiSiting.StartWorld;
        PlanetSurface.Poi.PoiSiting.StartWorld = true;
        int worlds = 0, cells = 0, closed = 0, moved = 0, surfaced = 0, buried = 0, unreached = 0, stations = 0, outfalls = 0, shallow = 0, tested = 0, spaces = 0, houses = 0;
        var notes = new List<string>();
        try
        {
            foreach (var (a, seed) in new[] { (0, 31UL), (1, 32UL), (2, 33UL), (0, 34UL) })
            {
                var spec = PlanetSpec.Roll(seed, (Archetype)a, 0);
                PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
                UnderTown under = w.StartTown?.Under;
                if (under == null) { notes.Add($"no underground on {spec.Archetype} {seed}"); continue; }
                worlds++;
                UnderBuilt built = under.Built;
                PlanetTerrainSource source = w.Source;
                if (UnderPlan.Metro(w.Profile.Before) != under.Scheme.HasMetro) notes.Add($"metro wrong for {w.Profile.Before} on {seed}");
                stations += under.Scheme.Stations.Count;
                outfalls += under.Scheme.Outfalls.Count;
                spaces += built.Spaces.Count;
                // A sample of every open cell: carved out of the ground, by a stamp, on the map.
                int n = 0;
                foreach (PoiGenerator.Gen.Int3 c in built.Open)
                {
                    if (n++ % 53 != 0) continue;
                    cells++;
                    int cx = (int)Math.Floor(Mathx.WrapX(c.X + 0.5, w.Width)), cz = (int)Math.Floor(Mathx.Wrap(c.Z + 0.5, w.Height));
                    if (source.CellDensity(cx, c.Y, cz) > 0f) closed++;
                }
                // Over the tunnels the ground stays the street's, and they keep some rock over them.
                foreach (UnderRun r in under.Scheme.Runs.Where(r => r.Kind != UnderKind.Metro))
                    for (int i = 6; i + 6 < r.Length; i += 11)
                    {
                        var (x, z) = r.Column(i);
                        double gx = Mathx.WrapX(x + 0.5, w.Width), gz = Mathx.Wrap(z + 0.5, w.Height);
                        float g = source.SurfaceHeight(gx, gz);
                        if (float.IsNaN(g) || g > 4000) moved++;
                        if (g - (r.Floors[i] + r.Height) < 0.5f && !under.Scheme.Outfalls.Any(o => Math.Abs(o.X - x) + Math.Abs(o.Z - z) < 20))
                        {
                            shallow++;
                            if (notes.Count < 6) notes.Add($"{r.Kind} {g - (r.Floors[i] + r.Height):F1} m under the ground at {x},{z}");
                        }
                    }
                // Every way in opens to the sky at the ground's own level.
                foreach (PoiGenerator.Gen.Int3 p in built.WaysIn)
                {
                    float g = source.SurfaceHeight(Mathx.WrapX(p.X + 0.5, w.Width), Mathx.Wrap(p.Z + 0.5, w.Height));
                    if (Math.Abs(g - p.Y) <= 1.6f) surfaced++;
                    else
                    {
                        buried++;
                        notes.Add($"way in at {p} with the ground at {g:F1}");
                    }
                }
                // Walked from the first way in, on the real plan.
                var view = new UnderView(built);
                var nav = new PoiGenerator.Gen.NavGrid(view, view.Mask());
                HashSet<PoiGenerator.Gen.Int3> reach = nav.Reachable(view.Local(built.WaysIn[0]));
                foreach (PoiGenerator.Gen.Int3 p in built.WaysIn) { tested++; if (!reach.Contains(view.Local(p))) unreached++; }
                foreach (UnderRoom r in under.Scheme.Rooms)
                {
                    tested++;
                    int mx = (r.Min.X + r.Max.X) / 2, mz = (r.Min.Z + r.Max.Z) / 2;
                    bool ok = false;
                    for (int k = -3; k <= 3 && !ok; k++)
                        for (int v = -1; v <= 1 && !ok; v++)
                            ok = reach.Contains(view.Local(new PoiGenerator.Gen.Int3(mx + k, r.Min.Y + v, mz))) || reach.Contains(view.Local(new PoiGenerator.Gen.Int3(mx, r.Min.Y + v, mz + k)));
                    if (!ok) unreached++;
                }
                houses += w.StartTown.Houses.Count;
                // No house stands on the underground: its cellars and the tunnels never meet.
                foreach (FlavourRuin h in w.StartTown.Houses)
                {
                    var (hx, hz) = h.Center;
                    double half = Math.Max(h.Stamp.MaxX - h.Stamp.MinX, h.Stamp.MaxZ - h.Stamp.MinZ) * 0.5;
                    foreach (PoiGenerator.Gen.Int3 c in built.Open)
                        if (Math.Abs(Mathx.WrapDelta(c.X - hx, w.Width)) < h.Stamp.MaxX - hx && Math.Abs(Mathx.WrapDelta(c.Z - hz, w.Height)) < h.Stamp.MaxZ - hz && c.Y > h.OriginY - 9)
                        {
                            if (notes.Count < 6) notes.Add($"tunnels under a house at {hx:F0},{hz:F0}: {c}");
                            moved++;
                            break;
                        }
                }
                await Frames(1);
            }
        }
        finally
        {
            PlanetSurface.Poi.PoiSiting.StartWorld = was;
        }
        Check(worlds >= 3 && closed == 0 && shallow == 0 && moved == 0,
            $"under {worlds} towns the ground is opened for every tunnel ({cells} cells sampled, {closed} still solid), their cover kept ({shallow} too shallow), no cellar in the way ({moved}){(notes.Count > 0 ? "; " + string.Join("; ", notes.Take(4)) : "")}");
        Check(buried == 0 && unreached == 0 && tested > 0,
            $"walked in from the surface: {surfaced} ways in at the ground's level ({buried} not), {tested} ways and chambers reached from the first ({unreached} not){(buried > 0 ? "; " + string.Join("; ", notes.Where(k => k.StartsWith("way in")).Take(3)) : "")}");
        Check(spaces > 0, $"{spaces} spaces underground for creatures, {stations} stations, {outfalls} outfall culverts over {worlds} towns");
    }

    // ---- a blast's dead zone and ruin ring ---------------------------------------------------------

    async Task BlastChecks()
    {
        string forced = PlanetSurface.Poi.PoiSiting.ForceFamily;
        PlanetSurface.Poi.PoiSiting.ForceFamily = "blast_crater";
        try
        {
            var spec = PlanetSpec.Roll(61, Archetype.Temperate, 0);
            PlanetWorld w = PlanetWorld.Build(spec, Main.Catalog);
            PoiSite crater = w.Pois.FirstOrDefault(p => p.GroundZero != null);
            if (crater == null) { Fail("no blast site to check"); return; }
            var (gx, gz) = crater.GroundZero.Value;
            BlastZone zone = crater.Instance.Layout.Extras.Blast;
            float deadR = zone.Dead * 3f, fadeR = zone.Fade * 4f;
            float atZero = w.Palette.Deadness(gx, gz), atDead = w.Palette.Deadness(gx + deadR * 0.9, gz), past = w.Palette.Deadness(gx + fadeR + 20, gz);
            float mid = w.Palette.Deadness(gx + (deadR + fadeR) * 0.5, gz);
            Color dead = w.Palette.Natural(gx + deadR * 0.9, gz, 10f, 1f, out _), alive = w.Palette.Natural(gx + fadeR + 40, gz, 10f, 1f, out _);
            float Sat(Color c) => Math.Max(c.R, Math.Max(c.G, c.B)) - Math.Min(c.R, Math.Min(c.G, c.B));
            Check(atZero == 1f && atDead == 1f && past == 0f && mid > 0f && mid < 1f && Sat(dead) < Sat(alive),
                $"a blast's dead zone: the ground ash-grey to its dead radius ({atDead:F2}), fading back by its edge ({mid:F2} halfway, {past:F2} past it); saturation {Sat(dead):F2} dead against {Sat(alive):F2} alive");
            var ring = w.Infra.Ruins.Where(r => r.Blast > 0.05f).ToList();
            float strongest = ring.Count > 0 ? ring.Max(r => r.Blast) : 0f;
            int flattened = 0;
            foreach (FlavourRuin r in ring.Take(4))
            {
                PoiInstance inst = r.Spawn(w.Profile);
                if (inst.Blasted > 0f || r.Blast > 0.05f) flattened++;
            }
            Check(ring.Count >= 4 && strongest > 0.3f,
                $"a ring of flattened ruins round ground zero: {ring.Count} ruins took the blast (strongest {strongest:F2}), {flattened} spawned flattened outward");
        }
        finally
        {
            PlanetSurface.Poi.PoiSiting.ForceFamily = forced;
        }
        await Frames(1);
    }
}
