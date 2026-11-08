using System;
using System.Collections.Generic;

namespace GalHack.Arms;

/// <summary>What a gun just did, for its sound and its look.</summary>
public enum GunEvent { Fired, Dry, Pumped, ReloadStarted, ReloadRound, Reloaded, Stage, ModeChanged }

/// <summary>
/// One weapon in someone's hands: what is loaded, the cooldown, the reload, the pump, the burst
/// and the charge. It never touches the world: each tick it says how many shots go (and at what
/// charge stage), and the shooter fires them. Ammo comes through <see cref="Take"/> (an item at a
/// time), whose owner decides where from: the player first, then the gun's origin, then the
/// nearest container in range. Rounds left over from an item stay with the gun (a box of 30
/// opened for a magazine of 12 keeps 18 loose), by ammo type, so switching shells loses nothing.
/// No wear and no jamming, ever.
/// </summary>
public sealed class Gun
{
    public readonly WeaponDef Base;
    public int ModeIndex { get; private set; }
    public WeaponDef Def => Base.Modes[ModeIndex];

    /// <summary>Rounds in the magazine, and which ammo they are.</summary>
    public int Loaded { get; private set; }
    public string LoadedAmmo { get; private set; }

    public float Cooldown { get; private set; }
    public float ReloadLeft { get; private set; }
    public float PumpLeft { get; private set; }
    public float ChargeTime { get; private set; }
    public bool Charging { get; private set; }
    public int BurstLeft { get; private set; }
    public int ShotsFired { get; private set; }
    public bool Reloading => ReloadLeft > 0f;

    /// <summary>Takes up to n ammo items of an id from wherever the owner keeps them; returns how many it got.</summary>
    public Func<string, int, int> Take = (_, _) => 0;
    /// <summary>How many ammo items of an id are within reach (to know a reload can start).</summary>
    public Func<string, int> Available = _ => 0;
    /// <summary>Rounds an ammo item gives when the weapon doesn't say (the item's own count).</summary>
    public Func<string, int> RoundsOf = _ => 1;

    public event Action<GunEvent, int> Did;

    readonly Dictionary<string, int> _loose = new(StringComparer.Ordinal);
    bool _wasHeld;

    public Gun(WeaponDef def) => Base = def.Base ?? def;

    /// <summary>Loose rounds of an ammo kept with the gun.</summary>
    public int Loose(string ammo) => _loose.TryGetValue(ammo, out int n) ? n : 0;

    /// <summary>Rounds that one item of this mode's ammo gives.</summary>
    public int PerItem => Def.PerItem > 0 ? Def.PerItem : Math.Max(1, RoundsOf(Def.Ammo));

    /// <summary>Rounds ready to load: loose ones plus everything in reach.</summary>
    public int Reserve => Loose(Def.Ammo) + Available(Def.Ammo) * PerItem;

    /// <summary>The charge stage reached so far (0 until the first stage is full).</summary>
    public int Stage => Def.Charge == null ? 0 : Math.Min(Def.Charge.Stages, (int)(ChargeTime / Math.Max(0.01f, Def.Charge.Seconds)));

    /// <summary>Steps to the next fire mode or shell type. A different ammo goes back loose and the new one loads.</summary>
    public void NextMode()
    {
        if (Base.Modes.Count < 2) return;
        string before = Def.Ammo;
        ModeIndex = (ModeIndex + 1) % Base.Modes.Count;
        Charging = false;
        ChargeTime = 0f;
        BurstLeft = 0;
        if (Def.Ammo != before && Loaded > 0)
        {
            _loose[LoadedAmmo ?? before] = Loose(LoadedAmmo ?? before) + Loaded;
            Loaded = 0;
        }
        Did?.Invoke(GunEvent.ModeChanged, ModeIndex);
        if (Loaded == 0) StartReload();
    }

    /// <summary>Starts a reload if there is room and anything to load.</summary>
    public bool StartReload()
    {
        if (Reloading || Loaded >= Def.Magazine || Reserve <= 0) return false;
        ReloadLeft = Def.Reload;
        Charging = false;
        ChargeTime = 0f;
        Did?.Invoke(GunEvent.ReloadStarted, 0);
        return true;
    }

