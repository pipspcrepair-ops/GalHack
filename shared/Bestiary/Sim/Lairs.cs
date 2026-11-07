using System;
using System.Collections.Generic;
using Godot;

namespace Wildlife;

/// <summary>Where a creature (or a herd, or a pack) spends the hours it is not out.</summary>
public sealed class RestSite
{
    public RestKind Kind;
    public Vector3 Position;
    /// <summary>How far round it a group spreads out to rest.</summary>
    public float Radius = 2f;
    public int Users;
}

/// <summary>What kind of hole a crack is: a crack in rock, a vent or duct grille, a door.</summary>
public enum CrackKind { Crack, Vent, Door }

/// <summary>
/// A gap too small for a player to follow: a crack in a cave wall, a vent grille, the dark
/// behind a door. Small things slip into one and are gone; cracks of one network join up through
/// the rock, so what goes in at one may come out at another, scratching as it goes.
/// </summary>
public sealed class Crack
{
    public int Id;
    public CrackKind Kind;
    /// <summary>The mouth, on the surface.</summary>
    public Vector3 Position;
    /// <summary>The surface normal there, out of the rock.</summary>
    public Vector3 Normal = Vector3.Up;
    /// <summary>Cracks with the same network number connect.</summary>
    public int Network;
    /// <summary>Width of the gap, metres: always too small to follow.</summary>
    public float Size = 0.3f;
}

/// <summary>
/// A nest in a place's deepest levels (or a wreck's hold, duct or engine room), and the
/// creatures that guard it. Deeper nests are bigger and better guarded.
/// </summary>
public sealed class Nest
{
    public Vector3 Position;
    public Vector3 Normal = Vector3.Up;
    public int Depth;
    /// <summary>A derelict ship's nest, built up from scrap while you watch.</summary>
    public bool Derelict;
    /// <summary>How big it has grown (0.5 a start, 1 full); derelict nesters build it up.</summary>
    public float Size = 1f;
    public float Radius = 2f;
    public readonly List<Creature> Guards = new();
    /// <summary>The guards take turns attacking, like a pack.</summary>
    public readonly Pack Turns = new();
}

/// <summary>Moss, fungus or scraps a subsister feeds on. It is eaten down and grows back.</summary>
public sealed class FoodPatch
{
    public Vector3 Position;
    public Vector3 Normal = Vector3.Up;
    public Diet Kind = Diet.Moss;
    public float Amount = 1f;
    public float Radius = 0.8f;
}

/// <summary>Where an ambusher hides and what gives it away.</summary>
public enum HideKind { None, Ceiling, Vent, Door, Crack, Dark }

/// <summary>An ambusher's hiding place and the spot it watches.</summary>
public sealed class AmbushPoint
{
    public HideKind Kind;
    /// <summary>Where it waits: on the ceiling, or the vent, crack or door it is behind.</summary>
    public Vector3 Position;
    public Vector3 Normal = Vector3.Down;
    /// <summary>The vent, crack or door it hides in, if any.</summary>
    public Crack Hole;
}

/// <summary>An ambush (or a hunt) giving itself away: what you hear and see before it strikes.</summary>
public struct TellEvent
{
    public Creature Creature;
    public TellKind Kind;
    /// <summary>Where the tell happens: the vent that rattles, the ceiling the dust falls from.</summary>
    public Vector3 Position;
    /// <summary>For a dust fall or a shadow: the floor point under it.</summary>
    public Vector3 Below;
    public float Time;
    /// <summary>How long it runs before the strike begins.</summary>
    public float Duration;
    public ITarget Target;
}

/// <summary>
/// A creature as something another creature hunts: a place's predators take its subsisters
/// through the same telegraphed attacks they use on players.
/// </summary>
public sealed class CreatureTarget : ITarget
{
    public readonly Creature Creature;
    readonly Health _health;

    public CreatureTarget(Creature c)
    {
        Creature = c;
        _health = new Health(c.Stats.MaxHealth);
    }

    public string Name => Creature.Species.Name;
    public Vector3 Position => Creature.Position;
    public Vector3 Forward => Creature.Forward;
    public Health Health
    {
        get
        {
            _health.Current = MathF.Max(0f, Creature.Health);
            return _health;
        }
    }
    public bool Attackable => Creature.Alive && !Creature.Removed && !Creature.InWalls;
    public float ThreatScale => 1f;
}
