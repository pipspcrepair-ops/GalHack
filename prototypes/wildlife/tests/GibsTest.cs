using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Bodies blown apart and burned away (owner, 13 Nov), fresh bodies each run: people, a Husk and a
/// walking machine burst on a floor with a crate on it; every piece comes to rest on
/// something solid (never floating, never through the floor or the crate), the pieces stay within
/// the cap, the physics stays inside the frame budget, and an ash heap sits on the ground.
///   godot --headless --path prototypes/wildlife res://tests/GibsTest.tscn
/// </summary>
public partial class GibsTest : Node3D
{
    Gibs _gibs;
    int _frame, _failures;
    double _worstMs;
    readonly Stopwatch _clock = new();

    public override void _Ready()
    {
        var floor = new StaticBody3D { CollisionLayer = Layers.World };
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(60f, 1f, 60f) }, Position = Vector3.Down * 0.5f });
        floor.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1f, 0.8f, 1f) }, Position = new Vector3(1.2f, 0.4f, 0f) }); // a crate
        AddChild(floor);
        _gibs = new Gibs();
        AddChild(_gibs);
        var rng = new Rng((ulong)DateTime.UtcNow.Ticks);
        WildlifeData data = DataFiles.LoadWildlife();
        var w = new WorldConditions { Seed = rng.NextU64(), Alienness = rng.Range(0f, 0.8f), TempC = rng.Range(-10f, 30f), Coreward = 1f };
        WorldLife life = Bestiary.Roll(data, w);
        var bodies = new List<Species> { life.People.Person(rng.NextU64(), PersonRole.Guard), life.People.Person(rng.NextU64(), PersonRole.Worker) };
        bodies.AddRange(life.Moving.Where(s => s.Husk).Take(1));
        for (int n = 0; n < 30 && !bodies.Any(s => s.Mechanical && s.Rig == RigKind.Humanoid); n++) { Species r = Robots.Roll(w, n); if (r.Rig == RigKind.Humanoid) bodies.Add(r); }
        int k = 0;
        foreach (Species s in bodies)
        {
            var rig = new HumanRig(s);
            var at = new Vector3((k++ - bodies.Count * 0.5f) * 1.5f, 0f, rng.Range(-1f, 1f));
            rig.Drive(new Gesture { Feet = at, Clock = 1f });
            _gibs.Burst(s, rig.Layout, rig.Pose, at + new Vector3(rng.Range(-1f, 1f), -0.3f, rng.Range(-1f, 1f)), 7f);
        }
        _gibs.Ash(new Vector3(-4f, 0f, 4f), Vector3.Up, 0.55f, rng.NextU64());
        GD.Print($"(burst {bodies.Count} bodies: {_gibs.Thrown} pieces)");
    }

    void Check(bool ok, string what)
    {
        GD.Print((ok ? "PASS  " : "FAIL  ") + what);
        if (!ok) _failures++;
    }

    public override void _PhysicsProcess(double delta)
    {
        _worstMs = Math.Max(_worstMs, Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0);
        if (++_frame < 60 * 9 && !(_frame > 60 && _gibs.AllResting)) return;
        SetPhysicsProcess(false);
        var space = GetWorld3D().DirectSpaceState;
        int floating = 0, sunk = 0;
        foreach (RigidBody3D b in _gibs.Pieces)
        {
            var shape = (CollisionShape3D)b.GetChild(0);
            Vector3 half = ((BoxShape3D)shape.Shape).Size * 0.5f;
            Aabb box = b.GlobalTransform * new Aabb(shape.Position - half, half * 2f);
            if (box.Position.Y < -0.03f) sunk++;
            // Resting on something: a ray from its middle straight down meets a floor, a crate or another piece just under it.
            var q = PhysicsRayQueryParameters3D.Create(box.GetCenter(), box.GetCenter() + Vector3.Down * (box.Size.Y * 0.5f + 0.08f), Layers.World | _gibs.Layer);
            q.Exclude = new Godot.Collections.Array<Rid> { b.GetRid() };
            if (space.IntersectRay(q).Count == 0 && box.Position.Y > 0.05f) floating++;
        }
        Check(_gibs.Thrown >= 20, $"bodies come apart into pieces of themselves and their insides ({_gibs.Thrown} pieces)");
        Check(_gibs.AllResting, $"every piece comes to rest ({_frame / 60f:F1} s)");
        Check(floating == 0 && sunk == 0, $"each rests on something solid: nothing floating, nothing through the floor or the crate ({floating} floating, {sunk} sunk of {_gibs.Count})");
        Check(_gibs.Count <= _gibs.MostPieces, $"pieces stay within the cap ({_gibs.Count} of {_gibs.MostPieces})");
        Check(_worstMs < 16.7, $"the physics of it stays inside the frame ({_worstMs:F1} ms at worst)");
        Check(_gibs.AshHeaps == 1, "a body burned away leaves a heap of ash on a scorch");
        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }
}
