using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=husk-grab: a Husk seizes the castaway by the head, seen through their own eyes (the
    /// game's camera, its close-up and the struggle as they are in play); then one shot from the
    /// survival tool, which goes into it and breaks the hold. Frames: grab.png, then shot.png.
    /// </summary>
    IEnumerable<int> HuskGrab()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("husk");
        foreach (int x in Play.Tool(Slice.Player.ToolMode.Shoot)) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        patch.Frozen = false;
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Creature other in patch.Sim.Creatures) if (patch.WorldOf(other).DistanceTo(feet) < 120f) other.Removed = true;
        foreach (DeepSite s in patch.Sites) foreach (Creature other in s.Sim.Creatures) other.Removed = true;
        _game.Suit.Sim.Player.Gear.HelmetOn = _args.GetValueOrDefault("helmet", "sealed") != "open";
        WorldLife life = patch.Life;
        // One of this world's Husks if it has them; else one of its people, taken.
        List<Species> hosts = life.Moving.FirstOrDefault(s => s.Husk)?.Hosts
            ?? Enumerable.Range(0, 6).Select(i => life.People.Person((ulong)Random.Shared.NextInt64(), (PersonRole)(i % 7), Occupancy.Friendly, 0.2f, husk: true)).ToList();
        // One tall enough to seize a head, whose face shows.
        Species husk = hosts.OrderBy(h => h.Figure.Height >= life.Data.Husks.SeizeHeight ? 0 : 1).ThenBy(h => h.Figure.Wear.Any(w => HumanBody.Slot(w) == "head") ? 1 : 0).First();
        Vector3 look = _game.Survivor.WorldLook();
        look = new Vector3(look.X, 0f, look.Z).Normalized();
        Creature c = patch.Sim.Spawn(husk, patch.PatchLocal(Ground(feet + look * 1.4f)));
        c.Yaw = MathF.Atan2(look.X, look.Z);
        c.Rig.Reset(patch.Sim, c);
        _game.Visor.Visible = false;
        foreach (int x in _drive.Wait(10)) yield return x;
        Seizing.Begin(patch.Sim, c, patch.Player);
        foreach (int x in _drive.Wait(55)) yield return x;
        GD.Print($"(held by a husk: {_game.HeldBy != null}, close-up {_game.CloseUp:F2}, struggle {_game.Struggle:F2}, health {_game.Suit.HealthShare * 100:F0}%)");
        foreach (int x in Shot(Path.Combine(dir, "grab.png"))) yield return x;
        // One shot: it can't miss, and it breaks the hold.
        _game.Controls.Primary = true;
        _game.Controls.PrimaryPressed = true;
        yield return 0;
        _game.Controls.Primary = false;
        foreach (int x in Shot(Path.Combine(dir, "shot.png"))) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        GD.Print($"(after the shot: held {_game.HeldBy != null}, holds shot off {_game.ShotsFree}, the husk {WildSim.Flat(patch.WorldOf(c) - _game.Survivor.WorldFeet).Length():F1} m off, its health {c.Health:F0})");
        foreach (int x in Shot(Path.Combine(dir, "after.png"))) yield return x;
    }
}
