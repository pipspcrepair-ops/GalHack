using System;
using System.Numerics;

namespace GalHack.Arms;

/// <summary>What a shot met.</summary>
public enum HitKind { Nothing, Ground, Block, Creature, Person, Player }

/// <summary>
/// One thing a sweep met: where, its surface normal, how far along, what it was (a creature, a
/// person, a structure and its cell; the ground has none), and how thick it is to pass through.
/// </summary>
public struct ShotHit
{
    public HitKind Kind;
    public Vector3 Point, Normal;
    public float Distance;
    public object Thing;
    public int CellX, CellY, CellZ;
    /// <summary>Metres a piercing shot has to go through it.</summary>
    public float Through;
}

/// <summary>
/// Where a stuck or placed shot is held: on what (the ground, a structure's cell, a creature),
/// and its point and normal in that thing's own frame, so it rides it however it moves. If the
/// thing is gone the shot falls: nothing floats.
/// </summary>
public struct Anchor
{
    public HitKind Kind;
    public object Thing;
    public int CellX, CellY, CellZ;
    public Vector3 Local, LocalNormal;
}

public enum ShotState { Flying, Stuck, Armed }

/// <summary>
/// One shot in flight, stuck or set down. A plain struct in a fixed pool, so firing and flying
/// allocate nothing.
/// </summary>
public struct Shot
{
    public bool Live;
    public int Id;
    public WeaponDef Def;
    /// <summary>Who fired it (0 the player; others later), and what it must not meet as it leaves the muzzle.</summary>
    public int Owner;
    public ShotState State;
    public Vector3 Pos, Vel;
    public float Age, Life, Damage, Speed;
    public int PierceLeft, BouncesLeft, Bounces, Jumps;
    public object LastHit;
    public object Target;
    public float Retarget;
    public Anchor Anchor;
    /// <summary>When it stuck or was set down (Age), and the next damage tick.</summary>
    public float StuckAt, NextTick, Check;
    /// <summary>Only a look: a flame lick or a spark, no harm of its own.</summary>
    public bool Visual;
    public bool Detonated;
    public Vector3 Normal;
}

/// <summary>
/// What the shots need from the world they fly in. The slice answers with its terrain, colliders,
/// creatures, townsfolk and the player; enemies later fire through the same calls.
/// </summary>
public interface IShotWorld
{
    /// <summary>Acceleration of gravity here, m/sÂ².</summary>
    Vector3 Gravity(Vector3 at);
    /// <summary>The nearest thing along a segment, never <paramref name="ignore"/>; <paramref name="age"/> lets a shot leaving the muzzle pass its shooter.</summary>
    bool Sweep(Vector3 from, Vector3 to, object ignore, int owner, float age, out ShotHit hit);
    /// <summary>A target for a seeker: what is nearest the aim line inside the cone, or the nearest living thing in range.</summary>
    bool Seek(Seek kind, Vector3 from, Vector3 dir, float range, float coneCos, int owner, out object target);
    /// <summary>Where a target is now (its middle), or false if it is gone or dead.</summary>
    bool Where(object target, out Vector3 at);
    /// <summary>Harm to what was hit: damage by class and tags, the noise and the look.</summary>
    void Strike(ref Shot s, in ShotHit hit, float damage);
    /// <summary>An explosion.</summary>
    void Blast(ref Shot s, Vector3 at, Vector3 normal, BlastPart blast);
    /// <summary>Holds a shot to what it hit.</summary>
    bool Hold(in ShotHit hit, out Anchor a);
    /// <summary>Where an anchor is now; false if what it was on is gone (<paramref name="verify"/>: check the ground is still there, a few times a second).</summary>
    bool Held(in Anchor a, bool verify, out Vector3 at, out Vector3 normal);
    /// <summary>Anything that trips a mine within the radius (never <paramref name="exclude"/>, what it is on).</summary>
    bool Near(Vector3 at, float radius, object exclude);
    /// <summary>The nearest target to <paramref name="at"/> in range not yet in <paramref name="done"/>, for an arc's next jump; its wetness stretches the reach.</summary>
    bool Nearest(Vector3 at, float range, float wetReach, object[] done, int count, out object target, out ShotHit hit);
    /// <summary>Something worth a sound or a look happened to a shot.</summary>
    void Saw(ShotNote note, ref Shot s, Vector3 at, Vector3 other);
}

