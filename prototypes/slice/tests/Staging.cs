using System;
using System.Collections.Generic;
using System.Globalization;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>The screenshot views: each stages one moment of the loop through the game itself.</summary>
public sealed class Staging
{
    readonly Game.Game _game;
    readonly Driver _drive;
    readonly Dictionary<string, string> _args;

    public Staging(Game.Game game, Dictionary<string, string> args)
    {
        _game = game;
        _drive = new Driver(game);
        _args = args;
    }

    public IEnumerable<int> Run(string view)
    {
        foreach (int x in _drive.UntilPlaying()) yield return x;
        switch (view)
        {
            case "pod":
                foreach (int x in Pod()) yield return x;
                break;
            default:
                throw new ArgumentException($"no view '{view}'");
        }
    }

    /// <summary>The escape pod just down at dawn, seen from a little way off.</summary>
    IEnumerable<int> Pod()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.262);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 pod = _game.Pod.Body.Position.ToGodot();
        float heading = (float)Num("heading", 200) * Mathf.Pi / 180f;
        var dir = new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading));
        Vector3 eye = pod + dir * (float)Num("distance", 9) + Vector3.Up * 2.2f;
        eye.Y = MathF.Max(eye.Y, _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(pod + Vector3.Up * 1.2f - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    double Num(string key, double fallback) => _args.TryGetValue(key, out string s) ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;
}
