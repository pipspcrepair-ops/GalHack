using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Plants;
using Godot;
using Planets.Gen;
using Planets.Look;
using PlanetSurface.Terrain;
using Slice.Game;

namespace Slice.Tests;

/// <summary>
/// A tree is whole at every distance: on a fresh world, one of its large species is set up in the sky
/// with each pair of its drawings that hand over (near to far, far to distant) and the camera walks
/// through the hand-over a quarter metre at a time. At each step the frame is compared with each
/// drawing drawn whole alone: the pair must draw all of one or all of the other (a canopy pixel of
/// it left empty is a hole). Any step with more than 3% holes in both fails (a stray pixel along a frond a pixel wide is not a hole; a tree half dissolved leaves far more). Windowed (it reads the frames).
///
/// Run: Godot --path prototypes/slice res://tests/FloraSwapTest.tscn
/// </summary>
public partial class FloraSwapTest : Node
{
    Game.Game _game;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(6));
            Console.Error.WriteLine("FAIL  the run took over 6 minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        _run = Scenario().GetEnumerator();
    }

    public override void _Process(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    MultiMeshInstance3D Drawing(Mesh mesh, Basis basis, Vector3 at, int layer, ShaderMaterial material, Color tint)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 1 };
        mm.SetInstanceTransform(0, new Transform3D(basis, at));
        mm.SetInstanceCustomData(0, tint);
        var m = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        m.SetInstanceShaderParameter("flora_layer", (float)layer);
        AddChild(m);
        return m;
    }

    IEnumerable<int> Scenario()
    {
        if (DisplayServer.GetName() == "headless")
        {
            Check(false, "a headless run draws no frames: run it in a window");
            yield break;
        }
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh("temperate")) };
        AddChild(_game);
        var drive = new Driver(_game);
        foreach (int x in drive.UntilPlaying(240)) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.45;
        _game.Visor.Visible = false;
        var streamer = _game.Zone.Streamer;
        var book = _game.Zone.Planet.Flora.Book;
        var plants = book.Plants.Where(p => p.Tree).ToList();
        if (plants.Count == 0)
        {
            Check(true, "(no trees on this world)");
            yield break;
        }
        var rng = new Random();
        GD.Print($"-- flora hand-overs on {_game.World} --");
        foreach (Plant plant in plants.OrderBy(_ => rng.Next()).Take(2))
        {
            Flora kind = Enum.GetValues<Flora>().FirstOrDefault(k => PlanetSurface.Terrain.PlanetFlora.FamilyOf(k) == plant.Family);
            foreach (var (name, a, b, reach) in new[] { ("near to far", 0, 1, DetailReach.Near), ("far to distant", 1, 3, DetailReach.Far) })
                foreach (int x in Sweep(streamer, plant, kind, name, a, b, reach)) yield return x;
        }
        _game.FixedCamera = null;
    }

    IEnumerable<int> Sweep(TerrainStreamer streamer, Plant plant, Flora kind, string name, int layerA, int layerB, float reach)
    {
        Vector3 feet = _game.Survivor.WorldFeet;
        // In the sky, well clear of the ground: the sky behind it.
        var root = new Vector3(feet.X + 37.3f, feet.Y + 700f, feet.Z + 11.9f);
        float s = MathF.Sin(root.X * 12.9898f + root.Z * 78.233f) * 43758.5453f;
        float own = 0.9f + 0.1f * (s - MathF.Floor(s));
        float at = reach * own;
        Basis basis = Basis.Identity;
        Mesh Of(int layer) => layer == 0 ? streamer.PlantMesh(plant.Index, 0) : layer == 1 ? streamer.PlantMesh(plant.Index, -1) : streamer.KindMesh(kind);
        Basis BasisOf(int layer) => layer == 3 ? FarBuilder.DistantBasis(kind, plant, basis) : basis;
        var tint = new Color(0.3f, 0.5f, 0.25f, 0f);
        ShaderMaterial mat = streamer.FloraMaterial;
        var pairA = Drawing(Of(layerA), BasisOf(layerA), root, layerA, mat, tint);
        var pairB = Drawing(Of(layerB), BasisOf(layerB), root, layerB, mat, tint);
        var wholeA = Drawing(Of(layerA), BasisOf(layerA), root, 2, mat, tint);
        var wholeB = Drawing(Of(layerB), BasisOf(layerB), root, 2, mat, tint);
        Vector3 aim = root + Vector3.Up * plant.Height * 0.6f;
        var way = new Vector3(0.6f, 0f, 0.8f);

        float worst = 0f, worstAt = 0f;
        int steps = 0;
        for (float d = at - 6f; d <= at + 6f; d += 0.25f)
        {
            // From a little below, so the sky stands behind the whole tree (the water and ground beyond shimmer).
            Vector3 eye = aim - way * d - Vector3.Up * d * 0.12f;
            _game.FixedCamera = new Transform3D(Basis.LookingAt(aim - eye, Vector3.Up), eye);
            Image Frame(bool pair, bool a, bool b)
            {
                pairA.Visible = pairB.Visible = pair;
                wholeA.Visible = a;
                wholeB.Visible = b;
                return null;
            }
            Image bg = null, both = null, onlyA = null, onlyB = null;
            foreach (var (pair, a, b, k) in new[] { (false, false, false, 0), (true, false, false, 1), (false, true, false, 2), (false, false, true, 3) })
            {
                Frame(pair, a, b);
                for (int i = 0; i < 3; i++) yield return 0;
                Image img = GetViewport().GetTexture().GetImage();
                if (k == 0) bg = img; else if (k == 1) both = img; else if (k == 2) onlyA = img; else onlyB = img;
            }
            // Whole: the pair draws all of one drawing or all of the other, not part of each.
            int inA = 0, inB = 0, missA = 0, missB = 0;
            int w = bg.GetWidth(), h = bg.GetHeight();
            bool Drawn(Image img, int x, int y)
            {
                Color c = img.GetPixel(x, y), o = bg.GetPixel(x, y);
                return MathF.Abs(c.R - o.R) + MathF.Abs(c.G - o.G) + MathF.Abs(c.B - o.B) > 0.06f;
            }
            // Only round the tree: the sky's clouds drift between frames.
            Camera3D cam = _game.Camera;
            float spread = MathF.Max(plant.CrownRadius, plant.Height * 0.3f) * DetailReach.DistantGrow * 1.6f;
            Vector2 lo = new(float.MaxValue, float.MaxValue), hi = new(float.MinValue, float.MinValue);
            Vector3 side = new Vector3(0.8f, 0f, -0.6f) * spread, low = Vector3.Down, high = Vector3.Up * plant.Height * 1.6f;
            foreach (Vector3 corner in new[] { root - side + low, root + side + low, root - side + high, root + side + high })
            {
                Vector2 p = cam.UnprojectPosition(corner);
                lo = lo.Min(p);
                hi = hi.Max(p);
            }
            int x0 = Math.Clamp((int)lo.X - 4, 0, w - 1), x1 = Math.Clamp((int)hi.X + 4, 0, w - 1), y0 = Math.Clamp((int)lo.Y - 4, 0, h - 1), y1 = Math.Clamp((int)hi.Y + 4, 0, h - 1);
            for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                bool drawn = Drawn(both, x, y);
                if (Drawn(onlyA, x, y)) { inA++; if (!drawn) missA++; }
                if (Drawn(onlyB, x, y)) { inB++; if (!drawn) missB++; }
            }
            int canopy = inA + inB;
            float share = MathF.Min(inA > 0 ? missA / (float)inA : 1f, inB > 0 ? missB / (float)inB : 1f);
            if (share > worst)
            {
                worst = share;
                worstAt = d;
                // The worst step's frames, for a look (--frames=DIR).
                string dir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--frames="))?[9..];
                if (dir != null)
                    foreach (var (img, tag) in new[] { (bg, "bg"), (both, "pair"), (onlyA, "a"), (onlyB, "b") })
                        img.SavePng($"{dir}/{plant.Index}-{layerA}{layerB}-{tag}.png");
            }
            steps++;
        }
        pairA.QueueFree();
        pairB.QueueFree();
        wholeA.QueueFree();
        wholeB.QueueFree();
        Check(worst <= 0.03f, $"{plant.Family} {plant.Form}, {name} at {at:F0} m: {steps} steps a quarter metre apart, at most {worst:P1} of the canopy left as holes (at {worstAt:F1} m)");
    }
}
