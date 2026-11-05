using System.Collections.Generic;
using GalHack.SkyCheck;
using Godot;

namespace Encounters.Tests;

/// <summary>
/// The space backdrop has no seams: rendered off-screen looking straight at a cube's edges and
/// corners, the poles, the axes and the azimuth's wrap, it shows no line, no pinch, and as many
/// stars along the axes as anywhere else. Headless, it runs itself again in a window off-screen.
/// </summary>
public partial class SkySeamTest : SkySeamCheck
{
    protected override IEnumerable<(string name, Material sky)> Skies()
    {
        yield return ("the space backdrop", new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/space_sky.gdshader") });
    }
}