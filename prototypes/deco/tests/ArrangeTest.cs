using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace DecoShowroom.Tests;

/// <summary>
/// Headless test of arranging and disordering rooms and sites: same seed, same result; nothing
/// overlaps, nothing is outside the room or in a wall; doorways stay clear and a walker (or a
/// driver, on a road) gets from every doorway to every other after disorder, checked by its own
/// path search; every theme set and generic POI prop resolves; wires are anchored; a dressed room
/// builds within a frame budget. Run with:
///   godot --headless --path prototypes/deco res://tests/ArrangeTest.tscn
/// </summary>
public partial class ArrangeTest : TestBase
{
    DecoForge _forge;
    DecoArrangements _arr;
    DecoPoi _poi;
    int _rooms, _placements, _paths;

    static readonly string[] States = { "active", "abandoned", "infested", "ruined" };

    protected override void RunAll()
    {
        Run("Arrangement data loads and resolves", TestData);
        Run("Generic POI props map to pieces", TestProps);
        Run("POI rooms dress", TestPoiRooms);
        Run("Same seed, same placements and disorder", TestDeterminism);
        Run("Rooms: no overlaps, nothing in a wall, doorways and paths clear", TestRooms);
        Run("Templates arrange pieces as used", TestTemplates);
        Run("Disorder follows the state and age", TestDisorder);
        Run("Sites: lanes clear, wires anchored", TestSites);
        Run("Ore surface tells", TestOreTells);
        Run("Frame cost (best of three windows)", TestFrameCost);
    }

    void TestData()
    {
        _forge = new DecoForge(DataPaths.Deco);
        _arr = DataPaths.Arrangements;
        _poi = new DecoPoi(_forge, _arr);
        DecoCatalogue cat = DataPaths.Deco;
        Note($"{_arr.Templates.Count} templates, {_arr.Sets.Count} set mixes, {_arr.Recipes.Count} room recipes, {_arr.Sites.Count} sites");
        foreach (DecoSet s in cat.Sets.Where(s => !s.Outdoor)) Check(_arr.Sets.ContainsKey(s.Id), $"set '{s.Id}' has an arrangement mix");
        foreach (DecoTemplate t in _arr.Templates.Values)
            Check(cat.Pieces.Any(p => !p.Outdoor && t.Anchor.Any(p.Has)), $"template '{t.Id}': some piece plays its anchor ({string.Join("/", t.Anchor)})");
        foreach (var (set, mix) in _arr.Sets)
            foreach (string role in mix.Fill) Check(cat.InSet(set).Any(p => p.Has(role)), $"set '{set}': some piece plays fill role '{role}'");
    }

    void TestProps()
    {
        DecoCatalogue cat = DataPaths.Deco;
        foreach (string prop in DecoPoi.GenericProps)
        {
            Check(_arr.Props.ContainsKey(prop), $"prop '{prop}' is mapped");
            var roles = _arr.Props[prop].Roles;
            int pieces = cat.Pieces.Count(p => roles.Any(p.Has));
            Check(pieces >= 1, $"prop '{prop}' maps to {pieces} pieces by role ({string.Join("/", roles)}), fallback {_arr.Props[prop].Fallback}");
            foreach (DecoSet s in cat.Sets.Where(s => !s.Outdoor))
                Check(_poi.PropPiece(prop, s.Id, 5) != null, $"prop '{prop}' in set '{s.Id}' becomes a piece");
        }
        Note(string.Join(", ", DecoPoi.GenericProps.Select(p => $"{p}->{_poi.PropPiece(p, "office", 5).Id}/{_poi.PropPiece(p, "medical", 5).Id}")));
    }

    void TestPoiRooms()
    {
        foreach (var (roomId, set) in _arr.Rooms)
        {
            var room = new DecoRoom { Width = 9, Depth = 7, RoomId = roomId, Doors = { new DecoDoor(DecoWall.South, 4.5f, 2f) }, Seed = DecoHash.Fnv(roomId), State = "abandoned" };
            DecoPlan plan = _poi.Dress(room);
            Check(room.Set == set && plan.Placements.Count >= 2, $"POI room '{roomId}' dresses from '{set}' ({plan.Placements.Count} pieces)");
        }
        var precursor = new DecoRoom { Width = 9, Depth = 7, RoomId = "lab", Doors = { new DecoDoor(DecoWall.South, 4.5f, 2f) } };
        _poi.Dress(precursor, kit: "precursor");
        Check(precursor.Set == "precursor", "a Precursor kit's lab is furnished from the Precursor set");
    }

