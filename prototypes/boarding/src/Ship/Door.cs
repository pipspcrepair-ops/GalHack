using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>
/// A door block. Ordinary doors slide open for anyone nearby. Security doors and the
/// airlock are locked and open three ways: a quiet hack (hold E), a loud breaching charge
/// (which simply destroys them), or an officer's keycard (silent and quick). Bulkheads stand
/// open until the ship seals them, and then behave exactly like a locked security door.
///
/// Opened security doors stay open. The panel slides up into the ceiling, carrying its
/// collision with it, and never closes on someone standing in the doorway.
/// </summary>
public partial class Door : Block
{
    public bool Locked { get; private set; }
    public bool Sealed { get; private set; }
    public float HackProgress { get; private set; }
    public float KeyProgress { get; private set; }
    public float OpenAmount { get; private set; }

    /// <summary>True if the panel spans the X axis (the passage runs along Z).</summary>
    public bool SpansX;

    public bool IsSecurity => Kind is BlockKind.SecurityDoor or BlockKind.Airlock or BlockKind.Bulkhead;
    public bool IsOpen => Destroyed || OpenAmount > 0.9f;

    /// <summary>Whether a path may go through this door right now.</summary>
    public bool Passable => Destroyed || !Locked;

    Node3D _panel;
    CollisionShape3D _shape;
    readonly System.Collections.Generic.List<MeshInstance3D> _lockLights = new();
    float _deckHeight;

    public void Setup(bool locked, float deckHeight)
    {
        Locked = locked;
        _deckHeight = deckHeight;
        // Bulkheads start open; everything else starts shut.
        OpenAmount = Kind == BlockKind.Bulkhead ? 1f : 0f;
    }

    protected override void BuildVisual()
    {
        float thickness = IsSecurity ? 0.6f : 0.3f;
        Vector3 panelSize = SpansX ? new Vector3(Size.X - 0.2f, Size.Y, thickness) : new Vector3(thickness, Size.Y, Size.Z - 0.2f);

        _panel = new Node3D { Name = "Panel" };
        AddChild(_panel);
        Material face = Kind switch
        {
            BlockKind.SecurityDoor => Art.Stripes(Palette.Yellow, Palette.Dark, 0.35f),
            BlockKind.Airlock => Art.Stripes(Palette.Bone, Palette.Oxblood, 0.45f),
            BlockKind.Bulkhead => Art.Stripes(Palette.Orange, Palette.Dark, 0.5f),
            _ => Art.Flat(Palette.Teal, 0.6f, 0.2f),
        };
        _panel.AddChild(Art.Mesh(Art.BoxMesh(panelSize), face, Vector3.Zero));

        // A thick dark frame face on both sides makes the panel read as a door, not a wall.
        Vector3 across = SpansX ? Vector3.Right : Vector3.Back;
        Vector3 normal = SpansX ? Vector3.Back : Vector3.Right;
        var trim = Art.Flat(Palette.Dark, 0.5f, 0.4f);
        foreach (float side in new[] { -1f, 1f })
        {
            var window = SpansX ? new Vector3(0.7f, 0.25f, 0.04f) : new Vector3(0.04f, 0.25f, 0.7f);
            _panel.AddChild(Art.Mesh(Art.BoxMesh(window), Art.Glow(Palette.Lamp, 0.6f),
                normal * side * (thickness * 0.5f + 0.01f) + Vector3.Up * 0.55f, false));
            var jamb = SpansX ? new Vector3(0.18f, Size.Y, Size.Z * 0.9f) : new Vector3(Size.X * 0.9f, Size.Y, 0.18f);
            AddChild(Art.Mesh(Art.BoxMesh(jamb), trim, across * side * (Size.Dot(across.Abs()) * 0.5f - 0.09f)));
        }

        if (IsSecurity)
        {
            foreach (float side in new[] { -1f, 1f })
            {
                var light = Art.Mesh(Art.BoxMesh(new Vector3(0.22f, 0.22f, 0.22f)), LockMaterial(),
                    Vector3.Up * 0.2f + normal * side * (thickness * 0.5f + 0.05f), false);
                _panel.AddChild(light);
                _lockLights.Add(light);
            }
        }
        PlacePanel();
    }

    protected override void BuildShape()
    {
        float thickness = IsSecurity ? 0.6f : 0.3f;
        Vector3 size = SpansX ? new Vector3(Size.X, Size.Y, thickness) : new Vector3(thickness, Size.Y, Size.Z);
        _shape = new CollisionShape3D { Shape = new BoxShape3D { Size = size } };
        AddChild(_shape);
        PlacePanel();
    }

    /// <summary>Called by the ship every physics tick with everyone's ship-local feet.</summary>
    public void Tick(float dt, IReadOnlyList<Vector3> actorsLocal, float openRadius)
    {
        if (Destroyed) return;
        bool someoneHere = false;
        foreach (Vector3 actor in actorsLocal)
            if (new Vector2(actor.X - Position.X, actor.Z - Position.Z).Length() < openRadius) someoneHere = true;

        bool wantOpen = Kind == BlockKind.Door ? someoneHere : !Locked;
        // Never shut on someone: a closing door waits for the doorway to clear.
        if (!wantOpen && someoneHere && OpenAmount > 0f) wantOpen = true;
        float target = wantOpen ? 1f : 0f;
        if (Mathf.IsEqualApprox(OpenAmount, target)) return;
        OpenAmount = Mathf.MoveToward(OpenAmount, target, dt * (IsSecurity ? 1.6f : 3f));
        PlacePanel();
    }

    void PlacePanel()
    {
        float lift = OpenAmount * (_deckHeight - 0.35f);
        if (_panel != null) _panel.Position = Vector3.Up * lift;
        if (_shape != null) _shape.Position = Vector3.Up * lift;
    }

    /// <summary>Hold E on it. Returns true on the tick it unlocks. Makes no noise.</summary>
    public bool Hack(float dt, float seconds)
    {
        if (!Locked || Destroyed) return false;
        HackProgress += dt;
        if (HackProgress < seconds) return false;
        Unlock();
        return true;
    }

    /// <summary>Swipe an officer's keycard. Quicker than a hack, and just as silent.</summary>
    public bool Swipe(float dt, float seconds)
    {
        if (!Locked || Destroyed) return false;
        KeyProgress += dt;
        if (KeyProgress < seconds) return false;
        Unlock();
        return true;
    }

    public void Unlock()
    {
        Locked = false;
        Sealed = false;
        HackProgress = 0f;
        KeyProgress = 0f;
        RefreshLockLights();
    }

    /// <summary>The ship slams a bulkhead shut and locks it.</summary>
    public void Seal()
    {
        if (Destroyed) return;
        Sealed = true;
        Locked = true;
        HackProgress = 0f;
        KeyProgress = 0f;
        RefreshLockLights();
    }

    /// <summary>Lets go of a half-finished hack or swipe (the player looked away).</summary>
    public void ResetProgress()
    {
        HackProgress = 0f;
        KeyProgress = 0f;
    }

    void RefreshLockLights()
    {
        foreach (MeshInstance3D light in _lockLights) light.MaterialOverride = LockMaterial();
    }

    Material LockMaterial() => Art.Glow(Locked ? Palette.AlarmRed : Palette.TealGlow, 3f);
}
