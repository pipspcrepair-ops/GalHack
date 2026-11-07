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
/// its people. Bodies are placeholders: a capsule, a head and a colour by role.
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
        /// <summary>Their head, turned as they glance about.</summary>
        public Node3D Head;
        public float HeadYaw;
        public bool Indoors, Sitting, Fleeing;
        public Person Partner;
        public Node3D Body;
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

    public Vector3 World(Vector3 local) => _place != null ? _place.ToWorld(local) : local;

    // ---- Standing and sitting ---------------------------------------------------------------------

    static readonly Dictionary<string, Color> RoleColour = new()
    {
        ["vendor"] = new Color("c9862b"), ["worker"] = new Color("4a6a8a"), ["guard"] = new Color("3a3f36"),
        ["rusher"] = new Color("8a4a3a"), ["wanderer"] = new Color("7a7462"),
    };

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
        foreach (Person p in People) p.Body = MakeBody(p);
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
        public override void _PhysicsProcess(double delta) => Town.Tick((float)delta);
    }

    bool Night()
    {
        float t = (float)_places.Zone.Sky.TimeOfDay;
        return t < 0.24f || t > 0.8f;
    }

    /// <summary>Beyond this someone updates a few times a second; beyond the second, their body isn't posed at all.</summary>
    const float NearPeople = 40f, FarPeople = 90f;
    int _tick;

    void Tick(float tickDt)
    {
        if (_place == null || !_place.Ready || _place.Dormant) return;
        bool night = Night();
        Mood mood = Memory.Mood;
        Vector3 player = _place.FromWorld(_places.Zone.Wildlife.PlayerWorld());
        _tick++;
        for (int who = 0; who < People.Count; who++)
        {
            Person p = People[who];
            if (p.Dead) continue;
            // Far from you a town needn't move every tick: each in their turn, with the time they missed.
            p.Saved += tickDt;
            float away = (player - p.Pos).Length();
            if (away > NearPeople && (_tick + who) % 6 != 0) continue;
            float dt = p.Saved;
            p.Saved = 0f;
            _posing = away < FarPeople;
            bool guard = p.Role == "guard";
            p.Hurt = MathF.Max(0f, p.Hurt - dt);
            // Hostile: the people go indoors; the guards turn on you.
            bool hide = !guard && (night || mood == Mood.Hostile);
            if (hide && !p.Indoors)
            {
                if (p.Path == null || p.Step >= p.Path.Count || !p.Fleeing && !night) GoTo(p, p.Home);
                p.Fleeing = mood == Mood.Hostile;
                p.Speed = p.Fleeing ? 3.4f : p.Speed;
                if (Move(p, dt)) { p.Indoors = _homes.Length > 0; }
                Pose(p);
                continue;
            }
            if (!hide && p.Indoors)
            {
                p.Indoors = false;
                p.Fleeing = false;
                p.Speed = p.Spawn.Gait == "hurry" ? 3.0f : p.Spawn.Gait == "amble" ? 1.1f : 1.3f;
            }
            if (p.Indoors) { Pose(p); continue; }
            if (guard && mood != Mood.Peaceful && (player - p.Pos).Length() < 40f)
            {
                Guard(p, player, mood, dt);
                Pose(p);
                continue;
            }
            if (Move(p, dt))
            {
                p.Wait -= dt;
                if (p.Wait <= 0f) Next(p);
                else Idle(p, dt);
            }
            Pose(p);
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
                if (linked.Count > 0) GoTo(p, linked[_rng.Next(linked.Count)]);
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
            if (p.Cooldown > 0f || !CanSee(p, player)) return;
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
        Memory.Mood = Mood.Hostile;
        foreach (Person p in People) { p.Path = null; p.Step = 0; p.Partner = null; }
        TurnedHostile?.Invoke();
    }

    public event Action TurnedHostile;

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
            Vector3 c = p.Pos + Vector3.Up * (p.Sitting ? 0.6f : 0.95f);
            float along = (c - e).Dot(dir);
            if (along < 0f || along > distance) continue;
            Vector3 off = e + dir * along - c;
            if (new Vector2(off.X, off.Z).Length() > 0.35f || MathF.Abs(off.Y) > 0.95f) continue;
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
        if (p.Body != null) p.Body.RotationDegrees = new Vector3(90f, Mathf.RadToDeg(p.Yaw), 0f);
        _places?.Zone.Wildlife.Sim?.Targets.Remove(p);
    }

    // ---- Bodies ---------------------------------------------------------------------------------------

    Node3D MakeBody(Person p)
    {
        var root = new Node3D { Name = $"{p.Role} {People.IndexOf(p)}" };
        Color c = RoleColour.GetValueOrDefault(p.Role, new Color("777777"));
        var cloth = new StandardMaterial3D { AlbedoColor = c, Roughness = 0.9f };
        var skin = new StandardMaterial3D { AlbedoColor = new Color("b08a6a"), Roughness = 0.8f };
        root.AddChild(new MeshInstance3D { Name = "Body", Mesh = new CapsuleMesh { Radius = 0.24f, Height = 1.35f }, Position = Vector3.Up * 0.72f, MaterialOverride = cloth });
        root.AddChild(new MeshInstance3D { Name = "Head", Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f }, Position = Vector3.Up * 1.55f, MaterialOverride = skin });
        // A hood or cap in a darker shade, so they don't read as mannequins.
        root.AddChild(new MeshInstance3D { Name = "Hood", Mesh = new SphereMesh { Radius = 0.155f, Height = 0.18f, IsHemisphere = true }, Position = Vector3.Up * 1.58f, MaterialOverride = new StandardMaterial3D { AlbedoColor = c.Darkened(0.4f) } });
        if (p.Role == "guard")
            root.AddChild(new MeshInstance3D { Name = "Rifle", Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.8f) }, Position = new Vector3(0.22f, 1.1f, -0.2f), RotationDegrees = new Vector3(-20, 0, 0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("202020") } });
        if (p.Role == "vendor")
            root.AddChild(new MeshInstance3D { Name = "Apron", Mesh = new BoxMesh { Size = new Vector3(0.36f, 0.6f, 0.04f) }, Position = new Vector3(0, 0.75f, -0.24f), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e8d8b0") } });
        _view.AddChild(root);
        root.Position = p.Pos;
        if (p.Dead) root.RotationDegrees = new Vector3(90f, 0, 0);
        return root;
    }

    bool _posing = true;

    void Pose(Person p)
    {
        if (p.Body == null) return;
        p.Body.Visible = !p.Indoors;
        if (p.Indoors || !_posing) return;
        bool moving = p.Now > 0.05f;
        int who = People.IndexOf(p);
        float t = (float)_places.Zone.Sky.TimeOfDay * 86400f / 60f + who;
        // The bob and sway go by the ground covered, so they keep step with the walk.
        float stride = p.Gait * 3.2f;
        float bob = moving ? MathF.Abs(MathF.Sin(stride)) * 0.05f : 0f;
        float sway = moving ? MathF.Sin(stride) * 0.03f : MathF.Sin(t * 0.7f) * 0.012f; // at rest, a slow shift of weight
        float lean = Math.Clamp(p.Now / 3f, 0f, 1f) * 7f;
        p.Seated = Mathf.MoveToward(p.Seated, p.Sitting ? 1f : 0f, 2.5f / 60f);
        float work = !moving && p.Role == "worker" ? MathF.Max(0f, MathF.Sin(t * 1.7f)) * 18f : 0f;
        p.Body.Position = p.Pos + Vector3.Up * (bob - p.Seated * 0.45f);
        p.Body.Rotation = new Vector3(Mathf.DegToRad(-work - lean), p.Yaw, sway);
        p.Head ??= p.Body.GetNodeOrNull<Node3D>("Head");
        if (p.Head == null) return;
        // Looks at you if you're close and in front of them; otherwise glances about now and then.
        float look = MathF.Sin(t * 0.31f) > 0.55f ? MathF.Sin(t * 1.1f) * 0.9f : 0f;
        Vector3 toPlayer = _place.FromWorld(_places.Zone.Wildlife.PlayerWorld()) - p.Pos;
        if (toPlayer.LengthSquared() < 100f)
        {
            float off = Mathf.AngleDifference(p.Yaw, MathF.Atan2(-toPlayer.X, -toPlayer.Z));
            if (MathF.Abs(off) < 1.25f) look = off;
        }
        p.HeadYaw = Mathf.Lerp(p.HeadYaw, look, 0.08f);
        p.Head.Rotation = new Vector3(0f, p.HeadYaw, 0f);
    }
}