    static DecoRoom MakeRoom(string set, string recipe, float w, float d, int doors, string state, ulong seed)
    {
        var room = new DecoRoom { Width = w, Depth = d, Set = set, RoomId = recipe, State = state, Seed = seed };
        room.Doors.Add(new DecoDoor(DecoWall.South, w * 0.5f, 1.6f));
        if (doors >= 2) room.Doors.Add(new DecoDoor(DecoWall.North, w * 0.3f, 1.4f));
        if (doors >= 3) room.Doors.Add(new DecoDoor(DecoWall.East, d * 0.5f, 1.4f));
        return room;
    }

    void TestDeterminism()
    {
        foreach (string set in new[] { "canteen", "medical", "industrial", "farm" })
        {
            string a = _poi.Dress(MakeRoom(set, null, 11, 8, 2, "ruined", 42)).Fingerprint();
            string b = _poi.Dress(MakeRoom(set, null, 11, 8, 2, "ruined", 42)).Fingerprint();
            string c = _poi.Dress(MakeRoom(set, null, 11, 8, 2, "ruined", 43)).Fingerprint();
            Check(a == b, $"{set}: the same seed gives the same placements and disorder");
            Check(a != c, $"{set}: another seed gives another room");
        }
        foreach (string site in new[] { "evacuation-jam", "airliner-crash", "derailed-train" })
        {
            string a = _poi.DressSite(_poi.SiteRoom(site, 9)).Fingerprint(), b = _poi.DressSite(_poi.SiteRoom(site, 9)).Fingerprint();
            Check(a == b, $"{site}: the same seed gives the same site");
        }
        // A fresh forge (meshes rebuilt from nothing) agrees too.
        var other = new DecoPoi(new DecoForge(DataPaths.Deco), _arr);
        Check(other.Dress(MakeRoom("office", null, 10, 8, 2, "abandoned", 7)).Fingerprint() == _poi.Dress(MakeRoom("office", null, 10, 8, 2, "abandoned", 7)).Fingerprint(), "a second forge and planner agree");
    }

    // ---- independent checks ------------------------------------------------------------------------

    /// <summary>Every rule a dressed room must keep, checked without the planner's own raster. Returns the failures.</summary>
    List<string> Audit(DecoPlan plan)
    {
        var bad = new List<string>();
        DecoRoom room = plan.Room;
        var pls = plan.Placements;
        var rects = pls.Select(p => DecoLayout.Footprint(_forge, p)).ToList();
        for (int i = 0; i < pls.Count; i++)
        {
            if (!rects[i].Corners().All(c => c.X >= -0.002f && c.Y >= -0.002f && c.X <= room.Width + 0.002f && c.Y <= room.Depth + 0.002f))
                bad.Add($"{pls[i]} pokes outside the room");
            var (lo, hi) = DecoLayout.Heights(_forge, pls[i]);
            if (lo < -0.002f || (!room.Outdoor && hi > room.Height + 0.01f)) bad.Add($"{pls[i]} is through the floor or ceiling ({lo:0.00}..{hi:0.00})");
            if (pls[i].Support >= 0)
            {
                DecoPlacement s = pls[pls[i].Support];
                DecoPiece sp = _forge.Catalogue.Piece(s.Piece);
                if (!rects[pls[i].Support].Contains(new Vector2(pls[i].X, pls[i].Z)) || MathF.Abs(pls[i].Y - (s.Y + sp.Surface)) > 0.01f)
                    bad.Add($"{pls[i]} doesn't sit on {s}");
            }
            for (int j = i + 1; j < pls.Count; j++)
                if (DecoLayout.Clash(_forge, pls[i], pls[j])) bad.Add($"{pls[i]} overlaps {pls[j]}");
            if (pls[i].Support < 0)
                foreach (DecoDoor d in room.Doors)
                    if (room.Apron(d, room.Outdoor ? 0.6f : 0.9f, 0).Overlaps(rects[i].Shrink(0.02f))) bad.Add($"{pls[i]} stands in the doorway at {d.Wall} {d.Offset}");
        }
        if (!PathClear(plan)) bad.Add("no clear path between the doorways");
        return bad;
    }

