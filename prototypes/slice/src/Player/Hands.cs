using System;
using Godot;
using Slice.Items;

namespace Slice.Player;

/// <summary>
/// The first-person hands: a gloved hand holding the survival tool low on the right of the view,
/// its emitter lit by mode (teal to gather, red to shoot, amber to build), kicking back on each
/// shot and swaying a little as you walk. Shown when the tool, a block, a core or a device is in
/// hand, hidden in a seat or in third person. Procedural boxes and cylinders, no assets.
/// </summary>
public partial class Hands : Node3D
{
    public Game.Game Game;

    Node3D _rig, _tip, _toolRoot, _gunRoot;
    ShaderMaterial _lens, _lensRound, _coreMat, _topCore;
    int _shotsSeen;
    float _kick, _bob, _strength = 1f, _camKick, _shake, _shakeTime, _reloadDip;
    Color _tint;

    /// <summary>Each weapon's model, built the first time it is in hand (shared/Arms' generated look).</summary>
    readonly System.Collections.Generic.Dictionary<string, Slice.Arms.GunModel.Built> _guns = new();
    Slice.Arms.GunModel.Built _gun;

    /// <summary>The weapon model in hand now, or null (tests and screenshots).</summary>
    public Slice.Arms.GunModel.Built Gun => _gunRoot != null && _gunRoot.Visible ? _gun : null;

    /// <summary>Where the gun model sits in the hand: its grip's top in the glove.</summary>
    static readonly Vector3 GripAt = new(0.19f, -0.175f, -0.36f);
    const float GunScale = 0.62f;

    /// <summary>Where the barrel's tip is now, in the world, or null when the hands aren't showing: flashes and bolts begin here, riding the hand and its recoil.</summary>
    public Vector3? BarrelTip => !Visible ? null : Gun != null ? _gun.Muzzle.GlobalPosition : _tip?.GlobalPosition;

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

    /// <summary>The same, with a weapon's own camera kick in degrees.</summary>
    public void Recoil(GunFeel feel, float cameraKick)
    {
        Recoil(feel);
        _camKick = cameraKick;
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
        _into = _rig;
        var suit = new StandardMaterial3D { AlbedoColor = new Color("e85d04"), Roughness = 0.7f };
        var glove = new StandardMaterial3D { AlbedoColor = new Color("2b2d42"), Roughness = 0.8f };
        var metal = new StandardMaterial3D { AlbedoColor = new Color("ece3c8"), Roughness = 0.4f, Metallic = 0.4f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color("1b263b"), Roughness = 0.6f };
        // Forearm and glove.
        Add(new CylinderMesh { TopRadius = 0.045f, BottomRadius = 0.055f, Height = 0.32f }, suit, new Vector3(0.21f, -0.24f, -0.18f), new Vector3(-75, 8, 0));
        Add(new BoxMesh { Size = new Vector3(0.09f, 0.07f, 0.1f) }, glove, new Vector3(0.185f, -0.19f, -0.33f), Vector3.Zero);
        // The tool's parts hang off their own node, so a weapon can take its place in the glove.
        _toolRoot = new Node3D { Name = "Tool" };
        _rig.AddChild(_toolRoot);
        _into = _toolRoot;
        // Guns are modelled at full size; in the hand they sit a little smaller, so the view stays open.
        _gunRoot = new Node3D { Name = "Gun", Position = GripAt, Scale = Vector3.One * GunScale, Visible = false };
        _rig.AddChild(_gunRoot);
        // The tool: body, grip, barrel, emitter ring; each a worn, mottled finish (HeldFinish), with
        // a few proud plates and a muzzle shroud for panel lines. The glowing panel stays clean.
        Color hull = new("d8cfb6"), plate = new("c4baa0"), grip = new("2a3550");
        void Worn(Mesh mesh, Color c, Vector3 at, Vector3 deg, float rough, float metal, float seed) =>
            Add(mesh, HeldFinish.Make(mesh, c, 0.65f, rough, metal, seed), at, deg);
        Worn(new BoxMesh { Size = new Vector3(0.08f, 0.09f, 0.26f) }, hull, new Vector3(0.17f, -0.15f, -0.38f), Vector3.Zero, 0.5f, 0.3f, 1f);
        Worn(new BoxMesh { Size = new Vector3(0.05f, 0.11f, 0.05f) }, grip, new Vector3(0.17f, -0.21f, -0.33f), new Vector3(15, 0, 0), 0.7f, 0.05f, 2f);
        Worn(new CylinderMesh { TopRadius = 0.022f, BottomRadius = 0.026f, Height = 0.14f, RadialSegments = 8 }, grip, new Vector3(0.17f, -0.145f, -0.55f + 0.07f), new Vector3(90, 0, 0), 0.5f, 0.3f, 3f);
        // The muzzle emitter: a dark worn bezel ring, a round translucent lens set back inside it, and a glowing core behind.
        var tipLens = new CylinderMesh { TopRadius = 0.021f, BottomRadius = 0.021f, Height = 0.004f, RadialSegments = 12 };
        _lensRound = HeldFinish.Emitter(tipLens, new Color("2ec4b6"));
        Worn(new TorusMesh { InnerRadius = 0.02f, OuterRadius = 0.034f, Rings = 12, RingSegments = 6 }, grip, new Vector3(0.17f, -0.145f, -0.545f), new Vector3(90, 0, 0), 0.5f, 0.3f, 9f);
        Add(tipLens, _lensRound, new Vector3(0.17f, -0.145f, -0.5485f), new Vector3(90, 0, 0));
        Add(new CylinderMesh { TopRadius = 0.011f, BottomRadius = 0.011f, Height = 0.004f, RadialSegments = 8 }, _coreMat = HeldFinish.Emitter(new CylinderMesh { TopRadius = 0.011f, Height = 0.004f }, new Color("2ec4b6")), new Vector3(0.17f, -0.145f, -0.5435f), new Vector3(90, 0, 0));
        // The top light: a bezel frame round a recessed translucent lens, with a bright core beneath.
        var topLens = new BoxMesh { Size = new Vector3(0.028f, 0.004f, 0.098f) };
        _lens = HeldFinish.Emitter(topLens, new Color("2ec4b6"));
        Add(topLens, _lens, new Vector3(0.17f, -0.1035f, -0.38f), Vector3.Zero);
        Add(new BoxMesh { Size = new Vector3(0.016f, 0.004f, 0.07f) }, _topCore = HeldFinish.Emitter(new BoxMesh { Size = new Vector3(0.016f, 0.004f, 0.07f) }, new Color("2ec4b6")), new Vector3(0.17f, -0.1085f, -0.38f), Vector3.Zero);
        foreach (float side in new[] { -1f, 1f })
        {
            Worn(new BoxMesh { Size = new Vector3(0.007f, 0.012f, 0.118f) }, grip, new Vector3(0.17f + side * 0.0185f, -0.1005f, -0.38f), Vector3.Zero, 0.6f, 0.1f, 10f + side);
            Worn(new BoxMesh { Size = new Vector3(0.044f, 0.012f, 0.007f) }, grip, new Vector3(0.17f, -0.1005f, -0.38f + side * 0.0555f), Vector3.Zero, 0.6f, 0.1f, 12f + side);
        }
        // Side plates, a rear cap, and a shroud round the barrel root.
        foreach (float side in new[] { -1f, 1f })
            Worn(new BoxMesh { Size = new Vector3(0.006f, 0.062f, 0.15f) }, plate, new Vector3(0.17f + side * 0.0405f, -0.15f, -0.4f), Vector3.Zero, 0.55f, 0.3f, 5f + side);
        Worn(new BoxMesh { Size = new Vector3(0.084f, 0.094f, 0.02f) }, plate, new Vector3(0.17f, -0.15f, -0.255f), Vector3.Zero, 0.55f, 0.3f, 7f);
        Worn(new BoxMesh { Size = new Vector3(0.058f, 0.058f, 0.05f) }, plate, new Vector3(0.17f, -0.145f, -0.52f), Vector3.Zero, 0.5f, 0.35f, 8f);
        // The barrel's tip (the barrel is 0.14 long, centred at z -0.48): where flashes and bolts begin.
        _tip = new Node3D { Name = "BarrelTip", Position = new Vector3(0.17f, -0.145f, -0.56f) };
        _toolRoot.AddChild(_tip);
        foreach (Node parent in new Node[] { _rig, _toolRoot })
            foreach (Node n in parent.GetChildren())
                if (n is MeshInstance3D m) m.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Visible = false;
    }

