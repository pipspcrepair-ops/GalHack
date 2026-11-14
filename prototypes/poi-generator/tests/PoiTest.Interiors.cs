using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using PoiGenerator.Gen;
using NV2 = System.Numerics.Vector2;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>What one room of a place shows a person walking in, as the game furnishes it.</summary>
    sealed record RoomLook(PlacedModule Module, int Pieces, float Detour, bool Lit, bool Lamp, bool Focal, string Signature, (Gen.Finish, Rgb) Floor, (Gen.Finish, Rgb) Wall, string How, int Ways);

    /// <summary>
    /// The interiors of the place a sleeper walks into most early, the start town's houses, as the game shows them (the
    /// generator's rooms dressed by the decorations library through Gen/RoomDecor), on fresh spawns, ruined and abandoned.
    /// Each room is checked for what makes an interior read as generated: an empty room; furniture in the walking line (a
    /// detour of more than a metre between two of its ways in); a room with no light (no lamp burning and no daylight); two
    /// rooms of a house alike (purpose, size, pieces, floor and walls); a room with nothing it is arranged round (its focal point:
    /// the roles the arrangements give its purpose, or its biggest piece); and per house, one material wall to wall (every
    /// room the same floor colour and the same wall colour, or more than half the ways between rooms changing nothing).
    /// </summary>
    void Interiors()
    {
        DecoPoi decor = PoiView.Decor;
        if (decor == null) { Fail("Interiors: the decorations library did not load"); return; }
        var rand = new Random();
        FamilyDef fam = _lib.Families.First(f => f.Id == "flavour_house");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int houses = 0, rooms = 0, empty = 0, lined = 0, inLine = 0, unlit = 0, lamps = 0, unfocused = 0, alike = 0, alikeHouses = 0, oneMaterial = 0;
        var firsts = new Dictionary<string, string>();
        void Note(string kind, string what) => firsts.TryAdd(kind, what);
        var weak = new Dictionary<string, int>();
        for (int i = 0; i < 24; i++)
        {
            var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3), null, new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble()));
            Layout layout = PoiFactory.SpawnLayout(_lib, fam, rand.Next(fam.Variants.Count), Rarity.Common, (ulong)rand.NextInt64(), hints, out string why);
            if (layout == null) { Fail($"Interiors: a house would not lay out: {why}"); continue; }
            PoiInstance inst = PoiFactory.Spawn(_lib, layout, i % 2 == 0 ? PoiState.Ruined : PoiState.Abandoned, (ulong)rand.NextInt64());
            houses++;
            string house = $"{layout.Extras.Homes.FirstOrDefault()?.Silhouette} {inst.State}";
            List<RoomLook> looks = LookRooms(inst, decor);
            foreach (RoomLook r in looks)
            {
                rooms++;
                string at = $"{r.Module.Purpose} on storey {r.Module.Storey} of a {house}, {r.How}";
                void Weak(string kind) { weak[$"{r.Module.Purpose} {kind}"] = weak.GetValueOrDefault($"{r.Module.Purpose} {kind}") + 1; }
                if (r.Pieces == 0) { empty++; Weak(r.How.StartsWith("dressed") ? "empty though dressed" : "empty, undressed"); Note("empty", at); }
                if (!float.IsNaN(r.Detour))
                {
                    lined++;
                    if (r.Detour > 1f) { inLine++; Weak("in the way"); Note("line", $"{at}: {(float.IsPositiveInfinity(r.Detour) ? "no way through" : $"{r.Detour:0.0} m round")} ({r.Signature})"); }
                }
                if (!r.Lit) { unlit++; Weak("unlit"); Note("unlit", at); }
                if (r.Lamp) lamps++;
                if (!r.Focal) { unfocused++; Weak("no focal point"); Note("focal", $"{at}, {r.Ways} ways in ({r.Signature})"); }
            }
            int pairs = looks.SelectMany((a, k) => looks.Skip(k + 1).Where(b => a.Signature == b.Signature)).Count();
            alike += pairs;
            if (pairs > 0) { alikeHouses++; Note("alike", $"{house}: {looks.GroupBy(r => r.Signature).First(g => g.Count() > 1).Key}"); }
            // One material wall to wall: one floor and one wall colour all through, or the ways between rooms change nothing.
            var of = RoomDecor.ModuleOf(layout);
            var byModule = looks.ToDictionary(r => r.Module.Index);
            var joined = looks.SelectMany(r => RoomDecor.Ways(inst, r.Module, r.Module.Min.Y, of).Where(w => w.From > r.Module.Index && byModule.ContainsKey(w.From)).Select(w => (r, byModule[w.From]))).ToList();
            int same = joined.Count(p => p.Item1.Floor == p.Item2.Floor && p.Item1.Wall == p.Item2.Wall);
            bool oneColour = looks.Count > 1 && looks.Select(r => r.Floor.Item2).Distinct().Count() == 1 && looks.Select(r => r.Wall.Item2).Distinct().Count() == 1;
            if (oneColour || joined.Count > 0 && same * 2 > joined.Count)
            {
                oneMaterial++;
                Note("material", $"{house}: {(oneColour ? $"one floor colour and one wall colour in all {looks.Count} rooms" : $"{same} of {joined.Count} ways between rooms change nothing")}");
            }
        }
        string First(string kind) => firsts.TryGetValue(kind, out string s) ? $" (first: {s})" : "";
        System.Console.WriteLine($"Interiors: {houses} houses, {rooms} rooms in {sw.Elapsed.TotalSeconds:0.0} s; {lamps} rooms with a lamp burning; weakest: {string.Join(", ", weak.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} {k.Value}"))}");
        Check(empty == 0, $"Interiors: no empty rooms ({empty} of {rooms}){First("empty")}");
        Check(inLine == 0, $"Interiors: nothing stands in the walking line between a room's ways in ({inLine} of {lined} rooms with two or more ways in){First("line")}");
        Check(oneMaterial == 0, $"Interiors: no house one material wall to wall ({oneMaterial} of {houses}){First("material")}");
        Check(unlit == 0, $"Interiors: every room lit, by a lamp or daylight ({unlit} of {rooms} unlit){First("unlit")}");
        Check(alike == 0, $"Interiors: no two rooms of a house alike ({alike} pairs in {alikeHouses} of {houses} houses){First("alike")}");
        Check(unfocused == 0, $"Interiors: every room has a focal point ({unfocused} of {rooms} without){First("focal")}");
    }

    /// <summary>Every room of a spawned place as the game shows it: dressed by the library where it dresses it (its props then cleared), its own props elsewhere.</summary>
    static List<RoomLook> LookRooms(PoiInstance inst, DecoPoi decor)
    {
        Layout layout = inst.Layout;
        var dressed = RoomDecor.Rooms(inst).ToDictionary(d => d.Module.Index);
        var of = RoomDecor.ModuleOf(layout);
        PlaceLook look = inst.Blocks.Look ?? PlaceLook.For(inst);
        BlockGrid g = inst.Blocks;
        var nav = inst.Nav();
        var looks = new List<RoomLook>();
        foreach (PlacedModule m in layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall))
        {
            int y = m.Min.Y;
            var floor = m.Cells.Where(c => c.Y == y).ToList();
            if (floor.Count < 2) continue;
            var ways = RoomDecor.Ways(inst, m, y, of);
            int pieces;
            string how;
            float detour = float.NaN;
            var kinds = new List<string>();
            string[] focus = decor.Arrangements.Focus.GetValueOrDefault(m.Purpose);
            bool focal = m.Kind == ModuleKind.Hall && floor.Any(c => Dirs.Horizontal.Any(h => g.Get(c + h.Vec()).Type == BlockType.Stair));
            if (dressed.TryGetValue(m.Index, out RoomDecor.Dressable d))
            {
                DecoPlan found = decor.Dress(d.Room, inst.Kit?.Id), used = decor.Dress(d.Room, inst.Kit?.Id, disorder: false);
                RoomDecor.ClearProps(inst, d);
                pieces = found.Placements.Count;
                how = $"dressed as {d.Room.Recipe ?? d.Room.Set}";
                kinds.AddRange(used.Placements.Select(p => p.Piece));
                focal |= used.Placements.Any(p => focus != null ? focus.Contains(p.Role) || decor.Forge.Catalogue.Piece(p.Piece).Roles.Any(focus.Contains)
                    : p.Support < 0 && decor.Forge.Catalogue.Piece(p.Piece) is { } piece && piece.Width * piece.Depth >= 1f);
                int x0 = d.Corner.X, z0 = d.Corner.Z, x1 = x0 + (int)d.Room.Width - 1, z1 = z0 + (int)d.Room.Depth - 1;
                var doors = ways.Select(w => RoomDecor.Door(w.Cells, x0, x1, z0, z1)).OfType<DecoDoor>().ToList();
                if (doors.Count >= 2) detour = Detour(d.Room, doors, used.Placements.Where(p => DecoLayout.Blocks(decor.Forge, p)).Select(p => DecoLayout.Footprint(decor.Forge, p)));
            }
            else
            {
                var props = m.Cells.Where(c => (c.Y == y || c.Y == y + 1) && g.Get(c) is { IsAir: false } b && b.Role is Role.Prop or Role.Story && b.Type != BlockType.Scorch).ToList();
                pieces = props.Count;
                how = RoomDecor.Interior(inst, m) is { } inner ? $"undressed: {inner.Cells} open cells of {(inner.X1 - inner.X0 + 1) * (inner.Z1 - inner.Z0 + 1)}, {inner.Height} m clear" : "undressed: no floor";
                kinds.AddRange(props.Select(c => g.Get(c).Type.ToString()));
                focal |= props.Any(c => g.Get(c).Type is BlockType.Table or BlockType.Meal or BlockType.Bed or BlockType.Bunk or BlockType.Bench or BlockType.Machine or BlockType.Terminal);
                if (ways.Count >= 2) detour = CellDetour(floor, ways, c => BlockInfo.Collides(g.Get(c).Type) && !g.Get(c).IsAir);
            }
            int w0 = floor.Max(c => c.X) - floor.Min(c => c.X) + 1, d0 = floor.Max(c => c.Z) - floor.Min(c => c.Z) + 1;
            var floorLook = floor.Select(c => FloorLook(look, g, c)).GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
            var walls = floor.SelectMany(c => Dirs.Horizontal.Select(h => c + h.Vec() + Int3.Up).Where(w => g.Get(w) is { Role: Role.Wall } b && BlockInfo.IsFullCube(b.Type) || g.Get(w).Type is BlockType.Half or BlockType.Partition && g.Get(w).Role == Role.Wall)
                .Select(w => look.Pick(g.Get(w), Side.In, false, look.RoomAt(c)).Finish is var f ? (f, look.Colour(g.Get(w), Side.In, f, look.RoomAt(c))) : default)).ToList();
            var wallLook = walls.Count == 0 ? (Gen.Finish.Plain, new Rgb(0, 0, 0)) : walls.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
            // Lit: a lamp burning in it, a window in its walls, or the sky over it.
            bool lamp = inst.Lights.Any(l => l.Mode != LightMode.Dead && l.Cell.Y >= y && l.Cell.Y <= m.Max.Y + 1 && floor.Any(c => c.X == l.Cell.X && c.Z == l.Cell.Z));
            bool window = floor.Any(c => Dirs.Horizontal.Any(h => Enumerable.Range(1, 2).Any(k => g.Get(c + h.Vec() + Int3.Up * k) is { Type: BlockType.Pane, Role: Role.Window } or { IsAir: true } && !of.ContainsKey(c + h.Vec() + Int3.Up * k) && !of.ContainsKey(c + h.Vec() * 2 + Int3.Up * k))));
            bool sky = floor.Any(c => !Enumerable.Range(1, 12).Any(k => g.Get(c + Int3.Up * k) is { IsAir: false } b && BlockInfo.IsFullCube(b.Type)));
            string signature = $"{m.Purpose} {Math.Min(w0, d0)}x{Math.Max(w0, d0)} {floorLook.Item1}/{floorLook.Item2} {wallLook.Item1}/{wallLook.Item2}: {string.Join(" ", kinds.OrderBy(k => k))}";
            looks.Add(new RoomLook(m, pieces, detour, lamp || window || sky, lamp, focal, signature, floorLook, wallLook, how, ways.Count));
        }
        return looks;
    }

    /// <summary>A floor as it is drawn: its finish (the room's, by its purpose) and colour, on the plate laid in the cell or the slab under it.</summary>
    static (Gen.Finish, Rgb) FloorLook(PlaceLook look, BlockGrid g, Int3 c)
    {
        Block f = g.Get(c);
        Block b = f.Type == BlockType.Plate && f.Role == Role.Floor ? f : g.Get(c + Int3.Down);
        return (look.Pick(b, Side.Floor, false, look.RoomAt(c)).Finish, b.Color);
    }

    /// <summary>The worst detour furniture forces between two of a room's doors (metres; infinity if it shuts the way), for a walker 0.75 m across on a 0.25 m raster.</summary>
    static float Detour(DecoRoom room, List<DecoDoor> doors, IEnumerable<DecoRect> furniture)
    {
        const float cell = 0.25f;
        const int k = 3;
        var bare = new DecoRaster(room.Width, room.Depth, cell);
        var full = new DecoRaster(room.Width, room.Depth, cell);
        foreach (DecoRect r in room.Blocked) { bare.Add(r, 1); full.Add(r, 1); }
        foreach (DecoRect r in furniture) full.Add(r, 1);
        float worst = 0f;
        for (int a = 0; a < doors.Count; a++)
            for (int b = a + 1; b < doors.Count; b++)
            {
                int e = Steps(bare, room, doors[a], doors[b], k), f = Steps(full, room, doors[a], doors[b], k);
                if (e < 0) continue; // no way between them even bare: not furniture's doing
                worst = MathF.Max(worst, f < 0 ? float.PositiveInfinity : (f - e) * cell);
            }
        return worst;
    }

    /// <summary>The fewest raster steps for a k-cell walker from one door's opening to another's (-1: none).</summary>
    static int Steps(DecoRaster raster, DecoRoom room, DecoDoor from, DecoDoor to, int k)
    {
        bool[] fit = raster.Fits(k);
        var goal = new HashSet<int>(raster.DoorStarts(room, to, k));
        var dist = new Dictionary<int, int>();
        var q = new Queue<int>();
        foreach (int s in raster.DoorStarts(room, from, k))
            if (s >= 0 && s < fit.Length && fit[s] && dist.TryAdd(s, 0)) q.Enqueue(s);
        while (q.Count > 0)
        {
            int c = q.Dequeue();
            if (goal.Contains(c)) return dist[c];
            int i = c % raster.Nx, j = c / raster.Nx;
            foreach (var (ni, nj) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
            {
                if (ni < 0 || nj < 0 || ni >= raster.Nx || nj >= raster.Nz) continue;
                int n = nj * raster.Nx + ni;
                if (fit[n] && dist.TryAdd(n, dist[c] + 1)) q.Enqueue(n);
            }
        }
        return -1;
    }

    /// <summary>The same on a room's 1 m floor cells, for a room keeping the generator's own props.</summary>
    static float CellDetour(List<Int3> floor, List<(List<Int3> Cells, int From)> ways, Func<Int3, bool> blocked)
    {
        var cells = new HashSet<Int3>(floor);
        List<Int3> Inside((List<Int3> Cells, int From) w) => w.Cells.SelectMany(c => Dirs.Horizontal.Select(h => c + h.Vec())).Where(cells.Contains).ToList();
        int Steps(List<Int3> a, List<Int3> b, bool furnished)
        {
            var goal = new HashSet<Int3>(b);
            var dist = new Dictionary<Int3, int>();
            var q = new Queue<Int3>();
            foreach (Int3 s in a) if (!(furnished && blocked(s)) && dist.TryAdd(s, 0)) q.Enqueue(s);
            while (q.Count > 0)
            {
                Int3 c = q.Dequeue();
                if (goal.Contains(c)) return dist[c];
                foreach (Dir h in Dirs.Horizontal)
                {
                    Int3 n = c + h.Vec();
                    if (cells.Contains(n) && !(furnished && blocked(n)) && dist.TryAdd(n, dist[c] + 1)) q.Enqueue(n);
                }
            }
            return -1;
        }
        float worst = 0f;
        for (int a = 0; a < ways.Count; a++)
            for (int b = a + 1; b < ways.Count; b++)
            {
                List<Int3> pa = Inside(ways[a]), pb = Inside(ways[b]);
                int e = Steps(pa, pb, false), f = Steps(pa, pb, true);
                if (e < 0) continue;
                worst = MathF.Max(worst, f < 0 ? float.PositiveInfinity : f - e);
            }
        return worst;
    }
}
