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
    /// --view=stomp: a pillar-legged giant (this world's biggest walker, grown to --size, 7 m) stomps on the
    /// sleeper, seen through their own eyes by day: tell.png (the foot up), contact.png (the moment it lands)
    /// and after.png (thrown back, the view shaking, dust round the foot). Prints what the game did with it.
    /// </summary>
    IEnumerable<int> StompView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.5);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        string dir = Folder("stomp");
        WildlifePatch patch = _game.Zone.Wildlife;
        patch.Frozen = false;
        Vector3 feet = _game.Survivor.WorldFeet;
        foreach (Creature other in patch.Sim.Creatures) if (patch.WorldOf(other).DistanceTo(feet) < 150f) other.Removed = true;
        Species model = patch.Life.Moving.Where(s => s.Rig == RigKind.Legged && !s.Mechanical && !s.Flies && s.Body.Legs >= 4).OrderByDescending(s => s.Body.Size).First();
        Species giant = model.Resized((float)Num("size", 7));
        Proportions b = giant.Body;
        b.LegStyle = LegStyle.Upright;
        b.LegThickness = MathF.Max(b.Width * 0.25f, b.LegLength * 0.15f); // pillars
        giant.Stats.Damage = 1f; // felt, not fatal: the frames are of the blow
        Vector3 look = _game.Survivor.WorldLook();
        look = new Vector3(look.X, 0f, look.Z).Normalized();
        Creature c = patch.Sim.Spawn(giant, patch.PatchLocal(Ground(feet + look * (b.Size * 0.5f + b.LegLength * 0.6f))));
        c.Yaw = MathF.Atan2(look.X, look.Z); // facing the sleeper
        c.Rig.Reset(patch.Sim, c);
        _game.Visor.Visible = false;
        _game.Hands.ResetPeaks();
        int stompsBefore = patch.Audio.Stomps;
        bool landed = false;
        Vector3 foot = default;
        patch.Sim.Attacked += e => { if (e.Creature == c && !landed) { landed = true; GD.Print($"(stomp: {(e.Hit ? "hit" : "missed")}, {e.Damage:F1} damage, push {e.Push.Length():F1} m/s, gap {e.Gap:F2} m)"); } };
        patch.Sim.Quaked += e => { if (e.Creature == c) foot = patch.FromPatch(e.Position); };
        foreach (int x in _drive.Wait(10)) yield return x;
        // Look up at it, a little above the foot that will come down.
        _game.Survivor.Pitch = 0.35f;
        bool started = Attacks.Start(patch.Sim, c, patch.Player);
        GD.Print($"(stomp: started {started}, move {c.Move}, wind-up {c.WindupLength:F2} s, a {b.Size:F1} m {giant.CommonName})");
        for (int i = 0; i < 120 && c.Phase == AttackPhase.Windup && c.PhaseTime < c.PhaseLength * 0.8f; i++) yield return 0;
        foreach (int x in Shot(Path.Combine(dir, "tell.png"))) yield return x;
        _game.Survivor.Pitch = -0.1f; // down to where the foot lands
        for (int i = 0; i < 120 && !landed; i++) yield return 0;
        Vector3 at = _game.Survivor.WorldFeet;
        foreach (int x in Shot(Path.Combine(dir, "contact.png"))) yield return x;
        float speed = 0f;
        for (int i = 0; i < 12; i++)
        {
            speed = MathF.Max(speed, _game.Survivor.Body.Velocity.Length());
            yield return 0;
        }
        foreach (int x in Shot(Path.Combine(dir, "after.png"))) yield return x;
        GD.Print($"(stomp: thrown {WildSim.Flat(_game.Survivor.WorldFeet - at).Length():F1} m)");
        GD.Print($"(stomp: sleeper thrown at {speed:F1} m/s, view shake {_game.Hands.PeakShake:F2}, jolt {_game.Hands.PeakJolt:F2}, thuds {patch.Audio.Stomps - stompsBefore}, foot came down {foot.DistanceTo(_game.Survivor.WorldFeet):F1} m from the sleeper)");
    }
}
