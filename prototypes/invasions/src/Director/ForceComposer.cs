using System;
using System.Collections.Generic;
using System.Linq;

namespace Invasions.Director;

/// <summary>
/// Turns a base's local resources and the players present into an invading force, using only the
/// numbers in invasions.json (scaling, tiers, source kinds, vehicles, troop roles).
/// </summary>
public static class ForceComposer
{
    /// <summary>How tempting and how dangerous the base is: stored goods value, size and defences.</summary>
    public static double Threat(BaseStats stats, ScalingData k) =>
        stats.StoredValue / k.StoredValuePerPoint + stats.Blocks / k.BlocksPerPoint + stats.Turrets * k.PointsPerTurret;

    public static double PlayerMultiplier(int players, ScalingData k)
    {
        if (players <= 0 || k.PlayerMultipliers.Count == 0) return 1.0;
        int listed = k.PlayerMultipliers.Count;
        return players <= listed
            ? k.PlayerMultipliers[players - 1]
            : k.PlayerMultipliers[listed - 1] + k.ExtraPlayerMultiplier * (players - listed);
    }

    public static int TroopCount(BaseStats stats, int players, SourceKindData source, ScalingData k)
    {
        double raw = (k.BaseTroops + k.TroopsPerPoint * Threat(stats, k)) * PlayerMultiplier(players, k) * source.ForceMultiplier;
        int cap = Math.Min(k.MaxTroops, source.MaxTroops);
        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), Math.Min(k.MinTroops, cap), cap);
    }

    public static string TierFor(int troops, InvasionData data) =>
        data.Tiers.Where(t => troops >= t.MinTroops).OrderBy(t => t.MinTroops).Select(t => t.Name).LastOrDefault() ?? "force";

    public static ForcePlan Compose(BaseStats stats, int players, SourceKind kind, InvasionData data, DeterministicRng rng)
    {
        SourceKindData source = data.SourceKind(kind);
        int troops = TroopCount(stats, players, source, data.Scaling);
        var plan = new ForcePlan
        {
            Threat = Threat(stats, data.Scaling),
            PlayerMultiplier = PlayerMultiplier(players, data.Scaling),
            PlayersPresent = players,
            Troops = troops,
            Tier = TierFor(troops, data),
        };

        // Carriers: keep drawing from the source's vehicle mix until everyone has a seat.
        var mix = Json.Sorted(source.Vehicles);
        int capacity = 0;
        while (capacity < troops && mix.Count > 0)
        {
            string type = rng.PickWeighted(mix);
            VehicleTypeData vt = data.Vehicles[type];
            plan.Vehicles.Add(new VehiclePlan
            {
                Index = plan.Vehicles.Count,
                Type = type,
                Label = vt.Label,
                Flies = vt.Flies,
                Speed = vt.Speed,
                Guns = new List<string>(vt.Guns),
            });
            capacity += Math.Max(1, vt.Capacity);
        }

        // Seat troops round-robin so every vehicle unloads a similar squad.
        var roles = Json.Sorted(data.TroopRoles);
        int next = 0;
        for (int t = 0; t < troops; t++)
        {
            for (int tries = 0; tries < plan.Vehicles.Count; tries++, next++)
            {
                VehiclePlan v = plan.Vehicles[next % plan.Vehicles.Count];
                if (v.Troops.Count >= data.Vehicles[v.Type].Capacity) continue;
                v.Troops.Add(roles.Count > 0 ? rng.PickWeighted(roles) : "rifleman");
                next++;
                break;
            }
        }

        // Every squad needs someone who can open a wall.
        foreach (VehiclePlan v in plan.Vehicles)
        {
            int breachers = v.Troops.Count(r => r == "breacher");
            for (int i = 0; i < v.Troops.Count && breachers < data.MinBreachersPerVehicle; i++)
            {
                if (v.Troops[i] == "breacher") continue;
                v.Troops[i] = "breacher";
                breachers++;
            }
        }

        // Come in abreast, centred on the approach line, a little out of step.
        int n = plan.Vehicles.Count;
        foreach (VehiclePlan v in plan.Vehicles)
        {
            v.Offset = (v.Index - (n - 1) / 2.0) * data.Approach.VehicleSpacing;
            v.Delay = rng.Range(0, data.Approach.MaxStaggerSeconds);
        }
        return plan;
    }
}
