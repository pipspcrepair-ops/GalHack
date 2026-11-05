using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>A rock in an asteroid field: something to graze on and to steer round.</summary>
public sealed class Asteroid
{
    public Vector3 Center;
    public float Radius;
}

/// <summary>
/// A derelict's hull as the leeches see it: a box with six faces to crawl on. Faces are
/// numbered +X, -X, +Y, -Y, +Z, -Z in the hull's own frame.
/// </summary>
public sealed class Hull
{
    public Vector3 Center;
    public Basis Basis = Basis.Identity;
    public Vector3 Half = new(10f, 4f, 30f);

    static readonly Vector3[] Axes = { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };

    public Vector3 Normal(int face) => (Basis * Axes[face]).Normalized();

    /// <summary>A point on a face, u and v running -1 to 1 across it.</summary>
    public Vector3 FacePoint(int face, float u, float v)
    {
        Vector3 n = Axes[face];
        (Vector3 a, Vector3 b) = Tangents(n);
        Vector3 local = n * Extent(n) + a * (u * Extent(a)) + b * (v * Extent(b));
        return Center + Basis * local;
    }

    /// <summary>Snaps a point onto a face, kept <paramref name="inset"/> in from its edges.</summary>
    public Vector3 ClampToFace(int face, Vector3 p, float inset)
    {
        Vector3 n = Axes[face];
        (Vector3 a, Vector3 b) = Tangents(n);
        Vector3 local = Basis.Inverse() * (p - Center);
        float ea = MathF.Max(0f, Extent(a) - inset), eb = MathF.Max(0f, Extent(b) - inset);
        Vector3 snapped = n * Extent(n) + a * Math.Clamp(local.Dot(a), -ea, ea) + b * Math.Clamp(local.Dot(b), -eb, eb);
        return Center + Basis * snapped;
    }

    public float Area(int face)
    {
        (Vector3 a, Vector3 b) = Tangents(Axes[face]);
        return 4f * Extent(a) * Extent(b);
    }

    /// <summary>Signed distance from the box's surface: negative inside.</summary>
    public float SignedDistance(Vector3 p)
    {
        Vector3 local = Basis.Inverse() * (p - Center);
        Vector3 q = new(MathF.Abs(local.X) - Half.X, MathF.Abs(local.Y) - Half.Y, MathF.Abs(local.Z) - Half.Z);
        Vector3 outside = new(MathF.Max(q.X, 0f), MathF.Max(q.Y, 0f), MathF.Max(q.Z, 0f));
        return outside.Length() + MathF.Min(MathF.Max(q.X, MathF.Max(q.Y, q.Z)), 0f);
    }

    /// <summary>Moves a point out of the box (plus a margin) along the nearest face.</summary>
    public Vector3 PushOut(Vector3 p, float margin, out Vector3 normal)
    {
        Vector3 local = Basis.Inverse() * (p - Center);
        int best = 0;
        float least = float.MaxValue;
        for (int f = 0; f < 6; f++)
        {
            Vector3 n = Axes[f];
            float depth = Extent(n) + margin - local.Dot(n);
            if (depth < least) { least = depth; best = f; }
        }
        normal = Normal(best);
        return least > 0f ? p + normal * least : p;
    }

    float Extent(Vector3 axis) => MathF.Abs(axis.X) * Half.X + MathF.Abs(axis.Y) * Half.Y + MathF.Abs(axis.Z) * Half.Z;

    static (Vector3, Vector3) Tangents(Vector3 n) =>
        MathF.Abs(n.X) > 0.5f ? (Vector3.Up, Vector3.Back) : MathF.Abs(n.Y) > 0.5f ? (Vector3.Right, Vector3.Back) : (Vector3.Right, Vector3.Up);
}

/// <summary>
/// The space fauna of one place (a nebula, a belt, a giant's skies, a graveyard of wrecks), in
/// plain C# like <see cref="WildSim"/>: creatures, shoals, the players they react to, and the
/// rocks and hulls they live among. There is no ground and no up: creatures carry a full
/// orientation and move through all three dimensions. Each step runs brain, attacks, motion,
/// then the rig. Attacks follow the planet rules exactly: the first is telegraphed with the
/// species' wind-up, the lunge flies at where the target was at the commit, and damage is never
/// capped. A kill rolls the creature's drop table.
/// </summary>
public sealed class SpaceSim
{
    public readonly WildlifeData Data;
    public readonly List<Creature> Creatures = new();
    public readonly List<Herd> Shoals = new();
    public readonly List<ITarget> Targets = new();
    public readonly List<Asteroid> Asteroids = new();
    public readonly List<Hull> Hulls = new();
    /// <summary>Creatures turn back once they stray this far from the middle of the place.</summary>
    public float BoundsRadius = 1600f;
    public float Time;
    public int Frame;
    int _nextId;

