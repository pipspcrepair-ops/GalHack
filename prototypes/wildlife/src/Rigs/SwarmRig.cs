using System;
using Godot;

namespace Wildlife;

/// <summary>
/// Swarms: one creature in the simulation (one brain, one health pool), drawn as dozens of
/// members flocking round it with boids: separation, alignment, cohesion, plus a jitter that
/// makes a cloud of insects buzz and a pack of skitterers dart. Its wind-up is the cloud
/// pulling in tight; the lunge throws every member at the committed spot. Shooting the swarm
/// thins it out member by member.
/// </summary>
public sealed class SwarmRig : Rig
{
    public readonly Vector3[] Members;
    readonly Vector3[] _vel;
    readonly bool _flies;
    readonly float _radius, _size;

    public int Alive { get; private set; }

    public SwarmRig(Species s, Creature c) : base(s, Build)
    {
        int n = Math.Max(1, s.Body.Members);
        Members = new Vector3[n];
        _vel = new Vector3[n];
        _flies = s.Flies;
        _size = s.Body.Size;
        _radius = _flies ? 1.4f + n * 0.025f : 1.8f + n * 0.06f;
        Alive = n;
    }

    public float Radius => _radius;

    public override void Reset(WildSim w, Creature c)
    {
        var rng = Rng.For(Species.Seed, "swarm", c.Id);
        for (int i = 0; i < Members.Length; i++)
        {
            Members[i] = c.Position + new Vector3(rng.Range(-1f, 1f), _flies ? rng.Range(-0.5f, 0.5f) : 0f, rng.Range(-1f, 1f)) * _radius;
            _vel[i] = Vector3.Zero;
        }
        Update(w, c, 0.016f);
    }

    public override void Update(WildSim w, Creature c, float dt)
    {
        if (dt <= 0f) return;
        SmoothIntents(c, dt);
        dt = MathF.Min(dt, 0.05f); // a far swarm updated rarely still integrates in small steps
        Alive = Math.Clamp(c.SwarmAlive, 0, Members.Length);
        Vector3 center = c.Position;
        // Wind-up: the cloud draws in tight and buzzes. Lunge: everyone flies at the aim.
        float radius = _radius * (1f - 0.55f * Rearing);
        Vector3 goal = Lunging > 0.3f && c.Attacking ? c.AimPoint + Vector3.Up * (_flies ? 0.9f : 0f) : center;
        float maxSpeed = (_flies ? 7f : 6f) + 6f * Lunging;
        float sepR = MathF.Max(0.25f, _size * 2.5f);
        Vector3 avgVel = Vector3.Zero;
        for (int i = 0; i < Alive; i++) avgVel += _vel[i];
        if (Alive > 0) avgVel /= Alive;

        for (int i = 0; i < Alive; i++)
        {
            Vector3 p = Members[i];
            Vector3 sep = Vector3.Zero;
            for (int j = 0; j < Alive; j++)
            {
                if (j == i) continue;
                Vector3 d = p - Members[j];
                float d2 = d.LengthSquared();
                if (d2 < sepR * sepR && d2 > 1e-6f) sep += d / d2;
            }
            Vector3 toGoal = goal - p;
            float dist = toGoal.Length();
            Vector3 coh = toGoal * (dist > radius ? 2.2f : 0.35f);
            Vector3 align = (avgVel - _vel[i]) * 0.6f;
            float t = c.Age * (_flies ? 5f : 3f) + i * 12.9898f;
            Vector3 jitter = new Vector3(MathF.Sin(t * 1.3f), _flies ? MathF.Sin(t * 1.7f + 1f) : 0f, MathF.Cos(t * 1.1f)) * (_flies ? 6f : 4f);
            if (!_flies && MathF.Sin(t * 2.3f) > 0.6f) jitter *= 2.5f; // skittering bursts
            Vector3 follow = (c.Velocity - _vel[i]) * 0.8f;
            Vector3 acc = sep * 0.6f + coh + align + jitter + follow;
            if (c.Dead) acc = Vector3.Down * 9f - _vel[i] * 2f;
            Vector3 v = (_vel[i] + acc * dt).LimitLength(maxSpeed);
            p += v * dt;
            float ground = w.Terrain.Height(p.X, p.Z);
            if (!_flies || c.Dead) p.Y = Mathf.Lerp(p.Y, ground + _size * 0.4f, _flies ? 0.2f : 1f);
            if (p.Y < ground + _size * 0.3f) { p.Y = ground + _size * 0.3f; if (v.Y < 0f) v.Y = 0f; }
            if (_flies && !c.Dead && p.Y > center.Y + radius * 1.5f) v.Y -= 4f * dt;
            _vel[i] = v;
            Members[i] = p;
        }

        float g0 = w.Terrain.Height(center.X, center.Z);
        BodyClearance = center.Y - g0;
        SetHit(0, center, radius);
        HitCount = 1;
        SetBounds(center, radius * 2f);
    }

