using System;
using Godot;
using SurvivalVitals.Model;

namespace Slice.Places;

/// <summary>
/// The town's people breathe (owner, 24 Nov: "if an NPC found itself naked in vacuum, would it die?" Yes). By the sleeper's own rules
/// (survival's vitals.json oxygen): where the world has no air at all (an airless world, above the air's ceiling) and they are not in a
/// closed room of their place, they hold their breath, then suffocate at the sleeper's share of health a second until dead, and run
/// for the nearest home that holds air. A sealed suit helmet carries its own air, as the sleeper's suit does: it runs down out of the
/// air and fills again in it; running low, they go in to fill it. Machines don't breathe. Thin air is breathable to them for now.
/// </summary>
public sealed partial class Town
{
    public sealed partial class Person
    {
        /// <summary>Air where they stand (looked at a few times a second, <see cref="AirEvery"/>); until the next look, s; breath held left, s; a sealed suit's air left (-1: not yet worn).</summary>
        public bool InAir = true;
        public float AirIn, Breath = -1f, Tank = -1f;
    }

    /// <summary>How often each of them looks at the air where they stand, s.</summary>
    const float AirEvery = 0.25f;

    /// <summary>The sleeper's vitals rules (survival's data, read once): the hold, the suit's air and suffocation.</summary>
    static VitalsRules _vitals;

    public static bool Sealed(Person p) => p.Figure.Has("suit_helmet");

    /// <summary>Air to breathe where they stand: the world's own (any at all), or a closed room of their place.</summary>
    bool AirAt(Person p)
    {
        Vector3 feet = World(p.Pos);
        return Slice.Player.Suit.OxygenAt(_places.Zone, feet.Y + Height(p)) > 0 || _place is { Ready: true } place && place.Closed(feet);
    }

    /// <summary>A step of their breathing: held breath, the suit's air, suffocation, and for air when short of it.</summary>
    void Breathe(Person p, float dt)
    {
        if (p.Looks == null || p.Looks.Mechanical) return; // (nobody before their body's rolled; machines don't breathe)
        _vitals ??= _places.Zone.Data.Survival().Vitals;
        OxygenRules o = _vitals.Oxygen;
        if ((p.AirIn -= dt) <= 0f) (p.AirIn, p.InAir) = (AirEvery, AirAt(p));
        bool suit = Sealed(p);
        if (p.Breath < 0f) (p.Breath, p.Tank) = ((float)o.HoldBreathSeconds, (float)o.Max);
        if (suit) p.Tank = Math.Clamp(p.Tank + (float)(p.InAir ? o.RefillPerSecond : -o.DrainPerSecond) * dt, 0f, (float)o.Max);
        bool breathing = p.InAir || suit && p.Tank > 0f;
        p.Breath = breathing ? MathF.Min((float)o.HoldBreathSeconds, p.Breath + (float)o.HoldBreathSeconds * dt) : MathF.Max(0f, p.Breath - dt);
        if (!breathing && p.Breath <= 0f)
        {
            float lost = (float)(o.SuffocationDamagePerSecond / _vitals.Health.Max) * p.Health.Max * dt; // (the sleeper's share of health a second)
            if (p.Health.Current > lost) p.Health.Current -= lost;
            else Struck(p, lost, GalHack.Arms.DamageClass.Kinetic, byPlayer: false); // (a cry as they go down)
        }
        if (p.Dead || p.Run != null) return;
        // Short of air, for the nearest home that holds it: at a run with none, at a walk with the suit running low.
        if (!breathing) p.ScaredFor = MathF.Max(p.ScaredFor, AirEvery * 2f);
        else if (!p.InAir && p.Tank < o.WarnBelow * o.Max) p.AvoidFor = MathF.Max(p.AvoidFor, AirEvery * 2f);
        else return;
        if (p.Refuge < 0 && (p.Refuge = AirRefuge(p)) < 0) (p.FleeFrom, p.FleeAt) = (p.Pos, _clock); // (none: away from here, home)
    }

    /// <summary>The nearest home spot standing in a closed room of the place, or -1 (none: they make for their own).</summary>
    int AirRefuge(Person p)
    {
        int best = -1;
        float near = float.MaxValue;
        if (_place is not { Ready: true } place) return best;
        foreach (int h in _homes)
        {
            float d = (At(h) - p.Pos).LengthSquared();
            if (d < near && Whole(h) && place.Closed(World(At(h)))) (near, best) = (d, h);
        }
        return best;
    }
}
