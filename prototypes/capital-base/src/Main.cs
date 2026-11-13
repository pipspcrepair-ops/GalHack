using System;
using System.IO;
using System.Linq;
using System.Text;
using CapitalBase.Core;
using CapitalBase.Records;
using CapitalBase.World;
using GalHack.Persistence;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using ShipHandling.Handling;
using Weather.Model;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;

namespace CapitalBase;

/// <summary>
/// The playable scene: Bastion 7 coming in over a desert world with Prospector 3 on its pad and
/// Outpost 2 nearby. Fly it, land it, switch its core to base mode, build onto it and into the
/// ground, dig round it, send the Prospector out farming with auto-transmit on, switch back and
/// lift off. The world is saved under .saves/play and continued next time (--new starts again,
/// keeping the old save beside it).
/// </summary>
public partial class Main : Node3D
{
    public CapitalWorld World { get; private set; }
    public Scenery Scenery { get; private set; }
    public Hud Hud { get; private set; }
    public Camera3D Camera { get; private set; }
    public SaveGame Save { get; private set; }

    /// <summary>False in screenshots: no input, no save, the camera placed by the caller.</summary>
    public bool Interactive = true;

    /// <summary>Screenshots place the camera themselves.</summary>
    public Transform3D? FixedCamera;

    Craft _selected;
    float _yaw = 0.6f, _pitch = -0.32f, _distance = 75f;
    bool _orbiting, _autoland;
    int _material;
    double _planTimer;
    string _ground = "";
    static readonly string[] Materials = { "steel", "concrete", "container", "glass", "light" };

    public override void _Ready()
    {
        ProcessPhysicsPriority = -30; // the pilot's hands before the physics host (-10)
        World = new CapitalWorld { Name = "World" };
        AddChild(World);
        Scenery = new Scenery { Name = "Scenery" };
        AddChild(Scenery);
        Scenery.Setup(World.Planet);
        // Placed every frame from interpolated transforms, so it needs no interpolation of its own.
        Camera = new Camera3D { Name = "Camera", Fov = 62f, Near = 0.15f, Far = 6000f, PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off };
        AddChild(Camera);
        Camera.MakeCurrent();
        Hud = new Hud { Name = "Hud", Visible = Interactive };
        AddChild(Hud);
        if (Interactive) StartOrContinue();
    }

    /// <summary>The opening: Bastion 7 high over the first landing spot, Prospector 3 on its pad, Outpost 2 set down nearby.</summary>
    public void Populate()
    {
        Spot a = World.Spots[0];
        World.SpawnBase("outpost", "outpost.gbp", World.Spots[3].Flat, 0.6f);
        World.SpawnFlying("bastion", "bastion.gbp", new N.Vector3(a.X - 60f, a.Ground + 55f, a.Z + 40f), 0f);
        World.Spawn("prospector", "prospector.gbp", N.Vector3.Zero, N.Quaternion.Identity);
        World.DockAt(World.CraftById("prospector"), World.CraftById("bastion"), new N.Vector3(0f, 7f, 6f), 0f);
        World.FlagHome(World.CraftById("bastion"));
        World.SetAutoTransmit(World.CraftById("prospector"), true);
        _selected = World.CraftById("bastion");
    }

    void StartOrContinue()
    {
        string folder = Paths.Own(".saves", "play");
        if (OS.GetCmdlineUserArgs().Contains("--new") && Directory.Exists(folder))
            Directory.Move(folder, folder + $"-{DateTime.Now:yyyyMMdd-HHmmss}"); // kept beside it, never deleted
        Save = new SaveGame(folder);
        Save.Attach(World);
        if (Save.Table.Count > 0)
        {
            var result = Save.Load();
            World.Say(result.Ok ? $"Continued: {result.Summary()}" : result.Summary());
            World.Patch.RebuildAllNow();
        }
        if (World.Crafts.Count == 0) Populate();
        _selected = World.CraftById("bastion") ?? World.Crafts.FirstOrDefault();
    }

    // ==== Every tick: the pilot's hands =============================================================

    public override void _PhysicsProcess(double delta)
    {
        if (!Interactive) return;
        foreach (Craft c in World.Crafts)
        {
            if (World.IsLiftingOff(c)) continue;
            c.Flight.Input = c == _selected && !c.IsBase ? Hands(c) : PilotInput.LetGo;
        }
    }

    PilotInput Hands(Craft c)
    {
        if (_autoland)
        {
            if (c.State != CraftState.Flying) _autoland = false;
            else return Pilot.Land(c, new N.Vector2(c.Body.Position.X, c.Body.Position.Z), 3f);
        }
        float Axis(Key minus, Key plus) => (Input.IsPhysicalKeyPressed(plus) ? 1f : 0f) - (Input.IsPhysicalKeyPressed(minus) ? 1f : 0f);
        var move = new Vector3(Axis(Key.A, Key.D), Axis(Key.Ctrl, Key.Space), Axis(Key.W, Key.S));
        var turn = new Vector3(Axis(Key.Down, Key.Up), Axis(Key.Right, Key.Left), Axis(Key.E, Key.Q));
        if (move == Vector3.Zero && turn == Vector3.Zero) return PilotInput.LetGo;
        return PilotInput.Hands(move, turn, Input.IsPhysicalKeyPressed(Key.Shift));
    }

