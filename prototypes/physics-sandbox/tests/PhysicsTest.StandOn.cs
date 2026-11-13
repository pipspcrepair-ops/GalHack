using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using ShipHandling.Handling;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox;

/// <summary>
/// Standing on built shapes (docs/one-metre-grid.md, section 8): the walker goes onto, over and off
/// every block shape in every one of its 24 orientations (each distinct one once), stairs too, on a
/// hand-built yard at 1 m cells, on a base and aboard a vessel (its cabin world). Then a small built
/// base: a thin-plate landing pad, a sloped roof, an open door (walked and jetpacked into), a two-cell
/// container's edge. Pass: never inside a block and never rescued, no teleport, no bounce over 5 mm in
/// 2 s standing still, and whatever a shape's own profile says is walkable is walked across.
/// (-- --stand-on runs only this.)
/// </summary>
public partial class PhysicsTest
{
    /// <summary>One block to try: a shape (or the stairs) in one orientation, on its own lane.</summary>
    sealed record Lane(string Name, Block Block, SInt3 Origin);

    /// <summary>What happened on a structure: tallies per kind of failure, and the first few cases of each.</summary>
    sealed class StandLog
    {
        public int Cases, Crossed, Walls, Rescues, InsideTicks, Teleports, Bounces, Blocked, Launches;
        public float WorstBounce;
        public readonly List<string> Bad = new();
        public void Note(string what) { if (Bad.Count < 30) Bad.Add(what); }
    }

    /// <summary>The player (or a creature) on one structure, in its frame (structure-local metres), watched every tick it is run.</summary>
    sealed class StandRig
    {
        readonly PhysicsTest _t;
        public readonly Ship On;
        public readonly bool Aboard;
        public readonly Creature Who;
        public int Inside, Teleports;
        public float Jump;
        N.Vector3 _last;
        int _settle;

        public StandRig(PhysicsTest t, Ship on, bool aboard, Creature who = null)
        {
            _t = t;
            On = on;
            Aboard = aboard;
            Who = who;
        }

        Player P => _t._w.Player;
        public Body Body => Who?.Body ?? P.Body;
        public WalkController Legs => Who?.Legs ?? P.Legs;
        public N.Vector3 Local => Aboard ? Body.Position : On.Body.ToLocal(Body.Position);

        public float Yaw
        {
            set
            {
                if (Who != null) Who.Legs.Yaw = value;
                else P.Yaw = value;
            }
        }

        public void Place(N.Vector3 local, float yaw)
        {
            Yaw = yaw;
            P.Pitch = 0f;
            Legs.Move = N.Vector2.Zero;
            Legs.Jump = Legs.Jetpack = false;
            if (Who != null)
            {
                Who.Body.Position = On.Body.ToParent(local);
                Who.Body.Velocity = N.Vector3.Zero;
                Who.Body.OnFloor = false;
                Who.Binding.Teleport();
            }
            else if (Aboard) P.PlaceAboard(On, local);
            else P.PlaceOutside(On.Body.ToParent(local));
            _last = Local;
            _settle = 5; // set down by hand: the engine may nudge a wide body clear of what it was set beside

        }

        /// <summary>One tick: never inside a block, never further than a stride (a step up included) in a tick.</summary>
        public IEnumerable<int> Tick()
        {
            yield return 0;
            if (_t._w.Physics.Stuck.IsStuck(Body, out _)) Inside++;
            N.Vector3 now = Local;
            if (_settle > 0) _settle--;
            // More than a step up and on (Steps.Up moves on by at least 0.4 of the radius) and a tick's stride: a teleport.
            else if (N.Vector3.Distance(now, _last) > _t._w.Rules.Walking.StepHeight + 0.4f * Body.Shape.Radius + 0.25f)
            {
                Teleports++;
                Jump = MathF.Max(Jump, N.Vector3.Distance(now, _last));
            }
            _last = now;
        }

        public IEnumerable<int> Ticks(int n)
        {
            for (int i = 0; i < n; i++)
                foreach (int x in Tick()) yield return x;
        }

