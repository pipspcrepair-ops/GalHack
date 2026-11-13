using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using N = System.Numerics;
using TInt3 = TerrainDigging.Terrain.Int3;

namespace PhysicsSandbox.World;

/// <summary>
/// The playable sandbox: the shared world plus a sky, a camera, a HUD and the keys. Input goes
/// to the player's legs, or to the vessel whose seat they are in, at physics priority -20 so
/// the physics host (-10) uses it the same tick. The camera reads interpolated transforms, so
/// everything looks smooth at any frame rate.
/// </summary>
public partial class Main : Node3D
{
    public SandboxWorld World { get; private set; }
    public Camera3D Camera { get; private set; }
    public Sky Backdrop { get; private set; }
    public Hud Hud { get; private set; }
    public TornadoView Tornadoes { get; private set; }

    /// <summary>False for screenshots: no mouse capture, no keys; a script drives everything.</summary>
    public bool Interactive = true;

    /// <summary>Chase camera instead of first person.</summary>
    public bool ThirdPerson;

    /// <summary>Screenshots: place the camera here instead of following anyone.</summary>
    public Transform3D? FixedCamera;

    /// <summary>Screenshots: the fixed camera is the player's own eye, so their body must not be drawn.</summary>
    public bool FixedIsEye;

    Vector2 _mouse, _stick;
    int _podsShot;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -20; // input before the physics host steps
        World = new SandboxWorld { Name = "World", Visuals = true };
        AddChild(World);
        Backdrop = Sky.Build(this);
        Tornadoes = new TornadoView { Name = "Tornadoes", Sim = World.Weather.Sim };
        AddChild(Tornadoes);
        Camera = new Camera3D { Name = "Camera", Fov = 75f, Near = 0.05f, Far = 4000f, Current = true };
        AddChild(Camera);
        Hud = new Hud { Main = this };
        AddChild(Hud);

        PhysicsWorld physics = World.Physics;
        physics.Impact += i =>
        {
            if (i.Speed > 8f) Hud.Log($"{i.Body.Label} hit {(i.Other != null ? i.Other.Label : i.Surface.ToString().ToLowerInvariant())} at {i.Speed:F0} m/s: {i.Damage:F2} damage");
        };
        physics.Cratered += c => Hud.Log($"crater: {c.CellsRemoved} cells, {c.Radius:F1} m, logged");
        physics.Rescued += r => Hud.Log($"{r.Body.Label} was inside {r.Inside}: moved {N.Vector3.Distance(r.From, r.To):F1} m to open ground");
        physics.Severed += s => Hud.Log($"{s.Wreck.Label} broke off {s.Parent.Label} ({s.Wreck.Mass:F0} kg)");
        physics.Landed += b => Hud.Log($"{b.Label} landed");
        physics.LiftedOff += b => Hud.Log($"{b.Label} lifted off");
        World.Player.Moved += m => Hud.Log($"you: {m}");