    // ==== Every frame: camera and console =================================================================

    public override void _Process(double delta)
    {
        if (_selected == null || !IsInstanceValid(_selected.Hull)) _selected = World.CraftById("bastion") ?? World.Crafts.FirstOrDefault();
        if (_selected == null) return;
        if (FixedCamera is Transform3D fixedAt) Camera.GlobalTransform = fixedAt;
        else
        {
            Vector3 target = _selected.Hull.GetGlobalTransformInterpolated().Origin + Vector3.Up * 4f;
            var offset = new Vector3(Mathf.Sin(_yaw) * Mathf.Cos(_pitch), -Mathf.Sin(_pitch), Mathf.Cos(_yaw) * Mathf.Cos(_pitch)) * _distance;
            Camera.GlobalPosition = target + offset;
            Camera.LookAt(target, Vector3.Up);
        }
        if (!Hud.Visible) return;
        _planTimer -= delta;
        if (_planTimer <= 0)
        {
            _planTimer = 0.5;
            _ground = GroundLine(ConsoleCraft);
        }
        UpdateHud();
    }

    Craft ConsoleCraft => _selected ?? World.HomeCraft ?? World.CraftById("bastion");

    string GroundLine(Craft c)
    {
        if (c == null || c.State == CraftState.Flying) return "";
        CutPlan plan = World.PlanCut(c);
        return plan.Cells.Count == 0 ? "GROUND  standing clear: nothing to cut" : $"GROUND  keyed in: {plan.Cells.Count} m³ hold it; lift-off cuts it free";
    }

    void UpdateHud()
    {
        var console = new StringBuilder();
        Craft c = ConsoleCraft;
        if (c != null)
        {
            StructureStats s = c.Stats;
            RoomReport rooms = Rooms.Detect(c.Structure);
            var (margin, _, _, carried) = World.LiftMargin(c);
            console.Append($"CORE CONSOLE  {c.Name}{(World.Home.IsHome(c.Id) ? "   [HOME]" : "")}").Append('\n');
            console.Append($"MODE  {c.Structure.Mode.ToString().ToUpperInvariant()}     STATE  {c.State}{(World.IsLiftingOff(c) ? $" (lift-off: {World.LiftOffOf(c).Phase})" : "")}").Append('\n');
            console.Append(c.IsBase
                ? $"MASS  {c.Tonnes:F0} t     LIFT  none: a base doesn't fly"
                : $"MASS  {c.Tonnes:F0} t{(carried > 0 ? $" + {carried:F1} t parked" : "")}     LIFT  {margin:+0;-0} kN ({margin / World.Physics.Zone.Strength:+0;-0} t spare)").Append('\n');
            console.Append($"POWER  {s.PowerOut:F1} / {s.PowerUse:F1} MW     STORAGE  {c.Cargo.UsedSu(World.Rules.Catalog):F0} / {s.StorageCapacity:F0} SU").Append('\n');
            console.Append($"LIFE SUPPORT  {(s.LifeSupport ? "on" : "off")}, {rooms.Sealed.Count()} sealed rooms, core gravity {(s.GravityActive ? "on" : "off")}").Append('\n');
            if (_ground.Length > 0) console.Append(_ground).Append('\n');
            console.Append($"HOLDS  {c.Cargo}");
        }

        var vessel = new StringBuilder();
        Craft v = World.CraftById("prospector");
        if (v != null)
        {
            vessel.Append($"{v.Name.ToUpperInvariant()}   {v.State}{(v.Pin is { Docked: true } p ? $" on {World.CraftOf(p.Host)?.Name}" : "")}").Append('\n');
            vessel.Append($"CARGO  {v.Cargo.UsedSu(World.Rules.Catalog):F0} / {v.StorageCapacity:F0} SU   {v.Cargo}").Append('\n');
            vessel.Append(World.LinkText(v));
        }

        string log = string.Join("\n", World.Log.Skip(Math.Max(0, World.Log.Count - 6)));
        string help =
            $"Tab  select ({_selected?.Name})   WASD Space Ctrl  fly   arrows Q E  turn   Shift  boost\n" +
            "G  land here   B  core: base / flight   L  lift off   H  flag Home\n" +
            "X  drill (Prospector)   T  auto-transmit   K  weather\n" +
            $"left click  build {Materials[_material]} (1-5)   Shift+click  remove   middle click  dig\n" +
            $"M  game mode: {(World.Inventory.Creative ? "creative" : "survival")}   F5 / F9  save / load   right drag, wheel  camera   F1  help";
        Hud.Set(console.ToString(), vessel.ToString(), log, help);
    }

    // ==== Keys and mouse ==================================================================================

