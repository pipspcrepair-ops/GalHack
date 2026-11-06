using System;
using System.Collections.Generic;
using Godot;
using LandformBlend = Planets.Gen.LandformBlend;

namespace Wildlife;

/// <summary>
/// Keeps a planet's fauna about. At the start it puts each species where its behaviour
/// belongs: herds on the plain, giants far off, ambushers by the groves, hunters out in the
/// hills. After that it counts heads every few seconds, and a species that has fallen short
/// (kills, harvests, scavenged corpses) gets a group back once its respawn time is up.
///
/// The rules for every spot, from <see cref="SpawnRules"/>:
/// - Nothing spawns or respawns inside a base's zone: within the clearance of any base-mode
///   structure's footprint edge, with room for the whole group's spread. Capital-mode
///   structures block nothing. Creatures already outside may wander in; nothing pushes them out.
/// - Respawns also land away from every player and out of their view, so nothing appears in sight.
/// A group with nowhere it may go waits for the next check. Pure C#: the tests run hours of it.
/// </summary>
public sealed class Spawner
{
    readonly WildSim _sim;
    readonly PlanetFauna _fauna;
    readonly IReadOnlyList<Vector3> _groves;
    readonly SpawnRules _rules;
    readonly Rng _rng;
    readonly Dictionary<Species, int> _target = new();
    readonly Dictionary<Species, float> _wait = new();
    float _check;

    /// <summary>How many creatures each species should have about.</summary>
    public IReadOnlyDictionary<Species, int> Target => _target;
    /// <summary>Candidate spots turned down because they were in a base's zone.</summary>
    public int RejectedForBase { get; private set; }
    /// <summary>Candidate spots turned down because a player was too near or could see them.</summary>
    public int RejectedForPlayers { get; private set; }
    /// <summary>Groups that found nowhere to go this time and wait for the next check.</summary>
    public int Deferred { get; private set; }
    public int Respawned { get; private set; }

    /// <summary>
    /// The landforms at a spot (sim frame), if the planet has them. When set, a species with home
    /// landforms only spawns where they hold: deep in a region, only its own species; near a
    /// border, either side's, as much as each holds there. Unset, everything spawns anywhere.
    /// </summary>
    public Func<Vector3, LandformBlend> LandformAt { get; set; }

    /// <summary>Candidate spots turned down because they were not the species' home ground.</summary>
    public int RejectedForLandform { get; private set; }

    /// <summary>How livable the world is, 0..1: it scales how many groups a species has.</summary>
    public float Livable { get; set; } = 1f;

    public Spawner(WildSim sim, PlanetFauna fauna, IReadOnlyList<Vector3> groves, ulong seed)
    {
        _sim = sim;
        _fauna = fauna;
        _groves = groves;
        _rules = sim.Data.Spawning;
        _rng = Rng.For(seed, "population", fauna.Bloom ? 1 : 0);
    }

    /// <summary>The planet's opening population: each species' groups where they belong.</summary>
    public void Populate()
    {
        foreach (Species s in _fauna.Species)
        {
            float natural = s.Plan == BodyPlan.Giant ? 2 : s.Behaviour == Behaviour.Herd || s.Rig == RigKind.Swarm ? 2 : 3;
            int groups = SpawnRules.Groups(natural * _rules.Density * Livable, _rng);
            int total = 0;
            for (int g = 0; g < groups; g++)
            {
                int count = Math.Max(1, _rng.Int(s.GroupMin, s.GroupMax));
                total += count;
                Place(s, Habitat(s, g), count, respawn: false);
            }
            _target[s] = total;
            _wait[s] = _rules.RespawnSeconds;
        }
    }

    /// <summary>Counts heads every few seconds and brings back a group for any species that has fallen short.</summary>
    public void Step(float dt)
    {
        _check -= dt;
        if (_check > 0f) return;
        float elapsed = _rules.CheckSeconds - _check;
        _check = _rules.CheckSeconds;

        var alive = new Dictionary<Species, int>();
        foreach (Creature c in _sim.Creatures)
            if (!c.Removed && c.Alive) alive[c.Species] = alive.GetValueOrDefault(c.Species) + 1;

        foreach (Species s in _fauna.Species)
        {
            int missing = _target.GetValueOrDefault(s) - alive.GetValueOrDefault(s);
            if (missing <= 0)
            {
                _wait[s] = _rules.RespawnSeconds; // the clock starts when it first falls short
                continue;
            }
            _wait[s] = _wait.GetValueOrDefault(s, _rules.RespawnSeconds) - elapsed;
            if (_wait[s] > 0f) continue;
            int count = s.Rig == RigKind.Swarm ? 1 : Math.Clamp(_rng.Int(s.GroupMin, s.GroupMax), 1, missing);
            int placed = Place(s, Habitat(s, 1), count, respawn: true);
            if (placed == 0) continue; // nowhere it may go right now: try again next check
            Respawned += placed;
            _wait[s] = _rules.RespawnSeconds;
        }
    }