        /// <summary>Stands still for 2 s after settling: how far it went up and down (a slide off an edge only goes down, and is no bounce).</summary>
        public IEnumerable<int> Stand(Action<float> bounce)
        {
            Legs.Move = N.Vector2.Zero;
            Legs.Jump = Legs.Jetpack = false;
            foreach (int x in Ticks(40)) yield return x;
            float lo = float.PositiveInfinity, hi = float.NegativeInfinity, up = 0f, prev = Local.Y;
            for (int i = 0; i < 120; i++)
            {
                foreach (int x in Tick()) yield return x;
                float y = Local.Y;
                lo = MathF.Min(lo, y);
                hi = MathF.Max(hi, y);
                up = MathF.Max(up, y - prev);
                prev = y;
            }
            bounce(up > 0.002f ? hi - lo : 0f);
        }
    }

    IEnumerable<int> StandOnShapes()
    {
        Catalogue cat = _w.Catalogue;
        var lanes = new List<Lane>();
        var seen = new HashSet<string>();
        BlockType steel = cat.Type("iron-plated"), stairs = cat.Type("stairs");
        void Add(string name, Block b)
        {
            // Each distinct geometry once: the shape's turned points (or the stairs' turned boxes).
            Mat3 m = Orientations.Matrix(b.Rot);
            IEnumerable<N.Vector3> pts = cat.Types[b.Type].IsArmor ? cat.Shapes[b.Shape].HullPoints.Select(m.Apply)
                : cat.Types[b.Type].Parts.SelectMany(p => new[] { m.Apply(p.Min), m.Apply(p.Max) });
            string key = name.Split(' ')[0] + ":" + string.Join(";", pts.Select(p => $"{MathF.Round(p.X, 2) + 0f},{MathF.Round(p.Y, 2) + 0f},{MathF.Round(p.Z, 2) + 0f}").OrderBy(x => x));
            if (!seen.Add(key)) return;
            int k = lanes.Count;
            lanes.Add(new Lane(name, b, new SInt3((k % 16) * 4, 0, (k / 16) * 10)));
        }
        var probe = new Structure(cat);
        foreach (ShapeDef shape in cat.Shapes)
            for (int rot = 0; rot < Orientations.Count; rot++)
                Add($"{shape.Id} rot {rot}", Building.Make(probe, steel, shape, rot));
        for (int rot = 0; rot < Orientations.Count; rot++)
            Add($"stairs rot {rot}", Building.Make(probe, stairs, null, rot));
        GD.Print($"  ({lanes.Count} distinct shapes and turns: {string.Join(", ", lanes.GroupBy(l => l.Name.Split(' ')[0]).Select(g => $"{g.Key} {g.Count()}"))})");
        int rescuesBefore = _w.Physics.Stuck.Rescues;

        // On a base, standing in the zone's world.
        Structure yard = Yard(cat, lanes);
        Ship pad = Ship.SpawnBase(_w, yard, _w.Ground(-110f, -110f) + N.Vector3.UnitY * 40f, 0f);
        for (int i = 0; i < 5; i++) yield return 0;
        var onBase = new StandLog();
        var rig = new StandRig(this, pad, aboard: false);
        foreach (Lane lane in lanes)
            foreach (int x in TryLane(rig, yard, lane, onBase)) yield return x;
        Report("on a base", onBase);

        _w.Player.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f); // out of the beasts' way
        // An animal (a 0.45 m by 1.4 m walker) and a giant (1.2 m by 4 m, never capped to a person's size) on the same base:
        // one of each kind on every lane at once.
        foreach (var (label, radius, height) in new[] { ("an animal", 0.45f, 1.4f), ("a giant", 1.2f, 4f) })
        {
            var log = new StandLog();
            var beasts = lanes.Select(l => Creature.Spawn(_w, pad.Body.ToParent(new N.Vector3(l.Origin.X, 0.6f, l.Origin.Z + 3f)), radius, height)).ToList();
            foreach (int x in Together(lanes.Select((l, i) => TryLane(new StandRig(this, pad, aboard: false, beasts[i]), yard, l, log)))) yield return x;
            Report($"{label} on a base", log, standOnly: radius > 1f);
            foreach (Creature beast in beasts) _w.Remove(beast);
        }
        _w.Remove(pad);

