using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Faction bases as docs/place-patterns.md sections 1.3 and 2.5 have them (rule E5, the owner's core ruling of 13 Nov),
    /// on fresh held places of every program that keeps a core vault, each at a tech its world could have:
    /// <list type="bullet">
    /// <item>its program's rooms are all there (command, barracks, armoury, workshop, power and fuel, a vehicle bay, a
    /// medical bay and the core vault), and its plan is 40 to 110 m on its longest side;</item>
    /// <item>the core stands in the core vault, deep and low (in from the outer third of the plan, in the lower half of
    /// its height), 10 m or more from the nearest cache;</item>
    /// <item>the vault is reached on foot from the way in through doors that open by hand, the core is seen from a
    /// standing spot in it, and any case round it is glass;</item>
    /// <item>the place's heaviest guard stands in the vault, and its turrets are out of doors on the upper half.</item>
    /// </list>
    /// </summary>
    void HeldPlaces()
    {
        var rand = new Random();
        string[] program = { "command", "quarters", "armoury", "workshop", "generator", "plant", "vehicle-bay", "ward", "core-vault" };
        var faults = new Dictionary<string, List<string>>();
        void Fault(string kind, string where) { if (!faults.TryGetValue(kind, out var l)) faults[kind] = l = new(); l.Add(where); }
        int places = 0, cased = 0;
        var sizes = new List<int>();
        foreach (FamilyDef f in _lib.Families.Where(f => f.Program != null && _lib.Programs[f.Program].Bands.Values.SelectMany(b => b).Any(d => d.Room == "core-vault")))
            for (int i = 0; i < 8; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
                if (layout == null) { Fault("not laid out", f.Id); continue; }
                PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Active, (ulong)rand.NextInt64());
                if (!inst.HasCore) continue;
                places++;
                string at = $"{f.Id} {tech}";
                var purposes = layout.Modules.Select(m => m.Purpose).ToHashSet();
                foreach (string p in program.Where(p => !purposes.Contains(p))) Fault($"no {p}", at);

                // The plan: what it built above and below ground.
                Int3 lo = new(int.MaxValue, int.MaxValue, int.MaxValue), hi = new(int.MinValue, int.MinValue, int.MinValue);
                foreach (var (c, b) in inst.Blocks.Blocks()) if (BlockInfo.IsStructure(b.Role)) { lo = Int3.Min(lo, c); hi = Int3.Max(hi, c); }
                int longest = Math.Max(hi.X - lo.X, hi.Z - lo.Z) + 1;
                sizes.Add(longest);
                if (longest < 40 || longest > 110) Fault("plan off 40-110 m", $"{at} {longest} m");

                // The core: in its vault, deep and low, away from the loot.
                Int3 core = inst.CoreCell;
                int cm = layout.CoreModule;
                if (cm < 0 || layout.Modules[cm].Purpose != "core-vault" || !layout.Modules[cm].Cells.Contains(core)) Fault("core not in the core vault", at);
                // In from the outer third of its own level's plan (a basement may be the main bar alone).
                var level = layout.Modules.Where(m => cm >= 0 && m.Storey == layout.Modules[cm].Storey).SelectMany(m => m.Cells).ToList();
                Int3 l0 = level.Aggregate(core, Int3.Min), l1 = level.Aggregate(core, Int3.Max);
                float ex = Math.Abs(core.X - (l0.X + l1.X) / 2f) / Math.Max(1f, (l1.X - l0.X) / 2f), ez = Math.Abs(core.Z - (l0.Z + l1.Z) / 2f) / Math.Max(1f, (l1.Z - l0.Z) / 2f);
                if (Math.Max(ex, ez) > 2f / 3f) Fault("core in the plan's outer third", at);
                if (core.Y - lo.Y > (hi.Y - lo.Y) / 2) Fault("core in the upper half", at);
                if (inst.Caches.Any(k => Dist(k.Cell, core) < 10f)) Fault("a cache within 10 m of the core", at);

                // Reached through doors that open, seen from inside, cased only in glass.
                var nav = inst.Nav();
                var reach = nav.Standable(layout.EntranceCell) ? nav.Reachable(layout.EntranceCell) : new HashSet<Int3>();
                var stands = cm < 0 ? new List<Int3>() : layout.Modules[cm].Cells.Where(reach.Contains).ToList();
                if (stands.Count == 0) Fault("core vault not reached on foot", at);
                if (!stands.Any(s => Sees(inst.Blocks, s + Int3.Up, core))) Fault("core not seen from its vault", at);
                if (inst.CoreCase.Count > 0) cased++;
                if (inst.CoreCase.Any(c => !inst.Blocks.Get(c).IsAir && !BlockInfo.IsTransparent(inst.Blocks.Get(c).Material))) Fault("core cased in something not glass", at);

                // The guards and the guns.
                var guards = inst.Enemies.Where(e => e.Kind == EnemyKind.Guard).GroupBy(e => e.Module).ToDictionary(g => g.Key, g => g.Count());
                if (guards.Count > 0 && guards.GetValueOrDefault(cm) < guards.Values.Max()) Fault("the core vault not the most guarded room", at);
                foreach (var t in inst.Enemies.Where(e => e.Kind == EnemyKind.Turret && e.Module != cm))
                    if (t.Cell.Y - lo.Y < (hi.Y - lo.Y) / 2 || !inst.Blocks.Get(t.Cell).IsAir) Fault("a turret low or not out of doors", at);
            }
        static float Dist(Int3 a, Int3 b) => MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
        sizes.Sort();
        GD.Print($"    {places} held places, {cased} cores in glass cases, longest side p10 {(sizes.Count > 0 ? sizes[sizes.Count / 10] : 0)} median {(sizes.Count > 0 ? sizes[sizes.Count / 2] : 0)} m");
        foreach (var kv in faults.OrderByDescending(k => k.Value.Count))
            GD.Print($"      {kv.Value.Count,4}  {kv.Key}: {string.Join(", ", kv.Value.Take(4))}");
        Check(places > 0 && faults.Count == 0, $"faction bases keep their program, size, core vault (deep, low, away from the loot, reached, seen, cased only in glass) and guard: {faults.Sum(k => k.Value.Count)} faults in {places} places");
    }

    /// <summary>Whether a straight sight line from a standing eye to the core's middle passes only air, glass and the core.</summary>
    static bool Sees(BlockGrid g, Int3 eye, Int3 core)
    {
        var a = new System.Numerics.Vector3(eye.X + 0.5f, eye.Y + 0.6f, eye.Z + 0.5f);
        var b = new System.Numerics.Vector3(core.X + 0.5f, core.Y + 1f, core.Z + 0.5f);
        int steps = (int)(System.Numerics.Vector3.Distance(a, b) * 4) + 1;
        for (int i = 1; i < steps; i++)
        {
            var p = System.Numerics.Vector3.Lerp(a, b, i / (float)steps);
            var c = new Int3((int)MathF.Floor(p.X), (int)MathF.Floor(p.Y), (int)MathF.Floor(p.Z));
            if (c == core) return true;
            Block k = g.Get(c);
            if (!k.IsAir && BlockInfo.Collides(k.Type) && !BlockInfo.IsTransparent(k.Material)) return false;
        }
        return true;
    }
}
