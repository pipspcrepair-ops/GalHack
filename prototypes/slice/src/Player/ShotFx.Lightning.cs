using System;
using System.Collections.Generic;
using Godot;

namespace Slice.Player;

/// <summary>
/// The Sleeper tool's look: jagged bolts of lightning (midpoint-displaced polylines drawn as
/// camera-facing ribbons, a soft glow under a hot core, forks off the main bolt) with a flash that
/// lights the surroundings for a moment, short arcs crawling over what they touch, and the repair
/// mode's luminous cone. All generated; the bolts and lights are pooled.
/// </summary>
public partial class ShotFx
{
    const int SparkPool = 24, LightPool = 4;

    sealed class Spark
    {
        public MeshInstance3D Node;
        public ImmediateMesh Mesh;
        public Func<Vector3> From;
        public Vector3 To;
        public Color Colour;
        public float Age, Life, Rough, Width;
        public int Forks;
        public bool Rejagged;
        public bool Busy => Age < Life;
    }

    sealed class Glow
    {
        public OmniLight3D Light;
        public float Energy, Age, Life;
    }

    readonly List<Spark> _sparks = new();
    readonly List<Glow> _glows = new();
    readonly List<Vector3> _path = new();
    readonly Random _rng = new();
    StandardMaterial3D _sparkMaterial;

    /// <summary>For tests and pictures: bolts thrown (main and arcs), lightning flashes, and whether the repair cone shows.</summary>
    public int LightningBolts { get; private set; }
    public int LightningArcs { get; private set; }
    public int LightningFlashes { get; private set; }
    public bool ConeShowing => _cone != null && _cone.Visible;
    public int Cones { get; private set; }
    public int SparksShowing { get { int n = 0; foreach (Spark s in _sparks) if (s.Node.Visible) n++; return n; } }

    /// <summary>The attack/gather mode's light, and repair's.</summary>
    public static readonly Color LightningBlue = new(0.45f, 0.7f, 1f), RepairGreen = new(0.3f, 1f, 0.45f);

