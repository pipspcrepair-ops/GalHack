using System.Collections.Generic;
using System.Linq;
using DiegeticUi.Screens;
using DiegeticUi.Theme;
using GalHack.Items.Production;
using GalHack.Physics;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;

namespace Slice.Ui;

/// <summary>
/// In-world screens on the things that make things: one on every placed survival constructor,
/// one on every constructor block of a base, and one on a base's core with its settings. Each
/// hosts the very panel the Tab menu's CORE tab shows (<see cref="ConstructorSection"/>,
/// <see cref="StructureSection"/>), hung at standing eye height on a free face (diegetic-ui's
/// <see cref="ScreenMount"/>), and used by looking at it: a cursor sits where you aim
/// (<see cref="ScreenPointer"/>) and the left button clicks there instead of firing the tool.
/// The Tab menu stays the main way in; these are the same controls where the machine stands.
/// </summary>
public partial class MachineScreens : Node3D
{
    public Game.Game Game;

    /// <summary>The physics layer the screens' pick slabs are on: one of its own (the diegetic default is the slice's hull layer).</summary>
    public const uint PickLayer = 1u << 9;

    public ScreenPointer Pointer { get; private set; }

    /// <summary>The screens up now, by what they belong to.</summary>
    public IReadOnlyDictionary<(object owner, Int3 cell), Mounted> Screens => _screens;

    readonly Dictionary<(object owner, Int3 cell), Mounted> _screens = new();
    double _sync;
    bool _down;

    /// <summary>A screen and the panel on it.</summary>
    public sealed class Mounted
    {
        public WorldScreen Screen;
        public SectionPanel Panel;
        public Node3D Holder;
    }

    // Portrait, the constructor panel's shape; about half a metre wide.
    static readonly Vector2 Size = new(0.52f, 0.74f);
    static readonly Vector2I Pixels = new(624, 888);

    public override void _Ready()
    {
        Pointer = new ScreenPointer { Name = "Pointer", Camera = Game.Camera, Reach = 3f, PickMask = PickLayer, BlockMask = Layers.World | Layers.Hull };
        AddChild(Pointer);
    }

    public override void _Process(double delta)
    {
        // The pointer has the mouse only on foot with the menu shut (the menu, a seat, death take it).
        Pointer.Enabled = Game.Survivor != null && Game.Survivor.Seated == null && !Game.Menu.IsOpen && !Game.Suit.Dead;
        _sync -= delta;
        if (_sync > 0) return;
        _sync = 0.5;
        Sync();
    }

    /// <summary>Puts up a screen on whatever has newly appeared and takes down those whose machine has gone.</summary>
    public void Sync()
    {
        if (Game.Zone == null) return;
        var want = new HashSet<(object, Int3)>();
        foreach (Device d in Game.Zone.Devices)
        {
            if (d.Constructor == null) continue;
            var key = ((object)d, Int3.Zero);
            want.Add(key);
            if (!_screens.ContainsKey(key)) _screens[key] = OnDevice(d);
        }
        foreach (Vessel v in Game.Zone.Vessels)
        {
            if (!v.IsBase || v.IsWreck || v.Structure.Core is not Int3 core) continue;
            var coreKey = ((object)v, core);
            want.Add(coreKey);
            if (!_screens.ContainsKey(coreKey) && OnBlock(v, core, new StructureSection(Game, () => v, modeSwitch: false), () => StructureState(v)) is Mounted m) _screens[coreKey] = m;
            foreach (var (cell, machine) in v.Machines)
            {
                var key = ((object)v, cell);
                want.Add(key);
                Constructor c = machine;
                if (!_screens.ContainsKey(key) && OnBlock(v, cell, new ConstructorSection(Game, () => c, c.Def.Name.ToUpperInvariant(), "screen"), () => MachineState(c)) is Mounted mm)
                    _screens[key] = mm;
            }
        }
        foreach (var key in _screens.Keys.Where(k => !want.Contains(k)).ToList())
        {
            if (IsInstanceValid(_screens[key].Holder)) _screens[key].Holder.QueueFree();
            _screens.Remove(key);
        }
    }

