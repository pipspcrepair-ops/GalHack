using System.Collections.Generic;
using Godot;

namespace Boarding;

/// <summary>One weapon's numbers, from data/weapons.txt.</summary>
public sealed class WeaponDef
{
    public const string DefaultPath = "res://data/weapons.txt";

    public string Id = "";
    public string Name = "";
    public int Slot = 1;
    public float Damage = 10f;
    public float Rpm = 600f;
    public bool Automatic = true;
    public int Magazine = 30;
    public int Reserve = 120;
    public float ReloadSeconds = 1.5f;
    public int Pellets = 1;
    public float Spread = 0.5f;
    public float SpreadMoving = 1.5f;
    public float RecoilUp = 0.4f;
    public float[] RecoilPattern = { 0f };
    public float RecoilRecovery = 8f;
    public float Kick = 0.5f;
    public float Headshot = 2f;
    public float Range = 80f;
    public float Noise = 40f;

    public float SecondsPerShot => 60f / Rpm;

    public static List<WeaponDef> LoadAll(string path = DefaultPath)
    {
        var data = DataFile.Load(path);
        var defs = new List<WeaponDef>();
        foreach (string section in data.SectionOrder)
        {
            var def = new WeaponDef { Id = section };
            DataFile.ApplyTo(def, data.Section(section));
            defs.Add(def);
        }
        defs.Sort((a, b) => a.Slot.CompareTo(b.Slot));
        return defs;
    }
}

/// <summary>A weapon in the player's hands: ammo, fire cooldown, reload, and recoil step.</summary>
public sealed class Weapon
{
    public readonly WeaponDef Def;
    public int Ammo;
    public int Reserve;
    public float Cooldown;
    public float ReloadLeft;
    public int RecoilStep;

    public bool Reloading => ReloadLeft > 0f;
    public bool CanFire => !Reloading && Ammo > 0 && Cooldown <= 0f;

    public Weapon(WeaponDef def)
    {
        Def = def;
        Ammo = def.Magazine;
        Reserve = def.Reserve;
    }

    public void Tick(float dt)
    {
        // Cooldown may go negative inside a frame so fast weapons keep their exact rate.
        if (Cooldown > 0f) Cooldown -= dt;
        if (!Reloading) return;
        ReloadLeft -= dt;
        if (ReloadLeft > 0f) return;
        int needed = Def.Magazine - Ammo;
        int taken = Mathf.Min(needed, Reserve);
        Ammo += taken;
        Reserve -= taken;
        ReloadLeft = 0f;
    }

    public bool StartReload()
    {
        if (Reloading || Ammo >= Def.Magazine || Reserve <= 0) return false;
        ReloadLeft = Def.ReloadSeconds;
        return true;
    }

    /// <summary>Spends a round and returns this shot's sideways recoil from the fixed pattern.</summary>
    public float Shoot()
    {
        Ammo--;
        Cooldown += Def.SecondsPerShot;
        if (Cooldown < 0f) Cooldown = Def.SecondsPerShot * 0.5f;
        float side = Def.RecoilPattern[RecoilStep % Def.RecoilPattern.Length];
        RecoilStep++;
        return side;
    }
}
