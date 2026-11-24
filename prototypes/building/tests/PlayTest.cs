using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// Headless smoke test of the playable scene: drives the real input paths (keys and clicks into
/// the build controller, with the crosshair aimed by moving the camera) and checks the structure
/// changed as a player would expect. Run with:
///   godot --headless --path prototypes/building res://tests/PlayTest.tscn
/// Exits 0 if every check passes, 1 if any fails.
/// </summary>
public partial class PlayTest : Node
{
    Main _main;
    int _tick, _step, _failures, _passes, _wait;
    int _shipCount;
    readonly List<(string name, Action act)> _steps = new();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _main = new Main();
        AddChild(_main);
        BuildSteps();
    }

    // Physics ticks let the space see new colliders; process frames let the HUD, ghost and
    // overlay (which update per frame) catch up. Wait for both between steps.
    public override void _Process(double delta) => _frames++;

    int _frames, _framesAtStep;

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick < 6 || --_wait > 0 || _frames < _framesAtStep + 2) return;
        _framesAtStep = _frames;
        if (_step >= _steps.Count)
        {
            GD.Print($"\n{_passes} passed, {_failures} failed");
            GetTree().Quit(_failures == 0 ? 0 : 1);
            return;
        }
        var (name, act) = _steps[_step++];
        try { act(); }
        catch (Exception e) { Check(false, $"{name} threw {e.Message}"); }
        _wait = 3; // let the physics space see new colliders and the aim update
    }

    StructureView Ship => _main.Ship;
    BuildController Build => _main.Build;
    Structure S => Ship.Structure;
    Vector3 Top(int x, int y, int z) => Ship.CellToGlobal(new Int3(x, y, z)) + Vector3.Up * (Ship.CellSize * 0.5f - 0.02f);

    void BuildSteps()
    {
        _steps.Add(("aim at the roof", () =>
        {
            Build.RequireMouseCapture = false;
            _shipCount = S.Count;
            Aim(new Vector3(9f, 16f, -1f), Top(3, 3, -1));
        }));
        _steps.Add(("place a block", () =>
        {
            Check(Build.Aim.OnBlock && Build.Aim.Cell == new Int3(3, 3, -1) && Build.Aim.Face == Dir.PosY,
                $"the crosshair finds the roof cell and its top face ({Build.Aim.Cell} {Build.Aim.Face})");
            Click(MouseButton.Left);
            Check(S.Has(new Int3(3, 4, -1)) && S.Count == _shipCount + 1, "left click places a steel block on the face");
            Key(Godot.Key.X);
            Aim(new Vector3(9f, 16f, 1f), Top(3, 3, 1));
        }));
        _steps.Add(("place with the mirror", () =>
        {
            Click(MouseButton.Left);
            Check(S.Has(new Int3(3, 4, 1)) && S.Has(new Int3(-3, 4, 1)), "with the X mirror on, one click places both sides");
            Aim(new Vector3(9f, 16f, 1f), Top(3, 4, 1));
        }));
        _steps.Add(("remove with the mirror", () =>
        {
            int plates = Build.Inventory["iron-plate"];
            Click(MouseButton.Right);
            Check(!S.Has(new Int3(3, 4, 1)) && !S.Has(new Int3(-3, 4, 1)) && Build.Inventory["iron-plate"] == plates + 8,
                "right click removes both mirrored blocks and returns their 8 iron plates");
            Key(Godot.Key.X);
            Aim(new Vector3(9f, 16f, -1f), Top(3, 4, -1));
        }));
        _steps.Add(("upgrade and paint", () =>
        {
            Key(Godot.Key.U);
            Check(S.TypeOf(S.Get(new Int3(3, 4, -1))).Id == "hardened-plated", "U upgrades iron plating to hardened in place");
            Key(Godot.Key.P);
            Key(Godot.Key.Key2);
            Click(MouseButton.Left);
            Check(Building.PaintOf(S, new Int3(3, 4, -1), Dir.PosY)?.Base == BuildController.PaintColors[1],
                "the sprayer paints the face under the crosshair");
            Key(Godot.Key.P);
            Key(Godot.Key.O);
        }));
        _steps.Add(("visor", () =>
        {
            Check(Build.OverlayOn && Build.Rooms != null && Build.Rooms.Sealed.Count() == 1,
                $"O shows the air overlay with the sealed cabin (on {Build.OverlayOn}, report {(Build.Rooms == null ? "none" : $"{Build.Rooms.Sealed.Count()} sealed, {Build.Rooms.Leaking.Count()} leaking")}, active {Build.Active?.Name})");
            Key(Godot.Key.O);
            Key(Godot.Key.B);
            Check(S.Mode == CoreMode.Base, "B switches the core to base mode");
            Key(Godot.Key.B);
            Key(Godot.Key.B); // through Hover back to Flight
            Aim(Top(5, 1, 6) + new Vector3(0.6f, 12f, 0f), Top(5, 1, 6)); // straight down onto the starboard pylon
        }));
        _steps.Add(("blaster", () =>
        {
            int before = _main.Structures.Count, blocks = S.Count;
            Key(Godot.Key.K);
            var wreck = _main.Structures.FirstOrDefault(v => v != Ship && v.Structure.IsWreck);
            // The Lancer is an old large-grid blueprint on 1 m cells: its nacelle is thin enough now that the blast may cut it in two.
            Check(_main.Structures.Count > before && wreck != null, $"K blasts the pylon and the nacelle breaks away as a wreck ({_main.Structures.Count - before} pieces)");
            Check(wreck != null && !wreck.Freeze && S.Count + wreck.Structure.Count < blocks,
                $"the wreck is a free body ({wreck?.Structure.Count} blocks) and the ship lost its pylon");
            Key(Godot.Key.Key9);
            Aim(new Vector3(-20f, 6f, 30f), new Vector3(-24f, World.PadTop, 30f));
        }));
        _steps.Add(("starter core", () =>
        {
            int before = _main.Structures.Count;
            Click(MouseButton.Left);
            var vessel = _main.Structures.LastOrDefault();
            Check(_main.Structures.Count == before + 1 && vessel?.Structure.Core != null,
                "a core on the pad starts a new structure");
            Build.SetActive(Ship);
            Key(Godot.Key.F5);
            Check(FileAccess.FileExists("res://saves/quicksave.gbp"), "F5 saves the blueprint");
            int count = S.Count;
            Key(Godot.Key.F9);
            Check(Build.Active != Ship && Build.Active.Structure.Count == count, "F9 loads it back in place, every block there");
        }));
    }

    void Aim(Vector3 eye, Vector3 target)
    {
        Player p = _main.Player;
        p.DebugPlace(eye - Vector3.Up * 1.62f, 0, 0, fly: true);
        p.DebugLookAt(target);
    }

    void Click(MouseButton button)
    {
        Build.DebugForceAim();
        Build.HandleInput(new InputEventMouseButton { ButtonIndex = button, Pressed = true });
    }

    void Key(Key key)
    {
        Build.DebugForceAim();
        Build.HandleInput(new InputEventKey { PhysicalKeycode = key, Pressed = true });
    }

    void Check(bool ok, string what)
    {
        if (ok) { _passes++; GD.Print($"PASS  {what}"); }
        else { _failures++; GD.PrintErr($"FAIL  {what}"); }
    }
}
