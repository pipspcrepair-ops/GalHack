using System.Collections.Generic;
using System.Threading.Tasks;
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
    NoiseTexture2D _clouds;

    /// <summary>The game's own cloud noise, built (it builds on a thread of its own).</summary>
    protected override async Task Prepare()
    {
        var fx = new WeatherFx();
        _clouds = fx.CloudNoise;
        fx.Free();
        for (int i = 0; i < 600 && _clouds.GetImage() == null; i++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    protected override IEnumerable<(string name, Material sky)> Skies()
    {
        var sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        sky.SetShaderParameter("cloud_noise", _clouds);
        yield return ("the weather sky", sky);
    }
}