    /// <summary>
    /// The buttons while your aim is on a screen: they click it, and nothing else fires. True
    /// while the cursor is on one (the game then leaves the tool and the build tool alone).
    /// </summary>
    public bool Use(Controls c)
    {
        if (Pointer.Target == null)
        {
            _down = false;
            return false;
        }
        bool shift = c.ModifierHeld(Key.Shift);
        if (c.PrimaryPressed && !_down)
        {
            _down = true;
            Pointer.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true, ButtonMask = MouseButtonMask.Left, ShiftPressed = shift });
        }
        else if (_down && (c.PrimaryReleased || !c.Primary))
        {
            _down = false;
            Pointer.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false, ShiftPressed = shift });
        }
        if (c.SecondaryPressed)
            Pointer.HandleInput(new InputEventMouseButton { ButtonIndex = MouseButton.Right, Pressed = true, ButtonMask = MouseButtonMask.Right });
        return true;
    }

    /// <summary>Where a widget on a screen's panel is, in the world (for aiming at it: a test, a hint).</summary>
    public static Vector3? WidgetPoint(Mounted m, string id)
    {
        if (m.Panel.RectOf(id) is not Rect2 r) return null;
        Vector2 pixel = m.Panel.Fit * r.GetCenter();
        return m.Screen.PixelToWorld(pixel);
    }

    // ---- Hanging them ---------------------------------------------------------------------------

    /// <summary>A survival constructor's screen: on a little mast at the front of its top, at eye height.</summary>
    Mounted OnDevice(Device d)
    {
        var holder = new Node3D { Name = "Screen" };
        d.AddChild(holder);
        // In the device's own frame: its floor is y 0, its front faces -z.
        Transform3D local = ScreenMount.OnFace(new Vector3(0f, 0f, -0.3f), Vector3.Forward, 0f, tipBackDegrees: 8f, rise: 0.04f);
        holder.Transform = local;
        var mast = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.7f };
        holder.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.5f, 0.05f) }, Position = new Vector3(0f, -Size.Y * 0.5f - 0.1f, -0.06f), MaterialOverride = mast });
        return Hang(holder, new ConstructorSection(Game, () => d.Constructor, d.Label.ToUpperInvariant(), "screen"), () => MachineState(d.Constructor));
    }

    /// <summary>
    /// Where a base block's screen goes: a free side that is not the machine's own front (its work bay, gantry and
    /// nozzle are on the front), hung far enough off the face to clear every fitting the block's model has on that
    /// side. A machine closed in on its other sides takes the front, standing off past the gantry. Null: no free side.
    /// </summary>
    public static (Dir Face, float StandOff)? BlockScreenFace(Structure s, Int3 cell)
    {
        Block b = s.Get(cell);
        var (lo, hi) = Footprint.Box(s.TypeOf(b), b.Rot);
        // A side is free when nothing stands against any cell of it (a block spanning several cells: its whole side).
        bool Free(Dir d)
        {
            int axis = d.Axis();
            Int3 from = cell + lo, to = cell + hi;
            int at = d.Sign() > 0 ? to[axis] + 1 : from[axis] - 1;
            from = from.With(axis, at);
            to = to.With(axis, at);
            for (int z = from.Z; z <= to.Z; z++)
                for (int y = from.Y; y <= to.Y; y++)
                    for (int x = from.X; x <= to.X; x++)
                        if (s.Has(new Int3(x, y, z))) return false;
            return true;
        }
        Dir front = Orientations.ToWorld(b.Rot, Dir.NegZ);
        Dir? pick = null;
        var order = new[] { Dir.NegZ, Dir.PosX, Dir.NegX, Dir.PosZ };
        foreach (Dir d in order)
            if (d != front && Free(d)) { pick = d; break; }
        if (pick == null && System.Array.IndexOf(order, front) >= 0 && Free(front)) pick = front;
        if (pick is not Dir face) return null;
        return (face, Protrusion(s, b, face) + 0.03f);
    }

    /// <summary>How far, in metres, the block's model stands out from the block's face on this world side (its gantry, nozzle, status screen, vents).</summary>
    public static float Protrusion(Structure s, in Block b, Dir worldFace)
    {
        BlockType t = s.TypeOf(b);
        Dir local = Orientations.ToLocal(b.Rot, worldFace);
        Int3 c = local.Vec();
        float most = 0f;
        foreach (Part p in t.Detail)
        {
            float e = c.X > 0 ? p.Max.X : c.X < 0 ? -p.Min.X : c.Y > 0 ? p.Max.Y : c.Y < 0 ? -p.Min.Y : c.Z > 0 ? p.Max.Z : -p.Min.Z;
            most = System.MathF.Max(most, e - 0.5f);
        }
        // Parts are in the -0.5..0.5 box that stands for the whole block: scaled by its size along that side.
        return most * t.Size[local.Axis()] * s.Grid.CellSize;
    }

    /// <summary>A block's screen in its structure's frame: centre at eye height over the block's floor, straight out of the face, <paramref name="standOff"/> off it.</summary>
    public static Transform3D BlockScreenLocal(Structure s, Int3 cell, Dir face, float standOff)
    {
        float size = s.Grid.CellSize;
        Block b = s.Get(cell);
        N.Vector3 centre = StructureBridge.BlockCenter(s, cell, b);
        N.Vector3 half = Footprint.WorldSize(s.TypeOf(b), b.Rot).ToVector3() * size * 0.5f;
        N.Vector3 n = face.Vec().ToVector3();
        var facePoint = new Vector3(centre.X + n.X * half.X, 0f, centre.Z + n.Z * half.Z);
        return ScreenMount.OnFace(facePoint, new Vector3(n.X, 0f, n.Z), centre.Y - half.Y, rise: -0.1f, standOff: standOff);
    }

    /// <summary>A screen on a free side of a base's block, at eye height over the ground beside it.</summary>
    Mounted OnBlock(Vessel v, Int3 cell, IMenuSection section, System.Func<string> state)
    {
        if (BlockScreenFace(v.Structure, cell) is not var (face, standOff)) return null;
        var holder = new Node3D { Name = $"Screen{cell.X}_{cell.Y}_{cell.Z}" };
        v.Node.AddChild(holder); // in the base's own frame (its node carries it)
        holder.Transform = BlockScreenLocal(v.Structure, cell, face, standOff);
        return Hang(holder, section, state);
    }

    Mounted Hang(Node3D holder, IMenuSection section, System.Func<string> state)
    {
        var screen = new WorldScreen { Name = "Glass", Pixels = Pixels, SizeMeters = Size, PickLayer = PickLayer, MovesCamera = false, Bezel = 0.025f, Prompt = "LOOK TO USE" };
        holder.AddChild(screen);
        var panel = new SectionPanel { Section = section, State = state };
        screen.SetContent(panel);
        return new Mounted { Screen = screen, Panel = panel, Holder = holder };
    }

    // What makes each panel look different (it redraws only then): the icons too, which render a few a frame.
    string MachineState(Constructor c) =>
        c == null ? "" : $"{c.Status}|{c.Version}|{c.Current?.Item.Id}|{(int)((c.Current?.Progress ?? 0) * 40)}|{c.Storage.Version}|{Game.Progress.Points}|{Game.Progress.State.Unlocked.Count}|{ItemIcons.Shared?.Count}";

    static string StructureState(Vessel v) => $"{v.PowerOn}|{v.LightsOn}|{v.Home}|{v.AutoTransmit}|{v.Powered}|{v.Structure.Count}|{v.Storage.Version}";
}
