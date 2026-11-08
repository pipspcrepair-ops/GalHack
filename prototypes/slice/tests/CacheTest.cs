using System;
using System.Diagnostics;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Items;

namespace Slice.Tests;

/// <summary>
/// E on a cache, no engine in play: a look from anywhere round it a person can stand (and crouch), aimed
/// at any visible part of it (its lid, its sides, its corners), meets its collision box and finds the
/// cache, as the prompt and E do (the same pick after the same world ray). A look that a wall stops
/// short of it finds nothing.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/CacheTest.tscn
/// </summary>
public partial class CacheTest : Node
{
    int _failures, _passes;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        try { Run(); }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");
    }

    /// <summary>Where a ray enters a box, or null.</summary>
    static float? Enter(Vector3 o, Vector3 d, Vector3 lo, Vector3 hi)
    {
        float t0 = 0f, t1 = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float inv = 1f / (MathF.Abs(d[i]) < 1e-9f ? 1e-9f : d[i]);
            float a = (lo[i] - o[i]) * inv, b = (hi[i] - o[i]) * inv;
            if (a > b) (a, b) = (b, a);
            t0 = MathF.Max(t0, a);
            t1 = MathF.Min(t1, b);
            if (t0 > t1) return null;
        }
        return t0;
    }

    void Run()
    {
        // A cache in cell (0, 0, 0) on a floor, as a place stands it: its container a twentieth over the floor, its collision the mesher's.
        var loot = new Lootables();
        var cache = new Lootable { Kind = LootKind.Cache, Name = "Cache", Position = new Vector3(0.5f, 0.05f, 0.5f), Radius = 0.55f };
        loot.All.Add(cache);
        var (clo, chi) = Shapes.CollisionBox(new Block(BlockType.Cache, PoiGenerator.Gen.Material.Alloy, default, Role.Loot));
        Vector3 boxLo = new(clo.X, clo.Y, clo.Z), boxHi = new(chi.X, chi.Y, chi.Z);
        // What's drawn: its body and its lid (the lid stands proud of the body all round).
        Vector3[] drawn =
        {
            new(0.5f, 0.65f, 0.5f), new(0.15f, 0.62f, 0.5f), new(0.85f, 0.62f, 0.5f), new(0.5f, 0.62f, 0.2f), new(0.5f, 0.62f, 0.8f),
            new(0.08f, 0.6f, 0.18f), new(0.92f, 0.6f, 0.82f), new(0.1f, 0.3f, 0.5f), new(0.9f, 0.3f, 0.5f), new(0.5f, 0.3f, 0.2f), new(0.5f, 0.3f, 0.8f),
            new(0.5f, 0.1f, 0.2f), new(0.12f, 0.45f, 0.22f),
        };
        int looks = 0, found = 0;
        string missed = null;
        foreach (float eyeY in new[] { 1.6f, 0.95f })
            for (int a = 0; a < 16; a++)
                foreach (float dist in new[] { 0.9f, 1.6f, 2.6f })
                {
                    float ang = a * MathF.Tau / 16f;
                    var eye = new Vector3(0.5f + MathF.Cos(ang) * dist, eyeY, 0.5f + MathF.Sin(ang) * dist);
                    foreach (Vector3 aim in drawn)
                    {
                        Vector3 dir = (aim - eye).Normalized();
                        // The far side of the cache isn't in sight from here.
                        float? body = Enter(eye, dir, new Vector3(0.07f, 0f, 0.17f), new Vector3(0.93f, 0.65f, 0.83f));
                        if (body == null || body.Value + 0.02f < (aim - eye).Length() - 0.05f) continue;
                        if ((aim - eye).Length() > Game.Game.UseReach) continue;
                        looks++;
                        // The world ray stops on the cache's collision box, or runs on to the floor.
                        float hit = Enter(eye, dir, boxLo, boxHi) ?? (dir.Y < 0 ? eye.Y / -dir.Y : Game.Game.UseReach);
                        if (loot.Pick(eye, dir, MathF.Min(hit, Game.Game.UseReach) + 0.3f, out _) == cache) found++;
                        else missed ??= $"from {eye} at {aim}";
                    }
                }
        Check(looks > 300 && found == looks, $"a look at any visible part of a cache, from all round it, standing or crouched, finds it ({found} of {looks}{(missed != null ? $"; missed {missed}" : "")})");
        // A wall between: the look stops a metre short of it.
        int through = 0;
        for (int a = 0; a < 16; a++)
        {
            float ang = a * MathF.Tau / 16f;
            var eye = new Vector3(0.5f + MathF.Cos(ang) * 2.8f, 1.6f, 0.5f + MathF.Sin(ang) * 2.8f);
            Vector3 dir = (new Vector3(0.5f, 0.4f, 0.5f) - eye).Normalized();
            if (loot.Pick(eye, dir, 1.4f + 0.3f, out _) != null) through++;
        }
        Check(through == 0, $"and none through a wall that stops the look short of it ({through} of 16 did)");
        loot.Free();
    }
}