    /// <summary>
    /// The path check, done differently from the planner: sample points on a grid; a point is
    /// free if an agent-sized square centred there touches no blocking footprint; flood from the
    /// first doorway and see every other doorway reached.
    /// </summary>
    bool PathClear(DecoPlan plan)
    {
        DecoRoom room = plan.Room;
        if (room.Doors.Count == 0) return true;
        _paths++;
        float step = room.Outdoor ? 0.25f : 0.1f, half = room.Agent / 2;
        var blockers = plan.Placements.Where(p => DecoLayout.Blocks(_forge, p)).Select(p => DecoLayout.Footprint(_forge, p).Shrink(0.01f)).ToList();
        int nx = (int)((room.Width - 2 * half) / step) + 1, nz = (int)((room.Depth - 2 * half) / step) + 1;
        if (nx <= 0 || nz <= 0) return false;
        var free = new bool[nx * nz];
        Array.Fill(free, true);
        Vector2 P(int i, int j) => new(half + i * step, half + j * step);
        // Each blocker knocks out the sample points whose agent square it touches (only those near it are tested).
        foreach (DecoRect b in blockers)
        {
            var (lo, hi) = b.Bounds();
            int i0 = Math.Max(0, (int)MathF.Floor((lo.X - 2 * half) / step)), i1 = Math.Min(nx - 1, (int)MathF.Ceiling((hi.X) / step));
            int j0 = Math.Max(0, (int)MathF.Floor((lo.Y - 2 * half) / step)), j1 = Math.Min(nz - 1, (int)MathF.Ceiling((hi.Y) / step));
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                    if (free[j * nx + i] && b.Overlaps(new DecoRect(P(i, j), new Vector2(half - 0.005f, 0), new Vector2(0, half - 0.005f)))) free[j * nx + i] = false;
        }
        List<int> Starts(DecoDoor d)
        {
            var (at, inward) = room.DoorPoint(d);
            var list = new List<int>();
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    Vector2 rel = P(i, j) - at;
                    float along = MathF.Abs(rel.X * inward.Y - rel.Y * inward.X), into = Vector2.Dot(rel, inward);
                    if (along <= d.Width / 2 - half + 0.01f && into <= half + step) list.Add(j * nx + i);
                }
            return list;
        }
        var seen = new bool[nx * nz];
        var q = new Queue<int>();
        foreach (int s in Starts(room.Doors[0])) if (free[s]) { seen[s] = true; q.Enqueue(s); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(), i = c % nx, j = c / nx;
            foreach (var (di, dj) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int ni = i + di, nj = j + dj;
                if (ni < 0 || nj < 0 || ni >= nx || nj >= nz) continue;
                int n = nj * nx + ni;
                if (!seen[n] && free[n]) { seen[n] = true; q.Enqueue(n); }
            }
        }
        if (room.Doors.Count == 1) return seen.Any(x => x);
        return room.Doors.Skip(1).All(d => Starts(d).Any(s => seen[s]));
    }

    void TestRooms()
    {
        var sizes = new (float w, float d)[] { (6, 5), (10, 8), (14, 10), (20, 3) };
        var mixes = _arr.Sets.Keys.Select(s => (set: s, recipe: (string)null)).Concat(_arr.Recipes.Select(r => (set: r.Value.Set, recipe: r.Key))).ToList();
        int failures = 0;
        foreach (var (set, recipe) in mixes)
            foreach (var (w, d) in sizes)
                foreach (int doors in new[] { 1, 2, 3 })
                    foreach (string state in States)
                        for (ulong seed = 1; seed <= 2; seed++)
                        {
                            DecoRoom room = MakeRoom(set, recipe, w, d, doors, state, seed * 1000 + (ulong)(w * 10 + d));
                            if (d < 4) room.Doors.RemoveAll(x => x.Wall == DecoWall.North);
                            DecoPlan plan = _poi.Dress(room);
                            _rooms++;
                            _placements += plan.Placements.Count;
                            List<string> bad = Audit(plan);
                            if (bad.Count > 0 && failures++ < 8) foreach (string b in bad.Take(3)) Note($"{room}: {b}");
                            Check(bad.Count == 0, $"{recipe ?? set} {w}x{d} {doors} doors {state} seed {seed}: legal ({bad.Count} problems)");
                        }
        Note($"{_rooms} rooms dressed and audited, {_placements} placements, {_paths} independent path searches");
    }

    void TestTemplates()
    {
        // A canteen: chairs face their tables; trays sit on the tables.
        DecoPlan canteen = _poi.Dress(MakeRoom("canteen", null, 12, 9, 1, "active", 3), disorder: false);
        var chairs = canteen.Placements.Where(p => p.Role is "chair" or "bench-seat").ToList();
        Check(chairs.Count >= 4, $"the canteen has chairs round its tables ({chairs.Count})");
        int facing = 0;
        foreach (DecoPlacement c in chairs)
        {
            DecoPlacement table = canteen.Placements.FirstOrDefault(p => p.Group == c.Group && _forge.Catalogue.Piece(p.Piece).Has("table"));
            if (table == null) continue;
            Vector2 front = DecoGeo.Yaw(new Vector2(0, -1), c.Yaw), toTable = new Vector2(table.X - c.X, table.Z - c.Z);
            if (Vector2.Dot(Vector2.Normalize(front), Vector2.Normalize(toTable)) > 0.6f) facing++;
        }
        Check(facing == chairs.Count, $"every chair faces its table ({facing} of {chairs.Count})");
        Check(canteen.Placements.Any(p => p.Support >= 0), "the tables are still set (trays on them)");
        DecoPlan ward = _poi.Dress(MakeRoom("medical", null, 12, 8, 2, "active", 4), disorder: false);
        Check(ward.Placements.Count(p => p.Group >= 0 && _forge.Catalogue.Piece(p.Piece).Has("bed")) >= 3, "the ward has beds in rows");
        Check(ward.Placements.Any(p => p.Role == "curtain"), "ward bays have curtains");
        DecoPlan office = _poi.Dress(MakeRoom("office", null, 10, 8, 2, "active", 5), disorder: false);
        Check(office.Placements.Count(p => _forge.Catalogue.Piece(p.Piece).Has("desk")) >= 2 && office.Placements.Any(p => p.Role == "top"), "office desk stations with monitors on them");
        DecoPlan hall = _poi.Dress(MakeRoom("industrial", "assembly-hall", 14, 10, 2, "active", 6), disorder: false);
        Check(hall.Placements.Count(p => p.Piece == "conveyor-belt") >= 2 && hall.Placements.Any(p => p.Piece == "robot-arm"), $"an assembly line: conveyors with robot arms beside them ({string.Join(',', hall.Placements.Select(p => p.Piece).Distinct())})");
        DecoPlan store = _poi.Dress(MakeRoom("industrial", "warehouse", 14, 10, 1, "active", 7), disorder: false);
        Check(store.Placements.Count(p => p.Piece == "pallet-racking") >= 2, "a warehouse aisle of pallet racking");
        DecoPlan green = _poi.Dress(new DecoRoom { Width = 12, Depth = 8, RoomId = "greenhouse", Set = "farm", Tech = DecoTech.High, Doors = { new DecoDoor(DecoWall.West, 4, 1.6f), new DecoDoor(DecoWall.East, 4, 1.6f) }, Seed = 8, State = "active" }, disorder: false);
        Check(green.Placements.Count(p => _forge.Catalogue.Piece(p.Piece).Has("growbed")) >= 2, "a greenhouse with rows of grow beds");
        DecoPlan barn = _poi.Dress(new DecoRoom { Width = 12, Depth = 10, RoomId = "barn", Set = "farm", Tech = DecoTech.Low, Doors = { new DecoDoor(DecoWall.South, 6, 3f) }, Seed = 9, State = "active" }, disorder: false);
        Check(barn.Placements.Any(p => p.Piece == "hay-bales") && barn.Placements.All(p => _forge.Catalogue.Piece(p.Piece).Tech == DecoTech.Low), $"a low-tech barn: hay, and nothing beyond its tech level ({string.Join(',', barn.Placements.Select(p => p.Piece + ':' + _forge.Catalogue.Piece(p.Piece).Tech).Distinct())})");
        DecoPlan chapel = _poi.Dress(MakeRoom("civic", null, 10, 12, 1, "active", 10), disorder: false);
        Check(chapel.Placements.Count(p => p.Role == "seat") >= 4 && chapel.Placements.Any(p => p.Role == "focus"), "pews in rows facing the altar");
    }

    void TestDisorder()
    {
        int knocked = 0, burnt = 0, notes = 0, stillOn = 0, activeKnocked = 0;
        for (ulong s = 1; s <= 12; s++)
        {
            DecoPlan ruined = _poi.Dress(MakeRoom("canteen", null, 12, 9, 2, "ruined", s));
            knocked += ruined.Placements.Count(p => p.Fall != DecoFall.None);
            burnt += ruined.Placements.Count(p => p.State == DecoState.Burnt);
            DecoPlan abandoned = _poi.Dress(MakeRoom("office", null, 10, 8, 2, "abandoned", s));
            notes += abandoned.Placements.Count(p => p.Note is "pushed back from the table" or "dropped mid-aisle" or "left out where it was last used" or "door left open");
            stillOn += abandoned.Placements.Count(p => p.Powered);
            Check(abandoned.Placements.All(p => p.Age == DecoAge.OldWorld), "an abandoned place is old-world by default");
            activeKnocked += _poi.Dress(MakeRoom("canteen", null, 12, 9, 2, "active", s)).Placements.Count(p => p.Fall != DecoFall.None);
        }
        Note($"over 12 ruined canteens: {knocked} knocked over, {burnt} burnt; over 12 abandoned offices: {notes} frozen moments, {stillOn} still powered");
        Check(knocked > 0 && burnt > 0, "ruined rooms have knocked-over and burnt pieces");
        Check(notes > 0, "abandoned rooms have frozen moments");
        Check(activeKnocked == 0, "rooms in use have nothing knocked over");
        Check(stillOn <= 12, "the still-powered touch is rare (at most one per room)");
        DecoPlan r1 = _poi.Dress(MakeRoom("military", null, 12, 9, 2, "ruined", 77));
        foreach (DecoPlacement p in r1.Placements.Where(p => p.Fall != DecoFall.None))
            Check(MathF.Abs(DecoLayout.Heights(_forge, p).Lo) < 0.002f, $"{p} lies on the floor");
        // Fast-rotting things go in an old-world barn; sturdy indoor furniture survives worn.
        int hayFresh = _forge.Mesh(new DecoLook("hay-bales", DecoState.Intact, DecoFall.None, false, DecoCover.Vines, "farm")).Triangles;
        int hayOld = _forge.Mesh(new DecoLook("hay-bales", DecoState.Intact, DecoFall.None, false, DecoCover.Vines, "farm", false, DecoAge.OldWorld)).Triangles;
        Check(hayOld < hayFresh * 0.6f, $"old-world hay is mostly gone ({hayOld} of {hayFresh} triangles)");
        int softFresh = _forge.ShapeOf("sofa", DecoState.Intact, DecoFall.None).Model.Parts.Count(p => p.Is(PartFlags.Soft));
        int softIndoor = _forge.ShapeOf("sofa", DecoState.Intact, DecoFall.None, false, DecoAge.OldWorld, false).Model.Parts.Count(p => p.Is(PartFlags.Soft));
        int softOutdoor = _forge.ShapeOf("sofa", DecoState.Intact, DecoFall.None, false, DecoAge.OldWorld, true).Model.Parts.Count(p => p.Is(PartFlags.Soft));
        Check(softIndoor == softFresh && softOutdoor < softFresh, $"a sofa indoors keeps its cushions, worn ({softIndoor}/{softFresh}); left in the weather it loses them ({softOutdoor})");
        var hulk = _forge.ShapeOf("sedan", DecoState.Intact, DecoFall.None, false, DecoAge.OldWorld, true);
        Check(hulk.Model.Parts.Count(p => p.Is(PartFlags.Tyre)) == 0 && MathF.Abs(hulk.Min.Y) < 0.01f, "an old-world car outdoors is a hulk on its hubs, still on the ground");
    }

    void TestSites()
    {
        int sites = 0, cables = 0;
        var wiresBySite = new SortedDictionary<string, int>();
        foreach (string site in _arr.Sites.Keys)
            foreach (DecoTech tech in new[] { DecoTech.Low, DecoTech.Mid, DecoTech.High })
                foreach (string state in new[] { "active", "abandoned", "ruined" })
                {
                    DecoRoom room = _poi.SiteRoom(site, DecoHash.Fnv(site) + (ulong)tech * 17, tech, state);
                    DecoPlan plan = _poi.DressSite(room);
                    sites++;
                    var bad = Audit(plan);
                    foreach (string b in bad.Take(2)) Note($"{site} {tech} {state}: {b}");
                    Check(bad.Count == 0, $"{site} {tech} {state}: legal, lane clear ({plan.Placements.Count} placements)");
                    Check(plan.Placements.Count >= 3, $"{site} {tech} {state}: dressed ({plan.Placements.Count} placements)");
                    foreach (DecoPlanCable c in plan.Cables)
                    {
                        cables++;
                        wiresBySite[site] = wiresBySite.GetValueOrDefault(site) + 1;
                        Vector3 anchor = _poi.AnchorAt(plan.Placements[c.From], c.FromAnchor);
                        Check(Vector3.Distance(anchor, c.A) < 0.01f && plan.Placements[c.From].Fall == DecoFall.None, $"{site}: a wire hangs from a standing pole's anchor");
                        if (c.Spanned) Check(Vector3.Distance(_poi.AnchorAt(plan.Placements[c.To], c.ToAnchor), c.B) < 0.01f, $"{site}: a strung wire reaches the next pole's anchor");
                        else Check(DecoCables.Path(c.AsCable())[^1].Y <= c.Radius + 0.01f, $"{site}: a wire from a downed pole lies on the ground");
                    }
                }
        Note($"{sites} sites audited, {cables} wires checked ({string.Join(", ", wiresBySite.Select(w => $"{w.Key} {w.Value}"))})");
        DecoPlan jam = _poi.DressSite(_poi.SiteRoom("evacuation-jam", 3, DecoTech.Mid, "abandoned"));
        Check(jam.Placements.Count(p => _forge.Catalogue.Piece(p.Piece).Has("vehicle")) >= 8, "the evacuation jam has a lane of vehicles");
        Check(jam.Placements.Any(p => p.Doors), "some doors flung open in the jam");
        Check(jam.Placements.Where(p => _forge.Catalogue.Piece(p.Piece).Has("vehicle")).All(p => p.Age == DecoAge.OldWorld && p.Exposed), "the jam is a century old, out in the weather");
        foreach (DecoTech tech in new[] { DecoTech.Low, DecoTech.High })
        {
            var veh = _poi.DressSite(_poi.SiteRoom("evacuation-jam", 4, tech, "abandoned")).Placements.Where(p => _forge.Catalogue.Piece(p.Piece).Has("vehicle")).ToList();
            Check(veh.Count > 0 && veh.Count(p => _forge.Catalogue.Piece(p.Piece).Tech == tech) >= veh.Count * 0.6f, $"a {tech}-tech jam is mostly {tech}-tech vehicles");
        }
        foreach (DecoTech tech in new[] { DecoTech.Low, DecoTech.Mid, DecoTech.High })
            foreach (string state in new[] { "active", "abandoned", "ruined" })
                for (ulong seed = 1; seed <= 4; seed++)
                {
                    var gate = _poi.DressSite(_poi.SiteRoom("airliner-gate", seed, tech, state)).Placements;
                    Check(gate.Any(p => p.Piece == "airbridge") && gate.Any(p => p.Piece == "boarding-stairs") && gate.Count(p => _forge.Catalogue.Piece(p.Piece).Has("airliner")) >= 6,
                        $"an airliner at the gate ({tech}, {state}, seed {seed}) is whole, with its airbridge and stairs ({string.Join(",", gate.Select(p => p.Piece).Distinct())})");
                    var crash = _poi.DressSite(_poi.SiteRoom("airliner-crash", seed, tech, state)).Placements;
                    Check(crash.Count(p => _forge.Catalogue.Piece(p.Piece).Has("airliner")) >= 5 && crash.Any(p => p.Piece == "crash-scar"), $"a crashed airliner ({tech}, {state}, seed {seed}) lies in sections along its scar");
                    foreach (string line in new[] { "derelict-train", "derailed-train", "station-platform" })
                    {
                        var trainPlan = _poi.DressSite(_poi.SiteRoom(line, seed, tech, state));
                        var train = trainPlan.Placements;
                        if (train.Count(p => _forge.Catalogue.Piece(p.Piece).Has("train")) < 3) foreach (string n in trainPlan.Notes.Where(n => n.Contains("left out")).Take(4)) Note(n);
                        Check(train.Count(p => _forge.Catalogue.Piece(p.Piece).Has("train")) >= 3, $"{line} ({tech}, {state}, seed {seed}) has a train of three or more ({string.Join(",", train.Where(p => _forge.Catalogue.Piece(p.Piece).Has("train")).Select(p => p.Piece + ":" + p.Fall))})");
                    }
                }
    }

    void TestOreTells()
    {
        foreach (var (name, hex, glow, glints) in Showroom.Ores)
            foreach (var (label, kind, shape, radius) in Showroom.TellRows)
            {
                DecoMeshData a = DecoOreTells.Build(kind, DecoRgb.Hex(hex), glow, glints, 11, radius, null, shape);
                DecoMeshData b = DecoOreTells.Build(kind, DecoRgb.Hex(hex), glow, glints, 11, radius, null, shape);
                Check(a.Triangles > 0 && a.Triangles == b.Triangles && a.Min == b.Min, $"{name} {label}: built, and the same from the same seed");
                Check(a.Min.Y > (kind == DecoTellKind.Scree ? -0.05f : -0.001f) && a.Min.Y < 0.01f, $"{name} {label}: sits on the ground, pebbles bedded in no deeper than 5 cm (lowest {a.Min.Y:0.000})");
                Check(MathF.Max(MathF.Max(-a.Min.X, a.Max.X), MathF.Max(-a.Min.Z, a.Max.Z)) <= radius * 1.6f + 0.2f, $"{name} {label}: within its size");
                if (kind == DecoTellKind.Outcrop) Check(a.Boxes.Count > 0, $"{name} {label}: a big outcrop is solid");
                if (kind == DecoTellKind.Scree) Check(a.Boxes.Count == 0, $"{name} {label}: scree is not");
            }
    }

    void TestFrameCost()
    {
        DecoRoom Room() => MakeRoom("canteen", null, 12, 9, 2, "abandoned", 21);
        double plan = BestOfThree(() => _poi.Dress(Room()), 3);
        DecoPlan p = _poi.Dress(Room());
        var cache = new DecoMeshCache(_forge);
        var looks = p.Placements.Select(x => x.Look).Distinct().ToList();
        double cold = BestOfThree(() => { var f = new DecoForge(DataPaths.Deco); foreach (DecoLook l in looks) f.Mesh(l); });
        foreach (DecoLook l in looks) cache.Mesh(l);
        double warm = BestOfThree(() => { var v = DecoPlanView.Create(cache, p); v.Free(); });
        Note($"a 12 x 9 m canteen: {p.Placements.Count} pieces, {looks.Count} distinct looks");
        Note($"plan + disorder {plan:0.00} ms; meshes from nothing {cold:0.0} ms; nodes from cached meshes {warm:0.00} ms");
        Check(plan < 30, $"planning and disorder within 30 ms ({plan:0.00})");
        Check(warm < 16, $"building the room's nodes from cached meshes within a 16 ms frame ({warm:0.00})");
        Check(cold < 250, $"meshing the room's looks from nothing within 250 ms, done once per POI load ({cold:0.0})");
        double sitePlan = BestOfThree(() => _poi.DressSite(_poi.SiteRoom("evacuation-jam", 5)));
        Note($"an evacuation jam site: plan {sitePlan:0.0} ms");
        Check(sitePlan < 120, $"a road site plans within 120 ms ({sitePlan:0.0})");
    }
}
