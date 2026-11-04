using System;
using System.Collections.Generic;
using System.Linq;

namespace Invasions.Model;

public enum CrewRole { Driver, Gunner }

public sealed class CrewSeat
{
    public int Index { get; init; }
    public CrewRole Role { get; init; }
    public double MaxHp { get; init; }
    public double Hp { get; internal set; }
    /// <summary>Climbed out to walk away (a stranded vehicle's crew withdrawing). Not dead, but not aboard.</summary>
    public bool BailedOut { get; internal set; }
    public bool Alive => Hp > 0 && !BailedOut;
}

/// <summary>
/// Who is aboard an NPC vehicle. Every vehicle is crewed: a driver or pilot plus gunners. Each gun
/// fires only while its gunner lives, and the vehicle only moves while its driver does.
/// </summary>
public sealed class VehicleCrew
{
    public CrewSeat Driver { get; }
    public IReadOnlyList<CrewSeat> Gunners { get; }
    public IEnumerable<CrewSeat> Seats => Gunners.Prepend(Driver);

    public VehicleCrew(int gunners, double hp = 90)
    {
        Driver = new CrewSeat { Index = 0, Role = CrewRole.Driver, MaxHp = hp, Hp = hp };
        Gunners = Enumerable.Range(1, gunners)
            .Select(i => new CrewSeat { Index = i, Role = CrewRole.Gunner, MaxHp = hp, Hp = hp }).ToList();
    }

    public bool CanMove => Driver.Alive;
    public bool CanFire(int gun) => gun >= 0 && gun < Gunners.Count && Gunners[gun].Alive;
    public int AliveCount => Seats.Count(s => s.Alive);

    public CrewSeat Seat(int index) => index == 0 ? Driver : Gunners[index - 1];

    /// <summary>Returns true if this hit killed the crew member.</summary>
    public bool Damage(int seatIndex, double amount)
    {
        CrewSeat seat = Seat(seatIndex);
        if (!seat.Alive || amount <= 0) return false;
        seat.Hp = Math.Max(0, seat.Hp - amount);
        return seat.Hp == 0;
    }

    /// <summary>Everyone still alive climbs out. Returns how many.</summary>
    public int BailOut()
    {
        int n = 0;
        foreach (CrewSeat s in Seats.Where(s => s.Alive))
        {
            s.BailedOut = true;
            n++;
        }
        return n;
    }
}
