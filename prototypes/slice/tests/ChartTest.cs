using System;
using System.Diagnostics;
using System.Linq;
using GalHack.Stars;
using Godot;
using PlanetSurface.Skies;
using Planets.Gen;
using Slice.Game;
using Slice.Orbit;

namespace Slice.Tests;

/// <summary>
/// The map levels' logic, no engine in play: marks appear when discovered and not before, a point
/// picked on the world map round-trips the wrap both ways, and the system and star listings hold
/// what they should.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/ChartTest.tscn
/// </summary>
public partial class ChartTest : Node
{
    int _failures, _passes;
    const double W = 16000, H = 8000;

    public override void _Ready()
    {
        var clock = Stopwatch.StartNew();
        try
        {
            Discovery();
            Wrap();
            Listings();
        }
        catch (Exception e) { Check(false, $"threw: {e}"); }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    void Discovery()
    {
        var charts = new Charts();
        var places = new[] { ("ruin", "Ruined Mill", 1000.0, 1000.0), ("seam", "Seam Camp", 15900.0, 7900.0) };
        charts.LookAround(places, 3000, 3000, W, H);
        Check(!charts.Marks.Any(), "from far off, no place is on the chart");
        charts.LookAround(places, 1100, 1200, W, H);
        Check(charts.OfKind("place").Count() == 1 && charts.Marks.First().Label == "Ruined Mill", "going near one puts it on the chart, and only that one");
        charts.LookAround(places, 100, 50, W, H);
        Check(charts.OfKind("place").Count() == 2, "and a place just across the torus's corner counts as near: distance wraps both ways");
        Check(!charts.Find("ruin", "Ruined Mill", 1000, 1000, "place"), "a place is only found once");
        Check(charts.Find("iron-1", "iron", 400, 400, "deposit") && charts.OfKind("deposit").Count() == 1, "a deposit the compass found is marked");
        ChartMark m = charts.Mark(5000, 2000);
        Check(charts.OfKind("mark").Single() == m && m.Label == "MARK 1", "your own mark stands on the chart");
        Check(!charts.SeeWreck("w1", "Freighter", 9000, 0.4f, 1f, "wreck") && charts.Wrecks.Count == 0, "a wreck out of sight isn't known");
        Check(charts.SeeWreck("w1", "Freighter", 2000, 0.4f, 1f, "wreck") && charts.Wrecks.Count == 1, "in sight it comes onto the system map");
        Check(!charts.SeeWreck("w1", "Freighter", 100, 0.4f, 1f, "wreck") && charts.Wrecks.Count == 1, "once");
    }

    void Wrap()
    {
        var view = new WorldMapView(W, H, 10);
        var rng = new Random(11);
        double worst = 0;
        for (int i = 0; i < 300; i++)
        {
            view.CentreX = rng.NextDouble() * W;
            view.CentreZ = rng.NextDouble() * H;
            double x = rng.NextDouble() * W, z = rng.NextDouble() * H;
            var (px, py) = view.ToView(x, z);
            var (bx, bz) = view.FromView(px, py);
            double dx = Math.Abs(bx - x), dz = Math.Abs(bz - z);
            dx = Math.Min(dx, W - dx);
            dz = Math.Min(dz, H - dz);
            worst = Math.Max(worst, Math.Max(dx, dz));
        }
        Check(worst < 1e-6, $"a point picked anywhere on the world map comes back to itself through the wrap (worst {worst:E1} m)");
        view.PanelHeight = 400;
        view.CentreX = W - 10;
        view.CentreZ = H / 2;
        var (sx, sy) = view.ToView(10, H / 2);
        Check(Math.Abs(sx - 2) < 1e-6 && Math.Abs(sy) < 1e-6, $"a point just across the east-west seam shows just across it ({sx:F1}) px");
        view.Drag(-400, -3000);
        Check(Math.Abs(view.CentreX - ((W - 10 + 4000) % W)) < 1e-6 && Math.Abs(view.CentreZ - (H - 2000)) < 1e-6, $"dragging east-west wraps but north-south stops at the pole ({view.CentreX:F0}, {view.CentreZ:F0})");
        view.Drag(0, 9000);
        Check(Math.Abs(view.CentreZ - 2000) < 1e-6, "and at the other pole");
        view.MetresPerPixel = H / 100;
        view.Settle();
        Check(Math.Abs(view.CentreZ - H / 2) < 1e-6, "a map shorter than the panel sits with the equator across the middle");
        view.MetresPerPixel = 10;
        double wide = view.MetresPerPixel;
        view.Zoom(-3);
        Check(view.MetresPerPixel < wide, "the wheel zooms in");
        view.Zoom(100);
        Check(view.MetresPerPixel == wide, "and out no further than the whole map");
        view.Zoom(-100);
        Check(view.MetresPerPixel == WorldMapView.Closest, "and in no closer than its closest");
    }

    void Listings()
    {
        PlanetSpec spec = WorldChoice.Tested.Spec();
        SkySystem sky = SkySystem.For(spec);
        StarSystem system = StarSystem.For(spec, sky, 16000);
        var bodies = Charts.Bodies(system, "Your world", 3.25);
        Check(bodies[0].Kind == "star" && bodies.Count(b => b.Kind == "world") == 1 && bodies.Count(b => b.Kind is "planet" or "giant" or "world" or "moon") >= 8 && bodies.Any(b => b.Kind == "belt"),
            $"the system map lists the star, the world and the rest of its system: {string.Join(", ", bodies.Select(b => b.Label))}");
        var later = Charts.Bodies(system, "Your world", 13.25);
        Check(bodies.Zip(later).Any(p => p.First.Bearing != p.Second.Bearing), "the system map reads the moment: its worlds move on along their orbits");
        // The stars are the galaxy's own: GalaxyTest holds the map to the sky.
        Galaxy galaxy = Galaxy.Make(spec.Seed);
        int home = galaxy.Home(PlanetSurface.Skies.SkyRig.KindOf(sky.Star), spec.Seed, false);
        SkyView view = SkyView.From(galaxy, home);
        var one = Charts.Stars(galaxy, view, home);
        Check(one.Select(s => s.Id).SequenceEqual(Charts.Stars(galaxy, view, home).Select(s => s.Id)), "the star map is the same every time for a world");
        Check(one.Count(s => s.Home) == 1 && one.Count >= 2 && one.Where(s => !s.Home).All(s => s.Light is > 0f and <= 30f),
            $"home and {one.Count - 1} neighbours, each within a jump (30 LY)");
        var charts = new Charts();
        charts.Visited.Add(home);
        Check(charts.Charted(galaxy, one[1].Id) && !charts.Charted(galaxy, 0), "the neighbours are charted; the far core isn't");
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }
}
