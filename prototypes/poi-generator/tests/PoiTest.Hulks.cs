using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// The capital hulks' plans (src/Gen/Arch/CapitalHull.cs) on fresh seeds, every crash variant: length, beam and
/// decks in the size class's range (Empyrion's capital wrecks and derelicts, place-patterns 1.4), the role's bays
/// stem to stern with the crew, power and engine bays and a hall, a tower, nacelles and the ways in; the place round
/// it (the ground it lies in, its ramps, yard and wreckage) passes the layout checks; and each way of coming down is
/// a pose of its own. The vessel's own blocks, rooms and dressing are the slice's HulkTest.
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
                var (l0, l1, h0, h1, decks) = CapitalHull.SizeOf(plan.Size);
                GD.Print($"      {v.Name}: {plan.Role} {plan.Size}, {plan.Pieces.Count} piece(s), {plan.L + plan.Nac} m long, {2 * plan.Half + 1} m beam, {plan.Decks} decks {CapitalHull.HullPlan.Pitch} m apart, " +
                         $"{plan.TowerTop + 1} m to the tower top; bays {string.Join(" ", plan.Bays.Select(b => $"{b.Kind} {b.V1 - b.V0}"))}; ways {string.Join(", ", plan.Ways.Select(w => w.What))}; " +
                         $"pitch {string.Join("/", plan.Pieces.Select(p => PitchOf(p.Rotation).ToString("F1")))} deg");
                Bad(plan.L >= l0 && plan.L <= Math.Max(l1, 270) && plan.Half >= h0 && plan.Half <= h1 && plan.Decks == decks, $"{plan.L} m long, half-beam {plan.Half}, {plan.Decks} decks: outside the {plan.Size} class");
                Bad(new[] { "bow", "crew", "power", "engine" }.All(b => plan.Bays.Any(x => x.Kind == b)) && plan.Bays.Any(b => b.Hall && b.Kind != "engine"), "a bay missing");
                Bad(plan.TV1 > plan.TV0 + 8 && plan.Nacelles.Count >= 2 && plan.Ways.Any(w => w.What == "cargo") && plan.DoorV > 0, "no tower, nacelles, cargo ramp or hull door");
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
        Check(bad == 0 && alike == 0 && built == fam.Variants.Count * 2, $"capital hulks: {built} plans on fresh seeds, sizes in their class, the role's bays with crew, power, engines and a hall, a tower, nacelles and ways in; {poses.Count} poses all different");
    }
}
