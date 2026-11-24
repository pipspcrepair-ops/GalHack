using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;

namespace PhysicsSandbox.World;

/// <summary>Draws a vessel's landing legs at their current extension: a strut and a foot per leg. Presentation only.</summary>
public partial class GearView : Node3D
{
    Ship _ship;
    MeshInstance3D[] _struts, _feet;

    public void Setup(Ship ship)
    {
        _ship = ship;
        LandingGear gear = ship.Vessel?.Gear;
        int n = gear?.Count ?? 0;
        _struts = new MeshInstance3D[n];
        _feet = new MeshInstance3D[n];
        var dark = new StandardMaterial3D { AlbedoColor = Palette.Dark, Roughness = 0.5f, Metallic = 0.4f };
        var pad = new StandardMaterial3D { AlbedoColor = Palette.Yellow, Roughness = 0.7f };
        for (int i = 0; i < n; i++)
        {
            _struts[i] = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 1f }, MaterialOverride = dark };
            _feet[i] = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.18f, BottomRadius = 0.24f, Height = 0.08f }, MaterialOverride = pad };
            AddChild(_struts[i]);
            AddChild(_feet[i]);
        }
        Refresh();
    }

    public void Refresh()
    {
        LandingGear gear = _ship?.Vessel?.Gear;
        if (gear == null) return;
        for (int i = 0; i < gear.Count; i++)
        {
            GearLeg leg = gear.Leg(i);
            float reach = Mathf.Max(0.05f, gear.Reach(i)); // the leg telescopes onto its own ground
            Vector3 hip = leg.Hip.ToGodot();
            _struts[i].Position = hip + Vector3.Down * (reach * 0.5f);
            _struts[i].Scale = new Vector3(1f, reach, 1f);
            _feet[i].Position = hip + Vector3.Down * reach;
            _feet[i].Visible = gear.Extension > 0.05f;
            _struts[i].Visible = gear.Extension > 0.05f;
        }
    }
}
