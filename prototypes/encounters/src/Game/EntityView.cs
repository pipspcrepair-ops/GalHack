using System.Collections.Generic;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// Draws one thing the director knows about: an event's crewed ship, a wreck, a claimed ship or
/// a player's cargo pod. It only reads the director. A ship promoted to a lasting record keeps
/// its id, so its view carries on without a seam.
/// </summary>
public partial class EntityView : Node3D
{
    public WorldEntity Entity { get; private set; }
    public Hull Hull { get; private set; }

    /// <summary>The last frame the director still listed it; views not seen are freed.</summary>
    public int SeenFrame;

    MeshInstance3D _mesh;
    readonly List<MeshInstance3D> _flares = new();
    CpuParticles3D _smoke;
    MeshInstance3D _beacon, _beam;
    Fx _fx;
    bool _wrecked, _placed;
    Vector3 _spinAxis, _lastPosition, _wreckDrift;
    float _spinRate;

    public static EntityView Create(WorldEntity entity, Fx fx) => new()
    {
        Entity = entity,
        Hull = entity.IsCreature ? CreatureForms.Get(entity.Hull) : HullLibrary.Get(entity.Hull),
        Name = $"E{entity.Id}",
        _fx = fx,
    };

    public override void _Ready()
    {
        _mesh = new MeshInstance3D { Mesh = Hull.Mesh, MaterialOverride = Entity.IsCreature ? CreatureForms.Material(Entity.Hull) : Visuals.Hull };
        AddChild(_mesh);
        foreach (var (at, size) in Hull.Engines)
        {
            Color c = Hull.EngineColor;
            MeshInstance3D flare = Visuals.Flare(new Color(c.R * 2.2f, c.G * 2.2f, c.B * 2.2f), size * 2.2f);
            flare.Position = at + Vector3.Back * size * 0.4f;
            _mesh.AddChild(flare);
            _flares.Add(flare);
        }

        var rng = new RandomNumberGenerator { Seed = (ulong)Entity.Id * 2654435761UL };
        _spinAxis = new Vector3(rng.RandfRange(-1, 1), rng.RandfRange(-1, 1), rng.RandfRange(-1, 1)).Normalized();
        _spinRate = rng.RandfRange(0.025f, 0.07f) * (rng.Randf() < 0.5f ? -1f : 1f);

        if (Entity.Hull == "bloom-hulk") _mesh.SetInstanceShaderParameter("corruption", 1f);
        if (IsDistress)
        {
            _beacon = Visuals.Flare(new Color(4f, 0.4f, 0.2f), 9f);
            _beacon.Position = new Vector3(0, 4.6f, -4);
            _mesh.AddChild(_beacon);
            AddSmoke(0.6f); // venting atmosphere
        }
        if (Entity.Role == "miner")
        {
            _beam = new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.9f, 0.9f, 1f) },
                MaterialOverride = Visuals.Bolt(new Color(0.6f, 2.4f, 2.2f)),
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TopLevel = true,
                Visible = false,
            };
            AddChild(_beam);
        }
    }

    bool IsDistress => Entity.EventId is "distress-call" or "crash-site";

    /// <summary>Places the view at the director's time. Called every physics tick, so physics interpolation smooths it.</summary>
    public void Sync(double time, float dt)
    {
        WorldEntity e = Entity;
        e.Sample(time, out var p, out var f);
        Vector3 position = p.ToGodot(), forward = f.ToGodot().Normalized();
        Basis basis;

        if (e.Script != null)
        {
            // Bank into turns: look where the heading will be in a moment.
            e.Script.Evaluate(time + 0.3, out _, out var ahead);
            Vector3 turn = (ahead.ToGodot() - forward) / 0.3f;
            basis = Look(forward, Vector3.Up + turn * 2.5f);
        }
        else basis = Look(forward, Vector3.Up);

        if (e.Wrecked && !_wrecked) BecomeWreck(position);
        if (_wrecked)
        {
            // The wreck coasts to a stop and tumbles; the record itself stays where it was lost.
            float age = (float)(time - e.WreckedAt);
            position += _wreckDrift * (1f - Mathf.Exp(-age)) ;
            basis = new Basis(_spinAxis, age * _spinRate) * basis;
        }
        else if (e.Hull == "bloom-hulk") basis = new Basis(_spinAxis, (float)time * _spinRate * 0.5f) * basis;
        else if (e.IsCreature) basis *= new Basis(Vector3.Right, Mathf.Sin((float)time * 0.8f + e.Id) * 0.08f); // a slow swimming sway

        if (_placed && !_wrecked && dt > 0f) _lastPosition = GlobalPosition;
        GlobalTransform = new Transform3D(basis, position);
        if (!_placed)
        {
            _placed = true;
            _lastPosition = position;
            ResetPhysicsInterpolation();
        }

        bool flying = e.Script != null && !_wrecked && !(e.Script.Kind == Motion.Arrive && e.Script.Arrived(time) && !e.Script.ThenOrbit);
        foreach (MeshInstance3D flare in _flares) flare.Visible = flying;

        if (_beacon != null)
        {
            _beacon.Visible = !_wrecked && Mathf.PosMod((float)time, 1.2f) < 0.45f; // a slow, anxious blink
        }
        if (_smoke != null && _wrecked && time - e.WreckedAt > 300) _smoke.Emitting = false; // old wrecks stop smoking

        if (_beam != null)
        {
            bool working = e.Script is { ThenOrbit: true } script && script.Arrived(time);
            _beam.Visible = working;
            if (working)
            {
                Vector3 site = e.Script.Center.ToGodot();
                Vector3 from = position + basis * new Vector3(0, 0, -Hull.Radius * 0.85f); // from the nose, whatever the hull
                float length = from.DistanceTo(site);
                _beam.GlobalTransform = new Transform3D(Look((site - from).Normalized(), Vector3.Up).Scaled(new Vector3(1, 1, length)), (from + site) * 0.5f);
            }
        }
    }

    void BecomeWreck(Vector3 position)
    {
        _wrecked = true;
        _wreckDrift = (position - _lastPosition) * Engine.PhysicsTicksPerSecond; // last velocity, coasted off over a second
        _wreckDrift = _wreckDrift.LimitLength(120f);
        _mesh.SetInstanceShaderParameter("damage", 1f);
        foreach (MeshInstance3D flare in _flares) flare.Visible = false;
        if (_beam != null) _beam.Visible = false;
        if (_smoke == null) AddSmoke(1f);
        else _smoke.ScaleAmountMax *= 1.6f;
        if (_placed) _fx?.Explode(position, Hull.Radius);
    }

    void AddSmoke(float strength)
    {
        _smoke = Visuals.Smoke(Mathf.Clamp(Hull.Radius, 8f, 60f) * strength);
        AddChild(_smoke);
    }

    static Basis Look(Vector3 forward, Vector3 up)
    {
        if (forward.LengthSquared() < 1e-6f) forward = Vector3.Forward;
        if (Mathf.Abs(forward.Normalized().Dot(up.Normalized())) > 0.98f) up = Vector3.Back;
        return Basis.LookingAt(forward, up.Normalized());
    }
}
