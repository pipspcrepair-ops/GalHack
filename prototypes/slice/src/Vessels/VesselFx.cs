using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using ShipHandling.Handling;

namespace Slice.Vessels;

/// <summary>
/// A vessel's moving parts, drawn from what the physics did this tick: landing legs at their
/// extension, and a flame from each thruster sized by how hard the handling model is using it
/// (after the physics sandbox's gear view and flames). Presentation only.
/// </summary>
public partial class VesselFx : Node3D
{
    static readonly Color Flame = new("ffb347"), Strut = new("2b2d42"), Foot = new("f4a100");
    static StandardMaterial3D _flame, _strut, _foot;

    Vessel _vessel;
    readonly List<(MeshInstance3D flame, Vector3 nozzle, Vector3 exhaust, Vector3 push)> _flames = new();
    readonly List<(MeshInstance3D strut, MeshInstance3D foot)> _legs = new();

    /// <summary>Extra flame on every engine, 0..1 (the pod's retro burn on the way down).</summary>
    public float Burn;

    public void Setup(Vessel vessel)
    {
        _vessel = vessel;
        foreach (Node child in GetChildren()) child.QueueFree();
        _flames.Clear();
        _legs.Clear();
        _flame ??= new StandardMaterial3D
        {
            AlbedoColor = new Color(Flame, 0.85f), EmissionEnabled = true, Emission = Flame, EmissionEnergyMultiplier = 3f,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _strut ??= new StandardMaterial3D { AlbedoColor = Strut, Roughness = 0.5f, Metallic = 0.4f };
        _foot ??= new StandardMaterial3D { AlbedoColor = Foot, Roughness = 0.7f };
        if (vessel.Spec == null) return;

        float cell = vessel.Structure.Grid.CellSize;
        var nozzles = new List<(Vector3 at, Vector3 push)>();
        foreach (ThrusterSpec t in vessel.Spec.Thrusters) nozzles.Add((t.Position, t.Facing.Normalized()));
        if (nozzles.Count == 0)
        {
            // No engines (the escape pod): its one-shot retro packs fire from under the floor's corners.
            var (min, max) = vessel.Structure.Bounds();
            for (int i = 0; i < 4; i++)
                nozzles.Add((new Vector3((i % 2 == 0 ? min.X + 1 : max.X - 1) * cell, (min.Y - 0.5f) * cell, (i / 2 == 0 ? min.Z + 1 : max.Z - 1) * cell), Vector3.Up));
        }
        foreach (var (position, facing) in nozzles)
        {
            Vector3 push = facing, exhaust = -push;
            var flame = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = cell * 0.32f, BottomRadius = 0.02f, Height = 1f, RadialSegments = 10, Rings = 1 },
                MaterialOverride = _flame,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Basis = push.Dot(Vector3.Up) < -0.999f ? new Basis(Vector3.Right, Mathf.Pi) : new Basis(new Quaternion(Vector3.Up, push)),
                Visible = false,
            };
            AddChild(flame);
            _flames.Add((flame, position + exhaust * (cell * 0.5f), exhaust, push));
        }
        for (int i = 0; i < vessel.Spec.LandingGear.Count; i++)
        {
            var strut = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.06f, BottomRadius = 0.06f, Height = 1f }, MaterialOverride = _strut };
            var foot = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.22f, Height = 0.08f }, MaterialOverride = _foot };
            AddChild(strut);
            AddChild(foot);
            _legs.Add((strut, foot));
        }
        Refresh();
    }

    public override void _PhysicsProcess(double delta) => Refresh();

    float _kick;
    static readonly RandomNumberGenerator Scatter = new() { Seed = 77 };

    /// <summary>Engines near the ground blow up a ring of dust under the vessel as it lands or lifts off.</summary>
    void Kick(float dt, float lift)
    {
        lift = Mathf.Max(lift, Burn);
        if (lift < 0.15f || (_kick -= dt) > 0f || _vessel.Zone?.Dust == null || _vessel.AboveGround > 9f) return;
        _kick = 0.09f;
        Vector3 at = _vessel.Body.WorldPosition.ToGodot();
        float angle = Scatter.Randf() * Mathf.Tau, reach = 2f + Scatter.Randf() * 3f;
        Vector3 spot = _vessel.Zone.GroundAt(at.X + Mathf.Cos(angle) * reach, at.Z + Mathf.Sin(angle) * reach);
        if (_vessel.Zone.Water.LiquidAt(spot.X, spot.Z, out _)) return;
        Color tint = _vessel.Zone.Planet.Look.Dust;
        _vessel.Zone.Dust.Puff(spot + Vector3.Up * 0.3f, new Color(tint.R, tint.G, tint.B, 1f), 2.4f);
    }

    void Refresh()
    {
        if (_vessel?.Flight == null) return;
        bool live = !_vessel.IsWreck && !_vessel.Body.Asleep && _vessel.Structure.Core != null;
        // A retro burn draws flames longer than any thruster's, and from a sleeping body too.
        Vector3 thrust = _vessel.Flight.State.ThrustLocal;
        float boost = _vessel.Flight.State.Boosting ? 1.5f : 1f;
        Kick((float)GetPhysicsProcessDeltaTime(), live ? Mathf.Clamp(thrust.Y / 10f, 0f, 1f) : 0f);
        foreach (var (flame, nozzle, exhaust, push) in _flames)
        {
            float effort = live ? Mathf.Clamp(thrust.Dot(push) / 10f, 0f, 1f) : 0f;
            float length = effort > 0.03f ? 0.3f + 2.7f * effort * boost : 0f;
            if (Burn > 0f) length = Mathf.Max(length, 2.5f + 3.5f * Burn);
            flame.Visible = length > 0.05f;
            flame.Scale = new Vector3(1f, Mathf.Max(length, 0.01f), 1f);
            flame.Position = nozzle + exhaust * (length * 0.5f);
        }

        LandingGear gear = _vessel.Controller?.Gear;
        for (int i = 0; i < _legs.Count; i++)
        {
            var (strut, foot) = _legs[i];
            if (gear == null || i >= gear.Count)
            {
                strut.Visible = foot.Visible = false;
                continue;
            }
            GearLeg leg = gear.Leg(i);
            float reach = Mathf.Max(0.05f, leg.Length * gear.Extension);
            Vector3 hip = leg.Hip.ToGodot();
            strut.Position = hip + Vector3.Down * (reach * 0.5f);
            strut.Scale = new Vector3(1f, reach, 1f);
            foot.Position = hip + Vector3.Down * reach;
            strut.Visible = foot.Visible = gear.Extension > 0.05f;
        }
    }
}
