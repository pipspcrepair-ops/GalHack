using System;
using System.Collections.Generic;
using Godot;
using Invasions.Combat;
using Invasions.Director;

namespace Invasions.World;

/// <summary>
/// A faction's base on the map, built facing the player base so its gate looks at it. It has a core:
/// shoot it out (or press K while it is the source) and the base is knocked out and sends no more
/// invasions. Whether it sends any at all is up to its faction's standing.
/// </summary>
public abstract partial class EnemyBaseNode : Node3D
{
    public string Id = "";
    public string DisplayName = "";
    /// <summary>The faction's design id (factions.json).</summary>
    public string Faction = "vharn";
    public EnemyBaseKind Kind = EnemyBaseKind.Garrison;
    public Vector3 Facing = Vector3.Forward;
    public Livery Livery;

    public bool Destroyed { get; private set; }
    public float CoreHealth01 => _coreHp / CoreMaxHp;
    public Vector3 CorePosition => Pylon.GlobalPosition + Vector3.Up * 4f;

    public event Action CoreDestroyed;

    protected virtual float CoreMaxHp => 3000f;
    float _coreHp;
    protected Node3D Pylon;
    protected StaticBody3D Walls;
    protected readonly List<Light3D> Lights = new();
    protected readonly List<MeshInstance3D> Glows = new();

    public override void _Ready()
    {
        _coreHp = CoreMaxHp;
        Rotation = new Vector3(0, Mathf.Atan2(-Facing.X, -Facing.Z), 0);
        Walls = new StaticBody3D { Name = "Walls", CollisionLayer = Layers.World, CollisionMask = 0 };
        AddChild(Walls);
        Build();
    }

    /// <summary>Builds the compound; must set <see cref="Pylon"/> with <see cref="BuildCore"/>.</summary>
    protected abstract void Build();

    protected void Solid(Vector3 centre, Vector3 size, Material m)
    {
        Palette.Box(this, centre, size, m);
        Walls.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = centre });
    }

    protected void Lamp(Vector3 at, Color color, float energy)
    {
        MeshInstance3D glow = Palette.Box(this, at, new Vector3(1f, 1f, 1f), Palette.Glow(color, 4f));
        Glows.Add(glow);
        var light = new OmniLight3D { LightColor = color, OmniRange = 18f, LightEnergy = energy, Position = at };
        AddChild(light);
        Lights.Add(light);
    }

    /// <summary>The core: a glowing crystal on a frame, with the hit box that knocks the base out.</summary>
    protected void BuildCore(Vector3 at, Color glow, Material frame, Material cap)
    {
        Pylon = new Node3D { Name = "CorePylon", Position = at };
        AddChild(Pylon);
        foreach (float x in new[] { -1.6f, 1.6f })
            foreach (float z in new[] { -1.6f, 1.6f })
                Palette.Box(Pylon, new Vector3(x, 4f, z), new Vector3(0.5f, 8f, 0.5f), frame);
        Palette.Box(Pylon, new Vector3(0, 8.2f, 0), new Vector3(4f, 0.5f, 4f), cap);
        MeshInstance3D crystal = Palette.Box(Pylon, new Vector3(0, 4f, 0), new Vector3(1.6f, 7f, 1.6f), Palette.Glow(glow, 3f));
        crystal.Rotation = new Vector3(0, Mathf.Pi / 4, 0);
        Glows.Add(crystal);
        var coreLight = new OmniLight3D { LightColor = glow, OmniRange = 28f, LightEnergy = 3f, Position = new Vector3(0, 4f, 0) };
        Pylon.AddChild(coreLight);
        Lights.Add(coreLight);

        HitBox core = HitBox.Create(new BoxShape3D { Size = new Vector3(3.6f, 8f, 3.6f) }, new Vector3(0, 4f, 0),
            Layers.InvaderHitboxes, Team.Invaders, () => !Destroyed, (amount, from, by) =>
            {
                _coreHp -= amount;
                if (_coreHp <= 0f) DestroyCore();
            });
        Pylon.AddChild(core);
    }

    /// <summary>Knocks the base out: the core blows, its lights die and it smokes from then on.</summary>
    public void DestroyCore()
    {
        if (Destroyed) return;
        Destroyed = true;
        _coreHp = 0f;
        Vector3 at = Pylon.GlobalPosition + Vector3.Up * 4f;
        Fx.Explosion(at, 9f);
        Fx.Debris(at, Livery?.Main ?? Palette.Oxblood, 24, 16f, 1.2f);
        foreach (Light3D light in Lights) light.Visible = false;
        foreach (MeshInstance3D glow in Glows) glow.MaterialOverride = Palette.Mat(Palette.Black);
        Pylon.Scale = new Vector3(1f, 0.35f, 1f);
        Fx.SmokeColumn(this, Pylon.Position + new Vector3(0, 1f, 0), 3f);
        CoreDestroyed?.Invoke();
    }
}

