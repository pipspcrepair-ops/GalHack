using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using ShipHandling.Handling;

namespace ShipHandling.Physics;

/// <summary>
/// The physics module: everything that moves vessels and resolves their contacts lives under
/// this node. One kinematic body per vessel (never one per block), moved by the handling
/// model's rules, with no deformation or stress.
///
/// The rest of the game hears about physics only through three events: <see cref="Impact"/>,
/// <see cref="Landed"/> and <see cref="LiftedOff"/>. Vessels step in a fixed order every
/// tick, so the same inputs always give the same trajectories.
/// </summary>
public partial class FlightPhysics : Node3D
{
    public Tunables Tunables { get; private set; }
    public Planet Planet { get; private set; }

    public List<Vessel> Vessels { get; } = new();
    public List<Crater> Craters { get; } = new();

    /// <summary>A vessel ran into something (or something ran into it).</summary>
    public event Action<ImpactInfo> Impact;

    /// <summary>A vessel settled on the ground and stopped simulating.</summary>
    public event Action<Vessel> Landed;

    /// <summary>A landed vessel woke up because its pilot applied thrust.</summary>
    public event Action<Vessel> LiftedOff;

    /// <summary>Step every physics tick by itself. Tests turn this off and call <see cref="Step"/>.</summary>
    public bool AutoStep = true;

    /// <summary>Wall-clock cost of the last <see cref="Step"/>, in milliseconds.</summary>
    public double LastStepMs { get; private set; }

    readonly Stopwatch _watch = new();

    public FlightPhysics() { }

    public FlightPhysics(Tunables tunables)
    {
        Tunables = tunables;
        Planet = Planet.From(tunables);
        Name = "FlightPhysics";
    }

    public override void _Ready()
    {
        // Vessels move before anything reads their transforms (cameras, art, passengers).
        ProcessPhysicsPriority = -10;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (AutoStep) Step((float)delta);
    }

    /// <summary>Adds a vessel to the world, resting where it is put.</summary>
    public Vessel Spawn(IVesselStructure structure, Vector3 position, float yawDegrees)
    {
        var vessel = new Vessel { Name = structure.Name.Replace(" ", "") };
        vessel.Setup(structure, this);
        AddChild(vessel);
        Vessels.Add(vessel);
        vessel.Place(position, yawDegrees);
        return vessel;
    }

    public void Remove(Vessel vessel)
    {
        Vessels.Remove(vessel);
        vessel.QueueFree();
    }

    public void Step(float dt)
    {
        _watch.Restart();
        for (int i = 0; i < Vessels.Count; i++) Vessels[i].Step(dt);
        LastStepMs = _watch.Elapsed.TotalMilliseconds;
    }

    internal void RaiseImpact(ImpactInfo info)
    {
        if (info.CraterRadius > 0f) Craters.Add(new Crater(info.Point, info.Normal, info.CraterRadius));
        Impact?.Invoke(info);
    }

    internal void RaiseLanded(Vessel v) => Landed?.Invoke(v);
    internal void RaiseLiftedOff(Vessel v) => LiftedOff?.Invoke(v);
}
