using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;
using Slice.Places;
using Slice.Surface;
using Wildlife;
using Layers = GalHack.Physics.Layers;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace Slice.Tests;

/// <summary>
/// Everyone falls when nothing is under their feet (owner, 21 Nov: "if NPCs have the floor destroyed beneath them, they don't
/// fall"; "Are they effected by gravity?"). On fresh worlds every run (nothing pinned), in a settlement: the floor is broken under
/// one of its people standing, one walking, one sitting and one asleep in bed; the ground is dug from under a scavenger out on the
/// land; the floor goes from under one of the place's creatures; a crater is blown under a small and a large creature on the land;
/// and one of the people is set high over the ground (30 m, then 100 m). Each must come down to what is below in the time a free fall
/// under the world's pull takes (a tick either way), never be inside anything solid, be hurt by the sleeper's fall rule for the
/// height (a long drop kills, limp), and keep acting after. (New members are read by name, so the same test runs, and fails, on a
/// build from before the falls.)
///
/// -- --worlds=N: settlements (default 3). --shots=DIR (windowed): falling.png, landed.png of a person below a blown floor.
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/FallTest.tscn
/// </summary>
public partial class FallTest : Node
{
    const int WatchdogMinutes = 25;
    const float Tick = 1f / 60f;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Game.Game _game;
    Driver _drive;
    double _max;
    string _shots;
    bool _snapping, _shot;
    int _solidSeen;
    readonly HashSet<string> _proven = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--shots=")) _shots = arg["--shots=".Length..];
        if (_shots != null) Directory.CreateDirectory(_shots);
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
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
        SetPhysicsProcess(false);
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    void TopUp() => _game.Suit.Vitals.Health = _max;
    static object Get(object o, string name) => o?.GetType().GetField(name)?.GetValue(o) ?? o?.GetType().GetProperty(name)?.GetValue(o);
    static void Set(object o, string name, object v) => o?.GetType().GetField(name)?.SetValue(o, v);
    float G => _game.Zone.Physics.Zone.Strength;

    /// <summary>Physics ticks since a frame count: the sim's own time, whatever the test does between (a shot taken).</summary>
    static int Frame(ulong from) => (int)(Engine.GetPhysicsFrames() - from);

    /// <summary>The ground as it stands now, dug or not (read by name: a build from before the falls had only the undug ground).</summary>
    float GroundNow(float x, float z) => _game.Zone.Planet.Terrain.GetType().GetMethod("GroundNow")?.Invoke(_game.Zone.Planet.Terrain, new object[] { (double)x, (double)z }) is float g ? g : _game.Zone.Planet.GroundHeight(x, z);

    /// <summary>The sleeper's fall rule, as the physics module has it: the share of health a landing at this speed costs.</summary>
    float Share(float speed)
    {
        PhysicsRules r = _game.Zone.Physics.Rules;
        return Impacts.Damage(100f, speed, r.Contacts, BodyKind.Player) / Impacts.Integrity(BodyKind.Player, 100f, r.Integrity);
    }

    IEnumerable<int> Scenario()
    {
        int worlds = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--worlds=")) is { } w ? int.Parse(w[9..]) : 3;
        for (int i = 0; i < worlds; i++)
            foreach (int x in World(i == 0)) yield return x;
        // Every case shown on at least one of the worlds (a world may have nobody sitting, or no creature near, at the time).
        foreach (string kind in new[] { "standing", "walking", "sitting", "asleep in bed", "out on the land", "into deep water", "into shallow water", "a creature into deep water", "one of a place's creatures", "a small creature", "a large creature", "set high", "a killing height" })
            Check(_proven.Contains(kind), $"shown falling on at least one world: {kind}");
    }

    IEnumerable<int> World(bool first)
    {
        const string family = "survivor_town";
        PlanetSurface.Poi.PoiSiting.ForceFamily = family;
        WorldChoice world = WorldChoice.Fresh("temperate");
        _game = new Game.Game { Interactive = false, World = world, SkipIntro = true };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        PlanetSurface.Poi.PoiSiting.ForceFamily = null;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _max = _game.Suit.Sim.Data.Vitals.Health.Max;
        GD.Print($"-- a settlement, {world}, gravity {G:F1} m/s² --");
        foreach (int x in Settlement(family, first)) yield return x;
        _game.QueueFree();
        _game = null;
        yield return 0;
    }

