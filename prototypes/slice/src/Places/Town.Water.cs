using System;
using System.Collections.Generic;
using Godot;
using PlanetSurface.World;
using Wildlife;

namespace Slice.Places;

/// <summary>
/// The town's people in water (owner: "add real swimming to shore"): in over their chest they swim as the sleeper swims (laid out,
/// the head clear, at a swimming pace), shallower they wade, slowed, on its floor; either way they make for the nearest bank they
/// can climb out on (<see cref="Shore"/>), frightened first away from what frightened them, and walk or clamber out, never leap,
/// then on from there. With no bank in reach they tread water and look again: nobody stands on the water, and nobody is put back
/// anywhere.
/// </summary>
public sealed partial class Town
{
    public sealed partial class Person
    {
        /// <summary>Swimming (over their depth) or wading (in it to below their chest); the bank they make for (the place's frame), when they last looked for one, how long the water has had them.</summary>
        public bool Swimming, Wading;
        public Vector3? Bank;
        public float BankAt = -99f, InWaterFor;
    }

    /// <summary>The water's surface over a point (the place's frame), or null on dry ground (and on a lava world, whose seas aren't entered).</summary>
    float? Surface(Vector3 local) => _places?.Zone.Planet?.WaterSurfaceAt(World(local)) is float s ? s - _origin.Y : null;

    /// <summary>The land's top under a point as the planet has it, dug or not (the place's frame): cheap, for looking for a bank.</summary>
    float LandAt(Vector3 local) => _places?.Zone.Planet is { } planet && World(local) is var w ? planet.Terrain.GroundNow(w.X, w.Z) - _origin.Y : local.Y;

    /// <summary>The ground under a point as it is built (the place's frame): under water, the bed a river has cut, by its collider where there is one.</summary>
    float Bottom(Vector3 local) => Top(local with { Y = Surface(local) ?? local.Y + 0.5f }, 0.5f, 60f) ?? LandAt(local);

    /// <summary>How deep someone wades and from how deep they swim, as the sleeper does (shares of their height), m; and how high a bank they climb out onto.</summary>
    float Wade(Person p) => Height(p) * (_places?.Zone.Physics.Rules.Water.WadeDepth ?? 0.55f);
    float SwimDepth(Person p) => Height(p) * (_places?.Zone.Physics.Rules.Water.SwimDepth ?? 0.7f);
    static float BankClimb(Person p) => Height(p) * 0.6f;

    /// <summary>Whether a point stands in water (more than ankle-deep), the place's frame.</summary>
    bool InWaterAt(Vector3 local) => Surface(local) is float top && top - Bottom(local) > 0.15f;

    /// <summary>In the water (landed in it, or walked into it out on a run): their way given up, the nearest bank looked for.</summary>
    void EnterWater(Person p)
    {
        Unseat(p);
        (p.Path, p.Step, p.Asked, p.ClimbTo, p.Clambering, p.InWaterFor, p.Bank, p.BankAt, p.CheckFor) = (null, 0, -1, null, false, 0f, null, -99f, 0f);
        p.Swimming = Surface(p.Pos) is float top && top - Bottom(p.Pos) > SwimDepth(p);
        p.Wading = !p.Swimming;
    }

