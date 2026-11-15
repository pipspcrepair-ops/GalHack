using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PlanetSurface;
using PlanetSurface.Skies;
using Slice.Game;
using Slice.Items;
using Slice.Orbit;
using Slice.Surface;
using Slice.Ui;
using Slice.Vessels;
using Weather.Model;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// To the moon and down, headless, by the game's own controls: the sleeper builds the Skiff, flies
/// it up out of the atmosphere, on out of high orbit to the start world's thruster moon (Empyrion's
/// distances: a minute or two at a small vessel's top speed), the moon taking over as the frame it
/// flies in on the way, descends through the moon's own crossing onto its generated surface, and
/// lands there on its gear. No loading screen anywhere: every crossing is the game's seamless one.
///
/// It checks the system model's promise (a moon within thruster reach, a vacuum world), the flight
/// readout (no world altitude in open space: speed and the nearest body), that the moon never
/// vanishes on the way (its far look hands over to its own globe, the two painted alike), the frame
/// hand-over, the crossing's mapping, the landing, the moon's world (its gravity, no air or weather,
/// its sky the system seen from it), a stride on it stopping as on a planet, its markers its own,
/// the climb back off it, the frame handed back to the world, and frame times on the way out. Then
/// the same down onto a second moon: every moon you can fly to has a surface of its own.
///
/// Setting up is the one shortcut, printed when it happens: the Skiff's parts are handed over and
/// the tech tree opened; and the cruise out to the second moon is skipped (the Skiff set inside its
/// reach). Fresh world every run.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/MoonTest.tscn
/// </summary>
public partial class MoonTest : Node
{
    const int WatchdogMinutes = 26;
    Game.Game _game;
    Driver _drive;
    Pilot _pilot;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Vessel _skiff;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            System.Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void Fail(string what) => Check(false, what);



    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Fresh()) };
        AddChild(_game);
        _game.Spaceflight.LedgerFolder = System.IO.Path.Combine(OS.GetUserDataDir(), "moon-test-zones");
        _drive = new Driver(_game);
        _pilot = new Pilot(_game, _drive);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        SurfaceZone zone = _game.Zone;
        zone.Weather.Bridge.Calm = true;
        zone.Weather.Sim.Snap(WeatherKind.Clear);
        zone.Wildlife.Frozen = true;
        GD.Print($"world: {_game.World}");

        TheSystem();
        Light();
        foreach (int x in BuildSkiff()) yield return x;
        if (_skiff == null) yield break;
        foreach (int x in ClimbOut()) yield return x;
        if (!_game.InOrbit) yield break;
        Celestial moon = null;
        foreach (int x in ToTheMoon()) yield return x;
        if (_game.Orbit.Anchor != (moon = _game.Orbit.Moon)) yield break;
        foreach (int x in DownOnto(moon)) yield return x;
        if (_game.Survivor.Seated != _skiff) yield break;
        foreach (int x in BackUp()) yield return x;
        if (_game.Orbit.Anchor != _game.Orbit.Home) yield break;
        Celestial next = null;
        foreach (int x in ToAnotherMoon(c => next = c)) yield return x;
        if (next != null && _game.Orbit.Anchor == next) foreach (int x in DownOnto(next)) yield return x;
    }

    /// <summary>Down through a moon's crossing, landed on its gear, out across its ground and back in the seat.</summary>
    IEnumerable<int> DownOnto(Celestial moon)
    {
        foreach (int x in Descent(moon)) yield return x;
        if (_game.Zone != moon.Surface) yield break;
        foreach (int x in Landing(moon)) yield return x;
        foreach (int x in StepOut(moon)) yield return x;
    }

    // ---- The system's promise -------------------------------------------------------------------

    void TheSystem()
    {
        GD.Print("-- the moon in the system --");
        var model = _game.Surface.Planet.StarSystem;
        var moon = model.FuelMoon;
        double centre = (model.Position(moon, 0) - model.Position(model.Home, 0)).Length;
        double home = model.Home.Radius;
        double gap = centre - home - (_game.Surface.Physics.Zone.Ceiling + OrbitRules.Shared.Crossing.Band) - moon.Radius;
        Check(model.StartFault() == null, $"a start system: {model.StartFault() ?? "everything it promises"}");
        Check(centre <= home * PlanetSurface.Skies.StarSystem.ThrusterMoonTo * 1.01 && gap is > 6000 and < 14000,
            $"the thruster moon is {moon.Name}, {centre / 1000:F1} km from the world's centre ({centre / home:F1} radii): {gap / 1000:F1} km of open space from the top of the climb to its ground, Empyrion's 8.3 to 12.3 km");
        Check(moon.Spec != null && moon.Spec.Airless, $"it is a vacuum world you can land on: {moon.Spec}");
        // Over its turning world it nearly holds still (it goes round the way the world turns), so a small vessel catches it.
        double day = _game.Surface.Planet.Sky.DayMinutes * 60.0, worstSpeed = 0;
        PlanetSurface.Skies.D3 Over(double t) => model.Orient(t).ToGlobe(model.Position(moon, t) - model.Position(model.Home, t));
        for (double t = 0; t < 2; t += 0.05) worstSpeed = Math.Max(worstSpeed, (Over(t + 0.001) - Over(t)).Length / (0.001 * day));
        Check(worstSpeed < 65, $"over the turning world it moves at {worstSpeed:F0} m/s at most, under half a small vessel's 130, so it is caught in a short flight (a {day / 60:F0} minute day)");
    }

    // ---- Its light -----------------------------------------------------------------------------

    /// <summary>
    /// The bodies in the sky are lit by the true sun, the light that lights their globes up close, so
    /// their phases are right; and a body in another's shadow darkens point by point: an umbra with a
    /// soft penumbra, found on the system's own clock (the moon if it has one soon, else any body).
    /// </summary>
    void Light()
    {
        GD.Print("-- the moon's light --");
        SkyRig sky = _game.Surface.Sky;
        StarSystem m = sky.Model;
        var mat = (ShaderMaterial)sky.Environment.Sky.SkyMaterial;
        double now = sky.Days;
        sky.Paused = true;
        sky.Track();
        int count = (int)mat.GetShaderParameter("moon_count"), near = 0;
        Vector3[] dirs = mat.GetShaderParameter("moon_dir").AsVector3Array(), lights = mat.GetShaderParameter("moon_light").AsVector3Array();
        float worst = 0f, most = 0f;
        for (int i = 0; i < count; i++)
        {
            Orbiter b = sky.Drawn.First(d => d.Direction.AngleTo(dirs[i]) < 1e-4f).Body;
            double apart = (m.Position(b, now) - m.Position(m.Home, now)).Length;
            if (apart > 1e6) continue; // a world round the star sees it from elsewhere
            near++;
            // Off ours by no more than the star's parallax between here and there (it is a few thousand km off).
            float parallax = Mathf.RadToDeg((float)Math.Asin((apart + m.Home.Radius) / (m.Position(m.Star, now) - m.Position(m.Home, now)).Length));
            float off = Mathf.RadToDeg(lights[i].AngleTo(sky.ToSun));
            if (off - parallax > worst - most || near == 1) (worst, most) = (off, parallax);
        }
        Check(near > 0 && worst <= most + 0.05f, $"the moons in the sky are lit from the sun's true direction, the one the scene lights their globes by: {near} of {count} bodies near, the worst {worst:F2} degrees off ours, within the star's parallax from there ({most:F2})");

        // An eclipse on the clock: the moon's, or the first body's any shadow covers.
        var bodies = new List<Orbiter> { m.FuelMoon };
        bodies.AddRange(sky.Drawn.Select(d => d.Body).Where(b => b != m.FuelMoon && b.Kind != OrbiterKind.GasGiant));
        Orbiter dark = null;
        double full = -1, partial = -1;
        double Covered(Orbiter b, double t) => m.EclipseAt(t, m.Position(b, t), b).Covered;
        foreach (Orbiter b in bodies)
        {
            double t = now;
            while (t < now + 40.0 && Covered(b, t) > 0.01) t += 0.001; // out of any shadow it is in now, first
            for (; t < now + 40.0 && full < 0; t += 0.001)
            {
                double covered = Covered(b, t);
                if (covered > 0.4 && partial < 0) partial = t;
                if (covered > 0.98) full = t;
            }
            if (full >= 0) { dark = b; break; }
            partial = -1;
        }
        if (dark == null)
        {
            Fail("no body in the sky passes through a shadow in 40 days");
            sky.Days = now;
            sky.Paused = false;
            return;
        }
        // Its deepest: the middle of the time it is all in shadow (its first moment is the umbra's very edge).
        double over = full;
        while (over < full + 1.0 && Covered(dark, over + 0.001) > 0.98) over += 0.001;
        double deep = (full + over) / 2;
        (double Mid, double Centre, double Least, double Most) Across(double t)
        {
            sky.Days = t;
            sky.Track();
            Vector3[] d = mat.GetShaderParameter("moon_dir").AsVector3Array(), l = mat.GetShaderParameter("moon_light").AsVector3Array();
            Vector4[] c = mat.GetShaderParameter("moon_caster").AsVector4Array();
            float[] s = mat.GetShaderParameter("moon_sun").AsFloat32Array();
            int i = Array.FindIndex(d, x => x.AngleTo(sky.Drawn.First(e => e.Body == dark).Direction) < 1e-4f);
            // Over the disc as the shader lays it: the points of the face toward the eye.
            Vector3 east = d[i].Cross(MathF.Abs(d[i].Y) > 0.95f ? Vector3.Right : Vector3.Up).Normalized(), north = d[i].Cross(east);
            double least = 1, most = 0;
            for (float x = -0.9f; x <= 0.9f; x += 0.15f)
                for (float y = -0.9f; y <= 0.9f; y += 0.15f)
                {
                    if (x * x + y * y > 0.81f) continue;
                    double h = Shadowed(east * x + north * y - d[i] * MathF.Sqrt(1f - x * x - y * y), l[i], c[i], s[i]);
                    (least, most) = (Math.Min(least, h), Math.Max(most, h));
                }
            return (Shadowed(-d[i], l[i], c[i], s[i]), Shadowed(Vector3.Zero, l[i], c[i], s[i]), least, most);
        }
        var inside = Across(deep);
        var edge = Across(partial);
        // The shader's sums at the body's centre are the system's own eclipse there; across the disc the shadow's edge shows as
        // it crosses (how sharp, by the star's size from there against the body's: a far caster's penumbra can be wider than it).
        double offDeep = Math.Abs(inside.Centre - Covered(dark, deep)), offEdge = Math.Abs(edge.Centre - Covered(dark, partial));
        Check(inside.Mid > 0.9 && edge.Most - edge.Least > 0.3 && offDeep < 0.03 && offEdge < 0.03,
            $"{Charts.Tidy(dark.Name)} goes dark in {Charts.Tidy(m.EclipseAt(deep, m.Position(dark, deep), dark).By?.Name ?? "nothing")}'s shadow {deep - now:F2} days on, drawn point by point: {inside.Mid:P0} of the sun hidden at its middle at the deepest; as the edge crosses it, {edge.Least:P0} to {edge.Most:P0} across the disc (umbra and penumbra); at its centre the sky's shadow and the system's agree within {Math.Max(offDeep, offEdge):P1}");
        sky.Days = now;
        sky.Paused = false;
        sky.Track();
        GD.Print($"  (tracking the ground's sky: {TrackCost(sky.Track)})");
    }

    static PlanetSurface.Skies.D3 D3Of(N.Vector3 q) => new(q.X, q.Y, q.Z);

    /// <summary>What a call costs on the main thread: its mean and worst ms over 300 calls, and what it allocates.</summary>
    static string TrackCost(Action track)
    {
        track();
        long bytes = GC.GetAllocatedBytesForCurrentThread();
        double total = 0, worst = 0;
        for (int i = 0; i < 300; i++)
        {
            long at = Stopwatch.GetTimestamp();
            track();
            double ms = Stopwatch.GetElapsedTime(at).TotalMilliseconds;
            (total, worst) = (total + ms, Math.Max(worst, ms));
        }
        return $"{total / 300:F3} ms a call, worst {worst:F3} ms, {(GC.GetAllocatedBytesForCurrentThread() - bytes) / 300 / 1024.0:F1} KB allocated";
    }

    /// <summary>The sky shader's own shadow on a point of a body (sky.gdshader's shadowed()): the share of the sun its caster hides there.</summary>
    static double Shadowed(Vector3 g, Vector3 toSun, Vector4 caster, float sun)
    {
        Vector3 v = new Vector3(caster.X, caster.Y, caster.Z) - g;
        if (caster.W <= 0f || v.Dot(toSun) <= 0f) return 0.0;
        return StarSystem.Covered(sun, Math.Asin(Math.Min(caster.W / v.Length(), 1f)), Math.Atan2(v.Cross(toSun).Length(), v.Dot(toSun)));
    }

    // ---- Setting up ----------------------------------------------------------------------------

    IEnumerable<int> BuildSkiff()
    {
        GD.Print("-- the Skiff --");
        foreach (int x in _pilot.BuildSkiff(s => _skiff = s)) yield return x;
        AirCells.Give(_game);
        Check(_skiff?.Structure.Count == _game.StarterPlan.Count, $"the Skiff is built: {_skiff?.Structure.Count} of {_game.StarterPlan.Count} blocks");
        if (_skiff?.Structure.Count != _game.StarterPlan.Count) _skiff = null;
    }

    // ---- Up and out ----------------------------------------------------------------------------

    IEnumerable<int> ClimbOut()
    {
        GD.Print("-- up out of the atmosphere --");
        Vessel v = _skiff;
        foreach (int x in Stride((slid, speed) => _worldStride = slid)) yield return x;
        GD.Print($"  (on the world a stride let go at a walk carries on {_worldStride:F2} m)");
        foreach (int x in _pilot.Board(v)) yield return x;
        Check(_game.Survivor.Seated == v, "at the Skiff's controls");
        if (_game.Survivor.Seated != v) yield break;
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        Check(_game.InOrbit, "it crossed into orbit");
        // On up through the band, the readout the world's altitude the whole way.
        bool altInBand = true;
        float bandTop = _game.Surface.Physics.Zone.Ceiling + OrbitRules.Shared.Crossing.Band;
        for (int i = 0; i < 60 * 60 && _game.InOrbit && _game.Spaceflight.Band < 1f; i++)
        {
            if (_game.Orbit.Home.Height(v.Body.Position.ToGodot()) < bandTop - 5f) altInBand &= _game.Spaceflight.FlightLine(v).Contains("ALT");
            yield return 0;
        }
        for (int i = 0; i < 60 * 8; i++) yield return 0; // well clear of the top
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        Check(altInBand, "near the world the flight readout gives its altitude");
        string line = _game.Spaceflight.FlightLine(v);
        Check(!line.Contains("ALT") && line.Contains("m/s"), $"over the band's top it gives no world altitude, only speed and the nearest body: '{line}'");
    }

    // ---- Out to the moon -----------------------------------------------------------------------

    IEnumerable<int> ToTheMoon()
    {
        GD.Print("-- out to the moon --");
        Vessel v = _skiff;
        OrbitZone orbit = _game.Orbit;
        for (int i = 0; i < 60 * 180 && (orbit.Moon?.Ready != true || _game.Spaceflight.MoonSurface?.Made == null); i++) yield return 0;
        Celestial moon = orbit.Moon;
        Check(moon?.Ready == true && _game.Spaceflight.MoonSurface?.Made != null, $"the moon's surface was made off the main thread while you played, and its globe painted from it ({_clock.Elapsed.TotalSeconds:F0} s in)");
        if (moon?.Ready != true) yield break;
        EveryMoon();
        var farLook = System.Threading.Tasks.Task.Run(() => BodySurfaces.Paint(moon.Body)); // as the skies paint it from afar
        // Over the moon's near side, a little over the top of its band.
        var (tx, tz) = moon.Map.MapPoint(moon.ToGlobe(v.Body.Position));
        float over = moon.Ceiling + orbit.Rules.Crossing.Band + 400f;
        Vector3 Above() => moon.ToWorld(moon.Map.Direction(tx, tz) * moon.RadiusFor(over)).ToGodot();
        float start = v.Body.Position.ToGodot().DistanceTo(Above());
        GD.Print($"  the moon is {start / 1000:F1} km off ({moon.Name}: radius {moon.GlobeRadius:F0} m)");
        // Solid from afar, ground or no ground: a ray from the Skiff meets each body at its true radius.
        void Solid(string when)
        {
            Vector3 from = v.Body.Position.ToGodot();
            foreach (Celestial c in orbit.Bodies)
            {
                var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, c.Centre, GalHack.Physics.Layers.World));
                float want = from.DistanceTo(c.Centre) - c.GlobeRadius, got = hit.Count > 0 ? from.DistanceTo((Vector3)hit["position"]) : float.NaN;
                Celestial first = hit.Count > 0 && hit["collider"].AsGodotObject() is Node n ? orbit.Bodies.FirstOrDefault(o => o.Sphere == n.GetParent()) : null;
                if (first != null && first != c) GD.Print($"  ({when}: {c.Name} stands behind {first.Name}, which the ray meets first)");
                else Check(first == c && MathF.Abs(got - want) < 2f + (c.Shift - c.ShiftWas).Length(), $"{when}: {c.Name} is solid at its true radius, its ground {(c.Surface?.Arrived == true ? "there" : "not there yet")} (a ray from the Skiff meets it {got:F0} m out, {want:F0} m expected, within the {(c.Shift - c.ShiftWas).Length():F0} m it goes in a tick)");
            }
            // The bodies near with no ground here (other moons, a giant and its moons): solid too, unless another stands in front.
            int met = 0, hidden = 0;
            foreach (var (body, shell) in orbit.OtherSolids)
            {
                Vector3 at = shell.GlobalPosition;
                var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, at, GalHack.Physics.Layers.World));
                if (hit.Count > 0 && hit["collider"].AsGodotObject() != shell) hidden++;
                else if (hit.Count > 0 && MathF.Abs(from.DistanceTo((Vector3)hit["position"]) - (from.DistanceTo(at) - (float)body.Radius)) < 6f) met++; // it moves on between ticks
            }
            Check(met + hidden == orbit.OtherSolids.Count, $"{when}: the {orbit.OtherSolids.Count} other bodies near with no ground here are solid at their true radius ({met} met by a ray, {hidden} behind another)");
        }
        Solid("from high orbit");
        var probe = new FrameProbe(this);
        var frames = new List<double>();
        var cpu = new List<double>();
        bool shown = true, nearestMoon = false;
        string lost = null;
        int handedOverAt = -1, tick = 0, worstAt = 0, sized = 0;
        float sizeOff = 0f;
        string sizeWhat = "";
        double worstCpu = 0;
        _game.Spaceflight.WorstSeamMs = _game.Spaceflight.WorstSkyMs = _game.Spaceflight.SkyMs = 0;
        _game.Spaceflight.SkyFrames = 0;
        float topSpeed = 0f;
        var watch = Stopwatch.StartNew();
        IEnumerable<int> Route()
        {
            foreach (int x in TowardTheMoon(_pilot, orbit, v)) yield return x;
            foreach (int x in _pilot.FlyTo(v, Above, 30f, 400f)) yield return x;
        }
        IEnumerator<int> fly = Route().GetEnumerator();
        while (fly.MoveNext())
        {
            tick++;
            frames.Add(probe.LastMs);
            cpu.Add(probe.LastCpuMs);
            if (probe.LastCpuMs > worstCpu) (worstCpu, worstAt) = (probe.LastCpuMs, tick);
            topSpeed = MathF.Max(topSpeed, v.Body.Velocity.Length());
            if (!_game.Spaceflight.MoonInView(out string why) && lost == null) { shown = false; lost = why; }
            foreach (Celestial c in orbit.Bodies)
            {
                float off = SizeOff(_game, c, out string what);
                if (off > sizeOff) (sizeOff, sizeWhat) = (off, $"{c.Name}: {what}, {tick / 60f:F1} s out");
                sized++;
            }
            nearestMoon |= _game.Spaceflight.FlightLine(v).Contains(moon.Name.ToUpperInvariant());
            if (handedOverAt < 0 && orbit.Anchor == moon)
            {
                handedOverAt = tick;
                Solid("as the moon took over");
            }
            if (tick % 1200 == 0)
                GD.Print($"  ({tick / 60} s: {v.Body.Position.ToGodot().DistanceTo(Above()):F0} m to go at {v.Body.Velocity.Length():F0} m/s, {moon.Height(v.Body.Position.ToGodot()):F0} m over the moon, frame {orbit.Anchor.Name}, band {_game.Spaceflight.Band:F2}, aligned {_game.Spaceflight.Aligned}, in {_game.Zone.Name}, cpu {probe.LastCpuMs:F1} ms)");
            yield return fly.Current;
        }
        double seconds = watch.Elapsed.TotalSeconds;
        probe.Remove();
        // Judged on the main thread's own CPU time: the machine is shared with other runs, and their load shows in wall time only.
        static (double P95, double Worst) Spread(List<double> ms)
        {
            ms.Sort();
            return ms.Count == 0 ? (0, 0) : (ms[(int)(ms.Count * 0.95)], ms[^1]);
        }
        var (p95, worst) = Spread(cpu);
        var (wallP95, wallWorst) = Spread(frames);
        GD.Print($"  frame times on the way, CPU: 95th {p95:F1} ms, worst {worst:F1} ms at {worstAt / 60f:F0} s; wall: 95th {wallP95:F1} ms, worst {wallWorst:F1} ms; {frames.Count} frames, headless, machine shared");
        Spaceflight sf = _game.Spaceflight;
        GD.Print($"  (tracking the skies in orbit: {sf.SkyMs / Math.Max(sf.SkyFrames, 1):F3} ms a frame on average, worst {sf.WorstSkyMs:F2} ms, over {sf.SkyFrames} frames; space's sky alone {TrackCost(() => orbit.Space.Track(_game.Surface.Planet.StarSystem, sf.Below.Sky.Days, orbit.Home.Turn.ToBasis(), D3Of(orbit.Home.ToGlobe(v.Body.Position)), v.Body.Position.ToGodot()))})");
        GD.Print($"  (the seam and skies took at most {_game.Spaceflight.WorstSeamMs:F1} ms of a frame: {_game.Spaceflight.WorstSeamParts}; the moon arrived a piece a frame: {_game.Spaceflight.MoonSurface.ArriveSteps})");
        Check(v.Body.Position.ToGodot().DistanceTo(Above()) < 40f, $"flew {start / 1000:F1} km from high orbit to over the moon in {tick / 60f:F0} s of game time at up to {topSpeed:F0} m/s ({v.Body.Position.ToGodot().DistanceTo(Above()):F0} m off the point)");
        Check(handedOverAt > 0 && orbit.Anchor == moon, $"the moon took over as the frame on the way ({handedOverAt / 60f:F0} s out), so its ground waits where you see it");
        Check(shown, $"the moon was in view every frame of the way, its far look handing over to its own globe ({lost ?? "never lost"})");
        Check(sized > 0 && sizeOff < 0.01f, $"every body was drawn at its true angular size every frame of the way, within 1%, so nothing pops ({sized} looks; the worst {sizeOff:P2} off{(sizeWhat == "" ? "" : ": " + sizeWhat)})");
        Check(nearestMoon, "the readout named the moon as the nearest body as it came near");
        Check(_game.Spaceflight.MoonSurface.Arrived, "its surface arrived ahead of the descent");
        Check(p95 < 16.7 && worst < 33.4, $"the flight keeps the frame budget in CPU time: 95th percentile {p95:F1} ms (budget 16.7), worst {worst:F1} ms (no frame dropped twice)");
        PaintedAlike(moon, farLook.Result);
    }

    /// <summary>Every moon you can fly to is a body of the orbit zone with a surface of its own, from its own spec.</summary>
    void EveryMoon()
    {
        StarSystem model = _game.Surface.Planet.StarSystem;
        int inReach = model.Bodies.Count(b => b.Kind == OrbiterKind.Moon && b != model.Home && (b.Parent == model.Home || b.Parent == model.Neighbour));
        List<Celestial> moons = _game.Orbit.Moons.ToList();
        string Of(Celestial c) => $"{c.Name}: {c.Body.Spec.Archetype}{(c.Body.Spec.Airless ? ", cratered, no air" : ", air")}, {c.Body.Spec.WidthKm} km, {c.Surface.Physics.Zone.Strength:F1} m/s²{(c.Surface.Making ? "" : ", not made yet")}";
        Check(moons.Count == inReach && moons[0] == _game.Orbit.Moon && moons.All(c => c.Surface is { IsOtherBody: true } s && s.Spec == c.Body.Spec && s.MakeLater == (c != moons[0])),
            $"every moon you can fly to is a body here with a surface of its own from its own spec, the thruster moon's made from the start, every other one's as you come near ({moons.Count} of {inReach}: {string.Join("; ", moons.Select(Of))})");
    }

    /// <summary>
    /// A moon's far look (as the skies paint it from afar) and its globe (painted from its own surface's map) agree in
    /// brightness and colour, so nothing pops as one hands over to the other: their mean colours over the sphere. Both
    /// are lit alike (the star's light and the dark round it, Burley's diffuse), so this is what is left to differ.
    /// </summary>
    void PaintedAlike(Celestial moon, Image far)
    {
        if (!moon.Body.Spec.Airless)
        {
            GD.Print($"  ({moon.Name} has air: its far look carries its clouds painted in, its globe draws them as a layer of their own, so the ground's paint is not compared)");
            return;
        }
        Vector3 a = Mean(far), b = Mean(moon.Painting.Result.Albedo);
        static float Lum(Vector3 c) => 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
        float bright = Lum(b) / Lum(a);
        Vector3 tint = b / Lum(b) - a / Lum(a);
        float off = MathF.Max(MathF.Abs(tint.X), MathF.Max(MathF.Abs(tint.Y), MathF.Abs(tint.Z)));
        Check(MathF.Abs(bright - 1f) < 0.06f && off < 0.06f,
            $"{moon.Name}'s far look and its globe are painted alike: the globe {bright:P0} as bright, its colour {off:P1} off (linear means: far {a.X:F3} {a.Y:F3} {a.Z:F3}, globe {b.X:F3} {b.Y:F3} {b.Z:F3})");
    }

    /// <summary>A map image's mean linear colour over the sphere it wraps (each row by its share of the area).</summary>
    static Vector3 Mean(Image image)
    {
        int w = image.GetWidth(), h = image.GetHeight(), step = Math.Max(1, w / 256);
        Vector3 sum = Vector3.Zero;
        float weight = 0f;
        for (int y = 0; y < h; y += step)
        {
            float area = MathF.Cos(((y + 0.5f) / h - 0.5f) * MathF.PI);
            for (int x = 0; x < w; x += step)
            {
                Color c = image.GetPixel(x, y).SrgbToLinear();
                sum += new Vector3(c.R, c.G, c.B) * area;
                weight += area;
            }
        }
        return sum / weight;
    }

    /// <summary>
    /// How far the looks of a body drawn this frame stand off its true angular size, as a share (the worst of
    /// them): its own globe, and the sky's far look of it. True is its radius seen from the eye at its place in
    /// the zone, over the top of its band (under it the seam lays the land out flat, by design).
    /// </summary>
    internal static float SizeOff(Game.Game g, Celestial c, out string what)
    {
        string said = "";
        float worst = 0f;
        void Look(string name, float drawn, Vector3 eye)
        {
            float truth = MathF.Asin(MathF.Min(c.GlobeRadius / eye.DistanceTo(c.Centre), 1f)), off = MathF.Abs(drawn / truth - 1f);
            if (off > worst) (worst, said) = (off, $"{name} {Mathf.RadToDeg(drawn):F3} against {Mathf.RadToDeg(truth):F3} degrees");
        }
        if (c.Globe is { Visible: true, Hidden: false } globe && c.Height(globe.DrawnFrom) >= c.Ceiling + g.Orbit.Rules.Crossing.Band) Look("its globe", globe.Angular, globe.DrawnFrom);
        SpaceSky sky = g.Orbit.Space;
        if (!c.IsHome && sky.BodiesShown && sky.FadeOf(c.Body) > 0.002f)
            foreach (var (body, _, angular) in sky.Drawn)
                if (body == c.Body) Look("the far look", angular, sky.Eye);
        what = said;
        return worst;
    }

    /// <summary>Out over the world's limb on the moon's side first, if the world stands between: the moon may be over its far side.</summary>
    internal static IEnumerable<int> TowardTheMoon(Pilot pilot, OrbitZone orbit, Vessel v)
    {
        Celestial home = orbit.Home;
        Vector3 side = home.Centre + (orbit.Moon.Centre - home.Centre).Normalized() * (home.ShellRadius + 2500f);
        foreach (Vector3 way in OrbitTest.RoundTheGlobe(orbit.Home, v.Body.Position.ToGodot(), side))
            foreach (int x in pilot.FlyTo(v, () => way, 300f, 120f, through: true)) yield return x;
    }

    /// <summary>Turns the vessel's top to a body's sky over the point under it, nose north, so Ctrl takes it straight down.</summary>
    internal static IEnumerable<int> Upright(Pilot pilot, Celestial body, Vessel v)
    {
        var (x, z) = body.Under(v.Body.Position.ToGodot());
        N.Quaternion toWorld = N.Quaternion.Normalize(body.Turn * body.Map.Frame(x, z));
        return pilot.TurnTo(v, N.Vector3.Transform(-N.Vector3.UnitZ, toWorld).ToGodot(), N.Vector3.Transform(N.Vector3.UnitY, toWorld).ToGodot());
    }

    // ---- Down onto it --------------------------------------------------------------------------

    IEnumerable<int> Descent(Celestial moon)
    {
        GD.Print($"-- down through {moon.Name}'s crossing --");
        Vessel v = _skiff;
        var (tx, tz) = moon.Under(v.Body.Position.ToGodot());
        foreach (int x in Upright(_pilot, moon, v)) yield return x;
        int crossings = _game.Spaceflight.Crossings.Count;
        // Down at a pilot's pace: a sink rate it can still stop from over the moon's ground.
        string alt = null;
        for (int i = 0; i < 60 * 120 && _game.Spaceflight.Crossings.Count == crossings; i++)
        {
            _game.Controls.Hold(Key.Ctrl, -v.Body.Velocity.ToGodot().Dot((v.Body.Position.ToGodot() - moon.Centre).Normalized()) < 40f);
            if (alt == null && _game.InOrbit && _game.Spaceflight.Band < 0.5f) alt = $"'{_game.Spaceflight.FlightLine(v)}' at {moon.Height(v.Body.Position.ToGodot()):F0} m over the moon";
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        Check(alt != null && alt.Contains("ALT"), $"down in the moon's band the readout gives the moon's altitude: {alt}");
        SurfaceZone surface = moon.Surface;
        Check(_game.Zone == surface && v.Zone == surface && _game.Survivor.Seated == v, "down through its band it crossed onto the moon's own surface, in the seat, no loading screen");
        if (_game.Zone != surface) yield break;
        Crossing down = _game.Spaceflight.Crossings[^1];
        Check(down.PositionBefore == down.PositionAfter && down.VelocityBefore == down.VelocityAfter && down.Audit.Count == 0,
            $"carried straight across at map ({down.MapX:F0}, {down.MapZ:F0}), in {down.Ms:F1} ms, nothing lost ({string.Join("; ", down.Audit)})");
        double dx = Math.Abs(down.MapX - tx);
        dx = Math.Min(dx, moon.Map.Width - dx);
        Check(Math.Sqrt(dx * dx + (down.MapZ - tz) * (down.MapZ - tz)) < 80, "it came in over the point it was over on the moon's globe");
    }

    IEnumerable<int> Landing(Celestial moon)
    {
        GD.Print("-- landing --");
        Vessel v = _skiff;
        SurfaceZone surface = _game.Zone;
        Vector3 pad = OrbitTest.FlatSpot(surface, v.Body.Position.ToGodot(), 40f);
        float hurtBefore = v.Body.Damage;
        int t = 0;
        foreach (int x in _pilot.Land(v, pad))
        {
            if (++t % 600 == 0 && v.Controller is { } c)
                GD.Print($"  ({t / 60} s: {v.AboveGround:F1} m up, moving {v.Body.Velocity}, settling {c.Settling}, gear {c.Gear?.Attached}, ground {c.GroundDistance:F1} m under, {v.Body.Position.ToGodot().DistanceTo(pad):F0} m from the pad, asleep {v.Body.Asleep}, input {v.Flight.Input.Move} seated {v.Flight.Input.Seated} thrust {v.Flight.Input.AnyThrust}, tossed {v.Body.Tossed:F2}, in air {c.Zone.InAtmosphere} gravity {c.Zone.Gravity:F1}, in the seat {_game.Survivor.Seated == v} as {v.Roles.Pilot}, the controls' up {_game.Controls.Axis(Key.Space, Key.Ctrl)}, held {string.Join(",", new[] { Key.W, Key.A, Key.S, Key.D, Key.Space, Key.Ctrl, Key.Shift }.Where(k => _game.Controls.IsHeld(k)))})");
            yield return x;
        }
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(v.Controller?.Frozen == true && v.Body.Damage == hurtBefore && !_game.Suit.Dead, $"landed on {moon.Name} on its gear, undamaged, the sleeper alive at the controls (settled {v.Controller?.Frozen}, {v.AboveGround:F1} m up, damage {v.Body.Damage - hurtBefore:F2}, the suit's air at {_game.Suit.OxygenShare:P0})");
        float g = surface.Physics.Zone.Strength, home = _game.Surface.Physics.Zone.Strength;
        float sized = home * moon.GlobeRadius / _game.Orbit.Home.GlobeRadius;
        Check(MathF.Abs(g - sized) < 0.01f, $"its gravity comes from its size: {g:F1} m/s² on a {surface.Planet.Width / 1000:F0} km moon, the world's {home:F1} on {_game.Surface.Planet.Width / 1000:F0} km");
        if (moon.Body.Spec.Airless)
            Check(surface.Weather.Sim == null && surface.Planet.Sky.Density < SurfaceWeather.NoAir && _game.Suit.Beyond != null, "no air: no weather, a black sky, the suit on its own oxygen");
        else Check(surface.Planet.Sky.Density >= SurfaceWeather.NoAir, $"air of its own: its sky's density {surface.Planet.Sky.Density:F2}");
        var seen = surface.Planet.StarSystem;
        Check(seen.Home == moon.Body && seen.Origin == _game.Surface.Planet.StarSystem,
            "its sky is the start world's system seen from the moon: one model, one clock");
        Check(MathF.Abs((float)(surface.Sky.Days - _game.Surface.Sky.Days)) < 1e-6f, $"one clock: the moon's sky and the world's tell the same day ({surface.Sky.Days:F4})");
    }

    IEnumerable<int> StepOut(Celestial moon)
    {
        GD.Print($"-- out onto {moon.Name} --");
        Vessel v = _skiff;
        SurfaceZone surface = _game.Zone;
        if (v.Controller?.Frozen != true) yield break;
        var s = _game.Survivor;
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(20)) yield return x;
        Vector3 from = s.WorldFeet, there = v.Body.Position.ToGodot() + MathX.Rotate(v.Body.Rotation, N.Vector3.UnitX).ToGodot() * 9f;
        there.Y = surface.GroundAt(there.X, there.Z).Y;
        foreach (int x in _drive.WalkTo(there, 0.8f, 40f, sprint: false, dig: false, hop: false)) yield return x;
        foreach (int x in _drive.Wait(90)) yield return x;
        Vector3 feet = s.WorldFeet;
        float ground = surface.GroundAt(feet.X, feet.Z).Y;
        Check(s.Seated == null && s.Zone == surface && s.Body.Frame == null && feet.DistanceTo(from) > 4f && MathF.Abs(feet.Y - ground) < 0.4f,
            $"the sleeper got up out of the seat and walked {new Vector2(feet.X - from.X, feet.Z - from.Z).Length():F1} m across the moon's own ground ({feet.DistanceTo(there):F1} m off the spot, feet {feet.Y - ground:F2} m over the ground, standing on {(s.Body.Frame == null ? "the moon" : "a hull")})");
        float slid = 0f, speed = 0f, g = surface.Physics.Zone.Strength;
        foreach (int x in Stride((d, at) => (slid, speed) = (d, at))) yield return x;
        Check(slid < MathF.Max(1f, 2f * _worldStride),
            $"a stride let go stops as on a planet in {g:F1} m/s²: let go at {speed:F1} m/s it carries on {slid:F2} m ({_worldStride:F2} m on the world at {s.Legs.WalkSpeed:F1} m/s)");
        MarkedHere(moon);
        foreach (int x in _pilot.Board(v)) yield return x;
        Check(s.Seated == v, "and back into the Skiff's seat");
    }

    float _worldStride;

    /// <summary>
    /// A stride let go: a second and a half's walk away from the Skiff, the key let go with the feet on the ground, and
    /// how far it carries on (m), with the speed it was let go at (m/s).
    /// </summary>
    IEnumerable<int> Stride(Action<float, float> done)
    {
        var s = _game.Survivor;
        Vector3 feet = s.WorldFeet, away = feet - _skiff.Body.Position.ToGodot();
        away.Y = 0f;
        Vector3 to = feet + away.Normalized() * 30f;
        _game.Controls.Hold(Key.W);
        for (int i = 0; i < 150 && (i < 90 || !s.Body.OnFloor); i++)
        {
            _drive.Face(new Vector3(to.X, s.WorldEye().Y, to.Z));
            yield return 0;
        }
        _game.Controls.Hold(Key.W, false);
        Vector3 letGo = s.WorldFeet;
        float speed = new Vector2(s.Body.Velocity.X, s.Body.Velocity.Z).Length();
        var trace = new System.Text.StringBuilder();
        for (int i = 0; i < 120; i++)
        {
            yield return 0;
            if (i % 6 == 0) trace.Append($"{(s.Body.OnFloor ? "" : "^")}{new Vector2(s.Body.Velocity.X, s.Body.Velocity.Z).Length():F1} ");
        }
        float slid = new Vector2(s.WorldFeet.X - letGo.X, s.WorldFeet.Z - letGo.Z).Length();
        GD.Print($"  (a stride let go at {speed:F1} m/s carried on {slid:F2} m; its speed every tenth of a second, ^ off the ground: {trace})");
        done(slid, speed);
    }

    /// <summary>On a moon the visor marks what is the moon's own (its places and deposits found there, the Skiff): none of the start world's.</summary>
    void MarkedHere(Celestial moon)
    {
        string on = _game.Spaceflight.ChartOf(moon.Surface);
        Charts charts = _game.Spaceflight.Charts;
        HashSet<string> own = charts.Marks.Where(m => m.On == on).Select(m => m.Label.ToUpperInvariant()).ToHashSet();
        List<Visor.Marker> marks = _game.Visor.Markers();
        bool foreign = marks.Any(m => m.Kind is "bunker" or "mark" || m.Kind is "place" or "deposit" && !own.Contains(m.Tag));
        bool skiff = marks.Any(m => m.Kind == "vessel" && m.Tag == _skiff.Name.ToUpperInvariant());
        Check(!foreign && skiff, $"on {moon.Name} the visor marks its own ({string.Join(", ", marks.Select(m => $"{m.Kind} {m.Tag}"))}), none of the start world's (its bunker and {charts.Marks.Count(m => m.On == null)} finds stay on its chart): {moon.Surface.Planet.Pois.Count} places of its own, {own.Count} finds charted here");
    }

    IEnumerable<int> BackUp()
    {
        GD.Print("-- and back up off it --");
        Vessel v = _skiff;
        int crossings = _game.Spaceflight.Crossings.Count;
        _game.Controls.Hold(Key.Space);
        for (int i = 0; i < 60 * 150 && !_game.InOrbit; i++) yield return 0;
        _game.Controls.ReleaseAll();
        Check(_game.InOrbit && _game.Spaceflight.Crossings.Count == crossings + 1 && _game.Orbit.Anchor == _game.Orbit.Moon, "the climb off the moon crossed back into orbit, the moon still the frame");
        if (!_game.InOrbit) yield break;
        Check(_game.Spaceflight.Crossings[^1].Audit.Count == 0, "nothing lost or doubled on the way up");
        // On out over the band's top and back toward the world, round the moon if it stands between.
        for (int i = 0; i < 60 * 30 && _game.Spaceflight.Band < 1f; i++)
        {
            _game.Controls.Hold(Key.Space);
            yield return 0;
        }
        _game.Controls.ReleaseAll();
        foreach (int x in _pilot.Stop()) yield return x;
        OrbitZone orbit = _game.Orbit;
        Celestial home = orbit.Home, moon = orbit.Moon;
        // Over the world's band on the line to the moon: out of the moon's reach, the world the nearer of the two.
        float over = home.GlobeRadius + home.Ceiling + orbit.Rules.Crossing.Band + 600f;
        Vector3 Back() => home.Centre + (moon.Centre - home.Centre).Normalized() * over;
        foreach (Vector3 way in OrbitTest.RoundTheGlobe(moon, v.Body.Position.ToGodot(), Back()))
            foreach (int x in _pilot.FlyTo(v, () => way, 300f, 120f, through: true)) yield return x;
        foreach (int x in _pilot.FlyTo(v, Back, 300f, 300f)) yield return x;
        string line = _game.Spaceflight.FlightLine(v);
        Vector3 at = v.Body.Position.ToGodot();
        Check(orbit.Anchor == home && line.Contains("YOUR WORLD"),
            $"flown back out of the moon's reach ({at.DistanceTo(moon.Centre) - moon.Reach:F0} m past it, {home.Height(at):F0} m over the world, {moon.Height(at):F0} m over the moon), the world took the frame back and the readout names it: '{line}'");
    }

    /// <summary>
    /// A second moon, the nearest other one you can fly to. The cruise out is skipped (printed): the Skiff is set just
    /// inside its reach, as if it had flown out and waited in its path. Its surface is made as you come near it, its globe
    /// painted from that alike with its far look, it takes over as the frame, and the readout names it.
    /// </summary>
    IEnumerable<int> ToAnotherMoon(Action<Celestial> found)
    {
        GD.Print("-- out to a second moon --");
        OrbitZone orbit = _game.Orbit;
        Vessel v = _skiff;
        Vector3 at = v.Body.Position.ToGodot();
        Celestial next = orbit.Moons.Where(c => c != orbit.Moon).OrderBy(c => at.DistanceTo(c.Centre)).FirstOrDefault();
        if (next == null)
        {
            GD.Print("  (no second moon to fly to: the thruster moon is this world's only one)");
            yield break;
        }
        found(next);
        Vector3 was = next.Centre;
        foreach (int x in _drive.Wait(30)) yield return x;
        float far = at.DistanceTo(next.Centre) - next.GlobeRadius, sweep = next.Centre.DistanceTo(was) * 2f;
        GD.Print($"  ({next.Name} is {far / 1000:F0} km off, going round the turning world at {sweep:F0} m/s; its surface {(next.Surface.Making ? "being made" : "not made yet")})");
        Vector3 inside = next.Centre + (at - next.Centre).Normalized() * (next.Reach - 400f);
        GD.Print($"  (shortcut: the {far / 1000:F0} km cruise out to {next.Name} skipped: the Skiff set {next.Height(inside):F0} m over it, just inside its reach)");
        v.Body.Position = inside.ToNumerics();
        v.Body.Velocity = N.Vector3.Zero;
        v.Body.AngularVelocity = N.Vector3.Zero;
        v.Binding.Teleport();
        var watch = Stopwatch.StartNew();
        for (int i = 0; i < 60 * 300 && !(next.Ready && next.Surface.Arrived && orbit.Anchor == next); i++) yield return 0;
        Check(orbit.Anchor == next, $"{next.Name} took over as the frame");
        Check(next.Ready && next.Surface.Arrived, $"its surface was made off the main thread as you came near and its globe painted from it, in {watch.Elapsed.TotalSeconds:F0} s: {next.Surface.Made?.Spec}");
        if (!next.Ready || orbit.Anchor != next) yield break;
        PaintedAlike(next, BodySurfaces.Paint(next.Body));
        string line = _game.Spaceflight.FlightLine(v);
        Check(line.Contains(next.Name.ToUpperInvariant()), $"the readout names it: '{line}'");
        var (tx, tz) = next.Map.MapPoint(next.ToGlobe(v.Body.Position));
        Vector3 Above() => next.ToWorld(next.Map.Direction(tx, tz) * next.RadiusFor(next.Ceiling + orbit.Rules.Crossing.Band + 400f)).ToGodot();
        foreach (int x in _pilot.FlyTo(v, Above, 30f, 400f)) yield return x;
    }
}

/// <summary>
/// As a sleeper out in vacuum does on a long trip (the Skiff's cockpit is open to space): air cells in the pack (a
/// printed setup shortcut), and one used from it whenever the suit runs low. MoonTest and the moon shots.
/// </summary>
public partial class AirCells : Node
{
    Game.Game _game;

    public static void Give(Game.Game game, int cells = 6)
    {
        game.Pack.Storage.Add("air-cell", cells);
        game.AddChild(new AirCells { _game = game });
        GD.Print($"  (setup: {cells} air cells for the trip, the Skiff's cockpit being open to space)");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_game.Phase != Phase.Playing || _game.Suit.Dead || _game.Suit.OxygenShare > 0.35) return;
        int slot = _game.Pack.Grid.IndexOf("air-cell");
        if (slot >= 0 && _game.Consume(slot)) GD.Print($"  (an air cell from the pack: the suit back to {_game.Suit.OxygenShare:P0})");
    }
}