    IEnumerable<int> Settlement(string family, bool shots)
    {
        var site = _game.Zone.Planet.Pois.FirstOrDefault(s => s.Family.Id == family && s.Instance?.Townsfolk != null);
        if (site == null) { GD.Print($"  (no {family} with people could be seated on this world)"); yield break; }
        if (site.Instance.Mood == Mood.Hostile) site.Instance.Mood = Mood.Wary;
        Int3 c0 = site.Instance.Townsfolk.Spots[0].Cell;
        Vector3 camp = _game.Zone.Planet.NearestCopy(new Vector3(site.Origin.X + c0.X + 0.5f, site.Origin.Y + c0.Y, site.Origin.Z + c0.Z + 0.5f), _game.Survivor.WorldFeet);
        _game.Survivor.PlaceOutside((camp + Vector3.Up * 0.2f).ToNumerics());
        for (int i = 0; i < 3600 && _game.Places.PlaceOf(site) is not { Ready: true }; i++) { TopUp(); yield return 0; }
        Place place = _game.Places.PlaceOf(site);
        Town town = place?.Memory.Town;
        if (town == null) { Check(false, $"the {family} stands with its people"); yield break; }
        if (town.Memory.Mood == Mood.Hostile) town.Memory.Mood = Mood.Wary;
        for (int i = 0; i < 900 && town.People.Any(p => p.Looks == null || !p.Dead && p.View == null); i++) { TopUp(); yield return 0; }
        var used = new HashSet<Town.Person>();
        bool Free(Town.Person p) => !p.Dead && !p.Gone && p.Looks != null && p.View != null && p.Run == null && !used.Contains(p) && town.CanStand(p.Pos);

        // What looking under them costs with nothing breaking: the slow sweep (and a look wherever the blocks show no floor).
        if (Get(town, "FallRays") is int rays0 && Get(town, "FallChecks") is int checks0)
        {
            double Ms() => Get(town, "FallMs") is double v ? v : 0.0;
            double ms0 = Ms(), last = ms0;
            int Fell() => town.People.Sum(p => Get(p, "Falls") is int f ? f : 0);
            int fell0 = Fell();
            var frames = new List<double>(600);
            for (int i = 0; i < 600; i++) { TopUp(); yield return 0; frames.Add(Ms() - last); last = Ms(); }
            frames.Sort();
            double worst = frames[^1], p99 = frames[593];
            double each = (Ms() - ms0) / 600.0;
            // (The worst single frame is told, not judged: a timer read across a thread switch can say anything.)
            Check(each < 0.05 && p99 < 0.1, $"looking under them costs well under 0.1 ms a frame ({each:F4} ms a frame on average, {p99:F4} ms in 99 frames of 100, {worst:F4} ms at the worst)");
            int rays = (int)Get(town, "FallRays") - rays0, checks = (int)Get(town, "FallChecks") - checks0;
            GD.Print($"  (the sweep, nothing breaking: {Fell() - fell0} fell meanwhile, {checks / 10f:F1} looks and {rays / 10f:F1} rays a second over {town.People.Count(p => !p.Dead)} people, {town.People.Count(p => !p.Dead && (town.World(p.Pos) - _game.Survivor.WorldFeet).Length() < 60f)} within its reach)");
        }

        foreach (int x in Floor(town, place, "standing", p => Free(p) && p.Now < 0.05f && !p.Sitting && !p.Asleep, used, shots && _shots != null)) yield return x;
        foreach (int x in Floor(town, place, "walking", p => Free(p) && p.Now > 0.5f && p.Path != null, used, false)) yield return x;
        foreach (int x in Floor(town, place, "sitting", p => Free(p) && p.Sitting && p.Seated > 0.5f, used, false, 120)) yield return x;
        _game.Zone.Sky.TimeOfDay = 0.02;
        foreach (int x in Floor(town, place, "asleep in bed", p => !p.Dead && !p.Gone && p.View != null && p.Asleep && !used.Contains(p), used, false, 150, 1f)) yield return x;
        _game.Zone.Sky.TimeOfDay = 0.42;
        foreach (int x in Land(town, place, used)) yield return x;
        foreach (int x in Water(town, used, deep: true)) yield return x;
        foreach (int x in Water(town, used, deep: false)) yield return x;
        foreach (int x in WaterCreature(town)) yield return x;
        foreach (int x in PlaceCreature(place)) yield return x;
        foreach (int x in Creatures(small: true)) yield return x;
        foreach (int x in Creatures(small: false)) yield return x;
        foreach (int x in High(town, place, used, 30f)) yield return x;
        // (High enough to kill on this world: a landing at its safe speed and 26 m/s more.)
        float lethal = MathF.Pow(_game.Zone.Physics.Rules.Contacts.PlayerSafeSpeed + 26f, 2f) / (2f * G);
        foreach (int x in High(town, place, used, lethal)) yield return x;
    }

