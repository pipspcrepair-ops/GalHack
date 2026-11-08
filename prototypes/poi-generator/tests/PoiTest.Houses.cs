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
        int houses = 0, garages = 0, badSize = 0, noDrive = 0, unreached = 0, floating = 0, noService = 0, badService = 0;
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
            var loose = Structure.Floating(g, layout.Occupied);
            if (loose.Count > 0) { floating++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: {g.Get(loose[0])} floats at {loose[0]}"); }
            // A fixing for every house's service drop, a bracket or a pole, facing level.
            if (layout.Extras.Service.Count == 0) { noService++; Note($"{layout.Extras.Homes.FirstOrDefault()?.Silhouette}: no service fixing"); }
            foreach (ServicePoint p in layout.Extras.Service)
                if (!p.Facing.IsHorizontal() || g.Get(p.Cell).Type is not (BlockType.Pipe or BlockType.Pillar)) { badService++; Note($"service fixing at {p.Cell} is {g.Get(p.Cell)} facing {p.Facing}"); }
        }
        GD.Print($"      {houses} houses, {garages} garages in {sw.ElapsedMilliseconds} ms: {string.Join(", ", kinds.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}; {singles} single, {doubles} double");
        Check(badSize == 0 && garages > 60, $"garage doors are real size: {garages} doors, {badSize} not 4, 6 or 8 m wide by 3 m{first}");
        Check(kinds.Count >= 6 && kinds.Values.All(n => n >= garages / 40) && singles > garages / 8 && doubles > garages / 8,
            $"garage doors come in every kind (panelled, ribbed, glazed, timber, side-hung, carport) and both widths: {kinds.Count} kinds, {singles} single, {doubles} double");
        Check(noDrive == 0, $"every garage has a drive from the lot's front to its door: {noDrive} of {garages} without{first}");
        Check(unreached == 0, $"every house is walked into through its door, every room reached: {unreached} of {houses} not{first}");
        Check(floating == 0, $"nothing floats in a house as built: {floating} of {houses} with a block in the air{first}");
        Check(noService == 0 && badService == 0, $"every house has a service fixing facing out: {noService} without, {badService} wrong{first}");

        // A street of one culture: eight neighbours, no two alike side by side, most of them different, all of the culture's few families.
        int streets = 0, alikeNext = 0, pairs = 0, fewShapes = 0, offCulture = 0;
        string example = "";
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
            if (example.Length == 0) example = string.Join(" | ", notes.Take(4).Select(n => n.Silhouette));
        }
        GD.Print($"      a street: {example}");
        Check(alikeNext == 0 && fewShapes <= streets / 12, $"a street's neighbours differ: {alikeNext} of {pairs} next-door pairs alike, {fewShapes} of {streets} streets with fewer than 6 shapes in 8");
        Check(offCulture == 0, $"a street reads as one place: every house from its culture's own families (or a rare odd one), {offCulture} of {streets} streets strayed");

        // City lots: each house a house of the rules, its service fixing too.
        int city = 0, cityNoNote = 0, cityNoService = 0;
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
            }
        }
        Check(cityNoNote == 0 && cityNoService == 0, $"city houses and terraces come from the house rules with their service fixings: {city} lots, {cityNoNote} not, {cityNoService} without a fixing");
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
