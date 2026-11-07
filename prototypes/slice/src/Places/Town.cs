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

    public sealed class Person : ITarget
    {
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
        public Person Partner;
        public Health Health { get; } = new(60f);
        public bool Dead => Health.Current <= 0f;

        // As wildlife sees them (the patch's frame): hunted only outside the town.
        public string Name => $"{Spawn.Role}";
        public Vector3 Position => Town._places == null ? Pos : Town._places.Zone.Wildlife.PatchLocal(Town.World(Pos));
        public Vector3 Forward => new(-MathF.Sin(Yaw), 0, -MathF.Cos(Yaw));
        public bool Attackable => !Dead && !Indoors;
        public float ThreatScale => 1f;
    }

    /// <summary>A place's town (worker-safe), or null where nobody lives: walking routes worked out once round the room dressing.</summary>
    public static Town For(PlaceMemory m)
    {
        Townsfolk data = m.Inst.Townsfolk;
        if (data == null || data.People.Count == 0 || data.Spots.Count < 2) return null;
        var town = new Town(m, data);
        town._tech = m.Inst.Layout.Variant?.Tech is { Length: > 0 } t ? t : m.Inst.Family.Tags.Contains("settlement") ? "low" : "mid";
        town._nav = Place.NavOf(m);
        foreach (var (a, b) in data.Links.Concat(data.Routes)) town.PathBetween(a, b);
        town._homes = data.Spots.Select((s, i) => (s, i)).Where(t => t.s.Tag == "home").Select(t => t.i).ToArray();
        return town;
    }

    static Vector3 Walk(PInt3 c) => new(c.X + 0.5f, c.Y, c.Z + 0.5f);

    List<Vector3> PathBetween(int a, int b)
    {
        if (_paths.TryGetValue((a, b), out var p)) return p;
        if (_paths.TryGetValue((b, a), out var q)) return _paths[(a, b)] = Enumerable.Reverse(q).ToList();
        PInt3 from = Data.Spots[a].Cell, to = Data.Spots[b].Cell;
        List<PInt3> cells = _nav.Standable(from) && _nav.Standable(to) ? _nav.FindPath(from, to, 30000) : null;
        var path = cells?.Select(Walk).ToList() ?? new List<Vector3> { Walk(from), Walk(to) };
        _paths[(a, b)] = path;
        return path;
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
        if (People.Count == 0)
            foreach (CitizenSpawn s in Data.People)
            {
                var p = new Person { Town = this, Spawn = s, Pos = Walk(s.Cell), Spot = s.Spot, Yaw = (float)_rng.NextDouble() * MathF.Tau };
                p.Home = _homes.Length > 0 ? _homes.OrderBy(h => Data.Spots[h].Cell.Manhattan(s.Cell)).First() : s.Spot;
                p.Speed = s.Gait switch { "hurry" => 3.0f, "amble" => 1.1f, "patrol" => 1.3f, _ => 1.3f };
                p.Wait = (float)_rng.NextDouble() * 6f;
                People.Add(p);
            }
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
        var ticker = new Ticker { Name = "Ticker", Town = this };
        _view.AddChild(ticker);
    }

    public void Sit(Place place)
    {
        WildlifePatch patch = _places.Zone.Wildlife;
        patch.Swapped -= OnSwapped;
        patch.Bitten -= OnBitten;
        if (patch.Sim != null)
        {
            patch.Sim.Settlements.Remove(_settlement);
            foreach (Person p in People) patch.Sim.Targets.Remove(p);
            foreach (var a in _animals) patch.Sim.Remove(a.Creature);
            patch.Sim.StrayEntered -= OnStray;
        }
        _animals.Clear();
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
                ITarget owner = kind == DomesticKind.Pet ? People.FirstOrDefault(p => p.Home == Data.Spots.FindIndex(sp => sp.Cell == cell)) : null;
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
        Person guard = People.Where(p => p.Role == "guard" && !p.Dead).OrderBy(p => (p.Position - c.Position).LengthSquared()).FirstOrDefault();
        _places.Zone.Wildlife.Sim.CullStray(c, guard);
        Culled++;
    }

    /// <summary>Strays the guards have culled (tests).</summary>
    public int Culled { get; private set; }

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

    /// <summary>Beyond this someone updates a few times a second; bodies built a tick at most.</summary>
    const float NearPeople = 40f;
    const int BodiesPerTick = 3;
    int _tick;

    void Tick(float tickDt)
    {
        if (_place == null || !_place.Ready || _place.Dormant || !Rolled) return;
        bool night = Night();
        Vector3 player = _place.FromWorld(_places.Zone.Wildlife.PlayerWorld());
        // Where you are looking, in the place's frame: what you point at, people see.
        Vector3 eye = player + Vector3.Up * 1.6f;
        ITarget you = _places.Zone.Wildlife.Player;
        Vector3 look = you == null ? Vector3.Forward : (_place.FromWorld(_places.Zone.Wildlife.PlayerWorld() + you.Forward) - player);
        look = look.LengthSquared() > 1e-4f ? look.Normalized() : Vector3.Forward;
        _tick++;
        _clock += tickDt;
        Voices(player, tickDt);
        int built = 0;
        for (int who = 0; who < People.Count; who++)
        {
            Person p = People[who];
            if (p.Dead)
            {
                // Going down, then lying where they fell.
                if (p.View == null) MakeBody(p);
                if (p.Down >= 1f && p.Laid) continue;
                p.Laid = p.Down >= 1f;
                p.Down = MathF.Min(1f, p.Down + tickDt * 1.8f);
                _posing = (player - p.Pos).Length() < PersonView.NearRange + 6f;
                Pose(p, who);
                continue;
            }
            if (p.View == null && built < BodiesPerTick) { MakeBody(p); built++; }
            // Far from you a town needn't move every tick: each in their turn, with the time they missed.
            p.Saved += tickDt;
            float away = (player - p.Pos).Length();
            if (away > NearPeople && (_tick + who) % 6 != 0) continue;
            float dt = p.Saved;
            p.Saved = 0f;
            _posing = away < PersonView.NearRange + 6f;
            if (_posing && p.Looks != null) Acts(p, player, eye, look, away, dt);
            bool guard = p.Role == "guard";
            p.Hurt = MathF.Max(0f, p.Hurt - dt);
            // Until it reaches them, they carry on as they were; then a start, and they act.
            Mood mood = Memory.Mood;
            if (!p.Reacted)
            {
                if (_clock < p.ReactAt) mood = _moodBefore;
                else { p.Reacted = true; p.Path = null; p.Step = 0; p.Partner = null; p.StartleFor = 0.7f; }
            }
            // Hostile: the people go indoors; the guards turn on you.
            bool hide = !guard && (night || mood == Mood.Hostile);
            if (hide && !p.Indoors)
            {
                if (p.Path == null || p.Step >= p.Path.Count || !p.Fleeing && !night) GoTo(p, p.Home);
                p.Fleeing = mood == Mood.Hostile;
                p.Speed = p.Fleeing ? 3.4f : p.Speed;
                if (Move(p, dt)) { p.Indoors = _homes.Length > 0; }
                Pose(p, who);
                continue;
            }
            if (!hide && p.Indoors)
            {
                p.Indoors = false;
                p.Fleeing = false;
                p.Speed = p.Spawn.Gait == "hurry" ? 3.0f : p.Spawn.Gait == "amble" ? 1.1f : 1.3f;
            }
            if (p.Indoors) { Pose(p, who); continue; }
            if (guard && mood != Mood.Peaceful && (player - p.Pos).Length() < 40f)
            {
                Guard(p, player, mood, dt);
                Pose(p, who);
                continue;
            }
            if (Move(p, dt))
            {
                p.Wait -= dt;
                if (p.Wait <= 0f) Next(p);
                else Idle(p, dt);
            }
            Pose(p, who);
        }
    }

    /// <summary>Walks a person along their path; true once they are there (or have no path).</summary>
    bool Move(Person p, float dt)
    {
        if (p.Path == null || p.Step >= p.Path.Count)
        {
            p.Now = 0f;
            return true;
        }
        Vector3 to = p.Path[p.Step];
        Vector3 d = to - p.Pos;
        float flat = new Vector2(d.X, d.Z).Length();
        // Eases into a walk and out of it, slowing only for the last of the way; corners keep their pace.
        bool last = p.Step >= p.Path.Count - 1;
        float wanted = last ? MathF.Min(p.Speed, MathF.Max(0.3f, MathF.Sqrt(2f * PeopleAccel * flat))) : p.Speed;
        p.Now = Mathf.MoveToward(p.Now, wanted, PeopleAccel * dt);
        float step = p.Now * dt;
        p.Gait += step;
        // Turns a corner a stride early, not on the spot.
        if (flat < step || flat < (last ? 0.05f : 0.4f))
        {
            if (last) p.Pos = to;
            p.Step++;
            return p.Step >= p.Path.Count;
        }
        p.Pos += new Vector3(d.X, 0f, d.Z) / flat * step;
        p.Pos.Y = Mathf.Lerp(p.Pos.Y, to.Y, MathF.Min(1f, step / MathF.Max(flat, 0.01f)));
        p.Yaw = Mathf.RotateToward(p.Yaw, MathF.Atan2(-d.X, -d.Z), 5f * dt);
        p.Sitting = false;
        return false;
    }

    const float PeopleAccel = 3f;

    void GoTo(Person p, int spot)
    {
        if (spot == p.Spot && p.Path != null && p.Step >= p.Path.Count) return;
        p.Path = PathFrom(p, spot);
        p.Step = 0;
        p.Spot = spot;
    }

    List<Vector3> PathFrom(Person p, int spot)
    {
        // From the spot they are at (or near) along the worked-out route, else straight there.
        if (_paths.TryGetValue((p.Spot, spot), out var known)) return known;
        List<Vector3> path = PathBetween(p.Spot, spot);
        return path;
    }

    /// <summary>What a person does next, by role.</summary>
    void Next(Person p)
    {
        p.Partner = null;
        var linked = Data.Links.Where(l => l.A == p.Spot || l.B == p.Spot).Select(l => l.A == p.Spot ? l.B : l.A).ToList();
        switch (p.Role)
        {
            case "vendor":
            case "worker":
                // They stay at their stall or their work; now and then a step and back.
                p.Wait = 8f + (float)_rng.NextDouble() * 10f;
                return;
            case "guard":
                // A short beat: to a spot nearby and back to the post.
                int post = p.Spawn.Spot;
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
        string tag = Data.Spots[p.Spot].Tag;
        p.Sitting = tag == "sit" && p.Role == "wanderer";
        if (p.Partner != null && !p.Partner.Dead)
        {
            Vector3 d = p.Partner.Pos - p.Pos;
            if (d.LengthSquared() > 0.01f) p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 4f));
        }
    }

    /// <summary>A guard in a wary or hostile place: watches you; hostile, takes aim (a red line you can see, a second and more) and fires.</summary>
    void Guard(Person p, Vector3 player, Mood mood, float dt)
    {
        Vector3 d = player - p.Pos;
        p.Yaw = Mathf.LerpAngle(p.Yaw, MathF.Atan2(-d.X, -d.Z), MathF.Min(1f, dt * 6f));
        if (mood != Mood.Hostile) return;
        p.Cooldown -= dt;
        if (p.Aim < 0f)
        {
            // One raises first; the next only a beat after: never all at once.
            if (p.Cooldown > 0f || _clock < _nextAimAt || !CanSee(p, player)) return;
            _nextAimAt = _clock + 0.9f + (float)_rng.NextDouble() * 1.1f;
            p.Aim = 1.4f;
            GuardAims++;
            Aimed?.Invoke(_place.ToWorld(p.Pos + Vector3.Up * 1.5f));
            return;
        }
        p.Aim -= dt;
        if (p.Aim > 0f) return;
        p.Aim = -1f;
        p.Cooldown = 2.5f;
        GuardShots++;
        Fired?.Invoke(_place.ToWorld(p.Pos + Vector3.Up * 1.5f), _place.ToWorld(player + Vector3.Up * 1.2f), CanSee(p, player));
    }

    bool CanSee(Person p, Vector3 player)
    {
        var space = _place.GetWorld3D().DirectSpaceState;
        Vector3 from = _place.ToWorld(p.Pos + Vector3.Up * 1.5f), to = _place.ToWorld(player + Vector3.Up * 1.4f);
        var q = PhysicsRayQueryParameters3D.Create(from, to, GalHack.Physics.Layers.World | GalHack.Physics.Layers.Hull);
        return space.IntersectRay(q).Count == 0;
    }

    /// <summary>A guard takes aim at you (where from), and fires (from, to, whether it can hit).</summary>
    public event Action<Vector3> Aimed;
    public event Action<Vector3, Vector3, bool> Fired;

    // ---- Being shot at, and shooting ------------------------------------------------------------------

    /// <summary>The survivor fired in or at the town: a peaceful or wary place turns hostile, its people flee.</summary>
    public void Provoked()
    {
        if (Memory.Mood == Mood.Hostile) return;
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
    float _nextAimAt;

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
            if (p.Dead || p.Indoors) continue;
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

    public void Shoot(Person p, float damage)
    {
        p.Health.Current -= damage;
        p.Hurt = 0.4f;
        Provoked();
        if (p.Dead) Fall(p);
    }

    void Fall(Person p)
    {
        _places?.Zone.Wildlife.Sim?.Targets.Remove(p);
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
        Founders founders = life.People.FoundersOf(Memory.Inst.SpawnSeed, tags.Contains("spaceport") || tags.Contains("trade") || tags.Contains("market"));
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
            p.Nerve = roll.Float();
            p.Speed *= role switch { PersonRole.Elder => 0.78f, PersonRole.Drunk => 0.72f, _ => 1f };
        }
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
        bool armed = f.Prop is "rifle" or "club";
        bool peaceful = Memory.Mood != Mood.Hostile;
        if (away > GreetRearm) p.Greeted = false;
        // Coming up close out of nowhere: a start, before anything else.
        if (away < StartleIn && !p.Close && !p.Greeted) { p.StartleFor = 0.9f; Speak(p, VoiceMood.Murmur); }
        p.Close = away < StartleIn + 1.5f;
        p.StartleFor = MathF.Max(0f, p.StartleFor - dt);
        BodyAct want = BodyAct.None;
        if (p.Fleeing) want = BodyAct.Panic;
        else if (p.StartleFor > 0f) want = f.Startled;
        else if (p.Covered && (!armed || f.Aimed != BodyAct.HandsUp) && p.Aim < 0f) want = f.Aimed;
        else if (peaceful && !p.Greeted && away < GreetIn && !p.Sitting && p.Now < 0.6f)
        {
            p.Greeted = true;
            p.ActHeld = 0f;
            // The first to react sets the tone; the rest mostly follow, each a little their own.
            var own = Wildlife.Rng.For(Memory.Inst.SpawnSeed, "stance", People.IndexOf(p));
            Stance stance = _setTone ?? _temper?.PersonStance(own) ?? Stance.Welcoming;
            if (_setTone != null && own.Chance(0.2f)) stance = _temper.PersonStance(own);
            _setTone ??= stance;
            want = Temper.ActFor(stance, f.Approach);
            Speak(p, VoiceMood.Murmur);
        }
        else if (p.Act is BodyAct.Greet or BodyAct.Present or BodyAct.Warn or BodyAct.Taunt && p.ActHeld < 2.6f && away < GreetRearm) want = p.Act;
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
            if (p.Dead || p.Indoors || p.Looks == null) continue;
            float d = (player - p.Pos).LengthSquared() * (0.6f + (float)_rng.NextDouble());
            if (d < best) { best = d; who = p; }
        }
        if (who == null) return;
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
    }

    bool _posing = true;
    float _clock;

    void Pose(Person p, int who)
    {
        if (p.View == null) return;
        if (p.View.Visible == p.Indoors) p.View.Visible = !p.Indoors;
        if (p.Indoors) return;
        // Far off they're one mesh, and only that moves.
        p.View.SyncFar(p.Pos, p.Yaw);
        if (!_posing) return;
        bool moving = p.Now > 0.05f;
        float dt = 1f / 60f;
        float t = _clock + who * 1.7f;
        p.Seated = Mathf.MoveToward(p.Seated, p.Sitting ? 1f : 0f, 2.5f * dt);
        p.Bent = Mathf.MoveToward(p.Bent, !moving && p.Role == "worker" ? 1f : 0f, 2f * dt);
        p.Raised = Mathf.MoveToward(p.Raised, p.Aim >= 0f ? 1f : 0f, 3f * dt);
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
            Down = p.Down,
            Act = p.Act,
            ActAmount = p.ActAmount,
            ActAt = p.Pos + new Vector3(-MathF.Sin(p.Yaw), 0f, -MathF.Cos(p.Yaw)) * 2f + Vector3.Up * 0.9f,
            Clock = t,
        });
        p.View.Sync();
        p.View.Hurt(p.Hurt / 0.4f);
    }
}
