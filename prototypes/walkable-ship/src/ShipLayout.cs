using System;
using System.Collections.Generic;
using Godot;

namespace WalkableShip;

/// <summary>Which parts of the ship a block takes part in.</summary>
[Flags]
public enum BlockRole
{
    /// <summary>Drawn as part of the ship.</summary>
    Visual = 1,
    /// <summary>Collides on the ghost cabin, where players aboard walk.</summary>
    Ghost = 2,
    /// <summary>Collides on the outer hull, for players standing outside.</summary>
    Hull = 4,
    /// <summary>Drawn as a window pane.</summary>
    Glass = 8,
    /// <summary>Drawn glowing, like an engine nozzle.</summary>
    Glow = 16,
}

public readonly record struct Block(Vector3 Center, Vector3 Size, Color Color, BlockRole Roles);

/// <summary>
/// The prototype ship, in ship-local metres. The origin is the middle of the cabin floor,
/// forward is -Z and up is +Y. The cabin is 10 m wide, 5 m tall and 24 m long, with a pilot
/// seat at the front, windows down both sides and an open door at the back.
/// </summary>
public static class ShipLayout
{
    // A 1970s paperback-cover palette.
    public static readonly Color HullColor = new("e9dfc4");
    public static readonly Color Orange = new("e85d04");
    public static readonly Color Yellow = new("f4a100");
    public static readonly Color Red = new("c1121f");
    public static readonly Color Teal = new("0a9396");
    public static readonly Color Dark = new("2b2d42");
    public static readonly Color Deck = new("3a3f47");
    public static readonly Color GlassTint = new(0.55f, 0.8f, 0.9f, 0.16f);

    /// <summary>The walkable cabin space.</summary>
    public static readonly Aabb Cabin = new(new Vector3(-5, 0, -12), new Vector3(10, 5, 24));

    public static readonly Vector3 SeatLocal = new(0, 0, -10.2f);
    public static readonly Vector3 SeatEyeLocal = new(0, 1.55f, -10.2f);
    public static readonly Vector3 SeatExitLocal = new(0, 0.05f, -8.4f);
    public static readonly Vector3 SpawnLocal = new(0, 0.05f, 4f);

    /// <summary>
    /// True while a player aboard is still in the ship. They leave once past the rear door's
    /// outer face (or if anything ever lets them slip out of the cabin).
    /// </summary>
    public static bool StillAboard(Vector3 local) =>
        local.Z < 12.9f && local.Z > -12.9f && Mathf.Abs(local.X) < 5.9f && local.Y > -2f && local.Y < 5.9f;

    /// <summary>
    /// True once a player outside is well inside the cabin. The gap between this and
    /// <see cref="StillAboard"/> stops the player flickering between the two at the door.
    /// </summary>
    public static bool ShouldBoard(Vector3 local) =>
        local.Z < 11.6f && local.Z > -11.6f && Mathf.Abs(local.X) < 4.6f && local.Y > -0.3f && local.Y < 4.4f;

    public static IReadOnlyList<Block> Blocks => _blocks ??= Build();
    static List<Block> _blocks;

