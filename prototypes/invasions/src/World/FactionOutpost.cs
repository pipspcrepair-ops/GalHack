using Godot;

namespace Invasions.World;

/// <summary>
/// Another faction's base on the map, in its colours and with its own landmark: a Kessari mining hold
/// with a derrick, a Meridian security post with a comm dish, a Reaver scrap den, a Freeholder
/// steading, a Ledger Exchange depot, or a Hollow Bloom infestation. Simple at a distance, which is
/// how it is seen. The core (a crystal on a frame, or for the Bloom a great heart) knocks it out.
/// </summary>
public partial class FactionOutpost : EnemyBaseNode
{
    protected override void Build()
    {
        Material main = Palette.Mat(Livery.Main, 0.6f), dark = Palette.Mat(Livery.Dark, 0.7f);
        Material trim = Palette.Mat(Livery.Trim, 0.5f), black = Palette.Mat(Livery.Black, 0.8f);
        var rng = new RandomNumberGenerator { Seed = Director.DeterministicRng.Hash(Id) }; // the same scrap every run
        switch (Faction)
        {
            case "kessari":
                Ring(18f, 4f, dark, main);
                // The derrick: four legs braced with brass, a drill head and a lamp on top.
                foreach (float x in new[] { -4f, 4f })
                    foreach (float z in new[] { -4f, 4f })
                        Solid(new Vector3(x * 0.7f, 15f, z * 0.7f + 4f), new Vector3(0.8f, 30f, 0.8f), main);
                for (int i = 1; i < 6; i++)
                    Palette.Box(this, new Vector3(0, i * 5.5f, 4f), new Vector3(6.2f, 0.4f, 6.2f), trim);
                Palette.Box(this, new Vector3(0, 12f, 4f), new Vector3(1.6f, 24f, 1.6f), dark);
                Lamp(new Vector3(0, 31f, 4f), Livery.Glow, 3f);
                Solid(new Vector3(-10f, 3f, 8f), new Vector3(8f, 6f, 5f), main);  // ore hoppers
                Solid(new Vector3(10f, 2.5f, 8f), new Vector3(6f, 5f, 6f), dark);
                BuildCore(new Vector3(0, 0, -6f), Livery.Glow, dark, trim);
                break;
            case "meridian":
                Ring(18f, 4f, main, dark);
                Solid(new Vector3(0, 5f, 6f), new Vector3(16f, 10f, 10f), main);
                Palette.Box(this, new Vector3(0, 7f, 0.95f), new Vector3(16.1f, 1.2f, 0.1f), trim);
                Solid(new Vector3(-9f, 9f, 9f), new Vector3(1.2f, 18f, 1.2f), dark);
                MeshInstance3D dish = Palette.Cylinder(this, new Vector3(-9f, 19f, 9f), 5f, 0.6f, main, 24);
                dish.Rotation = new Vector3(-0.9f, 0.4f, 0);
                Lamp(new Vector3(-9f, 22f, 9f), Livery.Glow, 3f);
                Lamp(new Vector3(8f, 11f, 6f), Livery.Glow, 2f);
                Palette.Box(this, new Vector3(9f, 0.1f, -8f), new Vector3(12f, 0.2f, 12f), dark);
                BuildCore(new Vector3(0, 10f, 6f), Livery.Glow, dark, trim);
                break;
            case "reavers":
                // A ring of scrap rather than walls, a tall pole with a rag of a flag.
                for (int i = 0; i < 22; i++)
                {
                    float a = i / 22f * Mathf.Tau;
                    if (Mathf.Abs(Mathf.Wrap(a - Mathf.Pi * 1.5f, -Mathf.Pi, Mathf.Pi)) < 0.35f) continue; // the gap
                    var size = new Vector3(rng.RandfRange(3f, 6f), rng.RandfRange(2f, 6f), rng.RandfRange(2f, 4f));
                    var at = new Vector3(Mathf.Cos(a) * 17f, size.Y / 2, Mathf.Sin(a) * 17f);
                    Solid(at, size, i % 3 == 0 ? trim : i % 2 == 0 ? main : black);
                }
                Solid(new Vector3(0, 3f, 6f), new Vector3(12f, 6f, 8f), main);
                Solid(new Vector3(7f, 2f, -3f), new Vector3(5f, 4f, 7f), black);
                Palette.Box(this, new Vector3(-8f, 14f, 6f), new Vector3(0.5f, 28f, 0.5f), black);
                Palette.Box(this, new Vector3(-6f, 25f, 6f), new Vector3(4f, 2.6f, 0.1f), Palette.Mat(Livery.Glow, 0.8f));
                Lamp(new Vector3(-8f, 28.5f, 6f), Livery.Glow, 3f);
                BuildCore(new Vector3(0, 6f, 6f), Livery.Glow, black, trim);
                break;
            case "freeholders":
                // A homestead: domes, a silo and a low fence, no walls worth the name.
                foreach (Vector3 at in new Vector3[] { new(-8f, 0, 4f), new(6f, 0, 6f), new(0, 0, -6f) })
                {
                    MeshInstance3D dome = Palette.Cylinder(this, at + new Vector3(0, 2.5f, 0), 5f, 5f, main, 20);
                    dome.Name = "Dome";
                    Palette.Cylinder(this, at + new Vector3(0, 5.2f, 0), 3.6f, 0.6f, trim, 20);
                    Walls.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 5f, Height = 5f }, Position = at + new Vector3(0, 2.5f, 0) });
                }
                Palette.Cylinder(this, new Vector3(12f, 7f, -6f), 2.5f, 14f, dark, 16);
                Lamp(new Vector3(12f, 14.6f, -6f), Livery.Glow, 2f);
                BuildCore(new Vector3(-14f, 0, -10f), Livery.Glow, dark, trim);
                break;
            case "exchange":
                Ring(20f, 3f, dark, main);
                // Stacked cargo containers and a gantry crane.
                for (int i = 0; i < 12; i++)
                {
                    var at = new Vector3(-12f + (i % 4) * 7f, 1.3f + (i / 4) * 2.6f, 6f + (i % 2) * 3f);
                    Solid(at, new Vector3(6f, 2.6f, 2.6f), i % 3 == 0 ? trim : main);
                }
                Solid(new Vector3(-14f, 10f, -4f), new Vector3(1f, 20f, 1f), dark);
                Solid(new Vector3(14f, 10f, -4f), new Vector3(1f, 20f, 1f), dark);
                Palette.Box(this, new Vector3(0, 20f, -4f), new Vector3(29f, 1.2f, 1.6f), main);
                Lamp(new Vector3(0, 21f, -4f), Livery.Glow, 3f);
                BuildCore(new Vector3(10f, 0, -10f), Livery.Glow, dark, trim);
                break;
            case "hollow":
                // An infestation: heaped growths, glowing pustules and tendrils, and a great heart.
                for (int i = 0; i < 14; i++)
                {
                    float a = rng.RandfRange(0f, Mathf.Tau), r = rng.RandfRange(4f, 20f);
                    var size = new Vector3(rng.RandfRange(5f, 12f), rng.RandfRange(3f, 10f), rng.RandfRange(5f, 12f));
                    MeshInstance3D lump = Palette.Box(this, new Vector3(Mathf.Cos(a) * r, size.Y * 0.4f, Mathf.Sin(a) * r), size, i % 3 == 0 ? trim : i % 2 == 0 ? main : dark);
                    lump.Rotation = new Vector3(rng.RandfRange(-0.2f, 0.2f), a, rng.RandfRange(-0.2f, 0.2f));
                    if (i % 4 == 0) Glows.Add(Palette.Box(this, lump.Position + Vector3.Up * size.Y * 0.5f, Vector3.One * 1.4f, Palette.Glow(Livery.Glow, 3f)));
                }
                for (int i = 0; i < 9; i++)
                {
                    float a = i / 9f * Mathf.Tau;
                    MeshInstance3D tendril = Palette.Box(this, new Vector3(Mathf.Cos(a) * 30f, 0.4f, Mathf.Sin(a) * 30f), new Vector3(1.2f, 0.8f, 22f), main);
                    tendril.Rotation = new Vector3(0, -a + Mathf.Pi / 2, 0);
                }
                BuildCore(new Vector3(0, 0, 0), Livery.Glow, dark, Palette.Mat(Livery.Trim, 0.4f));
                Lamp(new Vector3(0, 10f, 0), Livery.Glow, 4f);
                break;
            default:
                Ring(18f, 4f, dark, main);
                BuildCore(new Vector3(0, 0, 0), Livery.Glow, dark, trim);
                break;
        }
    }

    /// <summary>A square of wall blocks with a gate gap facing the player base (-Z local).</summary>
    void Ring(float half, float block, Material a, Material b)
    {
        int n = Mathf.RoundToInt(half / block);
        for (int i = -n; i <= n; i++)
        {
            Material m = i % 2 == 0 ? a : b;
            var size = new Vector3(block, 4f, block);
            if (Mathf.Abs(i) > 1) Solid(new Vector3(i * block, 2f, -half), size, m);
            Solid(new Vector3(i * block, 2f, half), size, m);
            Solid(new Vector3(-half, 2f, i * block), size, m);
            Solid(new Vector3(half, 2f, i * block), size, m);
        }
    }
}
