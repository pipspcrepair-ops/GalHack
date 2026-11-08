using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// The house rules on fresh worlds: garage doors at real size and of every kind, each with a drive from the
    /// lot's front to its door; a street of one culture where neighbours differ but keep to its families; every
    /// house walked into through its door, every room of it reached; nothing floats; each house has a fixing for
    /// its service drop facing out.
    /// </summary>
    void Houses()
    {
        var rand = new Random();
        FamilyDef fam = _lib.Families.First(f => f.Id == "flavour_house");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        int houses = 0, garages = 0, badSize = 0, noDrive = 0, unreached = 0, floating = 0, noService = 0, badService = 0, lowHatch = 0, lowDoor = 0, framed = 0;
        int exits = 0, offSize = 0, noHeadroom = 0, overDoor = 0;
        var fixings = new Dictionary<string, int>();
        var fence = new FenceTally();
        var kinds = new Dictionary<string, int>();
        int singles = 0, doubles = 0;
        string first = "";
        void Note(string s) { if (first.Length == 0) first = " (first: " + s + ")"; }
        for (int i = 0; i < 360; i++)
        {
            var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], (TechLevel)rand.Next(3), null, new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble()));
            Layout layout = PoiFactory.BuildLayout(_lib, fam, rand.Next(fam.Variants.Count), Rarity.Common, (ulong)rand.NextInt64(), hints);
            houses++;
            var g = layout.Blocks;
            foreach (HomeNote h in layout.Extras.Homes.Where(h => h.Garage != "none" && h.DoorWide > 0))
            {
                garages++;
                kinds[h.GarageDoor] = kinds.GetValueOrDefault(h.GarageDoor) + 1;
                if (h.DoorWide == 4) singles++; else doubles++;
                // A single about 2.5 m against our 4 m storeys is 4 m, a double 6 or 8 m; 3 m tall under the wall's top course.
                if (h.DoorWide is not (4 or 6 or 8) || h.DoorTall != 3) { badSize++; Note($"{h.Family} {h.GarageDoor} {h.DoorWide} by {h.DoorTall}"); }
                // The drive: paving from the cell in front of the door out to the lot's gate.
                if (!Enumerable.Range(0, h.DoorWide).Any(k => Driven(g, h.DoorCell + h.DoorFacing.Vec() + h.DoorFacing.RotateCw(1).Vec() * k, layout.Extras.Gate))) { noDrive++; Note($"{h.Family} {h.Garage} {h.GarageDoor}: no drive from {h.DoorCell} to {layout.Extras.Gate}"); }
            }
            // Into every room through the door.
            var nav = new NavGrid(g, layout.Occupied, layout.Extras.Lifts);
            var reach = nav.Reachable(layout.EntranceCell);
            foreach (var m in layout.Modules.Where(m => m.Kind is ModuleKind.Room or ModuleKind.Hall))
                if (!m.Cells.Any(reach.Contains)) { unreached++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: {m.Purpose} on storey {m.Storey} unreached"); break; }
            // Every opening a person can get to between two walls is the standard door's height clear (3 m against a
            // 1.8 m person): a door, an arch through to the court, a garage door; never a low hatch. Windows are glass, not openings.
            bool Wall(Int3 c) => g.Get(c) is { Role: Role.Wall } w && BlockInfo.Collides(w.Type);
            foreach (Int3 c in reach)
            {
                if (c.Y < 0) continue;
                bool jambs = Wall(c + Dir.East.Vec()) && Wall(c + Dir.West.Vec()) || Wall(c + Dir.North.Vec()) && Wall(c + Dir.South.Vec());
                if (!jambs) continue;
                int clear = 0;
                while (clear < 4 && !BlockInfo.Collides(g.Get(c + Int3.Up * clear).Type)) clear++;
                if (clear < DoorSize.Tall) { lowHatch++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: an opening {clear} m clear at {c}"); if (lowHatch <= 4) GD.Print($"      low opening: {layout.Extras.Homes.FirstOrDefault()?.Silhouette} at {c}: {g.Get(c)}, E {g.Get(c + Dir.East.Vec())} W {g.Get(c + Dir.West.Vec())} N {g.Get(c + Dir.North.Vec())} S {g.Get(c + Dir.South.Vec())} below {g.Get(c - Int3.Up)} fence {layout.Extras.Homes.FirstOrDefault()?.FenceKind}"); break; }
            }
            // Every door out of a house is the one standard door, exactly, in a wall; over it and two metres out in front
            // nothing comes lower than its head and a metre more; no service fixing within two metres either side of it.
            foreach (HomeNote h in layout.Extras.Homes)
                foreach (var (a, b, o) in h.Exits)
                {
                    exits++;
                    Int3 ov = o.Vec(), along = b - a;
                    int open = 0;
                    while (open < 6 && !BlockInfo.Collides(g.Get(a + Int3.Up * open).Type) && !BlockInfo.Collides(g.Get(b + Int3.Up * open).Type)) open++;
                    if (open != DoorSize.Tall) { offSize++; Note($"{h.Silhouette}: a door {open} m tall at {a}"); }
                    bool clearHead = true;
                    for (int d = 1; d <= DoorSize.Approach && clearHead; d++)
                        foreach (Int3 c in new[] { a, b })
                            for (int y = 1; y < DoorSize.Headroom && clearHead; y++)
                                if (BlockInfo.Collides(g.Get(c + ov * d + Int3.Up * y).Type)) { clearHead = false; Note($"{h.Silhouette}: {g.Get(c + ov * d + Int3.Up * y)} {y} m over the way out of the door at {a}"); if (noHeadroom < 4) GD.Print($"      in the way: {h.Family} {h.Silhouette}: {g.Get(c + ov * d + Int3.Up * y)} {y} m up, {d} m out of the door at {a} facing {o}"); }
                    if (!clearHead) noHeadroom++;
                    foreach (ServicePoint p in layout.Extras.Service)
                    {
                        Int3 rel = p.Cell - a;
                        int lat = rel.X * along.X + rel.Z * along.Z, outd = rel.X * ov.X + rel.Z * ov.Z;
                        if (lat >= -2 && lat <= 3 && outd >= -1 && outd <= 3) { overDoor++; Note($"{h.Silhouette}: the service fixing at {p.Cell} is over the door at {a}"); }
                    }
                }
            if (layout.Extras.Homes.FirstOrDefault() is { } note) fence.Lot(g, note, Note);
            // None framed in a kit's hazard yellow, each doorway the standard height.
            var hazard = new HashSet<Rgb> { new(0xe0, 0xa8, 0x00), new(0xc9, 0xa2, 0x27) };
            foreach (Doorway d in layout.Doorways)
            {
                Int3 lo = d.Cells.Aggregate(Int3.Min), hi = d.Cells.Aggregate(Int3.Max);
                if (hi.Y - lo.Y + 1 != DoorSize.Tall) { lowDoor++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: a door {hi.Y - lo.Y + 1} m tall at {lo}"); }
                foreach (Int3 c in d.Cells)
                    foreach (Dir side in Dirs.Horizontal)
                        if (hazard.Contains(g.Get(c + side.Vec()).Color) && g.Get(c + side.Vec()).Role == Role.Wall) { framed++; Note($"a hazard-yellow door frame at {c + side.Vec()}"); goto doneFrame; }
                doneFrame:;
            }
            var loose = Structure.Floating(g, layout.Occupied);
            if (loose.Count > 0) { floating++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: {g.Get(loose[0])} floats at {loose[0]}"); }
            // A fixing for every house's service drop, a bracket or a pole, facing level.
            if (layout.Extras.Service.Count == 0) { noService++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: no service fixing"); }
            // An insulator at the roof's street-side corner, on a slim mast up through the roof's edge or the parapet, or hung
            // under the eave's corner; a pole in the garden only where no corner will take it. Never the old cross-shaped lump.
            foreach (ServicePoint p in layout.Extras.Service)
            {
                Block under = g.Get(p.Cell - Int3.Up), over = g.Get(p.Cell + Int3.Up);
                if (!p.Facing.IsHorizontal() || g.Get(p.Cell) is not { Type: BlockType.Pillar, Material: PoiGenerator.Gen.Material.Ceramic } || under.Type != BlockType.Pillar && over.Role != Role.Roof)
                { badService++; Note($"service fixing at {p.Cell} is {g.Get(p.Cell)} on {under} under {over} facing {p.Facing}"); continue; }
                if (under.Type != BlockType.Pillar) { fixings["eave bracket"] = fixings.GetValueOrDefault("eave bracket") + 1; continue; }
                Int3 c = p.Cell - Int3.Up;
                while (g.Get(c - Int3.Up).Type == BlockType.Pillar) c -= Int3.Up;
                bool pole = !BlockInfo.Collides(g.Get(c - Int3.Up).Type) || g.Get(c - Int3.Up).Role is not (Role.Roof or Role.Wall);
                string kind = pole ? "garden pole" : g.Get(c - Int3.Up).Role == Role.Wall ? "parapet mast" : "roof mast";
                fixings[kind] = fixings.GetValueOrDefault(kind) + 1;
            }
        }
        GD.Print($"      {houses} houses, {garages} garages in {sw.ElapsedMilliseconds} ms: {string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}; {singles} single, {doubles} double");
        GD.Print($"      fences: {string.Join(", ", fence.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        GD.Print($"      service fixings: {string.Join(", ", fixings.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        Check(fixings.GetValueOrDefault("garden pole") * 4 < fixings.Values.Sum() && fixings.Count >= 3, $"the service fixing is at the roof's street-side corner, the garden pole only a fallback: {string.Join(", ", fixings.Select(k => $"{k.Key} {k.Value}"))}");
        Check(exits >= houses && offSize == 0, $"every door out of a house is the standard {DoorSize.Tall} m door: {exits} doors, {offSize} not{first}");
        Check(noHeadroom == 0, $"nothing comes lower than {DoorSize.Headroom} m over a door or {DoorSize.Approach} m in front of it: {noHeadroom} of {exits} doors with something in the way{first}");
        Check(overDoor == 0, $"no service fixing within 2 m either side of a door: {overDoor}{first}");
        fence.Report(Check, first);
        Check(badSize == 0 && garages > 60, $"garage doors are real size: {garages} doors, {badSize} not 4, 6 or 8 m wide by 3 m{first}");
        Check(kinds.Count >= 6 && kinds.Values.All(n => n >= garages / 40) && singles > garages / 8 && doubles > garages / 8,
            $"garage doors come in every kind (panelled, ribbed, glazed, timber, side-hung, carport) and both widths: {kinds.Count} kinds, {singles} single, {doubles} double");
        Check(noDrive == 0, $"every garage has a drive from the lot's front to its door: {noDrive} of {garages} without{first}");
        Check(unreached == 0, $"every house is walked into through its door, every room reached: {unreached} of {houses} not{first}");
        Check(floating == 0, $"nothing floats in a house as built: {floating} of {houses} with a block in the air{first}");
        Check(lowHatch == 0 && lowDoor == 0, $"every opening a person can reach is a window, a door or a garage door at full height: {lowHatch} houses with an opening under 3 m, {lowDoor} doors under 3 m{first}");
        Check(framed == 0, $"no house door is framed in a kit's hazard yellow: {framed}{first}");
        Check(noService == 0 && badService == 0, $"every house has a service fixing facing out: {noService} without, {badService} wrong{first}");

        // A street of one culture: eight neighbours, no two alike side by side, most of them different, all of the culture's few families.
        // Its fences: the culture's one or two kinds on most fenced lots, the odd one out from what suits it, some lots open.
        int streets = 0, alikeNext = 0, pairs = 0, fewShapes = 0, offCulture = 0, fenced = 0, mainFence = 0, strayFence = 0, openLots = 0;
        string example = "", fenceExample = "";
        for (int s = 0; s < 24; s++)
        {
            var home = new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble());
            var tech = (TechLevel)rand.Next(3);
            var notes = new List<HomeNote>();
            for (int k = 0; k < 8; k++)
            {
                Layout layout = PoiFactory.BuildLayout(_lib, fam, 0, Rarity.Common, (ulong)rand.NextInt64(), new LayoutHints(Dir.North, tech, null, home));
                notes.Add(layout.Extras.Homes[0]);
            }
            streets++;
            for (int k = 1; k < notes.Count; k++) { pairs++; if (notes[k].Silhouette == notes[k - 1].Silhouette) alikeNext++; }
            if (notes.Select(n => n.Silhouette).Distinct().Count() < 6) fewShapes++;
            var culture = HomeCulture.Of(_lib.Homes, home, tech);
            if (notes.Any(n => culture.Families.All(f => f.Family.Id != n.Family) && culture.Odd.All(f => f.Id != n.Family))) offCulture++;
            foreach (HomeNote n in notes)
            {
                if (n.FenceKind == "none") { openLots++; continue; }
                fenced++;
                if (culture.Fences.Any(x => x.Kind.Id == n.FenceKind)) mainFence++;
                else if (culture.FenceOdd.All(x => x.Id != n.FenceKind) && _lib.Homes.Find(n.Family)?.Allows("fence", n.FenceKind) != true) { strayFence++; Note($"a {n.Family} on a street of {string.Join("/", culture.Fences.Select(x => x.Kind.Id))} fenced in {n.FenceKind}"); }
            }
            if (example.Length == 0) example = string.Join(" | ", notes.Take(4).Select(n => n.Silhouette));
            if (fenceExample.Length == 0) fenceExample = $"{string.Join("/", culture.Fences.Select(x => x.Kind.Id))}: {string.Join(" ", notes.Select(n => n.FenceKind))}";
        }
        GD.Print($"      a street: {example}");
        GD.Print($"      a street's fences: {fenceExample}");
        Check(alikeNext <= pairs / 20 && fewShapes <= streets / 12, $"a street's neighbours rarely match: {alikeNext} of {pairs} next-door pairs alike, {fewShapes} of {streets} streets with fewer than 6 shapes in 8");
        Check(offCulture == 0, $"a street reads as one place: every house from its culture's own families (or a rare odd one), {offCulture} of {streets} streets strayed");
        Check(strayFence == 0 && mainFence >= fenced * 0.6f && openLots > 0 && openLots < (fenced + openLots) / 2,
            $"a street's fences come from its culture: {mainFence} of {fenced} fenced lots in its one or two kinds, {strayFence} from outside it, {openLots} lots open{first}");

        // A town: streets in one weather and tech, each its own culture, pick their fences apart.
        int towns = 0, samey = 0, nextSame = 0, nextPairs = 0;
        for (int t = 0; t < 12; t++)
        {
            var climate = (Climate)rand.Next(4);
            var tech = (TechLevel)rand.Next(3);
            var main = new List<string>();
            for (int s = 0; s < 8; s++)
            {
                var c = HomeCulture.Of(_lib.Homes, new HomeHints(rand.Next(), climate, 0.25f + 0.5f * (float)rand.NextDouble()), tech);
                main.Add(c.Fences.Count > 0 ? c.Fences[0].Kind.Id : "none");
            }
            towns++;
            if (main.Distinct().Count() < 3) samey++;
            for (int s = 1; s < main.Count; s++) { nextPairs++; if (main[s] == main[s - 1]) nextSame++; }
        }
        Check(samey <= towns / 6 && nextSame < nextPairs / 2, $"fences vary between a town's streets: {samey} of {towns} towns with fewer than 3 kinds over 8 streets, {nextSame} of {nextPairs} next streets alike");

        // The odd domes are domes: a rounded top of many cells, not a pyramid's point.
        int domes = 0, pointed = 0;
        foreach (string odd in new[] { "mushroom", "saucer", "pod" })
            for (int k = 0; k < 4; k++)
            {
                Layout layout;
                try { layout = PoiFactory.BuildLayout(_lib, fam, 0, Rarity.Common, (ulong)rand.NextInt64(), new LayoutHints(Dir.North, TechLevel.High, null, new HomeHints(rand.Next(), Climate.Temperate, 0.6f, odd))); }
                catch (InvalidOperationException e) { domes++; pointed++; GD.Print($"      {odd} failed: {e.Message}"); continue; }
                if (layout.Extras.Homes.FirstOrDefault()?.Family != odd) continue;
                domes++;
                var roof = layout.Blocks.Blocks().Where(x => x.Block.Role == Role.Roof).ToList();
                int top = roof.Max(x => x.Cell.Y);
                int crown = roof.Count(x => x.Cell.Y == top);
                if (crown < 9) { pointed++; Note($"{odd}: {crown} roof cells at its top"); }
            }
        Check(domes >= 6 && pointed == 0, $"mushroom, saucer and pod roofs are rounded domes: {pointed} of {domes} come to a point{first}");

        // City lots: each house a house of the rules, its service fixing too.
        int city = 0, cityNoNote = 0, cityNoService = 0;
        var cityFence = new FenceTally();
        foreach (string kind in new[] { "house", "terrace" })
        {
            FamilyDef f = _lib.Families.First(x => x.Id == "city_" + kind);
            for (int k = 0; k < 30; k++)
            {
                var lot = new CityLot(kind, kind == "house" ? rand.Next(10, 13) : new[] { 9, 10, 13, 14, 17 }[rand.Next(5)], 17, rand.Next(1, 4), rand.Next(1000), (float)rand.NextDouble(), Home: new HomeHints(rand.Next(), (Climate)rand.Next(4), (float)rand.NextDouble()));
                Layout layout = PoiFactory.BuildLayout(_lib, f, 0, f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[k % 4], (TechLevel)rand.Next(3), lot));
                city++;
                if (layout.Extras.Homes.Count == 0) cityNoNote++;
                if (layout.Extras.Service.Count == 0) cityNoService++;
                if (layout.Extras.Homes.FirstOrDefault() is { } n) cityFence.Lot(layout.Blocks, n, Note);
            }
        }
        Check(cityNoNote == 0 && cityNoService == 0, $"city houses and terraces come from the house rules with their service fixings: {city} lots, {cityNoNote} not, {cityNoService} without a fixing");
        GD.Print($"      city fences: {string.Join(", ", cityFence.Kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        cityFence.Report((ok, what) => Check(ok, "city lots: " + what), first, 15);
    }

    /// <summary>
    /// The fence rules on each lot as built: straight runs along the lot lines meeting at corners on one shared post,
    /// never crossing; each run starting and ending on a post (or the wall or hedge itself), no holes along it; every
    /// panel the mesher draws along its run (it turns a pane by what stands beside it), every railing and gate leaf
    /// facing along its own, never a cross-shaped rail; openings only where the path or drive is paved through, a gate
    /// post either side; one side line fenced on a street (the neighbour fences the other), both on a lot alone.
    /// </summary>
    sealed class FenceTally
    {
        public readonly Dictionary<string, int> Kinds = new();
        int lots, crooked, badEnd, crossing, across, turnstile, holes, badOpening, badGate, sides;

        public void Lot(BlockGrid g, HomeNote h, Action<string> note)
        {
            Kinds[h.FenceKind] = Kinds.GetValueOrDefault(h.FenceKind) + 1;
            if (h.Fence is not { } f) return;
            lots++;
            // A post is a whole cell, so the boards run flush into it: nothing stops short of its post.
            bool Post(Int3 c) => g.Get(c).Type == BlockType.Cube;
            var owners = new Dictionary<Int3, List<int>>();
            for (int i = 0; i < f.Runs.Count; i++)
            {
                var run = f.Runs[i];
                for (int j = 1; j < run.Count; j++)
                {
                    Int3 d = run[j] - run[j - 1];
                    if (Math.Abs(d.X) + Math.Abs(d.Z) != 1 || d.Y != 0 || j > 1 && d != run[1] - run[0]) { crooked++; note($"{f.Kind}: a crooked run at {run[j]}"); break; }
                }
                foreach (Int3 c in run)
                {
                    if (!owners.TryGetValue(c, out var list)) owners[c] = list = new List<int>();
                    list.Add(i);
                }
                if (f.Body != "edging")
                    foreach (Int3 end in new[] { run[0], run[^1] })
                        if (!(f.Body is "panel" or "railing" ? Post(end) : BlockInfo.Collides(g.Get(end).Type))) { badEnd++; note($"{f.Kind}: a run ends on {g.Get(end)} at {end}, not a post"); }
            }
            foreach (var (c, list) in owners)
            {
                if (list.Count < 2) continue;
                if (list.Any(i => f.Runs[i][0] != c && f.Runs[i][^1] != c)) { crossing++; note($"{f.Kind}: two runs cross at {c}"); }
                else if (f.Body is "panel" or "railing" && !Post(c)) { crossing++; note($"{f.Kind}: a corner at {c} without its post"); }
            }
            void Along(Int3 p, bool alongX)
            {
                Block b = g.Get(p);
                if (b.Type == BlockType.Rail) { turnstile++; note($"{f.Kind}: a cross-shaped rail at {p}"); }
                else if (b.Type == BlockType.Pane && (!g.Get(p + Dir.East.Vec()).IsAir || !g.Get(p + Dir.West.Vec()).IsAir) != alongX) { across++; note($"{f.Kind}: a panel across its run at {p}"); }
                else if (b.Type == BlockType.Ladder && (b.Facing is Dir.North or Dir.South) != alongX) { across++; note($"{f.Kind}: railings or a gate across their run at {p}"); }
            }
            foreach (var run in f.Runs)
            {
                bool alongX = run[0].X != run[^1].X;
                foreach (Int3 c in run)
                {
                    if (f.Body != "edging" && g.Get(c).IsAir) { holes++; note($"{f.Kind}: a hole in a run at {c}"); }
                    for (int y = 0; y < f.Tall + 1; y++) Along(c + Int3.Up * y, alongX);
                }
            }
            foreach (var leaf in f.Leaves.Where(x => x.Count > 1))
                foreach (Int3 c in leaf)
                    for (int y = 0; y < 2; y++) Along(c + Int3.Up * y, leaf[0].X != leaf[^1].X);
            foreach (var open in f.Openings)
                foreach (Int3 c in open)
                    if (g.Get(c).Type != BlockType.Plate || Enumerable.Range(0, f.Tall).Any(y => BlockInfo.Collides(g.Get(c + Int3.Up * y).Type))) { badOpening++; note($"{f.Kind}: an opening at {c} with no path through it"); break; }
            if (f.Body != "edging")
                foreach (var (a, b) in f.Gates)
                    if (!Post(a) && !(f.Body is "wall" && BlockInfo.Collides(g.Get(a).Type)) || !Post(b) && !(f.Body is "wall" && BlockInfo.Collides(g.Get(b).Type))) { badGate++; note($"{f.Kind}: a gate at {a}..{b} without its posts"); }
            if (f.SideLines != (f.BothSides ? 2 : 1)) { sides++; note($"{f.Kind}: {f.SideLines} side lines fenced, {(f.BothSides ? "alone" : "on a street")}"); }
        }

        public void Report(Action<bool, string> check, string first, int min = 40)
        {
            check(lots >= min, $"enough fences built to judge them: {lots}");
            check(crooked == 0 && crossing == 0, $"fences follow the lot lines and meet at corners on one shared post, never crossing: {crooked} crooked runs, {crossing} crossings in {lots} fences{first}");
            check(badEnd == 0 && holes == 0, $"every fence run starts and ends on a post (or the wall or hedge itself), nothing missing along it: {badEnd} loose ends, {holes} holes{first}");
            check(across == 0 && turnstile == 0, $"every panel, railing and gate leaf runs along its run, in every direction: {across} across, {turnstile} cross-shaped rails{first}");
            check(badOpening == 0 && badGate == 0, $"a fence opens only where the path or drive goes through, a gate post either side: {badOpening} openings off a path, {badGate} without posts{first}");
            check(sides == 0, $"a street lot fences one side line and its neighbour the other, a lot alone both: {sides} wrong{first}");
        }
    }

    /// <summary>Whether paving runs at ground level from a cell to beside the gate.</summary>
    static bool Driven(BlockGrid g, Int3 from, Int3 gate)
    {
        bool Paved(Int3 c) => g.Get(c).Type == BlockType.Plate;
        if (!Paved(from)) return false;
        var seen = new HashSet<Int3> { from };
        var q = new Queue<Int3>();
        q.Enqueue(from);
        while (q.Count > 0 && seen.Count < 4000)
        {
            Int3 c = q.Dequeue();
            if (Math.Abs(c.X - gate.X) + Math.Abs(c.Z - gate.Z) <= 1) return true;
            foreach (Dir d in Dirs.Horizontal)
            {
                Int3 n = c + d.Vec();
                if (Paved(n) && seen.Add(n)) q.Enqueue(n);
            }
        }
        return false;
    }
}
