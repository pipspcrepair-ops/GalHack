using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Vessels;

namespace Slice.Tests;

/// <summary>
/// The far land from high up: the Skiff climbs through the game's own controls, stops at each
/// height and looks out at the horizon every way, with what the streamer holds printed beside each
/// frame. --heights=a,b,c, --yaws=0,90,180,270 (degrees), --pitch=2 (degrees down), --time=0.42;
/// --fly=SECONDS flies on ahead at the last height first. Frames go beside --out, as the seam's do.
/// </summary>
public sealed partial class Staging
{
    IEnumerable<int> FarLandView()
    {
        foreach (int x in Seated()) yield return x;
        Vessel v = Skiff;
        _game.Visor.Visible = false;
        _game.Zone.Sky.TimeOfDay = Num("time", 0.42);
        float pitch = (float)Num("pitch", 2) * Mathf.Pi / 180f;
        float[] yaws = Floats("yaws", "0,90,180,270");
        float[] heights = Floats("heights", "320,700,1000");
        void Look(float yaw)
        {
            Vector3 eye = v.Body.Position.ToGodot() + Vector3.Up * 5f;
            Vector3 ahead = new Vector3(MathF.Sin(yaw), 0f, -MathF.Cos(yaw)) * MathF.Cos(pitch) + Vector3.Down * MathF.Sin(pitch);
            _game.FixedCamera = new Transform3D(Basis.LookingAt(ahead, Vector3.Up), eye);
        }
        IEnumerable<int> Stop()
        {
            _game.Controls.ReleaseAll();
            Hurry(1);
            for (int i = 0; i < 240 && v.Body.Velocity.Length() > 2f; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
        }
        IEnumerable<int> Frames(string tag)
        {
            foreach (float yaw in yaws)
            {
                for (int i = 0; i < 7; i++)
                {
                    Look(yaw * Mathf.Pi / 180f);
                    yield return 0;
                }
                Snap($"{tag}-y{yaw:0}");
            }
            var s = _game.Zone.Streamer;
            GD.Print($"(far {tag}: at {v.Body.Position.ToGodot()}, far complete {s.FarComplete}, voxels complete to {s.CompleteRadius}, pending {s.Pending}, settled {s.Settled})");
        }
        foreach (float height in heights)
        {
            Hurry(3);
            _game.Controls.Hold(Key.Space);
            for (int i = 0; i < 60 * 90 && v.Body.Position.Y < height; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            foreach (int x in Stop()) yield return x;
            foreach (int x in Frames($"up{height:0}")) yield return x;
        }
        if (_args.TryGetValue("fly", out string fly))
        {
            Hurry(3);
            _game.Controls.Hold(Key.W);
            int ticks = (int)(double.Parse(fly, System.Globalization.CultureInfo.InvariantCulture) * 60);
            for (int i = 0; i < ticks; i++)
            {
                Look(yaws[0] * Mathf.Pi / 180f);
                yield return 0;
            }
            foreach (int x in Stop()) yield return x;
            foreach (int x in Frames("flown")) yield return x;
        }
        _game.FixedCamera = null;
    }

    float[] Floats(string key, string fallback) =>
        _args.GetValueOrDefault(key, fallback).Split(',').Select(h => float.Parse(h, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
}
