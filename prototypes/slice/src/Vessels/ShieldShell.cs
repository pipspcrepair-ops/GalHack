using Godot;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// A structure's shield, seen only when it is struck (design section 13: "shields ripple where they're struck"): a faint
/// purple shell round the structure's bounds, rippling out from each hit and fading in under a second. One mesh and a
/// generated shader; hidden while no ripple is live. Purple, against the force fields' blue. On anything grounded or
/// anchored it is a dome, cut at the structure's lowest cell, so none of it shows in the ground; a flying vessel's is whole.
/// </summary>
public partial class ShieldShell : MeshInstance3D
{
    const int Ripples = 4;
    const float Life = 0.9f, Margin = 0.8f;

    const string Code = @"
shader_type spatial;
render_mode unshaded, blend_add, cull_disabled, depth_draw_never, shadows_disabled;
uniform vec4 tint : source_color = vec4(0.69, 0.29, 0.93, 1.0);
uniform vec3 hits[4];
uniform float ages[4];
uniform float life = 0.9;
uniform float floor_y = -2.0;
varying vec3 dir;
void vertex() { dir = VERTEX; }
void fragment() {
    if (dir.y < floor_y) discard;
    vec3 d = normalize(dir);
    float glow = 0.0;
    for (int i = 0; i < 4; i++) {
        float t = ages[i] / life;
        if (t < 0.0 || t >= 1.0) continue;
        float angle = acos(clamp(dot(d, hits[i]), -1.0, 1.0));
        float ring = exp(-pow((angle - t * 1.4) * 10.0, 2.0));
        float core = exp(-angle * 4.0) * (1.0 - t);
        glow += (ring * 0.7 + core * 0.5) * (1.0 - t) * (1.0 - t);
    }
    float rim = 1.0 - abs(dot(NORMAL, VIEW));
    ALBEDO = tint.rgb;
    ALPHA = clamp(glow * (0.35 + 0.65 * rim * rim), 0.0, 1.0) * 0.8;
}";

    static Shader _shader;
    readonly Vector3[] _hits = new Vector3[Ripples];
    readonly float[] _ages = new float[Ripples];
    int _next;
    ShaderMaterial _material;
    Vector3 _centre, _half = Vector3.One;

    ShieldShell()
    {
        for (int i = 0; i < Ripples; i++) _ages[i] = Life;
    }

    /// <summary>The shell on a vessel, made the first time its shield is struck.</summary>
    public static ShieldShell On(Vessel v)
    {
        foreach (Node child in v.Node.GetChildren())
            if (child is ShieldShell shell) return shell;
        _shader ??= new Shader { Code = Code };
        var made = new ShieldShell
        {
            Name = "ShieldShell",
            Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 32, Rings = 16 },
            CastShadow = ShadowCastingSetting.Off,
            Visible = false,
        };
        made._material = new ShaderMaterial { Shader = _shader };
        made.MaterialOverride = made._material;
        v.Node.AddChild(made);
        return made;
    }

    /// <summary>The lowest point of the shell drawn, in the structure's frame, metres: its lowest cell's underside when grounded or anchored.</summary>
    public float Floor { get; private set; }

    /// <summary>A hit taken at a point in the structure's frame, metres: the shell wraps the structure as it is now, and a ripple starts there.</summary>
    public void Struck(Vessel v, N.Vector3 local)
    {
        Structure s = v.Structure;
        var (lo, hi) = s.Bounds();
        float cell = s.Grid.CellSize;
        Vector3 min = new Vector3(lo.X - 0.5f, lo.Y - 0.5f, lo.Z - 0.5f) * cell, max = new Vector3(hi.X + 0.5f, hi.Y + 0.5f, hi.Z + 0.5f) * cell;
        _centre = (min + max) * 0.5f;
        // An ellipsoid through the box's corners, a little clear of them.
        _half = (max - min) * 0.5f * Mathf.Sqrt(3f) + Vector3.One * Margin;
        Transform = new Transform3D(Basis.FromScale(_half), _centre);
        Floor = v.IsBase || v.Controller is { Frozen: true } ? min.Y : _centre.Y - _half.Y;
        _material.SetShaderParameter("floor_y", (Floor - _centre.Y) / _half.Y);
        Vector3 at = new Vector3(local.X, local.Y, local.Z) - _centre;
        _hits[_next] = (at / _half).Normalized();
        _ages[_next] = 0f;
        _next = (_next + 1) % Ripples;
        Visible = true;
        Push();
    }

    /// <summary>Is a ripple still showing?</summary>
    public bool Rippling
    {
        get
        {
            foreach (float a in _ages)
                if (a < Life) return true;
            return false;
        }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        for (int i = 0; i < Ripples; i++) _ages[i] = Mathf.Min(Life, _ages[i] + (float)delta);
        Visible = Rippling;
        Push();
    }

    void Push()
    {
        _material.SetShaderParameter("hits", _hits);
        _material.SetShaderParameter("ages", _ages);
        _material.SetShaderParameter("life", Life);
    }
}
