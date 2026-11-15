using System;
using System.Collections.Generic;

namespace GalHack.Structures;

/// <summary>Why a force field can't go where it was drawn.</summary>
public enum FieldError { None, TooSmall, TooBig, Occupied, FarCornerLoose }

/// <summary>
/// A force field: one flat rectangle of cells on the grid, an L-shaped emitter block in each of its
/// four corners. People, vessels and objects pass through it; while it holds (up and powered) its
/// cells seal like a wall for the rooms' air (<see cref="Rooms"/>). The numbers that run it
/// (strength, regrowth, draw) are the game's; this is where it is and what it does to the air.
/// </summary>
public sealed class ForceField
{
    /// <summary>Its corner cells, inclusive. They differ on two axes; the third is <see cref="Axis"/>.</summary>
    public readonly Int3 Min, Max;

    /// <summary>The axis the field faces along (its normal).</summary>
    public readonly int Axis;

    /// <summary>The item that made it (its tier's row in the game's rules).</summary>
    public readonly string Tier;

    /// <summary>What it can still take before it fizzles, seconds left until it starts growing back, and a hit's glow (1 fading to 0).</summary>
    public float Strength, Wait, Flash;

    /// <summary>The power it draws is there (the structure's to say). Unpowered, it is down and holds no air.</summary>
    public bool Powered = true;

    /// <summary>Up, whole and powered: its cells seal. <see cref="Tick"/> and <see cref="Take"/> set it; the structure is told (<see cref="Structure.AirChanged"/>).</summary>
    public bool Holding;

    /// <summary>MW it draws: every tile while it holds (growing back included), nothing while it is down.</summary>
    public double Draw(double perTile) => Holding ? Tiles * perTile : 0;

    /// <summary>
    /// Harm (a shot, a blast): it fizzles to nothing at zero, its emitters untouched, and grows back
    /// only after <paramref name="delay"/> seconds with no hit. True if that knocked it down.
    /// </summary>
    public bool Take(float amount, float delay)
    {
        if (!Holding || amount <= 0f) return false;
        Strength = MathF.Max(0f, Strength - amount);
        Wait = delay;
        Flash = 1f;
        if (Strength > 0f) return false;
        Holding = false;
        return true;
    }

    /// <summary>
    /// A tick: once its wait is over it quietly grows back to its cap over <paramref name="regrowSeconds"/>
    /// while powered (an emitter destroyed takes the whole field with it, so it never ticks without
    /// all four). True if whether it holds changed.
    /// </summary>
    public bool Tick(float dt, float cap, float regrowSeconds)
    {
        if (Wait > 0f) Wait -= dt;
        else if (Powered && Strength < cap) Strength = MathF.Min(cap, Strength + cap / MathF.Max(0.01f, regrowSeconds) * dt);
        Flash = MathF.Max(0f, Flash - dt * 3f);
        bool holding = Powered && Strength > 0f;
        if (holding == Holding) return false;
        Holding = holding;
        return true;
    }

    public ForceField(Int3 a, Int3 b, int axis, string tier)
    {
        Min = Int3.Min(a, b);
        Max = Int3.Max(a, b);
        Axis = axis;
        Tier = tier;
    }

    /// <summary>The two axes it spans.</summary>
    public int U => (Axis + 1) % 3;
    public int V => (Axis + 2) % 3;

    public int Width => Max[U] - Min[U] + 1;
    public int Height => Max[V] - Min[V] + 1;

    /// <summary>Its 1 m tiles: what it draws power by.</summary>
    public int Tiles => Width * Height;

    public bool Contains(Int3 c) =>
        c[Axis] == Min[Axis] && c[U] >= Min[U] && c[U] <= Max[U] && c[V] >= Min[V] && c[V] <= Max[V];

    public IEnumerable<Int3> Cells()
    {
        for (int u = Min[U]; u <= Max[U]; u++)
            for (int v = Min[V]; v <= Max[V]; v++)
                yield return Min.With(U, u).With(V, v);
    }

    /// <summary>How far a point (structure frame, cell units) is from the sheet it spans (its cells' outer edges, through their middle), cells.</summary>
    public float DistanceTo(System.Numerics.Vector3 p)
    {
        float Clamp(int a) => Math.Clamp(Geometry.Get(p, a), Min[a] - 0.5f, Max[a] + 0.5f);
        var nearest = Geometry.Set(Geometry.Set(Geometry.Set(p, U, Clamp(U)), V, Clamp(V)), Axis, Min[Axis]);
        return System.Numerics.Vector3.Distance(p, nearest);
    }

    /// <summary>The four corners (min-min, max-min, max-max, min-max on its two axes).</summary>
    public Int3 Corner(int i) => Min.With(U, i is 1 or 2 ? Max[U] : Min[U]).With(V, i >= 2 ? Max[V] : Min[V]);

    /// <summary>
    /// How the emitter in a corner is turned: its L's arms (local +x and +y) run along the field's
    /// two edges from that corner, its face (local z) along the field's normal.
    /// </summary>
    public int CornerRot(int i)
    {
        Int3 x = Unit(U, i is 1 or 2 ? -1 : 1), y = Unit(V, i >= 2 ? -1 : 1);
        return Orientations.Find(new Mat3(x, y, Int3.Cross(x, y)));
    }

    static Int3 Unit(int axis, int sign) => Int3.Zero.With(axis, sign);

    /// <summary>
    /// Whether a field drawn here can go: at least <paramref name="minSide"/> and at most
    /// <paramref name="maxSide"/> cells each way, every cell clear of blocks and other fields, and
    /// the far corner touching a block of a structure with a core. (Each emitter must also attach,
    /// by building's own rule: the build tool checks that.)
    /// </summary>
    public FieldError Check(Structure s, Int3 far, int minSide, int maxSide)
    {
        if (Width < minSide || Height < minSide) return FieldError.TooSmall;
        if (Width > maxSide || Height > maxSide) return FieldError.TooBig;
        foreach (Int3 c in Cells())
        {
            if (s.Has(c)) return FieldError.Occupied;
            foreach (ForceField f in s.Fields)
                if (f.Contains(c)) return FieldError.Occupied;
        }
        return s.Core != null && Touches(s, far) ? FieldError.None : FieldError.FarCornerLoose;
    }

    /// <summary>Is a block beside a cell, through a face, outside the field?</summary>
    bool Touches(Structure s, Int3 cell)
    {
        foreach (Dir d in Dirs.All)
        {
            Int3 n = cell + d.Vec();
            if (!Contains(n) && s.Has(n)) return true;
        }
        return false;
    }
}