public enum ShotNote { Bounced, Stuck, Placed, Expired, Released, Arc, Tick }

/// <summary>
/// The fixed pool of shots and the rules they fly by: travel, gravity, homing and swarm wander,
/// pierce, bounce, stick and damage over time, placing and tripping, blasts and arcs. Each tick
/// sweeps the distance a shot covered, so a fast round never skips through a thin thing.
/// Engine-free and allocation-free.
/// </summary>
public sealed class ShotPool
{
    public readonly Shot[] Shots;
    int _next, _ids;
    readonly object[] _arcDone = new object[16];

    public ShotPool(int capacity = 256) => Shots = new Shot[capacity];

    /// <summary>Shots alive now, and how many are placed.</summary>
    public int Live { get; private set; }
    public int Placed(WeaponDef def)
    {
        int n = 0;
        foreach (ref readonly Shot s in Shots.AsSpan())
            if (s.Live && s.State == ShotState.Armed && s.Def.Base == def.Base) n++;
        return n;
    }

    /// <summary>Starts a shot; returns its slot, or -1 if the pool is full (the oldest flying one gives way first).</summary>
    public int Spawn(WeaponDef def, int owner, Vector3 at, Vector3 vel, float damage, int stagePierce = 0)
    {
        int slot = -1;
        for (int i = 0; i < Shots.Length; i++)
        {
            int k = (_next + i) % Shots.Length;
            if (!Shots[k].Live) { slot = k; break; }
        }
        if (slot < 0)
        {
            // Full: the oldest flying look gives way, then the oldest flying shot. Placed things stay.
            float oldest = -1f;
            for (int k = 0; k < Shots.Length; k++)
                if (Shots[k].State == ShotState.Flying && Shots[k].Age > oldest) { oldest = Shots[k].Age; slot = k; }
            if (slot < 0) return -1;
        }
        _next = (slot + 1) % Shots.Length;
        ref Shot s = ref Shots[slot];
        s = default;
        s.Live = true;
        s.Id = ++_ids;
        s.Def = def;
        s.Owner = owner;
        s.Pos = at;
        s.Vel = vel;
        s.Speed = vel.Length();
        s.Damage = damage;
        s.Life = def.Bounce is { Fuse: > 0f } bounce ? bounce.Fuse : def.Life;
        s.PierceLeft = (def.Pierce?.Count ?? 0) + stagePierce;
        s.BouncesLeft = def.Bounce?.Count ?? 0;
        return slot;
    }

    /// <summary>Moves every live shot one tick.</summary>
    public void Step(float dt, IShotWorld w)
    {
        int live = 0;
        for (int i = 0; i < Shots.Length; i++)
        {
            ref Shot s = ref Shots[i];
            if (!s.Live) continue;
            Step(ref s, dt, w);
            if (s.Live) live++;
        }
        Live = live;
    }

    public void Clear()
    {
        for (int i = 0; i < Shots.Length; i++) Shots[i].Live = false;
        Live = 0;
    }

    /// <summary>Everything moves by a whole planet (the zone's frame wrapped).</summary>
    public void Shift(Vector3 by)
    {
        for (int i = 0; i < Shots.Length; i++)
        {
            ref Shot s = ref Shots[i];
            if (!s.Live) continue;
            s.Pos += by;
            if (s.State != ShotState.Flying && s.Anchor.Kind == HitKind.Ground) s.Anchor.Local += by;
        }
    }

