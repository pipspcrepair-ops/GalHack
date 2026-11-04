using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using GalHack.Structures.Presentation;
using Godot;

namespace BlockBuilding;

/// <summary>
/// The creative build scene, built in code: a landing pad in the desert, the hand-written
/// example ship loaded from its blueprint, a first-person builder and the build HUD.
/// </summary>
public partial class Main : Node3D
{
    public Catalogue Catalogue { get; private set; }
    public readonly List<StructureView> Structures = new();
    public Player Player { get; private set; }
    public BuildController Build { get; private set; }
    public Hud Hud { get; private set; }
    public StructureView Ship { get; private set; }

    public override void _Ready()
    {
        Catalogue = GameData.Catalogue;
        World.Build(this);

        var effects = new Effects { Name = "Effects" };
        var sfx = new Sfx { Name = "Sfx" };
        AddChild(effects);
        AddChild(sfx);

        Structure ship = Blueprint.Load(GameData.ReadText(GameData.ExampleBlueprint), Catalogue, out List<string> errors);
        if (ship == null) GD.PrintErr($"example blueprint rejected:\n  {string.Join("\n  ", errors)}");
        else Ship = AddStructure(ship, PadFrame(ship));

        Player = new Player { Name = "Player", IsLadderAt = IsLadderAt };
        AddChild(Player);
        Player.DebugPlace(new Vector3(-17f, World.PadTop, 26f), Mathf.DegToRad(-35f), -0.08f);
        Input.MouseMode = Input.MouseModeEnum.Captured;

        Hud = new Hud { Name = "Hud" };
        Build = new BuildController { Name = "Build", Main = this, Player = Player, Sfx = sfx, Effects = effects, Hud = Hud };
        Hud.Build = Build;
        AddChild(Build);
        AddChild(Hud);
        if (Ship != null) Build.SetActive(Ship);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.F1 }) Hud.ToggleHelp();
        Player.HandleInput(e);
        Build.HandleInput(e);
    }

    // ---- Structures -----------------------------------------------------------------------

    public StructureView AddStructure(Structure s, Transform3D frame)
    {
        StructureView view = StructureView.Create(s);
        AddChild(view);
        view.GlobalTransform = frame;
        view.ResetPhysicsInterpolation();
        Structures.Add(view);
        return view;
    }

    /// <summary>A section that broke off: same place, its own body, free to fall.</summary>
    public StructureView AddWreck(Structure wreck, StructureView from, Vector3 push, Vector3 spin = default)
    {
        StructureView view = AddStructure(wreck, from.GlobalTransform);
        view.LinearVelocity = from.LinearVelocity + push;
        view.AngularVelocity = from.AngularVelocity + spin + new Vector3((float)GD.RandRange(-0.2, 0.2), 0, (float)GD.RandRange(-0.2, 0.2));
        return view;
    }

    public void RemoveStructure(StructureView view)
    {
        Structures.Remove(view);
        if (Build?.Active == view) Build.SetActive(null);
        view.QueueFree();
    }

    /// <summary>Where a structure sits on the pad: centred, its lowest blocks resting on the concrete.</summary>
    public Transform3D PadFrame(Structure s)
    {
        var (min, _) = s.Bounds();
        float size = s.Grid.CellSize;
        return new Transform3D(Basis.Identity, new Vector3(0, World.PadTop - (min.Y - 0.5f) * size, 0));
    }

    bool IsLadderAt(Vector3 world)
    {
        foreach (StructureView view in Structures)
        {
            if (!IsInstanceValid(view)) continue;
            Int3 cell = view.LocalToCell(view.GlobalTransform.AffineInverse() * world);
            if (view.Structure.TryGet(cell, out Block b) && view.Structure.TypeOf(b).IsLadder) return true;
        }
        return false;
    }

    public StructureView ViewOf(Structure s) => Structures.FirstOrDefault(v => v.Structure == s);
}
