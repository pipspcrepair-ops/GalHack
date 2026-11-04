using Godot;

namespace Boarding;

/// <summary>Something lying on the deck (an officer's keycard). Walk over it to take it.</summary>
public partial class Pickup : Node3D
{
    public string Item = Inventory.Keycard;
    public bool Taken { get; private set; }

    Node3D _spin;

    public override void _Ready()
    {
        _spin = new Node3D { Position = Vector3.Up * 0.45f };
        AddChild(_spin);
        _spin.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.32f, 0.2f, 0.03f)), Art.Glow(Palette.Yellow, 3f), Vector3.Zero, false));
        _spin.AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.1f, 0.06f, 0.035f)), Art.Flat(Palette.Dark), new Vector3(-0.07f, 0.03f, 0), false));
        AddChild(new OmniLight3D { LightColor = Palette.Yellow, LightEnergy = 0.8f, OmniRange = 2.5f, Position = Vector3.Up * 0.5f });
    }

    public override void _Process(double delta)
    {
        _spin.RotateY((float)delta * 2.5f);
        _spin.Position = Vector3.Up * (0.45f + 0.06f * Mathf.Sin(Time.GetTicksMsec() * 0.004f));
    }

    public void Take()
    {
        Taken = true;
        QueueFree();
    }
}