    void Step(ref Shot s, float dt, IShotWorld w)
    {
        s.Age += dt;
        WeaponDef d = s.Def;
        switch (s.State)
        {
            case ShotState.Stuck:
                bool verify = s.Age >= s.Check;
                if (verify) s.Check = s.Age + 0.25f;
                if (!w.Held(s.Anchor, verify, out Vector3 at, out Vector3 n)) { Release(ref s, w); return; }
                s.Pos = at;
                s.Normal = n;
                // A fused round that came to rest goes off on time.
                if (d.Blast != null && d.Bounce is { Fuse: > 0f } && s.Age >= s.Life) { Detonate(ref s, w); return; }
                if (d.Dot != null && s.Age >= s.NextTick)
                {
                    s.NextTick += 1f;
                    ShotHit h = HitOf(s.Anchor, at, n);
                    if (h.Kind != HitKind.Ground) w.Strike(ref s, h, d.Dot.PerSecond);
                    w.Saw(ShotNote.Tick, ref s, at, n);
                }
                float stay = d.Stick?.Seconds ?? 0f;
                if (d.Bounce is not { Fuse: > 0f } && s.Age - s.StuckAt >= stay) Expire(ref s, w);
                return;

            case ShotState.Armed:
                bool check = s.Age >= s.Check;
                if (check) s.Check = s.Age + 0.25f;
                if (!w.Held(s.Anchor, check, out Vector3 pa, out Vector3 pn)) { Release(ref s, w); return; }
                s.Pos = pa;
                s.Normal = pn;
                if (s.Age - s.StuckAt >= d.Placed.Arm && w.Near(pa + pn * 0.1f, d.Placed.Radius, s.Anchor.Thing)) Detonate(ref s, w);
                return;
        }

        if (s.Age >= s.Life && d.Placed == null)
        {
            if (d.Blast != null && !s.Visual) Detonate(ref s, w);
            else Expire(ref s, w);
            return;
        }

        // Seeking: a turn toward the target, limited per metre flown; a swarm wanders as it goes.
        float speed = s.Vel.Length();
        if (d.Homing != null && speed > 0.01f)
        {
            s.Retarget -= dt;
            if (s.Target == null || s.Retarget <= 0f || !w.Where(s.Target, out _))
            {
                s.Retarget = 0.25f;
                float cos = MathF.Cos(d.Homing.Cone * (MathF.PI / 180f));
                if (!w.Seek(d.Homing.Seek, s.Pos, s.Vel / speed, d.Homing.Range, cos, s.Owner, out s.Target)) s.Target = null;
            }
            if (s.Target != null && w.Where(s.Target, out Vector3 aim))
            {
                Vector3 want = aim - s.Pos;
                float len = want.Length();
                if (len > 0.01f) s.Vel = Turn(s.Vel, want / len, d.Homing.TurnPerMetre * speed * dt);
            }
        }
        if (d.Swarm != null && speed > 0.01f)
        {
            // Smooth wander: each seeker its own slow figure, from its id.
            float t = s.Age * 2.3f + s.Id * 1.7f;
            var wob = new Vector3(MathF.Sin(t * 1.3f + s.Id), MathF.Sin(t * 0.9f + s.Id * 2.1f) * 0.6f, MathF.Cos(t * 1.1f + s.Id * 0.7f));
            Vector3 v = s.Vel + wob * (d.Swarm.Wander * speed * dt);
            s.Vel = Vector3.Normalize(v) * speed;
        }
        if (d.Gravity > 0f) s.Vel += w.Gravity(s.Pos) * (d.Gravity * dt);

        Vector3 from = s.Pos;
        Vector3 move = s.Vel * dt;
        for (int pass = 0; pass < 6; pass++)
        {
            float len = move.Length();
            if (len < 1e-6f) break;
            Vector3 dir = move / len;
            if (!w.Sweep(from, from + move, s.LastHit, s.Owner, s.Age, out ShotHit hit))
            {
                s.Pos = from + move;
                return;
            }
            Vector3 left = move - dir * hit.Distance;
            bool living = hit.Kind is HitKind.Creature or HitKind.Person or HitKind.Player;
            if (s.Visual)
            {
                // A flame lick stops at the first solid thing; it harms nothing itself.
                Expire(ref s, w);
                return;
            }
            if (living)
            {
                w.Strike(ref s, hit, s.Damage);
                if (d.Arc != null) ArcFrom(ref s, hit, w);
                if (d.Stick != null && w.Hold(hit, out Anchor a)) { StickTo(ref s, a, hit, w, ShotNote.Stuck); return; }
                if (d.Blast != null) { s.Pos = hit.Point; Detonate(ref s, w, hit.Normal); return; }
                if (s.PierceLeft > 0)
                {
                    s.PierceLeft--;
                    s.LastHit = hit.Thing;
                    from = hit.Point + dir * 0.05f;
                    move = left;
                    continue;
                }
                Expire(ref s, w, hit.Point);
                return;
            }

            // Ground or a block.
            if (d.Placed != null && w.Hold(hit, out Anchor pa))
            {
                StickTo(ref s, pa, hit, w, ShotNote.Placed);
                s.State = ShotState.Armed;
                return;
            }
            if (s.BouncesLeft > 0)
            {
                s.BouncesLeft--;
                s.Bounces++;
                Vector3 n = hit.Normal;
                Vector3 v = s.Vel - 2f * Vector3.Dot(s.Vel, n) * n;
                float keep = d.Bounce.Keep;
                s.Vel = v * keep;
                s.LastHit = null;
                from = hit.Point + n * 0.03f;
                move = (move - 2f * Vector3.Dot(move, n) * n) * keep * (left.Length() / MathF.Max(1e-5f, len));
                w.Saw(ShotNote.Bounced, ref s, hit.Point, n);
                if (s.Vel.LengthSquared() < 0.25f && d.Bounce.Fuse > 0f && w.Hold(hit, out Anchor rest)) { StickTo(ref s, rest, hit, w, ShotNote.Stuck); return; }
                continue;
            }
            if (d.Bounce is { Fuse: > 0f } && d.Blast != null)
            {
                // Out of bounces: it comes to rest where it is and goes off on its fuse.
                if (w.Hold(hit, out Anchor rest)) { StickTo(ref s, rest, hit, w, ShotNote.Stuck); return; }
                Detonate(ref s, w, hit.Normal);
                return;
            }
            if (hit.Kind == HitKind.Block) w.Strike(ref s, hit, s.Damage);
            if (d.Arc != null) ArcFrom(ref s, hit, w);
            if (d.Stick != null && w.Hold(hit, out Anchor sa)) { StickTo(ref s, sa, hit, w, ShotNote.Stuck); return; }
            if (d.Blast != null) { s.Pos = hit.Point; Detonate(ref s, w, hit.Normal); return; }
            if (s.PierceLeft > 0 && hit.Kind == HitKind.Block)
            {
                s.PierceLeft--;
                s.LastHit = null;
                float through = MathF.Max(0.05f, hit.Through);
                from = hit.Point + dir * through;
                move = dir * MathF.Max(0f, left.Length() - through);
                if (hit.Kind == HitKind.Ground) { Expire(ref s, w, hit.Point); return; }
                continue;
            }
            if (hit.Kind == HitKind.Ground) w.Strike(ref s, hit, s.Damage); // the impact's look
            Expire(ref s, w, hit.Point);
            return;
        }
        s.Pos = from + move;
    }