    void EnsureSparks()
    {
        if (_sparks.Count > 0) return;
        _sparkMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled, VertexColorUseAsAlbedo = true,
            NoDepthTest = false, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled,
        };
        for (int i = 0; i < SparkPool; i++)
        {
            var mesh = new ImmediateMesh();
            var node = new MeshInstance3D { Name = $"Spark{i}", Mesh = mesh, MaterialOverride = _sparkMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, TopLevel = true };
            AddChild(node);
            _sparks.Add(new Spark { Node = node, Mesh = mesh });
        }
        for (int i = 0; i < LightPool; i++)
        {
            var light = new OmniLight3D { Name = $"LightningFlash{i}", Visible = false, ShadowEnabled = false };
            AddChild(light);
            _glows.Add(new Glow { Light = light });
        }
    }

    /// <summary>
    /// A blast of lightning from the barrel tip (asked afresh as the hand moves) to a point: a jagged bolt
    /// with forks, flickering into a new shape halfway through its short life, a flash of light at the
    /// muzzle, and a light near where it strikes that lights the surroundings for <paramref name="seconds"/>.
    /// </summary>
    public void Lightning(Func<Vector3> from, Vector3 to, Color colour, float energy, float range, float seconds)
    {
        EnsureSparks();
        LightningBolts++;
        Vector3 start = from(), aim = to - start;
        Throw(from, to, colour, seconds, 0.2f, 1f, forks: 2);
        Muzzle(GunFeel.Tool.Id, start, aim.LengthSquared() < 1e-6f ? Vector3.Forward : aim.Normalized(), colour, 1f, () => from());
        LightUp(start.Lerp(to, 0.7f), colour, energy, range, seconds);
    }

    /// <summary>Short arcs crawling over what a bolt touched: between points on a box of this half-size (a block) or a body round its middle.</summary>
    public void ArcOver(Vector3 centre, Vector3 half, Color colour, int arcs, float seconds)
    {
        EnsureSparks();
        for (int i = 0; i < arcs; i++)
        {
            Vector3 a = centre + OnBox(half), b = centre + OnBox(half);
            LightningArcs++;
            Throw(() => a, b, colour, seconds * (0.7f + 0.6f * (float)_rng.NextDouble()), 0.25f, 0.6f, forks: 0);
        }
    }

    /// <summary>A light that lights the surroundings for a moment (the bolt's flash).</summary>
    public void LightUp(Vector3 at, Color colour, float energy, float range, float seconds)
    {
        EnsureSparks();
        LightningFlashes++;
        Glow g = _glows[0];
        foreach (Glow c in _glows)
            if (c.Age >= c.Life) { g = c; break; }
        g.Energy = energy;
        g.Age = 0f;
        g.Life = seconds;
        g.Light.LightColor = colour.Lerp(Colors.White, 0.35f);
        g.Light.LightEnergy = energy;
        g.Light.OmniRange = range;
        g.Light.GlobalPosition = at;
        g.Light.Visible = true;
    }

    void Throw(Func<Vector3> from, Vector3 to, Color colour, float life, float rough, float width, int forks)
    {
        Spark s = _sparks[0];
        foreach (Spark c in _sparks)
        {
            if (!c.Busy) { s = c; break; }
            if (c.Life - c.Age < s.Life - s.Age) s = c; // all busy: the one nearest its end
        }
        s.From = from;
        s.To = to;
        s.Colour = colour;
        s.Age = 0f;
        s.Life = life;
        s.Rough = rough;
        s.Width = width;
        s.Forks = forks;
        s.Rejagged = false;
        Draw(s);
        s.Node.Visible = true;
    }

    /// <summary>A point on the surface of a box (half-sizes), any face.</summary>
    Vector3 OnBox(Vector3 half)
    {
        float R() => (float)_rng.NextDouble() * 2f - 1f;
        var p = new Vector3(R() * half.X, R() * half.Y, R() * half.Z);
        switch (_rng.Next(3))
        {
            case 0: p.X = MathF.Sign(R()) * half.X; break;
            case 1: p.Y = MathF.Sign(R()) * half.Y; break;
            default: p.Z = MathF.Sign(R()) * half.Z; break;
        }
        return p;
    }

    /// <summary>The bolt's shape, new each time: a midpoint-displaced line and its forks, as ribbons facing the camera.</summary>
    void Draw(Spark s)
    {
        Vector3 a = s.From(), b = s.To;
        float len = a.DistanceTo(b);
        ImmediateMesh m = s.Mesh;
        m.ClearSurfaces();
        if (len < 0.02f) return;
        Vector3 eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? a + Vector3.Up;
        int depth = Math.Clamp((int)MathF.Ceiling(MathF.Log2(len / 0.3f)), 2, 6);
        m.SurfaceBegin(Mesh.PrimitiveType.Triangles);
        _path.Clear();
        _path.Add(a);
        Jag(a, b, len * s.Rough, depth);
        Ribbons(m, eye, s.Colour, s.Width, len);
        for (int f = 0; f < s.Forks && _path.Count > 4; f++)
        {
            int at = _rng.Next(1, _path.Count * 2 / 3);
            Vector3 root = _path[at], dir = (b - root);
            float left = dir.Length();
            if (left < 0.3f) continue;
            Vector3 end = root + (dir / left + Perp(dir / left) * 0.9f).Normalized() * left * (0.25f + 0.25f * (float)_rng.NextDouble());
            _path.Clear();
            _path.Add(root);
            Jag(root, end, root.DistanceTo(end) * s.Rough, Math.Max(2, depth - 2));
            Ribbons(m, eye, s.Colour, s.Width * 0.55f, len);
        }
        m.SurfaceEnd();
    }

    /// <summary>A random unit direction across a line.</summary>
    Vector3 Perp(Vector3 dir)
    {
        Vector3 side = MathF.Abs(dir.Y) > 0.9f ? Vector3.Right : Vector3.Up;
        Vector3 u = dir.Cross(side).Normalized(), w = dir.Cross(u);
        float spin = (float)_rng.NextDouble() * MathF.Tau;
        return u * MathF.Cos(spin) + w * MathF.Sin(spin);
    }

    void Jag(Vector3 a, Vector3 b, float disp, int depth)
    {
        if (depth == 0)
        {
            _path.Add(b);
            return;
        }
        Vector3 mid = (a + b) * 0.5f + Perp((b - a).Normalized()) * disp * (0.4f + 0.6f * (float)_rng.NextDouble());
        Jag(a, mid, disp * 0.5f, depth - 1);
        Jag(mid, b, disp * 0.5f, depth - 1);
    }

    /// <summary>The path as two ribbons: a soft glow in the light's colour and a thin hot core, each segment turned to the eye.</summary>
    void Ribbons(ImmediateMesh m, Vector3 eye, Color colour, float width, float len)
    {
        float scale = Mathf.Clamp(len / 6f, 0.6f, 1.4f);
        Ribbon(m, eye, new Color(colour.R, colour.G, colour.B, 0.5f), 0.045f * width * scale);
        Ribbon(m, eye, new Color(colour.Lerp(Colors.White, 0.45f), 1f), 0.012f * width * scale);
    }

    void Ribbon(ImmediateMesh m, Vector3 eye, Color colour, float half)
    {
        for (int i = 0; i + 1 < _path.Count; i++)
        {
            Vector3 p0 = _path[i], p1 = _path[i + 1], seg = p1 - p0;
            Vector3 side = seg.Cross(eye - p0);
            if (side.LengthSquared() < 1e-10f) continue;
            // Thinner close to the eye, so the stretch leaving the barrel doesn't fill the view.
            side = side.Normalized() * half * Mathf.Clamp(p0.DistanceTo(eye) / 1.5f, 0.3f, 1f);
            m.SurfaceSetColor(colour);
            m.SurfaceAddVertex(p0 - side);
            m.SurfaceAddVertex(p0 + side);
            m.SurfaceAddVertex(p1 + side);
            m.SurfaceAddVertex(p0 - side);
            m.SurfaceAddVertex(p1 + side);
            m.SurfaceAddVertex(p1 - side);
        }
    }

    void TickLightning(float dt)
    {
        foreach (Spark s in _sparks)
        {
            if (!s.Node.Visible) continue;
            s.Age += dt;
            if (!s.Busy)
            {
                s.Node.Visible = false;
                s.Mesh.ClearSurfaces();
                continue;
            }
            if (!s.Rejagged && s.Age > s.Life * 0.5f)
            {
                s.Rejagged = true; // the flicker: a fresh shape halfway through
                Draw(s);
            }
        }
        foreach (Glow g in _glows)
        {
            if (!g.Light.Visible) continue;
            g.Age += dt;
            if (g.Age >= g.Life) g.Light.Visible = false;
            else g.Light.LightEnergy = g.Energy * (1f - g.Age / g.Life);
        }
        if (_cone != null && _cone.Visible)
        {
            _coneAge += dt;
            if (_coneAge > 0.12f) { _cone.Visible = false; _coneLight.Visible = false; }
            else PlaceCone();
        }
    }

    // ---- The repair cone -------------------------------------------------------------------------

    MeshInstance3D _cone;
    OmniLight3D _coneLight;
    Func<Vector3> _coneFrom;
    Func<Vector3> _coneDir;
    float _coneAge, _coneRange, _coneAngle;

    const string ConeShader = @"shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color;
