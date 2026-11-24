using System.Collections.Generic;
using System.IO;
using GalHack.Physics.Engine;
using Godot;

namespace Slice.Tests;

/// <summary>
/// --view=sleeperface: the sleeper's head from the front, helmet shut, half way up, and open, at 0.45 m and 3 m.
/// --out=DIR is the folder.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> SleeperFaceView()
    {
        string dirOut = _args.GetValueOrDefault("out", "sleeperface");
        Directory.CreateDirectory(dirOut);
        _game.Zone.Sky.TimeOfDay = Num("time", 0.4);
        Slice.Player.Survivor s = _game.Survivor;
        s.PlaceOutside((Play.FlatSpot(s.WorldFeet, 25f, span: 12f) + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        var gear = _game.Suit.Sim.Player.Gear;
        foreach ((string state, int wait) in new[] { ("closed", 20), ("lifting", 6), ("open", 30) })
        {
            gear.HelmetOn = state == "closed";
            foreach (int x in _drive.Wait(wait)) yield return x;
            // Straight ahead at 0.45 m and 3 m; at the open face also 30 degrees to each side and from above.
            var views = new List<(float dist, float yaw, float pitch, string tag)> { (0.45f, 0f, 0f, ""), (3f, 0f, 0f, "") };
            if (state == "open") { views.Add((0.45f, 30f, 0f, "-left30")); views.Add((0.45f, -30f, 0f, "-right30")); views.Add((0.45f, 0f, 30f, "-above30")); }
            if (state != "closed") { views.Add((0.6f, 90f, 0f, "-side")); views.Add((0.6f, 180f, 60f, "-above-back")); }
            foreach ((float dist, float yaw, float pitch, string tag) in views)
            {
                Node3D fig = _game.Figure;
                Vector3 head = fig.GlobalPosition + Vector3.Up * 1.6f * fig.Scale.Y, ahead = -fig.GlobalBasis.Z;
                Vector3 dir = ahead.Rotated(Vector3.Up, Mathf.DegToRad(yaw));
                dir = dir.Rotated(dir.Cross(Vector3.Up).Normalized(), Mathf.DegToRad(pitch));
                _game.FixedCamera = new Transform3D(Basis.Identity, head + dir * dist + Vector3.Up * (pitch == 0f ? 0.04f : 0f)).LookingAt(head, Vector3.Up);
                foreach (int x in _drive.Wait(3)) yield return x;
                foreach (int x in Shot(Path.Combine(dirOut, $"{state}-{dist:0.00}m{tag}.png"))) yield return x;
            }
        }
        _game.FixedCamera = null;
    }
}
