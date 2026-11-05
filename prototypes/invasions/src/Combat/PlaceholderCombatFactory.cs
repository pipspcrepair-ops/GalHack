using System;
using Godot;
using Invasions.Director;

namespace Invasions.Combat;

/// <summary>
/// Spawns the placeholder soldiers and vehicles of every faction. A vehicle's body (from factions.json)
/// picks its class; a trooper's faction and role pick its stats, tactics and looks. The boarding
/// prototype supplies its own ICombatFactory.
/// </summary>
public sealed class PlaceholderCombatFactory : ICombatFactory
{
    readonly CombatWorld _world;

    public PlaceholderCombatFactory(CombatWorld world) => _world = world;

    public IInvaderVehicle SpawnVehicle(VehiclePlan plan, VehicleRoute route)
    {
        CrewedVehicle vehicle = plan.Body switch
        {
            "apc" => new Apc { World = _world, Plan = plan, Route = route },
            "dropship" => new Dropship { World = _world, Plan = plan, Route = route },
            "strider" => new Strider { World = _world, Plan = plan, Route = route },
            "lifter" => new Lifter { World = _world, Plan = plan, Route = route },
            "hovertruck" => new HoverTruck { World = _world, Plan = plan, Route = route },
            "shuttle" => new Shuttle { World = _world, Plan = plan, Route = route },
            "hoverbike" => new HoverBike { World = _world, Plan = plan, Route = route },
            "gunship" => new Gunship { World = _world, Plan = plan, Route = route },
            "growth" => new Growth { World = _world, Plan = plan, Route = route },
            _ => throw new InvalidOperationException($"no vehicle body '{plan.Body}' (factions.json: {plan.Faction} {plan.Type})"),
        };
        vehicle.Name = $"{char.ToUpperInvariant(plan.Body[0])}{plan.Body[1..]}{plan.Index}";
        Vector3 heading = (route.Drop - route.Deploy) with { Y = 0 };
        vehicle.Rotation = new Vector3(0, Mathf.Atan2(-heading.X, -heading.Z), 0);
        vehicle.Position = route.Deploy;
        _world.AddChild(vehicle);
        vehicle.ResetPhysicsInterpolation();
        _world.Vehicles.Add(vehicle);
        return vehicle;
    }

    public IInvaderTroop SpawnTroop(string faction, string role, Vector3 position, Vector3 facing)
    {
        var soldier = new PlaceholderSoldier { World = _world, Faction = faction, Role = role, Name = $"Trooper{_world.Troops.Count}" };
        soldier.Position = position;
        soldier.Rotation = new Vector3(0, Mathf.Atan2(-facing.X, -facing.Z), 0);
        _world.AddChild(soldier);
        soldier.ResetPhysicsInterpolation();
        _world.Troops.Add(soldier);
        return soldier;
    }
}
