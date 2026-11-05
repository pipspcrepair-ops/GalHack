using System;
using System.Linq;
using CapitalBase.World;
using GalHack.Physics;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using ShipHandling.Handling;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace CapitalBase.Tests;

/// <summary>
/// Renders one scripted moment of the capital-base loop to a PNG and quits. Run windowed (not headless):
///   godot --fixed-fps 60 --path prototypes/capital-base res://tests/Screenshot.tscn -- --view=landed --out=shot.png
/// Views: descent (Bastion 7 coming down on its lift jets, gear out), landed (settled on its legs on
/// uneven ground), anchored (a base, built onto and into the ground, dug round, Prospector 3 parked
/// on the pad, the core console showing), farm (Prospector drilling beside the base, sending it
/// Home), liftoff (cut free, the ground crumbling as it rises).
/// </summary>
public partial class Screenshot : Node
{
    Main _main;
    CapitalWorld W => _main.World;
    string _view = "landed", _out = "shot.png";
    int _tick, _captureAt = -1, _phase;
    bool _captured, _airborne;
    Spot _spot;
    N.Vector3 _field;

    Craft Bastion => W.CraftById("bastion");
    Craft Prospector => W.CraftById("prospector");

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = -30; // the pilot's hands before the physics host
        _main = new Main { Interactive = false };
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == 2) Setup();
        if (_tick > 2) Drive();
        if (_tick > 6000) GetTree().Quit(2); // never there: give up rather than hang
        if (_captureAt > 0 && _tick >= _captureAt && W.Patch.FarReady && !_captured)
        {
            _captured = true;
            Capture();
        }
    }

    void Setup()
    {
        _spot = W.Spots[0];
        W.SpawnBase("outpost", "outpost.gbp", W.Spots[3].Flat, 0.6f);
        float height = _view == "descent" ? 30f : 13f;
        W.SpawnFlying("bastion", "bastion.gbp", new N.Vector3(_spot.X, _spot.Ground + height, _spot.Z), 0f);
        W.Spawn("prospector", "prospector.gbp", N.Vector3.Zero, N.Quaternion.Identity);
        W.DockAt(Prospector, Bastion, new N.Vector3(0f, 7f, 6f), 0f);
        W.FlagHome(Bastion);
        W.SetAutoTransmit(Prospector, true);
    }

    /// <summary>Each view's script, one physics tick at a time.</summary>
    void Drive()
    {
        Craft b = Bastion, p = Prospector;
        p.Flight.Input = PilotInput.LetGo;
        if (W.IsLiftingOff(b)) return;
        switch (_phase)
        {
            case 0: // come down and settle on the gear
                b.Flight.Input = Pilot.Land(b, _spot.Flat, 3f);
                if (_view == "descent" && b.Vessel.Gear.FootAltitude < 8f && b.Vessel.Gear.Extension > 0.99f)
                {
                    _captureAt = _tick;
                    _phase = 9;
                }
                if (b.State == CraftState.Landed && !b.Vessel.Settling) _phase = 1;
                break;
            case 1:
                b.Flight.Input = PilotInput.LetGo;
                if (_view == "landed")
                {
                    _captureAt = _tick + 30;
                    _phase = 9;
                    break;
                }
                W.SwitchMode(b, CoreMode.Base);
                BuildOnto(b);
                DigAround(b);
                _phase = 2;
                break;
            case 2:
                if (_view == "anchored")
                {
                    Caption("Bastion 7 as a base. Built onto it in base mode by the player, block by block: a stair from the door\n" +
                            "down into the ground, a foundation pylon under the keel, two containers and a beacon on the roof.");
                    _captureAt = _tick + 40;
                    _phase = 9;
                    break;
                }
                if (_view == "farm")
                {
                    p.Flight.Input = Pilot.Velocity(p, N.Vector3.UnitY * 4f);
                    _field = b.Body.ToParent(new N.Vector3(24f, 0f, -22f));
                    _phase = 3;
                    break;
                }
                W.SwitchMode(b, CoreMode.Capital);
                W.LiftOffFrom(b);
                Caption("Lift-off. The stair and pylon the player built are part of Bastion 7 and lift off with it;\n" +
                        "the ground they reached into is cut free and crumbles.", panels: false);
                _captureAt = _tick + 34;
                _phase = 9;
                break;
            case 3: // the Prospector lifts off the pad, flies out to the field and lands
                float cruise = b.Body.ToParent(new N.Vector3(0f, 7f, 6f)).Y + 8f; // clear of everything on the roof
                _airborne |= p.State == CraftState.Flying && p.Body.Position.Y > cruise - 1f;
                if (!_airborne) p.Flight.Input = Pilot.Velocity(p, N.Vector3.UnitY * 4f);
                else if (p.State == CraftState.Flying)
                {
                    var over = new N.Vector3(_field.X, cruise, _field.Z);
                    p.Flight.Input = MathX.Flat(p.Body.Position - over).Length() > 2f ? Pilot.Toward(p, over, 20f) : Pilot.Land(p, new N.Vector2(_field.X, _field.Z), 3f);
                }
                else
                {
                    for (int i = 0; i < 3; i++) W.Drill(p);
                    Caption("Prospector 3 drilling beside the base. With auto-transmit on, what it digs goes straight into\n" +
                            "Home's storage: Bastion 7, flagged as Home, is in range.");
                    _captureAt = _tick + 60;
                    _phase = 9;
                }
                break;
            default:
                b.Flight.Input = PilotInput.LetGo;
                break;
        }
    }

    /// <summary>Shows the overlay with a caption saying what the shot shows (and the console unless <paramref name="panels"/> is false).</summary>
    void Caption(string text, bool panels = true)
    {
        _main.Hud.Visible = true;
        _main.Hud.ShowHelp = false;
        _main.Hud.ShowPanels = panels;
        _main.Hud.Caption = text;
    }

    /// <summary>
    /// What a player builds onto the base, block by block, as they would by hand: a foundation pylon
    /// into the bump under the hull, a stair of blocks from the door down into the ground, two
    /// containers and a beacon. Nothing here is a feature of the ship; they are ordinary blocks.
    /// </summary>
    void BuildOnto(Craft b)
    {
        Structure s = b.Structure;
        Block hazard = s.Get(new SInt3(-3, 3, -1)), saffron = s.Get(new SInt3(0, 4, -4)), hull = s.Get(new SInt3(-3, 3, -5));
        BlockType Type(string id) => s.Catalogue.TryGetType(id, out BlockType t) ? t : throw new ArgumentException(id);
        Block Painted(string type, Block like, string shape = null, int rot = Orientations.Identity)
        {
            ShapeDef sh = shape == null ? null : s.Catalogue.TryGetShape(shape, out ShapeDef found) ? found : null;
            Block block = Building.Make(s, Type(type), sh, rot);
            block.Paint = like.Paint;
            return block;
        }

        // A pylon down to the ground where it stands highest under the keel.
        SInt3 best = new(0, -1, -2);
        float deepest = float.MinValue;
        foreach (int x in new[] { -3, -1, 0, 1, 3 })
            for (int z = -8; z <= 8; z++)
            {
                if (Enumerable.Range(1, 5).Any(d => s.Has(new SInt3(x, -d, z))) || !s.Has(new SInt3(x, 0, z))) continue;
                N.Vector3 bottom = b.Body.ToParent(new N.Vector3(x * 2f, -7f, z * 2f));
                float depth = W.GroundTop(bottom.X, bottom.Z) - bottom.Y;
                if (depth > deepest) { deepest = depth; best = new SInt3(x, -1, z); }
            }
        int cleared = 0;
        for (int y = -1; y >= -5 && (y >= -3 || cleared == 0); y--) cleared += W.PlaceBlock(b, new SInt3(best.X, y, best.Z), Painted("steel", hazard)).cleared;

        // A stair from the port door down into the ground: the base's front porch. Each tread
        // stands on a column that reaches into the ground below it.
        int[] steps = { 0, -1, -2, -3, -4 };
        for (int i = 0; i < steps.Length; i++)
        {
            W.PlaceBlock(b, new SInt3(-5 - i, steps[i], -5), Painted("steel", hazard));
            for (int y = steps[i] - 1; y >= -4; y--) W.PlaceBlock(b, new SInt3(-5 - i, y, -5), Painted("steel", hull));
        }
        // More storage and a beacon on the roof.
        W.PlaceBlock(b, new SInt3(3, 4, -3), Building.Make(s, Type("container")));
        W.PlaceBlock(b, new SInt3(-3, 4, -3), Building.Make(s, Type("container")));
        W.PlaceBlock(b, new SInt3(3, 5, -3), Painted("steel", saffron, "cylinder"));
    }

    void DigAround(Craft b)
    {
        foreach (N.Vector3 local in new[] { new N.Vector3(16f, 0f, -10f), new N.Vector3(17f, 0f, -6f), new N.Vector3(-4f, 0f, 26f) })
        {
            N.Vector3 at = b.Body.ToParent(local);
            at.Y = W.GroundTop(at.X, at.Z) - 1f;
            W.Dig(at, 2f);
        }
    }

    public override void _Process(double delta)
    {
        if (Bastion == null) return;
        Transform3D t = Bastion.Hull.GetGlobalTransformInterpolated();
        Vector3 eye, at;
        switch (_view)
        {
            case "descent":
                eye = t.Origin + new Vector3(-46f, -6f, -30f);
                at = t.Origin + new Vector3(0f, -3f, 0f);
                break;
            case "landed":
                eye = t.Origin + new Vector3(-30f, -3.5f, -24f);
                at = t.Origin + new Vector3(0f, -4f, 0f);
                break;
            case "anchored":
                eye = t.Origin + new Vector3(-44f, 16f, -26f);
                at = t.Origin + new Vector3(-4f, -2f, 2f);
                break;
            case "farm":
                eye = t.Origin + new Vector3(44f, 9f, -40f);
                at = t.Origin + new Vector3(14f, -5f, -12f);
                break;
            default: // liftoff: low over the stair's foot, where the ground is cut
                eye = t.Origin + new Vector3(-30f, 1f, -24f);
                at = t.Origin + new Vector3(-10f, -7f, -6f);
                break;
        }
        _main.FixedCamera = new Transform3D(Basis.LookingAt(at - eye, Vector3.Up), eye);
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
