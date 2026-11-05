using System.Collections.Generic;
using Godot;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Combat;

/// <summary>
/// A Hollow Bloom growth: it breaks the surface near the base, spreads tendrils toward the walls, and
/// pours out the swarm (mutants and corrupted crews) from its maw. It is the Bloom's carrier, and it
/// keeps the crew rules: its heart, pulsing in the open maw, is the "driver" (kill it and the growth
/// dies, and the unborn die with it), and each spitter pod is a gunner. Withdrawing, the swarm runs
/// back into it and it sinks into the ground.
/// </summary>
public partial class Growth : CrewedVehicle
{
    const float Buried = -4.2f, RiseSpeed = 1.6f, SinkSpeed = 1.2f, TendrilGrowth = 0.5f, TendrilMax = 34f;

    readonly List<(Node3D root, MeshInstance3D mesh)> _tendrils = new();
    Node3D _mound, _heart;
    float _pulse, _spread, _withered;

    protected override TargetClass HullClass => TargetClass.Hover;
    protected override Vector2 Footprint => new(9f, 9f);
    protected override float UnloadInterval => 0.25f;
    protected override bool StrandedCanUnload => false; // the unborn die with the heart

    Vector3 TowardBase => ((Route.BaseCentre - GlobalPosition) with { Y = 0 }).Normalized();
    public override Vector3 PickupPoint => GlobalPosition with { Y = 0 } + TowardBase * 6.5f;
    public override bool ReadyForPickup => Phase == Mode.Pickup;
    /// <summary>How far its tendrils have spread toward the base, metres.</summary>
    public float Spread => _spread;

    public override void _Ready()
    {
        base._Ready();
        // It erupts where the force sets down, not out on the deploy line.
        GlobalPosition = Route.Drop with { Y = Buried };
        Vector3 toBase = (Route.BaseCentre - Route.Drop) with { Y = 0 };
        Rotation = new Vector3(0, Mathf.Atan2(-toBase.X, -toBase.Z), 0);
    }

    protected override Vector3 ExitPoint()
    {
        float side = World.Rng.RandfRange(-2.2f, 2.2f);
        return (GlobalTransform * new Vector3(side, 0, -5.6f)) with { Y = 0.1f };
    }

    protected override void Move(float dt)
    {
        Vector3 p = GlobalPosition;
        _pulse += dt * (Phase == Mode.Stranded ? 0.4f : 3.2f);
        switch (Phase)
        {
            case Mode.Approach:
                // Breaking the surface: earth heaves and the mound rises.
                p.Y = Mathf.MoveToward(p.Y, 0f, RiseSpeed * dt);
                Speed = p.Y < -0.01f ? RiseSpeed : 0f;
                if (Engine.GetPhysicsFrames() % 20 == 0) Fx.Puff(p with { Y = 0.2f } + new Vector3(World.Rng.RandfRange(-4f, 4f), 0, World.Rng.RandfRange(-4f, 4f)), 1.2f);
                if (p.Y >= 0f) Phase = Mode.Unload;
                break;
            case Mode.Depart:
                // Drawing back into the ground.
                p.Y = Mathf.MoveToward(p.Y, Buried - 1f, SinkSpeed * dt);
                Speed = SinkSpeed;
                _spread = Mathf.MoveToward(_spread, 0f, TendrilGrowth * 6f * dt);
                break;
            case Mode.Stranded:
                // The heart is dead: it withers and slumps, and stays as a dead mound.
                Speed = 0f;
                _withered = Mathf.MoveToward(_withered, 1f, 0.4f * dt);
                p.Y = Mathf.MoveToward(p.Y, -1.2f * _withered, 0.6f * dt);
                break;
            default:
                Speed = 0f;
                // Spreading: tendrils creep toward the walls for as long as the growth lives.
                _spread = Mathf.MoveToward(_spread, TendrilMax, TendrilGrowth * dt);
                break;
        }
        GlobalPosition = p;

        float beat = 1f + 0.08f * Mathf.Sin(_pulse) * (1f - _withered);
        _mound.Scale = new Vector3(beat, 1f + (beat - 1f) * 0.5f, beat);
        if (_heart != null && Crew.CanMove) _heart.Scale = Vector3.One * (1f + 0.18f * Mathf.Max(0f, Mathf.Sin(_pulse * 1.5f)));
        foreach (var (root, mesh) in _tendrils)
        {
            float length = Mathf.Max(0.5f, _spread * (0.6f + 0.4f * (root.GetIndex() % 3) / 2f));
            root.Scale = new Vector3(1f, 1f, length);
        }
    }

    protected override bool Departed() => GlobalPosition.Y <= Buried;

    protected override void OnStranded()
    {
        LoseEveryoneAboard();
        Fx.Explosion(GlobalPosition with { Y = 2f }, 3f, withDebris: false);
        Fx.Debris(GlobalPosition with { Y = 2.5f }, Livery.Trim, 18, 9f, 0.5f);
        Fx.SmokeColumn(this, new Vector3(0, 3f, 0), 0.8f);
        foreach (var (_, mesh) in _tendrils) mesh.MaterialOverride = Palette.Mat(Livery.Black, 0.9f);
    }

    /// <summary>Its gunners are spitter pods: they can't climb out and walk.</summary>
    public override IReadOnlyList<Vector3> BailOut()
    {
        LoseEveryoneAboard();
        return System.Array.Empty<Vector3>();
    }

    protected override void Slump(int seat)
    {
        Node3D figure = Figure(seat);
        if (figure == null) return;
        figure.Scale = new Vector3(1f, 0.45f, 1f);
        foreach (Node child in figure.GetChildren())
            if (child is MeshInstance3D mesh) mesh.MaterialOverride = Palette.Mat(Livery.Black, 0.9f);
        Fx.Spark(figure.GlobalPosition + Vector3.Up * 0.4f, Livery.Glow, 0.4f);
    }