/// <summary>
/// The Vharn garrison about 600 m from the player base. Black block walls, two watchtowers, a landing
/// pad, a mast, and a crimson core pylon on the command block.
/// </summary>
public partial class Garrison : EnemyBaseNode
{
    public Garrison()
    {
        Id = "okkar";
        DisplayName = "Garrison Okkar-9";
        Faction = "vharn";
    }

    protected override void Build()
    {
        Material black = Palette.Mat(Palette.Black, 0.75f);
        Material gun = Palette.Mat(Palette.Gunmetal, 0.5f, 0.3f);
        Material red = Palette.Mat(Palette.Oxblood, 0.6f);
        Material bone = Palette.Mat(Palette.Bone, 0.5f);
        Material check = Palette.Textured(Palette.VharnCheck, new Vector3(8f, 2f, 1f));

        // Perimeter wall blocks (4 m cubes) with a gate gap facing the player base (-Z local).
        for (int i = -6; i <= 6; i++)
        {
            foreach (float z in new[] { -26f, 26f })
                if (!(z < 0 && Mathf.Abs(i) <= 1)) Solid(new Vector3(i * 4f, 2.5f, z), new Vector3(4f, 5f, 4f), i % 2 == 0 ? black : gun);
            Solid(new Vector3(-26f, 2.5f, i * 4f), new Vector3(4f, 5f, 4f), i % 2 == 0 ? gun : black);
            Solid(new Vector3(26f, 2.5f, i * 4f), new Vector3(4f, 5f, 4f), i % 2 == 0 ? gun : black);
        }
        // An oxblood band along the top of the wall.
        foreach (float z in new[] { -26f, 26f })
            Palette.Box(this, new Vector3(0, 5.15f, z), new Vector3(52f, 0.3f, 4.1f), red);
        foreach (float x in new[] { -26f, 26f })
            Palette.Box(this, new Vector3(x, 5.15f, 0), new Vector3(4.1f, 0.3f, 52f), red);

        // Watchtowers at the front corners, with checker crowns and red lamps.
        foreach (float x in new[] { -24f, 24f })
        {
            Solid(new Vector3(x, 12f, -24f), new Vector3(6f, 24f, 6f), black);
            Solid(new Vector3(x, 25f, -24f), new Vector3(8f, 3f, 8f), red);
            Palette.Box(this, new Vector3(x, 22.5f, -24f), new Vector3(6.1f, 2f, 6.1f), check);
            Lamp(new Vector3(x, 27f, -24f), Palette.SirenRed, 3f);
        }

        // Command block with the core pylon on top.
        Solid(new Vector3(0, 6f, 6f), new Vector3(18f, 12f, 14f), gun);
        Solid(new Vector3(0, 12.5f, 6f), new Vector3(14f, 1f, 10f), red);
        Palette.Box(this, new Vector3(0, 8f, -1.05f), new Vector3(18.1f, 2.5f, 0.1f), check);
        Palette.Box(this, new Vector3(0, 4f, -1.1f), new Vector3(6f, 8f, 0.1f), Palette.Mat(Palette.Maroon));
        Solid(new Vector3(-12f, 3f, 14f), new Vector3(6f, 6f, 6f), black);
        Solid(new Vector3(12f, 4f, 16f), new Vector3(5f, 8f, 5f), black);
        Palette.Box(this, new Vector3(12f, 14f, 16f), new Vector3(0.4f, 12f, 0.4f), gun); // mast
        Lamp(new Vector3(12f, 20.3f, 16f), Palette.SirenRed, 2f);

        BuildCore(new Vector3(0, 13f, 6f), Palette.SirenRed, black, red);

        // A landing pad inside the walls, where dropships would sit.
        Palette.Box(this, new Vector3(-10f, 0.1f, -10f), new Vector3(14f, 0.2f, 14f), Palette.Mat(Palette.Gunmetal, 0.9f));
        Palette.Box(this, new Vector3(-10f, 0.22f, -10f), new Vector3(10f, 0.05f, 1f), bone);
        Palette.Box(this, new Vector3(-10f, 0.22f, -10f), new Vector3(1f, 0.05f, 10f), bone);
    }
}
