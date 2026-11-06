using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Planets.Gen;
using Planets.Orbit;

namespace Planets;

/// <summary>The orbit globe's relief: deterministic, seamless round the wrap and at the poles, and cheap.</summary>
public partial class GenTest
{
    void GlobeChecks()
    {
        var spec = PlanetSpec.Roll(22, Archetype.Temperate, 0, 16);
        WorldMap map = PlanetGenerator.Generate(spec);
        var watch = Stopwatch.StartNew();
        GlobeRelief.Data a = GlobeRelief.Build(map);
        double ms = watch.Elapsed.TotalMilliseconds;
        GlobeRelief.Data b = GlobeRelief.Build(PlanetGenerator.Generate(spec));
        GlobeRelief.Data c = GlobeRelief.Build(PlanetGenerator.Generate(spec with { Seed = 23 }));
        Check(GlobeRelief.Hash(a) == GlobeRelief.Hash(b) && GlobeRelief.Hash(a) != GlobeRelief.Hash(c),
            $"globe: same seed, same globe ({GlobeRelief.Hash(a):X16}); another seed differs");

        // Relief that reads: ranges well up, oceans flat at sea level.
        float maxRise = 0f, seaRise = 0f;
        foreach (float r in a.Rise) maxRise = Math.Max(maxRise, r);
        int stride = a.Columns + 1;
        for (int j = 0; j <= a.Rows; j++)
        for (int i = 0; i < a.Columns; i++)
        {
            double x = i * map.WidthMeters / a.Columns, y = Math.Min(j / (double)a.Rows * map.HeightMeters, map.HeightMeters - 1e-3);
            if (map.Sample(map.Elevation, x, y) < 0f && j > a.Rows * 0.12 && j < a.Rows * 0.88) seaRise = Math.Max(seaRise, a.Rise[j * stride + i]);
        }
        Check(maxRise > 0.004f && maxRise < 0.012f && seaRise == 0f,
            $"globe relief: the highest ground stands {maxRise * 100:F2}% of the radius proud (under the clouds at 1.2%), seas flat ({seaRise:F4})");

        // The u seam: the last column is the first, and normals turn across it no more than anywhere else.
        double seamGap = 0, seamTurn = 0;
        var turns = new List<double>();
        for (int j = 1; j < a.Rows; j++)
        {
            int first = j * stride, last = first + a.Columns;
            seamGap = Math.Max(seamGap, (a.Vertices[first] - a.Vertices[last]).Length() + (a.Normals[first] - a.Normals[last]).Length());
            for (int i = 0; i < a.Columns; i++)
            {
                double turn = Angle(a.Normals[first + i], a.Normals[first + (i + 1) % a.Columns]);
                if (i == a.Columns - 1) seamTurn = Math.Max(seamTurn, turn);
                else turns.Add(turn);
            }
        }
        turns.Sort();
        double inner = turns[^1];
        Check(seamGap < 1e-6 && seamTurn <= inner,
            $"globe seam: the wrap's columns match (gap {seamGap:E1}); normals turn at most {seamTurn:F2} deg across it, {inner:F2} deg anywhere else");

        // The poles: one point each, no spike in the rings round them, normals pointing out.
        double poleSpread = 0, capTilt = 0, capRise = 0;
        for (int end = 0; end < 2; end++)
        {
            int j0 = end == 0 ? 0 : a.Rows;
            for (int i = 1; i <= a.Columns; i++) poleSpread = Math.Max(poleSpread, (a.Vertices[j0 * stride + i] - a.Vertices[j0 * stride]).Length());
            for (int d = 1; d <= 3; d++)
            {
                int j = end == 0 ? d : a.Rows - d;
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < a.Columns; i++)
                {
                    int k = j * stride + i;
                    lo = Math.Min(lo, a.Rise[k]);
                    hi = Math.Max(hi, a.Rise[k]);
                    capTilt = Math.Max(capTilt, Angle(a.Normals[k], a.Vertices[k].Normalized()));
                }
                capRise = Math.Max(capRise, hi - lo);
            }
        }
        Check(poleSpread < 1e-6 && capRise < 0.0005 && capTilt < 20,
            $"globe poles: each pole one point ({poleSpread:E1}), the rings round them level to {capRise * 100:F3}% of the radius, normals within {capTilt:F1} deg of straight out");

        int triangles = a.Indices.Length / 3;
        Check(ms < 400 && triangles <= 600_000,
            $"globe cost: built in {ms:F0} ms off the main thread, {triangles / 1000} k triangles ({a.Vertices.Length / 1000} k vertices)");
    }

    static double Angle(Vector3 a, Vector3 b) => Math.Acos(Math.Clamp(a.Normalized().Dot(b.Normalized()), -1f, 1f)) * 180 / Math.PI;
}
