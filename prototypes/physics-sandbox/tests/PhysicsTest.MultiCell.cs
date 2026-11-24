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
/// Blocks that span several cells, in the physics bridge (docs/one-metre-grid.md, section 2): the
/// 2×1×1 Container T4, the 2×2×1 door and the 1×1×2 Thruster T2 collide as all of their cells, and
/// a crate dropped on the container comes to rest on its top.
/// </summary>
public partial class PhysicsTest
{
    IEnumerable<int> MultiCellBlocks()
    {
        Catalogue cat = Catalogue.LoadDirectory(SandboxWorld.DataPath("prototypes", "building", "data"));
        var s = new Structure(cat) { Name = "Multi-cell pad", Mode = CoreMode.Base, Owner = Building.DefaultOwner };
        BlockType steel = cat.Type("iron-plated"), box = cat.Type("container-t4"), door = cat.Type("door-2x2x1"), jet = cat.Type("thruster-t2");
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                s.Set(new SInt3(x, 0, z), Building.Make(s, x == 0 && z == 0 ? cat.CoreType : steel));
        var boxAt = new SInt3(0, 1, 0);
        bool placed = Building.Place(s, boxAt, Building.Make(s, box)) == PlaceError.None
            & Building.Place(s, new SInt3(-2, 1, -2), Building.Make(s, door)) == PlaceError.None
            & Building.Place(s, new SInt3(2, 1, 2), Building.Make(s, jet)) == PlaceError.None;

        // Its colliders: every cell of each, nothing more (the deck's 25, the container's 2, the shut door's 4, the thruster's 2).
        float cell = s.Grid.CellSize, volume = 0f;
        foreach (var c in StructureBridge.Colliders(s))
            volume += c.Boxes.Sum(b => b.Size.X * b.Size.Y * b.Size.Z);
        Check(placed && System.MathF.Abs(volume / (cell * cell * cell) - (25 + 2 + 4 + 2)) < 1e-3f,
            $"a 2x1x1 container, a shut 2x2x1 door and a 1x1x2 thruster collide as all their cells ({volume / (cell * cell * cell):0.##} cells of boxes)");

        // A base on it, and a crate dropped onto the container's top.
        N.Vector3 ground = _w.Ground(-60f, 60f) + N.Vector3.UnitY * 4f;
        Ship pad = Ship.SpawnBase(_w, s, ground, 0f);
        N.Vector3 top = pad.Body.ToParent(StructureBridge.BlockCenter(s, boxAt, s.Get(boxAt)) + N.Vector3.UnitY * cell * 0.5f);
        Crate crate = Crate.Spawn(_w, top + N.Vector3.UnitY * 3f, 1);
        for (int i = 0; i < 240; i++) yield return 0;
        N.Vector3 at = crate.Body.WorldPosition;
        float rest = top.Y + crate.Kind.Size * 0.5f;
        Check(System.MathF.Abs(at.Y - rest) < 0.12f && N.Vector2.Distance(new N.Vector2(at.X, at.Z), new N.Vector2(top.X, top.Z)) < cell * 0.6f,
            $"a crate dropped on the container rests on its top, {cell:0} m up, not inside it (at {at.Y - top.Y:+0.00;-0.00} m from the top, want {crate.Kind.Size * 0.5f:0.00})");
        _w.Remove(crate);
        _w.Remove(pad);
    }
}
