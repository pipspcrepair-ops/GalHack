using System;
using Slice.Game;

namespace Slice.Player;

/// <summary>Where the survivor breathes: out in the world, the cryo bunker, a powered or unpowered sealed room, a place's shut room.</summary>
public enum RoomKind { None, Bunker, Sealed, Unpowered, Closed }

/// <summary>
/// One room's air (a vessel's or base's, a place's), on the suit's room clock. Powered, it eases to
/// the comfortable temperature and full oxygen; with the power off it holds a while, then drifts to
/// the outside's; let open, it is the outside's at once. Time between visits is caught up on the
/// next one, as though the power stood as it is now.
/// </summary>
public sealed class RoomAir
{
    public double Temperature, Oxygen;
    /// <summary>How long, room-clock seconds, its power has been off.</summary>
    public double Unpowered;
    double _clock;

    public RoomAir(double temperature, double oxygen, double clock) => (Temperature, Oxygen, _clock) = (temperature, oxygen, clock);

    static double Ease(double from, double to, double dt, double tau) => tau <= 0 ? to : to + (from - to) * Math.Exp(-dt / tau);

    /// <summary>Catches up to the clock: powered, toward comfortable; off, held, then toward the outside.</summary>
    public void Advance(double clock, bool powered, double outsideC, double outsideO2, SliceRules.ClimateRules rules)
    {
        double dt = Math.Max(0, clock - _clock);
        _clock = clock;
        if (powered)
        {
            Unpowered = 0;
            Temperature = Ease(Temperature, rules.SealedRoomC, dt, rules.RoomEaseSeconds);
            Oxygen = Ease(Oxygen, 21, dt, rules.RoomEaseSeconds);
            return;
        }
        double before = Unpowered;
        Unpowered += dt;
        double drift = Math.Min(dt, Math.Max(0, Unpowered - rules.RoomHoldSeconds)); // only the time past the hold
        if (before + dt <= rules.RoomHoldSeconds || drift <= 0) return;
        Temperature = Ease(Temperature, outsideC, drift, rules.RoomDriftSeconds);
        Oxygen = Ease(Oxygen, outsideO2, drift, rules.RoomDriftSeconds);
    }

    /// <summary>Toward a temperature (a place's shut room), its oxygen the outside's.</summary>
    public void Toward(double clock, double target, double tau)
    {
        double dt = Math.Max(0, clock - _clock);
        _clock = clock;
        Temperature = Ease(Temperature, target, dt, tau);
    }

    /// <summary>Opened to the world: the outside's air at once, the power's count left as it was.</summary>
    public void Let(double outsideC, double outsideO2, double clock)
    {
        (Temperature, Oxygen, _clock) = (outsideC, outsideO2, clock);
    }
}