    void StickTo(ref Shot s, Anchor a, in ShotHit hit, IShotWorld w, ShotNote note)
    {
        s.State = ShotState.Stuck;
        s.Anchor = a;
        s.Pos = hit.Point;
        s.Normal = hit.Normal;
        s.Vel = Vector3.Zero;
        s.StuckAt = s.Age;
        s.NextTick = s.Age + 1f;
        w.Saw(note, ref s, hit.Point, hit.Normal);
    }

    /// <summary>What it was on is gone: it falls again, to land on whatever is under it.</summary>
    static void Release(ref Shot s, IShotWorld w)
    {
        bool placed = s.State == ShotState.Armed;
        s.State = ShotState.Flying;
        s.Vel = Vector3.Zero;
        s.LastHit = null;
        s.Life = s.Age + 20f;
        w.Saw(ShotNote.Released, ref s, s.Pos, s.Normal);
        // A falling mine or worm drops under full gravity whatever its own row says.
        if (placed || s.Def.Gravity <= 0f) s.Def = Falling(s.Def);
    }

    /// <summary>The same row, falling (cached per weapon): what a dropped mine or worm flies as.</summary>
    static WeaponDef Falling(WeaponDef d) => d.Gravity >= 1f ? d : FallCache.Of(d);

    static ShotHit HitOf(in Anchor a, Vector3 at, Vector3 n) => new()
    {
        Kind = a.Kind, Thing = a.Thing, CellX = a.CellX, CellY = a.CellY, CellZ = a.CellZ, Point = at, Normal = n,
    };

    void Detonate(ref Shot s, IShotWorld w, Vector3? normal = null)
    {
        if (s.Detonated) return;
        s.Detonated = true;
        Vector3 n = normal ?? (s.State != ShotState.Flying ? s.Normal : Vector3.UnitY);
        w.Blast(ref s, s.Pos + n * 0.1f, n, s.Def.Blast);
        s.Live = false;
    }