    public override void _UnhandledInput(InputEvent e)
    {
        if (!Interactive) return;
        if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Right) _orbiting = mb.Pressed;
            else if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) _distance = Mathf.Max(12f, _distance * 0.9f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) _distance = Mathf.Min(400f, _distance * 1.1f);
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed) Aim(mb.Position, mb.ShiftPressed ? "remove" : "build");
            else if (mb.ButtonIndex == MouseButton.Middle && mb.Pressed) Aim(mb.Position, "dig");
            return;
        }
        if (e is InputEventMouseMotion motion && _orbiting)
        {
            _yaw -= motion.Relative.X * 0.005f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * 0.005f, -1.4f, 0.4f);
            return;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        Craft c = _selected;
        switch (key.PhysicalKeycode)
        {
            case Key.Tab:
                int i = World.Crafts.IndexOf(c);
                _selected = World.Crafts[(i + 1) % World.Crafts.Count];
                _autoland = false;
                break;
            case Key.G: _autoland = c != null && c.State == CraftState.Flying; break;
            case Key.B when c != null: World.SwitchMode(c, c.IsBase ? CoreMode.Flight : CoreMode.Base); break;
            case Key.L when c != null: World.LiftOffFrom(c); break;
            case Key.H when c != null: World.FlagHome(c); break;
            case Key.T:
                if (World.CraftById("prospector") is Craft v) World.SetAutoTransmit(v, !v.AutoTransmit);
                break;
            case Key.X:
                if (World.CraftById("prospector") is Craft d)
                {
                    CapitalWorld.DrillResult r = World.Drill(d);
                    World.Say(r.Refusal ?? $"Drilled {r.Dug}: {r.ToHome} straight to Home, {r.Aboard} aboard.");
                }
                break;
            case Key.K:
                World.Weather.Calm = !World.Weather.Calm;
                World.Weather.Sim.Snap(World.Weather.Calm ? WeatherKind.Clear : WeatherKind.Hurricane);
                World.Say(World.Weather.Calm ? "Weather: calm." : "Weather: hurricane.");
                break;
            case Key.M:
                World.Inventory.Creative = !World.Inventory.Creative;
                World.Say($"Game mode: {(World.Inventory.Creative ? "creative (building is free)" : "survival (building pays materials)")}.");
                break;
            case >= Key.Key1 and <= Key.Key5: _material = (int)(key.PhysicalKeycode - Key.Key1); break;
            case Key.F5: SaveNow(); break;
            case Key.F9: LoadNow(); break;
            case Key.F1: Hud.ShowHelp = !Hud.ShowHelp; break;
        }
    }

    void SaveNow()
    {
        try
        {
            World.Say($"Saved: {Save.Save()}");
        }
        catch (SaveRefusedException ex)
        {
            World.Say(ex.Message);
        }
    }

    void LoadNow()
    {
        var r = Save.Load();
        World.Patch.RebuildAllNow();
        _selected = World.CraftById("bastion") ?? World.Crafts.FirstOrDefault();
        World.Say(r.Summary());
    }

    /// <summary>Builds onto, takes off, or digs at whatever is under the mouse.</summary>
    void Aim(Vector2 screen, string action)
    {
        Vector3 from = Camera.ProjectRayOrigin(screen), to = from + Camera.ProjectRayNormal(screen) * 400f;
        var query = PhysicsRayQueryParameters3D.Create(from, to, Layers.World | Layers.Hull);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return;
        N.Vector3 point = hit["position"].AsVector3().ToNumerics(), normal = hit["normal"].AsVector3().ToNumerics();
        Body body = World.Host.BodyOf(hit["collider"].AsGodotObject());
        Craft craft = body == null ? null : World.CraftOf(body);
        if (action == "dig")
        {
            var edit = World.Dig(point - normal * 0.5f, 2f);
            World.Say(edit.Removed.Count > 0 ? $"Dug {edit.Removed.Count} cells into your pack." : $"Nothing dug ({edit.Headline}).");
            return;
        }
        if (craft == null) return;
        float cell = craft.Structure.Grid.CellSize;
        if (action == "remove")
        {
            World.RemoveBlock(craft, StructureBridge.CellAt(craft.Structure, craft.Body.ToLocal(point - normal * (cell * 0.25f))));
            return;
        }
        SInt3 at = StructureBridge.CellAt(craft.Structure, craft.Body.ToLocal(point + normal * (cell * 0.5f)));
        if (!craft.Structure.Catalogue.TryGetType(Materials[_material], out BlockType type)) return;
        var (error, cleared) = World.PlaceBlock(craft, at, Building.Make(craft.Structure, type, rot: type.IsLight ? LightFacing(craft, at) : Orientations.Identity));
        World.Say(error == PlaceError.None
            ? $"Built a {type.Name.ToLowerInvariant()} onto {craft.Name}{(cleared > 0 ? $", clearing {cleared} cells of ground" : "")}."
            : $"Can't build there: {error}.");
    }

    /// <summary>A light faces away from the block it is fixed to (it attaches by its back, +z).</summary>
    static int LightFacing(Craft craft, SInt3 at)
    {
        foreach (Dir d in Dirs.All)
            if (craft.Structure.Has(at + d.Vec()))
                for (int o = 0; o < Orientations.Count; o++)
                    if (Orientations.ToWorld(o, Dir.PosZ) == d) return o;
        return Orientations.Identity;
    }
}
