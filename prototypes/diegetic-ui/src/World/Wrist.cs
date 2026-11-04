using DiegeticUi.Logic;
using DiegeticUi.Pages;
using DiegeticUi.Screens;
using Godot;

namespace DiegeticUi.World;

/// <summary>
/// The left forearm and its suit computer, riding with the camera. Tab raises it into view;
/// it is a world screen like any other, except that it comes to you instead of the view going
/// to it, so you can keep walking while you read it.
///
/// Local space: the screen faces +Z (toward the eye when raised); the forearm runs off to
/// -X, toward the elbow at the lower left.
/// </summary>
public partial class Wrist : Node3D
{
    // Camera-relative poses. Raised: low in the view, tipped up to face the eye.
    static readonly Transform3D Raised = new(Basis.FromEuler(new Vector3(Mathf.DegToRad(-12), Mathf.DegToRad(3), Mathf.DegToRad(3))), new Vector3(-0.005f, -0.072f, -0.28f));
    static readonly Transform3D Lowered = new(Basis.FromEuler(new Vector3(Mathf.DegToRad(-80), Mathf.DegToRad(20), Mathf.DegToRad(35))), new Vector3(-0.22f, -0.62f, -0.3f));

    public Interactor Interactor { get; init; }
    public GameState State { get; init; }
    public System.Func<Hazard> HazardsHere { get; init; }

    public WorldScreen Screen { get; private set; }

    /// <summary>0 lowered and hidden, 1 fully raised.</summary>
    public float Raise { get; private set; }

    public override void _Ready()
    {
        ProcessPriority = 20; // after the interactor has moved the camera and stepped the blend
        var cream = new StandardMaterial3D { AlbedoColor = new Color("e9dfc4"), Roughness = 0.8f };
        var orange = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.7f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1d1f27"), Roughness = 0.5f, Metallic = 0.4f };
        var glove = new StandardMaterial3D { AlbedoColor = new Color("3a3d47"), Roughness = 0.9f };

        // Sleeve, cuff stripes, glove and the chunky device housing.
        Box(new Vector3(0.5f, 0.12f, 0.13f), new Vector3(-0.33f, -0.01f, -0.085f), cream);
        Box(new Vector3(0.04f, 0.135f, 0.145f), new Vector3(-0.2f, -0.01f, -0.085f), orange);
        Box(new Vector3(0.015f, 0.135f, 0.145f), new Vector3(-0.245f, -0.01f, -0.085f), orange);
        Box(new Vector3(0.13f, 0.1f, 0.11f), new Vector3(0.235f, -0.02f, -0.09f), glove);
        Box(new Vector3(0.36f, 0.235f, 0.06f), new Vector3(0, 0, -0.035f), dark);
        Box(new Vector3(0.36f, 0.018f, 0.064f), new Vector3(0, 0.126f, -0.035f), orange);
        Box(new Vector3(0.025f, 0.05f, 0.03f), new Vector3(0.17f, 0.07f, 0.0f), orange); // a chunky side button

        Screen = new WorldScreen
        {
            Name = "WristScreen",
            Pixels = new Vector2I(WristPage.Width, WristPage.Height),
            SizeMeters = new Vector2(0.3f, 0.1875f),
            Pickable = false,
            MovesCamera = false,
            Bezel = 0,
            Prompt = "WRIST",
            ReadableDistance = 2,
            Position = new Vector3(0, 0, 0.0f),
        };
        AddChild(Screen);
        Screen.SetContent(new WristPage(State.Suit, State.Loadout, HazardsHere));

        Transform = Lowered;
        Visible = false;
    }

    public override void _Process(double delta)
    {
        bool mine = Interactor.Focus.Target == Screen;
        Raise = mine ? (float)Interactor.Focus.EasedBlend : 0;
        Visible = Raise > 0.001f;
        if (Visible) Transform = Lowered.InterpolateWith(Raised, Raise);
    }

    void Box(Vector3 size, Vector3 position, Material material) =>
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = material, Position = position, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
}
