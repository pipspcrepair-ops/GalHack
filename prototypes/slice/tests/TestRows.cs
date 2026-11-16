using System;
using Godot;
using GalHack.Physics.Engine;
using PhysicsSandbox.Bridges;
using Slice.Vessels;
using N = System.Numerics;
using S = GalHack.Structures;

namespace Slice.Tests;

/// <summary>A small structure set down by the sleeper for a test: a few iron blocks off a core (or none), and where to aim at one.</summary>
public static class TestRows
{
    /// <summary>A few iron blocks off a core (or none), set down on the ground a few metres off.</summary>
    public static Vessel Row(Game.Game game, string owner, bool core, float bearing, out S.Int3[] cells)
    {
        S.Catalogue cat = game.Zone.Data.Catalogue;
        var s = new S.Structure(cat) { Name = $"Sleeper tool test {owner}" };
        S.BlockType bare = cat.Type("iron-plated");
        if (core) S.Building.Place(s, S.Int3.Zero, S.Building.Make(s, cat.CoreType), null, owner);
        // Three along from the core, and one stood on the first: the end and the top are leaves.
        cells = new[] { new S.Int3(1, 0, 0), new S.Int3(2, 0, 0), new S.Int3(3, 0, 0), new S.Int3(1, 1, 0) };
        foreach (S.Int3 c in cells) s.Set(c, S.Building.Make(s, bare));
        if (!core) s.Set(S.Int3.Zero, S.Building.Make(s, bare));
        s.DirtyChunks.Clear();
        Vector3 feet = game.Survivor.WorldFeet;
        Vector3 spot = feet + new Vector3(MathF.Cos(bearing), 0f, MathF.Sin(bearing)) * 3.5f;
        // Clear of the ground all along it, however the land lies (a few blocks may stand a little off it).
        float top = float.MinValue;
        for (float dx = -4f; dx <= 4f; dx += 1f)
            for (float dz = -4f; dz <= 4f; dz += 1f) top = MathF.Max(top, game.Zone.GroundAt(spot.X + dx, spot.Z + dz).Y);
        N.Vector3 at = new(spot.X, top + s.Grid.CellSize * 0.5f + 0.3f, spot.Z);
        Vessel v = core ? Vessel.SpawnBase(game.Zone, s, at, bearing) : Vessel.SpawnGroundWreck(game.Zone, s, at, N.Quaternion.CreateFromAxisAngle(N.Vector3.UnitY, -bearing));
        foreach (var (p, _) in s.Blocks()) v.ClearGround(p);
        return v;
    }

    public static Vector3 Centre(Vessel v, S.Int3 c) => v.ToWorld(StructureBridge.CellCenter(v.Structure, c));

    /// <summary>The block's face toward the eye: where a blast or a look at it lands.</summary>
    public static Vector3 FaceOf(Game.Game game, Vessel v, S.Int3 c)
    {
        Vector3 centre = Centre(v, c), eye = game.Survivor.WorldEye();
        return centre + (eye - centre).Normalized() * v.Structure.Grid.CellSize * 0.45f;
    }
}
