using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// Creatures for a voxel cave or a dug tunnel (the planet's caves): it finds the cave's floors,
/// walls and ceilings round a point, puts cracks low in its walls and moss on its floors, cave
/// crawlers on its ceilings and subsisters at the moss, so a cave has the same small food chain
/// as a place. This is what the planet surface calls for a cave mouth near the player.
/// </summary>
public static class CaveFauna
{
    public static List<Creature> Populate(WildSim sim, ISolidSpace space, DeepFauna fauna, Vector3 center, float radius, ulong seed, int depth = 1, int crawlers = 2, int subsisters = 4, Func<Vector3, bool> allow = null, IReadOnlyList<Species> dark = null)
    {
        var rng = Rng.For(seed, "cave-fauna");
        var made = new List<Creature>();
        HashSet<Vector3I> cells = SpaceNav.Reach(space, center, true, 1, 6000);
        var floors = new List<Vector3I>();
        var ceilings = new List<Vector3I>();
        var walls = new List<Vector3I>();
        foreach (Vector3I c in cells)
        {
            if ((SpaceMath.Center(c) - center).Length() > radius) continue;
            bool below = space.SolidCell(c.X, c.Y - 1, c.Z), above = space.SolidCell(c.X, c.Y + 1, c.Z);
            if (below && !space.SolidCell(c.X, c.Y + 1, c.Z) && !space.SolidCell(c.X, c.Y + 2, c.Z)) floors.Add(c);
            else if (above && !below && !space.SolidCell(c.X, c.Y - 1, c.Z) && !space.SolidCell(c.X, c.Y - 2, c.Z)) ceilings.Add(c);
            else if (!below && !above) walls.Add(c);
        }
        Sort(floors);
        Sort(ceilings);
        Sort(walls);
        // Filled with someone already near, nothing goes down beside them or where they are looking.
        if (allow != null)
        {
            floors.RemoveAll(f => !allow(SpaceMath.Center(f)));
            ceilings.RemoveAll(k => !allow(SpaceMath.Center(k)));
        }
        if (floors.Count == 0) return made;

        // Cracks low in the walls (one network: they join up through the rock), moss on the floor.
        int crackCount = Math.Min(walls.Count, 4);
        for (int i = 0; i < crackCount; i++)
        {
            Vector3I w = walls[rng.Int(0, walls.Count - 1)];
            Vector3 at = SpaceMath.Center(w);
            Vector3 n = SpaceMath.Normal(space, at);
            if (n == Vector3.Zero) continue;
            SpaceMath.Project(space, ref at, 0.02f, out n);
            sim.Cracks.Add(new Crack { Id = sim.Cracks.Count, Kind = CrackKind.Crack, Position = at, Normal = n, Network = 10 + (int)(seed % 1000), Size = rng.Range(0.18f, 0.3f) });
        }
        Species grazer = fauna.For(DeepRole.Subsister, PoiTheme.Natural, depth);
        floors.RemoveAll(f => !Roomy(space, SpaceMath.Center(f), grazer));
        if (floors.Count == 0) return made;
        int patches = Math.Min(floors.Count, 3);
        var food = new List<FoodPatch>();
        for (int i = 0; i < patches; i++)
        {
            Vector3I f = floors[rng.Int(0, floors.Count - 1)];
            float? y = SpaceMath.FloorBelow(space, f.X + 0.5f, f.Y + 0.5f, f.Z + 0.5f, 2f);
            var patch = new FoodPatch { Position = new Vector3(f.X + 0.5f, y ?? f.Y, f.Z + 0.5f), Kind = grazer.Diet, Amount = rng.Range(0.6f, 1f) };
            sim.Food.Add(patch);
            food.Add(patch);
        }
        var colony = new Colony { Species = grazer, Space = space, Home = food[0].Position };
        foreach (Crack k in sim.Cracks) if (k.Network == 10 + (int)(seed % 1000)) colony.Sources.Add(k);
        for (int i = 0; i < subsisters; i++)
        {
            FoodPatch at = food[i % food.Count];
            Creature c = sim.SpawnIn(grazer, space, at.Position + new Vector3(rng.Range(-1f, 1f), 0.3f, rng.Range(-1f, 1f)));
            c.Home = at.Position;
            made.Add(c);
            colony.Target++;
        }
        sim.Colonies.Add(colony);
        Species crawler = fauna.For(DeepRole.Crawler, PoiTheme.Natural, depth);
        ceilings.RemoveAll(k => !Roomy(space, SpaceMath.Center(k) + Vector3.Down, crawler));
        var taken = new List<Vector3>();
        for (int i = 0; i < crawlers && ceilings.Count > 0; i++)
        {
            // Spread out over the ceiling: each keeps its own patch.
            Vector3I k = ceilings[rng.Int(0, ceilings.Count - 1)];
            for (int tries = 0; tries < 12 && taken.Exists(t => (t - SpaceMath.Center(k)).Length() < 3.5f); tries++) k = ceilings[rng.Int(0, ceilings.Count - 1)];
            taken.Add(SpaceMath.Center(k));
            Creature c = sim.SpawnIn(crawler, space, SpaceMath.Center(k), Vector3.Down);
            c.Hunger = rng.Range(0.3f, 0.8f);
            c.Ambush = new AmbushPoint { Kind = HideKind.Ceiling, Position = Crawl.Center(c), Normal = c.Facing.Y };
            made.Add(c);
        }
        // And most caves have something of the world's dark lying up in them: a pack, a big hunter, the changed, machines, Hollows.
        if (dark is { Count: > 0 } && rng.Chance(0.7f))
        {
            Species kind = dark[rng.Int(0, dark.Count - 1)];
            if (fauna.Indoor(fauna.Holding(kind, DeepRole.Lurker, PoiTheme.Natural)) is { } sized && fauna.Fitted(sized, 4f, 3.2f) is { } sp && (sp.Figure != null || sp.BodyHeight >= DeepFauna.IndoorMin - 0.05f))
            {
                var room = floors.FindAll(f => Roomy(space, SpaceMath.Center(f), sp));
                if (room.Count > 0)
                {
                    Vector3I at = room[rng.Int(0, room.Count - 1)];
                    int n = sp.Behaviour is Behaviour.PackHunter or Behaviour.Herd ? rng.Int(2, 3) : 1;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3I f = room[rng.Int(0, room.Count - 1)];
                        if ((SpaceMath.Center(f) - SpaceMath.Center(at)).Length() > 6f) f = at;
                        Creature c = sim.SpawnIn(sp, space, SpaceMath.Center(f) + new Vector3(i * 0.7f, 0.3f, 0f));
                        c.Home = SpaceMath.Center(at);
                        made.Add(c);
                    }
                }
            }
        }
        return made;
    }

    /// <summary>Whether a body can stand and turn at a spot in open rock: clear all round by half its turning circle, and over its back.</summary>
    public static bool Roomy(ISolidSpace space, Vector3 at, Species s)
    {
        float reach = s.TurnCircle * 0.5f;
        for (int i = 0; i < 8; i++)
        {
            float a = i * MathF.Tau / 8f;
            var dir = new Vector3(MathF.Cos(a), 0f, MathF.Sin(a));
            if (SpaceMath.Ray(space, at, dir, reach) < reach) return false;
        }
        return SpaceMath.Ray(space, at, Vector3.Up, s.BodyHeight) >= s.BodyHeight - 0.5f;
    }

    /// <summary>
    /// The roomiest open chamber near a point in any space (a planet's voxel caves): open floor
    /// with 4 to 7 m of headroom, under at least <paramref name="minDepth"/> metres of rock when a
    /// surface height is given. Returns false if there is none within the radius.
    /// </summary>
    public static bool FindChamber(ISolidSpace space, Vector3 around, float radius, out Vector3 floor, Func<float, float, float> surface = null, float minDepth = 3f)
    {
        floor = around;
        float bestScore = -1f;
        int r = (int)MathF.Ceiling(radius);
        int cx = (int)MathF.Floor(around.X), cz = (int)MathF.Floor(around.Z);
        int top = (int)MathF.Floor(around.Y) + 12, bottom = (int)MathF.Floor(around.Y) - 40;
        for (int z = cz - r; z <= cz + r; z++)
        for (int x = cx - r; x <= cx + r; x++)
        {
            if ((x - cx) * (x - cx) + (z - cz) * (z - cz) > r * r) continue;
            int ceiling = surface != null ? (int)MathF.Floor(surface(x + 0.5f, z + 0.5f) - minDepth) : top;
            for (int y = Math.Min(ceiling, top); y > bottom; y--)
            {
                if (space.SolidCell(x, y, z) || !space.SolidCell(x, y - 1, z)) continue;
                int h = 0;
                while (h < 8 && !space.SolidCell(x, y + h, z)) h++;
                if (h < 4 || h >= 8) continue;
                int wide = 0;
                for (int dz = -2; dz <= 2; dz++)
                for (int dx = -2; dx <= 2; dx++)
                    if (!space.SolidCell(x + dx, y, z + dz) && space.SolidCell(x + dx, y - 1, z + dz)) wide++;
                float score = wide + h * 0.5f;
                if (score > bestScore)
                {
                    bestScore = score;
                    floor = new Vector3(x + 0.5f, y, z + 0.5f);
                }
            }
        }
        return bestScore >= 10f;
    }

    /// <summary>A stable order, so the same cave always gets the same creatures where they are.</summary>
    static void Sort(List<Vector3I> cells) => cells.Sort((a, b) => a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X));
}
