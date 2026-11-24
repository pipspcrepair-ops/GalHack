using System;
using System.Collections.Generic;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// A hard landing and away (the vehicle-ground fix): in Creative, the Skiff slammed into the ground nose first
    /// at speed with its pilot in the seat, then flown straight off it under its own power, seen from the ground.
    /// </summary>
    IEnumerable<int> VehGroundView()
    {
        foreach (int x in DamageSetting()) yield return x;
        _game.Mode = GameMode.Creative;
        GalHack.Structures.Structure s = _game.Zone.Data.Blueprint("starter.gbp");
        N.Vector3 site = DamageSite();
        Vessel v = Vessel.Spawn(_game.Zone, s, site + new N.Vector3(0f, 3f, 0f), 0.5f, landed: true);
        foreach (int x in _drive.Wait(10)) yield return x;
        _game.Survivor.Sit(v);
        foreach (int x in _drive.Wait(10)) yield return x;
        N.Quaternion nose = N.Quaternion.Normalize(MathX.Yaw(0.5f) * MathX.AxisAngle(N.Vector3.UnitX, -0.6f));
        N.Vector3 fwd = MathX.Rotate(MathX.Yaw(0.5f), -N.Vector3.UnitZ);
        v.Body.Position = site + new N.Vector3(0f, 14f, 0f) - fwd * 8f;
        v.Body.Rotation = nose;
        v.Body.Velocity = fwd * 12f - N.Vector3.UnitY * (float)Num("speed", 24);
        v.Controller.Wake(v.Body);
        v.Binding.Teleport();
        for (int t = 0; t < 600 && !(v.Controller.Frozen || v.Body.Velocity.Length() < 0.3f); t++) yield return 0;
        foreach (int x in _drive.Wait(20)) yield return x;
        Vector3 ground = v.Body.Position.ToGodot();
        _game.Visor.Visible = false;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait((int)Num("ticks", 40))) yield return x;
        Vector3 at = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 200) * Mathf.Pi / 180f;
        Vector3 eye = ground + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 16f;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 1.7f;
        _game.FixedCamera = new Transform3D(Basis.LookingAt((at + ground) * 0.5f - eye, Vector3.Up), eye);
        foreach (int x in _drive.Wait(4)) yield return x;
        GD.Print($"vehground: landed at {ground}, now {at} ({at.Y - ground.Y:F1} m up), frozen {v.Controller.Frozen}");
    }
}
