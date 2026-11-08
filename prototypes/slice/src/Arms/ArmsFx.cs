using System;
using System.Collections.Generic;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using Body = GalHack.Arms.Body;
using N = System.Numerics;

namespace Slice.Arms;

/// <summary>
/// What shots look like in flight: one pooled mesh per shot slot, set each frame from the shot
/// itself (a streak for a round, a ball for a grenade, a finned rocket with a smoke trail, a puck
/// with a blinking eye, a segmented worm, a hornet, a soft lick of flame), a few pooled lights for
/// the bright ones, jagged arcs for lightning and a fireball for a blast. Every node is made up
/// front, so flight allocates nothing. Procedural shapes only.
/// </summary>
public partial class ArmsFx : Node3D
{
    public ShotPool Pool;

    MeshInstance3D[] _slots;
    MeshInstance3D[] _eyes;
    readonly Dictionary<WeaponDef, Material> _tracers = new();
    Mesh _streak, _ball, _rocket, _puck, _worm, _hornet, _flameQuad;
    Material _ballMat, _rocketMat, _puckMat, _wormMat, _hornetMat;
    ShaderMaterial _flameMat, _eyeMat;
    readonly OmniLight3D[] _lights = new OmniLight3D[6];
    readonly OmniLight3D[] _eyeLights = new OmniLight3D[4];
    readonly int[] _eyeOf = { -1, -1, -1, -1 };
    readonly CpuParticles3D[] _trails = new CpuParticles3D[4];
    readonly int[] _trailOf = { -1, -1, -1, -1 };
    readonly int[] _lightOf = { -1, -1, -1, -1, -1, -1 };

    static readonly StringName Life01 = "life01", Blink = "blink";

    /// <summary>For tests and the frame budget: slots drawn last frame, arcs and blasts shown.</summary>
    public int Drawn { get; private set; }
    public int Arcs { get; private set; }
    public int Fireballs { get; private set; }

