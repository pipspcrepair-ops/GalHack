using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics.Engine;
using Godot;
using Slice.Arms;
using Slice.Surface;
using Wildlife;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

public sealed partial class Staging
{
    /// <summary>
    /// --view=fieldfight [--out=FILE] [--minspec=1]: machines on bands of the world's people at 60 to 800 m ahead, by day.
    /// Every shot: is its shooter drawn, its target drawn, and does it leave the drawn muzzle (never the feet)? Then what drawing
    /// them costs, A/B against drawing them only as far as other life (GPU, render CPU and main thread, ms). STAGING: the
    /// patch is laid out to 900 m so it holds the far fights, then drawn as the owner's 400 m spawn distance draws life.
    /// </summary>
    IEnumerable<int> FieldFightView()
    {
        bool minspec = _args.ContainsKey("minspec");
        var s = _game.Settings;
        if (minspec)
        {
            Process.GetCurrentProcess().ProcessorAffinity = 0x55;
            (s.Shadows, s.GroundDetail, s.DetailDistance, s.TreeDistance, s.ShadowDistance) = (PlanetSurface.Skies.ShadowLevel.Low, 3, 0f, 0f, 0f);
        }
        s.SpawnDistance = 900f;
        s.Changed();
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        foreach (int x in _drive.UntilSettled()) yield return x;
        Vector3 stand = FlatSpot(_game.Survivor.WorldFeet, 120f);
        _game.Survivor.PlaceOutside((stand + Vector3.Up * 0.2f).ToNumerics());
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in Daylight()) yield return x;
        WildlifePatch patch = _game.Zone.Wildlife;
        foreach (int x in Settled(patch)) yield return x;
        for (int i = 0; i < 10; i++) yield return 0;
        // The owner's draw: life to 400 m (the patch keeps its 900 m layout).
        patch.View.FarDistance = PersonView.FarRange = 400f;
        patch.View.MidDistance = patch.View.AmbientDistance = 180f;
        patch.Frozen = false;
        _game.Visor.Visible = false;
        double max = _game.Suit.Sim.Data.Vitals.Health.Max;
        Vector3 feet = _game.Survivor.WorldFeet, fwd = Openest(feet), side = fwd.Cross(Vector3.Up).Normalized();
        Clear(patch, feet, fwd, 60f);

        // A fight at each range, a little to one side or the other: a machine on two raiders 24 m across from it, set on each other.
        float[] ranges = { 60f, 200f, 350f, 500f, 650f, 800f };
        int era = _game.Zone.Wildlife.Life.World.Now;
        var fights = new List<(float Range, Creature Robot, List<Creature> Band)>();
        for (int k = 0; k < ranges.Length; k++)
        {
            float r = ranges[k];
            Vector3 mid = feet + fwd * r + side * (k % 2 == 0 ? 1f : -1f) * r * 0.08f;
            Species robot = Robots.Roll(patch.Life.World, 3 + 2 * k, 2);
            Creature m = patch.Sim.Spawn(robot, patch.PatchLocal(Ground(mid + side * 12f)));
            var band = new List<Creature>();
            for (int i = 0; i < 2; i++)
            {
                Species p = patch.Life.People.Person(Wildlife.Rng.Mix(patch.Life.World.Seed, 4400 + (ulong)(k * 4 + i)), PersonRole.Raider, Occupancy.Raider);
                p.Figure.Prop = Shooting(era, p.Seed);
                Creature c = patch.Sim.Spawn(p, patch.PatchLocal(Ground(mid - side * 12f + fwd * (i * 3f))));
                c.Nerve = 1f;
                band.Add(c);
            }
            patch.Sim.Damage(m, 1f, band[0].AsTarget);
            foreach (Creature c in band) patch.Sim.Damage(c, 1f, m.AsTarget);
            fights.Add((r, m, band));
            GD.Print($"(STAGING: at {r:F0} m a {robot.CommonName} ({robot.Mount}) set on two raiders ({string.Join(", ", band.Select(c => c.Species.Figure.Prop))}))");
        }
        var rangeOf = new Dictionary<Creature, float>();
        foreach (var f in fights) foreach (Creature c in f.Band.Append(f.Robot)) rangeOf[c] = f.Range;

