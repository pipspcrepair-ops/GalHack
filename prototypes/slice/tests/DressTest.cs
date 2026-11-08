using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Places;
using NV2 = System.Numerics.Vector2;
using NV3 = System.Numerics.Vector3;

namespace Slice.Tests;

/// <summary>
/// Places as the dressing leaves them, over many places rolled fresh every run: every piece and every
/// bit of its debris rests on a floor and is drawn inside no block, and no two of its flat faces lie a
/// hair apart one over the other; rooms are furnished by what they are and what the place is for; and
/// every doorway and opening stays walkable for the player's capsule. No engine in play, only the
/// generator and the decorations library.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/DressTest.tscn
/// </summary>
public partial class DressTest : Node
{
    const int Places = 60;
    const double TimeLimitMinutes = 8;
    int _failures, _passes;
    readonly Dictionary<string, int> _kinds = new();
    int _flecks;
    static readonly List<(double Ms, string Name)> PlanMs = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(TimeLimitMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {TimeLimitMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    /// <summary>A rolled place, dressed as the game dresses it.</summary>
    sealed record Rolled(FamilyDef Family, PoiInstance Inst, PlaceMemory Memory, string Name);

    /// <summary>Places of families picked at random (or of the families named), each in a random variant and state, dressed.</summary>
    static IEnumerable<Rolled> Roll(int count, int seed, Func<FamilyDef, bool> only = null)
    {
        PoiLibrary lib = Slice.Surface.SurfaceZone.PoiCatalog.Library;
        var rng = new Random(seed);
        var families = lib.Families.Where(f => f.Variants.Count > 0 && (only == null || only(f))).ToList();
        for (int made = 0, tries = 0; made < count && tries < count * 4 && families.Count > 0; tries++)
        {
            FamilyDef f = families[rng.Next(families.Count)];
            int variant = rng.Next(f.Variants.Count);
            var state = (PoiState)rng.Next(4);
            ulong spawn = (ulong)rng.NextInt64();
            PoiInstance inst;
            try { inst = PoiFactory.Generate(lib, f, variant, f.Rarity, state, spawn); }
            catch (Exception) { continue; } // a place that can't lay itself out is left out of the world too
            var site = new PoiSite { Instance = inst, Family = f, Name = $"{f.Id}/{f.Variants[variant].Name} {state}" };
            var memory = new PlaceMemory(site);
            var watch = Stopwatch.StartNew();
            memory.Rooms = Decor.Shared.Plan(memory);
            PlanMs.Add((watch.Elapsed.TotalMilliseconds, site.Name));
            made++;
            yield return new Rolled(f, inst, memory, site.Name);
        }
    }

    void Run()
    {
        Decor decor = Decor.Shared;
        Check(decor != null, "the decorations library loads");
        if (decor == null) return;
        int seed = (int)(System.Environment.TickCount64 & 0x7fffffff);
        GD.Print($"rolling {Places} places from {seed}");
        var rolled = Roll(Places, seed).ToList();
        Resting(decor, rolled);
        // Mines and airfields every run, beside whatever the dice brought.
        rolled.AddRange(Roll(16, seed + 1, f => f.Tags.Contains("mine") || f.Tags.Contains("aviation")));
        Fitting(decor, rolled);
        Walkable(decor, rolled);
        GD.Print($"dressing took {PlanMs.Average(p => p.Ms):F0} ms a place on average, {PlanMs.Max(p => p.Ms):F0} ms at most ({PlanMs.MaxBy(p => p.Ms).Name})");
    }

    /// <summary>Everything a piece lays on the floor lies on floor, nothing is drawn in a block, no flat faces flicker.</summary>
    void Resting(Decor decor, List<Rolled> rolled)
    {
        int rooms = 0, pieces = 0;
        var floating = new List<string>();
        var sunk = new List<string>();
        var coplanar = new List<string>();
        foreach (Rolled r in rolled)
            foreach (PlaceRoom room in r.Memory.Rooms.Values)
            {
                rooms++;
                HashSet<int> taken = r.Memory.DecoTaken.GetValueOrDefault(room.Module) ?? new HashSet<int>();
                var flats = new List<(int Piece, float Y, NV3 A, NV3 B, NV3 C, int Surface, System.Numerics.Vector4 Col)>();
                for (int i = 0; i < room.Plan.Placements.Count; i++)
                {
                    if (taken.Contains(i)) continue;
                    DecoPlacement pl = room.Plan.Placements[i];
                    DecoPiece piece = decor.Catalogue.Piece(pl.Piece);
                    pieces++;
                    DecoMeshData mesh = decor.Forge.Mesh(pl.Look);
                    bool onFloor = pl.Support < 0 && piece.Mount is DecoMount.Floor or DecoMount.Top;
                    float floor = room.Corner.Y + room.Floor;
                    string bad = null;
                    for (int si = 0; si < mesh.Surfaces.Length; si++)
                    {
                        DecoSurfaceData s = mesh.Surfaces[si];
                        for (int t = 0; t + 2 < s.Positions.Count; t += 3)
                        {
                            NV3 a = At(room, pl, s.Positions[t]), b = At(room, pl, s.Positions[t + 1]), c = At(room, pl, s.Positions[t + 2]);
                            if (s.Normals[t].Y > 0.999f && (a.Y + b.Y + c.Y) / 3f - floor is > 0.002f and < 0.12f) flats.Add((i, (a.Y + b.Y + c.Y) / 3f, a, b, c, si, s.Colors[t]));
                            if (bad != null) continue;
                            foreach (NV3 v in new[] { a, b, c })
                            {
                                // A hair in toward the piece's middle: a face flush with a wall isn't in it.
                                var flat = new NV2(v.X, v.Z);
                                NV2 inward = new NV2(room.Corner.X + pl.X, room.Corner.Z + pl.Z) - flat;
                                flat += new NV2(MathF.Sign(inward.X) * MathF.Min(0.01f, MathF.Abs(inward.X)), MathF.Sign(inward.Y) * MathF.Min(0.01f, MathF.Abs(inward.Y)));
                                var cell = new PoiGenerator.Gen.Int3((int)MathF.Floor(flat.X), (int)MathF.Floor(MathF.Max(v.Y, floor) + 0.005f), (int)MathF.Floor(flat.Y));
                                Block here = r.Inst.Blocks.Get(cell);
                                if (!here.IsAir && !(here.Type == BlockType.Plate && cell.Y == room.Corner.Y) && !(here.Type is BlockType.Sign or BlockType.Cable or BlockType.Vine))
                                {
                                    bad = $"in a {here.Type} ({here.Role})";
                                    sunk.Add($"{r.Name}, {room.Room.RoomId}: {pl} {bad} at {cell} [vertex {v}, corner {room.Corner}, floor {room.Floor}, support {pl.Support}, mount {piece.Mount}, taken {taken.Count}]");
                                    break;
                                }
                                if (!onFloor || v.Y - floor > 0.03f) continue;
                                if (SurfaceAt(r.Inst, cell with { Y = room.Corner.Y }) is not float under || MathF.Abs(under - floor) > 0.01f)
                                {
                                    Block below = r.Inst.Blocks.Get(cell with { Y = room.Corner.Y - 1 });
                                    bad = $"over {(below.IsAir ? "nothing" : below.Type.ToString())}";
                                    floating.Add($"{r.Name}, {room.Room.RoomId}: {pl} {bad} at {cell}");
                                    break;
                                }
                            }
                            if (bad != null) _kinds[bad] = _kinds.GetValueOrDefault(bad) + 1;
                        }
                    }
                }
                // Flat faces of different shades a hair apart, one over the other, flicker (a face flat on the floor is a base, hidden under what it carries).
                for (int p = 0; p < flats.Count; p++)
                    for (int q = p + 1; q < flats.Count; q++)
                    {
                        float dy = MathF.Abs(flats[p].Y - flats[q].Y);
                        if (dy > 0.002f || !Differ(flats[p].Col, flats[q].Col) || !Overlap(flats[p].A, flats[p].B, flats[p].C, flats[q].A, flats[q].B, flats[q].C)) continue;
                        // Two faces of one piece's own model above the floor marks (its scorch, stains, glass and the
                        // first of its fragments lie lowest) are the model's own detail: counted, not failed here.
                        // A fleck under 5 cm2 is no flicker anyone sees either.
                        if (flats[p].Piece == flats[q].Piece && (flats[p].Y - room.Corner.Y - room.Floor > 0.02f
                            || MathF.Min(Area(flats[p].A, flats[p].B, flats[p].C), Area(flats[q].A, flats[q].B, flats[q].C)) < 5e-4f)) { _flecks++; continue; }
                        string k = flats[p].Piece == flats[q].Piece ? $"within a {room.Plan.Placements[flats[p].Piece].State} {room.Plan.Placements[flats[p].Piece].Piece} ({(DecoSurface)flats[p].Surface}/{(DecoSurface)flats[q].Surface})" : "between pieces";
                        _kinds["flat faces " + k] = _kinds.GetValueOrDefault("flat faces " + k) + 1;
                        coplanar.Add($"{r.Name}, {room.Room.RoomId}: {room.Plan.Placements[flats[p].Piece]} and {room.Plan.Placements[flats[q].Piece]}, {dy * 1000:F2} mm apart, {flats[p].Y - room.Corner.Y - room.Floor:F3} up, colours {flats[p].Col} {flats[q].Col}, areas {Area(flats[p].A, flats[p].B, flats[p].C) * 1e4:F0} {Area(flats[q].A, flats[q].B, flats[q].C) * 1e4:F0} cm2");
                    }
            }
        GD.Print($"{rolled.Count} places, {rooms} rooms, {pieces} pieces ({_flecks} pairs of a model's own faces a hair apart, above its floor marks or flecks)");
        foreach (var kv in _kinds.OrderByDescending(k => k.Value).Take(20)) GD.Print($"  {kv.Key}: {kv.Value}");
        foreach (string s in floating.Take(4).Concat(sunk.Take(4)).Concat(coplanar.Where((_, n) => n % 97 == 0).Take(14))) GD.Print("  e.g. " + s);
        Check(floating.Count == 0, $"every piece, and the debris and ash it lays, lies on a floor at its room's height ({floating.Count} over a hole, a step or a ledge)");
        Check(sunk.Count == 0, $"no piece is drawn inside a block ({sunk.Count} are)");
        Check(coplanar.Count == 0, $"no two flat faces near the floor lie a hair apart one over the other ({coplanar.Count} pairs)");
    }

    /// <summary>The words of a room id that say it is an office.</summary>
    static readonly string[] OfficeWords = { "office", "archive", "lobby", "monitor", "counting", "reception" };

    static string[] Words(string id) => (id ?? "").ToLowerInvariant().Replace("'s", "").Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

    /// <summary>
    /// What a room holds comes from the place's function and the room's role: office furniture only where
    /// the room is an office; a mine's rooms from the mine (rock, tools, carts, blasting stores); an
    /// airfield's hangars with engines on stands, tugs and benches; an aircraft's cabin with seats and a galley.
    /// </summary>
    void Fitting(Decor decor, List<Rolled> rolled)
    {
        var officeOut = new List<string>();
        var mineOff = new List<string>();
        var hangars = new List<(string Where, bool Fits, bool Craft)>();
        var cabins = new List<(string Where, bool Fits)>();
        var seen = new SortedDictionary<string, int>();
        foreach (Rolled r in rolled)
        {
            bool culture = r.Inst.Kit?.Id is "precursor" or "vharn" or "kessari" || (r.Family.Style ?? "").StartsWith("alien_", StringComparison.Ordinal);
            foreach (PlaceRoom room in r.Memory.Rooms.Values)
            {
                string[] words = Words(room.Room.RoomId);
                string how = room.Room.Recipe ?? room.Room.Set;
                string key = $"{room.Room.RoomId} in {r.Family.Id}: {how}";
                seen[key] = seen.GetValueOrDefault(key) + 1;
                HashSet<int> taken = r.Memory.DecoTaken.GetValueOrDefault(room.Module) ?? new HashSet<int>();
                var pieces = room.Plan.Placements.Where((_, i) => !taken.Contains(i)).Select(pl => decor.Catalogue.Piece(pl.Piece)).ToList();
                bool officeRole = words.Any(OfficeWords.Contains);
                // Office furniture of the office set's own, as a room's set: only in a room that is an office.
                if (room.Room.Set == "office" && !officeRole) officeOut.Add($"{r.Name}: '{room.Room.RoomId}' furnished as an office");
                if (culture) continue;
                if ((r.Family.Tags.Contains("mine") || r.Family.Tags.Contains("ore")) && !officeRole)
                {
                    var desks = pieces.Where(p => p.Set == "office").Select(p => p.Id).Distinct().ToList();
                    if (desks.Count > 0) mineOff.Add($"{r.Name}: '{room.Room.RoomId}' ({how}) holds {string.Join(", ", desks)}");
                }
                if (r.Family.Tags.Contains("aviation") && words.Any(w => w is "hangar" or "workshop" or "hall") && room.Room.Width * room.Room.Depth >= 60)
                    hangars.Add(($"{r.Name}: '{room.Room.RoomId}' {room.Room.Width:F0}x{room.Room.Depth:F0} ({string.Join(", ", pieces.Select(p => p.Id).Distinct().Take(8))})",
                        how == "hangar" && pieces.All(p => p.Set != "office") && pieces.Any(p => p.Has("bench") || p.Has("toolrack") || p.Id is "engine-stand" or "aircraft-tug"),
                        pieces.Any(p => p.Id is "engine-stand" or "aircraft-tug")));
                if (words.Contains("aircraft") || words.Contains("cabin"))
                    cabins.Add(($"{r.Name}: '{room.Room.RoomId}' as {how} ({string.Join(", ", pieces.Select(p => p.Id).Distinct().Take(8))})",
                        how == "aircraft-cabin" && pieces.Any(p => p.Has("airline-seat")) && pieces.All(p => p.Set != "office")));
            }
        }
        foreach (string s in officeOut.Take(4).Concat(mineOff.Take(4)).Concat(hangars.Where(h => !h.Fits).Select(h => h.Where).Take(4)).Concat(cabins.Where(c => !c.Fits).Select(c => c.Where).Take(4))) GD.Print("  e.g. " + s);
        Check(officeOut.Count == 0, $"only a room that is an office is furnished as one ({officeOut.Count} others)");
        Check(mineOff.Count == 0, $"no desk, filing cabinet or bookshelf down a mine but in its offices ({mineOff.Count} rooms)");
        Check(hangars.Count > 0 && hangars.All(h => h.Fits), $"an airfield's hangars are furnished as hangars: benches, tool racks, engines, tugs, no office furniture ({hangars.Count(h => h.Fits)} of {hangars.Count})");
        Check(hangars.Count(h => h.Craft) >= hangars.Count * 0.7f, $"and most still have an engine on its stand or a tug in them, whatever the years carried off ({hangars.Count(h => h.Craft)} of {hangars.Count})");
        Check(cabins.All(c => c.Fits), $"an aircraft's cabin holds rows of seats and no office furniture ({cabins.Count(c => c.Fits)} of {cabins.Count})");
    }

    /// <summary>
    /// After dressing, every doorway and every opening between rooms that a person could reach on the bare
    /// blocks they still reach, as the player's capsule: walking cell to cell from the way in, a cell's middle
    /// and the step between two must clear every solid piece's collision boxes by the capsule's radius.
    /// </summary>
    void Walkable(Decor decor, List<Rolled> rolled)
    {
        const float Radius = Slice.Player.Survivor.Radius;
        var cut = new List<string>();
        int ways = 0;
        foreach (Rolled r in rolled)
        {
            PoiInstance inst = r.Inst;
            NavGrid nav = inst.Nav();
            PoiGenerator.Gen.Int3 entry = inst.Layout.EntranceCell;
            var bare = nav.Reachable(entry);
            // The solid pieces' collision boxes, seen from above, with the floor height they stand on.
            var boxes = new List<(NV2[] Hull, int Level, float Lo, float Hi, string What)>();
            foreach (PlaceRoom room in r.Memory.Rooms.Values)
                foreach (int i in room.Solid)
                {
                    if (r.Memory.DecoTaken.GetValueOrDefault(room.Module)?.Contains(i) == true) continue;
                    DecoPlacement pl = room.Plan.Placements[i];
                    foreach (DecoBox box in decor.Forge.Mesh(pl.Look).Boxes)
                    {
                        var corners = box.Corners().Select(c => At(room, pl, c)).ToList();
                        float lo = corners.Min(c => c.Y) - room.Corner.Y - room.Floor, hi = corners.Max(c => c.Y) - room.Corner.Y - room.Floor;
                        // What a person steps over or walks under doesn't stop them.
                        if (hi < 0.35f || lo > 1.8f) continue;
                        boxes.Add((Hull(corners.Select(c => new NV2(c.X, c.Z)).ToList()), room.Corner.Y, lo, hi, $"{pl} in {room.Room.RoomId} [{room.Module}] corner {room.Corner}"));
                    }
                }
            bool Clear(PoiGenerator.Gen.Int3 cell, NV2 at) => !boxes.Any(b => b.Level == cell.Y && Distance(b.Hull, at) < Radius);
            NV2 Middle(PoiGenerator.Gen.Int3 c) => new(c.X + 0.5f, c.Z + 0.5f);
            // Walking it as the capsule: cell to cell, each middle and each step between clear.
            var reached = new HashSet<PoiGenerator.Gen.Int3> { entry };
            var queue = new Queue<PoiGenerator.Gen.Int3>();
            queue.Enqueue(entry);
            while (queue.Count > 0)
            {
                PoiGenerator.Gen.Int3 c = queue.Dequeue();
                foreach (PoiGenerator.Gen.Int3 n in nav.Neighbours(c))
                {
                    if (reached.Contains(n)) continue;
                    if (!Clear(n, Middle(n)) || n.Y == c.Y && !Clear(n, (Middle(n) + Middle(c)) / 2f)) continue;
                    reached.Add(n);
                    queue.Enqueue(n);
                }
            }
            var routed = Place.NavOf(r.Memory).Reachable(entry);
            // The ways that must stay open: every doorway's cells and the cells where rooms open into each other (both sides walkable).
            var must = new HashSet<PoiGenerator.Gen.Int3>();
            foreach (Doorway d in inst.Layout.Doorways)
                foreach (PoiGenerator.Gen.Int3 c in d.Cells) must.Add(c);
            foreach (PoiGenerator.Gen.Int3 c in inst.Layout.ExitCells) must.Add(c);
            var of = new Dictionary<PoiGenerator.Gen.Int3, int>();
            foreach (PlacedModule m in inst.Layout.Modules)
                foreach (PoiGenerator.Gen.Int3 c in m.Cells) of.TryAdd(c, m.Index);
            foreach (var (c, mi) in of)
                foreach (PoiGenerator.Gen.Int3 n in new[] { c + new PoiGenerator.Gen.Int3(1, 0, 0), c + new PoiGenerator.Gen.Int3(0, 0, 1) })
                    if (of.TryGetValue(n, out int ni) && ni != mi && bare.Contains(c) && bare.Contains(n)) { must.Add(c); must.Add(n); }
            foreach (PoiGenerator.Gen.Int3 c in must)
            {
                if (!bare.Contains(c)) continue;
                ways++;
                if (!reached.Contains(c)) cut.Add($"{r.Name}: {c} ({inst.Layout.Modules[of.GetValueOrDefault(c)].Purpose}) can't be reached past the dressing [routed {routed.Contains(c)}, by {boxes.Where(b => b.Level == c.Y).OrderBy(b => Distance(b.Hull, Middle(c))).Select(b => b.What).FirstOrDefault()}]");
            }
        }
        foreach (string s in cut.Take(6)) GD.Print("  e.g. " + s);
        Check(cut.Count == 0, $"every doorway and opening reachable on the bare blocks is reachable by the player's capsule after dressing ({ways - cut.Count} of {ways})");
    }

    /// <summary>The convex hull of points (gift wrapping: a box's eight corners seen from above).</summary>
    static NV2[] Hull(List<NV2> pts)
    {
        var hull = new List<NV2>();
        NV2 start = pts.OrderBy(p => p.X).ThenBy(p => p.Y).First(), at = start;
        do
        {
            hull.Add(at);
            NV2 next = pts[0] == at ? pts[1] : pts[0];
            foreach (NV2 q in pts)
            {
                float cross = (next.X - at.X) * (q.Y - at.Y) - (next.Y - at.Y) * (q.X - at.X);
                if (cross < 0 || cross == 0 && NV2.DistanceSquared(at, q) > NV2.DistanceSquared(at, next)) next = q;
            }
            at = next;
        } while (at != start && hull.Count <= pts.Count);
        return hull.ToArray();
    }

    /// <summary>How far a point lies outside a convex polygon (0 inside).</summary>
    static float Distance(NV2[] poly, NV2 p)
    {
        if (poly.Length < 3) return poly.Length == 0 ? float.MaxValue : poly.Min(q => NV2.Distance(q, p));
        bool inside = true;
        float best = float.MaxValue;
        for (int i = 0; i < poly.Length; i++)
        {
            NV2 a = poly[i], b = poly[(i + 1) % poly.Length], e = b - a;
            float cross = e.X * (p.Y - a.Y) - e.Y * (p.X - a.X);
            if (cross < 0) inside = false;
            float t = Math.Clamp(NV2.Dot(p - a, e) / MathF.Max(e.LengthSquared(), 1e-9f), 0f, 1f);
            best = MathF.Min(best, NV2.Distance(a + e * t, p));
        }
        return inside ? 0f : best;
    }

    /// <summary>The top of what's underfoot in a walk-level cell: the ground or a full block under it, or a floor plate on that; null where there's none.</summary>
    static float? SurfaceAt(PoiInstance inst, PoiGenerator.Gen.Int3 c)
    {
        Block here = inst.Blocks.Get(c), under = inst.Blocks.Get(c + PoiGenerator.Gen.Int3.Down);
        bool firm = under.IsAir ? inst.Nav().IsRock(c + PoiGenerator.Gen.Int3.Down) : BlockInfo.IsFullCube(under.Type) || under.Type == BlockType.Lattice;
        if (!firm) return null;
        return here.Type == BlockType.Plate ? c.Y + 0.1f : c.Y;
    }

    /// <summary>Whether two faces' colours differ enough to show which one is drawn (one shade over another flickers; the same shade can't).</summary>
    static bool Differ(System.Numerics.Vector4 a, System.Numerics.Vector4 b) => MathF.Max(MathF.Abs(a.X - b.X), MathF.Max(MathF.Abs(a.Y - b.Y), MathF.Abs(a.Z - b.Z))) > 0.06f;

    static float Area(NV3 a, NV3 b, NV3 c) => NV3.Cross(b - a, c - a).Length() / 2f;

    static NV3 At(PlaceRoom room, DecoPlacement pl, NV3 v)
    {
        NV3 p = NV3.Transform(v, pl.Transform);
        return new NV3(room.Corner.X + p.X, room.Corner.Y + room.Floor + p.Y, room.Corner.Z + p.Z);
    }

    /// <summary>Whether two flat triangles overlap seen from above (by more than a sliver).</summary>
    static bool Overlap(NV3 a0, NV3 a1, NV3 a2, NV3 b0, NV3 b1, NV3 b2)
    {
        var a = new[] { new NV2(a0.X, a0.Z), new NV2(a1.X, a1.Z), new NV2(a2.X, a2.Z) };
        var b = new[] { new NV2(b0.X, b0.Z), new NV2(b1.X, b1.Z), new NV2(b2.X, b2.Z) };
        // Separating axes: the six edge normals, with a 2 mm allowance.
        foreach (var poly in new[] { a, b })
            for (int i = 0; i < 3; i++)
            {
                NV2 e = poly[(i + 1) % 3] - poly[i];
                var n = new NV2(-e.Y, e.X);
                float len = n.Length();
                if (len < 1e-6f) return false;
                n /= len;
                float amin = a.Min(v => NV2.Dot(v, n)), amax = a.Max(v => NV2.Dot(v, n));
                float bmin = b.Min(v => NV2.Dot(v, n)), bmax = b.Max(v => NV2.Dot(v, n));
                if (amax < bmin + 0.002f || bmax < amin + 0.002f) return false;
            }
        return true;
    }
}