    static void Expire(ref Shot s, IShotWorld w, Vector3? at = null)
    {
        if (at is Vector3 p) s.Pos = p;
        w.Saw(ShotNote.Expired, ref s, s.Pos, s.Normal);
        s.Live = false;
    }

    /// <summary>The arc: from what was hit, on to the nearest unhit target in reach, so many times.</summary>
    void ArcFrom(ref Shot s, in ShotHit first, IShotWorld w)
    {
        ArcPart arc = s.Def.Arc;
        int count = 0;
        if (first.Thing != null) _arcDone[count++] = first.Thing;
        Vector3 at = first.Point;
        for (int j = 0; j < arc.Jumps && count < _arcDone.Length; j++)
        {
            if (!w.Nearest(at, arc.Range, arc.WetReach, _arcDone, count, out object next, out ShotHit h)) break;
            _arcDone[count++] = next;
            s.Jumps++;
            w.Saw(ShotNote.Arc, ref s, at, h.Point);
            w.Strike(ref s, h, arc.Damage);
            at = h.Point;
        }
        for (int i = 0; i < count; i++) _arcDone[i] = null;
    }

    /// <summary>Turns a velocity toward a direction by at most an angle (radians), keeping its speed.</summary>
    public static Vector3 Turn(Vector3 vel, Vector3 want, float maxAngle)
    {
        float speed = vel.Length();
        if (speed < 1e-5f) return vel;
        Vector3 v = vel / speed;
        float dot = Math.Clamp(Vector3.Dot(v, want), -1f, 1f);
        float angle = MathF.Acos(dot);
        if (angle < 1e-5f) return vel;
        float t = MathF.Min(1f, maxAngle / angle);
        // Slerp between the two unit vectors.
        float sin = MathF.Sin(angle);
        Vector3 r = sin < 1e-4f ? Vector3.Normalize(Vector3.Lerp(v, want, t)) : (MathF.Sin((1f - t) * angle) * v + MathF.Sin(t * angle) * want) / sin;
        return r * speed;
    }

    /// <summary>Does a ball (a body, by its radius) reach into a cone from an origin (unit direction, cosine of the half-angle, range)?</summary>
    public static bool InWedge(Vector3 origin, Vector3 dir, float cos, float range, Vector3 centre, float radius)
    {
        Vector3 to = centre - origin;
        float along = Vector3.Dot(to, dir);
        if (along < -radius || along > range + radius) return false;
        float perp = (to - dir * along).Length();
        float tan = MathF.Sqrt(MathF.Max(0f, 1f - cos * cos)) / MathF.Max(1e-4f, cos);
        return perp - radius <= MathF.Max(0f, along) * tan;
    }

    /// <summary>Is a point inside a cone from an origin (unit direction, cosine of the half-angle, range)?</summary>
    public static bool InCone(Vector3 origin, Vector3 dir, float cos, float range, Vector3 point)
    {
        Vector3 to = point - origin;
        float d = to.Length();
        if (d > range || d < 1e-4f) return d < 1e-4f;
        return Vector3.Dot(to / d, dir) >= cos;
    }
}

/// <summary>Falling copies of rows, made once per weapon (a dropped mine falls under full gravity).</summary>
static class FallCache
{
    static readonly System.Collections.Generic.Dictionary<WeaponDef, WeaponDef> Made = new();

    public static WeaponDef Of(WeaponDef d)
    {
        if (Made.TryGetValue(d, out WeaponDef f)) return f;
        f = new WeaponDef
        {
            Id = d.Id, Code = d.Code, Name = d.Name, Item = d.Item, Mode = d.Mode, Trigger = d.Trigger, Delivery = d.Delivery, Class = d.Class, Tags = d.Tags,
            Damage = d.Damage, Speed = d.Speed, Life = d.Life, Blocks = d.Blocks, Gravity = 1f, Ammo = d.Ammo, Stick = d.Stick, Dot = d.Dot, Placed = d.Placed,
            Blast = d.Blast, Feel = d.Feel, Tracer = d.Tracer, Look = d.Look,
        };
        f.Base = d.Base;
        f.Modes = d.Modes;
        Made[d] = f;
        return f;
    }
}
