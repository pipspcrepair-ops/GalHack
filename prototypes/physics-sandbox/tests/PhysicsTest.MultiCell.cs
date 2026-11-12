using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Structures;
using PhysicsSandbox.Bridges;
using PhysicsSandbox.World;
using N = System.Numerics;
using SInt3 = GalHack.Structures.Int3;
using Structure = GalHack.Structures.Structure;

namespace PhysicsSandbox;

/// <summary>
/// Blocks that span several cells, in the physics bridge (docs/one-metre-grid.md, section 2): a
/// 2×2×2 container and a 2×2×1 door (<see cref="MultiCellParts"/>) collide as all of their cells,
/// and a crate dropped on the container comes to rest on its top.
/// </summary>
public partial class PhysicsTest
{
    IEnumerable<int> MultiCellBlocks()
    {
        Catalogue cat = MultiCellParts.Load(name => File.ReadAllText(SandboxWorld.DataPath("prototypes", "building", "data", name)));
        var s = new Structure(cat, cat.Grid(GridSize.Large)) { Name = "Multi-cell pad", Mode = CoreMode.Base, Owner = Building.DefaultOwner };
        BlockType steel = cat.Type("steel"), box = cat.Type(MultiCellParts.Container), door = cat.Type(MultiCellParts.Door);
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                s.Set(new SInt3(x, 0, z), Building.Make(s, x == 0 && z == 0 ? cat.CoreType : steel));
        var boxAt = new SInt3(0, 1, 0);
        bool placed = Building.Place(s, boxAt, Building.Make(s, box)) == PlaceError.None
            & Building.Place(s, new SInt3(-2, 1, -2), Building.Make(s, door)) == PlaceError.None;

        // Its colliders: every cell of each, nothing more (the deck's 25, the container's 8, the shut door's 4).
        float cell = s.Grid.CellSize, volume = 0f;
        foreach (var c in StructureBridge.Colliders(s))
            volume += c.Boxes.Sum(b => b.Size.X * b.Size.Y * b.Size.Z);
        Check(placed && System.MathF.Abs(volume / (cell * cell * cell) - (25 + 8 + 4)) < 1e-3f,
            $"a 2x2x2 container and a shut 2x2x1 door collide as all their cells ({volume / (cell * cell * cell):0.##} cells of boxes)");

        // A base on it, and a crate dropped onto the container's top.
        N.Vector3 ground = _w.Ground(-60f, 60f) + N.Vector3.UnitY * 4f;
        Ship pad = Ship.SpawnBase(_w, s, ground, 0f);
        N.Vector3 top = pad.Body.ToParent(StructureBridge.BlockCenter(s, boxAt, s.Get(boxAt)) + N.Vector3.UnitY * cell);
        Crate crate = Crate.Spawn(_w, top + N.Vector3.UnitY * 3f, 1);
        for (int i = 0; i < 240; i++) yield return 0;
        N.Vector3 at = crate.Body.WorldPosition;
        float rest = top.Y + crate.Kind.Size * 0.5f;
        Check(System.MathF.Abs(at.Y - rest) < 0.12f && N.Vector2.Distance(new N.Vector2(at.X, at.Z), new N.Vector2(top.X, top.Z)) < cell * 0.6f,
            $"a crate dropped on the container rests on its top, {cell * 2:0} m up, not inside it (at {at.Y - top.Y:+0.00;-0.00} m from the top, want {crate.Kind.Size * 0.5f:0.00})");
        _w.Remove(crate);
        _w.Remove(pad);
    }
}
