using System;

namespace GalHack.Items.Bridge;

/// <summary>
/// A structure's shield and its jump drives, on its power (design section 13). The shield takes hits before the hull;
/// it holds on a trickle of power, and refills on its generators' full draw plus dribbles of fold crystal from core
/// storage, only while it recharges, a while after the last hit. Switched off, or with no power, it is down. A drive
/// spools on its block's power, then burns its jump fuel from core storage; shields must be down to jump.
/// </summary>
public sealed partial class StructurePower
{
    double _shieldRecharge, _sinceHit = double.MaxValue, _foldOwed;

    /// <summary>The shield's hit points now and at most (its shield blocks' sum).</summary>
    public double ShieldHp { get; set; }
    public double ShieldMaxHp { get; private set; }

    /// <summary>The shield's switch (the chair bar's SHIELD toggle; the holder sets it each tick).</summary>
    public bool ShieldsOn { get; set; } = true;

    /// <summary>Holding: switched on with hit points left.</summary>
    public bool ShieldUp => ShieldsOn && ShieldHp > 0;

    /// <summary>This tick, refilling: switched on, short of its most, the cooldown since the last hit over, and fold crystal in core storage.</summary>
    public bool ShieldRecharging { get; private set; }

    bool RechargeDue()
    {
        Refresh();
        return ShieldsOn && ShieldMaxHp > 0 && ShieldHp < ShieldMaxHp && _sinceHit >= _rules.ShieldCooldown && _storage.Count(_rules.ShieldFuel) > 0;
    }

    /// <summary>A hit on the structure: the shield, while up, takes what it can of it. Returns what is left for the hull.</summary>
    public double Absorb(double amount)
    {
        if (!ShieldUp || amount <= 0) return amount;
        _sinceHit = 0;
        double taken = Math.Min(ShieldHp, amount);
        ShieldHp -= taken;
        return amount - taken;
    }

    void TickShield(double dt, bool on)
    {
        _sinceHit += dt;
        if (!on || !ShieldsOn || Supply <= 0 || ShieldMaxHp <= 0)
        {
            ShieldHp = 0; // down: it builds up again from nothing
            return;
        }
        ShieldHp = Math.Min(ShieldHp, ShieldMaxHp);
        if (!ShieldRecharging) return;
        double gain = Math.Min(ShieldMaxHp - ShieldHp, _shieldRecharge * Supply * dt);
        _foldOwed += gain * _rules.ShieldFoldPerHp;
        while (_foldOwed >= 1 && _storage.Remove(_rules.ShieldFuel, 1) > 0) _foldOwed -= 1;
        ShieldHp += gain;
    }

    // ---- Drives ---------------------------------------------------------------------------------

    double _jump;

    /// <summary>The drive spooling now (its function), or null, and its seconds left at full power.</summary>
    public string Spooling { get; private set; }
    public double SpoolLeft { get; private set; }

    /// <summary>The last jump made: the drive, how far (AU or LY), and the fuel items it burnt.</summary>
    public (string Function, double Distance, int Burnt)? LastJump { get; private set; }

    /// <summary>A jump made: the drive's function and how far. Whoever holds the structure moves it.</summary>
    public event Action<string, double> Jumped;

    /// <summary>Is a drive of this function spooling (its blocks draw)?</summary>
    public bool Engaged(string function) => Spooling == function;

    /// <summary>
    /// Starts a drive spooling for a jump of so far (AU or LY). Null when it starts; else what stops it, as the console
    /// shows it: NOT FITTED, SPOOLING, SHIELDS UP, NO FUEL (less than the jump burns in core storage) or NO POWER.
    /// </summary>
    public string Engage(string function, double distance)
    {
        if (Range(function) is not { } range) return "NOT FITTED";
        if (Spooling != null) return "SPOOLING";
        if (ShieldUp) return "SHIELDS UP";
        if (range.Reach < distance) return "NO FUEL";
        if (Supply <= 0) return "NO POWER";
        (Spooling, SpoolLeft, _jump) = (function, _rules.Drives[function].Spool, distance);
        return null;
    }

    /// <summary>Calls a spooling drive off: it winds down and nothing burns.</summary>
    public void Disengage() => Spooling = null;

    /// <summary>The spool's share done, 0 to 1 (0 with nothing spooling).</summary>
    public double SpoolShare => Spooling == null ? 0 : Math.Clamp(1 - SpoolLeft / _rules.Drives[Spooling].Spool, 0, 1);

    void TickDrive(double dt, bool on)
    {
        if (Spooling == null) return;
        if (!on)
        {
            Spooling = null; // power off: the spool winds down and nothing burns
            return;
        }
        SpoolLeft -= dt * Supply;
        if (SpoolLeft > 0) return;
        DriveFuel d = _rules.Drives[Spooling];
        int burnt = _storage.Remove(d.Item, (int)Math.Ceiling(_jump * d.Per - 1e-9));
        string function = Spooling;
        Spooling = null;
        LastJump = (function, _jump, burnt);
        Jumped?.Invoke(function, _jump);
    }
}
