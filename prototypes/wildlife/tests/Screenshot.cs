using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Renders one staged view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/wildlife res://tests/Screenshot.tscn -- --view=herd --out=shot.png
/// Views:
/// - herd: a temperate herd grazing below the hills
/// - pack: a pack closing in on the player from several sides
/// - giant: a territorial giant rearing into its trample
/// - bloom: a Bloom-corrupted creature winding up to lunge
/// - predator: a predator mid-stalk
/// - harvester: the hovercraft chewing through a grove
/// - lineup: the bestiary of two or three planets, for scale
/// - sheet: a contact sheet of one camera per species, for checking the generator by eye
/// - discovery: the brief card a first meeting brings up, over a stalking predator
/// - home: creatures in their home landform on a real generated planet (--landform=Plains, Mountains, DuneSea, Delta...)
/// --hud keeps the debug HUD. Only the discovery view shows a discovery card.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;

    Main _main;
    int _tick, _captureTick = 240;
    string _view = "herd", _out = "shot.png", _worlds = "temperate,alien,bloom";
    float _time = -1f;
    string _pick = "";
    bool _hud, _captured;
    Func<(Vector3 eye, Vector3 at)> _aim;
    Action _each;

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
            else if (arg == "--hud") _hud = true;
            else if (arg.StartsWith("--worlds=")) _worlds = arg["--worlds=".Length..];
            else if (arg.StartsWith("--pick=")) _pick = arg["--pick=".Length..];
            else if (arg.StartsWith("--frames=")) _frames = int.Parse(arg["--frames=".Length..]);
            else if (arg.StartsWith("--time=")) _time = float.Parse(arg["--time=".Length..], System.Globalization.CultureInfo.InvariantCulture);
        }
        ProcessPhysicsPriority = 100; // after Main has stepped the world
        if (_view.StartsWith("space-"))
        {
            _space = new SpaceScene { Name = "Space" };
            AddChild(_space);
            return;
        }
        if (IsDeep(_view))
        {
            _room = new Showroom { Name = "Showroom", FreeCamera = true };
            AddChild(_room);
            return;
        }
        _main = _view == "home" ? new Main { PatchTerrain = HomeTerrain() } : new Main();
        AddChild(_main);
    }

    SpaceScene _space;

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        if (_tick > SetupTick)
        {
            _each?.Invoke();
            if (_aim != null)
            {
                var (eye, at) = _aim();
                Camera3D camera = _main?.Camera ?? _room?.Camera ?? _space.Camera;
                camera.GlobalPosition = eye;
                camera.LookAt(at, Vector3.Up);
            }
        }
        if (_frames > 0)
        {
            if (_tick >= _captureTick && (_tick - _captureTick) % _frameEvery == 0 && _framesAsked < _frames)
            {
                _framesAsked++;
                CaptureFrame();
            }
        }
        else if (_tick == _captureTick && !_captured) Capture();
        // Its own time limit: a view that never gets to its shot still quits.
        if (_tick > _captureTick + _frames * _frameEvery + 900) Shutdown.Quit(this, 1);
    }

    /// <summary>
    /// Space fauna in their places, run by the space simulation:
    /// - space-nebula: a shoal of veil drifters pulsing through a nebula, a void leviathan behind
    /// - space-asteroids: belt rays grazing between the rocks of an asteroid field
    /// - space-derelict: hull leeches on a dead freighter, one rearing to strike at an intruder
    /// </summary>
    void SpaceSetup()
    {
        SpaceScene sp = _space;
        switch (_view)
        {
            case "space-nebula":
            {
                // A region whose drifters have a mouth and whose leviathan has teeth: the menacing end of the range.
                ulong region = 1;
                for (ulong r = 1; r < 400; r++)
                {
                    SpaceFauna f = sp.Generator.Generate(r, Habitat.Nebula);
                    Species d = f.Species.First(s => s.Swim == SwimStyle.Pulse), l = f.Species.First(s => s.Swim == SwimStyle.Undulate);
                    if (d.Part("head") == "maw" && d.Part("back") is "crystals" or "spines" && l.Part("head") is "jaws" or "maw" && l.Part("back") is "spines" or "ridge") { region = r; break; }
                }
                sp.Build(Habitat.Nebula, region);
                Species drifter = sp.Fauna.Species.First(s => s.Swim == SwimStyle.Pulse);
                Species leviathan = sp.Fauna.Species.First(s => s.Swim == SwimStyle.Undulate);
                List<Creature> shoal = sp.Sim.SpawnShoal(drifter, new Vector3(0f, 0f, -40f), 8);
                Herd h = shoal[0].Herd;
                sp.Sim.Spawn(leviathan, new Vector3(-80f, 22f, -190f), null, new Vector3(1f, -0.05f, 0.3f));
                _aim = () => (h.Center + new Vector3(14f, 2f, 22f), h.Center + new Vector3(-12f, 4f, -30f));
                _captureTick = 240;
                break;
            }
            case "space-asteroids":
            {
                sp.Build(Habitat.AsteroidField, 11);
                Species ray = sp.Fauna.Species.First(s => s.Swim == SwimStyle.Flap);
                Asteroid rock = sp.Sim.Asteroids.OrderByDescending(a => a.Radius / (a.Center.Length() + 50f)).First();
                Vector3 beside = rock.Center + (Vector3.Zero - rock.Center).Normalized() * (rock.Radius + 14f);
                List<Creature> shoal = sp.Sim.SpawnShoal(ray, beside, 6);
                Herd h = shoal[0].Herd;
                Vector3 toRock = (rock.Center - beside).Normalized(), side = toRock.Cross(Vector3.Up).Normalized();
                _aim = () => (h.Center - toRock * 22f + side * 16f + Vector3.Up * 12f, h.Center + toRock * 8f);
                _captureTick = 260;
                break;
            }
            case "space-derelict":
            {
                sp.Build(Habitat.Derelict, 5);
                Species leech = sp.Fauna.Species.First(s => s.Rig == RigKind.Clinger);
                Hull hull = sp.Sim.Hulls[0];
                // The breach side: the leeches crawl round the torn plating.
                List<Creature> leeches = sp.Sim.SpawnOnHull(leech, hull, 7, new Rng(9), onlyFace: 0);
                Vector3 n = hull.Normal(0);
                Creature striker = leeches.OrderBy(c => (c.Position - hull.FacePoint(0, 0.1f, 0f)).Length()).First();
                var intruder = new DummyTarget { Name = "Salvager", Position = striker.Position + n * 6f, Health = new Health(1e9f) };
                int strikeAt = _tick + 90;
                _each = () =>
                {
                    if (_tick == strikeAt)
                    {
                        sp.Sim.Targets.Add(intruder);
                        intruder.Position = striker.Position + n * 3f;
                    }
                };
                Vector3 up = hull.Basis * Vector3.Up, along = hull.Basis * Vector3.Back;
                Vector3 eye = striker.Position + n * 5.5f + up * 1.6f + along * 4.5f;
                _aim = () => (eye, striker.Position + n * 1.2f - along * 1.5f);
                _captureTick = strikeAt + (int)(leech.TelegraphWindup(sp.Data.Combat) * 0.75f * 60f);
                break;
            }
        }
    }

    void Setup()
    {
        if (_space != null)
        {
            SpaceSetup();
            return;
        }
        if (_room != null)
        {
            DeepSetup();
            return;
        }
        Main m = _main;
        m.FreeCamera = true;
        m.Hud.StatusVisible = _hud;
        // Discovery cards stay out of the shots, except the view staged to show one.
        m.Hud.ShowCards = _view == "discovery";
        m.Hud.ClearCards();
        m.Player.Muzzle.Visible = false;
        Player p = m.Player;
        if (HuskSetup()) return;
        if (ArmedSetup()) return;
        switch (_view)
        {
            case "discovery":
            {
                // The card a first meeting brings up, over a stalking predator.
                World(WorldKind.Alien, 0.42f);
                Species s = m.Fauna.Species.Where(x => x.Behaviour is Behaviour.Predator or Behaviour.PackHunter && x.Rig == RigKind.Legged && !m.Codex.Knows(x.Id))
                    .OrderByDescending(x => x.Weirdness).FirstOrDefault() ?? m.Fauna.Species.First(x => !m.Codex.Knows(x.Id));
                Vector3 toward = Turn(SunFlat(), 0.5f);
                Vector3 eye = Ground(new Vector3(-60f, 0f, 60f)) + Vector3.Up * (0.45f + s.Body.Size * 0.3f);
                p.Teleport(Ground(eye));
                foreach (Creature other in m.Sim.Creatures) if (WildSim.Flat(other.Position - eye).Length() < 40f) m.Sim.Remove(other);
                // Everything else on this world has been met already, so the card is this one's.
                foreach (Species other in m.Fauna.Species) if (other != s) m.Codex.Record(other, "Player 1", 0f, m.Fauna.PlanetName);
                m.Hud.ClearCards();
                Creature c = m.Sim.Spawn(s, eye + toward * (3.5f + s.Body.Size * 2.8f));
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                _each = () => c.SetState(CState.Stalk);
                // The codex looks from the game camera; first meetings queue the card.
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.15f);
                _each += () => { if (m.Hud.ShowingCard != null && m.Hud.Card.Modulate.A > 0.99f && _captureTick > _tick + 30) _captureTick = _tick + 30; };
                _captureTick = 600;
                break;
            }
            case "herd":
            case "temperate":
            {
                World(_view == "herd" ? WorldKind.Temperate : WorldKind.Alien, _view == "herd" ? 0.36f : 0.42f);
                p.Teleport(Ground(new Vector3(150f, 0f, 150f))); // out of the way: a calm herd
                Species herd = m.Fauna.Species.First(s => s.Behaviour == Behaviour.Herd);
                Creature[] members = m.Sim.Creatures.Where(c => c.Species == herd && c.Herd != null).OrderBy(c => c.Position.Length()).Take(8).ToArray();
                Herd h = members[0].Herd;
                // From the middle of the plain, looking out past the herd to the hills.
                Vector3 side = Turn(-WildSim.Flat(h.Center).Normalized(), 0.35f);
                float back = 4f + h.Spread * 0.7f + herd.Body.Size * 1.4f;
                _aim = () => (Ground(h.Center + side * back) + Vector3.Up * (1.0f + herd.Body.Size * 0.3f), h.Center + Vector3.Up * herd.Body.Size * 0.9f);
                _captureTick = 300;
                break;
            }
            case "pack":
            {
                World(WorldKind.Temperate, 0.41f);
                Species s = m.Fauna.Species.First(x => x.Behaviour == Behaviour.PackHunter);
                Vector3 stand = Ground(new Vector3(70f, 0f, -95f));
                p.Teleport(stand);
                p.Health.Max = p.Health.Current = 1e9f; // the staging must not end in a respawn
                Figure(stand);
                // Clear the area of other wildlife so the pack has the stage.
                foreach (Creature other in m.Sim.Creatures) if (WildSim.Flat(other.Position - stand).Length() < 60f) m.Sim.Remove(other);
                Vector3 from = stand + Turn(-SunFlat(), 0.9f) * 26f;
                List<Creature> pack = m.Sim.SpawnGroup(s, m.Sim.FreeSpot(from, new Rng(3)), 5);
                Pack pk = pack[0].Pack;
                // Over the player's shoulder, from behind and above, with the feinter ahead.
                _each = () =>
                {
                    if (pk.Phase == PackPhase.Engage && pk.PhaseTime > 0.5f && _captureTick > _tick + 2) _captureTick = _tick + 1;
                };
                _aim = () =>
                {
                    Vector3 front = pk.Feinter != null ? WildSim.Flat(pk.Feinter.Position - stand) : WildSim.Flat(pk.Center - stand);
                    front = front.LengthSquared() > 0.01f ? front.Normalized() : Vector3.Forward;
                    return (stand - front * 8.5f + Turn(front, 1.57f) * 2.5f + Vector3.Up * 6.5f, stand + front * 1f);
                };
                _captureTick = 60 * 40;
                break;
            }
            case "giant":
            {
                World(WorldKind.Temperate, 0.6f);
                Species s = m.Fauna.Species.Where(x => x.Plan == BodyPlan.Giant && x.Stats.Damage > 0f).OrderByDescending(x => x.Body.Size).First();
                Vector3 toward = Turn(SunFlat(), -0.4f);
                Vector3 stand = Ground(new Vector3(-110f, 0f, 60f));
                p.Teleport(stand);
                p.Health.Max = p.Health.Current = 1e9f;
                Figure(stand);
                foreach (Creature other in m.Sim.Creatures) if (WildSim.Flat(other.Position - stand).Length() < 50f) m.Sim.Remove(other);
                Creature c = m.Sim.Spawn(s, stand + toward * (s.Stats.AttackRange * 0.85f + 2f));
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                int windupAt = _tick + 30;
                _each = () => { if (_tick == windupAt) Attacks.Start(m.Sim, c, p.Target); };
                Vector3 side = Turn(toward, 1.57f);
                // Side-on, so a head lowered for the charge isn't foreshortened into a pole.
                _aim = () => (stand + side * (s.Body.Size * 1.5f) - toward * (s.Body.Size * 0.3f) + Vector3.Up * 2.4f, c.Rig.BoundsCenter * 0.6f + stand * 0.4f + Vector3.Up * 0.8f);
                _captureTick = windupAt + (int)(s.TelegraphWindup(m.Data.Combat) * 0.75f * 60f);
                break;
            }
            case "predator":
            {
                World(WorldKind.Alien, 0.62f); // late afternoon, the sun low behind us
                // The weirdest walking predator if there is one: the stalking crouch reads best on legs.
                Species predator = m.Fauna.Species.Where(s => s.Behaviour == Behaviour.Predator)
                    .OrderByDescending(s => (s.Rig == RigKind.Legged ? 1f : 0f) + s.Weirdness).First();
                Vector3 toward = Turn(SunFlat(), 0.5f);
                Vector3 eye = Ground(new Vector3(-60f, 0f, 60f)) + Vector3.Up * (0.45f + predator.Body.Size * 0.3f);
                p.Teleport(Ground(eye));
                float dist = 3.5f + predator.Body.Size * 2.8f;
                Creature c = m.Sim.Spawn(predator, eye + toward * dist);
                c.Yaw = MathF.Atan2(toward.X, toward.Z); // facing back at us
                // Hold it in the stalk (this close it would otherwise break into a charge).
                _each = () => c.SetState(CState.Stalk);
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.15f);
                _captureTick = 90;
                break;
            }
            case "bloom":
            {
                World(WorldKind.Bloom, 0.66f);
                // A corrupted hunter, rearing into its first, telegraphed lunge: seen three-quarters on.
                Species s = m.Fauna.Species.Where(x => x.Rig == RigKind.Legged && x.Plan != BodyPlan.Giant && x.Part("head") != "none")
                    .OrderByDescending(x => x.IsHunter ? 1 : 0).ThenByDescending(x => x.Weirdness).First();
                Vector3 toward = Turn(SunFlat(), -0.6f), side = Turn(toward, 1.57f);
                Vector3 stand = Ground(new Vector3(70f, 0f, 70f));
                p.Teleport(stand);
                p.Health.Max = p.Health.Current = 1e9f;
                foreach (Creature other in m.Sim.Creatures) if (WildSim.Flat(other.Position - stand).Length() < 30f) m.Sim.Remove(other);
                Creature c = m.Sim.Spawn(s, stand + toward * (s.Stats.AttackRange * 0.9f));
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                int windupAt = _tick + 40;
                _each = () => { if (_tick == windupAt) Attacks.Start(m.Sim, c, p.Target); };
                Vector3 eye = stand + side * (1.6f + s.Body.Size * 0.9f) - toward * 0.8f + Vector3.Up * (0.6f + s.Body.Size * 0.35f);
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.1f);
                _captureTick = windupAt + (int)(s.TelegraphWindup(m.Data.Combat) * 0.6f * 60f);
                break;
            }
            case "outpost":
            {
                // The players' outpost, whose zone the wildlife never spawns in, with the herds beyond it.
                World(WorldKind.Temperate, 0.4f);
                Structure s = m.Outpost.Structure;
                Vector3 back = new Basis(Vector3.Up, s.Yaw) * new Vector3(0.55f, 0f, 1f).Normalized();
                Vector3 eye = Ground(s.Center + back * 26f) + Vector3.Up * 7f;
                p.Teleport(Ground(s.Center + back * 40f));
                _aim = () => (eye, s.Center + Vector3.Up * 1.5f);
                _captureTick = 90;
                break;
            }
            case "home":
                HomeSetup();
                break;
            case "lineup":
                Lineup();
                break;
            case "sheet":
                Sheet();
                break;
            case "survey":
                Survey();
                break;
            case "harvester":
            {
                World(WorldKind.Alien, 0.42f);
                // Come at the grove from outside, so the camera behind the vessel sees it bite in.
                Vector3 grove = m.Forest.Groves[0];
                Vector3 outward = -SunFlat();
                Plant edge = m.Forest.Field.Plants.Where(x => x.Kind == PlantKind.Tree && WildSim.Flat(x.Position - grove).Length() < 25f)
                    .OrderByDescending(x => (x.Position - grove).Dot(outward)).First();
                Vessel v = m.Vessel;
                v.Yaw = MathF.Atan2(outward.X, outward.Z); // forward is -outward: into the trees
                v.GlobalPosition = Ground(edge.Position + outward * 6.5f) + Vector3.Up * 0.85f;
                v.Driver = p.Target;
                v.AutoThrottle = 0.4f;
                p.Teleport(Ground(new Vector3(160f, 0f, -160f)));
                int cutting = 0;
                _each = () => { if (v.CuttingFor > 0f) cutting++; if (cutting == 16) _captureTick = _tick + 1; };
                _aim = () =>
                {
                    Vector3 f = v.Forward, r = new(-f.Z, 0f, f.X);
                    return (v.GlobalPosition + f * 1.5f + r * 8.5f + Vector3.Up * 3.2f, v.GlobalPosition + f * 3.2f + Vector3.Up * 1.4f);
                };
                _captureTick = 900;
                break;
            }
        }
    }

    /// <summary>
    /// The bestiary: creatures of two or three planets side by side on clear ground for scale,
    /// one row per planet (front to back), smallest on the left, each row starting with a
    /// 1.8 m figure. Up to six per planet, one of each body form, so every row shows range.
    /// Creatures are frozen in place (idling, not wandering) and turned three-quarters to the
    /// camera. --worlds=temperate,alien,bloom picks the rows; "seed123:0.6" or
    /// "seed123:0.6:bloom" shows any planet.
    /// </summary>
    void Lineup()
    {
        Main m = _main;
        World(WorldKind.Temperate, _time >= 0f ? _time : 0.36f);
        m.Forest.Visible = false;
        m.Grass.Visible = false;
        m.Vessel.Visible = false;
        m.Outpost.Visible = false;
        m.Planet.ShowRocks(false);
        m.Player.Teleport(Ground(new Vector3(200f, 0f, 200f)));
        m.Sim.Clear();
        m.View.Bind(m.Sim, null);
        m.Sim.Frozen = true;
        m.Sim.Targets.Clear();

        // Lay out in a frame that looks along the sunlight, so the sun is behind the camera.
        Vector3 fwd = Turn(SunFlat(), 0.35f), right = fwd.Cross(Vector3.Up).Normalized();
        Vector3 At(float x, float depth) => right * x + fwd * depth;
        // Head to the left and turned towards the camera: three-quarter view.
        Vector3 face = (-right * 0.75f - fwd * 0.65f).Normalized();
        float faceYaw = MathF.Atan2(-face.X, -face.Z);

        var planets = _worlds.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(LineupPlanet).ToList();
        // One row per planet, one of each form, swarms aside (a cloud of specks says nothing
        // about scale); every planet's giants together in a last row at the back, so they
        // stand behind the others instead of hiding them.
        var rows = planets.Select(p => (p.Item2 + "\n" + p.Item1.PlanetName, p.Item1.Species.Where(s => s.Rig != RigKind.Swarm && s.Plan != BodyPlan.Giant)
            .GroupBy(s => s.Form).Select(g => g.First()).OrderBy(Footprint).Take(6).ToList())).ToList();
        var giants = planets.SelectMany(p => p.Item1.Species.Where(s => s.Plan == BodyPlan.Giant).GroupBy(s => s.Form).Select(g => g.First())).OrderBy(Footprint).ToList();
        if (giants.Count > 0) rows.Add(("Giants", giants));
        float depth = 0f, widest = 0f, frontWidth = 0f;
        foreach ((string label, List<Species> species) in rows)
        {
            if (species.Count == 0) continue;
            float total = species.Sum(s => Footprint(s) + 1.5f) + 2f;
            widest = MathF.Max(widest, total);
            if (label != "Giants") frontWidth = MathF.Max(frontWidth, total);
            float rowDepth = label == "Giants" ? 6f : species.Max(s => MathF.Min(Footprint(s), 6f));
            float z = depth + rowDepth * 0.5f;
            float x = -total * 0.5f;
            Figure(At(x + 0.5f, z));
            var tag = new Label3D
            {
                Text = label, FontSize = 64, PixelSize = 0.006f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.95f, 0.85f), OutlineSize = 18, OutlineModulate = new Color(0f, 0f, 0f, 0.8f),
            };
            m.AddChild(tag);
            tag.GlobalPosition = Ground(At(x + 0.5f, z)) + Vector3.Up * 2.6f;
            x += 2f;
            foreach (Species s in species)
            {
                float f = Footprint(s);
                x += f * 0.5f;
                Creature c = m.Sim.Spawn(s, At(x, z));
                c.Yaw = faceYaw;
                if (s.Flies) c.Altitude = s.Rig == RigKind.GasBag ? s.Body.Height * 0.9f + 1.2f : 1.4f + s.Body.Size * 0.3f;
                c.Position = new Vector3(c.Position.X, m.Planet.Terrain.Height(c.Position.X, c.Position.Z) + (s.Flies ? c.Altitude : 0f), c.Position.Z);
                c.Rig.Reset(m.Sim, c);
                x += f * 0.5f + 1.5f;
            }
            depth += rowDepth + 4f;
        }

        // From in front and above, far enough that the front row fits across.
        m.Camera.Fov = 45f;
        float hfov = 2f * MathF.Atan(MathF.Tan(Mathf.DegToRad(45f) * 0.5f) * 16f / 9f);
        float dist = frontWidth * 0.5f / MathF.Tan(hfov * 0.5f) * 1.3f;
        Vector3 look = At(0f, depth * 0.42f) + Vector3.Up * 3.6f;
        Vector3 eye = At(0f, -dist * 0.75f) + Vector3.Up * (dist * 0.62f);
        _aim = () => (eye, look);
        _captureTick = 150;
    }

    /// <summary>
    /// A contact sheet for checking the generator by eye: every species of the chosen planets,
    /// each in its own small view, framed three-quarters from the front at a size where faces,
    /// horns and feet can be judged. Same world, one camera per creature.
    /// </summary>
    void Sheet()
    {
        Main m = _main;
        World(WorldKind.Temperate, _time >= 0f ? _time : 0.36f);
        m.Forest.Visible = false;
        m.Grass.Visible = false;
        m.Vessel.Visible = false;
        m.Outpost.Visible = false;
        m.Planet.ShowRocks(false);
        m.Hud.Visible = false;
        m.Player.Teleport(Ground(new Vector3(200f, 0f, 200f)));
        m.Sim.Clear();
        m.View.Bind(m.Sim, null);
        m.Sim.Frozen = true;
        m.Sim.Targets.Clear();
        Vector3 sun = SunFlat();

        var species = _worlds.Split(',', StringSplitOptions.RemoveEmptyEntries).SelectMany(w => LineupPlanet(w).Item1.Species).ToList();
        if (_pick != "") species = _pick.Split(',').Select(int.Parse).Select(i => species[i]).ToList();
        int cols = species.Count <= 1 ? 1 : species.Count <= 4 ? 2 : species.Count <= 9 ? 3 : 4, rows = (species.Count + cols - 1) / cols;
        var grid = new GridContainer { Columns = cols };
        grid.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        grid.AddThemeConstantOverride("h_separation", 2);
        grid.AddThemeConstantOverride("v_separation", 2);
        var layer = new CanvasLayer { Layer = 5 };
        layer.AddChild(grid);
        AddChild(layer);
        Vector2I cell = new(1600 / cols - 2, 900 / rows - 2);
        var cams = new List<(Camera3D cam, Creature c)>();
        for (int i = 0; i < species.Count; i++)
        {
            Species s = species[i];
            // Far apart, so no creature wanders into another's frame.
            Vector3 at = new(-120f + (i % 8) * 34f, 0f, -60f + (i / 8) * 40f);
            Creature c = m.Sim.Spawn(s, at);
            Vector3 face = Turn(-sun, 0.75f);
            c.Yaw = MathF.Atan2(-face.X, -face.Z);
            if (s.Rig == RigKind.Swarm) c.Altitude = s.Flies ? 1.2f : 0f;
            else if (s.Flies) c.Altitude = s.Rig == RigKind.GasBag ? s.Body.Height * 0.9f + 1.2f : 1.4f + s.Body.Size * 0.3f;
            c.Position = new Vector3(c.Position.X, m.Planet.Terrain.Height(c.Position.X, c.Position.Z) + (s.Flies ? c.Altitude : 0f), c.Position.Z);
            c.Rig.Reset(m.Sim, c);

            var box = new SubViewportContainer { Stretch = true, CustomMinimumSize = cell };
            var vp = new SubViewport { Size = cell, Msaa3D = Viewport.Msaa.Msaa4X };
            var cam = new Camera3D { Fov = 40f, Near = 0.05f, Far = 800f };
            vp.AddChild(cam);
            box.AddChild(vp);
            var label = new Label { Text = $"{s.Name}: {s.CommonName}, {s.Behaviour}{(s.Stats.Damage > 0f ? $", {s.Attack.Name}" : "")}  {s.Body.Size:F1} m", Position = new Vector2(6, 4) };
            label.AddThemeFontSizeOverride("font_size", 13);
            label.AddThemeColorOverride("font_outline_color", Colors.Black);
            label.AddThemeConstantOverride("outline_size", 4);
            box.AddChild(label);
            grid.AddChild(box);
            cams.Add((cam, c));
        }
        _each = () =>
        {
            foreach (var (cam, c) in cams)
            {
                Species sp = c.Species;
                float r = sp.Rig switch
                {
                    RigKind.Winged => sp.Body.Size * 1.1f,
                    RigKind.Swarm => 2f,
                    RigKind.GasBag => MathF.Max(sp.Body.Width, sp.Body.Height) * 0.9f + sp.Body.TailLength * 0.25f,
                    _ => MathF.Max(sp.Body.Size * 0.62f, (sp.Body.LegLength + sp.Body.Height) * 0.55f),
                };
                Vector3 look = sp.Rig == RigKind.GasBag ? c.Position + Vector3.Down * sp.Body.TailLength * 0.25f : c.Rig.BoundsCenter;
                Vector3 dir = (c.Forward * 0.8f + c.Forward.Cross(Vector3.Up) * 0.6f).Normalized();
                cam.GlobalPosition = look + dir * r * 2.6f + Vector3.Up * r * 0.9f;
                cam.LookAt(look, Vector3.Up);
            }
        };
        _captureTick = 120;
    }

    /// <summary>
    /// Prints planets whose fauna would make good demo worlds (a walking herd, a walking pack,
    /// a giant) for --time as alienness, then quits. Runs headless; picks the worlds' seeds.
    /// </summary>
    void Survey()
    {
        float alien = _time >= 0f ? _time : 0.1f;
        for (ulong seed = 10000; seed < 10600; seed++)
        {
            PlanetFauna f = _main.Generator.Generate(seed, alien, false);
            bool herd = f.Species.Any(x => x.Behaviour == Behaviour.Herd && x.Rig == RigKind.Legged && x.Plan != BodyPlan.Giant);
            bool pack = f.Species.Any(x => x.Behaviour == Behaviour.PackHunter && x.Rig == RigKind.Legged && x.Body.Size > 1.4f);
            bool giant = f.Species.Any(x => x.Plan == BodyPlan.Giant);
            if (herd && pack && giant)
                GD.Print($"SURVEY {seed}: " + string.Join(" | ", f.Species.Select(x => $"{x.CommonName} {x.Behaviour} {x.Body.Size:F1}")));
        }
        Shutdown.Quit(this);
    }

    /// <summary>The planets a lineup row can show: the game's three worlds, or any seed.</summary>
    (PlanetFauna, string) LineupPlanet(string name)
    {
        FaunaGenerator gen = _main.Generator;
        WorldStyle Style(WorldKind k) => WorldStyle.For(k);
        return name switch
        {
            "temperate" => (gen.Generate(Style(WorldKind.Temperate).FaunaSeed, Style(WorldKind.Temperate).Alienness, false), "Temperate"),
            "alien" => (gen.Generate(Style(WorldKind.Alien).FaunaSeed, Style(WorldKind.Alien).Alienness, false), "Alien"),
            "bloom" => (gen.Generate(Style(WorldKind.Bloom).FaunaSeed, Style(WorldKind.Bloom).Alienness, true), "Bloom-infested"),
            _ when name.StartsWith("seed") => LineupSeed(name),
            _ => throw new ArgumentException($"unknown lineup row {name}"),
        };
    }

    /// <summary>"seed123:0.6" or "seed123:0.6:bloom": any planet, for checking the generator by eye.</summary>
    (PlanetFauna, string) LineupSeed(string spec)
    {
        string[] parts = spec[4..].Split(':');
        ulong seed = ulong.Parse(parts[0]);
        float alien = parts.Length > 1 ? float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture) : 0.5f;
        bool bloom = parts.Length > 2 && parts[2] == "bloom";
        return (_main.Generator.Generate(seed, alien, bloom), $"seed {seed}, alien {alien:F1}{(bloom ? ", Bloom" : "")}");
    }

    /// <summary>How much room a species needs in the row.</summary>
    static float Footprint(Species s) => s.Rig switch
    {
        RigKind.Winged => s.Body.Size * 2.2f,
        RigKind.GasBag => MathF.Max(s.Body.Width, s.Body.Size * 0.6f) * 1.3f,
        RigKind.Swarm => 3f,
        RigKind.Chain => s.Body.Size * 0.6f,
        _ => MathF.Max(s.Body.Size * 0.9f, s.Body.Width + s.Body.LegLength),
    };

    /// <summary>A plain 1.8 m figure for scale.</summary>
    void Figure(Vector3 at)
    {
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.62f, 0.64f, 0.66f), Roughness = 0.8f };
        var body = new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.22f, Height = 1.45f }, MaterialOverride = mat };
        var head = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.14f, Height = 0.28f }, MaterialOverride = mat };
        _main.AddChild(body);
        _main.AddChild(head);
        Vector3 g = Ground(at);
        body.GlobalPosition = g + Vector3.Up * 0.725f;
        head.GlobalPosition = g + Vector3.Up * 1.64f;
    }

    void World(WorldKind kind, float time)
    {
        _main.SwitchWorld(kind);
        _main.Sim.Clock.Time01 = time;
        _main.Planet.UpdateSky(_main.Sim.Clock); // so the sun direction is current for staging
    }

    /// <summary>The way the sunlight travels, flattened: look along it and the sun is behind you.</summary>
    Vector3 SunFlat() => WildSim.Flat(_main.Planet.SunDirection).Normalized();

    static Vector3 Turn(Vector3 v, float radians) => new Basis(Vector3.Up, radians) * v;

    Vector3 Ground(Vector3 p) => new(p.X, _main.Planet.Terrain.Height(p.X, p.Z), p.Z);

    async void Capture()
    {
        _captured = true;
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        Shutdown.Quit(this, result == Error.Ok ? 0 : 1);
    }
}
