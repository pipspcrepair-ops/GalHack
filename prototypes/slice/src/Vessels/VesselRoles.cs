using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Vessels;

/// <summary>
/// Who sits where aboard a vessel, and who holds the pilot role (design: every seat is a control
/// seat; one player flies at a time, and roles can be handed to anyone in a seat). Plain data, so
/// co-op and the full roles console can plug in: alone, you hold the role when you sit. A vessel's
/// heading is set by the seat of whoever holds the role: handing it to someone in a seat facing
/// another way turns what counts as forward, and nothing else: the ship itself doesn't move.
/// </summary>
public sealed class VesselRoles
{
    readonly Dictionary<string, Int3> _seated = new();

    /// <summary>Who holds the pilot role, or null.</summary>
    public string Pilot { get; private set; }

    /// <summary>Everyone in a seat, and which.</summary>
    public IReadOnlyDictionary<string, Int3> Seated => _seated;

    /// <summary>The seat the pilot role sits in now, or null.</summary>
    public Int3? PilotSeat => Pilot != null && _seated.TryGetValue(Pilot, out Int3 c) ? c : null;

    /// <summary>The pilot's seat changed (or the role did).</summary>
    public event Action PilotChanged;

    /// <summary>Someone took a seat: if nobody holds the pilot role, they do.</summary>
    public void Sat(string player, Int3 seat)
    {
        _seated[player] = seat;
        if (Pilot == null) Pilot = player;
        PilotChanged?.Invoke();
    }

    /// <summary>Someone got up: the role passes to anyone still seated, or stands empty.</summary>
    public void Stood(string player)
    {
        _seated.Remove(player);
        if (Pilot == player) Pilot = _seated.Keys.OrderBy(k => k, StringComparer.Ordinal).FirstOrDefault();
        PilotChanged?.Invoke();
    }

    /// <summary>Hands the pilot role to someone in a seat. False if they aren't in one.</summary>
    public bool Assign(string player)
    {
        if (!_seated.ContainsKey(player)) return false;
        Pilot = player;
        PilotChanged?.Invoke();
        return true;
    }

    /// <summary>The way a chair faces in its structure: right, up and back along the columns (hull axes), for the seat's control frame.</summary>
    public static Basis BasisOf(Structure s, Int3 chair)
    {
        Block b = s.Get(chair);
        Int3 r = Orientations.ToWorld(b.Rot, Dir.PosX).Vec(), u = Orientations.ToWorld(b.Rot, Dir.PosY).Vec(), k = Orientations.ToWorld(b.Rot, Dir.PosZ).Vec();
        return new Basis(new Vector3(r.X, r.Y, r.Z), new Vector3(u.X, u.Y, u.Z), new Vector3(k.X, k.Y, k.Z));
    }
}

/// <summary>Chairs aboard: every seat is a control seat.</summary>
public sealed partial class Vessel
{
    /// <summary>The one player of the slice, by name in the roles (co-op brings the rest).</summary>
    public const string Player = "castaway";

    /// <summary>Who sits where, and who flies.</summary>
    public readonly VesselRoles Roles = new();

    /// <summary>Every chair aboard (the one place that asks what a chair is: command chairs are coming to the catalogue).</summary>
    public IEnumerable<Int3> Chairs => Structure.Blocks().Where(b => IsChair(Structure.TypeOf(b.block))).Select(b => b.pos);

    public static bool IsChair(BlockType type) => type.IsCommandChair;

    /// <summary>The chair nearest a world point, and how far it is (a structure with none: infinitely far).</summary>
    public float NearestChair(Vector3 world, out Int3 chair)
    {
        float best = float.PositiveInfinity;
        chair = default;
        foreach (Int3 c in Chairs)
        {
            float d = ToWorld(StructureBridge.CellCenter(Structure, c)).DistanceTo(world);
            if (d < best)
            {
                best = d;
                chair = c;
            }
        }
        return best;
    }

    /// <summary>The chair you take: where <see cref="Seat"/> stands from now on.</summary>
    public void TakeChair(Int3 chair)
    {
        _chair = chair;
        if (Structure.TryGetBlock(chair, out Int3 anchor, out Block b)) SitAt(anchor, b);
    }

    Int3? _chair;

    /// <summary>The chair in use (the one last taken, or the first aboard).</summary>
    public Int3? Chair => _chair;

    /// <summary>The way the chair in use faces in the hull, for the view from it.</summary>
    public Basis ChairBasis => _chair is Int3 c && Structure.Has(c) ? VesselRoles.BasisOf(Structure, c) : Basis.Identity;

    /// <summary>The control frame follows the pilot's seat at once: the ship doesn't turn, only what counts as forward.</summary>
    void FollowPilot()
    {
        if (Flight == null) return;
        Flight.State.Seat = Roles.PilotSeat is Int3 c && Structure.Has(c) ? VesselRoles.BasisOf(Structure, c) : Basis.Identity;
    }
}
