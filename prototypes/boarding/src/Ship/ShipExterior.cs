using Godot;

namespace Boarding;

/// <summary>
/// The ship's silhouette: bridge tower, dorsal spine, engine pods, sponsons, landing feet
/// and the Vharn livery. Solid but indestructible (it isn't on the block grid) and placed
/// relative to the layout's footprint, so a different layout gets the same treatment.
/// </summary>
public partial class Ship
{
    StaticBody3D _superstructure;

    void BuildExterior()
    {
        _superstructure = new StaticBody3D { Name = "Superstructure", CollisionLayer = Layers.Ship, CollisionMask = 0 };
        _superstructure.SetMeta("surface", (int)Surface.Metal);
        AddChild(_superstructure);

        Aabb b = Layout.Bounds;
        float roof = Deck + 0.6f;
        float hullLeft = HullEdgeX(-1), hullRight = HullEdgeX(1);
        float aft = b.End.Z;                         // the stern hull's outer face
        float bow = b.Position.Z + CellSize;         // the bridge's front face

        var bone = Art.Flat(Palette.Bone, 0.7f);
        var oxblood = Art.Flat(Palette.Oxblood, 0.7f);
        var dark = Art.Flat(Palette.Dark, 0.6f, 0.3f);
        var yellow = Art.Flat(Palette.Yellow, 0.6f);
        var tower = Art.Banded("tower", 2.6f, (0f, Palette.Oxblood), (0.18f, Palette.Bone), (0.62f, Palette.Dark), (0.82f, Palette.Bone));
        var pod = Art.Banded("pod", 6.5f, (0f, Palette.Dark), (0.08f, Palette.Oxblood), (0.45f, Palette.Yellow), (0.5f, Palette.Bone), (0.85f, Palette.Dark));

        // Bridge tower over the bow, with a wide amber window band looking forward.
        Vector2 bridge = CellsX(2);
        Solid(new Vector3(0, roof + 1.3f, bow + 3.4f), new Vector3(bridge.Y - bridge.X - 1.5f, 2.6f, 6.8f), tower);
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(bridge.Y - bridge.X - 3f, 0.55f, 0.06f)), Art.Glow(new Color("ffb347"), 1.6f),
            new Vector3(0, roof + 1.65f, bow - 0.03f), false));
        foreach (float x in new[] { -4.5f, 3.5f })
        {
            Solid(new Vector3(x, roof + 4.3f, bow + 3.5f), new Vector3(0.3f, 3.6f, 0.3f), dark);
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.35f, 0.35f, 0.35f)), Art.Glow(Palette.AlarmRed, 4f), new Vector3(x, roof + 6.2f, bow + 3.5f), false));
        }
        // Prow wedge under the bridge window.
        Solid(new Vector3(0, 1.1f, bow - 1.0f), new Vector3(bridge.Y - bridge.X - 4f, 2.2f, 2f), oxblood);
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(bridge.Y - bridge.X - 4.2f, 0.3f, 2.05f)), yellow, new Vector3(0, 2.0f, bow - 1.0f)));

        // Dorsal spine with a hazard-yellow crest and checker flashes.
        float spineStart = b.Position.Z + 6 * CellSize, spineEnd = aft - 3f;
        float spineLength = spineEnd - spineStart, spineMid = (spineStart + spineEnd) * 0.5f;
        Solid(new Vector3(0, roof + 0.8f, spineMid), new Vector3(8f, 1.6f, spineLength), bone);
        AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.2f, 0.12f, spineLength)), yellow, new Vector3(0, roof + 1.66f, spineMid)));
        foreach (float side in new[] { -1f, 1f })
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(0.06f, 1.2f, 6f)), Art.Checker(Palette.Dark, Palette.Bone, 0.4f),
                new Vector3(side * 4.03f, roof + 0.8f, spineStart + 4f)));
        Solid(new Vector3(0, roof + 2.2f, spineEnd - 4f), new Vector3(2.4f, 1.4f, 2.4f), dark);   // sensor dome base
        AddChild(Art.Mesh(new SphereMesh { Radius = 1.4f, Height = 1.6f, IsHemisphere = true }, Art.Flat(Palette.Teal, 0.3f, 0.4f),
            new Vector3(0, roof + 2.9f, spineEnd - 4f)));

        // Engine pods at the stern, sitting on the ground: the ship is landed and cold-idling.
        foreach (float side in new[] { -1f, 1f })
        {
            float x = side * (hullRight - 9f);
            Solid(new Vector3(x, 3.25f, aft + 6.5f), new Vector3(10f, 6.5f, 13f), pod);
            for (int i = -1; i <= 1; i++)
            {
                AddChild(Art.Mesh(Art.BoxMesh(new Vector3(2.4f, 2.4f, 0.1f)), dark, new Vector3(x + i * 3f, 3.4f, aft + 13.05f), false));
                AddChild(Art.Mesh(Art.BoxMesh(new Vector3(1.5f, 1.5f, 0.1f)), Art.Glow(Palette.Orange, 1.4f), new Vector3(x + i * 3f, 3.4f, aft + 13.12f), false));
            }
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(10.1f, 0.5f, 3f)), Art.Checker(Palette.Yellow, Palette.Dark, 0.5f), new Vector3(x, 6.3f, aft + 2f)));
        }
        Solid(new Vector3(0, 2.4f, aft + 4.5f), new Vector3(8f, 4.8f, 9f), oxblood);

        // Sponsons along both flanks, and landing feet under them.
        foreach (float side in new[] { -1f, 1f })
        {
            float edge = side < 0 ? hullLeft : hullRight;
            float z = b.Position.Z + 12 * CellSize + 3f;
            Solid(new Vector3(edge + side * 1.4f, 3.0f, z), new Vector3(2.8f, 1.3f, 12f), oxblood);
            AddChild(Art.Mesh(Art.BoxMesh(new Vector3(2.85f, 0.18f, 12.05f)), yellow, new Vector3(edge + side * 1.4f, 3.7f, z)));
            foreach (float fz in new[] { z - 4f, z + 4f })
            {
                Solid(new Vector3(edge + side * 2.0f, 1.2f, fz), new Vector3(0.6f, 2.4f, 0.6f), dark);
                Solid(new Vector3(edge + side * 2.0f, 0.2f, fz), new Vector3(2.4f, 0.4f, 2.4f), dark);
            }
        }

        PaintHull();
    }

    /// <summary>Livery painted onto individual hull blocks, so it blows away with them.</summary>
    void PaintHull()
    {
        foreach (var (cell, block) in Blocks)
        {
            if (block.Kind != BlockKind.Hull) continue;
            foreach (Vector2I n in ShipLayout.Neighbours4)
            {
                if (Layout[cell + n] != CellKind.Outside) continue;
                var outward = new Vector3(n.X, 0, n.Y);
                var face = outward * (CellSize * 0.5f + 0.02f);
                var along = n.X != 0 ? new Vector3(0.04f, 0f, CellSize) : new Vector3(CellSize, 0f, 0.04f);

                // Black-and-bone checker flashes on alternating flank cells near the bow and stern.
                bool flank = n.X != 0 && (cell.Y is 6 or 8 or 17 or 19);
                if (flank)
                    block.AddDecor(Art.Mesh(Art.BoxMesh(along + Vector3.Up * 1.0f), Art.Checker(Palette.Dark, Palette.Bone, 0.25f),
                        face + Vector3.Up * 0.65f, false));

                // A red waterline stripe all the way round.
                block.AddDecor(Art.Mesh(Art.BoxMesh(along + Vector3.Up * 0.22f), Art.Flat(Palette.Red, 0.6f),
                    face + Vector3.Up * -0.95f, false));
            }
        }
    }

    /// <summary>Indestructible solid detailing: collision on the ship layer plus a mesh.</summary>
    void Solid(Vector3 center, Vector3 size, Material material)
    {
        _superstructure.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = center });
        AddChild(Art.Mesh(Art.Box(Vector3.Zero, size), material, center));
    }

    /// <summary>The outer face of the main hull on one side (-1 port, +1 starboard), in ship-local X.</summary>
    float HullEdgeX(int side)
    {
        int row = Layout.Height / 2;
        if (side < 0)
        {
            for (int c = 0; c < Layout.Width; c++)
                if (Layout.IsShip(new Vector2I(c, row))) return Layout.CellCenter(new Vector2I(c, row)).X - CellSize * 0.5f;
        }
        else
        {
            for (int c = Layout.Width - 1; c >= 0; c--)
                if (Layout.IsShip(new Vector2I(c, row))) return Layout.CellCenter(new Vector2I(c, row)).X + CellSize * 0.5f;
        }
        return 0f;
    }

    /// <summary>X extent (min, max) of the ship cells in a row.</summary>
    Vector2 CellsX(int row)
    {
        float min = float.MaxValue, max = float.MinValue;
        for (int c = 0; c < Layout.Width; c++)
        {
            if (!Layout.IsShip(new Vector2I(c, row))) continue;
            float x = Layout.CellCenter(new Vector2I(c, row)).X;
            min = Mathf.Min(min, x - CellSize * 0.5f);
            max = Mathf.Max(max, x + CellSize * 0.5f);
        }
        return new Vector2(min, max);
    }
}
