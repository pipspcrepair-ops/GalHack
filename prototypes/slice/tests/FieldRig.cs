using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The force field check's and views' room: a hollow iron plated box on a base (8 x 6 x 6 cells),
/// a 4 x 3 opening in its front wall (-z), a 1 x 2 door in its east wall (+x), its core, a
/// generator and a container with its fuel inside. Its floor sits a little proud of the ground, so the sleeper walks in.
/// </summary>
public sealed class FieldRig
{
    public static readonly Int3 OpeningMin = new(2, 1, 0), OpeningMax = new(5, 3, 0), Door = new(7, 1, 2), Inside = new(3, 2, 3);

    public Vessel Room { get; private set; }
    public Structure S => Room.Structure;
    float Cell => S.Grid.CellSize;

    /// <summary>A point given in cells (cell centres on whole numbers), in the world.</summary>
    public Vector3 World(float x, float y, float z) => Room.ToWorld(new N.Vector3(x, y, z) * Cell);

    /// <summary>The same, structure frame, metres.</summary>
    public N.Vector3 Local(float x, float y, float z) => new N.Vector3(x, y, z) * Cell;

    public static FieldRig Build(Game.Game game, Vector3 ground)
    {
        Catalogue cat = game.Zone.Data.Catalogue;
        var s = new Structure(cat) { Name = "Field room", Mode = CoreMode.Base };
        Block wall = Building.Make(s, cat.Type("iron-plated"));
        for (int x = 0; x <= 7; x++)
            for (int y = 0; y <= 5; y++)
                for (int z = 0; z <= 5; z++)
                {
                    var p = new Int3(x, y, z);
                    bool shell = x == 0 || x == 7 || y == 0 || y == 5 || z == 0 || z == 5;
                    bool opening = z == 0 && x >= OpeningMin.X && x <= OpeningMax.X && y >= OpeningMin.Y && y <= OpeningMax.Y;
                    bool door = x == Door.X && z == Door.Z && (y == Door.Y || y == Door.Y + 1);
                    if (shell && !opening && !door) s.Set(p, wall);
                }
        // The door's leaf across the east wall: its own x along -z, up +y, its face +x.
        int rot = Orientations.Find(new Mat3(new Int3(0, 0, -1), Int3.UnitY, Int3.UnitX));
        s.Set(Door, Building.Make(s, cat.Type("door-1x2x1"), null, rot));
        s.Set(new Int3(4, 1, 3), Building.Make(s, cat.CoreType));
        s.Set(new Int3(2, 1, 4), Building.Make(s, cat.Type("generator-t1")));
        s.Set(new Int3(5, 1, 4), Building.Make(s, cat.Type("container-t1"))); // core storage, for the generator's fuel
        s.DirtyChunks.Clear();
        Vessel v = Vessel.SpawnBase(game.Zone, s, (ground + Vector3.Down * 0.2f * s.Grid.CellSize).ToNumerics(), 0f);
        foreach (var (p, _) in s.Blocks()) v.ClearGround(p);
        v.Storage.Add("biofuel", 40);
        return new FieldRig { Room = v };
    }

    /// <summary>Is the room's inside a sealed room now (building's flood fill, fields counted)?</summary>
    public bool Sealed()
    {
        foreach (Room r in Room.Rooms.Sealed)
            if (r.Cells.Contains(Inside)) return true;
        return false;
    }
}
