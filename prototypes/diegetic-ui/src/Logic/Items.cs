using System;
using System.Collections.Generic;

namespace DiegeticUi.Logic;

/// <summary>The environmental hazards gear can protect against. Shown as a row of lamps on the wrist.</summary>
[Flags]
public enum Hazard
{
    None = 0,
    Cold = 1,
    Heat = 2,
    Radiation = 4,
    Toxic = 8,
    Vacuum = 16,
}

public enum ItemKind { Ore, Part, Consumable, Armor, Booster, Tool }

/// <summary>
/// One kind of item. Mass and volume are per unit: they are the two numbers a player juggles
/// when packing, so every screen that lists items shows them.
/// </summary>
public sealed record ItemDef(
    string Id,
    string Name,
    double MassKg,
    double VolumeL,
    ItemKind Kind,
    int BaseValue,
    Hazard Protects = Hazard.None);

/// <summary>The prototype's item list. In the game this is data (a text file with a schema).</summary>
public static class Catalog
{
    static readonly Dictionary<string, ItemDef> Items = new();

    public static readonly ItemDef IronOre = Add(new("iron_ore", "IRON ORE", 2.0, 1.0, ItemKind.Ore, 12));
    public static readonly ItemDef CopperOre = Add(new("copper_ore", "COPPER ORE", 2.5, 1.0, ItemKind.Ore, 18));
    public static readonly ItemDef Ice = Add(new("ice", "ICE", 1.0, 1.0, ItemKind.Ore, 4));
    public static readonly ItemDef Polymer = Add(new("polymer", "POLYMER", 0.5, 1.0, ItemKind.Ore, 22));
    public static readonly ItemDef BioGel = Add(new("bio_gel", "BIO-GEL", 0.5, 0.5, ItemKind.Ore, 40));
    public static readonly ItemDef IronPlate = Add(new("iron_plate", "IRON PLATE", 1.5, 0.5, ItemKind.Part, 30));
    public static readonly ItemDef CopperWire = Add(new("copper_wire", "COPPER WIRE", 0.5, 0.5, ItemKind.Part, 14));
    public static readonly ItemDef Motor = Add(new("motor", "MOTOR", 4.0, 2.0, ItemKind.Part, 120));
    public static readonly ItemDef RoverWheel = Add(new("rover_wheel", "ROVER WHEEL", 9.0, 6.0, ItemKind.Part, 320));
    public static readonly ItemDef HullPlate = Add(new("hull_plate", "HULL PLATE", 6.0, 2.0, ItemKind.Part, 140));
    public static readonly ItemDef O2Canister = Add(new("o2_canister", "O2 CANISTER", 2.0, 1.5, ItemKind.Consumable, 35));
    public static readonly ItemDef MedKit = Add(new("medkit", "MED KIT", 1.0, 1.0, ItemKind.Consumable, 60));
    public static readonly ItemDef Ration = Add(new("ration", "RATION", 0.5, 0.5, ItemKind.Consumable, 8));
    public static readonly ItemDef Multitool = Add(new("multitool", "MULTITOOL", 3.0, 2.0, ItemKind.Tool, 200));
    public static readonly ItemDef ThermalPlate = Add(new("thermal_armor", "THERMAL PLATE", 6.0, 4.0, ItemKind.Armor, 400,
        Hazard.Cold | Hazard.Vacuum));
    public static readonly ItemDef HazmatShell = Add(new("hazmat_shell", "HAZMAT SHELL", 5.0, 4.0, ItemKind.Armor, 300,
        Hazard.Toxic | Hazard.Vacuum));
    public static readonly ItemDef LeadLiner = Add(new("lead_liner", "LEAD LINER", 4.0, 1.0, ItemKind.Booster, 260, Hazard.Radiation));
    public static readonly ItemDef Rebreather = Add(new("rebreather", "REBREATHER", 1.5, 1.0, ItemKind.Booster, 180, Hazard.Toxic));
    public static readonly ItemDef Coolant = Add(new("coolant_loop", "COOLANT LOOP", 2.0, 1.0, ItemKind.Booster, 220, Hazard.Heat));

    public static ItemDef Get(string id) => Items[id];

    public static IEnumerable<ItemDef> All => Items.Values;

    static ItemDef Add(ItemDef item)
    {
        Items[item.Id] = item;
        return item;
    }
}
