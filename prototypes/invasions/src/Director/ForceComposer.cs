using System;
using System.Collections.Generic;
using System.Linq;

namespace Invasions.Director;

/// <summary>
/// Turns a base's local resources and the players present into an invading force, using only the
/// numbers in the data: the shared scaling, tiers and source kinds (invasions.json), and the invading
/// faction's carriers, vehicles, troop roles and swarm size (factions.json).
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

    public static int TroopCount(BaseStats stats, int players, SourceKindData source, ScalingData k, double factionMultiplier = 1)
    {
        double raw = (k.BaseTroops + k.TroopsPerPoint * Threat(stats, k)) * PlayerMultiplier(players, k) * source.ForceMultiplier *
                     Math.Max(0.1, factionMultiplier);
        int cap = Math.Min(k.MaxTroops, source.MaxTroops);
        return Math.Clamp((int)Math.Round(raw, MidpointRounding.AwayFromZero), Math.Min(k.MinTroops, cap), cap);
    }

    /// <summary>The size of force by troop count, in the faction's own words if it has them.</summary>
    public static string TierFor(int troops, InvasionData data, FactionData faction = null)
    {
        var ordered = data.Tiers.OrderBy(t => t.MinTroops).ToList();
        int index = ordered.FindLastIndex(t => troops >= t.MinTroops);
        if (index < 0) return "force";
        return faction?.TierName(index, ordered[index].Name) ?? ordered[index].Name;
    }

    public static ForcePlan Compose(BaseStats stats, int players, SourceKind kind, FactionData faction, InvasionData data, DeterministicRng rng)
    {
        SourceKindData source = data.SourceKind(kind);
        int troops = TroopCount(stats, players, source, data.Scaling, faction.Tactics.TroopMultiplier);
        var plan = new ForcePlan
        {
            Faction = faction.Id,
            Threat = Threat(stats, data.Scaling),
            PlayerMultiplier = PlayerMultiplier(players, data.Scaling),
            PlayersPresent = players,
            Troops = troops,
            Tier = TierFor(troops, data, faction),
        };

        // Carriers: keep drawing from the faction's mix for this kind of source until everyone has a seat.
        if (!faction.Carriers.TryGetValue(InvasionData.Key(kind), out var carriers)) carriers = faction.Carriers["garrison"];
        var mix = Json.Sorted(carriers);
        int capacity = 0;
        while (capacity < troops && mix.Count > 0)
        {
            string type = rng.PickWeighted(mix);
            VehicleTypeData vt = faction.Vehicles[type];
            plan.Vehicles.Add(new VehiclePlan
            {
                Index = plan.Vehicles.Count,
                Faction = faction.Id,
                Type = type,
                Body = vt.Body,
                Label = vt.Label,
                Flies = vt.Flies,
                Speed = vt.Speed,
                DropDistance = vt.DropDistance,
                UnloadSeconds = vt.UnloadSeconds,
                Guns = new List<string>(vt.Guns),
            });
            capacity += Math.Max(1, vt.Capacity);
        }

        // Seat troops round-robin so every vehicle unloads a similar squad.
        var roles = Json.Sorted(faction.Roles.Where(r => r.Value.Share > 0).ToDictionary(r => r.Key, r => r.Value.Share));
        int next = 0;
        for (int t = 0; t < troops; t++)
        {
            for (int tries = 0; tries < plan.Vehicles.Count; tries++, next++)
            {
                VehiclePlan v = plan.Vehicles[next % plan.Vehicles.Count];
                if (v.Troops.Count >= faction.Vehicles[v.Type].Capacity) continue;
                v.Troops.Add(rng.PickWeighted(roles));
                next++;
                break;
            }
        }

        // Every squad needs someone who can open a wall (a lone rider on a bike is not a squad).
        foreach (VehiclePlan v in plan.Vehicles)
        {
            if (v.Troops.Count < 2) continue;
            int want = Math.Min(faction.MinBreachersPerVehicle, v.Troops.Count);
            int breachers = v.Troops.Count(r => r == faction.BreacherRole);
            for (int i = 0; i < v.Troops.Count && breachers < want; i++)
            {
                if (v.Troops[i] == faction.BreacherRole) continue;
                v.Troops[i] = faction.BreacherRole;
                breachers++;
            }
        }

        // Come in abreast, centred on the approach line, a little out of step.
        int n = plan.Vehicles.Count;
        double spacing = faction.VehicleSpacing ?? data.Approach.VehicleSpacing;
        foreach (VehiclePlan v in plan.Vehicles)
        {
            v.Offset = (v.Index - (n - 1) / 2.0) * spacing;
            v.Delay = rng.Range(0, data.Approach.MaxStaggerSeconds);
        }
        return plan;
    }
}
