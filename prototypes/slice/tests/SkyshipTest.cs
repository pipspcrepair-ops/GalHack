using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Slice.Game;
using Slice.Places;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The start world's lone trader or scout (owner, 15 Nov). First the generator on its own: many rolls of both roles at
/// random gravities, each built only of the catalogue's blocks as a player builds, passing the player's blueprint
/// validation and flight standards (lift, push, brake, slide, turn, power, fuel) with a positive lift margin, in the
/// Freeholder look, never a stock blueprint; losing a thruster costs it what it would cost a player. Then on a fresh
/// random start world: the ship is there, generated, flying under its own thrust between at least three places and
/// landing at one, above the ground and clear of structures, its lights on at night; shot at, it runs; destroyed, it
/// leaves a wreck with its cargo; and what it costs stays within the frame budget.
///
/// Run: test-q.ps1 -Project prototypes/slice -Scene res://tests/SkyshipTest.tscn [-Extra --role=scout,--minspec=1,--pure]
/// </summary>
public partial class SkyshipTest : Node
{
    const int WatchdogMinutes = 40;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    readonly Dictionary<string, string> _args = new();

    public override void _Ready()
    {
        new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  SkyshipTest took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(2);
        }) { IsBackground = true }.Start();
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            _args[eq > 2 ? arg[2..eq] : arg.TrimStart('-')] = eq > 2 ? arg[(eq + 1)..] : "1";
        }
        // The minimum-spec stand-in: four of the machine's cores.
        if (_args.ContainsKey("minspec")) Process.GetCurrentProcess().ProcessorAffinity = 0x55;
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

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"  FAIL  {what}");
    }

    bool Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"  ok    {what}"); }
        else Fail(what);
        return ok;
    }

    IEnumerable<int> Scenario()
    {
        Rolls(GameData.Shared);
        if (_args.ContainsKey("pure")) yield break;
        foreach (int x in InWorld()) yield return x;
    }

    // ---- The generator --------------------------------------------------------------------------------

    static string Repo => Path.GetFullPath(Path.Combine(GameData.PrototypesRoot, ".."));

    void Rolls(GameData data)
    {
        Catalogue cat = data.Catalogue;
        var stock = BlueprintSet.All.Select(d => d.Make(cat)).Select(t => Blueprint.Load(t, cat, out _)).Where(s => s != null).ToList();
        var stockText = stock.Select(Blueprint.Save).ToHashSet();
        var stockNames = stock.Select(s => s.Name).ToHashSet();
        var rng = new Random();
        int rolls = int.TryParse(_args.GetValueOrDefault("rolls"), out int n) ? n : 30;
        foreach (CraftRole role in new[] { CraftRole.Trader, CraftRole.Scout })
        {
            int made = 0, valid = 0, flies = 0, standards = 0, freeholder = 0, unlike = 0, roleParts = 0, balanced = 0;
            var sizes = new HashSet<int>();
            var problems = new List<string>();
            long ms = 0;
            for (int i = 0; i < rolls; i++)
            {
                int seed = rng.Next();
                float g = 5f + (float)rng.NextDouble() * 10f;
                var clock = Stopwatch.StartNew();
                Structure s = SmallCraft.Roll(cat, role, CraftStyle.Freeholder, seed, g, out string short_);
                ms = Math.Max(ms, clock.ElapsedMilliseconds);
                if (s == null) { problems.Add($"seed {seed} g {g:F1}: no roll met its needs: {short_}"); continue; }
                made++;
                sizes.Add(s.BlockCount);
                // The player's blueprint validation: it saves and loads back whole, every block joined to the core.
                Structure back = Blueprint.Load(Blueprint.Save(s), cat, out List<string> errors);
                if (back != null) valid++;
                else problems.Add($"seed {seed}: {string.Join("; ", errors.Take(3))}");
                // Its flight mode lacks nothing, and its four numbers by the handling model meet the player's standards here.
                var missing = ModeFit.Missing(s, CoreMode.Flight, data.Handling, g);
                HandlingStats h = HandlingModel.Compute(new StructureVessel(s, data.Handling), new FlightConditions(g, true), data.Handling);
                StructureStats st = StructureStats.Compute(s);
                if (missing.Count == 0 && h.LiftMargin > 0f) flies++;
                else problems.Add($"seed {seed} g {g:F1}: missing {string.Join(", ", missing)}, lift margin {h.LiftMargin:F0} kN");
                float side = MathF.Min(h.Accel(Direction.Left), h.Accel(Direction.Right));
                double hours = st.StorageCapacity * CraftNeeds.Default.FuelMJPerSu / (3600 * Math.Max(st.PowerUse, 1e-6));
                bool meets = h.UpwardThrust / h.Weight >= 1.3f && h.Accel(Direction.Forward) >= 5f && h.Accel(Direction.Back) >= 5f && side >= 2f
                             && h.Accel(Direction.Down) >= 2f && h.TurnRate >= data.Handling.Turning.Floor && st.PowerOut >= 1.2 * st.PowerUse && hours >= 1;
                if (meets) standards++;
                else problems.Add($"seed {seed} g {g:F1}: lift x{h.UpwardThrust / h.Weight:F2}, fwd {h.Accel(Direction.Forward):F1}, back {h.Accel(Direction.Back):F1}, side {side:F1}, down {h.Accel(Direction.Down):F1}, turn {h.TurnRate:F0}, {st.PowerOut:F2}/{st.PowerUse:F2} MW, {hours:F1} h");
                // Every exhaust faces open air, as the player's standards ask.
                bool open = s.Blocks().Where(x => s.TypeOf(x.block).IsThruster).All(x =>
                {
                    Dir away = Orientations.ToWorld(x.block.Rot, Dir.PosZ);
                    var cells = s.CellsOf(x.pos, x.block).ToList();
                    int face = cells.Max(c => Int3.Dot(c, away.Vec()));
                    return !cells.Any(c => Int3.Dot(c, away.Vec()) == face && s.Has(c + away.Vec()));
                });
                if (open) balanced++;
                else problems.Add($"seed {seed}: an exhaust is blocked");
                if (Freeholder(s)) freeholder++;
                else problems.Add($"seed {seed}: not in the Freeholder look");
                if (!stockText.Contains(Blueprint.Save(s)) && !stockNames.Contains(s.Name)) unlike++;
                int containers = st.Containers, masts = s.Blocks().Count(x => s.ShapeOf(x.block).Id == "cylinder");
                if (role == CraftRole.Trader ? containers >= 4 : masts >= 3) roleParts++;
                else problems.Add($"seed {seed}: {containers} containers, {masts} mast cells");
            }
            foreach (string p in problems.Distinct().Take(8)) GD.Print($"    {role}: {p}");
            Check(made == rolls, $"{role}: {made} of {rolls} rolls at random gravities (5 to 15 m/s²) met the needs (slowest {ms} ms)");
            Check(valid == made, $"{role}: {valid} of {made} pass the player's blueprint validation (every block joined to the core, built as a player builds)");
            Check(flies == made, $"{role}: {flies} of {made} lack nothing for Flight Mode, with a positive lift margin on their gravity");
            Check(standards == made, $"{role}: {standards} of {made} meet the player's flight standards: lift 1.3x weight, push and brake 5, slide 2, down 2 m/s², the turn floor, 1.2x power, room for an hour of fuel");
            Check(balanced == made, $"{role}: {balanced} of {made} have every exhaust facing open air");
            Check(freeholder == made, $"{role}: {freeholder} of {made} in the Freeholder look (theirs, faded mismatched coats, grimy, patched, a hand-painted number)");
            Check(unlike == made, $"{role}: {unlike} of {made} unlike every stock blueprint");
            Check(roleParts == made, $"{role}: {roleParts} of {made} carry their role's parts ({(role == CraftRole.Trader ? "cargo pods of containers" : "a sensor mast")})");
            Check(sizes.Count >= Math.Min(Math.Max(rolls / 2, 2), 5), $"{role}: rolls vary ({sizes.Count} different block counts, {sizes.Min()} to {sizes.Max()})");
        }
        Losses(data);
    }

    /// <summary>Theirs, its armour in at least three of the style's faded coats, all of them grimy, and a hull number stencilled on.</summary>
    static bool Freeholder(Structure s)
    {
        CraftStyle style = CraftStyle.Freeholder;
        var coats = new HashSet<uint>();
        bool grimy = true;
        int stencils = 0;
        foreach (var (_, b) in s.Blocks())
        {
            if (!s.TypeOf(b).IsArmor) continue;
            foreach (ushort slot in new[] { b.Paint.PX, b.Paint.NX, b.Paint.PY, b.Paint.NY, b.Paint.PZ, b.Paint.NZ })
            {
                Paint p = s.Palette[slot];
                if (style.Coats.Contains(p.Base)) coats.Add(p.Base);
                grimy &= p.Grime >= style.GrimeLow;
                if (p.Stencil is >= (byte)'0' and <= (byte)'9') stencils++;
            }
        }
        return s.Owner == style.Owner && coats.Count >= 3 && grimy && stencils >= 4;
    }

    /// <summary>Losing a thruster costs it what it would cost a player: the handling model counts what's left.</summary>
    void Losses(GameData data)
    {
        float g = data.Rules.Planet.Gravity;
        Structure s = SmallCraft.Roll(data.Catalogue, CraftRole.Trader, CraftStyle.Freeholder, new Random().Next(), g);
        if (!Check(s != null, "a trader to shoot thrusters off")) return;
        HandlingStats Stats() => HandlingModel.Compute(new StructureVessel(s, data.Handling), new FlightConditions(g, true), data.Handling);
        foreach (Dir push in new[] { Dir.PosY, Dir.NegZ })
        {
            HandlingStats before = Stats();
            var (p, b) = s.Blocks().First(x => s.TypeOf(x.block).IsThruster && Orientations.ToWorld(x.block.Rot, Dir.NegZ) == push);
            BlockType t = s.TypeOf(b);
            s.Clear(p);
            HandlingStats after = Stats();
            Direction d = push == Dir.PosY ? Direction.Up : Direction.Forward;
            float lost = before.Thrust[(int)d] - after.Thrust[(int)d];
            Check(MathF.Abs(lost - t.Thrust) < 0.5f && after.Mass < before.Mass,
                $"a {t.Id} pushing {push.Name()} shot off: {lost:F0} kN of {d} thrust gone (its {t.Thrust:F0}), {before.Mass - after.Mass:F2} t lighter; lift margin {before.LiftMargin:F0} to {after.LiftMargin:F0} kN");
        }
    }

    // ---- On a start world -------------------------------------------------------------------------------

    IEnumerable<int> InWorld()
    {
        if (_args.TryGetValue("role", out string r) && Enum.TryParse(r, true, out CraftRole role)) SkyTraffic.ForceRole = role;
        Skyship.WaitScale = 0.12f;
        SkyTraffic.Off = _args.ContainsKey("noship");
        // The ship's own cost is the main thread's time in it: a stall while the OS ran something else is not the ship's.
        if (PlanetSurface.FrameProbe.CyclesPerMs > 0) SkyTraffic.Clock = () => PlanetSurface.FrameProbe.Cycles() / PlanetSurface.FrameProbe.CyclesPerMs;
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        var probe = new PlanetSurface.FrameProbe(this);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        GD.Print($"-- world: {_game.World} (the same again: --seed {_game.World.Seed} --planet {_game.World.Archetype}) --");
        _game.Zone.Wildlife.Frozen = true;
        SkyTraffic sky = _game.Sky;
        if (SkyTraffic.Off)
        {
            // The baseline: the same world and the same wait with no ship in its sky.
            foreach (int x in _drive.Wait(240)) yield return x;
            var still = new List<double>();
            for (int t = 0; t < Window; t++)
            {
                yield return 0;
                if (probe.LastMs > 0) still.Add(probe.LastMs);
            }
            Check(_game.Zone.Vessels.Count(x => x.Roles.Pilot == SkyTraffic.PilotName) == 0, "no ship in the sky (baseline)");
            Report("no ship", still);
            yield break;
        }
        for (int i = 0; i < 600 && sky.Ship == null && sky.Missing == null; i++) yield return 0;
        Skyship ship = sky.Ship;
        if (!Check(ship != null, $"the start world has its ship in the sky{(sky.Missing != null ? ": " + sky.Missing : "")}")) yield break;
        Vessel v = ship.Vessel;
        Structure s = v.Structure;
        GD.Print($"-- a {ship.Role}, {s.BlockCount} blocks, {v.Stats?.Mass:F1} t, top {v.Stats?.TopSpeed:F0} m/s, home {ship.At?.Family.Name} --");
        Check(_game.Zone.Vessels.Count(x => x.Roles.Pilot == SkyTraffic.PilotName || x.Structure.Owner == CraftStyle.Freeholder.Owner) == 1, "one such ship on the world");
        Check(Freeholder(s) && BlueprintSet.All.All(d => d.Make(s.Catalogue) != Blueprint.Save(s)), "generated in the Freeholder style, no stock blueprint");
        Check(v.Stats is { CanLiftOff: true } && v.Stats.LiftMargin > 0f && v.MissingFor(CoreMode.Flight).Count == 0,
            $"on this world ({_game.Zone.Physics.Zone.Strength:F1} m/s²) it lacks nothing to fly: lift margin {v.Stats?.LiftMargin:F0} kN");
        Check(!s.Blocks().Any(b => s.TypeOf(b.block).Function is "turret" or "weapon") && !s.Blocks().Any(b => s.TypeOf(b.block).Id is "gatling" or "rocketpod" or "turret"),
            "it carries no weapon: it never fires");
        Check(!v.Storage.IsEmpty, "its holds carry cargo and fuel");

        // Its lights by the time of day.
        var skyRig = _game.Zone.Sky;
        double noon = Staging.HighSun(_game), night = Staging.HighSun(_game, highest: false);
        int Lit() => s.Blocks().Count(b => s.TypeOf(b.block).IsLight && !b.block.Off);
        skyRig.TimeOfDay = night;
        for (int i = 0; i < 240 && !v.LightsOn; i++) yield return 0;
        Check(v.LightsOn && Lit() >= 4, $"its lights come on at night ({Lit()} lit, daylight {skyRig.Daylight:F2})");
        skyRig.TimeOfDay = noon;
        for (int i = 0; i < 240 && v.LightsOn; i++) yield return 0;
        Check(!v.LightsOn && Lit() == 0, $"and go off by day (daylight {skyRig.Daylight:F2})");

        // Its business: under its own thrust between places, landing at them.
        var places = new HashSet<PlanetSurface.Poi.PoiSite> { ship.At };
        int shifts = _game.Zone.FrameShifts, flying = 0, thrusting = 0, under = 0;
        float jump = 0f, top = v.Stats?.TopSpeed ?? 70f;
        N.Vector3 last = v.Body.Position;
        var frames = new List<double>();
        var window = new List<double>();
        int overlaps = 0, looked = 0, landedClear = 0;
        string overlapWhat = "";
        double skyMs = 0, skyWorst = 0;
        var skyTimes = new List<double>();
        int skyTicks = 0;
        double tookOff = double.NaN, landedWith = double.NaN, leastCharge = double.PositiveInfinity, leastSupply = 1;
        SkyPhase lastPhase = ship.Phase;
        if (ship.Phase != SkyPhase.Landed) tookOff = v.Power.FuelUnits; // already off the ground when the watch began
        bool Done() => places.Count >= 3 && ship.Landings >= 1 && ship.Departures >= 2;
        for (int t = 0; t < 60 * 60 * 18 && !Done() && ship.Phase != SkyPhase.Wrecked; t++)
        {
            yield return 0;
            if (probe.LastMs > 0) frames.Add(probe.LastMs);
            if (probe.LastMs > 0 && t < Window) window.Add(probe.LastMs);
            // Nothing of the terrain's scatter through its hull: at every landing, and near the ground a second at a time.
            bool landing = lastPhase != SkyPhase.Landed && ship.Phase == SkyPhase.Landed;
            if (landing || t % 60 == 0 && ship.Phase is SkyPhase.Descending or SkyPhase.Rising or SkyPhase.Approaching or SkyPhase.Landed)
                if (Overlap(v, sky) is int n)
                {
                    looked++;
                    if (n > 0) { overlaps += n; overlapWhat = $"{ship.Phase} by {ship.At?.Family.Name}"; }
                }
            if (landing && sky.Clear(v.Body.Position, v)) landedClear++;
            skyMs += sky.LastTickMs;
            skyWorst = Math.Max(skyWorst, sky.LastTickMs);
            skyTimes.Add(sky.LastTickMs);
            skyTicks++;
            if (ship.At != null) places.Add(ship.At);
            // Its generator fuel, taking off and putting down, and its capacitor whenever it is aloft.
            if (lastPhase == SkyPhase.Landed && ship.Phase != SkyPhase.Landed && double.IsNaN(tookOff)) tookOff = v.Power.FuelUnits;
            if (lastPhase != SkyPhase.Landed && ship.Phase == SkyPhase.Landed && !double.IsNaN(tookOff) && double.IsNaN(landedWith)) landedWith = v.Power.FuelUnits;
            lastPhase = ship.Phase;
            if (ship.Phase != SkyPhase.Landed)
            {
                leastCharge = Math.Min(leastCharge, v.Power.ChargeMJ);
                leastSupply = Math.Min(leastSupply, v.Power.Supply);
            }
            if (ship.Phase != SkyPhase.Landed)
            {
                flying++;
                if (v.Flight.State.ThrustLocal.LengthSquared() > 1e-4f || v.Flight.Input.AnyThrust) thrusting++;
            }
            if (_game.Zone.FrameShifts == shifts) jump = MathF.Max(jump, MathX.Flat(v.Body.Position - last).Length());
            shifts = _game.Zone.FrameShifts;
            last = v.Body.Position;
            if (v.AboveGround < -1f) under++;
            if (t % 1800 == 0) GD.Print($"  {t / 60} s: {ship.Phase}, {MathX.Flat(v.Body.Position - _game.Survivor.WorldFeet.ToNumerics()).Length():F0} m off, {v.AboveGround:F0} m up, {MathX.Flat(v.Body.Velocity).Length():F0} m/s; {places.Count} places, {ship.Departures} departures, {ship.Landings} landings");
        }
        Check(Done(), $"it flew between {places.Count} places ({string.Join(", ", places.Select(p => p.Family.Name))}): {ship.Departures} departures, {ship.Landings} landings at {string.Join(", ", ship.Visited.Select(p => p.Family.Name))}");
        Check(flying > 0 && thrusting > flying * 0.95f, $"under its own thrust: thrusters working {thrusting} of {flying} ticks aloft");
        Check(jump <= top * 1.6f / 60f + 0.5f, $"no jumps: the most it moved flat in a tick was {jump:F2} m (top speed {top:F0} m/s)");
        Check(under == 0 && ship.LeastClearance > 20f, $"above the ground: least {ship.LeastClearance:F0} m in cruise, {under} ticks under it");
        Check(looked > 0 && overlaps == 0, $"nothing passes through its hull: {overlaps} trees, rocks or crowns in it over {looked} looks near the ground{(overlaps > 0 ? $" ({overlapWhat})" : "")}");
        Check(landedClear == ship.Landings, $"every landing spot is clear of places, roads, roadside pieces and the scatter: {landedClear} of {ship.Landings}");
        for (int t = window.Count; t < Window; t++)
        {
            yield return 0;
            if (probe.LastMs > 0) window.Add(probe.LastMs);
        }
        Report("the ship flying", window);
        Check(landedWith < tookOff, $"its generators burn the fuel in its hold as it flies: {tookOff:F0} fuel units at take-off, {landedWith:F0} on landing");
        Check(leastCharge > 0 && leastSupply >= 0.999, $"never on an empty capacitor: least charge {leastCharge:F1} of {v.Power.CapacityMJ:F0} MJ aloft, every draw met ({leastSupply:P1} at worst)");
        Check(ship.Bumps == 0, $"clear of structures and terrain: {ship.Bumps} knocks in flight");

        // Shot at, it runs.
        for (int t = 0; t < 60 * 120 && ship.Phase != SkyPhase.Cruising; t++) yield return 0;
        SkyPhase was = ship.Phase;
        float before = MathX.Flat(v.Body.Position - _game.Survivor.WorldFeet.ToNumerics()).Length();
        var (cell, _) = s.Blocks().First(b => s.TypeOf(b.block).IsArmor);
        N.Vector3 at = v.Body.ToParent(StructureBridge.CellCenter(s, cell));
        v.Hits.Shot(cell, at, N.Vector3.Normalize(_game.Survivor.WorldFeet.ToNumerics() - at), 40f, DamageType.Kinetic);
        for (int t = 0; t < 120 && ship.Phase != SkyPhase.Fleeing; t++) yield return 0;
        Check(ship.Phase == SkyPhase.Fleeing, $"shot at ({was}), it runs");
        for (int t = 0; t < 60 * 15; t++) yield return 0;
        float after = MathX.Flat(v.Body.Position - _game.Survivor.WorldFeet.ToNumerics()).Length();
        Check(after > before + 150f, $"away from the sleeper: {before:F0} to {after:F0} m in 15 s");

        // Destroyed, it is a wreck with its cargo.
        _game.Zone.Physics.Hurt(v.Body, v.Body.Integrity * 2f + 1e5f);
        for (int t = 0; t < 300 && ship.Phase != SkyPhase.Wrecked; t++) yield return 0;
        Check(ship.Phase == SkyPhase.Wrecked && v.IsWreck && _game.Zone.Vessels.Contains(v) && s.Count > 0 && !v.Storage.IsEmpty,
            $"destroyed, it is a wreck of {s.Count} blocks to salvage, its holds still full ({v.Storage.UsedSu:F0} SU)");
        for (int t = 0; t < 60 * 60 && !(_game.Zone.Held.Contains(v.Body) || v.Body.Velocity.Length() < 0.2f && v.AboveGround < 3f); t++) yield return 0;
        Check(v.Body.Velocity.Length() < 0.5f && v.AboveGround > -2f, $"it came down and came to rest, over the ground ({v.AboveGround:F1} m up, {v.Body.Velocity.Length():F2} m/s)");

        // What it cost.
        frames.Sort();
        double median = frames.Count > 0 ? frames[frames.Count / 2] : 0, p95 = frames.Count > 0 ? frames[(int)(frames.Count * 0.95)] : 0;
        GD.Print($"  frames {frames.Count}: median {median:F2} ms, 95th {p95:F2} ms; the sky's own tick {skyMs / Math.Max(1, skyTicks):F3} ms mean, {skyWorst:F2} ms worst{(_args.ContainsKey("minspec") ? " (min-spec: 4 cores)" : "")}");
        Check(median <= 16.7, $"frame time within budget: median {median:F2} ms");
        skyTimes.Sort();
        double sky99 = skyTimes.Count > 0 ? skyTimes[(int)(skyTimes.Count * 0.999)] : 0;
        Check(skyMs / Math.Max(1, skyTicks) < 0.3 && sky99 < 2.0 && skyWorst < 16.7,
            $"the ship costs little (the main thread's own time in it): {skyMs / Math.Max(1, skyTicks):F3} ms a tick, 99.9th {sky99:F2} ms, {skyWorst:F2} ms at worst ({sky.WorstWhat})");
    }

    /// <summary>The frames measured for the baseline and the ship alike (150 s).</summary>
    const int Window = 60 * 150;

    void Report(string what, List<double> frames)
    {
        frames.Sort();
        double P(double q) => frames.Count > 0 ? frames[Math.Min(frames.Count - 1, (int)(frames.Count * q))] : 0;
        GD.Print($"  window, {what}: {frames.Count} frames, median {P(0.5):F2} ms, 95th {P(0.95):F2} ms, 99th {P(0.99):F2} ms, worst {P(1):F2} ms{(_args.ContainsKey("minspec") ? " (min-spec)" : "")}");
    }

    /// <summary>
    /// How many of the scatter's solids round the vessel (from the terrain's own scatter, not what has streamed in) stand
    /// inside its blocks: points along each trunk's axis, a rock's middle and a crown's middle, each its inscribed radius
    /// from the nearest block. Null while the scatter there is still being worked out.
    /// </summary>
    int? Overlap(Vessel v, SkyTraffic sky)
    {
        var (lo, hi) = v.Structure.Bounds();
        float reach = (hi - lo).ToVector3().Length() * v.Structure.Grid.CellSize * 0.5f + 2f;
        var near = sky.Zone.Scatter.Near(v.Body.Position, reach);
        if (near == null) return null;
        int n = 0;
        foreach (var s in near)
        {
            Vector3 c = _game.Zone.Planet.NearestCopy(s.Center, v.Body.Position.ToGodot());
            float r = s.IsTrunk ? s.Extents.X : MathF.Min(s.Extents.X, s.Extents.Z) * (s.IsCanopy ? 0.5f * s.Scale : 1f);
            Vector3 axis = s.Basis.Y.Normalized() * (s.IsTrunk ? s.Extents.Y : 0f);
            for (int k = 0; k <= 4; k++)
            {
                Vector3 p = c + axis * (k / 2f - 1f);
                if (v.DistanceFromHull(v.Body.ToLocal(p.ToNumerics()), r + 1f) < r - 0.05f) { n++; break; }
            }
        }
        return n;
    }
}
