using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The capital hulks (src/Gen/Arch/CapitalHull.cs) on fresh seeds, every crash variant. The hull is the player's own
/// large-grid blocks: length and beam in range, three decks and the tower's two, thrusters at the stern and lift
/// enough under the keel for the hull's weight, the bridge's chairs behind glass, generators, fuel and containers,
/// no core but an empty seat for one, and what the crash and the years leave is worth the work. The place round
/// it (the hollow it lies in, its ramps, its yard and wreckage) passes the layout checks, and each way of coming
/// down is a pose of its own. (The vessel's own claiming and flying are the slice's HulkTest.)
/// </summary>
public partial class PoiTest
{
    static double PitchOf(System.Numerics.Quaternion q) => Math.Atan2(2 * (q.W * q.X + q.Y * q.Z), 1 - 2 * (q.X * q.X + q.Y * q.Y)) * 180 / Math.PI;

    void Hulks()
    {
        FamilyDef fam = _lib.Families.First(f => f.Id == "crashed_freighter");
        int bad = 0, built = 0;
        var problems = new List<string>();
        var poses = new List<(string Name, string Pose)>();
        foreach (VariantDef v in fam.Variants)
            for (int k = 0; k < 2; k++)
            {
                ulong seed = (ulong)Random.Shared.NextInt64();
                Layout layout;
                try { layout = PoiFactory.BuildLayout(_lib, fam, v.Index, fam.Rarity, seed, new LayoutHints(Dir.North)); }
                catch (Exception e) { bad++; problems.Add($"{v.Name} seed {seed}: {e.Message}"); continue; }
                built++;
                void Bad(bool ok, string what) { if (!ok) { bad++; problems.Add($"{v.Name} seed {seed}: {what}"); } }
                CapitalHull.HullPlan plan = CapitalHull.Of(layout);
                Bad(plan != null, "no vessel plan for the place");
                if (plan == null) continue;
                var all = plan.Pieces.SelectMany(p => p.Intact).ToList();
                int Count(string id) => all.Count(kv => kv.Value == id || kv.Value.StartsWith(id + " "));
                int length = plan.Pieces.Sum(p => (p.Intact.Keys.Max(c => c.Z) - p.Intact.Keys.Min(c => c.Z) + 1) * 2);
                int beam = plan.Pieces.Max(p => (p.Intact.Keys.Max(c => c.X) - p.Intact.Keys.Min(c => c.X) + 1) * 2);
                int decks = all.Where(kv => kv.Value.Contains("paint=deck")).Select(kv => kv.Key.Y).Distinct().Count();
                var last = plan.Pieces[^1];
                int sternZ = last.Intact.Keys.Max(c => c.Z);
                var aft = last.Intact.Where(kv => kv.Value == "thruster").Select(kv => kv.Key).ToList();
                double mass = all.Sum(kv => kv.Value.StartsWith("steel") ? 1 : kv.Value.StartsWith("glass") ? 0.6 : kv.Value.StartsWith("generator") ? 6 : kv.Value.StartsWith("thruster") ? 3 : 2);
                int lift = Count("thruster rot=+z+y");
                double power = Count("generator") * 12, draw = Count("thruster") * 1.5;
                bool core = all.Any(kv => kv.Value == "core");
                int slots = plan.Pieces.Count(p => p.CoreSlot is Int3 s && !p.Intact.ContainsKey(s));
                var left = plan.Pieces.Select(p => (p, gone: p.Damage(PoiState.Abandoned, seed))).ToList();
                int Left(string id) => left.Sum(x => x.p.Intact.Count(kv => (kv.Value == id || kv.Value.StartsWith(id + " ")) && !x.gone.Contains(kv.Key)));
                GD.Print($"      {v.Name}: {plan.Pieces.Count} piece(s), {all.Count} blocks, {length} m long, {beam} m beam, {decks} floor levels, {aft.Count} stern thrusters, {lift} lift ({lift * 1.2:F0} MN for {mass * 9.81 / 1000:F0} MN), {Count("generator")} generators ({power:F0} of {draw:F0} MW), {Count("fueltank")} fuel tanks, {Count("container")} containers, {Count("chair")} chairs, {Count("glass")} glass, {Count("door")} doors; abandoned keeps {Left("thruster")} thrusters, {Left("generator")} generators; pitch {string.Join("/", plan.Pieces.Select(p => PitchOf(p.Rotation).ToString("F1")))} deg, origin y {string.Join("/", plan.Pieces.Select(p => p.Origin.Y.ToString("F0")))}");
                Bad(length is >= 150 and <= 260, $"length {length} m outside 150 to 260");
                Bad(beam is >= 36 and <= 64, $"beam {beam} m outside 36 to 64");
                Bad(decks >= 3, $"only {decks} floor levels");
                Bad(aft.Count >= 20 && aft.All(c => c.Z >= sternZ - 2), $"{aft.Count} stern thrusters, not all at the stern");
                Bad(lift * 1200.0 >= mass * 9.81 * 1.2, $"lift {lift * 1.2:F0} MN under the weight {mass * 9.81 / 1000:F0} MN");
                Bad(power >= draw, $"generators {power:F0} MW for {draw:F0} MW of thrusters");
                Bad(Count("chair") >= 2 && Count("glass") >= 6 && Count("fueltank") >= 1 && Count("container") >= 10 && Count("door") >= 10, "a device kind missing");
                Bad(!core && slots >= 1, $"core {core}, {slots} empty seats for one");
                Bad(Left("thruster") >= Count("thruster") / 4 && Left("generator") >= 1 && Left("chair") >= 1, "too little survives to restore");
                var walk = LayoutCheck.Check(layout);
                Bad(walk.Count == 0, "layout: " + string.Join("; ", walk.Take(3)));
                PoiInstance inst = PoiFactory.Spawn(_lib, layout, PoiState.Abandoned, seed + 1);
                var valid = Validator.Check(inst);
                Bad(valid.Count == 0, "spawn: " + string.Join("; ", valid.Take(3)));
                Bad(layout.Occupied.Min.Y < -3, "no hollow cut in the ground for the buried hull");
                if (k == 0) poses.Add((v.Name, $"{plan.Pieces.Count}:" + string.Join(",", plan.Pieces.Select(p => $"{Math.Round(PitchOf(p.Rotation))}@{Math.Round(p.Origin.Y)}"))));
            }
        int alike = 0;
        for (int i = 0; i < poses.Count; i++)
            for (int j = i + 1; j < poses.Count; j++)
                if (poses[i].Pose == poses[j].Pose) { alike++; problems.Add($"{poses[i].Name} and {poses[j].Name} lie alike ({poses[i].Pose})"); }
        foreach (string p in problems.Take(12)) GD.PrintErr("      " + p);
        Check(bad == 0 && alike == 0 && built == fam.Variants.Count * 2, $"capital hulks: {built} vessels of the player's blocks on fresh seeds, sizes in range, stern engines and lift for their weight, power, chairs behind glass, an empty core seat, enough left to restore; {poses.Count} poses all different");
    }
}