    public event Action<AttackEvent> Telegraph;
    public event Action<AttackEvent> Committed;
    public event Action<AttackEvent> Attacked;
    public event Action<Creature> Warned;
    public event Action<Creature> Spawned;
    /// <summary>A creature died; its loot (rolled from its drop table) is on <see cref="Creature.Loot"/>.</summary>
    public event Action<Creature, ITarget> Died;

    public CombatRules Combat => Data.Combat;

    public SpaceSim(WildlifeData data) => Data = data;

    // ---- Spawning ---------------------------------------------------------------------

    public Creature Spawn(Species species, Vector3 position, Herd herd = null, Vector3? forward = null)
    {
        var c = new Creature
        {
            Id = _nextId++,
            Species = species,
            Herd = herd,
            Health = species.Stats.MaxHealth,
            Home = position,
            Position = position,
        };
        c.Rng = new Rng(Rng.Mix(species.Seed, (ulong)c.Id));
        Vector3 f = forward ?? new Vector3(c.Rng.Range(-1f, 1f), c.Rng.Range(-0.25f, 0.25f), c.Rng.Range(-1f, 1f));
        c.Facing = Look(f, Vector3.Up);
        c.Yaw = MathF.Atan2(-f.X, -f.Z);
        if (herd != null)
        {
            c.HerdSlot = herd.Members.Count;
            herd.Members.Add(c);
        }
        c.Rig = Rig.Create(species, c);
        c.Rig.Reset(null, c);
        Creatures.Add(c);
        Spawned?.Invoke(c);
        return c;
    }

    /// <summary>A shoal: a herd in three dimensions, sharing a goal and an alarm.</summary>
    public List<Creature> SpawnShoal(Species species, Vector3 center, int count)
    {
        var shoal = new Herd { Home = center, Goal = center, Center = center, Spread = ShoalSpread(species, count) };
        Shoals.Add(shoal);
        var rng = Rng.For(species.Seed, "shoal", _nextId);
        Vector3 heading = new(rng.Range(-1f, 1f), rng.Range(-0.2f, 0.2f), rng.Range(-1f, 1f));
        var list = new List<Creature>();
        for (int i = 0; i < count; i++)
            list.Add(Spawn(species, center + SlotOffset(i, count) * shoal.Spread, shoal, heading));
        return list;
    }

