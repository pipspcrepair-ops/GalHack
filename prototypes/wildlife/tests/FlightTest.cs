using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// How winged animals fly, measured on the pure simulation over fresh random worlds (the seeds are
/// printed so a failure can be replayed):
///   godot --headless --path prototypes/wildlife res://tests/FlightTest.tscn
/// The rule under all of it: every winged species is a glider or a hoverer, by its body, and only a
/// hoverer ever holds still in the air, with its wings going too fast to see. Everything else is
/// always flying forward along its heading.
/// </summary>
public partial class FlightTest : Node
{
    const float Dt = 1f / 60f;

    WildlifeData _data;
    FaunaGenerator _gen;
    int _failures, _checks;
    readonly Random _rng = Random.Shared;

    /// <summary>Samples across every sim here of something airborne and going nowhere that isn't a hoverer on a rapid beat.</summary>
    int _stillAndNotHovering, _airSamples;

    public override void _Ready() => Run();

    void Run()
    {
        try
        {
            _data = DataFiles.LoadWildlife();
            _gen = new FaunaGenerator(_data.Fauna);
            KindsOfBody();
            GlidersNeverStop();
            WingsMatchTheFlight();
            TurnsAreBanked();
            Swoops();
            BiggerIsSlowerAndWider();
            StepAsideAndItMisses();
            KeepsOutOfThings();
            LandsAndTakesOff();
            RoostsAtRest();
            KilledInTheAir();
            Hoverers();
            HoverersSting();
            OthersStayAsTheyWere();
            Check(_stillAndNotHovering == 0 && _airSamples > 100000, $"in {_airSamples} samples of everything winged in the air here, nothing but a hoverer on a rapid beat ever held still ({_stillAndNotHovering} did)");
        }
        catch (Exception e)
        {
            Fail($"crashed: {e}");
        }
        GD.Print(_failures == 0 ? $"ALL PASSED ({_checks} checks)" : $"{_failures} OF {_checks} CHECK(S) FAILED");
        Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    // ---- Fixtures -----------------------------------------------------------------------------

    ulong Seed()
    {
        ulong seed = (ulong)_rng.NextInt64();
        GD.Print($"world: seed {seed}");
        return seed;
    }

    float Alien() => (float)_rng.NextDouble() * 0.9f;

    /// <summary>A species rolled on the spot, made into a hunter that goes for what it sees, at a given length if asked.</summary>
    Species Rolled(string form, Behaviour behaviour = Behaviour.Predator, float size = 0f)
    {
        Species s = _gen.RollOne(Seed(), form, behaviour, Alien()).Copy();
        if (size > 0f)
        {
            float k = size / s.Body.Size;
            Proportions b = s.Body;
            b.Size *= k; b.Width *= k; b.Height *= k; b.LegLength *= k; b.NeckLength *= k; b.HeadSize *= k; b.TailLength *= k;
            s.Stats.Radius *= k;
        }
        s.Activity = Activity.Cathemeral;
        s.Stats.Hunts = behaviour == Behaviour.Predator;
        s.Stats.Retaliates = true;
        s.Stats.Damage = MathF.Max(1f, s.Stats.Damage);
        s.Stats.Perception = 60f;
        s.Stats.FleeRadius = 0f;
        return s;
    }

    /// <summary>A range of real gliders: the two forms that glide, small to large.</summary>
    List<Species> Gliders(Behaviour b = Behaviour.Predator)
    {
        var list = new List<Species>();
        foreach (string form in new[] { "glider", "mothwing" })
        foreach (float size in new[] { 0f, 0.7f, 1.4f, 2.4f, 3.4f })
            list.Add(Rolled(form, b, size));
        return list;
    }

    WildSim Sim(Func<float, float, float> height = null) =>
        new(_data, new Terrain(height ?? Hills, 170f), new NavGrid(170f, 3f));

    static float Hills(float x, float z) => 4f + 3f * MathF.Sin(x * 0.041f) * MathF.Cos(z * 0.053f) + 1.2f * MathF.Sin(x * 0.11f + z * 0.07f);

    static DummyTarget Person(WildSim sim, Vector3 at)
    {
        var p = new DummyTarget { Name = "Person", Health = new Health(1e7f), Position = new Vector3(at.X, sim.Terrain.Height(at.X, at.Z), at.Z), Forward = Vector3.Forward };
        sim.Targets.Add(p);
        return p;
    }

    /// <summary>One step, counted towards the rule that nothing but a hoverer on a rapid beat holds still in the air.</summary>
    void Step(WildSim sim, Creature c)
    {
        sim.Step(Dt);
        if (c.Landed || c.Dead || c.Removed) return;
        _airSamples++;
        var rig = (WingedRig)c.Rig;
        bool still = WildSim.Flat(c.Velocity).Length() < 0.5f;
        if (still && !(c.Species.Flight == FlightKind.Hover && rig.BeatHz >= 20f && rig.Blurred)) _stillAndNotHovering++;
    }

    // ---- Which kind ---------------------------------------------------------------------------

    void KindsOfBody()
    {
        int winged = 0, gliders = 0, hoverers = 0, withHoverer = 0, worlds = 40;
        var wrong = new List<string>();
        for (int i = 0; i < worlds; i++)
        {
            ulong seed = (ulong)_rng.NextInt64();
            bool any = false;
            foreach (Species s in _gen.Generate(seed, Alien(), i % 3 == 0).Species)
            {
                if (s.Rig != RigKind.Winged) continue;
                winged++;
                FlightKind k = s.Flight;
                if (k == FlightKind.Hover)
                {
                    hoverers++;
                    any = true;
                    if (s.Body.Size > Species.HoverMostSize || s.Attack.Style != AttackStyle.Sting) wrong.Add($"{s} hovers at {s.Body.Size:F2} m with a {s.Attack.Style}");
                }
                else if (k == FlightKind.Glide)
                {
                    gliders++;
                    if (s.Body.Size <= Species.HoverMostSize || s.Attack.Style != AttackStyle.Dive) wrong.Add($"{s} glides at {s.Body.Size:F2} m with a {s.Attack.Style}");
                }
                else wrong.Add($"{s} is winged but neither kind");
            }
            if (any) withHoverer++;
        }
        Check(wrong.Count == 0 && gliders > 0 && hoverers > 0, $"every winged species of {worlds} fresh worlds is a glider ({gliders}) or a hoverer ({hoverers}), by its body: {string.Join("; ", wrong.Take(3))}");
        Note($"{withHoverer} of {worlds} worlds have a hoverer");

        // Rooms: nothing winged is put where it would need to turn round in a corridor.
        var deep = new List<string>();
        foreach (DeepRole role in Enum.GetValues<DeepRole>())
        {
            if (role == DeepRole.None) continue;
            foreach (string form in _data.Deep.Role(role).Forms.Keys)
                if (_data.Fauna.Forms.Find(f => f.Id == form)?.Rig == RigKind.Winged) deep.Add($"{role}: {form}");
        }
        Check(deep.Count == 0, $"no place or cave role uses a winged form, so no flyer is placed in a room it can't turn in: {string.Join(", ", deep)}");
    }

    // ---- Gliders ------------------------------------------------------------------------------

    void GlidersNeverStop()
    {
        float slowest = float.MaxValue, worstSide = 1f, worstOver = 0f, lowest = float.MaxValue;
        string slowWho = "", sideWho = "";
        int samples = 0, species = 0, offLine = 0, overTurn = 0, landings = 0;
        foreach (Species s in Gliders(Behaviour.Scavenger).Concat(Gliders(Behaviour.Grazer)))
        {
            species++;
            WildSim sim = Sim();
            Creature c = sim.Spawn(s, new Vector3(0f, 0f, 0f));
            float least = Heft.MinAirspeed(s), radius = Heft.TurnRadius(s);
            Vector3 was = c.Position;
            float yawWas = c.Yaw;
            bool wasUp = true;
            for (float t = 0f; t < 120f; t += Dt)
            {
                Step(sim, c);
                bool up = !c.Landed;
                if (wasUp && !up) landings++;
                if (up && wasUp)
                {
                    samples++;
                    Vector3 moved = WildSim.Flat(c.Position - was);
                    float speed = moved.Length() / Dt, along = moved.Dot(c.Forward) / Dt;
                    if (speed < slowest) { slowest = speed; slowWho = $"{s.CommonName}, {s.Body.Size:F1} m, in state {c.State}, needs {least:F1}"; }
                    if (speed < least * 0.97f) offLine++;
                    float side = speed > 0f ? along / speed : 1f;
                    if (side < worstSide) { worstSide = side; sideWho = $"{s.CommonName}, {s.Body.Size:F1} m, {c.State}"; }
                    float turning = MathF.Abs(Geo.WrapAngle(c.Yaw - yawWas)) / Dt;
                    if (turning > speed / radius * 1.05f + 0.05f) overTurn++;
                    worstOver = MathF.Max(worstOver, turning * radius / MathF.Max(speed, 0.1f));
                    lowest = MathF.Min(lowest, c.Position.Y - sim.Terrain.Height(c.Position.X, c.Position.Z) - Heft.StandHeight(s));
                }
                was = c.Position;
                yawWas = c.Yaw;
                wasUp = up;
            }
        }
        Check(offLine == 0, $"over {species} gliders and {samples} samples in the air, none fell below its stall: slowest {slowest:F2} m/s ({slowWho})");
        Check(worstSide > 0.985f, $"each only ever moved along its heading: no sliding, no reversing (worst {worstSide:F3} of its motion along its nose, {sideWho})");
        Check(overTurn == 0, $"each turned no tighter than its bank allows ({overTurn} over; at worst {worstOver:F2} of the limit)");
        Check(lowest > -0.06f, $"nothing went into the ground, however the hills came up (lowest {lowest:F2} m above standing height)");
        Note($"{landings} landings along the way");
    }

    void WingsMatchTheFlight()
    {
        Species s = Rolled("glider", Behaviour.HarmlessOddity, 1.4f);
        WildSim sim = Sim((x, z) => 0f); // on the level: hills make it work harder, as they should
        Creature c = sim.Spawn(s, Vector3.Zero);
        var rig = (WingedRig)c.Rig;
        float glide = 0f, longest = 0f, worstBeat = 0f, flapTime = 0f, airTime = 0f, climbNoFlap = 0f;
        for (float t = 0f; t < 180f; t += Dt)
        {
            Step(sim, c);
            if (c.Landed) { glide = 0f; continue; }
            airTime += Dt;
            bool flying = c.Flapping;
            if (flying) { flapTime += Dt; glide = 0f; }
            else
            {
                glide += Dt;
                longest = MathF.Max(longest, glide);
                if (glide > 1.2f) worstBeat = MathF.Max(worstBeat, rig.Beating);
            }
            if (c.Velocity.Y > 0.6f && !flying) climbNoFlap += Dt;
        }
        Check(longest > 3f, $"it has long glides: the longest {longest:F1} s, wings held out");
        Check(worstBeat < 0.1f, $"no flapping during a long glide: the wing beat is at most {worstBeat:F2} once a glide is over a second old");
        Check(climbNoFlap < 0.05f, $"it never climbs without working its wings ({climbNoFlap:F2} s of it)");
        Check(flapTime > 0f && flapTime / airTime < 0.45f, $"it flaps to climb and glides the rest: {flapTime / airTime * 100f:F0}% of its time in the air is flapping");
    }

    void TurnsAreBanked()
    {
        Species s = Rolled("glider", Behaviour.HarmlessOddity);
        WildSim sim = Sim((x, z) => 0f);
        Creature c = sim.Spawn(s, Vector3.Zero);
        var rig = (WingedRig)c.Rig;
        int inside = 0, turning = 0;
        for (float t = 0f; t < 60f; t += Dt)
        {
            Step(sim, c);
            if (MathF.Abs(c.TurnRate) < 0.3f) continue;
            turning++;
            if (MathF.Sign(rig.BodyRoll) == MathF.Sign(c.TurnRate)) inside++;
        }
        Check(turning > 300 && inside > turning * 0.97f, $"it rolls into its turns, inner wing down: {inside} of {turning} samples");
    }

    /// <summary>What a pass looked like: its tell, its dive and its climb out.</summary>
    sealed class Pass
    {
        public float TellAt, CommitAt, StrikeAt, TopY, LowY, AfterY;
        public bool Hit;
    }

    List<Pass> Watch(WildSim sim, Creature c, DummyTarget p, float seconds, out float worstInto, out string intoWho, out float slowest, Action<float> each = null)
    {
        var passes = new List<Pass>();
        Pass now = null;
        sim.Telegraph += e => { if (e.Creature == c) now = new Pass { TellAt = sim.Time, TopY = c.Position.Y, LowY = c.Position.Y }; };
        sim.Committed += e => { if (e.Creature == c && now != null) now.CommitAt = sim.Time; };
        sim.Attacked += e =>
        {
            if (e.Creature != c || now == null) return;
            now.StrikeAt = sim.Time;
            now.Hit = e.Hit;
            passes.Add(now);
        };
        worstInto = 0f;
        intoWho = "";
        slowest = float.MaxValue;
        float least = Heft.MinAirspeed(c.Species);
        for (float t = 0f; t < seconds; t += Dt)
        {
            each?.Invoke(t);
            Step(sim, c);
            if (c.Landed) continue;
            slowest = MathF.Min(slowest, WildSim.Flat(c.Velocity).Length() / least);
            if (now != null)
            {
                now.TopY = MathF.Max(now.TopY, now.StrikeAt == 0f ? c.Position.Y : now.TopY);
                if (now.StrikeAt == 0f) now.LowY = MathF.Min(now.LowY, c.Position.Y);
                else if (sim.Time - now.StrikeAt < 4f) now.AfterY = MathF.Max(now.AfterY, c.Position.Y);
            }
            for (int i = 0; i < c.Rig.HitCount; i++)
            {
                Vector4 h = c.Rig.Hits[i];
                if (h.Y - h.W > p.Position.Y + Attacks.BodyHeight) continue;
                float into = h.W + Attacks.BodyRadius - WildSim.Flat(new Vector3(h.X, 0f, h.Z) - p.Position).Length();
                if (into > worstInto) { worstInto = into; intoWho = $"{c.Species.CommonName} {c.Species.Body.Size:F1} m, part {i}, {c.Phase}"; }
            }
        }
        return passes;
    }

    /// <summary>Rays that stop at the ground, as the game's do: a look ahead must not take the ground for a wall.</summary>
    static void SeesTheGround(WildSim sim, Func<Vector3, bool> solid = null) => sim.SightLine = (a, b) =>
    {
        for (int i = 0; i <= 24; i++)
        {
            Vector3 q = a.Lerp(b, i / 24f);
            if (q.Y < sim.Terrain.Height(q.X, q.Z) || (solid?.Invoke(q) ?? false)) return false;
        }
        return true;
    };

    void Swoops()
    {
        int sized = 0, withTwo = 0, climbing = 0, fair = 0, hits = 0, passes = 0, dropped = 0;
        float shortestWarning = float.MaxValue, worst = 0f, slowest = float.MaxValue;
        string who = "";
        foreach (Species s in Gliders())
        {
            sized++;
            WildSim sim = Sim();
            SeesTheGround(sim);
            DummyTarget p = Person(sim, new Vector3(0f, 0f, 0f));
            Creature c = sim.Spawn(s, new Vector3(30f, 0f, 22f));
            List<Pass> list = Watch(sim, c, p, 70f, out float into, out string intoWho, out float slow);
            if (into > worst) { worst = into; who = intoWho; }
            slowest = MathF.Min(slowest, slow);
            if (list.Count >= 2) withTwo++;
            foreach (Pass x in list)
            {
                passes++;
                if (x.Hit) hits++;
                float warning = x.StrikeAt - x.TellAt;
                shortestWarning = MathF.Min(shortestWarning, warning);
                if (x.TopY - x.LowY > 3f) dropped++;
                if (x.AfterY - x.LowY > 2f) climbing++;
                if (x.CommitAt > x.TellAt && warning >= Swoop.LeastTell(s) + 0.5f) fair++;
            }
        }
        Check(withTwo == sized, $"every glider of {sized} sizes and species comes back for more: {withTwo} made two passes or more in 70 s ({passes} passes)");
        Check(dropped == passes, $"every pass dives: {dropped} of {passes} dropped over 3 m to reach you");
        Check(climbing >= passes - sized, $"and climbs away after it: {climbing} of {passes} gained over 2 m inside 4 s (the last of a run may be cut off)");
        Check(fair == passes && shortestWarning > 2f, $"every pass gives time to dodge or shoot: at least {shortestWarning:F1} s from the tell to the strike");
        Check(hits > passes * 0.5f, $"a person who stands there gets hit: {hits} of {passes} passes");
        Check(worst < 0.05f, $"nothing clips through the person: at worst {worst:F2} m into them ({who})");
        Check(slowest >= 0.99f, $"never stationary, not at the tell, the strike or the turn: at worst {slowest:F2} of its stall");
    }

    void BiggerIsSlowerAndWider()
    {
        Species small = Rolled("glider", Behaviour.Predator, 0.9f), big = Rolled("glider", Behaviour.Predator, 3.2f);
        float[] Measure(Species s)
        {
            WildSim sim = Sim((x, z) => 0f);
            DummyTarget p = Person(sim, Vector3.Zero);
            Creature c = sim.Spawn(s, new Vector3(30f, 0f, 22f));
            float fastest = 0f, furthest = 0f;
            bool seen = false;
            for (float t = 0f; t < 70f; t += Dt)
            {
                Step(sim, c);
                if (c.AirAimOn) { fastest = MathF.Max(fastest, WildSim.Flat(c.Velocity).Length()); seen = true; }
                if (seen) furthest = MathF.Max(furthest, WildSim.Flat(c.Position).Length());
            }
            return new[] { fastest, furthest, Heft.TurnRadius(s) };
        }
        float[] a = Measure(small), b = Measure(big);
        Check(b[0] < a[0], $"a bigger body stoops slower: {a[0]:F1} m/s at 0.9 m, {b[0]:F1} m/s at 3.2 m");
        Check(b[1] > a[1], $"and swings wider between passes: out to {a[1]:F0} m against {b[1]:F0} m");
        Check(b[2] > a[2] * 1.8f, $"and turns wider: a {a[2]:F1} m radius against {b[2]:F1} m");
    }

    void StepAsideAndItMisses()
    {
        int misses = 0, runs = 0;
        foreach (Species s in Gliders())
        {
            runs++;
            WildSim sim = Sim();
            DummyTarget p = Person(sim, Vector3.Zero);
            Creature c = sim.Spawn(s, new Vector3(30f, 0f, 22f));
            bool moved = false, missed = false;
            sim.Committed += e =>
            {
                if (moved) return;
                moved = true;
                // Out of the way, the moment it commits: a couple of good strides.
                Vector3 sideways = WildSim.Flat(c.AimPoint - c.Position).Cross(Vector3.Up).Normalized();
                p.Position += sideways * (2.4f + c.Radius * 0.4f + _data.Combat.ContactRadius * 0.5f);
                p.Position = new Vector3(p.Position.X, sim.Terrain.Height(p.Position.X, p.Position.Z), p.Position.Z);
            };
            sim.Attacked += e => { if (!missed) { missed = true; if (!e.Hit) misses++; else GD.Print($"  note: not dodged: {s.CommonName} {s.Body.Size:F1} m, {WildSim.Flat(p.Position - c.Position).Length():F1} m off at the strike, {WildSim.Flat(c.AimPoint - c.Position).Length():F1} m from its aim"); } };
            for (float t = 0f; t < 90f && !missed; t += Dt) Step(sim, c);
            if (!missed) GD.Print($"  note: never struck in 90 s: {s.CommonName} {s.Body.Size:F1} m, {c.State} {c.Phase}, moved {moved}, {WildSim.Flat(c.Position - p.Position).Length():F0} m off");
        }
        Check(misses == runs, $"it strikes where you stood at the commit: stepping aside dodged {misses} of {runs}");
    }

    void KeepsOutOfThings()
    {
        // A tower beside the person, of the kind a ruin has; nothing but the world's own rays know it's there.
        Vector3 lo = new(-7f, -5f, 5f), hi = new(7f, 60f, 18f);
        bool Inside(Vector3 q) => q.X > lo.X && q.X < hi.X && q.Y > lo.Y && q.Y < hi.Y && q.Z > lo.Z && q.Z < hi.Z;
        int rays = 0;
        int entered = 0, fights = 0, passes = 0;
        float seconds = 0f;
        string how = "";
        foreach (Species s in Gliders())
        {
            fights++;
            WildSim sim = Sim((x, z) => 0f);
            SeesTheGround(sim, q => Inside(q));
            Func<Vector3, Vector3, bool> ground = sim.SightLine;
            sim.SightLine = (a, b) => { rays++; return ground(a, b); };
            DummyTarget p = Person(sim, Vector3.Zero);
            Creature c = sim.Spawn(s, new Vector3(30f, 0f, -22f));
            sim.Attacked += e => passes++;
            for (float t = 0f; t < 60f; t += Dt)
            {
                Step(sim, c);
                seconds += Dt;
                if (Inside(c.Position)) { entered++; how = $"{s.CommonName} {s.Body.Size:F1} m, {c.Phase}, {WildSim.Flat(c.Velocity).Length():F1} m/s, avoiding {c.AvoidFor:F2}"; }
            }
        }
        Check(entered == 0, $"nothing flew into a building beside its target ({fights} fights, {passes} passes round it, {entered} samples inside: {how})");
        Check(rays / seconds < 16f * 3f, $"looking ahead costs little: {rays / seconds:F1} rays a second per flyer, dives included");

        // A ridge in the way of the dive: it clears it or breaks off, and never goes through.
        float Ridge(float x, float z) => MathF.Max(0f, 14f - MathF.Abs(z - 14f) * 1.2f);
        int under = 0;
        foreach (Species s in Gliders())
        {
            WildSim sim = Sim(Ridge);
            DummyTarget p = Person(sim, Vector3.Zero);
            Creature c = sim.Spawn(s, new Vector3(4f, 0f, 45f));
            float stand = Heft.StandHeight(s);
            for (float t = 0f; t < 40f; t += Dt)
            {
                Step(sim, c);
                if (!c.Landed && c.Position.Y < sim.Terrain.Height(c.Position.X, c.Position.Z) + stand - 0.06f) under++;
            }
        }
        Check(under == 0, $"it pulls up over a ridge in its path in good time ({under} samples under the ground)");
    }

    void LandsAndTakesOff()
    {
        int landed = 0, tried = 0, flew = 0, gentle = 0, stayedFast = 0, ranOff = 0, smooth = 0;
        foreach (Species s in Gliders(Behaviour.Grazer))
        {
            tried++;
            WildSim sim = Sim();
            SeesTheGround(sim);
            Person(sim, new Vector3(20f, 0f, 20f)).Attackable = false;
            Creature c = sim.Spawn(s, Vector3.Zero);
            var rig = (WingedRig)c.Rig;
            float least = Heft.MinAirspeed(s);
            for (int i = 0; i < 200; i++) Step(sim, c);
            c.SetState(CState.Graze);
            c.IdleFor = 3f;
            float steepest = 0f, slowest = float.MaxValue, jump = 0f, clearance = rig.BodyClearance;
            bool down = false, wentUp = false;
            float runFrom = 0f, ran = 0f, liftSpeed = 0f;
            Vector3 was = c.Position;
            for (float t = 0f; t < 90f && !wentUp; t += Dt)
            {
                bool before = c.Landed;
                bool runningBefore = c.TakingOff;
                Step(sim, c);
                float height = c.Position.Y - sim.Terrain.Height(c.Position.X, c.Position.Z);
                if (!c.Landed && !down) { slowest = MathF.Min(slowest, WildSim.Flat(c.Velocity).Length() / least); if (height < 8f) steepest = MathF.Max(steepest, -c.Velocity.Y / MathF.Max(1f, WildSim.Flat(c.Velocity).Length())); }
                if (c.Landed && !before) { down = true; landed++; }
                jump = MathF.Max(jump, MathF.Abs(rig.BodyClearance - clearance));
                clearance = rig.BodyClearance;
                if (c.TakingOff && !runningBefore) runFrom = t;
                if (before && !c.Landed)
                {
                    wentUp = true;
                    flew++;
                    ran = t - runFrom;
                    liftSpeed = WildSim.Flat(c.Velocity).Length() / least;
                }
                was = c.Position;
            }
            if (steepest < 0.45f) gentle++;
            if (slowest >= 0.99f) stayedFast++;
            if (ran > 0.15f && liftSpeed >= 0.99f) ranOff++;
            if (jump < 0.35f) smooth++;
        }
        Check(landed == tried, $"it comes down to feed: {landed} of {tried} landed");
        Check(gentle == tried, $"on a gliding approach, not a drop: {gentle} of {tried} stayed shallow");
        Check(stayedFast == tried, $"flying to the last: {stayedFast} of {tried} held flying speed down to the ground");
        Check(flew == tried && ranOff == tried, $"and it runs itself back up to flying speed to lift off: {ranOff} of {tried}");
        Check(smooth == tried, $"with no jump in the body at either end: {smooth} of {tried}");
    }

    void RoostsAtRest()
    {
        int down = 0, tried = 0;
        foreach (Species s in Gliders(Behaviour.Scavenger).Take(6))
        {
            tried++;
            s.Activity = Activity.Nocturnal;
            s.RestKind = RestKind.Roost;
            WildSim sim = Sim();
            sim.ActivityCycles = true;
            Creature c = sim.Spawn(s, Vector3.Zero);
            sim.AssignRest(new List<Creature> { c });
            sim.Clock.Time01 = 0.5f;
            for (float t = 0f; t < 90f && !(c.Landed && c.State == CState.Rest); t += Dt) Step(sim, c);
            if (c.Landed && c.State == CState.Rest) down++;
        }
        Check(down == tried, $"out of its hours it flies to its roost and settles there: {down} of {tried}");
    }

    void KilledInTheAir()
    {
        int arcs = 0, tried = 0, rigs = 0;
        foreach (Species s in Gliders(Behaviour.HarmlessOddity).Concat(new[] { Rolled("darter", Behaviour.HarmlessOddity) }))
        {
            tried++;
            WildSim sim = Sim((x, z) => 0f);
            Creature c = sim.Spawn(s, Vector3.Zero);
            for (int i = 0; i < 120; i++) sim.Step(Dt);
            float startY = c.Position.Y, speed = WildSim.Flat(c.Velocity).Length();
            Vector3 from = c.Position;
            sim.Damage(c, c.Health + 1f, null);
            bool followed = true;
            float t = 0f;
            for (; t < 12f && c.Position.Y > 0.02f; t += Dt)
            {
                sim.Step(Dt);
                if (c.Position.Y > 0.5f && MathF.Abs(c.Rig.BoundsCenter.Y - c.Position.Y) > 0.3f) followed = false;
            }
            float run = WildSim.Flat(c.Position - from).Length();
            bool arc = c.Position.Y <= 0.02f && (s.Flight == FlightKind.Hover ? run >= 0f : run > speed * 0.5f);
            if (arc) arcs++;
            if (followed) rigs++;
        }
        Check(arcs == tried, $"a flyer killed in the air falls along the line it was flying, down to the ground: {arcs} of {tried}");
        Check(rigs == tried, $"and its body falls with it instead of dropping to the ground at once: {rigs} of {tried}");
    }

    // ---- Hoverers -----------------------------------------------------------------------------

    void Hoverers()
    {
        int held = 0, tried = 0, beat = 0, upright = 0, backed = 0, darted = 0, slowBeat = 0;
        for (int n = 0; n < 8; n++)
        {
            tried++;
            Species s = Rolled("darter", Behaviour.HarmlessOddity);
            if (s.Flight != FlightKind.Hover) Fail($"a darter of {s.Body.Size:F2} m came out as {s.Flight}");
            WildSim sim = Sim();
            Creature c = sim.Spawn(s, Vector3.Zero);
            var rig = (WingedRig)c.Rig;
            for (int i = 0; i < 60; i++) Step(sim, c);
            // Left to itself: it darts from place to place and holds.
            float fastest = 0f, stillFor = 0f, longestStill = 0f, leastBeat = float.MaxValue, lean = 0f;
            for (float t = 0f; t < 60f; t += Dt)
            {
                Step(sim, c);
                if (c.Landed) continue;
                float speed = WildSim.Flat(c.Velocity).Length();
                fastest = MathF.Max(fastest, speed);
                leastBeat = MathF.Min(leastBeat, rig.Blurred ? rig.BeatHz * rig.Beating : 0f);
                stillFor = speed < 0.1f ? stillFor + Dt : 0f;
                longestStill = MathF.Max(longestStill, stillFor);
                if (stillFor > 0.5f) lean = MathF.Max(lean, rig.BodyPitch);
            }
            if (longestStill > 1f) held++;
            if (leastBeat >= 18f) beat++;
            else slowBeat++;
            if (lean > 0.8f) upright++;
            if (fastest > 5f) darted++;

            // Crowded, it backs off without turning its back.
            var q = Rolled("darter", Behaviour.HarmlessOddity);
            WildSim near = Sim();
            DummyTarget p = Person(near, Vector3.Zero);
            Creature d = near.Spawn(q, new Vector3(0f, 0f, 2.5f));
            d.Noticed = p;
            d.NoticedAt = near.Time;
            d.Awareness = 1f;
            bool back = false;
            for (float t = 0f; t < 6f; t += Dt)
            {
                Step(near, d);
                if (d.State == CState.Follow && d.Velocity.Dot(d.Forward) < -0.5f) back = true;
            }
            if (back) backed++;
        }
        Check(held == tried, $"a hoverer holds still in the air: {held} of {tried} held over a second");
        Check(beat == tried, $"on wings that never stop or slow while it is up, a blur at 18 Hz or more ({slowBeat} slowed)");
        Check(upright == tried, $"with its body upright under them: {upright} of {tried}");
        Check(darted == tried, $"it darts: {darted} of {tried} went over 5 m/s");
        Check(backed == tried, $"and backs up, facing what it backs from: {backed} of {tried}");
    }

    void HoverersSting()
    {
        int tried = 0, heldTell = 0, stung = 0, backedOff = 0;
        float worst = 0f;
        string who = "";
        for (int n = 0; n < 6; n++)
        {
            tried++;
            Species s = Rolled("darter", Behaviour.Predator);
            s.Stats.Damage = 5f;
            WildSim sim = Sim();
            DummyTarget p = Person(sim, Vector3.Zero);
            Creature c = sim.Spawn(s, new Vector3(5f, 0f, 3f));
            bool hit = false, still = true, away = false;
            sim.Attacked += e => hit |= e.Hit;
            for (float t = 0f; t < 25f; t += Dt)
            {
                Step(sim, c);
                if (c.Phase == AttackPhase.Windup && c.PhaseTime > 0.5f && WildSim.Flat(c.Velocity).Length() > 0.6f) still = false;
                if (c.Phase == AttackPhase.Recover && c.Velocity.Dot(c.Forward) < -0.3f) away = true;
                if (c.Phase == AttackPhase.Lunge) continue;
                for (int i = 0; i < c.Rig.HitCount; i++)
                {
                    Vector4 h = c.Rig.Hits[i];
                    if (h.Y - h.W > Attacks.BodyHeight + p.Position.Y) continue;
                    float into = h.W + Attacks.BodyRadius - WildSim.Flat(new Vector3(h.X, 0f, h.Z) - p.Position).Length();
                    if (into > worst) { worst = into; who = $"{c.Phase} {c.State}, part {i} of radius {h.W:F2} at height {h.Y - p.Position.Y:F2}, body {WildSim.Flat(c.Position - p.Position).Length():F2} m off"; }
                }
            }
            if (still) heldTell++;
            if (hit) stung++;
            if (away) backedOff++;
        }
        Check(heldTell == tried, $"a hoverer gives its tell holding still in front of you: {heldTell} of {tried}");
        Check(stung == tried, $"darts in and stings: {stung} of {tried}");
        Check(backedOff == tried, $"and backs off again: {backedOff} of {tried}");
        Check(worst < 0.25f, $"never through you: at worst {worst:F2} m into your body ({who})");
    }

    void OthersStayAsTheyWere()
    {
        Species bag = _gen.RollOne(Seed(), "gasbag", Behaviour.HarmlessOddity, 0.4f), cloud = _gen.RollOne(Seed(), "cloud", Behaviour.Swarm, 0.4f);
        Check(bag.Flight == FlightKind.None && cloud.Flight == FlightKind.None, "gas bags and swarm clouds are neither kind");
        WildSim sim = Sim();
        Creature b = sim.Spawn(bag, Vector3.Zero);
        float top = 0f;
        for (float t = 0f; t < 30f; t += Dt)
        {
            sim.Step(Dt);
            top = MathF.Max(top, WildSim.Flat(b.Velocity).Length());
        }
        Check(top < 2.5f, $"a gas bag still just drifts ({top:F1} m/s at most)");
    }

    // ---- Reporting ----------------------------------------------------------------------------

    void Check(bool ok, string what)
    {
        _checks++;
        if (ok) GD.Print($"  ok: {what}");
        else Fail(what);
    }

    void Note(string what) => GD.Print($"  note: {what}");

    void Fail(string what)
    {
        _failures++;
        GD.PrintErr($"FAIL: {what}");
        GD.Print($"FAIL: {what}");
    }
}
