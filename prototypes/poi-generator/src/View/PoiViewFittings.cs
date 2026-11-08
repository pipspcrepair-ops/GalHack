using Godot;
using PoiGenerator.Gen;

namespace PoiGenerator;

public partial class PoiView
{
    /// <summary>
    /// The small fittings no block can be: each house's service fitting (<see cref="ServicePoint"/>), an insulator on a
    /// short bracket at its roof's corner, drawn from its parts, and only where what holds it still stands.
    /// </summary>
    void BuildFittings()
    {
        foreach (ServicePoint p in Poi.Extras.Service)
        {
            if (Poi.Blocks.Get(p.Anchor).IsAir) continue;
            var root = new Node3D { Name = "Service fitting" };
            foreach (var (x, y, z, w, h, d, round, c) in p.Parts())
                root.AddChild(new MeshInstance3D
                {
                    Mesh = round ? new CylinderMesh { TopRadius = w / 2f, BottomRadius = w / 2f, Height = h, RadialSegments = 10, Rings = 1 } : new BoxMesh { Size = new Vector3(w, h, d) },
                    Position = new Vector3(x, y, z),
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = Look.ToColor(c), Roughness = round ? 0.25f : 0.5f, Metallic = round ? 0f : 0.5f },
                });
            AddChild(root);
        }
    }
}
