using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

/// <summary>
/// Renders one view of the viewer to a PNG and quits. Run windowed (not headless):
///   godot --path prototypes/poi-generator res://tests/Screenshot.tscn -- --view=garrison_active --out=shot.png
/// Views: garrison_active, garrison_infested, garrison_interior, vault_ruined, mine_abandoned,
/// lab_infested, spire_active, aerial, overlay.
/// </summary>
public partial class Screenshot : Node
{
    const int SetupTick = 2;
    const int CaptureTick = 150;

    Main _main;
    int _tick;
    string _view = "garrison_active";
    string _out = "shot.png";

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--view=")) _view = arg["--view=".Length..];
            else if (arg.StartsWith("--out=")) _out = arg["--out=".Length..];
        }
        ProcessPhysicsPriority = 100;
        _main = new Main();
        AddChild(_main);
    }

    public override void _PhysicsProcess(double delta)
    {
        _tick++;
        if (_tick == SetupTick) Setup();
        if (_tick == CaptureTick) Capture();
    }

    void Setup()
    {
        var m = _main;
        // inspect:<family>:<variant>:<rarity>:<state>:<seed>:<yaw>:<pitch>:<distance> frames anything.
        if (_view.StartsWith("inspect:"))
        {
            string[] a = _view.Split(':');
            m.Inspect(a[1], int.Parse(a[2]), System.Enum.Parse<Rarity>(a[3], true), System.Enum.Parse<PoiState>(a[4], true), ulong.Parse(a[5]));
            Orbit(float.Parse(a[6]), float.Parse(a[7]), float.Parse(a[8]), 20f, 14f);
            return;
        }
        switch (_view)
        {
            case "garrison_active":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Active, 5);
                Orbit(32f, -11f, 0.42f, -20f, 14f);
                break;
            case "garrison_infested":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Infested, 5);
                Orbit(32f, -11f, 0.42f, -20f, 14f);
                break;
            case "garrison_interior":
                m.Inspect("vharn_outpost", 0, Rarity.Uncommon, PoiState.Infested, 5);
                m.SetMode(Main.Mode.Walk);
                WalkInto();
                break;
            case "vault_ruined":
                m.Inspect("precursor_vault", 0, Rarity.Rare, PoiState.Ruined, 3);
                Orbit(35f, -9f, 0.75f, -24f, 14f);
                break;
            case "mine_abandoned":
                m.Inspect("abandoned_mine", 0, Rarity.Common, PoiState.Abandoned, 2);
                Orbit(30f, -4f, 0.9f, -24f, 20f);
                break;
            case "lab_infested":
                m.Inspect("research_lab", 1, Rarity.Uncommon, PoiState.Infested, 4);
                m.SetMode(Main.Mode.Walk);
                WalkInto();
                break;
            case "spire_active":
                m.Inspect("kessari_spire", 0, Rarity.Uncommon, PoiState.Active, 6);
                Orbit(40f, -8f, 0.7f, 25f, 20f);
                break;
            case "overlay":
                m.Inspect("vharn_outpost", 0, Rarity.Rare, PoiState.Active, 5);
                m.Map.Visible = true;
                break;
            case "aerial":
            {
                m.SetMode(Main.Mode.Aerial);
                m.RevealAll();
                // Look across the two closest places, from the side, low enough to see their shapes.
                var (a, b) = m.ClosestPair();
                Vector3 mid = (a + b) / 2f, across = (b - a).Normalized(), side = across.Cross(Vector3.Up).Normalized();
                float span = a.DistanceTo(b);
                m.SetAerialCamera(mid + side * (span * 0.9f + 50f) + Vector3.Up * (span * 0.45f + 30f), mid);
                m.Sky.PlacePlanetInView(m.Camera, 18f, 22f);
                break;
            }
        }
    }

    /// <summary>Orbits the inspector camera relative to the way in, and hangs the sky world in view.</summary>
    void Orbit(float yawFromEntrance, float pitch, float distance, float planetYaw, float planetElevation)
    {
        _main.SetOrbit(_main.EntranceYaw() + yawFromEntrance, pitch, distance);
        _main.Sky.PlacePlanetInView(_main.Camera, planetYaw, planetElevation);
    }

    /// <summary>Stands a little way inside the place, past the door, looking deeper in.</summary>
    void WalkInto()
    {
        var stage = _main.Stage;
        var poi = stage.Poi;
        // Pick a cell partway along the critical path, look along the path from there.
        var path = poi.CriticalPath;
        if (path.Count < 12) return;
        Int3 at = path[path.Count * 2 / 5], ahead = path[System.Math.Min(path.Count - 1, path.Count * 2 / 5 + 6)];
        var feet = stage.ToGlobal(new Vector3(at.X + 0.5f, at.Y + 0.05f, at.Z + 0.5f));
        var look = stage.ToGlobal(new Vector3(ahead.X + 0.5f, ahead.Y + 1.2f, ahead.Z + 0.5f)) - (feet + Vector3.Up * 1.6f);
        float yaw = Mathf.Atan2(-look.X, -look.Z);
        _main.Player.Place(feet, yaw);
        _main.Player.SetLook(yaw, Mathf.Clamp(Mathf.Asin(look.Normalized().Y), -0.4f, 0.3f));
    }

    async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, "frame_post_draw");
        Image image = GetViewport().GetTexture().GetImage();
        Error result = image.SavePng(_out);
        GD.Print(result == Error.Ok ? $"saved {_out}" : $"could not save {_out}: {result}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }
}
