using System;
using System.Collections.Generic;
using Godot;
using Invasions.Director;
using Invasions.Model;

namespace Invasions.Combat;

// The seam between the invasion systems and combat. Everything the invasion layer needs from
// soldiers, vehicles and guns goes through these few interfaces. The implementations in this folder
// are deliberately simple placeholders; the boarding prototype's soldier AI, gunplay and A* replace
// them by implementing the same interfaces (and IAssaultPlanner in Model/BreachPlanner.cs).

public enum Faction { Defenders, Vharn }

/// <summary>Anything a shot can hurt: players, soldiers, crew, turrets, base blocks, the garrison core.</summary>
public interface IDamageable
{
    Faction Faction { get; }
    bool IsAlive { get; }
    Vector3 AimPoint { get; }
    void TakeDamage(float amount, Vector3 from, Faction by);
}

public enum TroopState { Assaulting, Withdrawing, Gone, Dead }

/// <summary>What the invasion layer needs from a Vharn foot soldier.</summary>
public interface IInvaderTroop
{
    Node3D Body { get; }
    string Role { get; }
    TroopState State { get; }
    bool IsAlive { get; }
    /// <summary>Withdrawn: extracted by a vehicle or walked out of the area.</summary>
    bool IsGone { get; }
    /// <summary>The vehicle that brought them, and should take them home.</summary>
    IInvaderVehicle Ride { get; set; }

    /// <summary>Fight toward the core, breaking through whatever is in the way.</summary>
    void Assault();
    /// <summary>Stop fighting and leave: to <paramref name="pickup"/>'s vehicle, or on foot out of the area if null.</summary>
    void Withdraw(IInvaderVehicle pickup);
}

/// <summary>What the invasion layer needs from a crewed NPC vehicle.</summary>
public interface IInvaderVehicle
{
    Node3D Body { get; }
    VehiclePlan Plan { get; }
    /// <summary>Driver or pilot plus gunners. A dead gunner's gun goes quiet; a dead driver stops the vehicle.</summary>
    VehicleCrew Crew { get; }
    /// <summary>Troops still aboard: not yet unloaded, or picked up again.</summary>
    int TroopsAboard { get; }
    bool IsGone { get; }
    bool IsStranded { get; }
    /// <summary>Shots fired per gun, so tests (and the HUD) can see which guns are live.</summary>
    IReadOnlyList<int> ShotsFired { get; }
    Vector3 PickupPoint { get; }
    bool ReadyForPickup { get; }
    /// <summary>The crew's hit boxes, for defenders to aim at.</summary>
    IEnumerable<IDamageable> CrewTargets { get; }

    /// <summary>Raised for each troop that climbs out at the drop point.</summary>
    event Action<IInvaderVehicle, string, Vector3> TroopDismounted;

    /// <summary>Stop unloading and wait to take troops back aboard.</summary>
    void AwaitPickup();
    void Extract(IInvaderTroop troop);
    /// <summary>Leave the area. Ignored if the driver is dead.</summary>
    void Depart();
    /// <summary>A stranded vehicle's crew climb out to walk home. Returns where they stand.</summary>
    IReadOnlyList<Vector3> BailOut();
}

/// <summary>Builds the invaders. The boarding prototype supplies its own factory.</summary>
public interface ICombatFactory
{
    IInvaderVehicle SpawnVehicle(VehiclePlan plan, VehicleRoute route);
    IInvaderTroop SpawnTroop(string role, Vector3 position, Vector3 facing);
}

/// <summary>Where a vehicle comes from and where it sets its troops down.</summary>
public readonly record struct VehicleRoute(Vector3 Deploy, Vector3 Drop, Vector3 BaseCentre, Vector3 Outward, float GoneDistance);
