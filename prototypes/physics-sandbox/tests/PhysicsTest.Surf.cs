using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using N = System.Numerics;

namespace PhysicsSandbox;

/// <summary>
/// Space on the ground and in the air (the owner's two asks together): tapping it while running skims over the ground in
/// a chain of jumps and jetpack bursts (jetpack surfing), every tap answered, a press just before touchdown jumping on
/// landing; and a Space held on through a landing is no second jump: the sleeper stands, and the pack fills again.
/// Along the rover lane, where the hills are eased to a gentle roll. (-- --surf runs only this.)
/// </summary>
public partial class PhysicsTest
{
    IEnumerable<int> Surf()
    {
        Player p = _w.Player;
        float lane = SandboxTerrain.LaneZ;
        p.PlaceOutside(_w.Ground(-45f, lane) + N.Vector3.UnitY * 0.05f);
        p.Yaw = -MathF.PI / 2f; // facing east, along the lane
        p.Pitch = 0f;
        for (int i = 0; i < 30; i++) yield return 0;

        // Ten seconds running east, a 4-tick tap of Space every half second.
        const int Every = 30, Press = 4, Ticks = 600;
        N.Vector3 from = p.Body.Position;
        int taps = 0, answered = 0, jumps = 0, bursts = 0, waiting = -1;
        for (int t = 0; t < Ticks; t++)
        {
            bool down = t % Every < Press;
            if (t % Every == 0)
            {
                taps++;
                waiting = t;
            }
            p.Legs.Move = new N.Vector2(0f, -1f);
            p.Legs.Sprint = true;
            p.Legs.Jump = p.Legs.Jetpack = down;
            yield return 0;
            bool acted = p.Legs.Jumped || p.Legs.JetpackFiring;
            if (p.Legs.Jumped) jumps++;
            if (p.Legs.JetpackFiring && t % Every == 0) bursts++;
            // A tap is answered by a jump or a burst within its press, or a jump on a touchdown just after it.
            if (waiting >= 0 && acted && t - waiting <= Press + 8)
            {
                answered++;
                waiting = -1;
            }
        }
        p.Legs.Move = N.Vector2.Zero;
        p.Legs.Sprint = p.Legs.Jump = p.Legs.Jetpack = false;
        float ran = MathX.Flat(p.Body.Position - from).Length();
        GD.Print($"  (jetpack surfing: {taps} taps, {jumps} jumps, {bursts} bursts on the tap, {ran / (Ticks * Dt):F2} m/s over the ground)");
        Check(answered == taps, $"tapping Space while running skims the lane: every tap answered by a jump or a burst ({answered} of {taps}), {ran / (Ticks * Dt):F1} m/s over the ground");

        // Held on through a landing: one jump, then the pack, then it stands and the pack fills again.
        for (int i = 0; i < 60; i++) yield return 0;
        int jumped = 0, held = 0;
        float fuelLanded = -1f;
        bool airborne = false;
        for (int t = 0; t < 600; t++)
        {
            p.Legs.Jump = p.Legs.Jetpack = true;
            yield return 0;
            if (p.Legs.Jumped) jumped++;
            if (!p.Body.OnFloor) airborne = true;
            else if (airborne)
            {
                held++;
                if (fuelLanded < 0f) fuelLanded = p.Legs.JetpackFuel;
            }
        }
        float fuelAfter = p.Legs.JetpackFuel;
        p.Legs.Jump = p.Legs.Jetpack = false;
        Check(jumped == 1 && held > 60 && fuelAfter > fuelLanded + 0.5f,
            $"a Space held on through a landing jumps once ({jumped}), then stands {held * Dt:F1} s on the ground, the pack filling again ({fuelLanded:F2} -> {fuelAfter:F2} s)");
        for (int i = 0; i < 30; i++) yield return 0;
    }

    /// <summary>
    /// Ground too steep to stand on (a long 60° hillside, on the world layer like the terrain's own collision): the sleeper
    /// slides down it, as on scree, no faster than a slide goes, and walks away from the foot unhurt.
    /// </summary>
    IEnumerable<int> Slide()
    {
        Player p = _w.Player;
        N.Vector3 at = _w.Ground(-60f, 70f) + new N.Vector3(0f, 60f, 0f);
        const float Length = 40f, Angle = 60f;
        float rad = Angle * MathF.PI / 180f;
        var hill = new StaticBody3D { Name = "Hillside", CollisionLayer = GalHack.Physics.Layers.World, CollisionMask = 0 };
        hill.AddChild(new CollisionShape3D { Shape = new BoxMesh { Size = new Vector3(6f, 1f, Length) }.CreateTrimeshShape() });
        var foot = new CollisionShape3D { Shape = new BoxMesh { Size = new Vector3(30f, 1f, 30f) }.CreateTrimeshShape() };
        hill.AddChild(foot);
        _w.AddChild(hill);
        // The slab tilted down toward +z, its top face the hillside; a level floor at its foot.
        hill.GlobalPosition = at.ToGodot();
        ((CollisionShape3D)hill.GetChild(0)).Basis = new Basis(Vector3.Right, rad);
        float drop = Length * 0.5f * MathF.Sin(rad), run = Length * 0.5f * MathF.Cos(rad);
        foot.Position = new Vector3(0f, -drop - 0.5f, run + 15f - 0.5f);
        p.PlaceOutside(at + new N.Vector3(0f, drop + 0.6f, -run + 0.3f));
        p.Yaw = MathF.PI; // facing down the hill (+z)
        float health = p.Health.Current, fastest = 0f;
        for (int t = 0; t < 360; t++)
        {
            p.Legs.Move = t < 300 ? new N.Vector2(0f, -1f) : N.Vector2.Zero;
            yield return 0;
            fastest = MathF.Max(fastest, p.Body.Velocity.Length());
        }
        p.Legs.Move = N.Vector2.Zero;
        float lost = health - p.Health.Current;
        bool down = p.Body.OnFloor && p.Body.Position.Y < at.Y - drop + 0.3f;
        GD.Print($"  (down a {Length:F0} m {Angle:F0}° hillside, {2 * drop:F0} m high: fastest {fastest:F1} m/s, {lost:F0} health lost of {p.Health.Max:F0})");
        Check(down && lost <= 0f && fastest < 12f, $"a hillside too steep to stand on is slid down, as on scree: at most {fastest:F1} m/s, unhurt ({lost:F0} lost), standing at its foot ({down})");
        hill.GetParent().RemoveChild(hill);
        hill.QueueFree();
        p.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 10; i++) yield return 0;
    }
}