    Node3D _into;

    void Add(Mesh mesh, Material material, Vector3 at, Vector3 degrees) =>
        _into.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = material, Position = at, RotationDegrees = degrees });

    static readonly StringName Tint = "tint";

    /// <summary>The weapon in hand shows in the glove: its model is built the first time, and its charge rings light by stage.</summary>
    void ShowWeapon(GalHack.Arms.WeaponDef def, GalHack.Arms.Gun gun, float dt)
    {
        _toolRoot.Visible = def == null;
        _gunRoot.Visible = def != null;
        if (def == null) return;
        if (!_guns.TryGetValue(def.Base.Id, out Slice.Arms.GunModel.Built built))
        {
            built = Slice.Arms.GunModel.Build(def.Base);
            _gunRoot.AddChild(built.Root);
            _guns[def.Base.Id] = built;
        }
        if (_gun != built)
        {
            foreach (var b in _guns.Values) b.Root.Visible = b == built;
            _gun = built;
        }
        int stage = gun?.Stage ?? 0;
        if (built.Rings.Count > 0) Slice.Arms.GunModel.LightRings(built, gun != null && gun.Charging ? Math.Max(1, stage) : 0);
        // A reload dips the gun out of line and brings it back.
        _reloadDip = Mathf.MoveToward(_reloadDip, gun != null && gun.Reloading ? 1f : 0f, dt * 5f);
        _gunRoot.RotationDegrees = new Vector3(-28f * _reloadDip, 2.5f, 12f * _reloadDip);
        _gunRoot.Position = GripAt + new Vector3(0f, -0.05f * _reloadDip, 0f);
    }

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
        bool show = g?.Survivor != null && g.Survivor.Seated == null && !g.ThirdPerson && g.FixedCamera == null && !g.Suit.Dead
                    && hand is HandKind.Tool or HandKind.Block or HandKind.Core or HandKind.Device or HandKind.Blueprint or HandKind.Weapon;
        Visible = show;
        if (!show) return;
        GalHack.Arms.WeaponDef weapon = hand == HandKind.Weapon ? g.Zone.Data.Arms.OfItem(g.Pack.HandItem) : null;
        ShowWeapon(weapon, weapon != null && g.Arms?.InHand?.Base == weapon.Base ? g.Arms.InHand : null, dt);
        Color tint = hand != HandKind.Tool ? new Color("ffb703") : g.Tool.Mode == ToolMode.Shoot ? new Color("ef233c") : new Color("2ec4b6");
        if (weapon == null && tint != _tint)
        {
            _tint = tint;
            foreach (ShaderMaterial lens in new[] { _lens, _lensRound, _coreMat, _topCore }) lens.SetShaderParameter(Tint, tint);
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
