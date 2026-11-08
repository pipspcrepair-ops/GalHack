using System;
using System.Collections.Generic;
using GalHack.Items;
using Godot;
using Slice.Items;

namespace Slice.Player;

/// <summary>
/// The first-person hands: a gloved hand holding what is in it low on the right of the view. What it
/// holds is the item's own model from <see cref="ItemModels"/> (the same one its inventory icon is
/// rendered from): the survival tool or a drill, its emitter lit by mode (teal to gather, red to
/// shoot, amber to build), the handheld scanner; a block, a core or a device in hand shows the
/// plain tool, amber. It kicks back on each shot and sways a little as you walk. Shown when a
/// held model, a block, a core or a device is in hand, hidden in a seat or in third person.
/// Procedural boxes and cylinders, no assets.
/// </summary>
public partial class Hands : Node3D
{
    public Game.Game Game;

    Node3D _rig;
    readonly Dictionary<string, ItemModel> _models = new();
    ItemModel _shown;
    Color _tint;
    int _shotsSeen;
    float _kick, _bob, _strength = 1f, _camKick, _shake, _shakeTime;

    /// <summary>Where the barrel's tip is now, in the world, or null when the hands aren't showing: flashes and bolts begin here, riding the hand and its recoil.</summary>
    public Vector3? BarrelTip => Visible && _shown?.Tip != null ? _shown.Tip.GlobalPosition : null;

    /// <summary>The model in the glove now (tests compare it with the item's icon model), or null when the hands aren't showing.</summary>
    public ItemModel Shown => Visible ? _shown : null;

    /// <summary>The camera's tilt now, in radians (zero at rest): for tests.</summary>
    public float CameraTilt { get; private set; }
    public float Kick => _kick;

    /// <summary>The biggest kick and camera tilt since <see cref="ResetPeaks"/> (tests: a tick can fall between drawn frames).</summary>
    public float PeakKick { get; private set; }
    public float PeakTilt { get; private set; }
    public void ResetPeaks() => PeakKick = PeakTilt = 0f;

    /// <summary>A light camera shake for a pilot whose vessel gun fired (strength 0..1); it dies away on its own.</summary>
    public void Shake(float strength) => _shake = MathF.Max(_shake, strength);

    /// <summary>The hand's kick back and the camera's tilt for a fresh shot of this kind of fire.</summary>
    public void Recoil(GunFeel feel)
    {
        _kick = 1f;
        _strength = feel.Recoil;
        _camKick = feel.CameraKick;
        if (feel.Shake > 0f) Shake(feel.Shake);
    }

    /// <summary>A blow from somewhere (world frame): the view is knocked away from it, and rights itself.</summary>
    public void Jolt(Vector3 from, float strength)
    {
        Camera3D cam = Game?.Camera;
        if (cam == null) return;
        Vector3 local = cam.GlobalBasis.Inverse() * (from - cam.GlobalPosition);
        _joltRoll = local.X < 0f ? -1f : 1f;
        _joltPitch = local.Z < 0f ? 1f : -0.6f; // from ahead the head goes back; from behind, forward
        _jolt = MathF.Max(_jolt, Math.Clamp(strength, 0.4f, 1f));
        PeakJolt = MathF.Max(PeakJolt, _jolt);
    }

    float _jolt, _joltRoll, _joltPitch;

    /// <summary>The hardest blow felt so far (tests).</summary>
    public float PeakJolt { get; private set; }

    /// <summary>The muzzle's offset in the camera's frame (matches <see cref="SurvivalTool.Muzzle"/>).</summary>
    public static readonly Vector3 MuzzleOffset = new(0.17f, -0.15f, -0.55f);

