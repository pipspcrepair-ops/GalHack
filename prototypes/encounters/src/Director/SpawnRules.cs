using System;
using System.Collections.Generic;
using System.Numerics;

namespace Encounters.Director;

public enum SpawnVerdict { Ok, OutOfBounds, NoSpawnCircle, TooClose, InView }

/// <summary>
/// Where a ship may appear. Nothing pops into view: every ship of a new event starts outside the
/// view cone of every player in the zone, beyond the minimum distance from each of them, inside
/// the zone and outside every no-spawn circle and player base.
/// </summary>
public static class SpawnRules
{
    public static bool InView(Vector3 point, PlayerState player, float coneCos)
    {
        Vector3 offset = point - player.Position;
        float length = offset.Length();
        if (length < 1e-3f) return true;
        return Vector3.Dot(offset, player.Forward) / length > coneCos;
    }

    public static SpawnVerdict Check(Vector3 point, ZoneDef zone, IReadOnlyList<PlayerState> players, DirectorConfig config)
    {
        if (!zone.InBounds(point)) return SpawnVerdict.OutOfBounds;
        if (zone.BlocksSpawn(point)) return SpawnVerdict.NoSpawnCircle;
        float minSq = config.MinSpawnDistance * config.MinSpawnDistance;
        float coneCos = config.ViewConeCos;
        foreach (PlayerState player in players)
        {
            if (Vector3.DistanceSquared(point, player.Position) < minSq) return SpawnVerdict.TooClose;
            if (InView(point, player, coneCos)) return SpawnVerdict.InView;
        }
        return SpawnVerdict.Ok;
    }
}

/// <summary>One ship an event is about to spawn, before it has a flight.</summary>
internal readonly record struct PlannedShip(int GroupIndex, string Role, string Faction, string Hull, string Side, int Crew);

/// <summary>
/// Lays out an event's ships and gives each its scripted flight. Positions at the start time
/// are what the spawn rules check.
/// </summary>
internal static class Formation
{
    /// <summary>Somewhere a few hundred metres beside the player: passing traffic should be seen, not hit.</summary>
    public static Vector3 PassTargetNear(Vector3 anchor, Vector3 player, Rng rng)
    {
        Vector3 lateral = MathX.SafeNormalize(Vector3.Cross(player - anchor, Vector3.UnitY), Vector3.UnitX);
        return player + lateral * (rng.Sign() * rng.Range(300f, 900f)) + Vector3.UnitY * rng.Range(-150f, 150f);
    }

    public static void Build(EventDef def, IReadOnlyList<PlannedShip> plan, Vector3 anchor, Vector3 passTarget,
        ZoneDef zone, Rng rng, double t0, List<ShipScript> into)
    {
        into.Clear();
        for (int i = 0; i < plan.Count; i++) into.Add(null);
        float speed = rng.Range(def.Speed[0], def.Speed[1]);
        switch (def.Behaviour)
        {
            case Behaviour.Pass: Pass(plan, anchor, passTarget, speed, rng, t0, into, convoy: false); break;
            case Behaviour.Convoy: Pass(plan, anchor, passTarget, speed, rng, t0, into, convoy: true); break;
            case Behaviour.Dogfight: Dogfight(def, plan, anchor, rng, t0, into); break;
            case Behaviour.Dock: Dock(plan, anchor, zone.FindPoi(def.Anchor), speed, rng, t0, into); break;
            case Behaviour.Mine: Mine(plan, anchor, zone.FindPoi(def.Anchor), speed, rng, t0, into); break;
            default: Drift(plan, anchor, speed, rng, t0, into); break;
        }
    }

    static void Pass(IReadOnlyList<PlannedShip> plan, Vector3 anchor, Vector3 target, float speed, Rng rng, double t0,
        List<ShipScript> into, bool convoy)
    {
        Vector3 dir = MathX.SafeNormalize(target - anchor, -Vector3.UnitZ);
        Vector3 side = MathX.SafeNormalize(Vector3.Cross(dir, Vector3.UnitY), Vector3.UnitX);
        Vector3 up = Vector3.Cross(side, dir);
        int column = 0, escorts = 0;
        for (int i = 0; i < plan.Count; i++)
        {
            Vector3 start;
            if (!convoy)
                start = anchor - dir * (i * 240f) + side * rng.Range(-60f, 60f) + up * rng.Range(-40f, 40f);
            else if (plan[i].GroupIndex == 0) // the haulers, nose to tail
                start = anchor - dir * (column++ * 190f) + up * rng.Range(-10f, 10f);
            else // escorts, alternating sides along the column
            {
                float flank = escorts % 2 == 0 ? 1f : -1f;
                start = anchor - dir * (escorts / 2 * 420f + 90f) + side * (flank * 170f) + up * 45f;
                escorts++;
            }
            into[i] = ShipScript.Line(t0, start, dir * speed);
        }
    }

