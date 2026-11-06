using Godot;

namespace Slice.Orbit;

/// <summary>
/// The tunnel over a crossing's seam: streaks of light rushing outward and a white-blue glare that
/// fades in as the edge nears, holds while the other side is made ready, and fades out (the
/// design's hidden seam: a slower machine gets a longer tunnel, never a freeze).
/// </summary>
public partial class TunnelOverlay : CanvasLayer
{
    const string Source = """
        shader_type canvas_item;
        uniform float amount = 0.0;
        uniform float aspect = 1.7778;
        void fragment() {
            vec2 p = (UV - 0.5) * vec2(aspect, 1.0);
            float r = length(p);
            float a = atan(p.y, p.x);
            float t = TIME;
            float rays = pow(abs(sin(a * 37.0 + 1.3) * sin(a * 23.0 - 0.7)), 8.0);
            float rush = smoothstep(0.0, 1.0, fract(r * 2.5 - t * 1.6));
            float streaks = rays * rush * smoothstep(0.08, 0.7, r);
            vec3 glare = mix(vec3(0.82, 0.9, 1.0), vec3(1.0, 0.93, 0.82), smoothstep(0.0, 0.9, r));
            float alpha = amount * amount * (0.55 + 0.45 * smoothstep(0.85, 0.0, r)) + amount * streaks * 0.8;
            COLOR = vec4(glare + streaks * 0.3, clamp(alpha, 0.0, 1.0));
        }
        """;

    ColorRect _rect;
    ShaderMaterial _material;
    float _amount;

    /// <summary>0 (gone) to 1 (the seam hidden).</summary>
    public float Amount
    {
        get => _amount;
        set
        {
            _amount = Mathf.Clamp(value, 0f, 1f);
            if (_rect == null) return;
            _rect.Visible = _amount > 0.002f;
            _material.SetShaderParameter("amount", _amount);
        }
    }

    public override void _Ready()
    {
        Layer = 40;
        _material = new ShaderMaterial { Shader = new Shader { Code = Source } };
        _rect = new ColorRect { Name = "Glare", Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
    }

    public override void _Process(double delta)
    {
        Vector2 size = GetViewport().GetVisibleRect().Size;
        if (size.Y > 0) _material.SetShaderParameter("aspect", size.X / size.Y);
    }
}
