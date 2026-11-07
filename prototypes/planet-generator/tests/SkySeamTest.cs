using GalHack.Stars;
using System.Collections.Generic;
using GalHack.SkyCheck;
using Godot;

namespace Planets.Tests;

/// <summary>
/// The planet generator's skies (from the ground, and deep space) have no seams.
/// Rendered off-screen looking straight at a cube's edges and corners, the poles, the axes and
/// the azimuth's wrap: no line, no pinch, and as many stars along the axes as anywhere else.
/// Headless, it runs itself again in a window off-screen (shared/Sky/SkySeams.cs).
/// </summary>
public partial class SkySeamTest : SkySeamCheck
{
    protected override IEnumerable<(string name, Material sky)> Skies()
    {
        SkySeams.RealStars = true;
        var galaxy = new GalaxySky((ulong)System.Random.Shared.NextInt64(), StarKind.YellowDwarf, false);
        galaxy.Finish();
        var ground = new ShaderMaterial { Shader = Shaders.Sky };
        galaxy.Bind(ground);
        ground.SetShaderParameter("top_color", new Color(0.22f, 0.42f, 0.78f));
        ground.SetShaderParameter("horizon_color", new Color(0.75f, 0.82f, 0.9f));
        ground.SetShaderParameter("ground_color", new Color(0.35f, 0.32f, 0.28f));
        ground.SetShaderParameter("sun_color", new Color(1f, 0.92f, 0.8f));
        ground.SetShaderParameter("moon_color", new Color(0.8f, 0.78f, 0.75f));
        ground.SetShaderParameter("cloud_color", new Color(0.95f, 0.95f, 0.97f));
        ground.SetShaderParameter("stars", 1f);
        yield return ("the sky from the ground, its stars out", ground);
        var space = new ShaderMaterial { Shader = Shaders.Space };
        galaxy.Bind(space);
        yield return ("deep space", space);
    }
}
