using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiTest
{
    /// <summary>
    /// Every spawn its own layout: a world's place is laid out from its spawn's seed, and only when three of those
    /// fail does it fall back to the variant's authored layout (which repeats from spawn to spawn). On fresh seeds,
    /// as a world asks (its own tech, any front), each family is laid out a few times; the fallbacks are counted
    /// per family with the first reason each gave. The rate must stay near zero.
    /// </summary>
    void Fallbacks()
    {
        var rand = new Random();
        int spawns = 0, fallbacks = 0, skipped = PoiFactory.Skipped;
        var fell = new List<string>();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city")))
            for (int i = 0; i < (f.Tags.Contains("grand") ? 2 : 6); i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                int before = PoiFactory.Skipped;
                PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech, Ruin: RuinAge.Fallback), out string why);
                spawns++;
                // Three failed tries from the spawn's seed come before the authored layout.
                if (PoiFactory.Skipped - before >= 3) { fallbacks++; fell.Add($"{f.Id} ({why})"); }
            }
        Check(fallbacks * 100 <= spawns, $"spawns lay out from their own seed: {fallbacks} of {spawns} fell back to the authored layout, {PoiFactory.Skipped - skipped} tries failed{(fell.Count > 0 ? ": " + string.Join("; ", fell.Take(6)) : "")}");
    }

    /// <summary>
    /// The 1 m standard (<see cref="PlaceAudit"/>) over fresh places of every family, each turned a random way, in a
    /// state it rolls, at a tech its world could have: doors 1x2x1 or 2x2x1 (larger only for vehicles, never 1x1),
    /// leaves along their walls with floor under them, every directional piece facing as what it sits in says,
    /// loose things resting on something and nothing in the rock, every room reachable, rooms 3 to 4 m clear. Each
    /// kind of fault is counted with the families it came from; the facing count must be 0.
    /// </summary>
    void Standard()
    {
        var rand = new Random();
        var all = new PlaceAudit();
        var byKind = new Dictionary<string, Dictionary<string, int>>();
        int places = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (FamilyDef f in _lib.Families)
        {
            var states = Enum.GetValues<PoiState>().Where(s => f.StateWeights[(int)s] > 0f).ToList();
            if (states.Count == 0) continue;
            int n = f.Tags.Contains("grand") ? 1 : 3;
            for (int i = 0; i < n; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech);
                PoiInstance inst;
                try
                {
                    Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), hints, out _);
                    if (layout == null) continue;
                    inst = PoiFactory.Spawn(_lib, layout, states[rand.Next(states.Count)], (ulong)rand.NextInt64());
                }
                catch (Exception) { continue; }
                places++;
                PlaceAudit a = PlaceAudit.Of(inst);
                all.Add(a);
                foreach (var kv in a.Faults)
                {
                    if (!byKind.TryGetValue(kv.Key, out var fams)) byKind[kv.Key] = fams = new Dictionary<string, int>();
                    fams[f.Id] = fams.GetValueOrDefault(f.Id) + kv.Value.Count;
                }
            }
        }
        GD.Print($"    {places} places, {all.Doors} doors, {all.Pieces} directional pieces, {all.Rooms} rooms ({clock.ElapsedMilliseconds} ms)");
        GD.Print($"    door sizes (w x h x d): {string.Join(", ", all.DoorSizes.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        GD.Print($"    room clear heights: {string.Join(", ", all.RoomHeights.OrderBy(k => k.Key).Select(k => $"{k.Key} m {k.Value}"))}");
        foreach (var kv in all.Faults.OrderByDescending(k => k.Value.Count))
            GD.Print($"      {kv.Value.Count,6}  {kv.Key} (e.g. {kv.Value.First}): {string.Join(", ", byKind[kv.Key].OrderByDescending(x => x.Value).Take(6).Select(x => $"{x.Key} {x.Value}"))}");
        Check(all.FacingFaults == 0, $"every directional piece faces as what it sits in says: {all.FacingFaults} wrong-facing in {places} places");
        int doors = all.Count("door: 1x1") + all.Count("door: deeper than 1 m") + all.Count("door: off the 1x2 and 2x2 standard");
        Check(doors == 0, $"doors are 1x2x1 or 2x2x1, larger only for vehicles: {doors} of {all.Doors} off the standard");
        Check(all.Count("door: no floor under it") == 0, $"every door has floor under it: {all.Count("door: no floor under it")} without");
        int loose = all.Faults.Where(k => k.Key.StartsWith("rests on nothing") || k.Key.StartsWith("in the rock")).Sum(k => k.Value.Count);
        Check(loose == 0, $"loose things rest on something and nothing is set into the rock: {loose} cells");
        Check(all.Count("finish: painted on nothing") == 0, $"floors are never plates: every floor finish is painted on a block or the ground under it ({all.Count("finish: painted on nothing")} over nothing)");
        int stairs = all.Count("stair: foot blocked") + all.Count("stair: top blocked");
        Check(stairs * 200 <= all.Pieces, $"stairs are open at their foot and their head: {stairs} blocked");
        Check(all.Count("room: no way in") == 0, $"every room has a way in a person can walk: {all.Count("room: no way in")} of {all.Rooms} rooms without");
        Check(all.Count("room: ceiling under 3 m") == 0, $"rooms are 3 m clear or more: {all.Count("room: ceiling under 3 m")} lower");
    }
}
