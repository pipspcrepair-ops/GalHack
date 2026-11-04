using System.Collections.Generic;
using Godot;

namespace WalkableShip;

/// <summary>
/// A loop of glowing rings to fly the ship through, in order. The next ring glows amber,
/// passed rings turn teal. Backspace resets the course.
/// </summary>
public partial class RingCourse : Node3D
{
    public Ship Ship;

    public int Passed { get; private set; }
    public int Count => _rings.Count;
    public float Elapsed { get; private set; }
    public bool Finished => Passed >= Count;
    public float? BestTime { get; private set; }

    const float RingRadius = 16f;
    const int RingCount = 10;

    readonly List<(Vector3 center, Vector3 normal, MeshInstance3D mesh)> _rings = new();
    StandardMaterial3D _idle, _next, _done;
    Vector3 _lastShipPosition;
    bool _running;

    public override void _Ready()
    {
        _idle = Glow(new Color("e9dfc4"), 0.3f);
        _next = Glow(ShipLayout.Yellow, 4f);
        _done = Glow(ShipLayout.Teal, 1.5f);

        for (int i = 0; i < RingCount; i++)
        {
            float a = i / (float)RingCount * Mathf.Tau;
            Vector3 center = CoursePoint(a);
            Vector3 normal = (CoursePoint(a + 0.01f) - center).Normalized();
            var mesh = new MeshInstance3D
            {
                Mesh = new TorusMesh { InnerRadius = RingRadius - 1.4f, OuterRadius = RingRadius, Rings = 48, RingSegments = 12 },
            };
            AddChild(mesh);
            // A torus lies flat with its hole along Y; tip it up so the hole faces along the course.
            mesh.GlobalTransform = new Transform3D(Basis.LookingAt(normal) * new Basis(Vector3.Right, Mathf.Pi / 2f), center);
            _rings.Add((center, normal, mesh));
        }

        _lastShipPosition = Ship.GlobalPosition;
        Refresh();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, PhysicalKeycode: Key.Backspace }) Reset();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_running && !Finished) Elapsed += (float)delta;

        Vector3 now = Ship.GlobalPosition;
        if (!Finished)
        {
            var (center, normal, _) = _rings[Passed];
            float before = (_lastShipPosition - center).Dot(normal);
            float after = (now - center).Dot(normal);
            if (before < 0f && after >= 0f)
            {
                Vector3 crossing = _lastShipPosition.Lerp(now, before / (before - after));
                if (crossing.DistanceTo(center) < RingRadius)
                {
                    Passed++;
                    _running = true;
                    if (Finished && (BestTime == null || Elapsed < BestTime)) BestTime = Elapsed;
                    Refresh();
                }
            }
        }
        _lastShipPosition = now;
    }

    public void Reset()
    {
        Passed = 0;
        Elapsed = 0f;
        _running = false;
        Refresh();
    }

    static Vector3 CoursePoint(float a) =>
        new(Mathf.Cos(a) * 450f, 50f + 35f * Mathf.Sin(a * 2f), Mathf.Sin(a) * 450f - 520f);

    void Refresh()
    {
        for (int i = 0; i < _rings.Count; i++)
            _rings[i].mesh.MaterialOverride = i < Passed ? _done : i == Passed ? _next : _idle;
    }

    static StandardMaterial3D Glow(Color color, float energy) => new()
    {
        AlbedoColor = color,
        EmissionEnabled = true,
        Emission = color,
        EmissionEnergyMultiplier = energy,
    };
}
