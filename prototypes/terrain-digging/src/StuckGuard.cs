using System;
using System.Collections.Generic;
using Godot;
using TerrainDigging.Terrain;

namespace TerrainDigging;

/// <summary>Something the stuck guard keeps out of the ground: a player, creature or vehicle.</summary>
public interface IStuckSafe
{
    string Label { get; }
    Node3D Node { get; }
    Vector3 Feet { get; }

    /// <summary>The body as it is now (a crouching player is short).</summary>
    BodyShape Current { get; }

    /// <summary>The room to look for when rescuing: enough to stand up in, so nobody is rescued into a crawlspace.</summary>
    BodyShape Rescue { get; }

    void MoveTo(Vector3 feet);
}

/// <summary>
/// Rule 7: anything found inside terrain is moved to the nearest open space. Checked every
/// physics tick against the density field (cheap: a few samples per body). A body must be
/// inside for two ticks in a row, so a single frame of physics overlap never teleports anyone.
/// </summary>
public partial class StuckGuard : Node
{
    const int TicksBeforeRescue = 2;

    public ChunkStore Store;

    /// <summary>Who was moved, from where, to where.</summary>
    public event Action<IStuckSafe, Vector3, Vector3> Rescued;

    public int Rescues { get; private set; }

    readonly List<IStuckSafe> _bodies = new();
    readonly Dictionary<IStuckSafe, int> _stuckTicks = new();

    public void Watch(IStuckSafe body)
    {
        _bodies.Add(body);
        _stuckTicks[body] = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        foreach (IStuckSafe body in _bodies)
        {
            if (!GodotObject.IsInstanceValid(body.Node) || !body.Node.IsInsideTree()) continue;
            Vector3 feet = body.Feet;
            if (!StuckResolver.IsInsideTerrain(Store, ToNumerics(feet), body.Current))
            {
                _stuckTicks[body] = 0;
                continue;
            }
            if (++_stuckTicks[body] < TicksBeforeRescue) continue;
            _stuckTicks[body] = 0;
            Rescue(body);
        }
    }

    public void Rescue(IStuckSafe body)
    {
        Vector3 from = body.Feet;
        OpenSpace space = StuckResolver.FindOpenSpace(Store, ToNumerics(from), body.Rescue);
        if (space.Fallback && body.Current != body.Rescue)
        {
            // No standing room nearby: settle for room in the body's current (crouched) shape.
            OpenSpace tight = StuckResolver.FindOpenSpace(Store, ToNumerics(from), body.Current);
            if (!tight.Fallback) space = tight;
        }
        var to = new Vector3(space.Feet.X, space.Feet.Y, space.Feet.Z);
        body.MoveTo(to);
        Rescues++;
        Rescued?.Invoke(body, from, to);
    }

    /// <summary>
    /// Wakes sleeping physics bodies over a chunk whose collision just changed. Physics engines
    /// don't wake a resting body when the ground under it is removed, so a crate would hover.
    /// </summary>
    public void WakeBodiesIn(Aabb region)
    {
        foreach (IStuckSafe body in _bodies)
            if (body.Node is RigidBody3D rigid && GodotObject.IsInstanceValid(rigid) && region.Grow(1f).HasPoint(rigid.GlobalPosition))
                rigid.Sleeping = false;
    }

    static System.Numerics.Vector3 ToNumerics(Vector3 v) => new(v.X, v.Y, v.Z);
}

/// <summary>The player as a stuck-safe body: stuck checks use the current (maybe crouched) capsule.</summary>
public sealed class PlayerBody : IStuckSafe
{
    readonly Player _player;

    public PlayerBody(Player player) => _player = player;

    public string Label => "Player";
    public Node3D Node => _player;
    public Vector3 Feet => _player.GlobalPosition;
    public BodyShape Current => new(_player.CurrentHeight, Player.Radius);
    public BodyShape Rescue => new(Player.StandHeight, Player.Radius);
    public void MoveTo(Vector3 feet) => _player.Teleport(feet);
}

/// <summary>A crate, creature or vehicle: a rigid box, treated as a squat capsule around its centre.</summary>
public sealed class PropBody : IStuckSafe
{
    readonly RigidBody3D _body;
    readonly float _size;

    public PropBody(RigidBody3D body, string label, float size)
    {
        _body = body;
        _size = size;
        Label = label;
    }

    public string Label { get; }
    public Node3D Node => _body;
    public Vector3 Feet => _body.GlobalPosition - Vector3.Up * (_size * 0.5f);
    public BodyShape Current => new(_size, _size * 0.5f);
    public BodyShape Rescue => Current;

    public void MoveTo(Vector3 feet)
    {
        _body.GlobalPosition = feet + Vector3.Up * (_size * 0.5f + 0.05f);
        _body.LinearVelocity = Vector3.Zero;
        _body.AngularVelocity = Vector3.Zero;
        _body.Sleeping = false;
        _body.ResetPhysicsInterpolation();
    }
}
