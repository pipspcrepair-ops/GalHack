using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;

namespace Slice.Tests;

/// <summary>
/// Values not built yet (<c>off</c> in data/world-layers.txt): a fresh roll never picks one and a new game's
/// pickers never offer one, a forced value still works, and everything that is on names only what the game has.
/// </summary>
public partial class LayersTest
{
    static readonly LayerKind[] Skipped = { LayerKind.Dress, LayerKind.Old, LayerKind.Rule, LayerKind.Clash, LayerKind.Fam, LayerKind.Occ };

    void Off()
    {
        GD.Print("-- values not built yet --");
        var off = _lib.Layers.Where(l => l.Off).ToList();
        var offIds = new HashSet<string>(off.Select(l => $"{l.Kind.ToString().ToLowerInvariant()}.{l.Id}"));
        Check(off.Count > 0 && off.All(l => !string.IsNullOrWhiteSpace(l.OffWhy)), $"{off.Count} values are marked off, each with its reason");
        Check(off.Any(l => l.Kind == LayerKind.Theme) && off.Any(l => l.Kind == LayerKind.Fall), "a theme and a fall are among them");
        Check(off.All(l => !Skipped.Contains(l.Kind)), "no rule, occupant, note or sign record is ever off");
        foreach (LayerKind k in Enum.GetValues<LayerKind>())
            if (WorldForce.Values(_lib, k).Any(id => offIds.Contains($"{k.ToString().ToLowerInvariant()}.{id}")))
                Check(false, $"the new game's {k} picker offers a value that is off");
        Check(Enum.GetValues<LayerKind>().All(k => WorldForce.Values(_lib, k).Count == _lib.Layers.Count(l => l.Kind == k && !l.Off)), "the new game's pickers offer exactly the values that are on");

        // Fresh rolls: not one off value, in any layer.
        int rolls = 0, hits = 0;
        string seen = "";
        for (int i = 0; i < 600; i++)
        {
            ulong seed = Seed();
            string arch = Archetypes[_rng.Next(Archetypes.Length)];
            WorldProfile profile = WorldProfile.Roll(seed, arch, false);
            if (WorldRoll.Roll(_lib, seed, arch, profile, i % 4 == 0, null, (float)_rng.NextDouble()) is not { } r) continue;
            rolls++;
            foreach (WorldLayer l in r.Rolled.Where(l => l.Off)) { hits++; seen = $"{l.Kind}.{l.Id}"; }
        }
        Check(rolls > 400 && hits == 0, $"{rolls} fresh rolls, {hits} off values among them{(hits > 0 ? " (" + seen + ")" : "")}");

        // A forced value still builds, off or not.
        int forced = 0, tried = 0;
        for (int i = 0; i < 200 && forced < 3; i++)
        {
            ulong seed = Seed();
            WorldProfile profile = WorldProfile.Roll(seed, "Temperate", false);
            if (!profile.Inhabited) continue;
            tried++;
            var given = new WorldRoll.Given().Set(LayerKind.Theme, "wall_cities").Set(LayerKind.Fall, "dimensional_tear").Set(LayerKind.Sky, "seam");
            if (WorldRoll.Roll(_lib, seed, "Temperate", profile, false, given, 0.5f) is { } r && r.Theme.Id == "wall_cities" && r.All(LayerKind.Fall).Any(f => f.Id == "dimensional_tear") && r.All(LayerKind.Sky).Any(s => s.Id == "seam")) forced++;
        }
        Check(forced == 3, $"forced off values (a theme, a fall and a sky) still roll ({forced} of {tried} inhabited worlds)");
        var force = new WorldForce { Theme = "wall_cities", Fall = "dimensional_tear" };
        var into = new WorldRoll.Given();
        force.Into(into);
        Check(into.First(LayerKind.Theme) == "wall_cities" && into.First(LayerKind.Fall) == "dimensional_tear", "a test's or --layer's forced theme and fall reach the roll");

        // Everything that is on names only what the game has.
        var families = new HashSet<string>(_lib.Families.Select(f => f.Id));
        var values = new HashSet<string>(_lib.Layers.Where(l => !l.Off).Select(l => $"{l.Kind.ToString().ToLowerInvariant()}.{l.Id}"));
        var occupants = new HashSet<string>(_lib.Layers.Where(l => l.Kind == LayerKind.Occ).Select(l => l.Id));
        var dead = new List<string>();
        foreach (WorldLayer l in _lib.Layers.Where(l => !l.Off && !Skipped.Contains(l.Kind)))
        {
            string me = $"{l.Kind}.{l.Id}";
            foreach (var t in new[] { l.Hub, l.Industry, l.Plant, l.Farm, l.Poi })
                dead.AddRange(t.Keys.Where(k => !families.Contains(k)).Select(k => $"{me} family {k}"));
            dead.AddRange(l.Needs.Where(n => !families.Contains(n)).Select(n => $"{me} needs {n}"));
            if (l.Depot != null && !families.Contains(l.Depot)) dead.Add($"{me} depot {l.Depot}");
            dead.AddRange(l.Like.Where(k => !values.Contains($"{k.Layer.ToString().ToLowerInvariant()}.{k.Id}")).Select(k => $"{me} like {k.Layer}.{k.Id}"));
            dead.AddRange(l.Occupancy.Keys.Where(k => !occupants.Contains(k)).Select(k => $"{me} occ {k}"));
            dead.AddRange(l.Bias.Keys.Where(k => !WorldReading.Keys.Contains(k)).Select(k => $"{me} bias {k}"));
        }
        Check(dead.Count == 0, $"every value that is on names only built families, rolled values, occupants and read biases ({dead.Count} dead: {string.Join("; ", dead.Take(6))})");
    }
}
