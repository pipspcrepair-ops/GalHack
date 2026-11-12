using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface.WeatherView;
using Weather.Model;

namespace Slice.Tests;

/// <summary>
/// --view=lightning: a storm front on the horizon at dusk. A far strike in it (shot "far"), then a
/// near one (shot "near"), each held at its first stroke, with checks printed PASS or FAIL: the far
/// strike lights only its own part of the sky (the cloud well away from it changes by under a small
/// bound), the near one still flashes and lights the ground, and no bolt has rungs across it.
/// --time is the hour (0.8 dusk), --dist the front's distance (6000 m).
/// </summary>
public partial class Staging
{
    IEnumerable<int> LightningShot()
    {
        var sky = _game.Zone.Sky;
        sky.Paused = true;
        sky.TimeOfDay = Num("time", 0.8);
        _game.Visor.Visible = false;
        _game.Hands.Visible = false;
        var weather = _game.Zone.Weather;
        var sim = weather.Sim;
        sim.Snap(WeatherKind.Clouds);
        sim.ClearTornadoes();
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector2 wind = sim.Current.Wind;
        Vector2 way = Front.Lattice(wind, sim.WorldSize, 5500f).heading;
        Vector3 toward = new(-way.X, 0f, -way.Y);
        Vector3 eye = Lookout(_game.Survivor.WorldFeet, toward, 24f);
        _game.Survivor.PlaceOutside((eye + Vector3.Up).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        eye.Y = _game.Zone.GroundAt(eye.X, eye.Z).Y + 24f;
        Front f = sim.StartFront(FrontKind.Storm, eye, (float)Num("dist", 6000), wind, null);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(toward * MathF.Cos(0.12f) + Vector3.Up * MathF.Sin(0.12f), Vector3.Up), eye);
        foreach (int x in _drive.Wait(120)) yield return x;

        LightningFx fx = weather.Lightning;
        // The weather's own strikes would muddle the comparison: held, none comes in.
        fx.Closed = true;
        fx.FarBoltChance = 1f; // the far shot shows its thin bolt
        foreach (int x in _drive.Wait(60)) yield return x;
        Image calm = Shrunk();

        // Far: under the storm's edge, off to the right of the view.
        Vector3 right = toward.Cross(Vector3.Up).Normalized();
        Vector3 under = f.EdgeAt(f.Along(eye) + 1600f, eye) - new Vector3(f.Heading.X, 0f, f.Heading.Y) * 600f;
        Vector3 farAt = _game.Zone.GroundAt(under.X, under.Z);
        fx.Closed = false;
        weather.StrikeAt(farAt);
        fx.Closed = fx.Held = true;
        foreach (int x in _drive.Wait(30)) yield return x;
        Snap("far");
        Image far = Shrunk();
        float farNear = fx.LastNearness, farLight = weather.StrikeLight;
        Camera3D cam = _game.GetViewport().GetCamera3D();
        Vector2 size = _game.GetViewport().GetVisibleRect().Size;
        Vector2 lit = cam.UnprojectPosition(fx.FlashAt) / size; // 0..1 across the frame
        // Clouds well away from the strike: the sky's upper half, more than 40% of the frame's width off to the side.
        (float away, float round) = Difference(calm, far, (u, v) => v < 0.5f && MathF.Abs(u - lit.X) > 0.4f, (u, v) => v < 0.6f && MathF.Abs(u - lit.X) < 0.12f);
        fx.Held = false;
        foreach (int x in _drive.Wait(120)) yield return x;

        // Near: 90 m ahead.
        Vector3 nearAt = _game.Zone.GroundAt(eye.X + toward.X * 90f + right.X * 20f, eye.Z + toward.Z * 90f + right.Z * 20f);
        Image calm2 = Shrunk();
        fx.Closed = false;
        weather.StrikeAt(nearAt);
        fx.Closed = fx.Held = true;
        foreach (int x in _drive.Wait(30)) yield return x;
        Snap("near");
        Image near = Shrunk();
        float nearLight = weather.StrikeLight;
        bool nearBolt = fx.BoltShown;
        (float whole, _) = Difference(calm2, near, (u, v) => true, (u, v) => false);
        fx.Held = fx.Closed = false;
        fx.FarBoltChance = 0.35f;

        Result(far != null && away < 0.015f, $"a far strike ({farAt.DistanceTo(eye):F0} m, nearness {farNear:F2}) leaves the cloud well away from it as it was (mean change {away:F4}, under 0.015), while round it the cloud glows ({round:F4})");
        Result(farLight < 0.05f, $"and it puts no light on the ground here ({farLight:F2})");
        Result(whole > 0.03f && nearLight > 1f && nearBolt, $"a near strike still flashes: the frame brightens by {whole:F3} (over 0.03), its light {nearLight:F1}, its bolt drawn {nearBolt}");

        // Rungs: every channel is one strip whose pieces share their edges, untwisted, narrowing to a point at both ends.
        int channels = 0, bad = 0;
        for (ulong seed = 1; seed <= 20; seed++)
            foreach (LightningFx.Channel c in LightningFx.TestBolt(new Vector3(0f, 900f, 0f), new Vector3(40f, 0f, 30f), seed))
            {
                channels++;
                var e = LightningFx.Strip(c, eye, 1f);
                float widest = e.Max(p => p.left.DistanceTo(p.right));
                bool ends = e[0].left.DistanceTo(e[0].right) < 0.05f * widest && e[^1].left.DistanceTo(e[^1].right) < 0.05f * widest;
                bool untwisted = true;
                for (int i = 1; i < e.Length; i++)
                    if ((e[i].right - e[i].left).Dot(e[i - 1].right - e[i - 1].left) < 0f) untwisted = false;
                if (!ends || !untwisted || e.Length < 3) bad++;
            }
        Result(bad == 0, $"no bolt has rungs: {channels} channels of 20 bolts are each one unbroken strip, untwisted, closed to a point at both ends ({bad} not)");
    }

    void Result(bool ok, string what) => GD.Print($"{(ok ? "PASS" : "FAIL")}  {what}");

    /// <summary>What was last drawn, shrunk to 192 wide (the frame's brightness by region, cheaply).</summary>
    Image Shrunk()
    {
        Image i = _game.GetViewport().GetTexture().GetImage();
        i.Resize(192, Math.Max(1, 192 * i.GetHeight() / i.GetWidth()), Image.Interpolation.Bilinear);
        return i;
    }

    /// <summary>The mean change in brightness from one frame to another over two regions (by their place across and down the frame, 0..1).</summary>
    static (float, float) Difference(Image a, Image b, Func<float, float, bool> one, Func<float, float, bool> two)
    {
        double s1 = 0, s2 = 0;
        int n1 = 0, n2 = 0;
        for (int y = 0; y < a.GetHeight(); y++)
        for (int x = 0; x < a.GetWidth(); x++)
        {
            float u = (x + 0.5f) / a.GetWidth(), v = (y + 0.5f) / a.GetHeight();
            float d = b.GetPixel(x, y).Luminance - a.GetPixel(x, y).Luminance;
            if (one(u, v)) { s1 += d; n1++; }
            if (two(u, v)) { s2 += d; n2++; }
        }
        return ((float)(s1 / Math.Max(n1, 1)), (float)(s2 / Math.Max(n2, 1)));
    }
}
