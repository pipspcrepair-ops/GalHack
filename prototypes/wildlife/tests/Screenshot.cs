using System;
using System.Linq;
using Godot;

namespace Wildlife;

/// <summary>
/// Renders one staged view of the prototype to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/wildlife res://tests/Screenshot.tscn -- --view=herd --out=shot.png
/// Views: herd (an alien herd on the plains), predator (a weird predator mid-stalk), bloom (a
/// corrupted creature winding up to lunge), harvester (the hovercraft chewing through a grove),
/// temperate (a temperate scene for contrast). --hud keeps the debug HUD.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;

    Main _main;
    int _tick, _captureTick = 240;
    string _view = "herd", _out = "shot.png";
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
        }
        ProcessPhysicsPriority = 100; // after Main has stepped the world
        _main = new Main();
        AddChild(_main);
    }

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
                _main.Camera.GlobalPosition = eye;
                _main.Camera.LookAt(at, Vector3.Up);
            }
        }
        if (_tick == _captureTick && !_captured) Capture();
    }

    void Setup()
    {
        Main m = _main;
        m.FreeCamera = true;
        m.Hud.StatusVisible = _hud;
        m.Player.Muzzle.Visible = false;
        Player p = m.Player;
        switch (_view)
        {
            case "herd":
            case "temperate":
            {
                World(_view == "herd" ? WorldKind.Alien : WorldKind.Temperate, _view == "herd" ? 0.33f : 0.42f);
                p.Teleport(Ground(new Vector3(150f, 0f, 150f))); // out of the way: a calm herd
                Species herd = m.Fauna.Species.First(s => s.Behaviour == Behaviour.Herd);
                Creature[] members = m.Sim.Creatures.Where(c => c.Species == herd && c.Herd != null).OrderBy(c => c.Position.Length()).Take(8).ToArray();
                Herd h = members[0].Herd;
                Vector3 side = new Vector3(0.75f, 0f, 1f).Normalized();
                float back = 3.5f + h.Spread * 0.6f + herd.Body.Size * 1.3f;
                _aim = () => (Ground(h.Center + side * back) + Vector3.Up * (0.8f + herd.Body.Size * 0.25f), h.Center + Vector3.Up * herd.Body.Size * 0.5f);
                _captureTick = 300;
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
                World(WorldKind.Bloom, 0.7f);
                Species s = m.Fauna.Species.Where(x => x.Rig is RigKind.Legged or RigKind.Chain && x.Plan != BodyPlan.Giant)
                    .OrderByDescending(x => x.Weirdness).First();
                Vector3 toward = Turn(SunFlat(), -0.6f);
                Vector3 eye = Ground(new Vector3(70f, 0f, 70f)) + Vector3.Up * (0.7f + s.Body.Size * 0.3f);
                p.Teleport(Ground(eye));
                Creature c = m.Sim.Spawn(s, eye + toward * (3.2f + s.Body.Size * 1.3f));
                c.Yaw = MathF.Atan2(toward.X, toward.Z);
                int windupAt = _tick + 40;
                _each = () => { if (_tick == windupAt) Attacks.Start(m.Sim, c, p.Target); };
                _aim = () => (eye, c.Rig.BoundsCenter + Vector3.Up * 0.1f);
                _captureTick = windupAt + (int)(s.TelegraphWindup(m.Data.Combat) * 0.6f * 60f);
                break;
            }
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
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
