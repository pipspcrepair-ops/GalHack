using Godot;

namespace Slice.Ui;

/// <summary>
/// The picture's gamma (the escape menu's BRIGHTNESS): one full-screen pass over the 3D view,
/// under every HUD and menu layer, so the world is lifted or darkened and the text stays as it is.
/// At 1 it is hidden and costs nothing.
/// </summary>
public partial class Brightness : CanvasLayer
{
    const string Code = @"shader_type canvas_item;
uniform sampler2D screen : hint_screen_texture, filter_nearest;
uniform float gamma = 1.0;
void fragment() {
    vec3 c = texture(screen, SCREEN_UV).rgb;
    COLOR = vec4(pow(max(c, vec3(0.0)), vec3(1.0 / gamma)), 1.0);
}";

    ShaderMaterial _material;
    ColorRect _rect;

    /// <summary>The gamma shown now (1: none).</summary>
    public float Gamma { get; private set; } = 1f;

    public override void _Ready()
    {
        Layer = -1; // over the 3D view, under the visor (1), the Tab menu (3) and the escape menu
        _material = new ShaderMaterial { Shader = new Shader { Code = Code } };
        _rect = new ColorRect { Name = "Pass", Material = _material, MouseFilter = Control.MouseFilterEnum.Ignore };
        _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(_rect);
        SetGamma(Gamma);
    }

    public void SetGamma(float gamma)
    {
        Gamma = gamma;
        if (_material == null) return;
        _material.SetShaderParameter("gamma", gamma);
        Visible = Mathf.Abs(gamma - 1f) > 0.005f;
    }
}
