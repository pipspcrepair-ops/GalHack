using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using PlanetSurface.Fx;
using Slice.Game;
using Slice.Surface;
using Slice.Ui;
using Wildlife;

namespace Slice.Tests;

/// <summary>
/// The spawn distance: its slider runs from the default to the most and the setting keeps; it applies
/// live (the patch, how far bodies are drawn, people's reach); at the default, life is there and
/// drawn past the old lively ring; and on every route nothing living comes into view near you
/// (<see cref="AppearFloor"/>).
/// </summary>
public partial class PopTest
{
    /// <summary>Nothing living comes into view nearer than this, m: the bestiary's least spawn distance.</summary>
    const float AppearFloor = 90f;

    /// <summary>The nearest a creature or person came into view on a route, and what.</summary>
    void JudgeArrivals(string name, List<PopLog.Pop> pops)
    {
        var arrivals = pops.Where(p => p.Kind is "creature" or "person" && p.What == "comes into view").ToList();
        var near = arrivals.Where(p => p.Distance < AppearFloor).ToList();
        string nearest = arrivals.Count > 0 ? $"{arrivals.Min(p => p.Distance):F0} m" : "none";
        Check(near.Count == 0, $"{name}: nothing living comes into view within {AppearFloor:F0} m: {arrivals.Count} came into view, the nearest at {nearest}, {near.Count} too near ({string.Join(", ", near.Take(4).Select(p => $"{p.Kind} at {p.Distance:F0} m"))})");
    }

    IEnumerable<int> SpawnChecks()
    {
        GD.Print("-- spawn distance --");
        float top = OptionsSection.SpawnAt(1f), bottom = OptionsSection.SpawnAt(0f);
        Check(bottom == SpawnReach.Default && top == SpawnReach.Most && SpawnReach.Default >= SpawnReach.Old * 1.5f,
            $"the spawn slider runs from {bottom:F0} m (the default; the old ring was {SpawnReach.Old:F0} m) to {top:F0} m");
        const string path = "user://spawn-test-settings.cfg";
        new GameSettings { SpawnDistance = top }.Save(path);
        float back = GameSettings.Load(path).SpawnDistance;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
        Check(back == top, $"the spawn distance keeps in the settings file ({back:F0} m read back)");

        // At the default: life about and drawn past the old ring.
        var patch = _game.Zone.Wildlife;
        _game.FixedCamera = null;
        var clock = Stopwatch.StartNew();
        int Past()
        {
            Vector3 you = patch.Player.Position;
            return patch.Sim.Creatures.Count(c => !c.Removed && !c.Dead && patch.View.Drawn(c) && WildSim.Flat(c.Position - you).Length() > SpawnReach.Old + 10f);
        }
        while (clock.Elapsed.TotalSeconds < 15 && Past() == 0) yield return 0;
        int past = Past(), all = patch.Sim.Creatures.Count(c => !c.Removed && !c.Dead);
        Check(past > 0, $"at the default ({SpawnReach.Distance:F0} m) {past} creatures are drawn past the old {SpawnReach.Old:F0} m ring, of {all} about; the patch reaches {patch.Sim.Terrain.HalfSize:F0} m each way");

        // Live: the most, and back.
        _game.Settings.SpawnDistance = top;
        _game.Settings.Changed();
        clock.Restart();
        while (clock.Elapsed.TotalSeconds < 30 && patch.Sim.Terrain.HalfSize != SpawnReach.PatchHalf(_game.Zone.Data.Rules.Wildlife.PatchHalfSize)) yield return 0;
        for (int i = 0; i < 5; i++) yield return 0;
        Check(SpawnReach.Distance == top && patch.View.FarDistance == top && PersonView.FarRange == top && patch.Sim.Terrain.HalfSize >= top,
            $"at the most, live after {clock.Elapsed.TotalSeconds:F1} s: creatures drawn to {patch.View.FarDistance:F0} m, people to {PersonView.FarRange:F0} m, the patch {patch.Sim.Terrain.HalfSize:F0} m each way, {patch.Sim.Creatures.Count(c => !c.Removed && !c.Dead)} about");
        foreach (int x in Route("walk at the most spawn", null, 7f, 1.6f, 200f, 0f)) yield return x;
        _game.Settings.SpawnDistance = SpawnReach.Default;
        _game.Settings.Changed();
        for (int i = 0; i < 5; i++) yield return 0;
        Check(SpawnReach.Distance == SpawnReach.Default && patch.View.FarDistance == SpawnReach.Default, $"back to the default, live: creatures drawn to {patch.View.FarDistance:F0} m");
    }
}
