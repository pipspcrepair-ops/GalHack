using Godot;

namespace Saves;

/// <summary>
/// The climb and re-entry sequence: a full-screen tunnel of bold paperback-cover bands rushing
/// past. It plays for at least <see cref="MinimumSeconds"/> and then for as long as the zone
/// ahead still needs to load, so a slower PC gets a longer tunnel, never a frozen frame.
/// </summary>
public partial class Tunnel : ColorRect
{
    public const double MinimumSeconds = 2.5;

    /// <summary>0 hidden, 1 fully covering the screen.</summary>
    public float Strength { get; set; }
    public bool Climbing { get; set; } = true;

    ShaderMaterial _material;
    float _time;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        _material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        Material = _material;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        _time += (float)delta;
        Visible = Strength > 0.001f;
        if (!Visible) return;
        Vector2 size = GetViewportRect().Size;
        _material.SetShaderParameter("t", _time);
        _material.SetShaderParameter("strength", Strength);
        _material.SetShaderParameter("aspect", new Vector2(size.X / Mathf.Max(1f, size.Y), 1f));
        _material.SetShaderParameter("direction", Climbing ? 1f : -1f);
        // Climbing: cream, orange and teal into the dark. Re-entry: red and yellow fire.
        _material.SetShaderParameter("color_a", Climbing ? new Color("e9dfc4") : new Color("c1121f"));
        _material.SetShaderParameter("color_b", Climbing ? new Color("e85d04") : new Color("f4a100"));
        _material.SetShaderParameter("color_c", Climbing ? new Color("0a9396") : new Color("e85d04"));
        _material.SetShaderParameter("core", Climbing ? new Color("05070f") : new Color("ffe2b0"));
    }

    /// <summary>Sets the time, so screenshots are repeatable.</summary>
    public void SetTime(float time) => _time = time;

    const string Code = @"
shader_type canvas_item;
uniform float t;
uniform float strength;
uniform float direction;
uniform vec2 aspect;
uniform vec4 color_a : source_color;
uniform vec4 color_b : source_color;
uniform vec4 color_c : source_color;
uniform vec4 core : source_color;

void fragment() {
    vec2 p = (UV - 0.5) * aspect;
    float r = max(length(p), 0.001);
    float a = atan(p.y, p.x) / 6.2831853;
    float depth = 0.3 / r;                          // rings get closer together towards the centre
    float z = depth + t * 2.4 * direction;
    float ring = fract(z);
    vec3 col = mix(color_a.rgb, color_b.rgb, step(0.33, ring));
    col = mix(col, color_c.rgb, step(0.66, ring));
    float stripe = step(0.86, fract(a * 16.0 + depth * 0.15));
    col = mix(col, vec3(0.08, 0.05, 0.04), stripe * 0.7);
    float hole = smoothstep(0.03, 0.16, r);         // the far end of the tunnel
    col = mix(core.rgb, col, hole);
    col *= 0.55 + 0.45 * smoothstep(0.9, 0.2, r);   // darker at the edges, like an old print
    COLOR = vec4(col, strength);
}";
}
