using System;
using System.Collections.Generic;
using Godot;
using TerrainDigging.Terrain;

namespace PlanetSurface.Player;

/// <summary>
/// Terrain-digging's rule 7 on the planet: anything found inside terrain is moved to the nearest
/// open space. Checked every physics tick against the density field the meshes are drawn from;
/// a body must be inside for two ticks in a row, so a single frame of overlap never teleports
/// anyone. Bodies get here through loads, frame shifts, planet switches and tests, never by the
/// terrain growing (it can't).
/// </summary>
public partial class StuckGuard : Node
{
    const int TicksBeforeRescue = 2;

    public Func<ChunkStore> Store;
    public Walker Player;
    public bool Enabled = true;

    /// <summary>
    /// Solid flora: where a body's feet should go if its core is inside a tree trunk or a boulder
    /// (from feet, height and radius), or null if it isn't inside one. Trunks aren't in the density field.
    /// </summary>
    public Func<Vector3, float, float, Vector3?> OutOfSolids;

    /// <summary>Who was moved, from where, to where.</summary>
    public event Action<Vector3, Vector3> Rescued;
    public int Rescues { get; private set; }

    int _stuckTicks;

    public BodyShape Current => new(Player.CurrentHeight, Walker.Radius);
    public BodyShape Rescue => new(Walker.StandHeight, Walker.Radius);

    public override void _PhysicsProcess(double delta)
    {
        ChunkStore store = Store?.Invoke();
        if (!Enabled || store == null || Player == null || !Player.IsInsideTree() || Player.ProcessMode == ProcessModeEnum.Disabled) return;
        bool terrain = IsInside(store);
        Vector3? beside = terrain ? null : InsideSolid();
        if (!terrain && beside == null)
        {
            _stuckTicks = 0;
            return;
        }
        if (++_stuckTicks < TicksBeforeRescue) return;
        _stuckTicks = 0;
        if (terrain) RescueNow(store);
        else MoveOut(beside.Value);
    }

    /// <summary>Where the player should be if they are inside a trunk or a boulder now, or null.</summary>
    public Vector3? InsideSolid() => OutOfSolids?.Invoke(Player.GlobalPosition, Player.CurrentHeight, Walker.Radius);

    void MoveOut(Vector3 to)
    {
        Vector3 from = Player.GlobalPosition;
        Player.Teleport(to);
        Rescues++;
        Rescued?.Invoke(from, to);
    }

    public bool IsInside(ChunkStore store) => StuckResolver.IsInsideTerrain(store, ToN(Player.GlobalPosition), Current);

    public Vector3 RescueNow(ChunkStore store)
    {
        Vector3 from = Player.GlobalPosition;
        OpenSpace space = StuckResolver.FindOpenSpace(store, ToN(from), Rescue);
        if (space.Fallback && Player.IsCrouching)
        {
            OpenSpace tight = StuckResolver.FindOpenSpace(store, ToN(from), Current);
            if (!tight.Fallback) space = tight;
        }
        var to = new Vector3(space.Feet.X, space.Feet.Y, space.Feet.Z);
        Player.Teleport(to);
        Rescues++;
        Rescued?.Invoke(from, to);
        return to;
    }

    static System.Numerics.Vector3 ToN(Vector3 v) => new(v.X, v.Y, v.Z);
}