        // Every shot by them: drawn, at the drawn muzzle, well up off the drawn feet.
        var shots = new Dictionary<float, (int N, int Drawn, int TargetDrawn, float Off, float Low)>();
        foreach (float r in ranges) shots[r] = (0, 0, 0, 0f, float.MaxValue);
        void Fired(object who, Vector3 muzzle)
        {
            if (who is not Creature c || !rangeOf.TryGetValue(c, out float r)) return;
            SimArms.Kit k = _game.Arms.People.KitOf(c);
            bool drawn = patch.View.Drawn(c) && k?.Model != null && k.Model.Muzzle.IsVisibleInTree();
            float off = drawn ? muzzle.DistanceTo(k.Model.Muzzle.GlobalPosition) : 0f;
            float low = muzzle.Y - (patch.WorldOf(c).Y + patch.View.DrawnLift(c));
            bool target = k?.Target is CreatureTarget t && patch.View.Drawn(t.Creature);
            var was = shots[r];
            shots[r] = (was.N + 1, was.Drawn + (drawn ? 1 : 0), was.TargetDrawn + (target ? 1 : 0), MathF.Max(was.Off, off), MathF.Min(was.Low, low));
        }
        _game.Arms.OtherFired += Fired;
        foreach (int x in _drive.LookAt(feet + fwd * 400f + Vector3.Up * 2f, 20)) yield return x;
        for (int i = 0; i < 60 * 30 && shots.Values.Count(v => v.N >= 3) < ranges.Length; i++)
        {
            _game.Suit.Vitals.Health = max;
            yield return 0;
        }
        foreach (float r in ranges)
        {
            var v = shots[r];
            GD.Print($"  {(v.N > 0 && v.Drawn == v.N && v.TargetDrawn == v.N && v.Off < 0.05f && v.Low > 0.5f ? "ok   " : "FAIL ")} at {r:F0} m: {v.N} shots, shooter drawn {v.Drawn}, target drawn {v.TargetDrawn}, from the drawn muzzle within {v.Off * 100f:F1} cm, the lowest {(v.N > 0 ? v.Low : 0f):F2} m over the drawn feet");
        }
        foreach (var f in fights)
            GD.Print($"  (at {f.Range:F0} m: the machine {(f.Robot.Dead ? "down" : f.Robot.State.ToString())} drawn {patch.View.Drawn(f.Robot)}; raiders {string.Join(", ", f.Band.Select(c => $"{(c.Dead ? "down" : c.State.ToString())} drawn {patch.View.Drawn(c)}"))})");

        // The far ones, framed: the 350 and 500 m fights.
        Vector3 look = patch.WorldOf(fights[3].Robot).Lerp(patch.WorldOf(fights[3].Band[0]), 0.5f);
        foreach (int x in _drive.LookAt(look + Vector3.Up * 1f, 12)) yield return x;
        var zoomed = _game.Zone.Camera.Fov;
        _game.Zone.Camera.Fov = 8f; // STAGING: zoomed in, as through a scope, so 500 m off reads in the frame
        for (int i = 0; i < 6; i++) yield return 0;
        foreach (int x in Shot(_args.GetValueOrDefault("out", "fieldfight.png"))) yield return x;
        _game.Zone.Camera.Fov = zoomed;

        // The cost: alternated passes, drawn out in the field and not (as before), three each.
        foreach (int x in _drive.LookAt(feet + fwd * 400f + Vector3.Up * 2f, 12)) yield return x;
        Rid vp = _game.GetViewport().GetViewportRid();
        RenderingServer.ViewportSetMeasureRenderTime(vp, true);
        var got = new (double Gpu, double Cpu, double Main, double View, double Worst)[2];
        for (int pass = -1; pass < 6; pass++)
        {
            WildlifeView.FieldDrawn = pass % 2 == 0;
            for (int i = 0; i < 40; i++) { _game.Suit.Vitals.Health = max; yield return 0; }
            double gpu = 0, cpu = 0, main = 0, view = patch.ViewMs, worst = 0;
            for (int i = 0; i < 120; i++)
            {
                _game.Suit.Vitals.Health = max;
                yield return 0;
                gpu += RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp);
                cpu += RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp);
                double m = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
                main += m;
                worst = Math.Max(worst, m);
            }
            if (pass < 0) continue;
            var was = got[pass % 2];
            got[pass % 2] = (was.Gpu + gpu / 360, was.Cpu + cpu / 360, was.Main + main / 360, was.View + (patch.ViewMs - view) / 360, Math.Max(was.Worst, worst));
        }
        WildlifeView.FieldDrawn = true;
        string tag = minspec ? "minimum spec" : "this machine";
        GD.Print($"(field fight cost, {tag}, {patch.View.DrawnCount} bodies drawn: drawn far GPU {got[0].Gpu:F2} ms, render CPU {got[0].Cpu:F2}, main {got[0].Main:F2} (worst {got[0].Worst:F2}), creature views {got[0].View:F3}; as before GPU {got[1].Gpu:F2}, render CPU {got[1].Cpu:F2}, main {got[1].Main:F2} (worst {got[1].Worst:F2}), creature views {got[1].View:F3})");
        _game.Arms.OtherFired -= Fired;
    }
}
