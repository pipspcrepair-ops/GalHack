using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Items;
using PoiGenerator.Gen;
using NV2 = System.Numerics.Vector2;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>What one room of a place shows a person walking in, as the game furnishes it.</summary>
    sealed record RoomLook(PlacedModule Module, int Pieces, float Detour, bool Lit, bool Lamp, bool Focal, string Signature, (Gen.Finish, Rgb) Floor, (Gen.Finish, Rgb) Wall, string How, int Ways)
    {
        /// <summary>A room of 24 mÂ² or more, and whether its middle is bare (a third of it with nothing within 1.5 m).</summary>
        public bool Big, Sparse;
        /// <summary>Its ceiling as you look up: its heights over the floor, what it is made of, and any beams or pipes under it.</summary>
        public string Ceiling = "";
        public bool Flat;
        /// <summary>Its ways to another room a door's width or less, and how many have no door.</summary>
        public int Narrow, Doorless;
        /// <summary>Ruined: what it was arranged round is down or gone; its middle has more of the fallen in it than its edges; most of it is open to the sky.</summary>
        public bool FocalDown, MidDebris, Sky;
        /// <summary>The room's rules (DecoRules): pieces taller than a sill before a window, fires off a chimney breast and fires lit at all,
        /// what it is arranged round against an opening, pieces whose use side no one walks to, a narrow room off its one wall, over half the floor stood on.</summary>
        public int Window, OffFlue, Fires, Unreached;
        public bool FocalWall, OffWall, Crowded;
    }

    /// <summary>The places checked: the one a sleeper walks into most early, then the next most visited, each by its families and spawns a run.</summary>
    static readonly (string Type, string[] Families, int Spawns)[] InteriorTypes =
    {
        ("house", new[] { "flavour_house" }, 24),
        ("ominous tower", new[] { "ominous_tower" }, 3),
        ("city offices and civic", new[] { "city_tower", "city_midrise", "city_civic", "city_police", "city_fire", "city_school", "city_university" }, 7),
        ("military", new[] { "meridian_garrison", "military_base", "command_bunker" }, 6),
        ("industrial halls", new[] { "abandoned_factory", "city_works" }, 6),
        ("hospital", new[] { "hospital" }, 4),
    };

    /// <summary>
    /// The interiors of the places a sleeper walks into most (the start town's houses, then the Ominous Tower, a city's offices
    /// and civic buildings, military places, industrial halls and hospitals) as the game shows them (the generator's rooms
    /// dressed by the decorations library through Gen/RoomDecor), on fresh spawns, ruined and abandoned. Each room is checked
    /// for what makes an interior read as generated: an empty room; furniture in the walking line (a detour of more than a metre
    /// between two of its ways in); a room with no light; two rooms of a storey alike (purpose, size, pieces, floor and walls); a
    /// room with nothing it is arranged round (its focal point); per place, one material wall to wall. And the weak points the
    /// owner saw: a big room with a bare middle (no second group, rug or divider), a door's-width way between rooms with no
    /// door in it, a ceiling flat all through a place, and in a ruin, a room whose focal point is down or gone, its fallen
    /// heaped across the middle, or mostly open sky. Numbers per type.
    /// </summary>
    void Interiors()
    {
        DecoPoi decor = PoiView.Decor;
        if (decor == null) { Fail("Interiors: the decorations library did not load"); return; }
        var rand = new Random();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var (type, families, spawns) in InteriorTypes)
        {
            int places = 0, rooms = 0, empty = 0, lined = 0, inLine = 0, unlit = 0, lamps = 0, unfocused = 0, alike = 0, oneMaterial = 0;
            int big = 0, sparse = 0, narrow = 0, doorless = 0, flat = 0, oneCeiling = 0, ruined = 0, focalDown = 0, midDebris = 0, sky = 0;
            int window = 0, offFlue = 0, fires = 0, focalWall = 0, unreached = 0, lining = 0, crowded = 0;
            var firsts = new Dictionary<string, string>();
            void Note(string kind, string what) => firsts.TryAdd(kind, what);
            var weak = new Dictionary<string, int>();
            for (int i = 0; i < spawns; i++)
            {
                FamilyDef fam = _lib.Families.First(f => f.Id == families[i % families.Length]);
                var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3), null, fam.Tags.Contains("house") ? new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble()) : null);
                Layout layout = PoiFactory.SpawnLayout(_lib, fam, rand.Next(fam.Variants.Count), fam.Rarity, (ulong)rand.NextInt64(), hints, out string why);
                if (layout == null) { Fail($"Interiors: a {type} ({fam.Id}) would not lay out: {why}"); continue; }
                PoiState state = i % 2 == 0 ? PoiState.Ruined : PoiState.Abandoned;
                PoiInstance inst = PoiFactory.Spawn(_lib, layout, state, (ulong)rand.NextInt64());
                places++;
                string place = layout.Extras.Homes.FirstOrDefault()?.Silhouette is { } s ? $"{s} {inst.State}" : $"{fam.Id} {inst.State}";
                List<RoomLook> looks = LookRooms(inst, decor);
                foreach (RoomLook r in looks)
                {
                    rooms++;
                    string at = $"{r.Module.Purpose} on storey {r.Module.Storey} of a {place}, {r.How}";
                    void Weak(string kind) { weak[$"{r.Module.Purpose} {kind}"] = weak.GetValueOrDefault($"{r.Module.Purpose} {kind}") + 1; }
                    if (r.Pieces == 0) { empty++; Weak(r.How.StartsWith("dressed") ? "empty though dressed" : "empty, undressed"); Note("empty", at); }
                    if (!float.IsNaN(r.Detour))
                    {
                        lined++;
                        if (r.Detour > 1f) { inLine++; Weak("in the way"); Note("line", $"{at}: {(float.IsPositiveInfinity(r.Detour) ? "no way through" : $"{r.Detour:0.0} m round")} ({r.Signature})"); }
                    }
                    // (Whether a lamp still burns is the state's: a ruin goes dark, deep levels blacker, by design (Decay). Each room has its own hung.)
                    if (!r.Lit) { unlit++; Weak("unlit"); Note("unlit", at); }
                    if (r.Lamp) lamps++;
                    if (!r.Focal) { unfocused++; Weak("no focal point"); Note("focal", $"{at}, {r.Ways} ways in ({r.Signature})"); }
                    if (r.Big) big++;
                    if (r.Sparse) { sparse++; Weak("bare middle"); Note("sparse", $"{at} ({r.Signature})"); }
                    narrow += r.Narrow;
                    doorless += r.Doorless;
                    if (r.Doorless > 0) Note("door", $"{at}: {r.Doorless} of {r.Narrow} ways a door's width with no door");
                    if (r.Flat) flat++;
                    window += r.Window; offFlue += r.OffFlue; fires += r.Fires; unreached += r.Unreached;
                    if (r.Window > 0) Note("window", $"{at} ({r.Signature})");
                    if (r.OffFlue > 0) Note("flue", at);
                    if (r.FocalWall) { focalWall++; Note("focal wall", at); }
                    if (r.Unreached > 0) Note("reach", $"{at} ({r.Signature})");
                    if (r.OffWall) { lining++; Note("narrow", $"{at} ({r.Signature})"); }
                    if (r.Crowded) { crowded++; Note("crowded", $"{at} ({r.Signature})"); }
                    if (state == PoiState.Ruined)
                    {
                        ruined++;
                        if (r.FocalDown) { focalDown++; Weak("focal down in a ruin"); Note("down", at); }
                        if (r.MidDebris) { midDebris++; Weak("fallen across the middle"); Note("debris", at); }
                        if (r.Sky) { sky++; Weak("mostly sky"); Note("sky", at); }
                    }
                }
                // Alike: two rooms of a storey the same in purpose, size, pieces, floor and walls.
                int pairs = looks.GroupBy(r => r.Module.Storey).Sum(st => st.SelectMany((a, k) => st.Skip(k + 1).Where(b => a.Signature == b.Signature)).Count());
                alike += pairs;
                if (pairs > 0) Note("alike", $"{place}: {looks.GroupBy(r => (r.Module.Storey, r.Signature)).First(g => g.Count() > 1).Key.Signature}");
                if (looks.Count > 1 && looks.Select(r => r.Ceiling).Distinct().Count() == 1) { oneCeiling++; Note("ceiling", $"{place}: every room's ceiling {looks[0].Ceiling}"); }
                // One material wall to wall: one floor and one wall colour all through, or the ways between rooms change nothing.
                var of = RoomDecor.ModuleOf(layout);
                var byModule = looks.ToDictionary(r => r.Module.Index);
                var joined = looks.SelectMany(r => RoomDecor.Ways(inst, r.Module, r.Module.Min.Y, of).Where(w => w.From > r.Module.Index && byModule.ContainsKey(w.From)).Select(w => (r, byModule[w.From]))).ToList();
                int same = joined.Count(p => p.Item1.Floor == p.Item2.Floor && p.Item1.Wall == p.Item2.Wall);
                bool oneColour = looks.Count > 1 && looks.Select(r => r.Floor.Item2).Distinct().Count() == 1 && looks.Select(r => r.Wall.Item2).Distinct().Count() == 1;
                if (oneColour || joined.Count > 0 && same * 2 > joined.Count)
                {
                    oneMaterial++;
                    Note("material", $"{place}: {(oneColour ? $"one floor colour and one wall colour in all {looks.Count} rooms" : $"{same} of {joined.Count} ways between rooms change nothing")}");
                }
            }
            string First(string kind) => firsts.TryGetValue(kind, out string s) ? $" (first: {s})" : "";
            string T(string what) => $"Interiors, {type}: {what}";
            System.Console.WriteLine($"Interiors, {type}: {places} places, {rooms} rooms; {lamps} with a lamp burning; big rooms {big}, bare middle {sparse}; narrow ways {narrow}, no door {doorless}; flat ceilings {flat} of {rooms}, places one ceiling all through {oneCeiling}; "
                + $"ruined rooms {ruined}: focal down {focalDown}, fallen across the middle {midDebris}, mostly sky {sky}; before a window {window}, fires {fires} ({offFlue} off a breast), focal on an opening {focalWall}, cut off {unreached}, narrow off one wall {lining}, over half the floor {crowded}; weakest: {string.Join(", ", weak.OrderByDescending(k => k.Value).Take(6).Select(k => $"{k.Key} {k.Value}"))}");
            Check(empty == 0, T($"no empty rooms ({empty} of {rooms}){First("empty")}"));
            Check(inLine == 0, T($"nothing stands in the walking line between a room's ways in ({inLine} of {lined} rooms with two or more ways in){First("line")}"));
            Check(oneMaterial == 0, T($"no place one material wall to wall ({oneMaterial} of {places}){First("material")}"));
            Check(unlit == 0, T($"every room has its own lamp or daylight ({unlit} of {rooms} without){First("unlit")}"));
            Check(alike == 0, T($"no two rooms of a storey alike ({alike} pairs){First("alike")}"));
            Check(unfocused == 0, T($"every room has a focal point ({unfocused} of {rooms} without){First("focal")}"));
            Check(sparse == 0, T($"no big room with a bare middle ({sparse} of {big} big rooms){First("sparse")}"));
            Check(doorless == 0, T($"a door in every door-sized way between rooms ({doorless} of {narrow} without){First("door")}"));
            Check(oneCeiling == 0, T($"no place one ceiling all through ({oneCeiling} of {places}){First("ceiling")}"));
            Check(window == 0, T($"nothing taller than a sill stands before a window ({window} pieces){First("window")}"));
            Check(offFlue == 0, T($"a fire only at a chimney breast, never by a window ({offFlue} of {fires} fires){First("flue")}"));
            Check(focalWall == 0, T($"what a room is arranged round stands on a solid wall ({focalWall} with a window or door behind){First("focal wall")}"));
            Check(unreached == 0, T($"a clear walk a metre wide from the doors to every piece's use side, drawers out ({unreached} pieces cut off){First("reach")}"));
            Check(lining == 0, T($"a room under {DecoRules.Narrow} m across lined along one wall ({lining} with furniture off it){First("narrow")}"));
            Check(crowded == 0, T($"furniture on no more than half a room's floor ({crowded} rooms over){First("crowded")}"));
            Check(focalDown == 0 && midDebris == 0 && sky * 4 <= ruined, T($"ruined rooms read as rooms: focal point standing ({focalDown} down), the fallen against the walls ({midDebris} across the middle), at most a quarter mostly sky ({sky} of {ruined}){First("down")}{First("debris")}{First("sky")}"));
        }
        System.Console.WriteLine($"Interiors: {sw.Elapsed.TotalSeconds:0.0} s");
    }

    /// <summary>Every room of a spawned place as the game shows it: dressed by the library where it dresses it (its props then cleared), its own props elsewhere.</summary>
    static List<RoomLook> LookRooms(PoiInstance inst, DecoPoi decor)
    {
        Layout layout = inst.Layout;
        var dressed = RoomDecor.Rooms(inst).ToDictionary(d => d.Module.Index);
        var of = RoomDecor.ModuleOf(layout);
        PlaceLook look = inst.Blocks.Look ?? PlaceLook.For(inst);
        BlockGrid g = inst.Blocks;
        var doorCells = new HashSet<Int3>(layout.Doorways.SelectMany(dw => dw.Cells));
        var looks = new List<RoomLook>();
        foreach (PlacedModule m in layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault))
        {
            int y = m.Min.Y;
            var floor = m.Cells.Where(c => c.Y == y).ToList();
            if (floor.Count < 2) continue;
            var own = new HashSet<Int3>(floor);
            var ways = RoomDecor.Ways(inst, m, y, of);
            int pieces;
            string how;
            float detour = float.NaN;
            var kinds = new List<string>();
            string[] focus = decor.Arrangements.Focus.GetValueOrDefault(m.Purpose);
            bool focal = m.Kind == ModuleKind.Hall && floor.Any(c => Dirs.Horizontal.Any(h => g.Get(c + h.Vec()).Type == BlockType.Stair));
            bool sparse = false, focalDown = false, focalWall = false, narrowOff = false, crowdedRoom = false;
            int windowed = 0, offFlue = 0, fires = 0, cut = 0;
            bool Focus(DecoPlacement p) => focus != null ? focus.Contains(p.Role) || decor.Forge.Catalogue.Piece(p.Piece).Roles.Any(focus.Contains)
                : p.Support < 0 && decor.Forge.Catalogue.Piece(p.Piece) is { } piece && piece.Width * piece.Depth >= 1f;
            if (dressed.TryGetValue(m.Index, out RoomDecor.Dressable d))
            {
                DecoPlan found = decor.Dress(d.Room, inst.Kit?.Id), used = decor.Dress(d.Room, inst.Kit?.Id, disorder: false);
                focus = decor.Arrangements.FocusFor(m.Purpose, d.Room.Recipe);
                RoomDecor.ClearProps(inst, d);
                pieces = found.Placements.Count;
                how = $"dressed as {d.Room.Recipe ?? d.Room.Set} ({d.Room.Width}x{d.Room.Depth}, {d.Room.Height} m, {d.Room.Doors.Count} doors, {d.Room.Blocked.Count} cells blocked)";
                kinds.AddRange(used.Placements.Select(p => p.Piece));
                bool wasFocal = used.Placements.Any(Focus);
                focal |= wasFocal;
                focalDown = wasFocal && !found.Placements.Any(p => Focus(p) && p.Fall == DecoFall.None && p.State != DecoState.Knocked);
                int x0 = d.Corner.X, z0 = d.Corner.Z, x1 = x0 + (int)d.Room.Width - 1, z1 = z0 + (int)d.Room.Depth - 1;
                var doors = ways.Select(w => RoomDecor.Door(w.Cells, x0, x1, z0, z1)).OfType<DecoDoor>().ToList();
                if (doors.Count >= 2) detour = Detour(d.Room, doors, used.Placements.Where(p => DecoLayout.Blocks(decor.Forge, p)).Select(p => DecoLayout.Footprint(decor.Forge, p)));
                if (!wasFocal && inst.Layout.Extras.Homes.Count > 0) { DecoPlanner.Trace = new(); decor.Dress(d.Room, inst.Kit?.Id, disorder: false); System.Console.WriteLine($"DIAG {m.Purpose} {d.Room} windows {d.Room.Windows.Count} flues {d.Room.Flues.Count} kept {d.Room.Kept.Count}: {string.Join(", ", DecoPlanner.Trace.OrderByDescending(k => k.Value).Take(12).Select(k => $"{k.Key} {k.Value}"))}"); DecoPlanner.Trace = null; } // DIAG
                var forge = decor.Forge;
                windowed = used.Placements.Count(p => DecoRules.BeforeWindow(forge, d.Room, p));
                fires = used.Placements.Count(p => p.Support < 0 && forge.Catalogue.Piece(p.Piece).Has("hearth"));
                offFlue = used.Placements.Count(p => DecoRules.OffFlue(forge, d.Room, p));
                focalWall = used.Placements.Any(p => p.Group == used.FocalGroup && used.FocalGroup >= 0 && p.Support < 0 && DecoLayout.Blocks(forge, p) && DecoRules.OpeningBehind(d.Room, DecoLayout.Footprint(forge, p)));
                cut = DecoRules.Unreached(forge, d.Room, used.Placements).Count;
                narrowOff = DecoRules.OffLining(forge, d.Room, used.Placements);
                crowdedRoom = DecoRules.Share(forge, d.Room, used.Placements) > DecoRules.MostFloor;
                sparse = d.Room.Width * d.Room.Depth >= BigRoom && MathF.Min(d.Room.Width, d.Room.Depth) >= 4 && Bare(d.Room, found.Placements.Where(p => p.Support < 0 && decor.Forge.Catalogue.Piece(p.Piece).Mount != DecoMount.Ceiling).Select(p => DecoLayout.Footprint(decor.Forge, p)).ToList());
            }
            else
            {
                var props = m.Cells.Where(c => (c.Y == y || c.Y == y + 1) && g.Get(c) is { IsAir: false } b && b.Role is Role.Prop or Role.Story && b.Type != BlockType.Scorch).ToList();
                pieces = props.Count;
                how = RoomDecor.Interior(inst, m) is { } inner ? $"undressed: {inner.Cells} open cells of {(inner.X1 - inner.X0 + 1) * (inner.Z1 - inner.Z0 + 1)}, {inner.Height} m clear" : "undressed: no floor";
                kinds.AddRange(props.Select(c => g.Get(c).Type.ToString()));
                // (A hidden stash is arranged round its cache.)
                focal |= props.Any(c => g.Get(c).Type is BlockType.Table or BlockType.Meal or BlockType.Bed or BlockType.Bunk or BlockType.Bench or BlockType.Machine or BlockType.Terminal)
                    || inst.Extras.Secrets.Any(s => s.Module == m.Index) && inst.Caches.Any(c => c.Module == m.Index);
                if (ways.Count >= 2) detour = CellDetour(floor, ways, c => BlockInfo.Collides(g.Get(c).Type) && !g.Get(c).IsAir);
            }
            int w0 = floor.Max(c => c.X) - floor.Min(c => c.X) + 1, d0 = floor.Max(c => c.Z) - floor.Min(c => c.Z) + 1;
            var floorLook = floor.Select(c => FloorLook(look, g, c)).GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
            var walls = floor.SelectMany(c => Dirs.Horizontal.Select(h => c + h.Vec() + Int3.Up).Where(w => g.Get(w) is { Role: Role.Wall } b && BlockInfo.IsFullCube(b.Type) || g.Get(w).Type is BlockType.Half or BlockType.Partition && g.Get(w).Role == Role.Wall)
                .Select(w => look.Pick(g.Get(w), Side.In, false, look.RoomAt(c)).Finish is var f ? (f, look.Colour(g.Get(w), Side.In, f, look.RoomAt(c))) : default)).ToList();
            var wallLook = walls.Count == 0 ? (Gen.Finish.Plain, new Rgb(0, 0, 0)) : walls.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key;
            // Lit: a lamp burning in it, a window in its walls, or the sky over it.
            bool lamp = inst.Lights.Any(l => l.Mode != LightMode.Dead && l.Cell.Y >= y && l.Cell.Y <= m.Max.Y + 1 && floor.Any(c => c.X == l.Cell.X && c.Z == l.Cell.Z));
            bool hung = layout.Extras.Lights.Any(l => l.Module == m.Index || l.Cell.Y >= y && l.Cell.Y <= m.Max.Y + 1 && own.Contains(l.Cell with { Y = y }));
            bool window = floor.Any(c => Dirs.Horizontal.Any(h => Enumerable.Range(1, 2).Any(k => g.Get(c + h.Vec() + Int3.Up * k) is { Type: BlockType.Pane, Role: Role.Window } or { IsAir: true } && !of.ContainsKey(c + h.Vec() + Int3.Up * k) && !of.ContainsKey(c + h.Vec() * 2 + Int3.Up * k))));
            bool Open(Int3 c) => !Enumerable.Range(1, 12).Any(k => g.Get(c + Int3.Up * k) is { IsAir: false } b && BlockInfo.IsFullCube(b.Type));
            int open = floor.Count(Open);
            string signature = $"{m.Purpose} {Math.Min(w0, d0)}x{Math.Max(w0, d0)} {floorLook.Item1}/{floorLook.Item2} {wallLook.Item1}/{wallLook.Item2}: {string.Join(" ", kinds.OrderBy(k => k))}";
            var (ceiling, flatCeiling) = CeilingOf(g, floor.Where(c => Dirs.Horizontal.All(h => own.Contains(c + h.Vec()))).DefaultIfEmpty(floor[0]).ToList());
            // Its ways to another room a door's width or less, and those with no door hung in them (a ruin's may have gone since).
            var toRooms = ways.Where(w => w.From >= 0 && layout.Modules[w.From].Kind is not (ModuleKind.Outdoor or ModuleKind.Stair or ModuleKind.Lift) && w.Cells.Count == DoorSize.Wide
                && w.Cells.All(c => BlockInfo.Collides(g.Get(c + Int3.Up * DoorSize.Tall).Type))).ToList();
            bool Doored(List<Int3> cells) => cells.Any(c => doorCells.Contains(c) || g.Get(c).Type == BlockType.Door);
            // What fell, at the floor: heaped in the middle (cells clear of every wall) more than round the edges.
            bool Fallen(Int3 c) => g.Get(c).Role == Role.Debris && g.Get(c).Type is BlockType.Rubble or BlockType.Cube or BlockType.Ramp || g.Get(c + Int3.Up).Role == Role.Debris && g.Get(c + Int3.Up).Type is BlockType.Rubble or BlockType.Cube;
            var middle = floor.Where(c => Dirs.Horizontal.All(h => own.Contains(c + h.Vec()) && own.Contains(c + h.Vec() * 2))).ToList();
            var edge = floor.Except(middle).ToList();
            float midShare = middle.Count == 0 ? 0 : middle.Count(Fallen) / (float)middle.Count, edgeShare = edge.Count == 0 ? 0 : edge.Count(Fallen) / (float)edge.Count;
            looks.Add(new RoomLook(m, pieces, detour, hung || window || open > 0, lamp, focal, signature, floorLook, wallLook, how, ways.Count)
            {
                Big = Math.Min(w0, d0) >= 4 && floor.Count >= BigRoom, Sparse = sparse, Ceiling = ceiling, Flat = flatCeiling,
                Narrow = toRooms.Count, Doorless = toRooms.Count(w => !Doored(w.Cells)),
                FocalDown = focalDown, MidDebris = midShare > 0.1f && midShare > edgeShare, Sky = open * 2 > floor.Count,
                Window = windowed, OffFlue = offFlue, Fires = fires, Unreached = cut, FocalWall = focalWall, OffWall = narrowOff, Crowded = crowdedRoom,
            });
        }
        return looks;
    }

    /// <summary>A room of this many square metres or more, 4 m or more across, is big: it wants a second zone (a group of its own, a rug, a divider).</summary>
    const float BigRoom = 24f;

    /// <summary>A big room's middle is bare: a third of the floor 1.5 m in from its walls has nothing standing within 1.5 m of it.</summary>
    static bool Bare(DecoRoom room, List<DecoRect> furniture)
    {
        int points = 0, bare = 0;
        for (float z = 1.5f; z <= room.Depth - 1.5f; z += 0.5f)
            for (float x = 1.5f; x <= room.Width - 1.5f; x += 0.5f)
            {
                points++;
                var p = new NV2(x, z);
                var near = new DecoRect(p, new NV2(1.5f, 0), new NV2(0, 1.5f));
                if (!furniture.Any(f => f.Overlaps(near))) bare++;
            }
        return points > 0 && bare * 3 > points;
    }

    /// <summary>
    /// A room's ceiling as you look up from the middle of its floor (a cornice round its edge is no ceiling): the clear heights
    /// over its cells, what stops them (a slab, a slope, a lowered panel), and any beams or pipes under it. Flat: one height all
    /// over, a plain slab, nothing under it.
    /// </summary>
    static (string Form, bool Flat) CeilingOf(BlockGrid g, List<Int3> floor)
    {
        var heights = new SortedSet<int>();
        var stops = new SortedSet<string>();
        bool beams = false;
        foreach (Int3 c in floor)
            for (int k = 1; k < 14; k++)
            {
                Block b = g.Get(c + Int3.Up * k);
                if (b.Role == Role.Decor && b.Type is BlockType.Beam or BlockType.Pipe) { beams = true; continue; }
                if (b.IsAir || b.Role is not (Role.Floor or Role.Roof or Role.Wall)) continue;
                heights.Add(k);
                stops.Add(b.Type.ToString());
                break;
            }
        bool flat = heights.Count <= 1 && !beams && stops.All(s => s == nameof(BlockType.Cube));
        return ($"{string.Join("/", heights)} m {string.Join("/", stops)}{(beams ? " beamed" : "")}", flat);
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
