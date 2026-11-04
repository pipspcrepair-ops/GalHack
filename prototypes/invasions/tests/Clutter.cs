using System.Collections.Generic;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Model;
using Invasions.World;

namespace Invasions.Tests;

/// <summary>
/// Litters an invasion's approach and the base interior with obstacles for the navigation soak:
/// burnt-out vehicle hulks, spill crates and rubble, on the same physics layers as the real things.
/// Each obstacle is also registered with the combat world so the planner can see it.
/// </summary>
public static class Clutter
{
    public static int Scatter(Main main, RandomNumberGenerator rng, Vector3 outward, float spawnDistance)
    {
        var right = new Vector3(-outward.Z, 0, outward.X);
        BaseGrid grid = main.Model.Grid;
        int count = 0;

        // How far the base's outer wall is along this bearing (the layout is 36 m x 26 m).
        float wall = Mathf.Min(18f / Mathf.Max(0.01f, Mathf.Abs(outward.X)), 13f / Mathf.Max(0.01f, Mathf.Abs(outward.Z)));
        float clearOfSpawn = spawnDistance - 7f;

        // Vehicle hulks on the approach.
        int hulks = rng.RandiRange(2, 3);
        for (int i = 0; i < hulks; i++)
        {
            Vector3 at = outward * rng.RandfRange(wall + 8f, clearOfSpawn) + right * rng.RandfRange(-14f, 14f);
            Add(main, at, new Vector3(3.4f, 2.6f, 8f), rng.RandfRange(0, Mathf.Pi), Layers.Vehicles, Palette.Maroon.Darkened(0.3f));
            count++;
        }

        // Crates outside, and a few on the base's floor.
        for (int i = rng.RandiRange(4, 6); i > 0; i--)
        {
            Vector3 at = outward * rng.RandfRange(wall + 3f, clearOfSpawn) + right * rng.RandfRange(-20f, 20f);
            Add(main, at, Vector3.One * 1.3f, rng.RandfRange(0, Mathf.Pi), Layers.Loot, Palette.Yellow);
            count++;
        }
        var floor = new List<Cell>();
        for (int z = 1; z < main.Model.Data.Layout.Count - 1; z++)
            for (int x = 1; x < main.Model.Data.Layout[z].Length - 1; x++)
                if (grid.IsOpen(grid.FromLayout(x, z))) floor.Add(grid.FromLayout(x, z));
        for (int i = rng.RandiRange(2, 3); i > 0 && floor.Count > 0; i--)
        {
            Cell c = floor[rng.RandiRange(0, floor.Count - 1)];
            floor.Remove(c);
            Add(main, main.Combat.CellCentre(c) + new Vector3(rng.RandfRange(-0.3f, 0.3f), 0, rng.RandfRange(-0.3f, 0.3f)),
                Vector3.One * 1.3f, rng.RandfRange(0, Mathf.Pi), Layers.Loot, Palette.Yellow);
            count++;
        }

        // Rubble: some hard against the outer wall where the breach will be, the rest strewn about.
        for (int i = rng.RandiRange(6, 10); i > 0; i--)
        {
            bool atWall = i % 2 == 0;
            Vector3 at = atWall
                ? outward * (wall + rng.RandfRange(1.5f, 6f)) + right * rng.RandfRange(-10f, 10f)
                : outward * rng.RandfRange(wall + 4f, clearOfSpawn) + right * rng.RandfRange(-22f, 22f);
            var size = new Vector3(rng.RandfRange(0.8f, 2.6f), rng.RandfRange(0.5f, 1.3f), rng.RandfRange(0.8f, 2.6f));
            Add(main, at, size, rng.RandfRange(0, Mathf.Pi), Layers.World, new Color("7f3518"));
            count++;
        }
        return count;
    }

    static void Add(Main main, Vector3 at, Vector3 size, float yaw, uint layer, Color color)
    {
        var body = new StaticBody3D { Name = "Clutter", CollisionLayer = layer, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = Vector3.Up * size.Y / 2 });
        body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = Palette.Mat(color), Position = Vector3.Up * size.Y / 2 });
        main.AddChild(body);
        body.GlobalPosition = at with { Y = 0 };
        body.Rotation = new Vector3(0, yaw, 0);
        main.Combat.RegisterObstacle(at with { Y = 0 }, size, yaw);
    }
}