    /// <summary>Facing for a member, from its velocity.</summary>
    public Basis MemberBasis(int i)
    {
        Vector3 v = _vel[i];
        float yaw = v.LengthSquared() > 1e-4f ? MathF.Atan2(-v.X, -v.Z) : 0f;
        return new Basis(Vector3.Up, yaw);
    }

    static RigLayout Build(Species s)
    {
        var layout = new RigLayout();
        var k = new Paint(s);
        float sz = s.Body.Size;
        var member = new Piece("member") { Shadow = false };
        if (s.Flies)
        {
            // A wasp-like flier: head, thorax, a banded abdomen ending in a sting, two wings.
            member.Add(Shape.Sphere, new Vector3(sz * 0.32f, sz * 0.3f, sz * 0.36f), new Vector3(0, 0, -sz * 0.08f), k.Base, 0.3f, k.GlowStrength * 0.3f, null, 8, Mat.Carapace);
            member.Add(Shape.Sphere, new Vector3(sz * 0.36f, sz * 0.34f, sz * 0.62f), new Vector3(0, -sz * 0.04f, sz * 0.36f), k.Accent, 1f, k.GlowStrength * 0.5f, null, 8, Mat.Carapace);
            member.Taper(new Vector3(0, -sz * 0.06f, sz * 0.62f), new Vector3(0, -sz * 0.12f, sz * 0.82f), sz * 0.05f, sz * 0.005f, k.Dark, 0f, 0f, Mat.Horn, 5);
            member.Add(Shape.Sphere, Vector3.One * sz * 0.24f, new Vector3(0, sz * 0.02f, -sz * 0.36f), k.EyeColor, 0f, k.EyeGlow, null, 8, Mat.Eye);
            for (int side = -1; side <= 1; side += 2)
                member.Add(Shape.Sphere, new Vector3(sz * 0.7f, sz * 0.03f, sz * 0.26f), new Vector3(side * sz * 0.36f, sz * 0.16f, sz * 0.02f),
                    k.Belly.Lerp(Colors.White, 0.4f), 0f, k.GlowStrength * 0.3f, new Basis(Vector3.Up, side * 0.35f), 8, Mat.Membrane);
        }
        else
        {
            // A low, armoured skitterer with six legs and a pair of pincers.
            member.Add(Shape.Sphere, new Vector3(sz * 0.62f, sz * 0.34f, sz * 0.95f), new Vector3(0, sz * 0.2f, 0), k.Base, 1f, 0f, null, 10, Mat.Carapace);
            member.Add(Shape.Sphere, new Vector3(sz * 0.42f, sz * 0.3f, sz * 0.34f), new Vector3(0, sz * 0.2f, -sz * 0.52f), k.Accent, 0f, 0f, null, 8, Mat.Carapace);
            for (int i = 0; i < 3; i++)
            for (int side = -1; side <= 1; side += 2)
                member.Taper(new Vector3(side * sz * 0.2f, sz * 0.18f, (i - 1) * sz * 0.3f), new Vector3(side * sz * 0.62f, 0f, (i - 1) * sz * 0.42f), sz * 0.045f, sz * 0.012f, k.Dark, 0f, 0f, Mat.Carapace, 5);
            for (int side = -1; side <= 1; side += 2)
                member.Horn(k.Horn, sz * 0.05f, Mat.Horn, new Vector3(side * sz * 0.12f, sz * 0.16f, -sz * 0.66f), new Vector3(side * sz * 0.18f, sz * 0.14f, -sz * 0.86f), new Vector3(side * sz * 0.04f, sz * 0.14f, -sz * 0.98f));
            member.Add(Shape.Sphere, Vector3.One * sz * 0.1f, new Vector3(sz * 0.12f, sz * 0.32f, -sz * 0.62f), k.EyeColor, 0f, k.EyeGlow, null, 6, Mat.Eye);
            member.Add(Shape.Sphere, Vector3.One * sz * 0.1f, new Vector3(-sz * 0.12f, sz * 0.32f, -sz * 0.62f), k.EyeColor, 0f, k.EyeGlow, null, 6, Mat.Eye);
        }
        layout.Add(member);
        return layout;
    }
}