    /// <summary>
    /// One tick with the trigger held or not (<paramref name="pressed"/> on the tick it went down).
    /// Returns how many shots go now; <paramref name="stage"/> is the charge stage for a charged one.
    /// </summary>
    public int Tick(float dt, bool held, bool pressed, out int stage)
    {
        stage = 0;
        bool released = _wasHeld && !held;
        _wasHeld = held;
        WeaponDef d = Def;
        if (Cooldown > 0f) Cooldown -= dt;
        if (PumpLeft > 0f && (PumpLeft -= dt) <= 0f) Did?.Invoke(GunEvent.Pumped, 0);

        if (Reloading)
        {
            // Shells go in one at a time, and a pull of the trigger with any in fires.
            if (d.ReloadEach && pressed && Loaded > 0) ReloadLeft = 0f;
            else
            {
                ReloadLeft -= dt;
                if (ReloadLeft > 0f) return 0;
                if (d.ReloadEach)
                {
                    if (Fill(1) > 0) Did?.Invoke(GunEvent.ReloadRound, Loaded);
                    if (Loaded < d.Magazine && Reserve > 0) { ReloadLeft = d.Reload; return 0; }
                }
                else Fill(d.Magazine - Loaded);
                ReloadLeft = 0f;
                Did?.Invoke(GunEvent.Reloaded, Loaded);
                return 0;
            }
        }

        if (Loaded <= 0)
        {
            if (pressed && !StartReload()) Did?.Invoke(GunEvent.Dry, 0);
            BurstLeft = 0;
            Charging = false;
            return 0;
        }
        if (PumpLeft > 0f) return 0;

        int shots = 0;
        switch (d.Trigger)
        {
            case Trigger.Semi:
            case Trigger.Pump:
                if (pressed && Cooldown <= 0f) shots = 1;
                break;
            case Trigger.Auto:
                while (held && Cooldown <= 0f && Loaded - shots > 0 && shots < 8)
                {
                    shots++;
                    Cooldown += d.SecondsPerShot;
                }
                if (shots > 0) Cooldown -= d.SecondsPerShot; // Shoot() below adds the last one back
                break;
            case Trigger.Hold:
                // A cone burns while held, a tick at a time.
                float every = d.Cone != null ? 1f / Math.Max(1f, d.Cone.Ticks) : d.SecondsPerShot;
                while (held && Cooldown <= 0f && Loaded - shots > 0 && shots < 8)
                {
                    shots++;
                    Cooldown += every;
                }
                if (shots > 0)
                {
                    Loaded -= shots;
                    ShotsFired += shots;
                    LoadedAmmo = d.Ammo;
                    Did?.Invoke(GunEvent.Fired, shots);
                    if (Loaded <= 0) StartReload();
                }
                return shots;
            case Trigger.Burst:
                if (pressed && Cooldown <= 0f && BurstLeft == 0) BurstLeft = d.Burst;
                if (BurstLeft > 0 && Cooldown <= 0f)
                {
                    shots = 1;
                    BurstLeft--;
                }
                break;
            case Trigger.Charge:
                if (pressed && Cooldown <= 0f) { Charging = true; ChargeTime = 0f; }
                if (Charging && held)
                {
                    int before = Stage;
                    ChargeTime += dt;
                    if (Stage > before) Did?.Invoke(GunEvent.Stage, Stage);
                }
                if (Charging && released)
                {
                    stage = Stage;
                    shots = 1;
                    Charging = false;
                    ChargeTime = 0f;
                }
                break;
        }
        if (shots <= 0) return 0;
        Shoot(shots, d);
        if (d.Trigger == Trigger.Charge) return shots; // stage already set
        return shots;
    }

    void Shoot(int shots, WeaponDef d)
    {
        Loaded -= shots;
        LoadedAmmo = d.Ammo;
        ShotsFired += shots;
        if (d.Trigger != Trigger.Auto) Cooldown = d.SecondsPerShot;
        else Cooldown += d.SecondsPerShot;
        if (Cooldown < 0f) Cooldown = d.SecondsPerShot * 0.5f;
        if (d.Pump > 0f) PumpLeft = d.Pump;
        Did?.Invoke(GunEvent.Fired, shots);
        if (Loaded <= 0)
        {
            BurstLeft = 0;
            StartReload();
        }
    }

    /// <summary>Loads up to n rounds of this mode's ammo, loose ones first, then an item at a time. Returns how many went in.</summary>
    int Fill(int n)
    {
        string ammo = Def.Ammo;
        int got = 0;
        while (got < n)
        {
            int loose = Loose(ammo);
            if (loose == 0)
            {
                if (Take(ammo, 1) <= 0) break;
                loose = PerItem;
            }
            int use = Math.Min(loose, n - got);
            got += use;
            _loose[ammo] = loose - use;
        }
        Loaded += got;
        if (got > 0) LoadedAmmo = ammo;
        return got;
    }

    /// <summary>Fills the magazine at once (a gun found loaded, tests).</summary>
    public void LoadNow()
    {
        ReloadLeft = 0f;
        Fill(Def.Magazine - Loaded);
    }
}
