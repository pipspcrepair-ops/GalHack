using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using PoiGenerator.Gen;
using Slice.Surface;
using Wildlife;
using PInt3 = PoiGenerator.Gen.Int3;
using PNav = PoiGenerator.Gen.NavGrid;

namespace Slice.Places;

/// <summary>
/// A lived-in place's people, kept very simple (the owner: "just give the impression of going
/// about doing things"). From the generator's townsfolk data: vendors man their stalls, workers
/// work, guards hold their posts and walk short beats, rushers hurry between far spots,
/// wanderers amble to near ones and sit or chat in pairs; at night everyone but the guards goes
/// indoors. The place's mood decides how they take you: peaceful, wary (the guards watch you) or
/// hostile (the guards shoot, after taking aim where you can see it). Shooting in a peaceful
/// place turns it hostile and its people flee indoors. Its animals are wildlife's domestic kinds
/// (working beasts and stock only where the tech is low, stock on farms, pets anywhere); wildlife
/// keeps out of the town, the guards cull strays, and outside the town a predator may take one of
/// its people. Bodies come from the bestiary: each person is rolled for this world, this place and
/// their job, walks on real legs, and from a distance is one cheap mesh.
/// </summary>
public sealed partial class Town
{
    public PlaceMemory Memory;
    public Townsfolk Data;
    public readonly List<Person> People = new();
    /// <summary>Paths between linked spots (worked out once), in local cells' walk points.</summary>
    readonly Dictionary<(int, int), List<Vector3>> _paths = new();
    PNav _nav;
    /// <summary>The place's furniture and clutter as it really stands (null: the old whole-cell rule, tests only).</summary>
    Wildlife.PartSet _parts;
    /// <summary>The same without the doors' leaves: what routes are worked out on (doors are opened on the way).</summary>
    Wildlife.PartSet _fixed;
    int[] _homes;
    string _tech;

    Place _place;
    PoiPlaces _places;
    Node3D _view;
    Settlement _settlement;
    readonly List<(Creature Creature, DomesticKind Kind, Vector3 Pen)> _animals = new();
    readonly Random _rng;

    /// <summary>Shots the guards have fired at you, and their aims shown before each (tests).</summary>
    public int GuardShots { get; private set; }
    public int GuardAims { get; private set; }

    Town(PlaceMemory m, Townsfolk data)
    {
        Memory = m;
        Data = data;
        _rng = new Random((int)(m.Inst.SpawnSeed & 0x7fffffff));
    }

    public sealed partial class Person : IHost
    {
        /// <summary>Seized by a Husk and turned: one of them now, out in the wild sim; this one is gone.</summary>
        public bool Gone;
        /// <summary>A silent carrier: the worms are in them, and nothing shows until they are alone with someone (<see cref="Carriers"/>).</summary>
        public bool Carrier;
        Species IHost.Body => Looks;
        void IHost.Taken()
        {
            Gone = true;
            Health.Current = 0f;
            if (View != null) View.Visible = false;
        }

        public Town Town;
        public CitizenSpawn Spawn;
        public string Role => Spawn.Role;
        public Vector3 Pos;                // local feet
        public float Yaw;
        public int Spot, Home;
        public List<Vector3> Path;
        public int Step;
        public float Wait, Speed = 1.3f, Hurt, Aim = -1f, Cooldown;
        /// <summary>How fast they are going now (they ease into it), how far through a stride, how far down into a seat; time since their last full update.</summary>
        public float Now, Gait, Seated, Saved;
        /// <summary>Who they are, how they stand, and what draws them (once near enough to have been built).</summary>
        public Species Looks;
        public HumanRig Rig;
        public PersonView View;
        public Figure Figure => Looks.Figure;
        /// <summary>How far over they have gone (the dead), how far their rifle is up, how far bent to work.</summary>
        public float Down, Raised, Bent;
        public bool Laid;
        /// <summary>Their nerve (set once), and when they act on the latest upset: nobody reacts on the same instant.</summary>
        public float Nerve = 0.5f, ReactAt = -1f;
        public bool Reacted = true;
        /// <summary>Their meshes, worked out ahead on a worker.</summary>
        public PersonView.Ready Ready;
        /// <summary>What their body is saying, how far into it (eased), how long it holds at least, and what's next.</summary>
        public BodyAct Act, Wanted;
        public float ActAmount, ActHeld;
        /// <summary>Greeted you on this approach; aimed at (with hysteresis); you were close last tick.</summary>
        public bool Greeted, Covered, Close;
        public float StartleFor;
        /// <summary>Gesture changes, and those that came back within a second (tests: there should be none).</summary>
        public int ActChanges, Flickers;
        public float LastChange = -9f;
        public BodyAct Before;
        public float HeadYaw;
        public bool Indoors, Sitting, Fleeing;
        /// <summary>In a seat, or up out of it and not yet clear of it (and of the table it was drawn up to).</summary>
        public bool GettingUp;
        /// <summary>How long they have been held where they stood by the furniture.</summary>
        public float Blocked;
        /// <summary>In bed for the night (lying on it), and how far down into it.</summary>
        public bool Asleep;
        public float Slept;
        /// <summary>Far enough from you that their day is kept as a schedule, not walked.</summary>
        public bool Far;
        /// <summary>The spot they last left (a seat they are getting up out of).</summary>
        public int Left = -1;
        /// <summary>The seat they sat down in and haven't yet got up out of (-1: none).</summary>
        public int Seat = -1;
        /// <summary>Their job (from the generator), which sets their day; out of town on it (a scavenger's run); bent to a task where they are.</summary>
        public PersonRole Job;
        public bool Away, Working;
        public float AwayFor;
        /// <summary>At a checkpoint or a post: how long they've had you halted, and whether their gun is on you (never fired outside a hostile place).</summary>
        public float Halt;
        public bool Holding;
        /// <summary>A creature they're dealing with, how long until their shot at it, how long they're scared off or waving one away; whether they're working stock.</summary>
        public Creature Threat;
        public float Facing, ScaredFor, ShooFor;
        public bool Herding;
        /// <summary>Their answer to your gesture (from when, for how long), and where they've been shown to look.</summary>
        /// <summary>You robbed them (they remember); a guard on your trail after a robbery, for how long.</summary>
        public bool Robbed;
        /// <summary>Their own mood (see Town.Mood.cs).</summary>
        public Feeling Feel = new();
        /// <summary>Waiting a moment for someone to pass (a doorway); how long since you last pushed them.</summary>
        public float Yield, ShovedFor;
        /// <summary>When a vessel last hit them (clock).</summary>
        public float StruckAt = -9f;
        /// <summary>How long you've been leaning on them.</summary>
        public float PressFor;
        public float Pursue;
        public BodyAct Answer;
        public float AnswerAt, AnswerFor, LookFor;
        public Vector3 LookAt;
        public Person Partner;
        public Health Health { get; } = new(60f);
        public bool Dead => Health.Current <= 0f;

        // As wildlife sees them (the patch's frame): hunted only outside the town.
        public string Name => $"{Spawn.Role}";
        public Vector3 Position => Town._places == null ? Pos : Town._places.Zone.Wildlife.PatchLocal(Town.World(Pos));
        public Vector3 Forward => new(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        public bool Attackable => !Dead && !Indoors && !Away;
        public float ThreatScale => 1f;
    }