    protected override void BuildCrewLook(Node3D figure, int seat)
    {
        if (seat == 0)
        {
            // The heart: a pulsing bile-lit sac in the maw.
            _heart = new Node3D { Name = "Heart", Position = new Vector3(0, 0.45f, 0) };
            figure.AddChild(_heart);
            Palette.Box(_heart, Vector3.Zero, new Vector3(0.8f, 0.9f, 0.7f), Palette.Glow(Livery.Glow, 3.5f));
            Palette.Box(_heart, new Vector3(0, 0.1f, -0.2f), new Vector3(0.5f, 0.5f, 0.5f), Palette.Mat(Livery.Trim, 0.4f));
            return;
        }
        // A spitter pod's bulb (the gun's mouth is on the cradle).
        Palette.Box(figure, new Vector3(0, 0.3f, 0), new Vector3(0.9f, 0.8f, 0.9f), Palette.Mat(Livery.Main, 0.5f));
        Palette.Box(figure, new Vector3(0, 0.55f, -0.3f), new Vector3(0.3f, 0.2f, 0.3f), Palette.Glow(Livery.Glow, 3f));
    }

    protected override void BuildGunLook(Node3D yaw, Node3D pitch)
    {
        Palette.Box(yaw, new Vector3(0, 0.2f, 0), new Vector3(1.2f, 0.5f, 1.2f), Palette.Mat(Livery.Dark, 0.6f));
        Palette.Box(pitch, Vector3.Zero, new Vector3(0.5f, 0.45f, 0.7f), Palette.Mat(Livery.Main, 0.5f));
        Palette.Box(pitch, new Vector3(0, 0, -0.6f), new Vector3(0.32f, 0.32f, 0.7f), Palette.Mat(Livery.Trim, 0.45f));
        Palette.Box(pitch, new Vector3(0, 0, -0.98f), new Vector3(0.22f, 0.22f, 0.06f), Palette.Glow(Livery.Glow, 4f));
    }

    protected override void BuildBody()
    {
        Material flesh = Palette.Mat(Livery.Main, 0.55f);
        Material dark = Palette.Mat(Livery.Dark, 0.7f);
        Material pink = Palette.Mat(Livery.Trim, 0.45f);
        Material bile = Palette.Glow(Livery.Glow, 3f);

        // The mound: lumpy flesh in rough tiers, with the maw opening toward the base (-Z). The maw is
        // left open (no collision in front of the heart) so the heart can be shot.
        _mound = new Node3D { Name = "Mound" };
        AddChild(_mound);
        var lumps = new (Vector3 at, Vector3 size, Material m)[]
        {
            (new(0, 1.2f, 1.6f), new(7.5f, 2.4f, 5.0f), flesh),
            (new(-1.6f, 2.6f, 1.2f), new(3.6f, 2.4f, 3.8f), dark),
            (new(1.8f, 2.4f, 0.6f), new(3.2f, 2.2f, 3.6f), flesh),
            (new(0.2f, 3.8f, 1.6f), new(2.6f, 2.0f, 2.6f), pink),
            (new(-3.2f, 0.8f, -1.2f), new(2.2f, 1.6f, 2.6f), dark),
            (new(3.3f, 0.9f, -1.0f), new(2.0f, 1.8f, 2.4f), dark),
        };
        foreach (var (at, size, m) in lumps)
        {
            MeshInstance3D lump = Palette.Box(_mound, at, size, m);
            lump.Rotation = new Vector3(0, at.X * 0.2f, at.Z * 0.05f);
            Hull.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size * 0.9f }, Position = at });
        }
        // Glowing pustules, and the lips of the maw.
        foreach (Vector3 at in new Vector3[] { new(-2.6f, 2.8f, -0.6f), new(2.5f, 3.0f, -0.4f), new(0.9f, 4.6f, 1.4f), new(-1.4f, 1.6f, 3.6f), new(3.6f, 1.8f, 2.2f) })
            Palette.Box(_mound, at, Vector3.One * 0.6f, bile);
        Palette.Box(_mound, new Vector3(0, 2.3f, -2.9f), new Vector3(3.2f, 0.6f, 0.8f), pink);
        Palette.Box(_mound, new Vector3(0, 0.15f, -3.0f), new Vector3(3.4f, 0.3f, 1.0f), pink);
        Palette.Box(_mound, new Vector3(0, 1.2f, -0.8f), new Vector3(2.6f, 1.9f, 0.2f), Palette.Mat(Livery.Black, 0.9f)); // the dark of the maw, behind the heart

        // Tendrils fanning out toward the base, spreading as it lives.
        for (int i = 0; i < 7; i++)
        {
            float angle = (i - 3) * 0.32f;
            var root = new Node3D { Name = $"Tendril{i}", Position = new Vector3(Mathf.Sin(angle) * 3f, 0.12f, -2.5f), Rotation = new Vector3(0, -angle, 0) };
            AddChild(root);
            MeshInstance3D mesh = Palette.Box(root, new Vector3(0, 0, -0.5f), new Vector3(0.55f - (i % 2) * 0.15f, 0.24f, 1f), i % 3 == 0 ? pink : flesh);
            _tendrils.Add((root, mesh));
        }

        // The heart sits in the open maw where it can be shot; two spitter pods on the shoulders.
        CrewFigure(0, this, new Vector3(0, 0.75f, -2.1f));
        AddGun(new Vector3(-2.4f, 3.4f, -0.6f), -0.4f);
        AddGun(new Vector3(2.4f, 3.4f, -0.4f), 0.4f);
    }
}
