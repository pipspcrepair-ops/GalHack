using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>
/// What a domestic animal is kept for. Callers filter by it: working animals only in low-tech
/// places, stock in low-tech places and on farms of any tech level, pets anywhere.
/// </summary>
public enum DomesticKind
{
    /// <summary>Wild.</summary>
    None,
    /// <summary>Grazers kept in pens or fields.</summary>
    Stock,
    /// <summary>Pack or draught beasts, kept by carts and wagons.</summary>
    Working,
    /// <summary>Small companions that follow townsfolk about or lie around.</summary>
    Pet,
}

/// <summary>
/// A settlement (a survivor village or town, a faction's civilian place) as wildlife sees it:
/// an area wild creatures keep out of. Nothing wild spawns inside it or within its margin, wild
/// creatures roaming near it steer round it, and nobody inside it is hunted. Any wild creature
/// that does end up inside raises <see cref="WildSim.StrayEntered"/>, so the town can send its
/// guards (<see cref="WildSim.CullStray"/>). Its own animals (<see cref="DomesticKind"/>) live inside.
/// </summary>
public sealed class Settlement
{
    public string Name = "";
    public Vector3 Center;
    /// <summary>The settled area's radius, metres.</summary>
    public float Radius = 40f;
    /// <summary>No wild spawns this far beyond the edge either (negative: the spawning base clearance).</summary>
    public float SpawnMargin = -1f;
    /// <summary>Wild creatures roaming near keep this far off the edge.</summary>
    public float Buffer = 6f;

    public bool Contains(Vector3 p, float margin = 0f) => WildSim.Flat(p - Center).Length() < Radius + margin;
}
