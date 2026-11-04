using System;
using Godot;

namespace Coop;

/// <summary>Runs a callback each physics tick at a chosen priority (after ships and players have moved).</summary>
public partial class LateTick : Node
{
    public Action Tick;
    public int Priority = 20;

    public override void _Ready() => ProcessPhysicsPriority = Priority;

    public override void _PhysicsProcess(double delta) => Tick?.Invoke();
}
