using System.Collections.Generic;
using Encounters.Director;
using Godot;

namespace Encounters;

/// <summary>
/// Presentation-only effects: gunfire across a dogfight and flashes when a ship is lost.
/// Pooled, so a busy fight allocates nothing. None of it touches the director: the fight's
/// outcome is already scripted, this only makes it read as one.
/// </summary>
public partial class Fx : Node3D
{
    sealed class Bolt
    {
        public MeshInstance3D Mesh;
        public Vector3 Velocity;
        public float Life;
    }

    sealed class Flash
    {
        public MeshInstance3D Mesh;
        public StandardMaterial3D Material;
        public float Age, Size;
    }

    readonly List<Bolt> _bolts = new();
    readonly List<Flash> _flashes = new();
    readonly Dictionary<string, StandardMaterial3D> _boltMaterials = new();
    readonly RandomNumberGenerator _rng = new() { Seed = 77 };
    readonly List<EntityView> _sideA = new(), _sideB = new();
    float _gunTimer;

    // Tracers are slowed right down from real shells so the eye can follow them across a fight.
    const float BoltSpeed = 650f, FlashLife = 1.4f;

    public int BoltsInFlight
    {
        get
        {
            int n = 0;
            foreach (Bolt bolt in _bolts)
                if (bolt.Life > 0f) n++;
            return n;
        }
    }

    public void Clear()
    {
        foreach (Bolt bolt in _bolts) bolt.Life = 0f;
        foreach (Flash flash in _flashes) flash.Age = FlashLife;
    }

    /// <summary>Called every physics tick with the views of the current zone.</summary>
    public void Step(float dt, ZoneInstance zone, IReadOnlyDictionary<long, EntityView> views)
    {
        _gunTimer -= dt;
        if (zone != null && _gunTimer <= 0f)
        {
            _gunTimer = 0.05f;
            foreach (Encounter enc in zone.Encounters)
                if (enc.Def.Behaviour == Behaviour.Dogfight && enc.State == EncounterState.Active)
                {
                    Exchange(enc, views);
                    Exchange(enc, views);
                }
        }

        foreach (Bolt bolt in _bolts)
        {
            if (bolt.Life <= 0f) continue;
            bolt.Life -= dt;
            bolt.Mesh.Position += bolt.Velocity * dt;
            bolt.Mesh.Visible = bolt.Life > 0f;
        }
        foreach (Flash flash in _flashes)
        {
            if (flash.Age >= FlashLife) continue;
            flash.Age += dt;
            float t = Mathf.Clamp(flash.Age / FlashLife, 0f, 1f);
            flash.Mesh.Scale = Vector3.One * flash.Size * (0.4f + 2.2f * Mathf.Sqrt(t));
            float heat = (1f - t) * (1f - t);
            flash.Material.AlbedoColor = new Color(5f * heat, 2.2f * heat, 0.7f * heat, 1f);
            flash.Mesh.Visible = flash.Age < FlashLife;
        }
    }

    /// <summary>A few ships on each side take a shot at their nearest opponent.</summary>
    void Exchange(Encounter enc, IReadOnlyDictionary<long, EntityView> views)
    {
        _sideA.Clear();
        _sideB.Clear();
        foreach (WorldEntity ship in enc.Ships)
            if (ship.Live && views.TryGetValue(ship.Id, out EntityView view))
                (ship.Side == "b" ? _sideB : _sideA).Add(view);
        if (_sideA.Count == 0 || _sideB.Count == 0) return;

        bool fromA = _rng.Randf() < 0.5f;
        List<EntityView> shooters = fromA ? _sideA : _sideB, targets = fromA ? _sideB : _sideA;
        EntityView shooter = shooters[_rng.RandiRange(0, shooters.Count - 1)];
        EntityView target = null;
        float best = 900f;
        foreach (EntityView t in targets)
        {
            float d = t.GlobalPosition.DistanceTo(shooter.GlobalPosition);
            if (d < best) (best, target) = (d, t);
        }
        if (target == null) return;
        Vector3 nose = shooter.GlobalTransform * new Vector3(0, 0, -shooter.Hull.Radius * 0.8f);
        Vector3 aim = target.GlobalPosition + new Vector3(_rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1), _rng.RandfRange(-1, 1)) * target.Hull.Radius;
        Shoot(nose, aim, shooter.Entity.Faction);
    }

    public void Shoot(Vector3 from, Vector3 to, string faction)
    {
        Bolt bolt = null;
        foreach (Bolt b in _bolts)
            if (b.Life <= 0f) { bolt = b; break; }
        if (bolt == null)
        {
            if (_bolts.Count >= 160) return;
            bolt = new Bolt
            {
                Mesh = new MeshInstance3D
                {
                    Mesh = new BoxMesh { Size = new Vector3(1.4f, 1.4f, 40f) },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                },
            };
            AddChild(bolt.Mesh);
            _bolts.Add(bolt);
        }
        Vector3 dir = (to - from).Normalized();
        bolt.Mesh.MaterialOverride = BoltMaterial(faction);
        bolt.Mesh.GlobalTransform = new Transform3D(Basis.LookingAt(dir, Mathf.Abs(dir.Y) > 0.98f ? Vector3.Back : Vector3.Up), from);
        bolt.Mesh.ResetPhysicsInterpolation();
        bolt.Velocity = dir * BoltSpeed;
        bolt.Life = Mathf.Min(from.DistanceTo(to) / BoltSpeed, 1.2f);
        bolt.Mesh.Visible = true;
    }

    public void Explode(Vector3 at, float size)
    {
        Flash flash = null;
        foreach (Flash f in _flashes)
            if (f.Age >= FlashLife) { flash = f; break; }
        if (flash == null)
        {
            var material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                BlendMode = BaseMaterial3D.BlendModeEnum.Add,
                DisableReceiveShadows = true,
            };
            flash = new Flash
            {
                Material = material,
                Mesh = new MeshInstance3D
                {
                    Mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 16, Rings = 8 },
                    MaterialOverride = material,
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                },
            };
            AddChild(flash.Mesh);
            _flashes.Add(flash);
        }
        flash.Age = 0f;
        flash.Size = Mathf.Max(size, 10f);
        flash.Mesh.GlobalPosition = at;
        flash.Mesh.ResetPhysicsInterpolation();
        flash.Mesh.Visible = true;
    }

    StandardMaterial3D BoltMaterial(string faction)
    {
        if (_boltMaterials.TryGetValue(faction, out StandardMaterial3D m)) return m;
        Color c = Palette.Faction(faction);
        m = Visuals.Bolt(new Color(c.R * 1.8f, c.G * 1.8f, c.B * 1.8f)); // bright enough to bloom, not so bright it bleaches
        _boltMaterials[faction] = m;
        return m;
    }
}
