using System;
using System.Collections.Generic;
using Godot;
using BoltDraw = PlanetSurface.Fx.Bolts;

namespace Slice.Player;

/// <summary>
/// The Sleeper tool's look: jagged bolts of lightning (midpoint-displaced paths with forks, drawn
/// as every bolt is, by <see cref="PlanetSurface.Fx.Bolts"/>) with a flash that
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
        public ShaderMaterial Material;
        public Color Colour;
        public float Age, Rise, Hold, Fade, Rough, Width;
        public int Forks;
        public bool Rejagged;
        public float Life => Rise + Hold + Fade;
        public bool Busy => Age < Life;
    }

    sealed class Glow
    {
        public OmniLight3D Light;
        public float Energy, Age, Rise, Fade;
    }

    /// <summary>How bright something that rises, holds and fades is at an age, 0 to 1: up in <paramref name="rise"/>, full for <paramref name="hold"/>, down smoothly over <paramref name="fade"/>.</summary>
    public static float Envelope(float age, float rise, float hold, float fade)
    {
        if (age < rise) return age / rise;
        float t = (age - rise - hold) / MathF.Max(fade, 1e-4f);
        return t <= 0f ? 1f : t >= 1f ? 0f : (1f - t) * (1f - t) * (1f + 2f * t); // smoothstep down: no edge to the fade
    }

    /// <summary>A bolt's base brightness (its material's energy at full).</summary>
    const float SparkEnergy = 3f;

    readonly List<Spark> _sparks = new();
    readonly List<Glow> _glows = new();
    readonly Random _rng = new();

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
        for (int i = 0; i < SparkPool; i++)
        {
            // Each its own material, so each fades on its own; a strong glow, so the light reads blue round a white-hot core.
            ShaderMaterial m = BoltDraw.Material(Colors.White, SparkEnergy, 0.9f);
            var mesh = new ImmediateMesh();
            var node = new MeshInstance3D { Name = $"Spark{i}", Mesh = mesh, MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Visible = false, TopLevel = true };
            AddChild(node);
            _sparks.Add(new Spark { Node = node, Mesh = mesh, Material = m });
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
    /// with forks that comes up, holds (flickering into a new shape once) and fades smoothly from the tip
    /// (no muzzle flash that would pop on), and a light near where it strikes that comes up and fades with it (timing from the data).
    /// </summary>
    public void Lightning(Func<Vector3> from, Vector3 to, Color colour, Slice.Game.SliceRules.LightningRules look)
    {
        EnsureSparks();
        LightningBolts++;
        Vector3 start = from();
        Throw(from, to, colour, look, 1f, 0.2f, 1f, forks: 2);
        LightUp(start.Lerp(to, 0.7f), colour, look);
    }

    /// <summary>Short arcs crawling over what a bolt touched: between points on a box of this half-size (a block) or a body round its middle.</summary>
    public void ArcOver(Vector3 centre, Vector3 half, Color colour, int arcs, Slice.Game.SliceRules.LightningRules look)
    {
        EnsureSparks();
        for (int i = 0; i < arcs; i++)
        {
            Vector3 a = centre + OnBox(half), b = centre + OnBox(half);
            LightningArcs++;
            Throw(() => a, b, colour, look, 0.8f + 0.5f * (float)_rng.NextDouble(), 0.25f, 0.6f, forks: 0);
        }
    }

    /// <summary>A light that lights the surroundings for a moment (the bolt's flash): it comes up quickly and fades.</summary>
    public void LightUp(Vector3 at, Color colour, Slice.Game.SliceRules.LightningRules look)
    {
        EnsureSparks();
        LightningFlashes++;
        Glow g = _glows[0];
        foreach (Glow c in _glows)
            if (c.Age >= c.Rise + c.Fade) { g = c; break; }
        float energy = look.FlashEnergy, range = look.FlashRange;
        g.Energy = energy;
        g.Age = 0f;
        g.Rise = look.FlashRise;
        g.Fade = look.FlashFade;
        g.Light.LightColor = colour.Lerp(Colors.White, 0.35f);
        g.Light.LightEnergy = 0f;
        g.Light.OmniRange = range;
        g.Light.GlobalPosition = at;
        g.Light.Visible = true;
    }

    void Throw(Func<Vector3> from, Vector3 to, Color colour, Slice.Game.SliceRules.LightningRules look, float stretch, float rough, float width, int forks)
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
        s.Rise = look.BoltRise;
        s.Hold = look.BoltHold * stretch;
        s.Fade = look.BoltFade * stretch;
        s.Material.SetShaderParameter(PlanetSurface.Fx.Names.Of("energy"), 0f);
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

    /// <summary>The bolt's shape, new each time: a midpoint-displaced line and its forks, drawn as every bolt is (<see cref="PlanetSurface.Fx.Bolts"/>).</summary>
    void Draw(Spark s)
    {
        Vector3 a = s.From(), b = s.To;
        float len = a.DistanceTo(b);
        _channels.Clear();
        if (len < 0.02f) { s.Mesh.ClearSurfaces(); return; }
        Vector3 eye = GetViewport()?.GetCamera3D()?.GlobalPosition ?? a + Vector3.Up;
        int depth = Math.Clamp((int)MathF.Ceiling(MathF.Log2(len / 0.3f)), 2, 6);
        float width = BoltWidth * s.Width * Mathf.Clamp(len / 6f, 0.6f, 1.4f);
        var main = new List<Vector3>();
        BoltDraw.Jag(main, a, b, s.Rough, depth, Random01);
        _channels.Add(new BoltDraw.Channel(main, width, 1f));
        for (int f = 0; f < s.Forks && main.Count > 4; f++)
        {
            Vector3 root = main[_rng.Next(1, main.Count * 2 / 3)], dir = b - root;
            float left = dir.Length();
            if (left < 0.3f) continue;
            Vector3 side = dir.Cross(MathF.Abs(dir.Y / left) > 0.9f ? Vector3.Right : Vector3.Up).Normalized().Rotated(dir / left, Random01() * MathF.Tau);
            Vector3 end = root + (dir / left + side * 0.9f).Normalized() * left * (0.25f + 0.25f * Random01());
            var fork = new List<Vector3>();
            BoltDraw.Jag(fork, root, end, s.Rough, Math.Max(2, depth - 2), Random01);
            _channels.Add(new BoltDraw.Channel(fork, width * 0.55f, 0.6f));
        }
        BoltDraw.Draw(s.Mesh, _channels, eye, 1f, 1f, s.Colour);
    }

    /// <summary>A bolt's whole width (its glow's, the core a sixth of it), m, at a 6 m reach.</summary>
    const float BoltWidth = 0.11f;

    readonly List<BoltDraw.Channel> _channels = new();
    float Random01() => (float)_rng.NextDouble();

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
            if (!s.Rejagged && s.Age > s.Rise + s.Hold * 0.5f)
            {
                s.Rejagged = true; // the flicker: a fresh shape halfway through its hold
                Draw(s);
            }
            s.Material.SetShaderParameter(PlanetSurface.Fx.Names.Of("energy"), SparkEnergy * Envelope(s.Age, s.Rise, s.Hold, s.Fade));
        }
        foreach (Glow g in _glows)
        {
            if (!g.Light.Visible) continue;
            g.Age += dt;
            if (g.Age >= g.Rise + g.Fade) g.Light.Visible = false;
            else g.Light.LightEnergy = g.Energy * Envelope(g.Age, g.Rise, 0f, g.Fade);
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
