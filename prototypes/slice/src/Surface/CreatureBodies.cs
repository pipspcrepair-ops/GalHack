using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Game;
using Slice.Vessels;
using Wildlife;
using Layers = GalHack.Physics.Layers;
using N = System.Numerics;

namespace Slice.Surface;

/// <summary>
/// Creatures are as solid as they look. Each creature near the sleeper gets collision taken
/// from its own generated body (the rig's hit spheres, the same ones shots land on): its torso
/// and head as capsules, the neck joining them where it is long, and on a big creature its legs
/// down to the feet, so a giant is solid at head height and a small critter is a small thing to
/// step round. Flyers have shapes too; a swarm doesn't (it's a cloud). The shapes are pooled
/// bodies on the props layer, and only creatures in range get them, the range growing with the
/// creature. Rays pass through them to the rig's spheres, so a hit always lands on the creature
/// itself. Vessels meet creatures here as well: the one damage formula, shared by both, with the
/// reduced mass, so a small creature is knocked aside or killed and a big one hurts the vessel.
/// </summary>
public partial class CreatureBodies : Node3D
{
    public Game.Game Game;

    public const float BaseRange = 14f, DeepRange = 10f;

    sealed class Part
    {
        public StaticBody3D Body;
        public CapsuleShape3D Shape;
    }

    readonly Stack<Part> _free = new();
    readonly Dictionary<Creature, List<Part>> _parts = new();
    readonly List<Creature> _gone = new();
    readonly HashSet<Creature> _seen = new();
    readonly Dictionary<Creature, float> _struck = new();

    /// <summary>Most shape parts in use at once, and the longest tick here, ms (tests).</summary>
    public int MostParts { get; private set; }
    public double WorstMs { get; private set; }
    public int InUse { get; private set; }

    /// <summary>Hits between vessels and creatures so far (tests).</summary>
    public int VesselHits { get; private set; }

    /// <summary>A range that grows with the creature: a giant is solid from further off.</summary>
    static float RangeOf(Creature c) => BaseRange + MathF.Min(c.Species.Body.Size * 3f, 60f);