varying float along;
void vertex() { along = 0.5 - VERTEX.y; }
void fragment() {
    float rim = pow(1.0 - abs(dot(normalize(NORMAL), normalize(VIEW))), 1.4);
    float shimmer = 0.8 + 0.2 * sin(TIME * 17.0 - along * 26.0);
    ALBEDO = tint.rgb;
    ALPHA = clamp((0.12 + rim * 0.75) * (1.0 - along * 0.8) * shimmer * tint.a, 0.0, 1.0);
}";

    /// <summary>
    /// The repair mode's luminous green cone from the barrel tip along the look, while it is called each
    /// tick (it goes out a few frames after the last call). It lights what is in front of it, green.
    /// </summary>
    public void RepairCone(Func<Vector3> from, Func<Vector3> dir, float range, float angleDegrees)
    {
        ProcessPriority = 100; // after the camera and the hands
        if (_cone == null)
        {
            var shader = new Shader { Code = ConeShader };
            var material = new ShaderMaterial { Shader = shader };
            material.SetShaderParameter("tint", new Color(RepairGreen, 0.9f));
            _cone = new MeshInstance3D
            {
                Name = "RepairCone", Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 1f, Height = 1f, RadialSegments = 24, Rings = 6, CapTop = false, CapBottom = false },
                MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, TopLevel = true,
            };
            _coneLight = new OmniLight3D { Name = "RepairLight", LightColor = RepairGreen, LightEnergy = 1.4f, OmniRange = 5f, ShadowEnabled = false, Visible = false };
            AddChild(_cone);
            AddChild(_coneLight);
        }
        _coneFrom = from;
        _coneDir = dir;
        _coneRange = range;
        _coneAngle = angleDegrees;
        _coneAge = 0f;
        if (!_cone.Visible) Cones++;
        _cone.Visible = true;
        _coneLight.Visible = true;
        PlaceCone();
    }

    void PlaceCone()
    {
        Vector3 from = _coneFrom(), dir = _coneDir();
        if (dir.LengthSquared() < 1e-6f) return;
        dir = dir.Normalized();
        float wide = MathF.Tan(Mathf.DegToRad(_coneAngle)) * _coneRange;
        // The cylinder's top (narrow) at the tip: its +Y turned back along the look.
        Vector3 y = -dir, x = y.Cross(MathF.Abs(y.Y) > 0.9f ? Vector3.Right : Vector3.Up).Normalized(), z = x.Cross(y);
        _cone.GlobalTransform = new Transform3D(new Basis(x * wide, y * _coneRange, z * wide), from + dir * _coneRange * 0.5f);
        _coneLight.GlobalPosition = from + dir * MathF.Min(1.5f, _coneRange * 0.4f);
    }
}
