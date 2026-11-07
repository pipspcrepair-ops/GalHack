using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using Slice.Orbit;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// The sphere-to-map mapping (<see cref="GlobeMap"/>): its pure checks (round trips, descents at
/// eight points with both poles and the seam among them, the seam and a pole crossing), and checks
/// against the engine itself, so the globe the player sees and the map they land on agree: every
/// vertex of Godot's own SphereMesh points where the mapping says its texture coordinate is (the
/// globe's colour map is planet-generator's ColorMap, whose pixel (u, v) is the map point
/// (u·width, v·height)).
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/GlobeTest.tscn
/// </summary>
public partial class GlobeTest : Node
{
    int _failures, _passes;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        const double width = 16000, height = 8000;
        foreach (var (ok, text) in GlobeChecks.Run(width, height)) Check(ok, text);
        SphereMeshAgrees(width, height);
        SheetLooksUpTheMap(width, height);
        TheCap();
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    /// <summary>
    /// Godot's SphereMesh carries the globe's texture: a vertex with texture coordinate (u, v) shows
    /// the map point (u·width, v·height). The mapping must put that map point in the same direction
    /// as the vertex, or the place under a descending vessel isn't the place it lands on.
    /// </summary>
    void SphereMeshAgrees(double width, double height)
    {
        var globe = new GlobeMap(width, height, 1);
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 48, Rings = 24 };
        Godot.Collections.Array arrays = mesh.GetMeshArrays();
        var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var uvs = arrays[(int)Mesh.ArrayType.TexUV].AsVector2Array();
        double worst = 0;
        int checkedCount = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Vector3 v = vertices[i];
            if (v.Length() < 0.5f) continue;
            Vector3 d = v.Normalized();
            // At a pole every longitude is the same point.
            N.Vector3 m = globe.Direction(uvs[i].X * width, uvs[i].Y * height);
            double angle = Math.Acos(Math.Clamp(d.Dot(new Vector3(m.X, m.Y, m.Z)), -1f, 1f)) * 180 / Math.PI;
            worst = Math.Max(worst, angle);
            checkedCount++;
        }
        Check(checkedCount > 1000 && worst < 0.5,
            $"globe: all {checkedCount} vertices of Godot's SphereMesh point where the mapping puts their texture's map point (worst {worst:F3}°)");

        // East is east: a quarter of the way round the map from x = 0 is the globe's +X side (u runs east).
        N.Vector3 quarter = globe.Direction(width / 4, height / 2);
        Check(quarter.X > 0.99f, $"globe: a quarter of the map east of the seam, on the equator, is the globe's +X side ({quarter.X:F3}, {quarter.Y:F3}, {quarter.Z:F3})");
        N.Vector3 north = globe.Direction(width / 3, 0);
        Check(north.Y > 0.999f, $"globe: the map's top row is the north pole ({north.Y:F4} up)");
    }

    /// <summary>The sheet's shader finds the map point of a direction by this same sum: it must be the mapping's own inverse.</summary>
    void SheetLooksUpTheMap(double width, double height)
    {
        var globe = new GlobeMap(width, height, width / Math.Tau);
        var rng = new Random();
        double worst = 0;
        for (int i = 0; i < 400; i++)
        {
            double x = rng.NextDouble() * width, z = (0.01 + 0.98 * rng.NextDouble()) * height;
            N.Vector3 d = globe.Direction(x, z);
            double u = Math.Atan2(d.X, d.Z) / Math.Tau, v = Math.Acos(Math.Clamp(d.Y, -1f, 1f)) / Math.PI;
            double dx = Math.Abs(u - Math.Round(u - x / width) - x / width) * width;
            worst = Math.Max(worst, Math.Max(dx, Math.Abs(v * height - z)));
        }
        Check(worst < 3, $"globe: the sheet's lookup of a direction is the map point it came from, within {worst:F2} m at 400 random places");
    }

    /// <summary>The cap: orbit starts at 1,060 m on every world, and the land stands under it on fresh worlds of every type, tall or not.</summary>
    void TheCap()
    {
        OrbitRules rules = OrbitRules.Shared;
        foreach (string air in Enum.GetNames<PlanetSurface.Skies.AirKind>())
            Check(rules.OrbitAltitude(air) == 1060f, $"globe: orbit starts at 1,060 m under {air} air ({rules.OrbitAltitude(air):F0} m), whatever the world's peaks");
        var worlds = new Slice.Game.WorldChoice[20];
        for (int i = 0; i < worlds.Length; i++) worlds[i] = Slice.Game.WorldChoice.Fresh(Slice.Game.WorldChoice.Types[i % Slice.Game.WorldChoice.Types.Count].Key);
        var highest = new float[worlds.Length];
        System.Threading.Tasks.Parallel.For(0, worlds.Length, i => highest[i] = Planets.Gen.PlanetGenerator.Generate(worlds[i].Spec()).Elevation.Max());
        int tallest = Array.IndexOf(highest, highest.Max());
        Check(highest.Max() < 1060f - 60f, $"globe: the land on {worlds.Length} fresh worlds stands well under the cap: the tallest peak {highest[tallest]:F0} m ({worlds[tallest]}), the average {highest.Average():F0} m");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