    /// <summary>Leeches spread over a hull's faces (or one face), each holding on where it landed.</summary>
    public List<Creature> SpawnOnHull(Species species, Hull hull, int count, Rng rng, int onlyFace = -1)
    {
        var list = new List<Creature>();
        for (int i = 0; i < count; i++)
        {
            int face = onlyFace >= 0 ? onlyFace : rng.Weighted(new[] { 0, 1, 2, 3, 4, 5 }, f => hull.Area(f));
            Vector3 at = hull.FacePoint(face, rng.Range(-0.8f, 0.8f), rng.Range(-0.8f, 0.8f));
            Vector3 n = hull.Normal(face);
            Vector3 along = (n.Cross(new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f)))).Normalized();
            Creature c = Spawn(species, at, null, along);
            Cling(c, hull, face);
            c.Anchor = at;
            c.HomeHull = hull;
            c.HomeFace = face;
            c.Rig.Reset(null, c);
            list.Add(c);
        }
        return list;
    }

    /// <summary>Holds on to a hull face: lies flat on it, belly down.</summary>
    public static void Cling(Creature c, Hull hull, int face)
    {
        c.ClingHull = hull;
        c.ClingFace = face;
        c.Position = hull.ClampToFace(face, c.Position, c.Radius);
        Vector3 n = hull.Normal(face);
        Vector3 f = -c.Facing.Z;
        f -= n * f.Dot(n);
        if (f.LengthSquared() < 1e-4f) f = n.Cross(Vector3.Right).LengthSquared() > 1e-3f ? n.Cross(Vector3.Right) : n.Cross(Vector3.Forward);
        c.Facing = Look(f.Normalized(), n);
        c.Velocity = Vector3.Zero;
    }

    public static float ShoalSpread(Species s, int count) => MathF.Max(s.Stats.Radius * 3f, s.Body.Size * 1.1f) * MathF.Cbrt(MathF.Max(1, count));

    /// <summary>Evenly spread spots in a unit ball, for shoal members.</summary>
    public static Vector3 SlotOffset(int i, int n)
    {
        float k = (i + 0.5f) / MathF.Max(1, n);
        float y = 1f - 2f * k, r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
        float a = i * 2.39996f;
        return new Vector3(MathF.Cos(a) * r, y * 0.6f, MathF.Sin(a) * r) * MathF.Cbrt(k);
    }

    public static Basis Look(Vector3 forward, Vector3 up)
    {
        Vector3 f = forward.LengthSquared() > 1e-6f ? forward.Normalized() : Vector3.Forward;
        Vector3 u = up.LengthSquared() > 1e-6f ? up.Normalized() : Vector3.Up;
        if (MathF.Abs(f.Dot(u)) > 0.98f) u = MathF.Abs(f.Y) < 0.9f ? Vector3.Up : Vector3.Back;
        return Basis.LookingAt(f, u);
    }

    // ---- Stepping ---------------------------------------------------------------------

    public void Step(float dt)
    {
        Time += dt;
        Frame++;
        for (int i = 0; i < Creatures.Count; i++)
        {
            Creature c = Creatures[i];
            if (c.Removed) continue;
            c.Age += dt;
            c.HurtFlash = MathF.Max(0f, c.HurtFlash - dt * 4f);
            if (c.Dead)
            {
                c.DeadTime += dt;
                if (c.DeadTime > 90f) c.Removed = true;
            }
            else
            {
                SpaceBrain.Update(this, c, dt);
                SpaceAttacks.Update(this, c, dt);
            }
            SpaceMotion.Move(this, c, dt);
            c.RigPending += dt;
            if (c.RigInterval <= 1 || (Frame + c.Id) % c.RigInterval == 0)
            {
                c.Rig.Update(null, c, c.RigPending);
                c.RigPending = 0f;
            }
        }
        if (Frame % 120 == 0) Creatures.RemoveAll(c => c.Removed);
    }

    // ---- Damage -----------------------------------------------------------------------

    public void Damage(Creature c, float amount, ITarget source)
    {
        if (c.Dead || amount <= 0f) return;
        c.Health -= amount;
        c.HurtFlash = 1f;
        if (source != null)
        {
            c.ProvokedBy = source;
            c.ProvokedAt = Time;
            c.Herd?.Alarm(source.Position, Time);
        }
        if (c.Health <= 0f) Kill(c, source);
    }

    public void Kill(Creature c, ITarget killer)
    {
        if (c.Dead) return;
        c.Dead = true;
        c.Health = 0f;
        c.DeadTime = 0f;
        c.Phase = AttackPhase.None;
        c.Lunge = c.Rear = 0f;
        c.ClingHull = null; // the dead let go and drift
        c.Loot = SpaceDrops.Roll(c.Species, Rng.For(c.Species.Seed, "loot", c.Id + (long)(Time * 1000f)));
        Died?.Invoke(c, killer);
    }

    internal void RaiseTelegraph(AttackEvent e) => Telegraph?.Invoke(e);
    internal void RaiseCommitted(AttackEvent e) => Committed?.Invoke(e);
    internal void RaiseAttacked(AttackEvent e) => Attacked?.Invoke(e);
    internal void RaiseWarned(Creature c) => Warned?.Invoke(c);

    // ---- Queries ----------------------------------------------------------------------

    public ITarget NearestTarget(Vector3 from, out float distance)
    {
        ITarget best = null;
        distance = float.MaxValue;
        foreach (ITarget t in Targets)
        {
            if (t.Health != null && t.Health.Downed) continue;
            float d = (t.Position - from).Length();
            if (d < distance)
            {
                distance = d;
                best = t;
            }
        }
        return best;
    }

    public Creature Raycast(Vector3 origin, Vector3 dir, float maxDistance, out float distance, out Vector3 point) =>
        WildSim.Raycast(Creatures, origin, dir, maxDistance, out distance, out point);
}
