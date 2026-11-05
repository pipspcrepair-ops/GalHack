using System.Collections.Generic;
using GalHack.SkyCheck;
using Godot;

namespace Weather.Tests;

/// <summary>
/// The weather's sky has no seams.
/// Rendered off-screen looking straight at a cube's edges and corners, the poles, the axes and
/// the azimuth's wrap: no line, no pinch, and as many stars along the axes as anywhere else.
/// Headless, it runs itself again in a window off-screen (shared/Sky/SkySeams.cs).
/// </summary>
public partial class SkySeamTest : SkySeamCheck
{
    protected override IEnumerable<(string name, Material sky)> Skies()
    {
        yield return ("the weather sky", new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") });
    }
}
