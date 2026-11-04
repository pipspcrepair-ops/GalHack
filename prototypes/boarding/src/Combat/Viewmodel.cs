using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// The gun in your hands. Built from boxes in the paperback palette, it kicks back on every
/// shot on a stiff spring (so it settles fast and never drifts), bobs with your stride, sways
/// against your mouse, and pulls back when you stand nose-to-wall instead of clipping through.
/// The muzzle flash is a pair of crossed hot quads plus a light that lives for one or two frames.
/// </summary>
public partial class Viewmodel : Node3D
{
    static readonly Vector3 Rest = new(0.2f, -0.19f, -0.42f);

    readonly Dictionary<string, (Node3D gun, Node3D muzzle)> _guns = new();
    Node3D _current;
    Node3D _muzzle;
    Node3D _flash;
    OmniLight3D _flashLight;
    float _flashLeft;

    Vector3 _kick, _kickVel;        // x: sideways, y: up, z: back
    float _pitchKick, _pitchVel;
    float _bob, _lower, _retract;
    Vector2 _sway;

    public Vector3 MuzzleGlobal => _muzzle?.GlobalPosition ?? GlobalPosition;

    public override void _Ready()
    {
        Build("smg", BuildSmg);
        Build("shotgun", BuildShotgun);
        Build("sidearm", BuildSidearm);

        _flash = new Node3D { Visible = false };
        var hot = new StandardMaterial3D
        {
            AlbedoColor = new Color("ffd27a"),
            AlbedoTexture = Art.Spot,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        // Two long quads crossed around the barrel axis, so the flash reads from any angle.
        foreach (float roll in new[] { 0f, 90f })
        {
            var holder = new Node3D { RotationDegrees = new Vector3(0, 0, roll) };
            _flash.AddChild(holder);
            var quad = new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(0.16f, 0.42f) }, MaterialOverride = hot, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            quad.RotationDegrees = new Vector3(90f, 0f, 0f);
            quad.Position = new Vector3(0, 0, -0.12f);
            holder.AddChild(quad);
        }
        _flash.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(0.24f, 0.24f) },
            MaterialOverride = hot,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        _flashLight = new OmniLight3D { LightColor = new Color("ffb347"), LightEnergy = 0f, OmniRange = 6f };
        _flash.AddChild(_flashLight);
    }

    public void Show(string id)
    {
        foreach (var (key, (gun, _)) in _guns) gun.Visible = key == id;
        if (!_guns.TryGetValue(id, out var entry)) return;
        _current = entry.gun;
        _muzzle = entry.muzzle;
        _flash.GetParent()?.RemoveChild(_flash);
        _muzzle.AddChild(_flash);
        _lower = 1f;    // raise the new gun into view
    }

    /// <summary>One shot: kick the gun on its spring and pop the flash.</summary>
    public void Fired(float kick)
    {
        _kickVel += new Vector3((GD.Randf() - 0.5f) * 0.4f * kick, 0.6f * kick, 2.4f * kick);
        _pitchVel += 9f * kick;
        _flashLeft = 0.045f;
        _flash.Visible = true;
        _flash.Rotation = new Vector3(0, 0, GD.Randf() * Mathf.Tau);
        _flash.Scale = Vector3.One * (0.8f + GD.Randf() * 0.5f) * Mathf.Clamp(kick, 0.6f, 1.6f);
        _flashLight.LightEnergy = 2.5f;
    }

    /// <summary>Per render frame. speed01: how fast we're walking; wallDistance: free space ahead.</summary>
    public void Tick(float dt, float speed01, Vector2 mouse, bool reloading, float wallDistance)
    {
        // Critically damped-ish springs: stiff, so a burst reads as distinct kicks.
        const float stiffness = 260f, damping = 24f;
        _kickVel += (-_kick * stiffness - _kickVel * damping) * dt;
        _kick += _kickVel * dt;
        _pitchVel += (-_pitchKick * stiffness - _pitchVel * damping) * dt;
        _pitchKick += _pitchVel * dt;

        _bob += dt * Mathf.Lerp(0f, 11f, speed01);
        _sway = _sway.Lerp(new Vector2(-mouse.X, mouse.Y) * 0.0006f, Mathf.Min(1f, dt * 12f));
        _lower = Mathf.MoveToward(_lower, reloading ? 0.55f : 0f, dt * 4f);
        _retract = Mathf.Lerp(_retract, Mathf.Clamp(0.75f - wallDistance, 0f, 0.35f), Mathf.Min(1f, dt * 15f));

        Vector3 bob = new(Mathf.Sin(_bob) * 0.012f * speed01, -Mathf.Abs(Mathf.Cos(_bob)) * 0.014f * speed01, 0f);
        Position = Rest + bob + new Vector3(_sway.X + _kick.X * 0.02f, _sway.Y - _lower * 0.25f + _kick.Y * 0.01f, _kick.Z * 0.03f + _retract);
        Rotation = new Vector3(Mathf.DegToRad(_pitchKick * 1.5f + _lower * -25f), 0f, Mathf.DegToRad(-_sway.X * 400f));

        if (_flashLeft > 0f)
        {
            _flashLeft -= dt;
            if (_flashLeft <= 0f)
            {
                _flash.Visible = false;
                _flashLight.LightEnergy = 0f;
            }
        }
    }

    // ---- Models ------------------------------------------------------------------------

    void Build(string id, System.Action<Node3D> build)
    {
        var gun = new Node3D { Name = id, Visible = false };
        AddChild(gun);
        build(gun);
        var muzzle = gun.GetNode<Node3D>("Muzzle");
        _guns[id] = (gun, muzzle);
    }

    static void Part(Node3D gun, Vector3 size, Material material, Vector3 at) =>
        gun.AddChild(Art.Mesh(Art.BoxMesh(size), material, at, false));

    static void BuildSmg(Node3D g)
    {
        var dark = Art.Flat(Palette.Dark, 0.45f, 0.5f);
        Part(g, new Vector3(0.075f, 0.1f, 0.36f), dark, new Vector3(0, 0, 0));
        Part(g, new Vector3(0.078f, 0.03f, 0.3f), Art.Flat(Palette.Orange, 0.5f), new Vector3(0, 0.035f, 0.01f));
        Part(g, new Vector3(0.05f, 0.17f, 0.06f), dark, new Vector3(0, -0.12f, -0.06f));            // magazine
        Part(g, new Vector3(0.05f, 0.12f, 0.05f), Art.Flat(Palette.Oxblood, 0.6f), new Vector3(0, -0.1f, 0.1f)); // grip
        Part(g, new Vector3(0.035f, 0.035f, 0.16f), dark, new Vector3(0, 0.01f, -0.25f));            // barrel
        Part(g, new Vector3(0.06f, 0.06f, 0.14f), Art.Flat(Palette.Bone, 0.6f), new Vector3(0, -0.01f, 0.24f));  // stock
        Part(g, new Vector3(0.02f, 0.02f, 0.02f), Art.Glow(Palette.TealGlow, 4f), new Vector3(0, 0.07f, -0.05f)); // sight
        g.AddChild(new Node3D { Name = "Muzzle", Position = new Vector3(0, 0.01f, -0.34f) });
    }

    static void BuildShotgun(Node3D g)
    {
        var dark = Art.Flat(Palette.Dark, 0.45f, 0.5f);
        Part(g, new Vector3(0.085f, 0.1f, 0.42f), Art.Flat(Palette.Red, 0.55f, 0.2f), new Vector3(0, 0, 0.02f));
        Part(g, new Vector3(0.04f, 0.04f, 0.32f), dark, new Vector3(-0.022f, 0.02f, -0.32f));
        Part(g, new Vector3(0.04f, 0.04f, 0.32f), dark, new Vector3(0.022f, 0.02f, -0.32f));
        Part(g, new Vector3(0.1f, 0.06f, 0.14f), Art.Flat(Palette.Yellow, 0.5f), new Vector3(0, -0.03f, -0.22f));  // pump
        Part(g, new Vector3(0.06f, 0.13f, 0.06f), dark, new Vector3(0, -0.1f, 0.13f));
        Part(g, new Vector3(0.07f, 0.08f, 0.16f), dark, new Vector3(0, -0.02f, 0.28f));
        g.AddChild(new Node3D { Name = "Muzzle", Position = new Vector3(0, 0.02f, -0.5f) });
    }

    static void BuildSidearm(Node3D g)
    {
        var dark = Art.Flat(Palette.Dark, 0.45f, 0.5f);
        Part(g, new Vector3(0.07f, 0.1f, 0.24f), Art.Flat(Palette.Yellow, 0.5f, 0.2f), new Vector3(0, 0, -0.02f));
        Part(g, new Vector3(0.05f, 0.13f, 0.06f), dark, new Vector3(0, -0.1f, 0.06f));
        Part(g, new Vector3(0.05f, 0.05f, 0.08f), dark, new Vector3(0, 0.01f, -0.17f));
        Part(g, new Vector3(0.03f, 0.03f, 0.03f), Art.Glow(Palette.TealGlow, 4f), new Vector3(0, 0.01f, -0.215f));
        Part(g, new Vector3(0.074f, 0.02f, 0.18f), Art.Flat(Palette.Teal, 0.5f), new Vector3(0, 0.05f, -0.02f));
        g.AddChild(new Node3D { Name = "Muzzle", Position = new Vector3(0, 0.01f, -0.23f) });
    }
}
