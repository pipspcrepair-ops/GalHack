using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Player;

namespace Slice.Tests;

public partial class SleeperToolTest
{
    /// <summary>
    /// A tree has hit points the gather blast wears as it wears a block (owner: "Shouldn't our sleeper tool work on trees the same
    /// way it works on blocks"): they show over the reticle, fall at the tool's block rate, the tree is felled at none, and its
    /// biomass comes in as they go.
    /// </summary>
    IEnumerable<int> Tree()
    {
        GD.Print("-- a tree's hit points --");
        foreach (int x in _play.Tool(ToolMode.Gather)) yield return x;
        Vector3 feet = _game.Survivor.WorldFeet;
        var tree = _game.Zone.Streamer.FloraNear(feet, 150f)
            .Where(s => TreeFx.IsTree(s.Kind) && s.Variant >= 0 && !_game.Zone.Planet.Felled.Contains(s.Id) && MathF.Abs(s.Root.Y - feet.Y) < 30f)
            .OrderBy(s => s.Root.DistanceTo(feet)).FirstOrDefault();
        Check(tree.Id != 0, "a tree stands within 150 m");
        if (tree.Id == 0) yield break;
        Vector3 side = (feet - tree.Root) with { Y = 0f };
        side = side.LengthSquared() > 0.01f ? side.Normalized() : Vector3.Right;
        _game.Survivor.PlaceOutside((tree.Root + side * 2.2f + Vector3.Up * 0.5f).ToNumerics());
        foreach (int x in _drive.Wait(60)) yield return x;
        Vector3 trunk = tree.Root + Vector3.Up * 1.1f;
        foreach (int x in _drive.LookAt(trunk, 8)) yield return x;
        float max = _game.Zone.PlantMax(tree);
        string whole = _game.Visor.BlockHealth();
        Check(whole == $"{MathF.Ceiling(max):F0} / {max:F0}", $"its hit points show over the reticle: {whole ?? "nothing"} (a tree of scale {tree.Scale:F2}: {max:F1})");
        var rules = _game.Zone.Data.Rules;
        float rate = rules.Tool.Lightning.BlockPerSecond * rules.Tool.TierOf(Tool.ItemId).Shot, start = Time.GetTicksMsec() / 1000f;
        int biomass = _game.Pack["biomass"], frames = 0;
        string partway = null;
        float half = 0f;
        int halfBiomass = 0;
        while (!_game.Zone.Planet.Felled.Contains(tree.Id) && frames < 60 * 60)
        {
            _drive.Face(trunk);
            _game.Controls.Primary = true;
            yield return 0;
            frames++;
            if (partway == null && _game.Zone.PlantHealth(tree).Hp <= max * 0.5f)
            {
                partway = _game.Visor.BlockHealth();
                half = frames / 60f;
                halfBiomass = _game.Pack["biomass"] - biomass;
            }
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(10)) yield return x;
        float took = frames / 60f, expected = max / rate;
        int got = _game.Pack["biomass"] - biomass;
        double yield = Math.Max(1.0, Math.Round(rules.Materials.BiomassPerPlant * Math.Clamp(tree.Scale, 0.5f, 3f))) * rules.Plants.TreeYield;
        Check(_game.Zone.Planet.Felled.Contains(tree.Id) && took > expected * 0.6f && took < expected * 1.6f,
            $"held, the blast wears it at the tool's block rate ({rate} a second): felled at none after {took:F1} s ({expected:F1} at that rate), readout halfway {partway}");
        Check(got >= Math.Floor(yield) - 1 && got <= Math.Ceiling(yield) + 1 && halfBiomass <= Math.Ceiling(yield * 0.5) + 1,
            $"its biomass comes as its hit points go: {halfBiomass} by halfway ({half:F1} s), {got} in all ({yield:F2} for the whole tree)");
    }
}