    public override void _Ready()
    {
        _rig = new Node3D { Name = "Rig", Scale = Vector3.One * 0.75f, Position = new Vector3(0.05f, -0.03f, 0f) };
        AddChild(_rig);
        var suit = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.7f };
        var glove = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.8f };
        // Forearm and glove.
        Add(new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.055f, Height = 0.32f }, suit, new Vector3(0.21f, -0.24f, -0.18f), new Vector3(-75, 8, 0));
        Add(new BoxMesh { Size = new Vector3(0.09f, 0.07f, 0.1f) }, glove, new Vector3(0.185f, -0.19f, -0.33f), Vector3.Zero);
        _shown = Held(PlainTool);
        Visible = false;
    }

    /// <summary>What a block, a core or a device in the hand shows: the plain tool.</summary>
    const string PlainTool = "";

    /// <summary>The model for what is in hand, built the first time it is wanted and kept: the item's own, else the plain tool.</summary>
    ItemModel Held(string key, ItemDef item = null)
    {
        if (_models.TryGetValue(key, out ItemModel model)) return model;
        model = (item != null ? ItemModels.Build(item) : null) ?? HeldTool.Build(null);
        model.Source = ModelSource.Held;
        model.Root.Visible = false;
        _rig.AddChild(model.Root);
        foreach (Node n in Walk(model.Root))
            if (n is MeshInstance3D m) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        return _models[key] = model;
    }

    static IEnumerable<Node> Walk(Node n)
    {
        yield return n;
        foreach (Node c in n.GetChildren())
            foreach (Node d in Walk(c)) yield return d;
    }

    void Add(Mesh mesh, Material material, Vector3 at, Vector3 degrees) =>
        _rig.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = degrees });

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        var g = Game;
        if (g?.Tool != null && g.Tool.HeldMuzzle == null) g.Tool.HeldMuzzle = () => BarrelTip;
        if (g?.Tool != null && g.Tool.Shots != _shotsSeen)
        {
            _shotsSeen = g.Tool.Shots;
            Recoil(GunFeel.Get(g.Tool.Feel));
        }
        _kick = Mathf.MoveToward(_kick, 0f, dt / 0.15f);
        _shake = Mathf.MoveToward(_shake, 0f, dt * 4f);
        _jolt = Mathf.MoveToward(_jolt, 0f, dt * 3.5f);
        _shakeTime += dt * 55f;
        TiltCamera();
        HandKind hand = g?.Pack?.Hand ?? HandKind.Empty;
        ItemDef item = null;
        if (g?.Pack?.HandItem is string id) g.Zone?.Data?.Items.TryGet(id, out item);
        bool model = ItemModels.IsHeld(item);
        // (Not while a Husk has your head: its face is all there is to see.)
        bool show = g?.Survivor != null && g.Survivor.Seated == null && !g.ThirdPerson && g.FixedCamera == null && !g.Suit.Dead && g.HeldBy == null
                    && (model || hand is HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint);
        Visible = show;
        if (!show) return;
        ItemModel now = model ? Held(item.Id, item) : Held(PlainTool);
        if (now != _shown)
        {
            _shown.Root.Visible = false;
            _shown = now;
            _tint = default;
        }
        _shown.Root.Visible = true;
        Color tint = hand == HandKind.Scanner ? (g.ScanWait > 0 ? new Color("ffb703") : new Color("2ec4b6"))
            : hand != HandKind.Tool ? new Color("ffb703") : g.Tool.Mode == ToolMode.Shoot ? new Color("ef233c") : new Color("2ec4b6");
        if (tint != _tint)
        {
            _tint = tint;
            foreach (ShaderMaterial lens in _shown.Lenses) lens.SetShaderParameter("tint", tint);
        }
        float speed = g.Survivor.Body.OnFloor ? new Vector2(g.Survivor.Body.Velocity.X, g.Survivor.Body.Velocity.Z).Length() : 0f;
        _bob += dt * speed * 1.6f;
        float sway = MathF.Min(speed, 6f) / 6f;
        // A snappy kick: back and up at once, then easing home over about 0.15 s.
        float k = _kick * _kick * _strength;
        _rig.Position = new Vector3(0.05f + MathF.Sin(_bob) * 0.008f * sway, -0.03f + MathF.Abs(MathF.Cos(_bob)) * 0.01f * sway + k * 0.012f, k * 0.06f);
        _rig.RotationDegrees = new Vector3(k * 7f, 0f, 0f);
    }

    /// <summary>
    /// Tilts the camera a hair after the game has set it for the frame (the game sets it afresh every
    /// frame, so nothing is ever carried over: at rest the aim is exactly what the player aimed).
    /// </summary>
    void TiltCamera()
    {
        Camera3D cam = Game?.Camera;
        if (cam == null) return;
        float up = _kick * _kick * _camKick * Mathf.DegToRad(1f);
        float sx = _shake * 0.012f * MathF.Sin(_shakeTime * 1.7f), sy = _shake * 0.012f * MathF.Sin(_shakeTime * 2.3f + 1f);
        CameraTilt = up + MathF.Abs(sx) + MathF.Abs(sy);
        PeakKick = MathF.Max(PeakKick, _kick);
        PeakTilt = MathF.Max(PeakTilt, CameraTilt);
        if (_kick <= 0f && _shake <= 0f && _jolt <= 0f) return;
        Transform3D t = cam.GlobalTransform;
        float blow = _jolt * _jolt;
        t.Basis = t.Basis * new Basis(Vector3.Right, up + sx + blow * _joltPitch * 0.07f) * new Basis(Vector3.Up, sy) * new Basis(Vector3.Forward, blow * _joltRoll * 0.09f);
        cam.GlobalTransform = t;
    }
}
