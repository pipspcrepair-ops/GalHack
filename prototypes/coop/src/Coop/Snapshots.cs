using Godot;

namespace Coop;

/// <summary>One moment of a ship's motion, in its zone's world.</summary>
public struct ShipSnap
{
    public Vector3 Pos;
    public Quaternion Rot;
    public Vector3 Vel;
    public Vector3 Spin; // ship-local, rad/s
}

/// <summary>
/// One moment of a player, as their own client reported it. Aboard a ship the position is
/// ship-local (the ship's ID plus a local transform), so everyone aboard stays put however fast
/// the ship flies; outside it is in the zone's world.
/// </summary>
public struct PlayerSnap
{
    public int Mode;
    public int ShipId;
    public Vector3 Pos;
    public float Yaw, Pitch;
    public bool Crouch;
}

