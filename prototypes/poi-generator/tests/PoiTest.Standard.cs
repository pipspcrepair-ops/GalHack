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
        int spawns = 0, fallbacks = 0, tries = 0, failedTries = 0;
        var byFamily = new Dictionary<string, (int Fell, int Of, string Why)>();
        var reasons = new Dictionary<string, int>();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (FamilyDef f in _lib.Families.Where(f => !f.Tags.Contains("city")))
        {
            int n = f.Tags.Contains("grand") ? 2 : 6;
            for (int i = 0; i < n; i++)
            {
                TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
                var hints = new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech, Ruin: RuinAge.Fallback);
                int v = rand.Next(f.Variants.Count);
                ulong spawn = (ulong)rand.NextInt64();
                bool made = false;
                string why = null;
                // As PoiFactory.SpawnLayout tries them: three seeds from the spawn's, then the authored layout.
                for (ulong k = 0; k < 3 && !made; k++)
                {
                    tries++;
                    try { PoiFactory.BuildLayout(_lib, f, v, f.Rarity, Hash.Combine(spawn, 0x6A11UL, k), hints); made = true; }
                    catch (Exception e)
                    {
                        failedTries++;
                        why ??= e.Message;
                        string key = Reason(e.Message);
                        reasons[key] = reasons.GetValueOrDefault(key) + 1;
                    }
                }
                spawns++;
                var was = byFamily.GetValueOrDefault(f.Id);
                byFamily[f.Id] = (was.Fell + (made ? 0 : 1), was.Of + 1, was.Why ?? (made ? null : why));
                if (!made) fallbacks++;
            }
        }
        GD.Print($"    {spawns} spawns of {byFamily.Count} families, {tries} layouts tried, {failedTries} failed, {fallbacks} fell back to the authored layout ({clock.ElapsedMilliseconds} ms)");
        foreach (var kv in reasons.OrderByDescending(r => r.Value).Take(12)) GD.Print($"      {kv.Value,5}  {kv.Key}");
        foreach (var kv in byFamily.Where(b => b.Value.Fell > 0).OrderByDescending(b => b.Value.Fell))
            GD.Print($"      {kv.Key}: {kv.Value.Fell} of {kv.Value.Of} fell back ({kv.Value.Why})");
        // A failed try costs a layout's time; a fallback costs the place its variety.
        Check(fallbacks * 100 <= spawns, $"spawns lay out from their own seed: {fallbacks} of {spawns} fell back to the authored layout");
        Check(failedTries * 20 <= tries, $"layouts rarely fail: {failedTries} of {tries} tries failed");
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
        GD.Print($"    doors taken out at spawn: {Settle.Broken} broken, {Settle.Unwalled} with no wall at an end ({string.Join(", ", Settle.Ends.OrderByDescending(k => k.Value).Take(8).Select(k => $"{k.Key} {k.Value}"))})");
        GD.Print($"    door sizes (w x h x d): {string.Join(", ", all.DoorSizes.OrderByDescending(k => k.Value).Select(k => $"{k.Key} {k.Value}"))}");
        GD.Print($"    rooms with no way in, by purpose: {string.Join(", ", all.Shut.OrderByDescending(k => k.Value).Take(14).Select(k => $"{k.Key} {k.Value}"))}");
        GD.Print($"    rooms under 3 m, by purpose: {string.Join(", ", all.Low.OrderByDescending(k => k.Value).Take(20).Select(k => $"{k.Key} {k.Value}"))}");
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

    /// <summary>
    /// A look at one kind of fault (a tool, not a check: --only=Probe --family=id --kind=text): places of the family are
    /// rolled until the audit finds that kind, and the cells round the first one are printed level by level.
    /// </summary>
    void Probe()
    {
        var args = OS.GetCmdlineUserArgs();
        string fam = args.FirstOrDefault(a => a.StartsWith("--family="))?["--family=".Length..];
        string kind = args.FirstOrDefault(a => a.StartsWith("--kind="))?["--kind=".Length..] ?? "";
        if (fam == null) return; // a tool: only when asked for
        FamilyDef f = _lib.Families.First(x => x.Id == fam);
        var rand = new Random();
        for (int tries = 0; tries < 40; tries++)
        {
            var states = Enum.GetValues<PoiState>().Where(s => f.StateWeights[(int)s] > 0f).ToList();
            TechLevel tech = f.Techs.Count > 0 ? WorldProfile.Parse(f.Techs[rand.Next(f.Techs.Count)]) : (TechLevel)rand.Next(3);
            Layout layout = PoiFactory.SpawnLayout(_lib, f, rand.Next(f.Variants.Count), f.Rarity, (ulong)rand.NextInt64(), new LayoutHints(Dirs.Horizontal[rand.Next(4)], tech), out _);
            if (layout == null) continue;
            PoiInstance inst = PoiFactory.Spawn(_lib, layout, states[rand.Next(states.Count)], (ulong)rand.NextInt64());
            PlaceAudit a = PlaceAudit.Of(inst);
            var hit = a.Faults.FirstOrDefault(k => k.Key.Contains(kind));
            if (hit.Key == null) continue;
            Int3 at = hit.Value.First;
            GD.Print($"PROBE {f.Id} {inst.State} {hit.Key} x{hit.Value.Count} at {at}; grid {inst.Blocks.Min} size {inst.Blocks.Size}");
            foreach (PlacedModule m in layout.Modules.Where(m => at.X >= m.Min.X - 1 && at.X <= m.Max.X + 1 && at.Y >= m.Min.Y - 1 && at.Y <= m.Max.Y + 1 && at.Z >= m.Min.Z - 1 && at.Z <= m.Max.Z + 1))
                GD.Print($"PROBE   module {m.Index} {m.Kind} {m.Purpose} storey {m.Storey} {m.Min}..{m.Max}");
            var nav = inst.Nav();
            for (int y = at.Y + 3; y >= at.Y - 2; y--)
            {
                GD.Print($"PROBE y={y}");
                for (int z = at.Z - 6; z <= at.Z + 6; z++)
                {
                    var row = new System.Text.StringBuilder("PROBE   ");
                    for (int x = at.X - 10; x <= at.X + 10; x++)
                    {
                        var p = new Int3(x, y, z);
                        Block b = inst.Blocks.Get(p);
                        char c = b.IsAir ? (nav.IsRock(p) ? '#' : layout.Occupied[p] ? '.' : ' ') : b.Type switch
                        {
                            BlockType.Cube => 'X', BlockType.Door => 'D', BlockType.Pane => 'P', BlockType.Stair => 'S', BlockType.Ramp => 'R',
                            BlockType.Terminal => 'T', BlockType.Locker => 'L', BlockType.Rubble => 'r', BlockType.Plate => '_', BlockType.Bed => 'B',
                            BlockType.Ladder => 'H', BlockType.Crate => 'c', BlockType.Pod => 'o', BlockType.Table => 't', BlockType.Rail => '=',
                            _ => char.ToLowerInvariant(b.Type.ToString()[0]) is var ch && ch != 'x' ? ch : '?',
                        };
                        if (p == at) row.Append('*');
                        row.Append(c);
                        if (p == at && (b.Facing.IsHorizontal())) row.Append(b.Facing.ToString()[0]);
                    }
                    GD.Print(row.ToString());
                }
            }
            return;
        }
        GD.Print($"PROBE {fam}: no '{kind}' in 40 places");
    }

    /// <summary>A failure's message with its numbers and cells taken out, so alike failures count together.</summary>
    static string Reason(string message)
    {
        var s = System.Text.RegularExpressions.Regex.Replace(message, @"\(-?\d+,-?\d+,-?\d+\)", "(cell)");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"-?\d+(\.\d+)?", "#");
        return s.Length > 160 ? s[..160] : s;
    }
}