    /// <summary>A step in the water: for the bank (away from danger first, frightened), swimming or wading; out on it, on from there. True while the water has them.</summary>
    bool Water(Person p, float dt)
    {
        p.InWaterFor += dt;
        float bottom = Bottom(p.Pos);
        if (Surface(p.Pos) is not float top || top - bottom <= 0.15f)
        {
            // Out on the bank: up onto it at a clamber where it is a step up, never a leap; then on from there.
            if (bottom - p.Pos.Y > 0.05f) { p.Pos.Y = MathF.Min(bottom, p.Pos.Y + Clamber * dt); return true; }
            p.Pos.Y = bottom;
            OutOfWater(p);
            return false;
        }
        float depth = top - bottom;
        (p.Swimming, p.Wading) = (depth > SwimDepth(p), depth <= SwimDepth(p));
        if (p.Bank == null && _clock - p.BankAt > 5f)
        {
            p.BankAt = _clock;
            p.Bank = Shore.Nearest(p.Pos, (x, z) => Surface(new Vector3(x, 0f, z)), (x, z) => LandAt(new Vector3(x, 0f, z)), BankClimb(p), 80f);
        }
        // Frightened, away from what frightened them first; then for the bank (none in reach: treading where they are).
        Vector3 danger = _clock - p.FleeAt < 4f ? p.FleeFrom : _you;
        Vector3 to = p.ScaredFor > 0f && p.InWaterFor < 3f ? p.Pos - danger : p.Bank is { } bank ? bank - p.Pos : Vector3.Zero;
        to.Y = 0f;
        float pace = p.Swimming ? _places.Zone.Physics.Rules.Water.SwimSpeed : p.Speed * 0.5f;
        p.Now = Mathf.MoveToward(p.Now, to.LengthSquared() > 0.04f ? pace : 0f, PeopleAccel * dt);
        if (to.LengthSquared() > 1e-4f) p.Yaw = Mathf.RotateToward(p.Yaw, MathF.Atan2(-to.X, -to.Z), 3f * dt);
        Vector3 next = p.Pos + p.Forward * p.Now * dt;
        float under = Bottom(next);
        // Swimming, the head clear at the surface; wading, on its floor; a step too high to stride up out of it, clambered.
        next.Y = p.Swimming ? Mathf.MoveToward(p.Pos.Y, MathF.Max(top - SwimDepth(p), under), 2f * dt) : under;
        if (next.Y - p.Pos.Y > StepDown) next = p.Pos with { Y = p.Pos.Y + Clamber * dt };
        p.Gait += p.Now * dt;
        p.Pos = next;
        return true;
    }

    /// <summary>
    /// Out of the water: on from the bank. Out on a run, on along their way from its nearest point ahead; in the place, by the way
    /// <see cref="OnFrom"/> finds; out on the land with no run, home over it to the way in.
    /// </summary>
    void OutOfWater(Person p)
    {
        (p.Swimming, p.Wading, p.Bank) = (false, false, null);
        if (p.Run is { } r)
        {
            int near = r.Leg;
            for (int i = r.Leg; i < r.Way.Count; i++) if (Flat(r.Way[i] - p.Pos) < Flat(r.Way[near] - p.Pos)) near = i;
            r.Leg = Math.Min(near + 1, r.Way.Count - 1);
            return;
        }
        if (Memory.Inst.Blocks.InBounds(Cell(p.Pos)))
        {
            if (OnFrom(p) == SpotSearchSpent) Next(p);
            return;
        }
        if (_places?.Zone.Planet is not PlanetWorld planet) return;
        // (Home over the land, as a scavenger comes home: by the way in.)
        var way = new List<Vector3> { World(p.Pos) };
        Densify(planet, way, World(At(Gate)));
        for (int i = 0; i < way.Count; i++) way[i] = Local(way[i]);
        p.Run = new ScavRun { Out = way, Round = new List<Vector3> { way[0] }, Way = way, Find = way[0], Home = true, Why = "out of the water" };
    }

    /// <summary>The sleeper's swimming pose on a body the rig has posed standing: laid out along the way they face, the head clear of the water.</summary>
    static void LayOut(Person p)
    {
        // (As the sleeper's own figure is laid out: tipped forward about a point a little over a metre up.)
        Vector3 pivot = p.Pos + Vector3.Up * 1.1f;
        Basis facing = new(Vector3.Up, p.Yaw);
        var tip = new Transform3D(facing * new Basis(Vector3.Right, -1.2f) * facing.Inverse(), Vector3.Zero);
        Transform3D about = new Transform3D(Basis.Identity, pivot) * tip * new Transform3D(Basis.Identity, -pivot);
        Transform3D[] pose = p.Rig.Pose;
        for (int i = 0; i < pose.Length; i++) pose[i] = about * pose[i];
    }
}