    Part Take()
    {
        if (_free.TryPop(out Part p))
        {
            p.Body.CollisionLayer = Layers.Props;
            return p;
        }
        _grew = true;
        var shape = new CapsuleShape3D { Radius = 0.3f, Height = 1f };
        var body = new StaticBody3D { Name = "Part", CollisionLayer = Layers.Props, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = shape });
        AddChild(body);
        WorldRay.Ignored.Add(body.GetRid()); // rays meet the creature's own hit spheres
        return new Part { Body = body, Shape = shape };
    }

    void Give(Part p)
    {
        p.Body.CollisionLayer = 0;
        _free.Push(p);
    }

    /// <summary>Every tick: shapes onto the creatures in range, creatures eased out of the sleeper, vessels meeting creatures.</summary>
    public void KeepOff()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        WildlifePatch patch = Game.Zone.Wildlife;
        if (patch.Sim == null) return;
        Vector3 feet = Game.Survivor.WorldFeet;
        _seen.Clear();
        Fit(patch.Sim, patch.Origin, feet, deep: false);
        foreach (DeepSite s in patch.Sites) Fit(s.Sim, s.GlobalPosition, feet, deep: true);
        _gone.Clear();
        foreach (var (c, parts) in _parts)
        {
            if (_seen.Contains(c)) continue;
            foreach (Part p in parts) Give(p);
            _gone.Add(c);
        }
        foreach (Creature c in _gone) _parts.Remove(c);
        InUse = 0;
        foreach (var (_, parts) in _parts) InUse += parts.Count;
        MostParts = Math.Max(MostParts, InUse);
        foreach (Vessel v in Game.Zone.Vessels)
            if (!v.IsBase && !v.IsWreck && v.Body.Velocity.LengthSquared() > 4f) Strike(v, patch);
        // (The ticks the pool grew don't count: a shape is made once, and reused from then on.)
        if (!_grew) WorstMs = Math.Max(WorstMs, clock.Elapsed.TotalMilliseconds);
        _grew = false;
    }

    bool _grew;

    void Fit(WildSim sim, Vector3 origin, Vector3 feet, bool deep)
    {
        bool onFoot = Game.Survivor.Body.Frame == null && Game.Survivor.Seated == null;
        foreach (Creature c in sim.Creatures)
        {
            if (c.Removed || c.Dead || c.Rig == null || c.Species.Rig == RigKind.Swarm || c.InWalls) continue;
            if (deep && sim.Frozen) continue; // held still (tests only): no statues in the corridors
            Vector3 at = c.Rig.BoundsCenter + origin;
            Vector3 flat = new(at.X - feet.X, 0f, at.Z - feet.Z);
            float d = flat.Length(), range = deep ? DeepRange : RangeOf(c);
            if (d > range || MathF.Abs(at.Y - feet.Y) > range) continue;
            _seen.Add(c);
            // A creature that ended up inside the sleeper is nudged out (the wildlife sim moves on
            // its own and knows nothing of the player's body).
            float radius = Mathf.Clamp(c.Species.Stats.Radius, 0.2f, 2.5f);
            float clear = radius + Slice.Player.Survivor.Radius;
            if (!deep && onFoot && !c.Species.Flies && c.Phase != AttackPhase.Hold && d < clear && d > 1e-3f && MathF.Abs(c.Position.Y + origin.Y - feet.Y) < 2.5f)
                c.Position += flat / d * (clear - d);
            Shape(c, origin);
        }
    }

    readonly List<(Vector3 A, Vector3 B, float R)> _caps = new();

    /// <summary>The creature's capsules, from its rig: one a body sphere, joined to the nearest one before it where they stand apart (a neck, a long back), and big legs down to the feet.</summary>
    void Shape(Creature c, Vector3 origin)
    {
        Rig rig = c.Rig;
        _caps.Clear();
        if (rig.HitCount == 0) _caps.Add((rig.BoundsCenter, rig.BoundsCenter, MathF.Max(0.1f, rig.BoundsRadius * 0.5f)));
        for (int i = 0; i < rig.HitCount; i++)
        {
            Vector4 s = rig.Hits[i];
            var at = new Vector3(s.X, s.Y, s.Z);
            _caps.Add((at, at, s.W));
            if (i == 0) continue;
            int nearest = 0;
            float best = float.MaxValue;
            for (int j = 0; j < i; j++)
            {
                float dj = at.DistanceTo(new Vector3(rig.Hits[j].X, rig.Hits[j].Y, rig.Hits[j].Z));
                if (dj < best) { best = dj; nearest = j; }
            }
            Vector4 o = rig.Hits[nearest];
            if (best > (s.W + o.W) * 0.7f)
                _caps.Add((new Vector3(o.X, o.Y, o.Z), at, MathF.Max(0.06f, MathF.Min(s.W, o.W) * 0.7f)));
        }
        Proportions b = c.Species.Body;
        if (b.Size > 1.8f)
        {
            int legs = Math.Min(rig.FootCount, 6);
            float r = MathF.Max(0.08f, b.LegThickness * 0.6f);
            Vector3 hip = new(rig.Hits[0].X, rig.Hits[0].Y, rig.Hits[0].Z);
            for (int i = 0; i < legs; i++) _caps.Add((hip, rig.FootDrawn(i), r));
        }
        if (!_parts.TryGetValue(c, out List<Part> parts)) _parts[c] = parts = new List<Part>();
        while (parts.Count < _caps.Count) parts.Add(Take());
        while (parts.Count > _caps.Count)
        {
            Give(parts[^1]);
            parts.RemoveAt(parts.Count - 1);
        }
        Vector3 feet = Game.Survivor.WorldFeet;
        for (int i = 0; i < _caps.Count; i++)
        {
            Pose(parts[i], _caps[i].A + origin, _caps[i].B + origin, _caps[i].R);
            // Never shoved by a body: a shape that has come down round the sleeper lets go until they part
            // (a character controller pushed out of it could go through a wall).
            parts[i].Body.CollisionLayer = Round(_caps[i].A + origin, _caps[i].B + origin, _caps[i].R, feet) ? 0u : Layers.Props;
        }
    }

    /// <summary>Whether a capsule overlaps the sleeper's, feet at a point.</summary>
    static bool Round(Vector3 a, Vector3 b, float r, Vector3 feet)
    {
        const float R = Slice.Player.Survivor.Radius, H = Slice.Player.Survivor.Height;
        Vector3 p = feet + Vector3.Up * R, q = feet + Vector3.Up * (H - R);
        // The nearest points of the two segments, the plain way: a few samples along the creature's.
        float best = float.MaxValue;
        for (int i = 0; i <= 4; i++)
        {
            Vector3 s = a.Lerp(b, i / 4f);
            float t = Mathf.Clamp(s.Y, p.Y, q.Y);
            best = MathF.Min(best, s.DistanceTo(new Vector3(p.X, t, p.Z)));
        }
        return best < r + R;
    }

    static void Pose(Part p, Vector3 a, Vector3 b, float r)
    {
        Vector3 d = b - a;
        float length = d.Length();
        float height = length + r * 2f;
        if (MathF.Abs(p.Shape.Radius - r) > 0.02f * MathF.Max(r, 0.2f)) p.Shape.Radius = r;
        if (MathF.Abs(p.Shape.Height - height) > 0.03f * height) p.Shape.Height = height;
        Basis basis = Basis.Identity;
        if (length > 1e-3f)
        {
            Vector3 up = d / length;
            basis = MathF.Abs(up.Y) > 0.9999f ? Basis.Identity : new Basis(new Quaternion(Vector3.Up, up));
        }
        p.Body.GlobalTransform = new Transform3D(basis, (a + b) * 0.5f);
    }

    /// <summary>The creatures' mass from their bodies, kg: about water's density over the body's box.</summary>
    public static float MassOf(Creature c)
    {
        Proportions b = c.Species.Body;
        return MathF.Max(1f, b.Size * MathF.Max(0.1f, b.Width) * MathF.Max(0.1f, b.Height) * 700f + 2f);
    }

    /// <summary>
    /// A vessel on the move against the creatures near it, sphere by sphere against its real
    /// blocks. A hit is the one damage formula with the reduced mass, both sides taking it: a
    /// small creature is flung out of the way and killed if it's enough, a big one barely moves,
    /// and the vessel loses the momentum it gave and takes the same damage.
    /// </summary>
    void Strike(Vessel v, WildlifePatch patch)
    {
        var (min, max) = v.Structure.Bounds();
        float reach = (max - min).ToVector3().Length() * v.Structure.Grid.CellSize * 0.6f + 1f;
        Vector3 centre = v.Body.WorldPosition.ToGodot();
        foreach ((WildSim sim, Vector3 origin) in Sims(patch))
            foreach (Creature c in sim.Creatures)
            {
                if (c.Removed || c.Dead || c.Rig == null || c.InWalls || c.Species.Rig == RigKind.Swarm) continue;
                Vector3 at = c.Rig.BoundsCenter + origin;
                if (at.DistanceTo(centre) > reach + c.Rig.BoundsRadius) continue;
                if (_struck.TryGetValue(c, out float last) && sim.Time - last < 1f) continue;
                for (int i = 0; i < c.Rig.HitCount; i++)
                {
                    Vector4 s = c.Rig.Hits[i];
                    Vector3 p = new Vector3(s.X, s.Y, s.Z) + origin;
                    if (v.DistanceFromHull(v.Body.ToLocal(p.ToNumerics()), s.W + 0.2f) > s.W) continue;
                    Hit(v, c, sim, p);
                    _struck[c] = sim.Time;
                    break;
                }
            }
    }

    IEnumerable<(WildSim, Vector3)> Sims(WildlifePatch patch)
    {
        yield return (patch.Sim, patch.Origin);
        foreach (DeepSite s in patch.Sites) yield return (s.Sim, s.GlobalPosition);
    }

    void Hit(Vessel v, Creature c, WildSim sim, Vector3 at)
    {
        PhysicsRules.ContactData rules = Game.Zone.Physics.Rules.Contacts;
        Vector3 velocity = v.Body.Velocity.ToGodot() - c.Velocity;
        float closing = velocity.Length();
        if (closing < 1f) return;
        Vector3 dir = velocity / closing;
        float mass = MassOf(c), mu = Impacts.ReducedMass(v.Body.Mass, mass);
        float damage = Impacts.Damage(mu, closing, rules);
        Game.Zone.Physics.Hurt(v.Body, damage);
        // What a creature can take in the same units (a creature's integrity by its mass), as a share of its health.
        float integrity = MathF.Max(0.01f, Impacts.Integrity(BodyKind.Creature, mass, Game.Zone.Physics.Rules.Integrity));
        float share = damage / integrity;
        // Momentum passed on: the creature is flung along the vessel's way (and out of its hull), the vessel loses as much.
        float thrown = mu * closing / mass;
        Vector3 aside = dir + new Vector3(dir.Z, 0.4f, -dir.X) * 0.5f;
        c.Position += aside.Normalized() * MathF.Min(thrown * 0.15f + 1f, c.Rig.BoundsRadius + 3f);
        c.Velocity += dir * MathF.Min(thrown, 25f);
        Game.Zone.Physics.AddImpulse(v.Body, -(dir * mu * closing * 0.5f).ToNumerics(), ForceKind.Contact);
        sim.Damage(c, MathF.Max(share * c.Species.Stats.MaxHealth, closing * 0.5f), null);
        VesselHits++;
    }
}