    /// <summary>
    /// Tries spots near the one wanted, then further out, until one keeps the whole group out
    /// of every base's zone (and, for a respawn, away from players and out of sight). Returns how
    /// many creatures went down.
    /// </summary>
    int Place(Species s, Vector3 wanted, int count, bool respawn)
    {
        // Room for the group's own spread, and for the nudges off rocks each member may get.
        float margin = _sim.GroupRadius(s, count) + 8f;
        for (int attempt = 0; attempt < _rules.Attempts; attempt++)
        {
            Vector3 at = attempt == 0 ? wanted : wanted + Ring(_rng, 10f, 20f + attempt * 12f);
            at = _sim.FreeSpot(at, _rng);
            if (!AtHome(s, at))
            {
                RejectedForLandform++;
                continue;
            }
            if (_sim.InBaseZone(at, margin))
            {
                RejectedForBase++;
                continue;
            }
            if (respawn && !OutOfPlayersWay(at, margin))
            {
                RejectedForPlayers++;
                continue;
            }
            return _sim.SpawnGroup(s, at, count, respawn ? p => OutOfPlayersWay(p, 0f) : null).Count;
        }
        Deferred++;
        return 0;
    }

    /// <summary>Far enough from every player, and not in front of anyone who could see it.</summary>
    bool OutOfPlayersWay(Vector3 at, float margin)
    {
        float cone = MathF.Cos(Mathf.DegToRad(_rules.ViewConeDegrees));
        foreach (ITarget t in _sim.Targets)
        {
            Vector3 to = WildSim.Flat(at - t.Position);
            float d = to.Length();
            if (d < _rules.MinPlayerDistance + margin) return false;
            Vector3 look = WildSim.Flat(t.Forward);
            if (d < _rules.ViewDistance + margin && look.LengthSquared() > 1e-4f && look.Normalized().Dot(to / d) > cone) return false;
        }
        return true;
    }

    /// <summary>Whether a spot is a species' home ground: always without landforms, else as much as its homes hold there.</summary>
    bool AtHome(Species s, Vector3 at)
    {
        if (LandformAt == null || s.Homes.Length == 0) return true;
        float w = s.HomeWeight(LandformAt(at));
        return w >= 0.98f || (w > LandformFauna.MinHome && _rng.Float() < w);
    }

    /// <summary>Where a species' groups belong: herds on the plain, ambushers by the groves, giants far off, hunters out in the hills.</summary>
    Vector3 Habitat(Species s, int group)
    {
        // On a planet with landforms, somewhere in the species' home ground first.
        if (LandformAt != null && s.Homes.Length > 0)
        {
            float edge = _sim.Terrain.HalfSize - 20f;
            for (int tries = 0; tries < 60; tries++)
            {
                var spot = new Vector3(_rng.Range(-edge, edge), 0f, _rng.Range(-edge, edge));
                if (s.HomeWeight(LandformAt(spot)) >= 0.6f) return spot;
            }
        }
        if (s.Behaviour == Behaviour.Herd && group == 0) return new Vector3(_rng.Range(-15f, 15f), 0f, _rng.Range(-30f, -15f));
        if (s.Behaviour == Behaviour.Ambusher && _groves.Count > 0) return _groves[_rng.Int(0, _groves.Count - 1)] + Ring(_rng, 14f, 24f);
        if (s.Plan == BodyPlan.Giant) return group == 0 ? new Vector3(_rng.Range(30f, 60f), 0f, _rng.Range(-110f, -80f)) : Ring(_rng, 90f, 180f);
        if (s.Behaviour is Behaviour.Predator or Behaviour.PackHunter) return Ring(_rng, 50f, 120f);
        return Ring(_rng, group == 0 ? 25f : 40f, 170f);
    }

    static Vector3 Ring(Rng rng, float min, float max)
    {
        float a = rng.Range(0f, MathF.Tau), r = rng.Range(min, max);
        return new Vector3(MathF.Cos(a) * r, 0f, MathF.Sin(a) * r);
    }
}