    /// <summary>The drop under a point of the place (local feet): from the floor's underside down to what is below, m (by the colliders).</summary>
    float DropUnder(Town town, Vector3 feet)
    {
        Vector3 from = town.World(feet + Vector3.Down * 1.05f);
        var hit = _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + Vector3.Down * 60f, Layers.World | Layers.Hull | Layers.Interior));
        return hit.Count == 0 ? 0f : from.Y - ((Vector3)hit["position"]).Y + 1.05f;
    }

    /// <summary>
    /// One of them chosen (the one with the most under their floor, near you), the sleeper set a few metres off watching, and the
    /// floor's blocks under them and round them broken.
    /// </summary>
    IEnumerable<int> Floor(Town town, Place place, string what, Func<Town.Person, bool> fits, HashSet<Town.Person> used, bool shots, int wait = 60, float room = 2f)
    {
        Town.Person who = null;
        // (One chosen, the sleeper set by them; gone on from over the room under them meanwhile, another.)
        for (int t = 0; t < wait * 60 && who == null; t++)
        {
            TopUp();
            if (t % 30 == 0 && town.People.Where(fits).Where(p => Under(place, p.Pos) && DropUnder(town, p.Pos) >= room).OrderByDescending(p => MathF.Min(DropUnder(town, p.Pos), 6f)).FirstOrDefault() is { } pick)
            {
                Stand(town, pick);
                for (int i = 0; i < 30; i++) { TopUp(); _drive.Face(town.World(pick.Pos + Vector3.Up)); yield return 0; }
                if (fits(pick) && Under(place, pick.Pos) && DropUnder(town, pick.Pos) >= room) who = pick;
            }
            yield return 0;
        }
        if (who == null) { GD.Print($"  (nobody {what} on a floor of the place's blocks with room under it within {wait} s: not tried here)"); yield break; }
        used.Add(who);
        float drop = DropUnder(town, who.Pos);
        GD.Print($"  -- the floor broken under one {what} ({who.Job}, {drop:F1} m down to what is under the floor) --");
        Break(place, who.Pos, what == "walking" ? 2 : 1); // (one walking may be a stride on by the time it goes: wider under them)
        foreach (int x in Watch(town, who, what, shots)) yield return x;
    }

    /// <summary>Whether a point's floor is one of the place's own blocks (to be broken).</summary>
    static bool Under(Place place, Vector3 feet)
    {
        var c = new Int3((int)MathF.Floor(feet.X), (int)MathF.Floor(feet.Y + 0.05f) - 1, (int)MathF.Floor(feet.Z));
        BlockGrid g = place.Memory.Inst.Blocks;
        return g.InBounds(c) && !g.Get(c).IsAir && g.Get(c).Role is not (Role.Loot or Role.Terminal or Role.Core or Role.Secret);
    }

    /// <summary>The floor's blocks under a point (local feet) and round it broken by a weapon's hits.</summary>
    void Break(Place place, Vector3 feet, int r)
    {
        int y = (int)MathF.Floor(feet.Y + 0.05f) - 1;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                _game.Places.Struck(new PoiPlaces.Hit(place, new Int3((int)MathF.Floor(feet.X) + dx, y, (int)MathF.Floor(feet.Z) + dz), null, null), 1e6f);
        int left = 0;
        for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
                if (!place.Memory.Inst.Blocks.Get(new Int3((int)MathF.Floor(feet.X) + dx, y, (int)MathF.Floor(feet.Z) + dz)).IsAir) left++;
        if (left > 0) GD.Print($"    ({left} of the floor's blocks there would not break)");
    }

    /// <summary>The sleeper a few metres off one of them, where a body can stand, facing them.</summary>
    void Stand(Town town, Town.Person who)
    {
        Vector3 stand = who.Pos + Vector3.Back * 5f;
        for (int k = 0; k < 24; k++)
        {
            float a = k * MathF.Tau / 12f, d = k < 12 ? 6f : 4f;
            Vector3 at = who.Pos + new Vector3(MathF.Sin(a), 0f, MathF.Cos(a)) * d;
            if (town.CanStand(at) && MathF.Abs(at.Y - who.Pos.Y) < 0.5f) { stand = at; break; }
        }
        _game.Survivor.PlaceOutside(town.World(stand + Vector3.Up * 0.15f).ToNumerics());
    }

    /// <summary>Whether a body (feet, world) is in anything solid: a capsule from just over its feet to its chest.</summary>
    bool InSolid(Vector3 feet, float radius)
    {
        var q = new PhysicsShapeQueryParameters3D
        {
            Shape = new CapsuleShape3D { Radius = radius, Height = 1.1f },
            Transform = new Transform3D(Basis.Identity, feet + Vector3.Up * 0.85f),
            CollisionMask = Layers.World | Layers.Hull | Layers.Interior,
        };
        var hits = _game.Zone.GetWorld3D().DirectSpaceState.IntersectShape(q, 1);
        if (hits.Count > 0 && _solidSeen++ < 3) GD.Print($"    (in {(hits[0]["collider"].AsGodotObject() as Node)?.GetPath()} at feet {feet}, {(Engine.GetPhysicsFrames())})");
        return hits.Count > 0;
    }

    /// <summary>Whether something solid is under a body's footing (feet, world), within a hand of its feet.</summary>
    bool OnSomething(Vector3 feet, float radius)
    {
        var space = _game.Zone.GetWorld3D().DirectSpaceState;
        for (int i = 0; i < 5; i++)
        {
            Vector3 d = i switch { 1 => new(radius, 0, 0), 2 => new(-radius, 0, 0), 3 => new(0, 0, radius), 4 => new(0, 0, -radius), _ => Vector3.Zero };
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(feet + d + Vector3.Up * 0.3f, feet + d + Vector3.Down * 0.15f, Layers.World | Layers.Hull | Layers.Interior));
            if (hit.Count > 0) return true;
        }
        return false;
    }

    /// <summary>
    /// A person whose footing has gone, watched: when they start to come down and when they are down, against a free fall from
    /// that height under the world's pull; never in anything solid; hurt by the fall rule for the speed it comes to; acting after.
    /// </summary>
    IEnumerable<int> Watch(Town town, Town.Person who, string what, bool shots, string kind = null)
    {
        float y0 = who.Pos.Y, health = who.Health.Current, last = y0, from = y0;
        _solidSeen = 0;
        int start = -1, end = -1, inSolid = 0, t;
        ulong f0 = Engine.GetPhysicsFrames();
        bool shot = false;
        for (t = 0; t < 12 * 60; t++)
        {
            TopUp();
            _drive.Face(town.World(who.Pos + Vector3.Up));
            yield return 0;
            float y = who.Pos.Y;
            // (Off their feet: the moment their fall has them; on a build without falls, the moment they first go down.)
            if (start < 0 && (Get(who, "Airborne") is bool air ? air : y < y0 - 1e-3f)) (start, from) = (Frame(f0), last);
            if (start >= 0 && (Get(who, "Airborne") is bool still ? !still : y >= last - 1e-4f)) { end = Frame(f0); break; }
            if (start >= 0 && _shots != null && !_shot && y0 - y > 1.5f) { foreach (int x in Snap(town, who, "falling.png")) yield return x; (_shot, shot) = (true, true); }
            if (start >= 0 && InSolid(town.World(who.Pos), 0.2f)) inSolid++;
            last = y;
        }
        float h = from - who.Pos.Y, took = end >= 0 ? (end - start) * Tick : -1f, free = MathF.Sqrt(2f * MathF.Max(0f, h) / G);
        // (A shot taken in the fall holds this watch a few ticks: their own count of the fall's ticks then.)
        if (shot && end >= 0 && Get(who, "LastFallTime") is float own) took = own - Tick;
        float speed = MathF.Sqrt(2f * G * MathF.Max(0f, h)), expect = Share(speed) * who.Health.Max, lost = health - who.Health.Current;
        GD.Print($"  {what}: began {(start < 0 ? "never" : $"{start * Tick:F2} s after")}, down {h:F2} m in {took:F3} s (a free fall {free:F3} s), {speed:F1} m/s, lost {lost:F1} health (the rule {expect:F1}), {(who.Dead ? "dead" : "alive")}, falls {Get(who, "Falls")}");
        Check(start >= 0 && start * Tick < 1.5f && h > 0.3f, $"{what}: fell, from the moment their footing went (began {start * Tick:F2} s after, {h:F2} m)");
        if (start >= 0 && end >= 0 && h > 0.3f) _proven.Add(kind ?? what);
        Check(end >= 0 && MathF.Abs(took - free) <= Tick + 1e-3f, $"{what}: down in the time a free fall from {h:F2} m takes ({took:F3} s against {free:F3} s)");
        Check(inSolid == 0 && !InSolid(town.World(who.Pos), 0.2f), $"{what}: never in anything solid on the way or landed ({inSolid} ticks)");
        Check(OnSomething(town.World(who.Pos), 0.25f) || town.World(who.Pos).Y - (_game.Zone.Planet.WaterSurfaceAt(town.World(who.Pos)) ?? -1e9f) < 0f, $"{what}: landed on something ({town.World(who.Pos)})");
        Check(MathF.Abs(lost - expect) <= MathF.Max(1f, expect * 0.08f), $"{what}: hurt by the sleeper's fall rule ({lost:F1} lost, {expect:F1} by the rule)");
        if (shot) foreach (int x in Snap(town, who, "landed.png", 40)) yield return x;
        if (!who.Dead) foreach (int x in Acting(town, who, what)) yield return x;
    }

    /// <summary>After their fall, on with their day: up and going from where they landed within a few seconds.</summary>
    IEnumerable<int> Acting(Town town, Town.Person who, string what)
    {
        Vector3 at = who.Pos;
        float moved = 0f;
        for (int t = 0; t < 10 * 60 && moved < 0.5f; t++)
        {
            TopUp();
            yield return 0;
            moved = MathF.Max(moved, new Vector2(who.Pos.X - at.X, who.Pos.Z - at.Z).Length());
            if (t % 60 == 0 && moved < 0.5f) GD.Print($"    ({t / 60} s: at {who.Pos}, run {(who.Run == null ? "none" : $"leg {who.Run.Leg}/{who.Run.Way.Count} home {who.Run.Home} searching {who.Run.Searching}")}, now {who.Now:F2}, clambering {Get(who, "Clambering")}, airborne {Get(who, "Airborne")}, spot {who.Spot} refuge {who.Refuge} asked {who.Asked}, wait {who.Wait:F1})");
        }
        // (Landed somewhere with no walking out nor climbing out: said, and a failure.)
        bool stranded = moved < 0.5f && town.GetType().GetMethod("Stranded")?.Invoke(town, new object[] { who }) is true;
        // (Or gone on to a spot right by where they landed: there, about their day.)
        bool there = who.Spot >= 0 && (who.Pos - town.SpotAt(who.Spot)).Length() < 1.5f && (who.Path == null || who.Step >= who.Path.Count) && Get(who, "Falls") is int n && n > 0;
        Check(moved >= 0.5f || there || who.Path is { Count: > 0 } && !stranded, $"{what}: acting after the fall ({moved:F1} m gone from where they landed{(stranded ? ", no way out of where they landed" : there ? ", at a spot by it" : "")}, way {who.Step}/{who.Path?.Count ?? -1}, scared {who.ScaredFor:F1}{(moved < 0.5f && !there ? $"; {town.GetType().GetMethod("WayOn")?.Invoke(town, new object[] { who })}" : "")})");
    }

    /// <summary>A shot of one of them from the side (windowed only).</summary>
    IEnumerable<int> Snap(Town town, Town.Person who, string name, int wait = 0)
    {
        for (int i = 0; i < wait; i++) yield return 0;
        // (From where the sleeper stands watching, a little nearer and higher: over the hole, looking down into it.)
        Vector3 at = town.World(who.Pos + Vector3.Up * 0.8f), eye = _game.Survivor.WorldFeet + Vector3.Up * 2.2f;
        eye += (new Vector3(at.X, eye.Y, at.Z) - eye).LimitLength(2f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
        _game.Visor.Visible = false;
        yield return 0;
        _snapping = true;
        Capture(Path.Combine(_shots, name));
        while (_snapping) yield return 0;
        _game.FixedCamera = null;
        _game.Visor.Visible = true;
    }

    async void Capture(string file)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Error err = GetViewport().GetTexture().GetImage().SavePng(file);
        GD.Print(err == Error.Ok ? $"  (saved {file})" : $"  (could not save {file}: {err})");
        _snapping = false;
    }

    /// <summary>
    /// A scavenger out on the land: one of the people sent on a short way out over the ground past the place, the sleeper by them,
    /// and the ground dug from under their feet.
    /// </summary>
    IEnumerable<int> Land(Town town, Place place, HashSet<Town.Person> used)
    {
        Town.Person who = town.People.Where(p => !p.Dead && !p.Gone && p.View != null && !used.Contains(p) && !p.Asleep && p.Looks != null && p.Figure.OutAt((float)_game.Zone.Sky.TimeOfDay)).OrderBy(p => p.Job == PersonRole.Scavenger ? 0 : 1).FirstOrDefault();
        if (who == null) { Check(false, "someone to send out on the land"); yield break; }
        used.Add(who);
        // Out past the place's cells on the side away from its middle, a way of a few points over the ground.
        BlockGrid g = place.Memory.Inst.Blocks;
        Vector3 mid = new(g.Min.X + g.Size.X * 0.5f, 0f, g.Min.Z + g.Size.Z * 0.5f);
        Vector3 start = new(g.Min.X - 12f, 0f, mid.Z), dir = Vector3.Back;
        Vector3 Ground(Vector3 local)
        {
            Vector3 w = town.World(local);
            return local with { Y = _game.Zone.Planet.GroundHeight(w.X, w.Z) - town.World(Vector3.Zero).Y };
        }
        var way = Enumerable.Range(0, 20).Select(i => Ground(start + dir * (i * 2f))).ToList();
        if (_game.Zone.Planet.WaterSurfaceAt(town.World(way[0])) != null) { GD.Print("  (water past the place: no way out on the land here)"); yield break; }
        who.Pos = way[0];
        (who.Path, who.Step, who.Sitting, who.Asleep) = (null, 0, false, false);
        who.Run = new ScavRun { Out = way, Round = new List<Vector3> { way[^1] }, Way = way, Find = way[^1] + dir * 3f, Leg = 1 };
        _game.Survivor.PlaceOutside(town.World(way[0] + Vector3.Left * 5f + Vector3.Up * 0.5f).ToNumerics());
        for (int i = 0; i < 120; i++) { TopUp(); _drive.Face(town.World(who.Pos + Vector3.Up)); yield return 0; }
        if (who.Dead) { GD.Print($"  (the one sent out died out there before the ground was dug ({Get(who, "Falls")} falls): not tried)"); yield break; }
        if (who.Run == null) { Check(false, "the one sent out stays out on the land"); yield break; }
        Vector3 w = town.World(who.Pos);
        GD.Print($"  -- the ground dug from under one out on the land ({who.Job}, {(who.Pos - way[0]).Length():F1} m along) --");
        _game.Zone.Edits.Dig(new TInt3((int)MathF.Floor(w.X), (int)MathF.Floor(w.Y - 0.6f), (int)MathF.Floor(w.Z)), 2f);
        foreach (int x in Watch(town, who, "out on the land", false)) yield return x;
    }

    /// <summary>Water near the settlement (world): deep enough to swim in (over two metres) or shallow enough to wade (a hand to half a metre); null if none within 250 m.</summary>
    (Vector3 At, float Top, float Floor)? WaterNear(Town town, bool deep)
    {
        Vector3 o = town.World(Vector3.Zero);
        for (float r = 20f; r <= 250f; r += 6f)
            for (int k = 0; k < 36; k++)
            {
                Vector3 at = o + new Vector3(MathF.Sin(k * MathF.Tau / 36f), 0f, MathF.Cos(k * MathF.Tau / 36f)) * r;
                if (_game.Zone.Planet.WaterSurfaceAt(at) is not float top) continue;
                float floor = GroundNow(at.X, at.Z), depth = top - floor;
                if (deep ? depth > 2.2f : depth is > 0.2f and < 0.5f) return (at, top, floor);
            }
        return null;
    }

    /// <summary>
    /// One of the people dropped into water near the settlement (as if off a bank that went): unhurt by it, they swim (deep) or wade
    /// (shallow) for a bank, never standing on the water, and climb or walk out onto it, never with a leap; then on from there.
    /// </summary>
    IEnumerable<int> Water(Town town, HashSet<Town.Person> used, bool deep)
    {
        string what = deep ? "into deep water" : "into shallow water";
        if (WaterNear(town, deep) is not var (at, top, floor)) { GD.Print($"  (no {(deep ? "deep" : "shallow")} water within 250 m of the settlement: not tried here)"); yield break; }
        Town.Person who = town.People.Where(p => !p.Dead && !p.Gone && p.View != null && p.Run == null && !used.Contains(p) && !p.Asleep).FirstOrDefault();
        if (who == null) { Check(false, $"someone to drop {what}"); yield break; }
        used.Add(who);
        _game.Survivor.PlaceOutside(((at + Vector3.Back * 8f) with { Y = MathF.Max(top, _game.Zone.Planet.GroundHeight(at.X, at.Z + 8f)) + 1f }).ToNumerics());
        for (int i = 0; i < 120; i++) { TopUp(); yield return 0; }
        GD.Print($"  -- one dropped 4 m {what} ({who.Job}; {top - floor:F1} m deep) --");
        (who.Sitting, who.Asleep, who.Path) = (false, false, null);
        who.Pos = town.Local(at with { Y = top + 4f });
        // (Out there as if on an errand home, so they are stepped there whether the place stands or not.)
        Vector3 home = town.SpotAt(0);
        who.Run = new ScavRun { Out = new List<Vector3> { home }, Round = new List<Vector3> { home }, Way = new List<Vector3> { home }, Find = home, Home = true };
        Set(who, "CheckFor", 1f);
        float health = who.Health.Current, last = who.Pos.Y, rise = 0f, swimOff = 0f;
        int onWater = 0, swum = 0, t;
        bool entered = false, outOn = false;
        for (t = 0; t < 120 * 60; t++)
        {
            TopUp();
            yield return 0;
            Vector3 w = town.World(who.Pos);
            float? s = _game.Zone.Planet.WaterSurfaceAt(w);
            float bottom = GroundNow(w.X, w.Z);
            bool inWater = s is float st && st - bottom > 0.15f, swimming = Get(who, "Swimming") is true, wading = Get(who, "Wading") is true;
            if (!entered) { entered = Get(who, "Airborne") is false && t > 1; last = who.Pos.Y; continue; }
            rise = MathF.Max(rise, who.Pos.Y - last);
            last = who.Pos.Y;
            // In it, nobody stands on the water: swimming, the head at the surface (feet a swimming depth under it); else on its floor.
            if (inWater && swimming) { swum++; swimOff = MathF.Max(swimOff, MathF.Abs(s.Value - w.Y - who.Figure.Height * _game.Zone.Physics.Rules.Water.SwimDepth)); }
            else if (inWater && w.Y > bottom + 0.2f) onWater++;
            if (!inWater && !swimming && !wading && MathF.Abs(w.Y - bottom) < 0.3f) { outOn = true; break; }
        }
        float lost = health - who.Health.Current;
        GD.Print($"  {what}: {(outOn ? $"out on the bank after {t / 60f:F1} s" : "still in it")} ({town.World(who.Pos)}), {swum / 60f:F1} s swimming (the head off the surface by {swimOff:F2} m at most), {onWater} ticks standing on the water, the most risen in a tick {rise:F3} m, lost {lost:F1}");
        Check(entered && lost < 0.5f, $"{what}: in it, unhurt by the fall ({lost:F1} lost)");
        Check(onWater == 0 && (!deep || swum > 0 && swimOff < 0.3f), $"{what}: never standing on the water ({onWater} ticks), {(deep ? $"swimming with the head at the surface ({swimOff:F2} m off)" : "wading on its floor")}");
        Check(outOn, $"{what}: out onto a bank within two minutes");
        Check(rise < 0.05f, $"{what}: out on foot, never with a leap (the most risen in a tick {rise:F3} m)");
        if (outOn && onWater == 0) _proven.Add(what);
    }

    /// <summary>One of the land's creatures that swims, dropped into deep water: it swims for a bank and walks out, never with a leap.</summary>
    IEnumerable<int> WaterCreature(Town town)
    {
        if (WaterNear(town, deep: true) is not var (at, top, floor)) { GD.Print("  (no deep water within 250 m: no creature dropped in)"); yield break; }
        _game.Survivor.PlaceOutside(((at + Vector3.Back * 8f) with { Y = MathF.Max(top, _game.Zone.Planet.GroundHeight(at.X, at.Z + 8f)) + 1f }).ToNumerics());
        for (int i = 0; i < 240; i++) { TopUp(); yield return 0; }
        WildlifePatch patch = _game.Zone.Wildlife;
        Creature who = patch.Sim.Creatures.Where(c => !c.Dead && !c.Removed && c.Space == null && !c.Species.Flies && c.Species.Rig is not (RigKind.GasBag or RigKind.Swarm or RigKind.Hopper)
            && c.Species.Domestic == DomesticKind.None && Shore.Swims(c.Species)).MinBy(c => (patch.WorldOf(c) - at).LengthSquared());
        if (who == null) { GD.Print("  (no creature that swims in the patch: none dropped in)"); yield break; }
        GD.Print($"  -- a creature dropped into deep water ({who.Species.Name}, {who.Species.BodyLength:F1} m; {top - floor:F1} m deep) --");
        who.Position = patch.PatchLocal(at with { Y = top + 3f });
        who.Velocity = Vector3.Zero;
        float last = who.Position.Y, rise = 0f;
        int swum = 0, t;
        bool entered = false, outOn = false;
        for (t = 0; t < 120 * 60 && patch.Sim.Creatures.Contains(who) && !who.Dead; t++)
        {
            TopUp();
            yield return 0;
            Vector3 w = patch.WorldOf(who);
            bool inWater = _game.Zone.Planet.WaterSurfaceAt(w) is float s && s - GroundNow(w.X, w.Z) > 0.15f;
            if (!entered) { entered = !who.Falling && t > 1; last = who.Position.Y; continue; }
            rise = MathF.Max(rise, who.Position.Y - last);
            last = who.Position.Y;
            if (inWater && who.Position.Y - patch.Sim.Terrain.Height(who.Position.X, who.Position.Z) > 0.3f) swum++;
            if (!inWater) { outOn = true; break; }
        }
        GD.Print($"  a creature in deep water: {(outOn ? $"out on the bank after {t / 60f:F1} s" : who.Dead ? "drowned" : "still in it")}, {swum / 60f:F1} s swimming, the most risen in a tick {rise:F3} m");
        Check(outOn, "a creature that swims, dropped into deep water: out onto a bank within two minutes");
        Check(rise < 0.06f, $"a creature out of the water on foot, never with a leap (the most risen in a tick {rise:F3} m)");
        if (outOn) _proven.Add("a creature into deep water");
    }


    /// <summary>One of the people set high over the ground (as if on a ledge that went): hurt by the rule, killed by a long drop, limp.</summary>
    IEnumerable<int> High(Town town, Place place, HashSet<Town.Person> used, float height)
    {
        // (One with nothing over them all the way up: they come down where they stood, the whole height.)
        bool Open(Town.Person p) => _game.Zone.GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(town.World(p.Pos + Vector3.Up * (height + 1f)), town.World(p.Pos + Vector3.Up * 0.3f), Layers.World | Layers.Hull | Layers.Interior)).Count == 0;
        Town.Person who = town.People.Where(p => !p.Dead && !p.Gone && p.View != null && p.Run == null && !used.Contains(p) && !p.Asleep && town.CanStand(p.Pos) && Open(p) && town.GetType().GetMethod("Stranded")?.Invoke(town, new object[] { p }) is not true).FirstOrDefault(); // (not a lookout set on a roof with no way down)
        if (who == null) { Check(false, $"someone to set {height:F0} m up"); yield break; }
        used.Add(who);
        Stand(town, who);
        for (int i = 0; i < 30; i++) { TopUp(); yield return 0; }
        GD.Print($"  -- one set {height:F0} m over where they stood ({who.Job}) --");
        (who.Sitting, who.Asleep, who.Path) = (false, false, null);
        who.Pos += Vector3.Up * height;
        Set(who, "CheckFor", 1f);
        foreach (int x in Watch(town, who, $"{height:F0} m up", false, height < 50f ? "set high" : "a killing height")) yield return x;
        if (height < 50f) yield break;
        Check(who.Dead, $"a {height:F0} m drop kills");
        int t = 0;
        for (; t < 10 * 60 && who.Rig is { Settled: false }; t++) { TopUp(); yield return 0; }
        Check(who.Rig is { Settled: true }, $"the body falls limp and comes to rest ({t * Tick:F1} s)");
    }

    /// <summary>One of a place's own creatures on its floor (the settlement's, else the next places round it): the floor broken under it.</summary>
    IEnumerable<int> PlaceCreature(Place home)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        Creature who = null;
        DeepSite site = null;
        Place place = home;
        Vector3 from = _game.Survivor.WorldFeet;
        var others = _game.Zone.Planet.Pois.Where(s => s.Instance != null && s != home.Memory.Site)
            .Select(s => (s, at: _game.Zone.Planet.NearestCopy(new Vector3(s.Entrance.X + 0.5f, s.Entrance.Y, s.Entrance.Z + 0.5f), from)))
            .Where(o => (o.at - from).Length() < 900f).OrderBy(o => (o.at - from).Length()).Take(4).ToList();
        for (int k = -1; k < others.Count && who == null; k++)
        {
            if (k >= 0)
            {
                _game.Survivor.PlaceOutside((others[k].at + Vector3.Up * 0.5f).ToNumerics());
                for (int i = 0; i < 2400 && _game.Places.PlaceOf(others[k].s) is not { Ready: true }; i++) { TopUp(); yield return 0; }
                if (_game.Places.PlaceOf(others[k].s) is not { Ready: true } p) continue;
                place = p;
            }
            for (int t = 0; t < 15 * 60 && who == null; t++)
            {
                TopUp();
                if (t % 30 == 0)
                    foreach (DeepSite s in patch.Sites)
                        if (s.Sim.Creatures.FirstOrDefault(c => !c.Dead && !c.Removed && c.Space != null && !c.Species.Climbs && !c.Species.Flies && !c.Falling && !c.InWalls && Under(place, place.FromWorld(patch.WorldOf(c)))) is { } c) { (who, site) = (c, s); break; }
                yield return 0;
            }
        }
        if (who == null) { GD.Print("  (none of the creatures of the settlement or the places nearest it on a floor of their blocks: not tried here)"); yield break; }
        Vector3 feet = place.FromWorld(patch.WorldOf(who));
        _game.Survivor.PlaceOutside((patch.WorldOf(who) + Vector3.Back * 5f + Vector3.Up * 0.3f).ToNumerics());
        GD.Print($"  -- the floor broken under one of the place's creatures ({who.Species.Name}, {who.Species.BodyLength:F1} m) --");
        Break(place, feet, 1);
        foreach (int x in WatchCreature(who, $"the place's {who.Species.Name}", () => site.Sim.Creatures.Contains(who), "one of a place's creatures")) yield return x;
    }

    /// <summary>A small or large walker on the land near the sleeper: a crater blown under it.</summary>
    IEnumerable<int> Creatures(bool small)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        string what = small ? "a small creature" : "a large creature";
        Creature who = null;
        for (int t = 0; t < 30 * 60 && who == null; t++)
        {
            TopUp();
            if (t % 60 == 0)
            {
                var walkers = patch.Sim.Creatures.Where(c => !c.Dead && !c.Removed && c.Space == null && !c.Species.Flies && c.Species.Rig is not (RigKind.GasBag or RigKind.Swarm or RigKind.Hopper)
                    && c.Species.Domestic == DomesticKind.None && (patch.WorldOf(c) - _game.Survivor.WorldFeet).Length() < 400f && _game.Zone.Planet.WaterSurfaceAt(patch.WorldOf(c)) == null).ToList();
                who = small ? walkers.Where(c => c.Species.BodyLength < 1.5f).MinBy(c => c.Species.BodyLength) : walkers.Where(c => c.Species.BodyLength > 2.5f).MaxBy(c => c.Species.BodyLength);
            }
            yield return 0;
        }
        if (who == null) { GD.Print($"  ({what} on dry land near the sleeper: none here)"); yield break; }
        Vector3 at = patch.WorldOf(who);
        _game.Survivor.PlaceOutside((at + new Vector3(8f + who.Species.BodyLength, 0f, 0f) with { Y = _game.Zone.Planet.GroundHeight(at.X + 8f + who.Species.BodyLength, at.Z) + 0.5f }).ToNumerics());
        for (int i = 0; i < 120; i++) { TopUp(); yield return 0; }
        // (Moved near it, the patch may have been made again round the sleeper: the same one, as the patch now has it.)
        Species kind = who.Species;
        who = patch.Sim.Creatures.Where(c => c.Species == kind && !c.Dead && !c.Removed && !c.Falling).MinBy(c => (patch.WorldOf(c) - at).LengthSquared());
        if (who == null) { GD.Print($"  ({what} gone from the patch as the sleeper came: not tried)"); yield break; }
        at = patch.WorldOf(who);
        float radius = Math.Clamp(who.Species.BodyLength * 0.7f, 2.5f, 6f);
        GD.Print($"  -- a crater of {radius:F1} m blown under {what} ({who.Species.Name}, {who.Species.BodyLength:F1} m) --");
        _game.Zone.Edits.Blast((at + Vector3.Down * 0.5f).ToNumerics(), radius);
        foreach (int x in WatchCreature(who, what, () => patch.Sim.Creatures.Contains(who), small ? "a small creature" : "a large creature")) yield return x;
    }

    /// <summary>A creature whose footing has gone, watched as a person is: the fall's time, the rule's hurt, solid ground at the end, acting after.</summary>
    IEnumerable<int> WatchCreature(Creature who, string what, Func<bool> here, string kind)
    {
        WildlifePatch patch = _game.Zone.Wildlife;
        float y0 = who.Position.Y, health = who.Health, last = y0, from = y0;
        int start = -1, end = -1, t;
        ulong f0 = Engine.GetPhysicsFrames();
        for (t = 0; t < 12 * 60; t++)
        {
            TopUp();
            yield return 0;
            if (!here()) break;
            float y = who.Position.Y;
            if (start < 0 && who.Falling) (start, from) = (Frame(f0), last);
            if (start >= 0 && !who.Falling) { end = Frame(f0); break; }
            last = y;
        }
        if (!here()) { GD.Print($"  ({what} left its sim before it was down ({(who.Dead ? "dead" : "taken off")}): not judged)"); yield break; }
        float h = from - who.Position.Y, took = end >= 0 ? (end - start) * Tick : -1f, free = MathF.Sqrt(2f * MathF.Max(0f, h) / G);
        float speed = MathF.Sqrt(2f * G * MathF.Max(0f, h)), expect = Share(speed) * who.Stats.MaxHealth, lost = health - who.Health;
        Vector3 w = patch.WorldOf(who);
        float ground = who.Space == null ? GroundNow(w.X, w.Z) : float.NaN;
        GD.Print($"  {what}: began {(start < 0 ? "never" : $"{start * Tick:F2} s after")}, down {h:F2} m in {took:F3} s (a free fall {free:F3} s), lost {lost:F1} health (the rule {expect:F1}), feet {w.Y:F2} (from {y0:F2}, in its sim {here()}){(float.IsNaN(ground) ? "" : $", the ground {ground:F2}")}");
        Check(start >= 0 && start * Tick < 1.5f && h > 0.3f, $"{what}: fell ({h:F2} m)");
        if (start >= 0 && end >= 0 && h > 0.3f) _proven.Add(kind);
        // (A fall in a place's rooms is stepped in short pieces with the speed at each: within a tick and a half.)
        Check(end >= 0 && MathF.Abs(took - free) <= Tick * (who.Space == null ? 1f : 1.5f) + 1e-3f, $"{what}: down in the time a free fall from {h:F2} m takes ({took:F3} s against {free:F3} s)");
        // (On the land a creature stands on the ground as dug, by the metre.)
        Check(float.IsNaN(ground) || MathF.Abs(w.Y - ground) < 0.35f, $"{what}: on the ground as it is now, never in it ({w.Y:F2} against {ground:F2})");
        Check(MathF.Abs(lost - expect) <= MathF.Max(1f, expect * 0.08f), $"{what}: hurt by the sleeper's fall rule ({lost:F1} lost, {expect:F1} by the rule)");
        Vector3 at = who.Position;
        float moved = 0f;
        bool fled = false;
        for (t = 0; t < 10 * 60 && moved < 0.3f && !fled && here() && !who.Dead; t++)
        {
            TopUp();
            yield return 0;
            moved = (who.Position - at).Length();
            fled |= who.State == CState.Flee;
        }
        Check(who.Dead || moved >= 0.3f || fled, $"{what}: acting after the fall ({moved:F1} m{(fled ? ", running from where it fell" : "")})");
    }
}