        // Aboard a vessel in orbit under its core's gravity: the cabin world.
        Structure yard2 = Yard(cat, lanes);
        Ship ship = Ship.SpawnVessel(_w, yard2, new N.Vector3(300f, _w.Physics.Zone.Ceiling + 100f, 300f), 0f, landed: false);
        ship.Flight.Input = PilotInput.LetGo;
        ship.Body.Core.On = true;
        ship.Cabin.SetGravity(ship.Body.Core);
        for (int i = 0; i < 5; i++) yield return 0;
        var aboard = new StandLog();
        rig = new StandRig(this, ship, aboard: true);
        foreach (Lane lane in lanes)
            foreach (int x in TryLane(rig, yard2, lane, aboard)) yield return x;
        Report("aboard a vessel (its cabin world)", aboard);
        _w.Remove(ship);

        foreach (int x in SmallBase(cat)) yield return x;
        _w.Player.PlaceOutside(_w.Ground(1.5f, 8.5f) + N.Vector3.UnitY * 0.05f);
        for (int i = 0; i < 5; i++) yield return 0;
        GD.Print($"  (the stuck guard fired {_w.Physics.Stuck.Rescues - rescuesBefore} times in all of the above)");
    }

    /// <summary>Runs walks side by side, a tick of each per tick.</summary>
    static IEnumerable<int> Together(IEnumerable<IEnumerable<int>> walks)
    {
        List<IEnumerator<int>> running = walks.Select(w => w.GetEnumerator()).ToList();
        while (running.Count > 0)
        {
            running.RemoveAll(r => !r.MoveNext());
            yield return 0;
        }
    }

    /// <summary><paramref name="standOnly"/>: a walker too wide for a lane's 3 m deck is judged on standing, not on crossing.</summary>
    void Report(string where, StandLog log, bool standOnly = false)
    {
        foreach (string bad in log.Bad) GD.Print($"    {bad}");
        Check(log.Rescues == 0 && log.InsideTicks == 0 && log.Teleports == 0,
            $"{where}: {log.Cases} shapes walked and stood on, never inside a block or rescued ({log.InsideTicks} ticks inside, {log.Rescues} rescues), no teleport ({log.Teleports})");
        Check(log.Bounces == 0, $"{where}: standing still 2 s on each, none bounces over 5 mm (worst {log.WorstBounce * 1000f:F1} mm, {log.Bounces} bouncing)");
        if (!standOnly) Check(log.Blocked == 0 && log.Launches == 0,
            $"{where}: every shape its profile says is walkable is walked across ({log.Crossed} crossed, {log.Walls} walls stop it, {log.Blocked} stuck on a walkable one, {log.Launches} thrown up)");
    }

    /// <summary>A deck lane per block: 3 cells wide, 9 long, the block in the middle one cell up; a core off to the side.</summary>
    static Structure Yard(Catalogue cat, List<Lane> lanes)
    {
        var s = new Structure(cat) { Name = "Stand-on yard", Mode = CoreMode.Base, Owner = Building.DefaultOwner, GravityOn = true };
        BlockType steel = cat.Type("iron-plated");
        s.Set(new SInt3(-4, 0, 0), Building.Make(s, cat.CoreType));
        foreach (Lane l in lanes)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -4; z <= 4; z++)
                    s.Set(l.Origin + new SInt3(x, 0, z), Building.Make(s, steel));
            s.Set(l.Origin + new SInt3(0, 1, 0), l.Block);
        }
        s.DirtyChunks.Clear();
        return s;
    }

    /// <summary>The top of whatever is solid in a column (structure frame, cells), from <paramref name="from"/> down; <paramref name="floor"/> if nothing.</summary>
    static float TopAt(Structure s, float x, float z, float from, float floor)
    {
        for (float y = from; y > floor; y -= 0.01f)
            if (s.SolidAt(new N.Vector3(x, y, z))) return y;
        return floor;
    }

    /// <summary>
    /// Walkable across, by the block's own shape along every line the walker's width covers: every rise is either a ramp no
    /// steeper than the walker climbs or a step (a run of steeper rise) no taller than a step.
    /// </summary>
    bool Walkable(Structure s, SInt3 o, float radius)
    {
        float step = _w.Rules.Walking.StepHeight, deck = o.Y + 0.5f, dz = 0.02f, ramp = dz * MathF.Tan(45.5f * MathF.PI / 180f);
        for (int k = -2; k <= 2; k++)
        {
            // A rise steeper than a ramp is a step for as long as it keeps rising so steeply: the whole of it counts.
            float x = o.X + MathF.Min(radius, 0.49f) * k / 2f, last = deck, steep = 0f;
            for (float z = o.Z + 0.6f; z >= o.Z - 0.6f; z -= dz)
            {
                float h = TopAt(s, x, z, o.Y + 1.49f, deck), rise = h - last;
                steep = rise > ramp + 1e-3f ? steep + rise : 0f;
                if (steep > step) return false;
                last = h;
            }
        }
        return true;
    }

    IEnumerable<int> TryLane(StandRig rig, Structure s, Lane lane, StandLog log)
    {
        float cell = s.Grid.CellSize;
        SInt3 o = lane.Origin;
        log.Cases++;
        int rescues = _rescues.Count(r => r.Body == rig.Body);
        rig.Inside = rig.Teleports = 0;
        rig.Jump = 0f;

        // Across: from the deck before it, walking forward (-z) over it and off the far side.
        rig.Place(new N.Vector3(o.X, o.Y + 0.5f, o.Z + 3f) * cell + N.Vector3.UnitY * 0.02f, 0f);
        foreach (int x in rig.Ticks(10)) yield return x;
        int launches = 0;
        bool crossed = false;
        for (int t = 0; t < 240 && !crossed; t++)
        {
            rig.Legs.Move = new N.Vector2(0f, -1f);
            foreach (int x in rig.Tick()) yield return x;
            if (N.Vector3.Dot(rig.Body.Velocity, rig.Body.Up) > 4f) launches++;
            crossed = rig.Local.Z < (o.Z - 2f) * cell;
        }
        rig.Legs.Move = N.Vector2.Zero;
        if (crossed) log.Crossed++;
        else if (!Walkable(s, o, rig.Body.Shape.Radius)) log.Walls++;
        else
        {
            log.Blocked++;
            log.Note($"{lane.Name}: walkable by its profile, but the walker stopped at z {rig.Local.Z - o.Z * cell:F2} m, y {rig.Local.Y - (o.Y + 0.5f) * cell:F2} m");
        }
        if (launches > 0)
        {
            log.Launches++;
            log.Note($"{lane.Name}: thrown up {launches} ticks walking across");
        }

        // Stand still on top: on the highest point under the feet (the capsule's foot rests there).
        float top = float.NegativeInfinity, r = rig.Body.Shape.Radius;
        for (float dx = -r; dx <= r + 1e-3f; dx += 0.05f)
            for (float dz = -r; dz <= r + 1e-3f; dz += 0.05f)
                if (dx * dx + dz * dz <= r * r + 1e-4f) top = MathF.Max(top, TopAt(s, o.X + dx, o.Z + dz, o.Y + 1.49f, o.Y + 0.5f));
        rig.Place(new N.Vector3(o.X, top, o.Z) * cell + N.Vector3.UnitY * 0.03f, 0f);
        foreach (int x in rig.Stand(b =>
        {
            log.WorstBounce = MathF.Max(log.WorstBounce, b);
            if (b <= 0.005f) return;
            log.Bounces++;
            log.Note($"{lane.Name}: standing still, {b * 1000f:F0} mm up and down");
        })) yield return x;

        int rescued = _rescues.Count(r => r.Body == rig.Body) - rescues;
        log.Rescues += rescued;
        log.InsideTicks += rig.Inside;
        log.Teleports += rig.Teleports;
        if (rescued > 0 || rig.Inside > 0 || rig.Teleports > 0) log.Note($"{lane.Name}: {rig.Inside} ticks inside a block, {rescued} rescues, {rig.Teleports} teleports (up to {rig.Jump:F1} m)");
    }

    /// <summary>
    /// A small base as a player builds one on the 1 m grid: a deck, a 7×7 room three blocks high with an open 1×2 door,
    /// a flat roof with a ridge of slopes on it, a landing pad of thin plates, a two-cell container. Each is walked onto,
    /// jumped or jetpacked onto, stood on and walked off; the door is walked through both ways and jetpacked into.
    /// </summary>
    IEnumerable<int> SmallBase(Catalogue cat)
    {
        var s = new Structure(cat) { Name = "Small base", Mode = CoreMode.Base, Owner = Building.DefaultOwner, GravityOn = true };
        BlockType steel = cat.Type("iron-plated");
        ShapeDef slope = cat.Shape("slope"), plate = cat.Shape("plate");
        int back = Enumerable.Range(0, Orientations.Count).First(r =>
            Orientations.Matrix(r).Apply(N.Vector3.UnitY) == N.Vector3.UnitY && Orientations.Matrix(r).Apply(N.Vector3.UnitZ) == -N.Vector3.UnitZ);
        void Put(int x, int y, int z, ShapeDef shape = null, int rot = 0) => s.Set(new SInt3(x, y, z), Building.Make(s, steel, shape, rot));
        s.Set(new SInt3(-2, 0, -6), Building.Make(s, cat.CoreType));
        for (int x = -2; x <= 16; x++)
            for (int z = -6; z <= 14; z++)
                if (x != -2 || z != -6) Put(x, 0, z);
        for (int y = 1; y <= 3; y++)
            for (int x = 0; x <= 6; x++)
                for (int z = 0; z <= 6; z++)
                    if ((x == 0 || x == 6 || z == 0 || z == 6) && !(x == 3 && z == 6 && y <= 2)) Put(x, y, z);
        Block door = Building.Make(s, cat.Type("door-1x2x1"));
        door.Flags |= BlockFlags.Open;
        bool doorIn = Building.Place(s, new SInt3(3, 1, 6), door) == PlaceError.None;
        for (int x = 0; x <= 6; x++)
        {
            for (int z = 0; z <= 6; z++) Put(x, 4, z);
            Put(x, 5, 1, slope);
            for (int z = 2; z <= 4; z++) Put(x, 5, z);
            Put(x, 5, 5, slope, back);
        }
        for (int x = 9; x <= 13; x++)
            for (int z = 0; z <= 4; z++) Put(x, 1, z, plate);
        bool boxIn = Building.Place(s, new SInt3(10, 1, 9), Building.Make(s, cat.Type("container-t4"))) == PlaceError.None;
        s.DirtyChunks.Clear();

        Ship b = Ship.SpawnBase(_w, s, _w.Ground(40f, -110f) + N.Vector3.UnitY * 30f, 0f);
        for (int i = 0; i < 5; i++) yield return 0;
        var rig = new StandRig(this, b, aboard: false);
        Player p = _w.Player;
        int rescues = _w.Physics.Stuck.Rescues;
        var bad = new List<string>();
        float worst = 0f;
        void Bounce(string what, float m)
        {
            worst = MathF.Max(worst, m);
            if (m > 0.005f) bad.Add($"{what}: {m * 1000f:F0} mm up and down standing still");
        }
        IEnumerable<int> Walk(float yaw, Func<N.Vector3, bool> done, int max, bool jump = false, int jet = 0)
        {
            p.Yaw = yaw;
            for (int t = 0; t < max && !done(rig.Local); t++)
            {
                p.Legs.Move = new N.Vector2(0f, -1f);
                p.Legs.Jump = jump && t == 0;
                p.Legs.Jetpack = t < jet;
                foreach (int x in rig.Tick()) yield return x;
            }
            p.Legs.Move = N.Vector2.Zero;
            p.Legs.Jump = p.Legs.Jetpack = false;
        }
        IEnumerable<int> Landed()
        {
            for (int t = 0; t < 120 && !p.Body.OnFloor; t++)
                foreach (int x in rig.Tick()) yield return x;
        }
        const float Deck = 0.5f, North = 0f, South = MathF.PI;

        // The door: in from outside and out again; then jetpacked into from a run-up, and walked out of.
        rig.Place(new N.Vector3(3f, Deck + 0.02f, 10f), North);
        foreach (int x in Walk(North, l => l.Z < 3f, 240)) yield return x;
        bool inside = rig.Local.Z < 3.5f;
        foreach (int x in Walk(South, l => l.Z > 9f, 240)) yield return x;
        bool outAgain = rig.Local.Z > 8.5f;
        Check(doorIn && inside && outAgain, $"the base's open door is walked through, in and out again (in {inside}, out {outAgain})");
        rig.Place(new N.Vector3(3f, Deck + 0.02f, 12f), North);
        // A short burst at the opening, as a player hopping in: it carries the sleeper into the doorway's head height.
        foreach (int x in Walk(North, l => l.Z < 5.5f, 150, jump: true, jet: 14)) yield return x;
        foreach (int x in rig.Ticks(60)) yield return x;
        foreach (int x in Walk(South, l => l.Z > 9f, 240)) yield return x;
        foreach (int x in Landed()) yield return x;
        Check(rig.Local.Z > 8.5f && MathF.Abs(rig.Local.Y - Deck) < 0.1f, $"jetpacked into the doorway, then walked out of it onto the deck (at z {rig.Local.Z:F1}, {rig.Local.Y - Deck:F2} m over the deck)");

        // The pad of thin plates: walked onto, stood on, jumped onto, stood on.
        rig.Place(new N.Vector3(11f, Deck + 0.02f, 7.5f), North);
        foreach (int x in Walk(North, l => l.Z < 2f, 240)) yield return x;
        bool onPad = rig.Local.Z < 2.5f && rig.Local.Y > Deck + 0.1f;
        foreach (int x in rig.Stand(m => Bounce("on the pad, walked onto", m))) yield return x;
        rig.Place(new N.Vector3(11f, Deck + 0.02f, 6f), North);
        foreach (int x in Walk(North, l => l.Z < 3f, 120, jump: true)) yield return x;
        foreach (int x in rig.Stand(m => Bounce("on the pad, jumped onto", m))) yield return x;
        bool jumpedOn = rig.Local.Y > Deck + 0.1f;

        // The roof: stood on, walked up the slope onto the ridge, down the far slope and off the edge to the deck.
        rig.Place(new N.Vector3(3f, 4.52f, 0f), South);
        foreach (int x in rig.Stand(m => Bounce("on the roof", m))) yield return x;
        bool onRoof = rig.Local.Y > 4.4f;
        float ridge = 0f;
        foreach (int x in Walk(South, l => { ridge = MathF.Max(ridge, l.Y); return l.Z > 8f; }, 300)) yield return x;
        foreach (int x in Landed()) yield return x;
        Check(onRoof && ridge > 5.3f && rig.Local.Z > 7.5f && MathF.Abs(rig.Local.Y - Deck) < 0.1f,
            $"the roof is stood on, and walked up its slope onto the ridge ({ridge:F2} m), down the far slope and off its edge to the deck");

        // The container's top: stood on in its middle, and at its very edge, half the feet over it.
        rig.Place(new N.Vector3(10.5f, 1.52f, 9f), North);
        foreach (int x in rig.Stand(m => Bounce("on the container", m))) yield return x;
        bool onBox = rig.Local.Y > 1.4f;
        rig.Place(new N.Vector3(11f, 1.52f, 9.35f), North);
        foreach (int x in rig.Stand(m => Bounce("on the container's edge", m))) yield return x;

        foreach (string what in bad) GD.Print($"    {what}");
        int rescued = _w.Physics.Stuck.Rescues - rescues;
        Check(boxIn && onPad && jumpedOn && onBox && bad.Count == 0,
            $"the base's thin-plate pad (walked onto {onPad}, jumped onto {jumpedOn}), roof and two-cell container (on top {onBox}, and at its edge) are stood on without a bounce (worst {worst * 1000f:F1} mm)");
        Check(rescued == 0 && rig.Inside == 0 && rig.Teleports == 0,
            $"and on the small base the player is never inside a block ({rig.Inside} ticks), never rescued ({rescued}), never teleported ({rig.Teleports})");
        _w.Remove(b);
    }
}