    /// <summary>
    /// Each side-a ship flies a tilted circle round the fight; its opposite number on side b
    /// chases it round the same circle a little behind. A furball without any real combat.
    /// </summary>
    static void Dogfight(EventDef def, IReadOnlyList<PlannedShip> plan, Vector3 anchor, Rng rng, double t0, List<ShipScript> into)
    {
        var a = new List<int>();
        var b = new List<int>();
        for (int i = 0; i < plan.Count; i++)
            (plan[i].Side == "b" ? b : a).Add(i);

        int pairs = Math.Max(a.Count, b.Count);
        for (int k = 0; k < pairs; k++)
        {
            Vector3 normal = MathX.SafeNormalize(Vector3.UnitY * 1.2f + rng.OnSphere(), Vector3.UnitY);
            Vector3 u = MathX.AnyPerpendicular(normal);
            Vector3 v = Vector3.Cross(normal, u);
            float radius = rng.Range(170f, 420f);
            float omega = rng.Range(def.Speed[0], def.Speed[1]) / radius * rng.Sign();
            float phase = rng.Range(0f, MathF.Tau);
            Vector3 center = anchor + rng.OnSphere() * rng.Range(0f, 180f);
            float bob = rng.Range(15f, 60f);
            if (k < a.Count) into[a[k]] = ShipScript.Orbit(t0, center, u, v, radius, omega, phase, bob);
            if (k < b.Count)
            {
                float lag = MathF.Sign(omega) * rng.Range(0.3f, 0.55f);
                into[b[k]] = ShipScript.Orbit(t0, center, u, v, radius + rng.Range(-30f, 30f), omega, phase - lag, bob);
            }
        }
    }

    /// <summary>Flies in from the spawn point and holds nose-in beside the station.</summary>
    static void Dock(IReadOnlyList<PlannedShip> plan, Vector3 anchor, ZoneCircle poi, float speed, Rng rng, double t0, List<ShipScript> into)
    {
        Vector3 station = poi.Center;
        for (int i = 0; i < plan.Count; i++)
        {
            Vector3 start = anchor + rng.OnSphere() * rng.Range(0f, 80f) + Vector3.UnitY * (i * 110f);
            Vector3 approach = MathX.SafeNormalize(MathX.Flat(start - station), Vector3.UnitX);
            approach = MathX.Rotate(approach, Vector3.UnitY, rng.Range(-0.5f, 0.5f) + i * 0.4f);
            Vector3 dock = station + approach * (poi.R + 90f) + Vector3.UnitY * rng.Range(-80f, 80f);
            Vector3 facing = MathX.SafeNormalize(MathX.Flat(station - dock), -Vector3.UnitZ);
            into[i] = ShipScript.ArriveAndHold(t0, start, dock, speed, facing);
        }
    }

    /// <summary>Flies in to a spot in the asteroid field and circles it slowly, working.</summary>
    static void Mine(IReadOnlyList<PlannedShip> plan, Vector3 anchor, ZoneCircle poi, float speed, Rng rng, double t0, List<ShipScript> into)
    {
        Vector3 site = poi.Center + MathX.Flat(rng.OnSphere()) * rng.Range(0f, poi.R * 0.6f) + Vector3.UnitY * rng.Range(-120f, 120f);
        for (int i = 0; i < plan.Count; i++)
        {
            Vector3 start = anchor + rng.OnSphere() * rng.Range(40f, 160f);
            Vector3 normal = MathX.SafeNormalize(Vector3.UnitY + rng.OnSphere() * 0.35f, Vector3.UnitY);
            Vector3 u = MathX.AnyPerpendicular(normal);
            Vector3 v = Vector3.Cross(normal, u);
            float radius = rng.Range(70f, 170f);
            float omega = rng.Range(5f, 9f) / radius * rng.Sign();
            into[i] = ShipScript.ArriveAndOrbit(t0, start, speed, site, u, v, radius, omega, rng.Range(0f, MathF.Tau));
        }
    }

    static void Drift(IReadOnlyList<PlannedShip> plan, Vector3 anchor, float speed, Rng rng, double t0, List<ShipScript> into)
    {
        Vector3 velocity = rng.AroundHorizon(0.3f) * speed;
        for (int i = 0; i < plan.Count; i++)
        {
            Vector3 start = anchor + (i == 0 ? Vector3.Zero : rng.OnSphere() * (150f * i));
            Vector3 facing = MathX.SafeNormalize(MathX.Flat(rng.OnSphere()), -Vector3.UnitZ);
            into[i] = ShipScript.Line(t0, start, velocity, facing);
        }
    }
}