    static List<Block> Build()
    {
        var blocks = new List<Block>();
        const BlockRole all = BlockRole.Visual | BlockRole.Ghost | BlockRole.Hull;
        const BlockRole solid = BlockRole.Ghost | BlockRole.Hull;   // collision only
        const BlockRole prop = BlockRole.Visual | BlockRole.Ghost;  // inside the cabin only
        const BlockRole outside = BlockRole.Visual | BlockRole.Hull;

        void Add(Vector3 center, Vector3 size, Color color, BlockRole roles) =>
            blocks.Add(new Block(center, size, color, roles));

        // Deck and roof.
        Add(new(0, -0.25f, 0), new(11, 0.5f, 25), Deck, all);
        Add(new(0, 5.25f, 0), new(11, 0.5f, 25), HullColor, all);

        // Side walls: solid for collision, drawn with a window band from 2 m to 3.2 m.
        foreach (float x in new[] { -5.25f, 5.25f })
        {
            Add(new(x, 2.5f, 0), new(0.5f, 5, 25), HullColor, solid);
            Add(new(x, 1f, 0), new(0.5f, 2, 25), HullColor, BlockRole.Visual);
            Add(new(x, 4.1f, 0), new(0.5f, 1.8f, 25), HullColor, BlockRole.Visual);
            for (float z = -12f; z <= 12f; z += 6f)
                Add(new(x, 2.6f, z), new(0.5f, 1.2f, 0.6f), HullColor, BlockRole.Visual);
            Add(new(x, 2.6f, 0), new(0.1f, 1.2f, 24), GlassTint, BlockRole.Glass);

            // Livery: a broad orange band and a yellow pinstripe on the outside.
            float sx = x + Mathf.Sign(x) * 0.27f;
            Add(new(sx, 1.1f, 0), new(0.04f, 0.9f, 25), Orange, BlockRole.Visual);
            Add(new(sx, 1.75f, 0), new(0.04f, 0.18f, 25), Yellow, BlockRole.Visual);
        }

        // Front wall with a wide cockpit window.
        Add(new(0, 2.5f, -12.25f), new(11, 5, 0.5f), HullColor, solid);
        Add(new(0, 0.75f, -12.25f), new(11, 1.5f, 0.5f), HullColor, BlockRole.Visual);
        Add(new(0, 4.3f, -12.25f), new(11, 1.4f, 0.5f), HullColor, BlockRole.Visual);
        foreach (float x in new[] { -5.25f, -1.8f, 1.8f, 5.25f })
            Add(new(x, 2.55f, -12.25f), new(0.5f, 2.1f, 0.5f), HullColor, BlockRole.Visual);
        Add(new(0, 2.55f, -12.25f), new(10.5f, 2.1f, 0.1f), GlassTint, BlockRole.Glass);
        Add(new(0, 0.75f, -12.52f), new(11, 0.5f, 0.04f), Red, BlockRole.Visual);

        // Rear wall with a 2.4 m by 3 m door.
        Add(new(-3.35f, 2.5f, 12.25f), new(4.3f, 5, 0.5f), HullColor, all);
        Add(new(3.35f, 2.5f, 12.25f), new(4.3f, 5, 0.5f), HullColor, all);
        Add(new(0, 4f, 12.25f), new(2.4f, 2, 0.5f), HullColor, all);
        Add(new(0, 3.1f, 12.52f), new(2.6f, 0.2f, 0.04f), Yellow, BlockRole.Visual);

        // Engine nacelles riding on the roof, clear of the windows.
        foreach (float x in new[] { -4.4f, 4.4f })
        {
            Add(new(x, 6.8f, 6.5f), new(2.6f, 2.2f, 12f), Dark, outside);
            Add(new(x, 6.8f, 12.6f), new(2f, 1.6f, 0.2f), Orange, BlockRole.Visual | BlockRole.Glow);
            Add(new(x, 5.8f, 6.5f), new(1f, 0.6f, 4f), HullColor, outside);          // pylon
            Add(new(x, 7.95f, 6.5f), new(2.7f, 0.12f, 12.1f), Teal, BlockRole.Visual); // nacelle stripe
            Add(new(x, 6.8f, 0.3f), new(2.2f, 1.8f, 0.4f), Orange, BlockRole.Visual);  // intake ring
        }

        // Nose prow, dorsal fin and landing skids give it a silhouette.
        Add(new(0, 0.75f, -13.6f), new(8f, 1.5f, 2.2f), HullColor, outside);
        Add(new(0, 1.2f, -14.75f), new(6f, 0.6f, 0.2f), Red, BlockRole.Visual);
        Add(new(0, 6.6f, 10f), new(0.4f, 2.4f, 4.5f), Orange, outside);
        foreach (float x in new[] { -3.6f, 3.6f })
            Add(new(x, -0.65f, 0), new(0.5f, 0.3f, 20f), Dark, outside);

        // Checkerboard flash near the tail, both sides.
        foreach (float x in new[] { -5.53f, 5.53f })
            for (int i = 0; i < 8; i++)
                for (int j = 0; j < 2; j++)
                    if ((i + j) % 2 == 0)
                        Add(new(x, 4.05f + j * 0.45f, 8.4f + i * 0.45f), new(0.04f, 0.45f, 0.45f), Dark, BlockRole.Visual);

        // Cabin trim: a pale ceiling liner, light panels and a hazard stripe at the door.
        Add(new(0, 4.97f, 0), new(10f, 0.05f, 24f), new Color("c9cdd2"), BlockRole.Visual);
        foreach (float z in new[] { -7f, 0f, 7f })
            Add(new(0, 4.93f, z), new(1.4f, 0.04f, 3f), new Color("fff1d6"), BlockRole.Visual | BlockRole.Glow);
        Add(new(0, 0.01f, 11.6f), new(2.4f, 0.02f, 0.5f), Yellow, BlockRole.Visual);

        // Cabin furniture. The pilot faces -Z, so the seat back is on the +Z side.
        Add(new(0, 0.45f, -10.2f), new(1.2f, 0.9f, 1.2f), Red, prop);     // pilot seat
        Add(new(0, 1.3f, -9.6f), new(1.2f, 1.2f, 0.25f), Red, prop);      // seat back
        Add(new(0, 0.6f, -11.6f), new(4f, 1.2f, 0.7f), Dark, prop);       // console
        Add(new(-4.2f, 0.6f, -2f), new(1.4f, 1.2f, 6f), Teal, prop);      // bunks
        Add(new(4.2f, 1f, 6f), new(1.4f, 2f, 3f), Dark, prop);            // locker
        return blocks;
    }
}
