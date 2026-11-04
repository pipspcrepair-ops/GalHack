using Godot;
using Invasions.Director;

namespace Invasions.Combat;

/// <summary>Spawns the placeholder soldiers and vehicles. The boarding prototype supplies its own ICombatFactory.</summary>
public sealed class PlaceholderCombatFactory : ICombatFactory
{
    readonly CombatWorld _world;

    public PlaceholderCombatFactory(CombatWorld world) => _world = world;

    public IInvaderVehicle SpawnVehicle(VehiclePlan plan, VehicleRoute route)
    {
        CrewedVehicle vehicle = plan.Flies
            ? new Dropship { World = _world, Plan = plan, Route = route, Name = $"Dropship{plan.Index}" }
            : new Apc { World = _world, Plan = plan, Route = route, Name = $"Apc{plan.Index}" };
        Vector3 heading = (route.Drop - route.Deploy) with { Y = 0 };
        vehicle.Rotation = new Vector3(0, Mathf.Atan2(-heading.X, -heading.Z), 0);
        vehicle.Position = route.Deploy;
        _world.AddChild(vehicle);
        vehicle.ResetPhysicsInterpolation();
        _world.Vehicles.Add(vehicle);
        return vehicle;
    }

    public IInvaderTroop SpawnTroop(string role, Vector3 position, Vector3 facing)
    {
        var soldier = new PlaceholderSoldier { World = _world, Role = role, Name = $"Trooper{_world.Troops.Count}" };
        soldier.Position = position;
        soldier.Rotation = new Vector3(0, Mathf.Atan2(-facing.X, -facing.Z), 0);
        _world.AddChild(soldier);
        soldier.ResetPhysicsInterpolation();
        _world.Troops.Add(soldier);
        return soldier;
    }
}