    /// <summary>A place's town (worker-safe), or null where nobody lives: walking routes worked out once round the room dressing.</summary>
    public static Town For(PlaceMemory m, PlanetSurface.World.PlanetWorld planet = null)
    {
        Townsfolk data = m.Inst.Townsfolk;
        if (data == null || data.People.Count == 0 || data.Spots.Count < 2) return null;
        var town = new Town(m, data);
        town._tech = m.Inst.Layout.Variant?.Tech is { Length: > 0 } t ? t : m.Inst.Family.Tags.Contains("settlement") ? "low" : "mid";
        town._nav = Place.FootNavOf(m, planet);
        town._parts = PlaceParts.Of(m);
        town._fixed = PlaceParts.Fixed(m);
        town._ways = DoorWays.Of(m);
        m.Bodies.Add(town.BodiesNear);
        town.Furnish(m);
        foreach (var (a, b) in data.Links.Concat(data.Routes)) town.PathBetween(a, b);
        town._homes = town.Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag == "home").Select(t => t.i).ToArray();
        // A place with no quarters of its own still has rooms with a roof: its people go in there for the night.
        if (town._homes.Length == 0)
            town._homes = town.Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag != "bed" && t.s.Module >= 0 && t.s.Module < m.Inst.Layout.Modules.Count && m.Inst.Layout.Modules[t.s.Module].Kind == ModuleKind.Room).Select(t => t.i).ToArray();
        return town;
    }

    /// <summary>A cell's walking point: the middle of its floor (among furniture, where a body fits nearest it), or halfway up the stair or ramp standing in it.</summary>
    Vector3 Walk(PInt3 c)
    {
        if (_nav != null && _nav.RampAt(c, out _)) return new(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.5f);
        return (_fixed != null ? Place.PersonSpot(_fixed, Place.WallsOf(_nav), c) : null) ?? new Vector3(c.X + 0.5f, c.Y, c.Z + 0.5f);
    }

    /// <summary>
    /// A route's walking points: each cell's (<see cref="Walk"/>), and between two cells where furniture bars the
    /// straight way from one's point to the next's, the gap on the face between them it goes by.
    /// </summary>
    List<Vector3> Points(List<PInt3> cells)
    {
        if (cells == null) return null;
        var path = new List<Vector3>(cells.Count);
        Func<int, int, int, bool> walls = _fixed != null ? Place.WallsOf(_nav) : null;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 here = Walk(cells[i]);
            if (i > 0 && walls != null && !_nav.RampAt(cells[i], out _) && !_nav.RampAt(cells[i - 1], out _))
            {
                PInt3 a = cells[i - 1], b = cells[i];
                if (_fixed.Link(new Vector3I(a.X, a.Y, a.Z), path[^1], new Vector3I(b.X, b.Y, b.Z), here, PlaceParts.PersonRadius, PlaceParts.PersonHeight, walls, PlaceParts.PersonRadius, out Vector3[] vias))
                    path.AddRange(vias);
            }
            path.Add(here);
        }
        return path;
    }

    List<Vector3> PathBetween(int a, int b)
    {
        if (_paths.TryGetValue((a, b), out var p)) return p;
        if (_paths.TryGetValue((b, a), out var q))
        {
            // The way back is the way there walked the other way (from the spot's own point, to the other's).
            if (q == null) return _paths[(a, b)] = null;
            var back = Enumerable.Reverse(q).ToList();
            if ((At(a) - Walk(Spots[a].Cell)).LengthSquared() > 0.01f) back.RemoveAt(0);
            if (back.Count == 0 || (At(b) - back[^1]).LengthSquared() > 0.01f) back.Add(At(b));
            return _paths[(a, b)] = back;
        }
        PInt3 from = Spots[a].Cell, to = Spots[b].Cell;
        // By the walking grid only (the dressing standing in, through the doors): where there is no way, nobody goes,
        // never straight across through a wall, a floor or the furniture.
        List<PInt3> cells = _nav.Standable(from) && _nav.Standable(to) ? _nav.FindPath(from, to, 30000) : null;
        List<Vector3> path = Points(cells);
        if (path != null && (At(b) - path[^1]).LengthSquared() > 0.01f) path.Add(At(b));
        _paths[(a, b)] = path;
        return path;
    }

    /// <summary>Everyone, at their spots (once): each with a bed of their own for the night where the rooms have beds.</summary>
    void Populate()
    {
        if (People.Count > 0) return;
        foreach (CitizenSpawn s in Data.People)
        {
            var p = new Person { Town = this, Spawn = s, Pos = At(s.Spot), Spot = s.Spot, Yaw = (float)_rng.NextDouble() * MathF.Tau };
            p.Home = _homes.Length > 0 ? _homes.OrderBy(h => Spots[h].Cell.Manhattan(s.Cell)).First() : s.Spot;
            p.Speed = s.Gait switch { "hurry" => 3.0f, "amble" => 1.1f, "patrol" => 1.3f, _ => 1.3f };
            p.Wait = (float)_rng.NextDouble() * 6f;
            People.Add(p);
            Bed(p);
        }
    }

    /// <summary>What a tick of everyone in the town cost last time, ms.</summary>
    public float LastTickMs { get; private set; }

    public Vector3 World(Vector3 local) => _place != null ? _place.ToWorld(local) : local;

    // ---- Standing and sitting ---------------------------------------------------------------------


    public void Stand(Place place, PoiPlaces places)
    {
        _place = place;
        _places = places;
        _view = new Node3D { Name = "Townsfolk" };
        place.AddChild(_view);
        Populate();
        // Who they are and what they're drawn with is worked out on a worker: a town coming into view costs the frame nothing.
        if (People.Count > 0 && People[0].Looks == null && _rolling == null)
        {
            WorldLife life = places.Zone.Wildlife.Life;
            _rolling = System.Threading.Tasks.Task.Run(() =>
            {
                RollPeople(life);
                foreach (Person p in People) p.Ready = PersonView.Prepare(p.Rig);
            });
        }
        WildlifePatch patch = places.Zone.Wildlife;
        Register(patch.Sim, null, Vector3.Zero);
        patch.Swapped += OnSwapped;
        patch.Bitten += OnBitten;
        patch.Died += OnDied;
        var ticker = new Ticker { Name = "Ticker", Town = this };
        _view.AddChild(ticker);
    }

    public void Sit(Place place)
    {
        WildlifePatch patch = _places.Zone.Wildlife;
        patch.Swapped -= OnSwapped;
        patch.Bitten -= OnBitten;
        patch.Died -= OnDied;
        if (patch.Sim != null)
        {
            patch.Sim.Settlements.Remove(_settlement);
            foreach (Person p in People) patch.Sim.Targets.Remove(p);
            foreach (var a in _animals) patch.Sim.Remove(a.Creature);
            patch.Sim.StrayEntered -= OnStray;
        }
        _animals.Clear();
        DropSolids();
        foreach (Person p in People) p.View = null;
        _view = null;
        _place = null;
    }

    /// <summary>The town in the patch's sim: its area (wildlife keeps out), its people (hunted only outside it), its animals.</summary>
    void Register(WildSim sim, WildSim old, Vector3 oldOrigin)
    {
        if (sim == null) return;
        WildlifePatch patch = _places.Zone.Wildlife;
        Vector3 centre = World(Walk(Data.AreaCenter) with { Y = 0 });
        _settlement = sim.AddSettlement(Memory.Site.Name, patch.PatchLocal(centre), Data.AreaRadius);
        sim.StrayEntered += OnStray;
        foreach (Person p in People)
            if (!p.Dead && !sim.Targets.Contains(p)) sim.Targets.Add(p);
        var before = _animals.ToList();
        _animals.Clear();
        if (before.Count > 0)
        {
            // Across a patch swap the animals come with their pens, where they stood.
            foreach (var (c, kind, pen) in before)
            {
                if (c.Dead || c.Removed) continue;
                Vector3 at = c.Position + oldOrigin - patch.Origin;
                _animals.Add((sim.SpawnDomestic(c.Species, at, patch.PatchLocal(pen), c.PenRadius, c.Owner), kind, pen));
            }
            return;
        }
        WorldLife life = patch.Life;
        int n = 0;
        foreach (var (kindName, cell) in Data.Animals)
        {
            DomesticKind kind = kindName switch { "stock" => DomesticKind.Stock, "working" => DomesticKind.Working, "pet" => DomesticKind.Pet, _ => DomesticKind.None };
            // Working beasts only where the tech is low; stock where it is low or on a farm; pets anywhere.
            if (kind == DomesticKind.None || kind == DomesticKind.Working && _tech != "low"
                || kind == DomesticKind.Stock && _tech != "low" && !Memory.Inst.Family.Tags.Contains("farm")) continue;
            Species s;
            try { s = life.Domestic(kind, n++); }
            catch (Exception) { continue; }
            Vector3 pen = World(Walk(cell));
            int count = kind == DomesticKind.Stock ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                Vector3 at = pen + new Vector3(i * 1.5f - 1.5f, 0, (i % 2) * 1.2f);
                ITarget owner = kind == DomesticKind.Pet ? People.FirstOrDefault(p => p.Home == Spots.FindIndex(sp => sp.Cell == cell)) : null;
                _animals.Add((sim.SpawnDomestic(s, patch.PatchLocal(at), patch.PatchLocal(pen), kind == DomesticKind.Stock ? 5f : 3f, owner), kind, pen));
            }
        }
    }

    void OnSwapped(WildSim old, Vector3 oldOrigin, WildSim sim)
    {
        if (old != null) old.StrayEntered -= OnStray;
        Register(sim, old, oldOrigin);
    }

    /// <summary>A wild creature got in: the nearest guard deals with it.</summary>
    void OnStray(Creature c, Settlement town)
    {
        if (town != _settlement) return;
        if (c.Species.Husk) return; // a Husk is met by temper: fought, fled or barricaded against (Town.Reactions)
        Person guard = People.Where(p => p.Role == "guard" && !p.Dead).OrderBy(p => (p.Position - c.Position).LengthSquared()).FirstOrDefault();
        _places.Zone.Wildlife.Sim.CullStray(c, guard);
        Culled++;
    }

    /// <summary>Strays the guards have culled (tests).</summary>
    public int Culled { get; private set; }

    /// <summary>You killed something dangerous at their door: they're glad of it.</summary>
    void OnDied(Creature c, ITarget killer)
    {
        if (killer == null || killer != _places.Zone.Wildlife.Player || !(c.Species.Husk || CrossReactions.Danger(c.Species))) return;
        if (_place != null && Covers(_places.Zone.Wildlife.WorldOf(c)) || _place != null && (_place.FromWorld(_places.Zone.Wildlife.WorldOf(c)) - Walk(Data.AreaCenter)).Length() < Data.AreaRadius + 25f) Did(Deed.Help);
    }

    /// <summary>A creature went for one of the town's people (outside the town, where they can be hunted).</summary>
    void OnBitten(AttackEvent e)
    {
        if (e.Target is not Person p || p.Town != this || !e.Hit) return;
        p.Health.Current -= e.Damage * 0.6f;
        if (p.Dead) Fall(p);
    }

    // ---- The routine ------------------------------------------------------------------------------

    /// <summary>Steps the town's people once a physics tick while the place stands.</summary>
    sealed partial class Ticker : Node
    {
        public Town Town;
        public override void _PhysicsProcess(double delta)
        {
            long from = System.Diagnostics.Stopwatch.GetTimestamp();
            Town.Tick((float)delta);
            Town.LastTickMs = (float)System.Diagnostics.Stopwatch.GetElapsedTime(from).TotalMilliseconds;
        }
    }

    bool Night()
    {
        float t = (float)_places.Zone.Sky.TimeOfDay;
        return t < 0.24f || t > 0.8f;
    }

    /// <summary>Bodies built a tick at most.</summary>
    const int BodiesPerTick = 3;
    /// <summary>Close enough that someone sees you and answers with their body (greets, waves you off, startles), m.</summary>
    const float ActRange = 48f;
    int _tick;

    void Tick(float tickDt)
    {
        if (_place == null || !_place.Ready || _place.Dormant || !Rolled) return;
        bool night = Night();
        float time = (float)_places.Zone.Sky.TimeOfDay;
        Vector3 player = _place.FromWorld(_places.Zone.Wildlife.PlayerWorld());
        // Where you are looking, in the place's frame: what you point at, people see.
        Vector3 eye = player + Vector3.Up * 1.6f;
        ITarget you = _places.Zone.Wildlife.Player;
        Vector3 look = you == null ? Vector3.Forward : (_place.FromWorld(_places.Zone.Wildlife.PlayerWorld() + you.Forward) - player);
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward;
        _tick++;
        _clock += tickDt;
        Voices(player, tickDt);
        Worms(player, tickDt);
        Sense(tickDt);
        Word(tickDt);
        MoodTick(tickDt);
        // Whoever a Husk has by the head stays where they are taken.
        _held.Clear();
        if (_places.Zone.Wildlife.Sim is { } wild)
            foreach (Creature c in wild.Creatures)
                if (c.Phase == AttackPhase.Hold && c.Holding is Person h && h.Town == this) _held.Add(h);
        int built = 0;
        for (int who = 0; who < People.Count; who++)
        {
            Person p = People[who];
            if (p.Gone) continue;
            if (_held.Contains(p) && !p.Dead)
            {
                p.Now = 0f;
                Pose(p, who, (player - p.Pos).Length(), tickDt);
                continue;
            }
            if (p.Dead)
            {
                // Going down, then lying where they fell; the hurt flash fades whatever happens.
                if (p.View == null) MakeBody(p);
                p.Hurt = MathF.Max(0f, p.Hurt - tickDt);
                float lying = (player - p.Pos).Length();
                if (p.Down >= 1f && p.Laid)
                {
                    // Down: only seen or not as you come and go, and posed where they lie when they come into view.
                    p.Saved += tickDt;
                    if ((_tick + who) % PersonView.PoseEvery(lying) != 0) continue;
                    bool was = p.View.Visible;
                    if (Shown(p, lying, p.Saved) && !was) PoseBody(p, who, p.Saved);
                    p.Saved = 0f;
                    continue;
                }
                // Laid once the limp body has come to rest (it falls as a ragdoll, onto whatever is under it).
                p.Laid = p.Down >= 1f && p.Rig.Settled && p.Hurt <= 0f;
                p.Down = MathF.Min(1f, p.Down + tickDt * 1.8f);
                Pose(p, who, lying, tickDt);
                continue;
            }
            if (p.View == null && built < BodiesPerTick) { MakeBody(p); built++; }
            // Far from you a town needn't move every tick: each in their turn, with the time they missed,
            // the further off the fewer times a second (and so their bodies are posed).
            p.Saved += tickDt;
            float away = (player - p.Pos).Length();
            p.Far = away > FarSchedule;
            if ((_tick + who) % PersonView.PoseEvery(away) != 0) continue;
            float dt = p.Saved;
            p.Saved = 0f;
            if (away < ActRange && p.Looks != null && !p.Asleep) Acts(p, player, eye, look, away, dt);
            bool guard = p.Role == "guard";
            p.Hurt = MathF.Max(0f, p.Hurt - dt);
            // Until it reaches them, they carry on as they were; then a start, and they act.
            Mood mood = Memory.Mood;
            if (!p.Reacted)
            {
                if (_clock < p.ReactAt) mood = _moodBefore;
                else { p.Reacted = true; p.Path = null; p.Step = 0; p.Partner = null; p.StartleFor = 0.7f; }
            }
            // Their day over (each job keeps its own hours), or the place turned hostile: indoors.
            // The guards and the law keep their posts round the clock, and turn on you.
            bool offDuty = !guard && p.Looks != null ? !p.Figure.OutAt(time) : night;
            p.ScaredFor = MathF.Max(0f, p.ScaredFor - dt);
            p.ShovedFor = MathF.Max(0f, p.ShovedFor - dt);
            p.ShooFor = MathF.Max(0f, p.ShooFor - dt);
            // Something dangerous about: those who answer it with a gun do; the scared run in; a barricaded place keeps everyone in.
            if (Fight(p, dt))
            {
                Pose(p, who, away, dt);
                continue;
            }
            // A hostile place's fighters fight you (Town.Combat); the rest go to their doors.
            if (Combat(p, player, mood, dt))
            {
                Pose(p, who, away, dt);
                continue;
            }
            bool scared = p.ScaredFor > 0f || Barricaded;
            bool fighter = mood == Mood.Hostile && Fighter(p) && p.ScaredFor <= 0f;
            bool hide = !guard && (offDuty || mood == Mood.Hostile && !fighter || scared);
            if (hide && p.Away) { p.Away = false; p.Pos = At(p.Spot); } // back from a run in time for the night
            if (p.Away)
            {
                // Out of town on a scavenger's run: back after a while, by the way in.
                if ((p.AwayFor -= dt) <= 0f) p.Away = false;
                Pose(p, who, away, dt);
                continue;
            }
            if (hide && !p.Indoors && !p.Asleep)
            {
                // Home, wherever they were going: only reaching their own bed or door takes them in.
                if (p.Spot != p.Home || p.Path == null) GoTo(p, p.Home);
                p.Fleeing = mood == Mood.Hostile || scared;
                p.Speed = p.Fleeing ? 3.4f : MathF.Max(p.Speed, 1.5f); // home before dark: a brisk walk
                Arrive(p, dt, offDuty && !p.Fleeing);
                Pose(p, who, away, dt);
                continue;
            }
            // Morning, or the fright over, or a fright in the night: up and about.
            if (!hide || p.Asleep && (mood == Mood.Hostile || scared)) Wake(p);
            if (p.Indoors || p.Asleep) { Pose(p, who, away, dt); continue; }
            if (guard && mood != Mood.Hostile && Pursuit(p, player, dt))
            {
                Pose(p, who, away, dt);
                continue;
            }
            if (guard && mood != Mood.Hostile && Checkpoint(p, player, mood, dt))
            {
                Pose(p, who, away, dt);
                continue;
            }
            // Unwelcome, you: off their post, they make for it before anything else (watching you on the way).
            if (guard && mood != Mood.Hostile && Unwelcome && (p.Pos - At(p.Spawn.Spot)).Length() > 2f)
            {
                if (p.Spot != p.Spawn.Spot || p.Path == null || p.Step >= p.Path.Count) GoTo(p, p.Spawn.Spot);
                Move(p, dt);
                Pose(p, who, away, dt);
                continue;
            }
            if (guard && mood != Mood.Peaceful && (player - p.Pos).Length() < 40f)
            {
                Guard(p, player, mood, dt);
                Pose(p, who, away, dt);
                continue;
            }
            if (Move(p, dt))
            {
                p.Wait -= dt;
                if (p.Wait <= 0f) Next(p);
                else Idle(p, dt);
            }
            Pose(p, who, away, dt);
        }
        KeepApart(player, tickDt);
        Solids(player, tickDt);
    }

    /// <summary>Walks a person along their path; true once they are there (or have no path).</summary>
    bool Move(Person p, float dt)
    {
        if (p.Yield > 0f && p.Path != null && p.Step < p.Path.Count)
        {
            // Letting someone through first: a step back out of their way.
            p.Yield -= dt;
            Vector3 back = new(MathF.Sin(p.Yaw), 0f, MathF.Cos(p.Yaw));
            if (Open(p.Pos + back * 0.5f * dt)) p.Pos += back * 0.5f * dt;
            p.Now = Mathf.MoveToward(p.Now, 0f, PeopleAccel * 2f * dt);
            return false;
        }
        if (p.Path == null || p.Step >= p.Path.Count)
        {
            p.Now = 0f;
            // Standing about: still off the furniture (a tall one under a hanging lamp steps out from under it).
            OffParts(p, p.Pos);
            return true;
        }
        // The doors ahead opened as they come (a door they may not open: they wait where they are).
        if (!ThroughDoors(p)) { p.Now = 0f; return true; }
        Vector3 to = p.Path[p.Step];
        Vector3 d = to - p.Pos;
        float flat = new Vector2(d.X, d.Z).Length();
        // Eases into a walk and out of it, slowing only for the last of the way; corners keep their pace.
        bool last = p.Step >= p.Path.Count - 1;
        float pace = p.Speed * p.Feel.Pace; // their mood sets their pace
        float wanted = last ? MathF.Min(pace, MathF.Max(0.3f, MathF.Sqrt(2f * PeopleAccel * flat))) : pace;
        p.Now = Mathf.MoveToward(p.Now, wanted, PeopleAccel * dt);
        float step = p.Now * dt;
        p.Gait += step;
        // Turns a corner a stride early, not on the spot.
        // (Not out of a seat or into one: that way is walked as it lies, clear of the furniture round it.)
        bool exact = p.Step == 0 && p.Seat >= 0 || p.Step >= p.Path.Count - 2 && p.Spot >= 0 && p.Spot < _own.Count && _own[p.Spot] != null;
        // (Among furniture the corner is turned at the point itself: the gap it goes through is no wider.)
        float cut = last || exact ? 0.05f : _fixed != null && _fixed.Touches(to, PlaceParts.PersonRadius, PlaceParts.PersonHeight) ? 0.1f : 0.4f;
        if (flat < step || flat < cut)
        {
            if (last)
            {
                p.Pos = to;
                if (p.Spot >= 0 && p.Spot < _own.Count && _own[p.Spot] != null) { p.Seat = p.Spot; p.GettingUp = true; }
            }
            p.Step++;
            return p.Step >= p.Path.Count;
        }
        Vector3 was = p.Pos;
        if (p.Sitting) p.GettingUp = true;
        p.Pos += new Vector3(d.X, 0f, d.Z) / flat * step;
        if (p.Seat >= 0 && !InSeat(p, p.Seat)) p.Seat = -1; // up and out of it
        p.Pos.Y = Mathf.Lerp(p.Pos.Y, to.Y, MathF.Min(1f, step / MathF.Max(flat, 0.01f)));
        // Feet on what is under them: the floor, or the stair's slope, never sunk into it or above it.
        if (Ground(p.Pos) is float ground && MathF.Abs(ground - p.Pos.Y) < 0.75f) p.Pos.Y = ground;
        // And never into the furniture or the clutter: held off its real shape.
        OffParts(p, was);
        // Held where it stood (a piece across the straight way to the next point): round it, by a way found over the cells there.
        if ((p.Pos - was).LengthSquared() < 1e-8f && _parts != null && (p.Blocked += dt) > 0.5f)
        {
            p.Blocked = 0f;
            if (_parts.WayRound(p.Pos, to, BodyRadius(p), Height(p), Place.WallsOf(_nav), PlaceParts.PersonRadius) is { Count: > 0 } round) p.Path.InsertRange(p.Step, round);
        }
        p.Yaw = Mathf.RotateToward(p.Yaw, MathF.Atan2(-d.X, -d.Z), 5f * dt);
        p.Sitting = false;
        return false;
    }

    const float PeopleAccel = 3f;

    void GoTo(Person p, int spot)
    {
        if (spot == p.Spot && p.Path != null && p.Step >= p.Path.Count && (p.Pos - At(spot)).Length() < 1.5f) return;
        if (spot == p.Spot && p.Path != null && p.Step >= p.Path.Count)
        {
            // Moved off it (pushed, or after something): back to it by the walking grid.
            p.Path = PathFromHere(p, spot) ?? new List<Vector3>();
            p.Step = 0;
            return;
        }
        List<Vector3> path = PathFrom(p, spot);
        if (path == null)
        {
            // No way there from here: they stay where they are a while rather than walk through anything.
            p.Path = new List<Vector3>();
            p.Step = 0;
            p.Wait = MathF.Max(p.Wait, 4f);
            return;
        }
        p.Left = p.Spot;
        p.Spot = spot;
        p.Step = 0;
        if (p.Far)
        {
            // Out of your sight: there already, their day kept as a schedule.
            p.Pos = path[^1];
            p.Path = new List<Vector3>();
            return;
        }
        p.Path = path;
    }

    List<Vector3> PathFrom(Person p, int spot)
    {
        // From the spot they are at (or near) along the worked-out route; called away partway, from where they are.
        if ((p.Pos - At(p.Spot)).Length() > 1.2f && !InSeat(p, p.Spot)) return PathFromHere(p, spot);
        if (_paths.TryGetValue((p.Spot, spot), out var known)) return known;
        List<Vector3> path = PathBetween(p.Spot, spot);
        return path;
    }

    /// <summary>A spot of one of these tags, near this one where there is one (null if the place has none).</summary>
    int? Haunt(Person p, string[] tags)
    {
        if (tags == null || tags.Length == 0) return null;
        var near = Spots.Select((s, i) => (s, i)).Where(t => Array.IndexOf(tags, t.s.Tag) >= 0).OrderBy(t => t.s.Cell.Manhattan(Spots[p.Spot].Cell) + _rng.Next(12)).Take(3).ToList();
        return near.Count == 0 ? null : near[_rng.Next(near.Count)].i;
    }

    /// <summary>
    /// What a person does next, by their job's day (data: <see cref="RoleLook"/>): a trader or a
    /// worker keeps their place; guards walk a short beat; the law patrols the streets and comes
    /// back to its checkpoint; farmers and mechanics go from task to task and bend to each; a medic
    /// goes round people; elders sit; children play; a drunk roams from bench to bench; a
    /// scavenger leaves by the way in and comes back later. Spawn roles from the place's data
    /// still lead where the job says nothing.
    /// </summary>
    void Next(Person p)
    {
        p.Partner = null;
        p.Working = false;
        var linked = Data.Links.Where(l => l.A == p.Spot || l.B == p.Spot).Select(l => l.A == p.Spot ? l.B : l.A).ToList();
        RoleLook day = p.Looks?.Figure.Routine;
        string moves = day?.Moves ?? "wander";
        // Unwelcome, you: every guard and the law keep their post at the way in.
        if (p.Role == "guard" && Unwelcome) { GoTo(p, p.Spawn.Spot); p.Wait = 4f; return; }
        switch (moves)
        {
            case "patrol":
            {
                // From the checkpoint to a far spot and back: the streets walked, the way in kept.
                int post = p.Spawn.Spot;
                var routes = Data.Routes.Select(r => r.A == p.Spot ? r.B : r.B == p.Spot ? r.A : -1).Where(i => i >= 0).ToList();
                int far = routes.Count > 0 ? routes[_rng.Next(routes.Count)] : linked.Count > 0 ? linked[_rng.Next(linked.Count)] : post;
                GoTo(p, p.Spot != post ? post : far);
                p.Wait = p.Spot == post ? 10f + (float)_rng.NextDouble() * 10f : 3f + (float)_rng.NextDouble() * 3f;
                return;
            }
            case "work":
                if (Haunt(p, day.Haunts) is int task)
                {
                    GoTo(p, task);
                    p.Working = true;
                    p.Wait = 12f + (float)_rng.NextDouble() * 16f;
                    return;
                }
                break;
            case "visit":
            {
                // To someone out and about, to look them over; between rounds, their own place.
                Person who = People.Where(o => o != p && !o.Dead && !o.Indoors && !o.Away && o.Job != PersonRole.Guard && o.Job != PersonRole.Lawman).OrderBy(_ => _rng.Next()).FirstOrDefault();
                if (who != null && _rng.NextDouble() < 0.6)
                {
                    GoTo(p, who.Spot);
                    p.Partner = who;
                    p.Working = true;
                    p.Wait = 6f + (float)_rng.NextDouble() * 6f;
                    return;
                }
                if (Haunt(p, day.Haunts) is int base_) GoTo(p, base_);
                p.Wait = 5f + (float)_rng.NextDouble() * 6f;
                return;
            }
            case "sit":
            case "roam":
                if (Haunt(p, day.Haunts) is int seat)
                {
                    GoTo(p, seat);
                    p.Wait = moves == "sit" ? 20f + (float)_rng.NextDouble() * 25f : 6f + (float)_rng.NextDouble() * 14f;
                    return;
                }
                break;
            case "leave":
                // Out by the way in, a while away, back with whatever they found.
                int gate = Spots.Select((s, i) => (s, i)).OrderBy(t => t.s.Cell.Manhattan(Memory.Inst.Layout.EntranceCell)).First().i;
                if (p.Spot == gate && _rng.NextDouble() < 0.7)
                {
                    p.Away = true;
                    p.AwayFor = 30f + (float)_rng.NextDouble() * 60f;
                    p.Wait = 1f;
                    return;
                }
                if (p.Spot != gate && _rng.NextDouble() < 0.5)
                {
                    GoTo(p, gate);
                    p.Wait = 1f + (float)_rng.NextDouble() * 2f;
                    return;
                }
                break;
        }
        switch (p.Role)
        {
            case "vendor":
            case "worker":
                // They stay at their stall or their work; now and then a step and back.
                p.Working = p.Role == "worker";
                p.Wait = 8f + (float)_rng.NextDouble() * 10f;
                // Now and then to a bench or a counter in the rooms round them, and back.
                if (_rng.NextDouble() < 0.4 && Haunt(p, p.Role == "vendor" ? Keep : Bench) is int at && at != p.Spot) { GoTo(p, at); p.Working = true; }
                return;
            case "guard":
                // A short beat: to a spot nearby and back to the post; unwelcome, you, they keep the post.
                int post = p.Spawn.Spot;
                if (Unwelcome) { GoTo(p, post); p.Wait = 4f; return; }
                GoTo(p, p.Spot != post ? post : linked.Count > 0 ? linked[_rng.Next(linked.Count)] : post);
                p.Wait = 6f + (float)_rng.NextDouble() * 8f;
                return;
            case "rusher":
                var routes = Data.Routes.Where(r => r.A == p.Spot || r.B == p.Spot).ToList();
                int far = routes.Count > 0 ? (routes[_rng.Next(routes.Count)] is var r && r.A == p.Spot ? r.B : r.A)
                    : linked.Count > 0 ? linked[_rng.Next(linked.Count)] : p.Spot;
                GoTo(p, far);
                p.Wait = 2f + (float)_rng.NextDouble() * 4f;
                return;
            default:
                if (p.Figure?.Playful == true)
                {
                    // Children run about: after another child, round a grown-up, off somewhere and back, in quick bursts with sudden stops.
                    Person other = People.Where(o => o != p && !o.Dead && !o.Indoors && o.Pos.DistanceSquaredTo(p.Pos) < 400f).OrderBy(_ => _rng.Next()).FirstOrDefault();
                    int dash = other != null && _rng.NextDouble() < 0.6 ? other.Spot : linked.Count > 0 ? linked[_rng.Next(linked.Count)] : p.Spot;
                    GoTo(p, dash);
                    p.Speed = 2.4f + (float)_rng.NextDouble() * 1.4f;
                    p.Wait = 0.3f + (float)_rng.NextDouble() * 1.6f;
                    return;
                }
                // A child goes where a grown-up is.
                Person minder = p.Figure?.Stage == LifeStage.Child ? People.Where(o => o != p && !o.Dead && !o.Indoors && o.Figure?.Stage != LifeStage.Child).OrderBy(o => o.Pos.DistanceSquaredTo(p.Pos)).FirstOrDefault() : null;
                if (minder != null) GoTo(p, minder.Spot);
                else if (_rng.NextDouble() < 0.35 && Haunt(p, Seat) is int seat_) GoTo(p, seat_); // a sit down in one of the rooms
                else if (linked.Count > 0) GoTo(p, linked[_rng.Next(linked.Count)]);
                p.Wait = 4f + (float)_rng.NextDouble() * 9f;
                // Someone else already at this spot: they stop and chat.
                p.Partner = People.FirstOrDefault(o => o != p && !o.Dead && !o.Indoors && o.Spot == p.Spot && o.Partner == null && o.Role is "wanderer" or "rusher");
                if (p.Partner != null) { p.Partner.Partner = p; p.Partner.Wait = p.Wait; }
                return;
        }
    }

    /// <summary>At a spot: sit at a sit spot, face a partner to chat, bend to work.</summary>
    void Idle(Person p, float dt)
    {
        string tag = Spots[p.Spot].Tag;
        FaceSpot(p, dt);
        string moves = p.Looks?.Figure.Routine?.Moves ?? "wander";
        p.Sitting = tag == "sit" && moves is "wander" or "sit" or "roam" && p.Role is not ("guard" or "vendor");
        if (p.Partner != null && !p.Partner.Dead)
        {
            Vector3 d = p.Partner.Pos - p.Pos;
            if (d.LengthSquared() > 0.01f) p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 4f));
        }
    }

    /// <summary>A guard in a wary or hostile place: watches you. (Hostile, they fight with what they carry: Town.Combat.)</summary>
    void Guard(Person p, Vector3 player, Mood mood, float dt)
    {
        Vector3 d = player - p.Pos;
        p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 6f));
    }

    /// <summary>The place's standing with you (<see cref="PlaceMemory.Standing"/>); below this they treat you as a wary place would.</summary>
    public float Standing => Memory.Standing;
    public const float StandingWary = -20f;

    /// <summary>Checkpoint warnings and gun holds, outside a hostile place (tests).</summary>
    public int Halts { get; private set; }
    public int HoldAims { get; private set; }

    /// <summary>Whether you count as unwelcome here: a wary place, or one whose standing with you has fallen.</summary>
    public bool Unwelcome => Memory.Mood == Mood.Wary || Standing < StandingWary;

    /// <summary>
    /// A guard at their post, or the law at its checkpoint by the way in, when you are unwelcome:
    /// you come up, they face you and warn you off (the stop); come on closer while warned and the
    /// gun comes up on you; back off and it goes down. Nobody fires outside a hostile place.
    /// True while they're dealing with you.
    /// </summary>
    bool Checkpoint(Person p, Vector3 player, Mood mood, float dt)
    {
        float away = (player - p.Pos).Length();
        bool post = (p.Pos - Walk(Spots[p.Spawn.Spot].Cell)).Length() < 4f;
        if (!post || !Unwelcome || away > 12f || p.Looks == null)
        {
            if (p.Halt > 0f || p.Holding) { p.Halt = 0f; p.Holding = false; }
            return false;
        }
        Vector3 d = player - p.Pos;
        p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 5f));
        p.Now = 0f;
        if (p.Halt <= 0f) Halts++;
        p.Halt += dt;
        if (!p.Holding && away < 5f && p.Halt > 1.6f && Weapons.Ranged(p.Figure.Prop)) { p.Holding = true; HoldAims++; }
        if (p.Holding && away > 7.5f) p.Holding = false;
        return true;
    }

    /// <summary>One of them takes aim at you (where from): their shot follows (<see cref="ShotFired"/>).</summary>
    public event Action<Vector3> Aimed;

    // ---- Being shot at, and shooting ------------------------------------------------------------------

    /// <summary>The survivor fired in or at the town: a peaceful or wary place turns hostile, its people flee.</summary>
    public void Provoked()
    {
        Gunshot();
        // Already hostile: a shot in or at the place tells them where you are.
        if (Memory.Mood == Mood.Hostile) { if (_place != null) Alert(_place.FromWorld(_places.Zone.Wildlife.PlayerWorld())); return; }
        _moodBefore = Memory.Mood;
        Memory.Mood = Mood.Hostile;
        // Each takes it in in their own time: the bold and the near first, children and the old later.
        Vector3 at = _place != null ? _place.FromWorld(_places.Zone.Wildlife.PlayerWorld()) : Vector3.Zero;
        var roll = Wildlife.Rng.For(Memory.Inst.SpawnSeed, "provoked", (long)(_clock * 100f));
        foreach (Person p in People)
        {
            Figure f = p.Looks?.Figure;
            p.ReactAt = _clock + Reaction.Delay(p.Nerve, (p.Pos - at).Length(), saw: false, f?.Stage == LifeStage.Child, f?.Stage == LifeStage.Elder, roll);
            p.Reacted = false;
        }
        TurnedHostile?.Invoke();
    }

    public event Action TurnedHostile;
    Mood _moodBefore = Mood.Peaceful;
    Temper _temper;
    /// <summary>The way the first to react went: the rest mostly follow it.</summary>
    Stance? _setTone;

    /// <summary>Whether a world point is in the town's area.</summary>
    public bool Covers(Vector3 world)
    {
        if (_place == null) return false;
        Vector3 l = _place.FromWorld(world);
        return new Vector2(l.X - Data.AreaCenter.X, l.Z - Data.AreaCenter.Z).Length() < Data.AreaRadius;
    }

    /// <summary>The person nearest along a ray (world), within reach.</summary>
    public Person Along(Vector3 eye, Vector3 dir, float reach, out float distance)
    {
        distance = reach;
        Person best = null;
        if (_place == null) return null;
        Vector3 e = _place.FromWorld(eye);
        foreach (Person p in People)
        {
            if (p.Dead || p.Indoors || p.Away) continue;
            float h = p.Looks?.Figure.Height ?? 1.75f;
            Vector3 c = p.Pos + Vector3.Up * h * (p.Sitting ? 0.34f : 0.53f);
            float along = (c - e).Dot(dir);
            if (along < 0f || along > distance) continue;
            Vector3 off = e + dir * along - c;
            if (new Vector2(off.X, off.Z).Length() > 0.35f || MathF.Abs(off.Y) > h * 0.53f) continue;
            distance = along;
            best = p;
        }
        return best;
    }

    /// <summary>The survivor hurts one of them (<see cref="Struck"/>).</summary>
    public void Shoot(Person p, float damage) => Struck(p, damage, GalHack.Arms.DamageClass.Kinetic, byPlayer: true);

    void Fall(Person p)
    {
        _places?.Zone.Wildlife.Sim?.Targets.Remove(p);
        if (p.Dropped) return;
        p.Dropped = true;
        Fell?.Invoke(p);
    }

    // ---- Bodies ---------------------------------------------------------------------------------------

    /// <summary>
    /// Who each of them is, from the bestiary: this world's people, in this world's clothes, with
    /// the look of their job. A hostile place's people have gone to raiding; what the world does to
    /// bodies shows on some of them, more on some than others.
    /// </summary>
    void RollPeople(WorldLife life)
    {
        bool raiders = Memory.Mood == Mood.Hostile, farm = Memory.Inst.Family.Tags.Contains("farm");
        var roll = Wildlife.Rng.For(Memory.Inst.SpawnSeed, "townsfolk");
        // The place's founders: most here share a look; trade hubs and spaceports mix more.
        var tags = Memory.Inst.Family.Tags;
        Founders founders = life.People.FoundersOf(Memory.Inst.SpawnSeed, tags.Contains("spaceport") || tags.Contains("trade") || tags.Contains("market"), Memory.Inst.Family.Faction == Faction.Vharn);
        // Dressed for their own region of the world: its temperature and its humidity, off the world map.
        var map = _places.Zone.Planet.Map;
        founders.Locale = RegionAt(map, Memory.Site.Entrance.X + 0.5, Memory.Site.Entrance.Z + 0.5);
        lock (life.People.Regions)
            if (life.People.Regions.Count == 0)
                for (int i = 0; i < 32; i++) life.People.Regions.Add(RegionAt(map, (i * 0.618034 % 1.0) * map.WidthMeters, ((i * 0.381966 + 0.13) % 1.0) * map.HeightMeters));
        // How this group takes to outsiders: from its tech and its world, and then its own roll.
        _temper = Temper.Roll(_tech, Math.Clamp(life.World.Mutagen + life.World.Radiation * 0.15f, 0f, 1f), Memory.Inst.SpawnSeed);
        float hardened = life.People.HardenedChance(raiders, People.Count(q => q.Role != "wanderer"));
        for (int i = 0; i < People.Count; i++)
        {
            Person p = People[i];
            PersonRole role = PeopleGenerator.RoleFor(p.Role, farm, roll);
            // Raiders keep a few who don't fight, and they don't dress for it.
            if (raiders && role != PersonRole.Child && role != PersonRole.Elder) role = PersonRole.Raider;
            bool raider = raiders; // everyone in a raider place is of it, children too, whatever they look like
            float twist = roll.Float();
            float mutation = life.World.Mutagen * twist * twist * 1.4f;
            // Now and then, where the world forces it, the one doing this job is a child.
            bool young = role is not (PersonRole.Child or PersonRole.Elder or PersonRole.Drunk) && roll.Chance(hardened);
            p.Looks = life.People.Person(Wildlife.Rng.Mix(Memory.Inst.SpawnSeed, (ulong)i + 1), role, raider ? Occupancy.Raider : Occupancy.Friendly, mutation < 0.05f ? 0f : mutation, young: young, founders: founders);
            p.Rig = new HumanRig(p.Looks);
            p.Job = role;
            p.Nerve = roll.Float();
            p.Speed *= role switch { PersonRole.Elder => 0.78f, PersonRole.Drunk => 0.72f, _ => 1f };
        }
        // A place of copies: everyone in it is the same person.
        if (Data.Copies && People.Count > 1)
            foreach (Person q in People.Skip(1))
            {
                q.Looks = People[0].Looks;
                q.Rig = new HumanRig(q.Looks);
            }
        // A few of them may not be what they seem: silent carriers, rolled as anyone is, nothing showing.
        HuskRules husks = life.Data.Husks;
        int carriers = Carriers.Roll(husks, life.Moving.Any(s => s.Husk), life.World.Coreward, People.Count, roll);
        // (Only a body the worms can take: a machine never carries them.)
        foreach (Person q in People.Where(q => q.Looks.Figure.Stage != LifeStage.Child && Husks.CanTurn(q.Looks)).OrderBy(_ => roll.Float()).Take(carriers)) q.Carrier = true;
        CarriersAtStart = People.Count(q => q.Carrier);
        foreach (Person q in People) _carried.Add(new Carried());
    }

    /// <summary>The region at a world point: the map's temperature (as degrees) and humidity there.</summary>
    public static Locale RegionAt(Planets.Gen.WorldMap map, double x, double z)
    {
        double wx = ((x % map.WidthMeters) + map.WidthMeters) % map.WidthMeters, wz = ((z % map.HeightMeters) + map.HeightMeters) % map.HeightMeters;
        int cx = (int)Math.Floor(wx / map.CellSize) % map.Width, cz = (int)Math.Floor(wz / map.CellSize) % map.Height;
        int k = Math.Clamp(cz, 0, map.Height - 1) * map.Width + Math.Clamp(cx, 0, map.Width - 1);
        return new Locale { TempC = Mathf.Lerp(-45f, 65f, map.Temperature[k]), Humidity = map.Humidity[k] };
    }

    // ---- The worms among them ----------------------------------------------------------------------

    readonly List<Carried> _carried = new();
    readonly List<int> _rise = new();
    readonly HashSet<Person> _held = new();
    float _wormsIn;
    const float WormsEvery = 0.25f;

    /// <summary>Silent carriers rolled for the place, and those who have dropped the act since (tests).</summary>
    public int CarriersAtStart { get; private set; }
    public int Risen { get; private set; }
    public int Carrying => People.Count(p => p.Carrier && !p.Gone && !p.Dead);

    /// <summary>
    /// The silent carriers (<see cref="Carriers"/>), a few times a second: one alone with you rises
    /// a Husk where it stands and goes for you; one alone long enough with one of the town's own
    /// passes the worms on; past the set share, they all drop the act.
    /// </summary>
    void Worms(Vector3 player, float dt)
    {
        if (_carried.Count != People.Count || (_wormsIn -= dt) > 0f) return;
        _wormsIn = WormsEvery;
        bool any = false;
        for (int i = 0; i < People.Count; i++)
        {
            Person p = People[i];
            Carried c = _carried[i];
            c.At = p.Pos;
            c.Out = !p.Dead && !p.Gone && !p.Indoors;
            c.Carrier = p.Carrier && !p.Dead && !p.Gone;
            any |= c.Carrier;
        }
        if (!any) return;
        _rise.Clear();
        ITarget you = _places.Zone.Wildlife.Player;
        Carriers.Step(_places.Zone.Wildlife.Life.Data.Husks, _carried, you is { Attackable: true } ? player : null, WormsEvery, _rise);
        for (int i = 0; i < People.Count; i++) People[i].Carrier = _carried[i].Carrier && Husks.CanTurn(People[i].Looks);
        foreach (int i in _rise) Rise(People[i]);
    }

    /// <summary>A carrier drops the act: a Husk stands where they stood, in the wild sim, and they are gone from the town.</summary>
    void Rise(Person p)
    {
        WildSim sim = _places.Zone.Wildlife.Sim;
        if (sim == null || p.Gone || p.Dead) return;
        Creature h = Seizing.Rise(sim, p, Wildlife.Rng.Mix(Memory.Inst.SpawnSeed, (ulong)(People.IndexOf(p) + 1) * 7919UL));
        if (h == null) return;
        p.Carrier = false;
        Risen++;
        sim.Targets.Remove(p);
        h.Stir = 1f;
    }

    float _voiceIn = 2f;
    System.Threading.Tasks.Task _rolling;
    /// <summary>Everyone has been rolled and their meshes worked out.</summary>
    bool Rolled => _rolling == null ? People.Count == 0 || People[0].Looks != null : _rolling.IsCompleted;

    /// <summary>A body's act has to hold this long before another may start, s; it eases in and out at this rate a second.</summary>
    const float ActHold = 1.4f, ActEase = 3.5f;
    /// <summary>Aimed at inside this angle, no longer outside the wider one (so the edge never flickers); within this reach, m.</summary>
    static readonly float AimIn = MathF.Cos(Mathf.DegToRad(5f)), AimOut = MathF.Cos(Mathf.DegToRad(10f));
    const float AimReach = 32f, GreetIn = 11f, GreetRearm = 22f, StartleIn = 4.5f;

    /// <summary>Gesture flickers across the whole town (tests).</summary>
    public int Flickers => People.Sum(p => p.Flickers);

    /// <summary>
    /// What each body says to you, without words, chosen by state, role and mood (the role's
    /// reactions are data): a gun on them puts unarmed hands up, a peaceful approach gets a wave (or
    /// a guard's warning, a trader's wares, a raider's taunt), coming up on them suddenly makes them
    /// start, and running for cover is panic. Every change eases in and out, holds long enough to
    /// read, and the aim test has an inner and an outer edge, so nothing flickers.
    /// </summary>
    void Acts(Person p, Vector3 player, Vector3 eye, Vector3 look, float away, float dt)
    {
        Figure f = p.Figure;
        Vector3 to = p.Pos + Vector3.Up * f.Height * 0.6f - eye;
        float along = to.LengthSquared() > 0.01f ? look.Dot(to.Normalized()) : 0f;
        p.Covered = away < AimReach && along > (p.Covered ? AimOut : AimIn);
        bool armed = Weapons.Armed(f.Prop);
        bool peaceful = Memory.Mood != Mood.Hostile;
        if (away > GreetRearm) p.Greeted = false;
        // Coming up close out of nowhere: a start, before anything else.
        if (away < StartleIn && !p.Close && !p.Greeted) { p.StartleFor = 0.9f; Speak(p, VoiceMood.Murmur); }
        p.Close = away < StartleIn + 1.5f;
        p.StartleFor = MathF.Max(0f, p.StartleFor - dt);
        BodyAct want = BodyAct.None;
        if (p.Fleeing) want = BodyAct.Panic;
        else if (p.Halt > 0f && !p.Holding) want = BodyAct.Warn; // stop there
        else if (p.AnswerFor > 0f && _clock >= p.AnswerAt)
        {
            // Answering your gesture, facing you (or where you pointed).
            p.AnswerFor -= dt;
            want = p.Answer;
            p.Greeted = true;
            Vector3 d = (p.LookFor > 0f ? p.LookAt : player) - p.Pos;
            p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 4f));
        }
        else if (p.ShooFor > 0f && p.Threat != null)
        {
            // Waving a pest off: face it, the back of the hand at it.
            want = BodyAct.WaveOff;
            Vector3 d = _place.FromWorld(_places.Zone.Wildlife.WorldOf(p.Threat)) - p.Pos;
            p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 4f));
        }
        else if (p.StartleFor > 0f) want = f.Startled;
        else if (p.Covered && (!armed || f.Aimed != BodyAct.HandsUp) && p.Aim < 0f) want = f.Aimed;
        else if (peaceful && !p.Greeted && away < GreetIn && !p.Sitting && p.Now < 0.6f)
        {
            p.Greeted = true;
            p.ActHeld = 0f;
            // The first to react sets the tone; the rest mostly follow, each a little their own.
            var own = Wildlife.Rng.For(Memory.Inst.SpawnSeed, "stance", People.IndexOf(p));
            Stance stance = _setTone ?? _temper?.PersonStance(own) ?? Stance.Welcoming;
            // How you stand with them colours it: cold to one they've cause to fear, warm to a friend; one you robbed never warms.
            if (Standing < StandingWary || p.Robbed) stance = p.Figure.Role is PersonRole.Guard or PersonRole.Lawman ? Stance.Guarded : Stance.Wary;
            else if (Standing > 40f && stance is Stance.Wary or Stance.Guarded or Stance.Indifferent) stance = Stance.Welcoming;
            if (_setTone != null && own.Chance(0.2f)) stance = _temper.PersonStance(own);
            _setTone ??= stance;
            want = Temper.ActFor(stance, f.Approach);
            Speak(p, VoiceMood.Murmur);
        }
        else if (p.Act is BodyAct.Greet or BodyAct.Present or BodyAct.Warn or BodyAct.Taunt && p.ActHeld < 2.6f && away < GreetRearm) want = p.Act;
        if (want == BodyAct.None && !p.Sitting && p.Now < 0.6f) want = MoodAct(p);
        // A child at ease plays, on and off.
        if (want == BodyAct.None && peaceful && f.Stage == LifeStage.Child && p.Now < 0.2f && !p.Sitting && MathF.Sin(_clock * 0.23f + p.Spot) > 0.1f) want = BodyAct.Play;
        if (want == BodyAct.HandsUp && p.Wanted != BodyAct.HandsUp) Speak(p, _rng.NextDouble() < 0.5 ? VoiceMood.Whisper : VoiceMood.Cry);
        // A new act waits for the old one to have held, then the old eases out before the new eases in.
        if (want != p.Wanted && (p.ActHeld >= ActHold || p.Wanted == BodyAct.None || want is BodyAct.Panic or BodyAct.Startle)) p.Wanted = want;
        p.ActHeld += dt;
        if (p.Act != p.Wanted)
        {
            p.ActAmount = MathF.Max(0f, p.ActAmount - dt * ActEase * (p.Wanted is BodyAct.Startle ? 3f : 1f));
            if (p.ActAmount <= 0f)
            {
                if (p.Wanted != BodyAct.None)
                {
                    p.ActChanges++;
                    if (p.Wanted == p.Before && _clock - p.LastChange < 1f) p.Flickers++;
                    p.Before = p.Act == BodyAct.None ? p.Before : p.Act;
                    p.LastChange = _clock;
                }
                p.Act = p.Wanted;
                p.ActHeld = 0f;
            }
        }
        else if (p.Act != BodyAct.None) p.ActAmount = MathF.Min(1f, p.ActAmount + dt * ActEase * (p.Act == BodyAct.Startle ? 3f : 1f));
        // Hands up, they face you; close in on them and they cower.
        if (p.Act is BodyAct.HandsUp or BodyAct.Warn or BodyAct.Taunt or BodyAct.Greet or BodyAct.Startle)
        {
            Vector3 d = player - p.Pos;
            p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 3f));
        }
        if (p.Act == BodyAct.HandsUp && away < 3f) p.Wanted = BodyAct.Cower;
    }

    void Speak(Person p, VoiceMood mood)
    {
        Vector3 at = World(p.Pos + Vector3.Up * p.Figure.Height * 0.9f);
        _places.Zone.Wildlife.Sound(Vector3.Zero, a => a.Say(p.Looks, mood, at));
    }

    /// <summary>
    /// The sound of the place, one voice at a time and only near you: people at ease murmur, a
    /// frightened one whispers or cries, whoever turns on you shouts.
    /// </summary>
    void Voices(Vector3 player, float dt)
    {
        if ((_voiceIn -= dt) > 0f) return;
        _voiceIn = 0.9f + (float)_rng.NextDouble() * 2.4f;
        Person who = null;
        float best = 26f * 26f;
        foreach (Person p in People)
        {
            if (p.Dead || p.Indoors || p.Away || p.Looks == null) continue;
            float d = (player - p.Pos).LengthSquared() * (0.6f + (float)_rng.NextDouble());
            if (d < best) { best = d; who = p; }
        }
        if (who == null) return;
        if (Vocalise(who)) return;
        VoiceMood mood = who.Aim >= 0f ? VoiceMood.Shout
            : who.Fleeing ? (_rng.NextDouble() < 0.5 ? VoiceMood.Cry : VoiceMood.Whisper)
            : Memory.Mood == Mood.Hostile ? VoiceMood.Shout : VoiceMood.Murmur;
        if (mood == VoiceMood.Shout && who.Figure.Stage == LifeStage.Child) mood = VoiceMood.Cry;
        Vector3 at = World(who.Pos + Vector3.Up * who.Figure.Height * 0.9f);
        _places.Zone.Wildlife.Sound(Vector3.Zero, a => a.Say(who.Looks, mood, at));
    }

    /// <summary>Builds what they're drawn with: a few at a time, so a town coming into view never costs a whole frame.</summary>
    void MakeBody(Person p)
    {
        p.View = new PersonView { Name = $"{p.Role} {People.IndexOf(p)}" };
        _view.AddChild(p.View);
        p.View.Build(p.Rig, p.Ready);
        p.Ready = null;
        GroundBody(p);
        HoldWeapon(p);
    }

    /// <summary>This world's form of the friendly gestures.</summary>
    int? _style;
    float _clock;

    /// <summary>Their body where they are now, this far from you (dt: the time since they were last posed).</summary>
    void Pose(Person p, int who, float away, float dt)
    {
        if (Shown(p, away, dt)) PoseBody(p, who, dt);
    }

    /// <summary>Whether their body shows at all (not indoors, not away on a run, not beyond the far range), faded in and out at its edges.</summary>
    bool Shown(Person p, float away, float dt)
    {
        if (p.View == null) return false;
        bool shows = (!p.Indoors || p.Asleep) && !p.Away && p.View.Fade(away, dt);
        if (p.View.Visible != shows) p.View.Visible = shows;
        return shows;
    }

    void PoseBody(Person p, int who, float dt)
    {
        p.View.SyncFar(p.Pos, p.Yaw);
        bool moving = p.Now > 0.05f;
        float t = _clock + who * 1.7f;
        p.Seated = Mathf.MoveToward(p.Seated, p.Sitting ? 1f : 0f, 2.5f * dt);
        // Into bed: onto the mattress at its foot and back down onto it; out again in the morning.
        p.Slept = Mathf.MoveToward(p.Slept, p.Asleep ? 1f : 0f, 1.2f * dt);
        if (p.Asleep && p.Home < _lie.Count && _lie[p.Home] is Vector3 lie) p.Pos = p.Pos.MoveToward(lie, 1.5f * dt);
        p.Bent = Mathf.MoveToward(p.Bent, !moving && p.Working && !p.Sitting ? (p.Job == PersonRole.Medic ? 0.6f : 1f) : 0f, 2f * dt);
        p.Raised = Mathf.MoveToward(p.Raised, p.Aim >= 0f || p.Holding ? 1f : 0f, 3f * dt);
        // Looks at you if you're close and in front of them; otherwise glances about now and then.
        float look = MathF.Sin(t * 0.31f) > 0.55f ? MathF.Sin(t * 1.1f) * 0.9f : 0f;
        Vector3 toPlayer = _place.FromWorld(_places.Zone.Wildlife.PlayerWorld()) - p.Pos;
        if (toPlayer.LengthSquared() < 100f)
        {
            float off = Mathf.AngleDifference(p.Yaw, MathF.Atan2(-toPlayer.X, -toPlayer.Z));
            if (MathF.Abs(off) < 1.25f) look = off;
        }
        p.HeadYaw = Mathf.Lerp(p.HeadYaw, look, 0.08f);
        p.Rig.Drive(new Gesture
        {
            Feet = p.Pos,
            Yaw = p.Yaw,
            Speed = p.Now,
            Walked = p.Gait,
            Sit = p.Seated,
            Work = p.Bent,
            Aim = p.Raised,
            Look = p.HeadYaw,
            Down = MathF.Max(p.Down, p.Slept),
            Dead = p.Dead,
            Crouch = p.Crouched,
            Strike = p.Strike,
            Act = p.Act,
            ActAmount = p.ActAmount,
            ActAt = p.Pos + new Vector3(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw)) * 2f + Vector3.Up * 0.9f,
            Clock = t,
            Style = _style ??= Gestures.FriendlyStyle(_places.Zone.Wildlife.Life.World.Seed),
        });
        p.LookFor = MathF.Max(0f, p.LookFor - dt);
        p.View.Sync();
        PoseWeapon(p);
        p.View.Hurt(p.Hurt / 0.4f);
    }
}