    public void Setup(ShotPool pool, IEnumerable<WeaponDef> weapons)
    {
        Pool = pool;
        PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        _streak = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.5f, Height = 1f, RadialSegments = 6, Rings = 1 };
        _ball = new SphereMesh { Radius = 0.045f, Height = 0.09f, RadialSegments = 10, Rings = 5 };
        _rocket = Merge(
            (new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.36f, RadialSegments = 10 }, Transform3D.Identity.Rotated(Vector3.Right, Mathf.Pi / 2f)),
            (new CylinderMesh { TopRadius = 0f, BottomRadius = 0.035f, Height = 0.1f, RadialSegments = 10 }, new Transform3D(Basis.FromEuler(new Vector3(-Mathf.Pi / 2f, 0, 0)), new Vector3(0, 0, -0.23f))),
            (new BoxMesh { Size = new Vector3(0.11f, 0.006f, 0.07f) }, new Transform3D(Basis.Identity, new Vector3(0, 0, 0.15f))),
            (new BoxMesh { Size = new Vector3(0.006f, 0.11f, 0.07f) }, new Transform3D(Basis.Identity, new Vector3(0, 0, 0.15f))));
        _puck = Merge(
            (new CylinderMesh { TopRadius = 0.055f, BottomRadius = 0.062f, Height = 0.03f, RadialSegments = 14 }, Transform3D.Identity.Translated(new Vector3(0, 0.015f, 0))),
            (new CylinderMesh { TopRadius = 0.028f, BottomRadius = 0.032f, Height = 0.012f, RadialSegments = 12 }, Transform3D.Identity.Translated(new Vector3(0, 0.035f, 0))));
        _worm = Merge(
            (new SphereMesh { Radius = 0.03f, Height = 0.05f, RadialSegments = 8, Rings = 4 }, Transform3D.Identity.Translated(new Vector3(0, 0, -0.06f))),
            (new SphereMesh { Radius = 0.034f, Height = 0.06f, RadialSegments = 8, Rings = 4 }, Transform3D.Identity.Translated(new Vector3(0, 0, -0.015f))),
            (new SphereMesh { Radius = 0.032f, Height = 0.058f, RadialSegments = 8, Rings = 4 }, Transform3D.Identity.Translated(new Vector3(0, 0, 0.03f))),
            (new SphereMesh { Radius = 0.026f, Height = 0.05f, RadialSegments = 8, Rings = 4 }, Transform3D.Identity.Translated(new Vector3(0, 0, 0.07f))));
        _hornet = Merge(
            (new SphereMesh { Radius = 0.032f, Height = 0.09f, RadialSegments = 8, Rings = 4 }, new Transform3D(Basis.FromEuler(new Vector3(Mathf.Pi / 2f, 0, 0)), new Vector3(0, 0, 0.034f))),
            (new SphereMesh { Radius = 0.022f, Height = 0.044f, RadialSegments = 8, Rings = 4 }, Transform3D.Identity.Translated(new Vector3(0, 0, -0.03f))),
            (new BoxMesh { Size = new Vector3(0.15f, 0.004f, 0.04f) }, Transform3D.Identity.Translated(new Vector3(0, 0.022f, -0.01f))));
        _flameQuad = new QuadMesh { Size = Vector2.One };
        _ballMat = new StandardMaterial3D { AlbedoColor = new Color("3d4a3a"), Roughness = 0.6f, Metallic = 0.4f, EmissionEnabled = true, Emission = new Color(1f, 0.5f, 0.2f), EmissionEnergyMultiplier = 0.4f };
        _rocketMat = new StandardMaterial3D { AlbedoColor = new Color("5a6248"), Roughness = 0.55f, Metallic = 0.3f };
        _puckMat = new StandardMaterial3D { AlbedoColor = new Color("4a4f55"), Roughness = 0.6f, Metallic = 0.35f };
        _wormMat = new StandardMaterial3D { AlbedoColor = new Color("b06a58"), Roughness = 0.45f, Metallic = 0f, RimEnabled = true, Rim = 0.4f };
        _hornetMat = new StandardMaterial3D { AlbedoColor = new Color("e0a422"), Roughness = 0.5f, EmissionEnabled = true, Emission = new Color(1f, 0.62f, 0.1f), EmissionEnergyMultiplier = 1.4f };
        _flameMat = new ShaderMaterial { Shader = new Shader { Code = FlameCode } };
        _eyeMat = new ShaderMaterial { Shader = new Shader { Code = EyeCode } };
        foreach (WeaponDef w in weapons)
            foreach (WeaponDef m in w.Modes)
            {
                Color c = GunModel.ColourOf(m.Tracer.Colour);
                _tracers[m] = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = c, EmissionEnabled = true, Emission = c, EmissionEnergyMultiplier = 4f,
                };
            }

        _slots = new MeshInstance3D[pool.Shots.Length];
        _eyes = new MeshInstance3D[pool.Shots.Length];
        for (int i = 0; i < _slots.Length; i++)
        {
            var m = new MeshInstance3D { Name = $"Shot{i}", Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
            AddChild(m);
            _slots[i] = m;
        }
        for (int i = 0; i < _lights.Length; i++)
        {
            _lights[i] = new OmniLight3D { Visible = false, OmniRange = 5f, LightEnergy = 1.4f, ShadowEnabled = false };
            AddChild(_lights[i]);
        }
        for (int i = 0; i < _eyeLights.Length; i++)
        {
            _eyeLights[i] = new OmniLight3D { Visible = false, OmniRange = 1.4f, LightColor = new Color(1f, 0.15f, 0.08f), ShadowEnabled = false };
            AddChild(_eyeLights[i]);
        }
        var grow = new Curve();
        grow.AddPoint(new Vector2(0f, 0.5f));
        grow.AddPoint(new Vector2(1f, 2.4f));
        for (int i = 0; i < _trails.Length; i++)
        {
            _trails[i] = new CpuParticles3D
            {
                Emitting = false, Amount = 60, Lifetime = 1.4f, LocalCoords = false,
                Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f, RadialSegments = 6, Rings = 3 },
                Direction = Vector3.Back, Spread = 12f, InitialVelocityMin = 0.2f, InitialVelocityMax = 0.6f, Gravity = new Vector3(0, 0.3f, 0),
                ScaleAmountCurve = grow, ColorRamp = new Gradient { Colors = new[] { new Color(0.85f, 0.82f, 0.78f, 0.5f), new Color(0.55f, 0.55f, 0.55f, 0f) } },
                MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, VertexColorUseAsAlbedo = true },
            };
            AddChild(_trails[i]);
        }
        for (int i = 0; i < 6; i++) _arcs.Add(MakeArc());
        for (int i = 0; i < 4; i++) _balls.Add(MakeFireball());
    }

    public override void _Process(double delta)
    {
        if (Pool == null) return;
        float dt = (float)delta;
        int drawn = 0;
        Shot[] shots = Pool.Shots;
        for (int i = 0; i < shots.Length; i++)
        {
            ref Shot s = ref shots[i];
            MeshInstance3D m = _slots[i];
            if (!s.Live)
            {
                if (m.Visible) m.Visible = false;
                if (_eyes[i] != null && _eyes[i].Visible) _eyes[i].Visible = false;
                continue;
            }
            drawn++;
            Place(i, ref s, m);
        }
        Drawn = drawn;
        TickLights(shots);
        for (int k = 0; k < _eyeLights.Length; k++)
            if (_eyeOf[k] >= 0 && (!shots[_eyeOf[k]].Live || shots[_eyeOf[k]].State != ShotState.Armed)) { _eyeOf[k] = -1; _eyeLights[k].Visible = false; }
        TickArcs(dt);
        TickFireballs(dt);
    }

    void Place(int i, ref Shot s, MeshInstance3D m)
    {
        TracerSpec t = s.Def.Tracer;
        Vector3 pos = s.Pos.ToGodot(), vel = s.Vel.ToGodot();
        float speed = vel.Length();
        Vector3 dir = speed > 0.01f ? vel / speed : (s.State != ShotState.Flying ? -s.Normal.ToGodot() : Vector3.Forward);
        if (dir.LengthSquared() < 0.5f) dir = Vector3.Down;
        Basis look = Basis.LookingAt(dir, MathF.Abs(dir.Y) > 0.98f ? Vector3.Right : Vector3.Up);
        Mesh mesh;
        Material mat;
        Basis basis = look;
        switch (t.Body)
        {
            case Body.Ball: mesh = _ball; mat = _ballMat; basis = look.Rotated(look.X, s.Age * 14f); break;
            case Body.Rocket: mesh = _rocket; mat = _rocketMat; break;
            case Body.Puck:
                mesh = _puck;
                mat = _puckMat;
                Vector3 up = s.State != ShotState.Flying ? s.Normal.ToGodot() : Vector3.Up;
                if (up.LengthSquared() < 0.5f) up = Vector3.Up;
                Vector3 side = MathF.Abs(up.Dot(Vector3.Forward)) > 0.9f ? Vector3.Right : Vector3.Forward;
                Vector3 x = side.Cross(up).Normalized();
                basis = s.State == ShotState.Flying ? new Basis(Vector3.Right, s.Age * 9f) : new Basis(x, up, x.Cross(up));
                Eye(i, ref s, pos, basis);
                break;
            case Body.Worm:
                mesh = _worm;
                mat = _wormMat;
                if (s.State == ShotState.Stuck)
                {
                    // Half into what it's eating, tail out and squirming.
                    Vector3 into = -s.Normal.ToGodot();
                    if (into.LengthSquared() < 0.5f) into = Vector3.Down;
                    basis = Basis.LookingAt(into, MathF.Abs(into.Y) > 0.98f ? Vector3.Right : Vector3.Up).Rotated(into, MathF.Sin(s.Age * 11f) * 0.5f);
                    pos -= into * -0.02f;
                    float bite = Mathf.Clamp((s.Age - s.StuckAt) / Mathf.Max(0.1f, s.Def.Stick?.Seconds ?? 1f), 0f, 1f);
                    pos += into * bite * 0.06f;
                }
                else basis = look.Rotated(look.Y, MathF.Sin(s.Age * 18f) * 0.35f);
                break;
            case Body.Hornet:
                mesh = _hornet;
                mat = _hornetMat;
                basis = look.Rotated(look.Z, MathF.Sin(s.Age * 60f + s.Id) * 0.3f);
                break;
            case Body.Flame:
                mesh = _flameQuad;
                mat = _flameMat;
                float life = Mathf.Clamp(s.Age / Mathf.Max(0.05f, s.Life), 0f, 1f);
                m.SetInstanceShaderParameter(Life01, life);
                float size = t.Width + life * 1.2f;
                basis = Basis.FromScale(Vector3.One * size);
                break;
            default:
                // A streak: its head at the shot, its tail back along its path, never longer than it has flown.
                mesh = _streak;
                mat = _tracers.TryGetValue(s.Def, out Material tm) ? tm : _rocketMat;
                float len = MathF.Min(t.Length, speed * s.Age + 0.05f);
                if (speed < 0.01f) len = 0.05f;
                float width = t.Body == Body.Slug ? t.Width * 1.4f : t.Width;
                basis = look * new Basis(Vector3.Right, Mathf.Pi / 2f) * Basis.FromScale(new Vector3(width, len, width));
                pos -= dir * len * 0.5f;
                break;
        }
        if (m.Mesh != mesh) m.Mesh = mesh;
        if (m.MaterialOverride != mat) m.MaterialOverride = mat;
        m.GlobalTransform = new Transform3D(basis, pos);
        if (!m.Visible) m.Visible = true;
    }

    /// <summary>A placed puck's red eye: slow when it's waiting to arm, quick once armed.</summary>
    void Eye(int i, ref Shot s, Vector3 pos, Basis basis)
    {
        MeshInstance3D e = _eyes[i];
        if (e == null)
        {
            e = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.012f, Height = 0.016f, RadialSegments = 8, Rings = 4 }, MaterialOverride = _eyeMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            AddChild(e);
            _eyes[i] = e;
        }
        bool armed = s.State == ShotState.Armed && s.Age - s.StuckAt >= (s.Def.Placed?.Arm ?? 0f);
        float rate = armed ? 6f : 1.5f;
        bool lit = MathF.Sin(s.Age * rate * MathF.Tau) > 0.2f;
        e.SetInstanceShaderParameter(Blink, lit ? 1f : 0.15f);
        e.GlobalTransform = new Transform3D(basis, pos + basis.Y * 0.042f);
        if (!e.Visible) e.Visible = true;
        // A set mine's eye throws a little red light round it, so it shows in grass and in the dark.
        if (s.State == ShotState.Armed)
        {
            int l = -1;
            for (int k = 0; k < _eyeLights.Length; k++)
                if (_eyeOf[k] == i) { l = k; break; }
            if (l < 0)
                for (int k = 0; k < _eyeLights.Length; k++)
                    if (_eyeOf[k] < 0 || !Pool.Shots[_eyeOf[k]].Live || Pool.Shots[_eyeOf[k]].State != ShotState.Armed) { l = k; _eyeOf[k] = i; break; }
            if (l >= 0)
            {
                _eyeLights[l].GlobalPosition = pos + basis.Y * 0.12f;
                _eyeLights[l].LightEnergy = lit ? 1.6f : 0.2f;
                _eyeLights[l].Visible = true;
            }
        }
    }

    void TickLights(Shot[] shots)
    {
        // Lights and smoke go to the bright shots in flight, slot by slot.
        for (int l = 0; l < _lights.Length; l++)
        {
            int k = _lightOf[l];
            if (k >= 0 && (!shots[k].Live || !shots[k].Def.Tracer.Light || shots[k].State != ShotState.Flying)) { _lightOf[l] = -1; _lights[l].Visible = false; }
        }
        for (int t = 0; t < _trails.Length; t++)
        {
            int k = _trailOf[t];
            if (k >= 0 && (!shots[k].Live || shots[k].Def.Tracer.Body != Body.Rocket || shots[k].State != ShotState.Flying)) { _trailOf[t] = -1; _trails[t].Emitting = false; }
        }
        for (int i = 0; i < shots.Length; i++)
        {
            ref Shot s = ref shots[i];
            if (!s.Live || s.State != ShotState.Flying) continue;
            if (s.Def.Tracer.Light && Assign(_lightOf, i) is int l and >= 0)
            {
                _lights[l].Visible = true;
                _lights[l].LightColor = GunModel.ColourOf(s.Def.Tracer.Colour);
            }
            if (s.Def.Tracer.Body == Body.Rocket && Assign(_trailOf, i) is int t and >= 0)
            {
                _trails[t].GlobalPosition = s.Pos.ToGodot();
                _trails[t].Restart();
                _trails[t].Emitting = true;
            }
        }
        for (int l = 0; l < _lights.Length; l++)
            if (_lightOf[l] >= 0) _lights[l].GlobalPosition = shots[_lightOf[l]].Pos.ToGodot();
        for (int t = 0; t < _trails.Length; t++)
            if (_trailOf[t] >= 0)
            {
                ref Shot s = ref shots[_trailOf[t]];
                Vector3 v = s.Vel.ToGodot();
                Vector3 back = v.LengthSquared() > 0.01f ? -v.Normalized() : Vector3.Back;
                _trails[t].GlobalPosition = s.Pos.ToGodot() + back * 0.2f;
            }
    }

    /// <summary>Gives a shot a free holder (a light, a smoke trail): its index when newly taken, else -1 (already held, or none free).</summary>
    static int Assign(int[] holders, int slot)
    {
        int free = -1;
        for (int h = 0; h < holders.Length; h++)
        {
            if (holders[h] == slot) return -1;
            if (holders[h] < 0 && free < 0) free = h;
        }
        if (free < 0) return -1;
        holders[free] = slot;
        return free;
    }

    // ---- Arcs ---------------------------------------------------------------------------------

    sealed class ArcFx
    {
        public MeshInstance3D Mesh;
        public ImmediateMesh Lines;
        public OmniLight3D Light;
        public float Left;
    }

    readonly List<ArcFx> _arcs = new();
    readonly Random _rng = new(5);

    ArcFx MakeArc()
    {
        var a = new ArcFx { Lines = new ImmediateMesh() };
        a.Mesh = new MeshInstance3D
        {
            Mesh = a.Lines, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.78f, 0.88f, 1f), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
                EmissionEnabled = true, Emission = new Color(0.7f, 0.85f, 1f), EmissionEnergyMultiplier = 6f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            },
        };
        a.Light = new OmniLight3D { Visible = false, LightColor = new Color(0.7f, 0.82f, 1f), LightEnergy = 3f, OmniRange = 6f };
        AddChild(a.Mesh);
        AddChild(a.Light);
        return a;
    }

    /// <summary>A forking bolt of lightning between two points, gone in a blink.</summary>
    public void Arc(Vector3 from, Vector3 to, Vector3 eye)
    {
        Arcs++;
        ArcFx a = _arcs[0];
        foreach (ArcFx c in _arcs) if (c.Left < a.Left) a = c;
        a.Left = 0.16f;
        a.Lines.ClearSurfaces();
        a.Lines.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        Bolt(a.Lines, from, to, eye, 0.06f, 10);
        // A couple of short side forks off the main line.
        for (int f = 0; f < 2; f++)
        {
            float t = 0.3f + 0.4f * (float)_rng.NextDouble();
            Vector3 p = from.Lerp(to, t);
            Vector3 off = new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.3f, (float)_rng.NextDouble() - 0.5f) * from.DistanceTo(to) * 0.35f;
            Bolt(a.Lines, p, p + off, eye, 0.03f, 4);
        }
        a.Lines.SurfaceEnd();
        a.Mesh.Visible = true;
        a.Light.GlobalPosition = from.Lerp(to, 0.5f);
        a.Light.Visible = true;
    }

    void Bolt(ImmediateMesh m, Vector3 from, Vector3 to, Vector3 eye, float width, int steps)
    {
        Vector3 line = to - from;
        float len = line.Length();
        if (len < 0.01f) return;
        Vector3 prev = from;
        for (int i = 1; i <= steps; i++)
        {
            float t = i / (float)steps;
            Vector3 next = from + line * t;
            if (i < steps)
                next += new Vector3((float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f, (float)_rng.NextDouble() - 0.5f) * len * 0.12f;
            Vector3 seg = next - prev;
            Vector3 side = seg.Cross(eye - prev);
            side = side.LengthSquared() > 1e-6f ? side.Normalized() * width : Vector3.Up * width;
            m.SurfaceAddVertex(prev - side);
            m.SurfaceAddVertex(prev + side);
            m.SurfaceAddVertex(next + side);
            m.SurfaceAddVertex(prev - side);
            m.SurfaceAddVertex(next + side);
            m.SurfaceAddVertex(next - side);
            prev = next;
        }
    }

    void TickArcs(float dt)
    {
        foreach (ArcFx a in _arcs)
        {
            if (a.Left <= 0f) continue;
            a.Left -= dt;
            a.Light.LightEnergy = 3f * Mathf.Clamp(a.Left / 0.16f, 0f, 1f);
            if (a.Left <= 0f)
            {
                a.Mesh.Visible = false;
                a.Light.Visible = false;
            }
        }
    }

    // ---- Blasts -------------------------------------------------------------------------------

    sealed class Fireball
    {
        public MeshInstance3D Mesh;
        public OmniLight3D Light;
        public StandardMaterial3D Material;
        public float Age = 9f, Radius;
    }

    readonly List<Fireball> _balls = new();

    Fireball MakeFireball()
    {
        var f = new Fireball
        {
            Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(1f, 0.6f, 0.2f, 0.9f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add, EmissionEnabled = true, Emission = new Color(1f, 0.55f, 0.2f), EmissionEnergyMultiplier = 3f,
            },
        };
        f.Mesh = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 1f, RadialSegments = 16, Rings = 8 }, MaterialOverride = f.Material, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        f.Light = new OmniLight3D { Visible = false, LightColor = new Color(1f, 0.65f, 0.3f), OmniRange = 14f, ShadowEnabled = false };
        AddChild(f.Mesh);
        AddChild(f.Light);
        return f;
    }

    /// <summary>A blast's fireball and its light, growing and fading over half a second.</summary>
    public void Blast(Vector3 at, float radius)
    {
        Fireballs++;
        Fireball f = _balls[0];
        foreach (Fireball b in _balls) if (b.Age > f.Age) f = b;
        f.Age = 0f;
        f.Radius = radius;
        f.Mesh.GlobalPosition = at;
        f.Light.GlobalPosition = at;
        f.Mesh.Visible = true;
        f.Light.Visible = true;
    }

    void TickFireballs(float dt)
    {
        foreach (Fireball f in _balls)
        {
            if (f.Age > 0.6f) continue;
            f.Age += dt;
            float t = Mathf.Clamp(f.Age / 0.55f, 0f, 1f);
            f.Mesh.Scale = Vector3.One * f.Radius * (0.4f + 1.2f * MathF.Sqrt(t));
            f.Material.AlbedoColor = new Color(1f, 0.6f - 0.3f * t, 0.2f, 0.9f * (1f - t));
            f.Light.LightEnergy = 8f * (1f - t);
            if (f.Age > 0.6f)
            {
                f.Mesh.Visible = false;
                f.Light.Visible = false;
            }
        }
    }

    // ---- Meshes and shaders -------------------------------------------------------------------

    static ArrayMesh Merge(params (Mesh mesh, Transform3D at)[] parts)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (var (mesh, at) in parts) st.AppendFrom(mesh, 0, at);
        st.GenerateNormals();
        return st.Commit();
    }

    const string FlameCode = @"
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_disabled;
instance uniform float life01 = 0.0;
void vertex() {
    // A billboard: always faces the eye.
    MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0], INV_VIEW_MATRIX[1], INV_VIEW_MATRIX[2], MODEL_MATRIX[3]);
    MODELVIEW_MATRIX = MODELVIEW_MATRIX * mat4(vec4(length(MODEL_MATRIX[0].xyz), 0, 0, 0), vec4(0, length(MODEL_MATRIX[1].xyz), 0, 0), vec4(0, 0, 1, 0), vec4(0, 0, 0, 1));
}
void fragment() {
    vec2 q = UV * 2.0 - 1.0;
    float r = length(q);
    float soft = pow(clamp(1.0 - r, 0.0, 1.0), 1.6);
    float t = life01;
    vec3 hot = mix(vec3(1.0, 0.85, 0.45), vec3(1.0, 0.35, 0.08), smoothstep(0.0, 0.6, t));
    vec3 col = mix(hot, vec3(0.25, 0.2, 0.18), smoothstep(0.55, 1.0, t));
    ALBEDO = col * soft * (1.0 - t * 0.7) * 3.0;
    ALPHA = soft;
}
";

    const string EyeCode = @"
shader_type spatial;
render_mode unshaded;
instance uniform float blink = 1.0;
void fragment() {
    ALBEDO = vec3(1.0, 0.12, 0.05) * (0.25 + 2.5 * blink);
}
";
}
