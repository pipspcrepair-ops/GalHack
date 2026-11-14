using System;
using System.Threading.Tasks;
using Godot;
using Planets.Gen;
using PlanetSurface.Skies;
using Weather.Model;
using Archetype = Planets.Gen.Archetype;

namespace PlanetSurface;

public partial class SkyTest
{
    /// <summary>
    /// Fog weather is a layer on the low ground, not a blanket: from 300 m up over clear land the fog density is no more
    /// than the air's haze, on the ground inside the layer the visibility is the weather's own, and climbing through
    /// the layer's top changes it smoothly. Shots (<c>--keep=DIR</c>): in the fog, on a hilltop above it, from 300 m up.
    /// </summary>
    async Task FogLayer()
    {
        string keep = System.Array.Find(OS.GetCmdlineUserArgs(), a => a.StartsWith("--keep="))?["--keep=".Length..];
        var main = new Main { Interactive = false };
        AddChild(main);
        main.Arrive(PlanetSpec.Roll(21, Archetype.Temperate, 0));
        if (!await Until(() => main.World != null, 120)) { Fail("fog layer: the planet never built"); return; }
        var w = main.World;
        main.Player.ProcessMode = ProcessModeEnum.Disabled;
        main.Hud.HideAll();
        main.Sky.Paused = true;
        main.Sky.TimeOfDay = 0.45;
        main.Weather.Snap(WeatherKind.Fog);
        var env = main.Sky.Environment;

        // The home ground, and the hill that stands highest over its own low ground (as the layer finds it).
        float cx = (float)w.Landing.X, cz = (float)w.Landing.Y, hx = cx, hz = cz, hy = 0f, best = float.MinValue;
        for (int i = -8; i <= 8; i++)
        for (int j = -8; j <= 8; j++)
        {
            float x = cx + i * 600f, z = cz + j * 600f, g = w.GroundHeight(x, z);
            var around = new System.Collections.Generic.List<float>();
            for (int r = 1; r <= 3; r++)
            for (int k = 0; k < 16; k++)
                around.Add(MathF.Max(0f, w.GroundHeight(x + MathF.Cos(k * MathF.Tau / 16) * r * 350f, z + MathF.Sin(k * MathF.Tau / 16) * r * 350f)));
            around.Sort();
            if (g - around[around.Count / 6] > best) { best = g - around[around.Count / 6]; hy = g; hx = x; hz = z; }
        }
        float home = MathF.Max(w.GroundHeight(cx, cz), 0f);

        async Task<float> At(float x, float z, float y, float pitch, string save)
        {
            main.Fly.Begin(new Vector3(x, y, z), 0.6f, Mathf.DegToRad(pitch));
            await Until(() => main.Streamer.Settled, 90);
            await Frames(40);
            if (keep != null && save != null) GetViewport().GetTexture().GetImage().SavePng(System.IO.Path.Combine(keep, save + ".png"));
            return env.FogDensity;
        }

        float inside = await At(cx, cz, home + 2f, -2f, "fog-1-ground");
        float visible = main.Sky.Air.Visibility;
        float seen = 2.3f / inside;
        Check(MathF.Abs(inside - 2.3f * 0.85f / visible) / (2.3f * 0.85f / visible) < 0.2f,
            $"on the ground in the layer the fog is the weather's own: density {inside:F4} against {2.3f * 0.85f / visible:F4} for {visible:F0} m visibility");
        float topY = env.FogHeight, depth = Atmosphere.ValleyFogDepthOf(1f), floor = topY - depth;
        float high = hy + 2f;
        float hill = await At(hx, hz, high, -2f, "fog-2-hilltop");
        await Frames(90); // the layer's floor is found once a second
        hill = env.FogDensity;
        float hillTop = env.FogHeight;
        float up = await At(cx, cz, home + 300f, -18f, "fog-3-300m");
        float upHaze = env.FogDensity;
        Check(upHaze <= Atmosphere.HazeMost * 1.001f,
            $"300 m above the ground over clear land the fog is no more than the air's haze: {upHaze * 1e6f:F0}e-6 against {Atmosphere.HazeMost * 1e6f:F0}e-6 (visibility {2.3f / MathF.Max(upHaze, 1e-6f):F0} m, was {seen:F0} m)");
        Check(high < hillTop + depth * 0.5f ? best < depth * 1.5f : hill <= Atmosphere.HazeMost * 1.001f,
            $"on a hilltop {high:F0} m (layer's top there {hillTop:F0} m, home ground's {topY:F0} m) the fog is {hill * 1e6f:F0}e-6");

        // Climb through the top in 1 m steps over the home ground: no jump.
        float prev = -1f, worst = 0f;
        for (float y = floor + 1f; y <= topY + depth * 0.5f + 20f; y += 2f)
        {
            main.Fly.Begin(new Vector3(cx, y, cz), 0.6f, 0f);
            await Frames(3);
            float d = env.FogDensity;
            if (prev >= 0f) worst = MathF.Max(worst, MathF.Abs(d - prev));
            prev = d;
        }
        Check(worst < 0.2f * inside, $"crossing the layer's top (2 m steps): the largest step in fog density is {worst * 1e6f:F0}e-6 of {inside * 1e6f:F0}e-6");
        RemoveChild(main);
        main.QueueFree();
    }
}