        if (Interactive) Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    // ---- Input ------------------------------------------------------------------------------

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Interactive) return;
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured) _mouse += motion.Relative;
        else if (e is InputEventMouseButton { Pressed: true } && Input.MouseMode != Input.MouseModeEnum.Captured)
            Input.MouseMode = Input.MouseModeEnum.Captured;
        else if (e is InputEventKey { Pressed: true, Echo: false } key) OnKey(key.PhysicalKeycode);
    }

    void OnKey(Key key)
    {
        Player p = World.Player;
        Ship flying = p.Seated;
        switch (key)
        {
            case Key.Escape: Input.MouseMode = Input.MouseModeEnum.Visible; break;
            case Key.F1: Hud.ShowHelp = !Hud.ShowHelp; break;
            case Key.V: ThirdPerson = !ThirdPerson; break;
            case Key.R: GetTree().ReloadCurrentScene(); break;
            case Key.F:
                if (flying != null) p.StandUp();
                else if (p.Aboard is Ship ship && N.Vector3.Distance(p.Body.Position, ship.Seat) < 1.6f) p.Sit(ship);
                else Hud.Log("the pilot seat is at the front of the cabin");
                break;
            case Key.Key1: p.PlaceAboard(World.Corsair, new N.Vector3(0f, 0.3f, 1.5f)); break;
            case Key.Key2: p.Sit(World.Rover); break;
            case Key.Key3: p.PlaceOutside(World.Corsair.Body.ToParent(new N.Vector3(-1f, 2.9f, -1.5f))); break;
            case Key.Key4: Orbit(); break;
            case Key.Key5: Swim(); break;
            case Key.C: flying?.Flight.ToggleCruise(); break;
            case Key.Y:
                if ((flying ?? p.Aboard) is Ship s && s.Body.Core != null)
                {
                    s.Body.Core.On = !s.Body.Core.On;
                    s.Cabin.SetGravity(s.Body.Core);
                }
                break;
            case Key.K: ShootPod(); break;
            case Key.B: AtCrosshair(blast: true); break;
            case Key.N: AtCrosshair(blast: false); break;
            case Key.T: TornadoAhead(); break;
        }
    }

    /// <summary>
    /// A spacewalk: the Corsair parked in orbit, 100 m above the atmosphere, with its core gravity
    /// on, and the player on its roof, held there by it.
    /// </summary>
    public void Orbit()
    {
        Ship ship = World.Corsair;
        Player p = World.Player;
        if (ship == null || ship.IsWreck) return;
        if (p.Seated != null) p.StandUp();
        ship.Vessel.Place(ship.Body, new N.Vector3(0f, World.Physics.Zone.Ceiling + 100f, 0f), 0f);
        ship.Binding.Teleport();
        ship.Flight.Input = PilotInput.Autopilot;
        ship.Body.Core.On = true;
        ship.Cabin.SetGravity(ship.Body.Core);
        p.PlaceAboard(ship, new N.Vector3(-1f, 2.78f, -1.5f));
        Hud.Log("in orbit: the core's gravity holds you to the roof. Walk off an edge or jump to let go; Y turns it off and you float");
    }

    /// <summary>A swim: the player on the lake's southern beach, looking out across it.</summary>
    public void Swim()
    {
        Player p = World.Player;
        var beach = new N.Vector2(SandboxTerrain.LakeCenter.X, SandboxTerrain.LakeCenter.Y + SandboxTerrain.LakeRadiusZ * 1.15f);
        p.PlaceOutside(World.Ground(beach.X, beach.Y) + N.Vector3.UnitY * 0.05f);
        p.Yaw = 0f; // facing north, over the water
        p.Pitch = -0.1f;
        Hud.Log("the lake: walk in and swim; Space up, Ctrl down. Under water your suit breathes its own oxygen");
    }

    /// <summary>Shoots one of the Corsair's engine pods off (port, then starboard).</summary>
    public void ShootPod()
    {
        Ship ship = World.Corsair;
        if (ship == null || _podsShot >= 2) return;
        var cells = _podsShot == 0 ? Ship.PortPodMount : Ship.StarboardPodMount;
        ship.ShootThrough(cells, push: 5f);
        _podsShot++;
    }

    void AtCrosshair(bool blast)
    {
        Transform3D eye = Camera.GlobalTransform;
        N.Vector3 from = eye.Origin.ToNumerics(), dir = (-eye.Basis.Z).ToNumerics();
        if (!World.Store.Raycast(from, dir, 150f, out TInt3 cell, out _, out float distance)) return;
        N.Vector3 hit = from + dir * distance;
        if (blast)
        {
            CraterResult r = World.Terrain.Crater(hit + N.Vector3.UnitY, 4f);
            World.Physics.NotifyChanged(r.Changed);
            Hud.Log($"crater: {r.CellsRemoved} cells");
        }
        else World.Terrain.Dig(cell, 1.5f);
    }

    public void TornadoAhead()
    {
        N.Vector3 me = World.Player.Body.WorldPosition;
        Vector3 ahead = -Camera.GlobalBasis.Z;
        ahead.Y = 0f;
        Vector3 at = me.ToGodot() + ahead.Normalized() * 120f;
        World.Weather.Calm = false;
        World.Weather.Sim.SpawnTornado(at, me.ToGodot());
        Hud.Log("a funnel is lowering out of the cloud base ahead");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!Interactive) return;
        Player p = World.Player;
        Vector2 mouse = _mouse;
        _mouse = Vector2.Zero;

        if (p.Seated is Ship ship)
        {
            // The mouse moves a virtual stick that recentres by itself (as in ship-handling).
            _stick = (_stick * 0.85f + mouse * 0.02f).Clamp(new Vector2(-1, -1), new Vector2(1, 1));
            var move = new Vector3(Axis(Key.D, Key.A), Axis(Key.Space, Key.Ctrl), Axis(Key.S, Key.W));
            var turn = new Vector3(-_stick.Y, -_stick.X, Axis(Key.Q, Key.E));
            ship.Flight.Input = PilotInput.Hands(move, turn, Held(Key.Shift));
            ship.Flight.Input.Jump = Held(Key.Space) && ship.Flight.Stats?.IsHover == true;
            return;
        }

        p.Yaw -= mouse.X * 0.0025f;
        p.Pitch = Mathf.Clamp(p.Pitch - mouse.Y * 0.0025f, -1.5f, 1.5f);
        p.Legs.Move = new N.Vector2(Axis(Key.D, Key.A), Axis(Key.S, Key.W));
        p.Legs.Sprint = Held(Key.Shift);
        p.Legs.Jump = Held(Key.Space);
        p.Legs.Jetpack = Held(Key.Space);
        p.Legs.Vertical = Axis(Key.Space, Key.Ctrl);
    }

    static bool Held(Key k) => Input.IsPhysicalKeyPressed(k);
    static float Axis(Key positive, Key negative) => (Held(positive) ? 1f : 0f) - (Held(negative) ? 1f : 0f);

    // ---- Camera -----------------------------------------------------------------------------

    public override void _Process(double delta)
    {
        Player p = World.Player;
        if (p.View != null)
        {
            p.View.GlobalTransform = p.WorldTransformInterpolated();
            p.View.Visible = (FixedCamera != null && !FixedIsEye) || (FixedCamera == null && ThirdPerson);
        }
        Camera.GlobalTransform = FixedCamera is Transform3D fixedCam ? fixedCam
            : p.Seated is Ship ship && (ThirdPerson || ship.Flight.Stats?.IsHover == true) ? Chase(ship)
            : ThirdPerson ? Behind(Eye(p)) : Eye(p);
        Backdrop.Follow(!World.Physics.Zone.InAtmosphere(Camera.GlobalPosition.ToNumerics()));
        N.Vector3 eye = Camera.GlobalPosition.ToNumerics();
        float surface = float.NegativeInfinity;
        bool under = World.Physics.Water?.SurfaceAt(eye, out surface) == true && eye.Y < surface;
        Backdrop.Submerge(under, under ? surface - eye.Y : 0f);
    }

    /// <summary>
    /// The player's eye, from interpolated transforms: their cabin's frame times the cabin-local
    /// eye while aboard. It stands on the eased view frame, so the camera rolls smoothly when mag
    /// boots carry the player round a hull edge or through an airlock.
    /// </summary>
    public Transform3D Eye(Player p)
    {
        var look = new Basis(Vector3.Up, p.Yaw) * new Basis(Vector3.Right, p.Pitch);
        if (p.Seated is Ship seat)
            return seat.Node.GetGlobalTransformInterpolated() * new Transform3D(look, (seat.Seat + N.Vector3.UnitY * 0.95f).ToGodot());
        Vector3 feet = p.Node.GetGlobalTransformInterpolated().Origin;
        Basis frame = p.ViewFrame.ToBasis();
        var local = new Transform3D(frame * look, feet + frame.Y * Player.EyeHeight);
        Ship aboard = p.Aboard;
        return aboard == null ? local : aboard.Node.GetGlobalTransformInterpolated() * local;
    }

    static Transform3D Behind(Transform3D eye) => eye.TranslatedLocal(new Vector3(0f, 0.6f, 4.5f));

    /// <summary>A chase camera behind and above a vessel, sized to it.</summary>
    public static Transform3D Chase(Ship ship, float distance = 1.3f, float height = 0.45f)
    {
        Transform3D t = ship.Node.GetGlobalTransformInterpolated();
        var (min, max) = ship.Structure.Bounds();
        float size = (max - min).ToVector3().Length() * ship.Structure.Grid.CellSize;
        Vector3 target = t.Origin + Vector3.Up * size * 0.15f;
        Vector3 back = t.Basis.Z;
        back.Y = 0f;
        back = back.LengthSquared() < 1e-4f ? Vector3.Back : back.Normalized();
        Vector3 eye = target + back * size * distance + Vector3.Up * size * height;
        return new Transform3D(Basis.LookingAt(target - eye, Vector3.Up), eye);
    }
}
