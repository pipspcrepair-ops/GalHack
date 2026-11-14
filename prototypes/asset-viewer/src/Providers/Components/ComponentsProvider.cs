using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Structures;
using AssetViewer.Providers.Parts;
using Godot;
using Structure = GalHack.Structures.Structure;

namespace AssetViewer.Providers.Components;

/// <summary>
/// The core components of every structure (everything in the block catalogue that is not armour:
/// chairs, thrusters, generators, fuel tanks, containers, gyros, hover plates, landing gear, drives,
/// fabricators, weapons, doors, lights, ladders and stairs), one entry each, grouped by what they do,
/// plus one row per family that stands every size side by side. Each is the block drawn by the slice's
/// own mesher, with a person beside it for scale, and works as it does in the game: the chair is sat
/// in, thrusters fire, landing gear folds, doors open for whoever walks through, lamps run their modes,
/// power is cut and restored, and damage plays over all of it. Entries come from the catalogue, so a
/// new component appears by itself.
/// </summary>
public sealed class ComponentsProvider : IAssetProvider
{
    public string Category => "Components";

    /// <summary>Which family each function belongs to. A function not listed lands in "Other"; the test fails on it.</summary>
    static readonly Dictionary<string, string> Families = new()
    {
        ["core"] = "Control", ["chair"] = "Control",
        ["generator"] = "Power", ["fuel"] = "Power",
        ["thruster"] = "Propulsion", ["gyro"] = "Propulsion", ["hover"] = "Propulsion", ["gear"] = "Propulsion",
        ["warp-drive"] = "Travel", ["warp-tank"] = "Travel", ["hyperdrive"] = "Travel", ["fold-tank"] = "Travel",
        ["constructor"] = "Production", ["deconstructor"] = "Production", ["factory"] = "Production",
        ["container"] = "Storage",
        ["weapon"] = "Defence", ["turret"] = "Defence",
        ["door"] = "Access", ["ladder"] = "Access", ["stairs"] = "Access",
        ["light"] = "Utility", ["forecast"] = "Utility",
    };

    public static string FamilyOf(BlockType t) => Families.TryGetValue(t.Function, out string f) ? f : "Other";

    public const string Beside = "Standing beside", Seated = "Seated", NoPerson = "None";
    public const string AtNozzle = "At the nozzle", AsFlown = "As the game flies it";

    public IEnumerable<AssetEntry> Entries()
    {
        Catalogue cat = PartsData.Blocks;
        var devices = cat.Types.Skip(1).Where(t => !t.IsArmor).ToList();
        foreach (BlockType t in devices)
            yield return Single(cat, t);
        foreach (var family in devices.GroupBy(t => t.Function).Where(g => g.Count() > 1))
            yield return Tiers(cat, family.Key, family.ToList());
    }

    static AssetEntry Single(Catalogue cat, BlockType t)
    {
        List<AssetOption> options = BlocksProvider.OptionsFor(cat, t);
        options.Insert(0, new AssetOption("Person", t.IsCommandChair ? new[] { Seated, NoPerson } : new[] { Beside, NoPerson }));
        if (t.IsThruster) options.Add(new AssetOption("Flame", AtNozzle, AsFlown));
        return new AssetEntry
        {
            Id = "component." + t.Id, Group = FamilyOf(t), Name = t.Name, Randomisable = true,
            Options = options.ToArray(), Build = r => BuildSingle(cat, t, r),
        };
    }

    static AssetInstance BuildSingle(Catalogue cat, BlockType t, BuildRequest r)
    {
        // A ladder is a few rungs high and a stair a flight, or there is nothing to climb.
        Int3 step = t.IsLadder ? new Int3(0, 1, 0) : t.Function == "stairs" ? new Int3(0, 1, 1) : default;
        StructureAsset a = BlocksProvider.BuildBlock(cat, t, r, r.Get("Flame", AtNozzle) == AtNozzle, step, step == default ? 1 : 3);
        ComponentActs.Add(a, r.Get("Person", NoPerson) != NoPerson);
        return a;
    }

    // ---- Every size of a family in a row --------------------------------------------------------

    static AssetEntry Tiers(Catalogue cat, string function, List<BlockType> types) => new()
    {
        Id = "tiers." + function, Group = "All sizes", Name = PartsData.Nice(function) + "s, every size", Randomisable = false,
        Options = new[]
        {
            new AssetOption("Person", Beside, NoPerson),
            new AssetOption("Orientation", BlocksProvider.Orients) { Default = Math.Max(0, Array.IndexOf(BlocksProvider.Orients, types[0].IsThruster ? BlocksProvider.Orients[0] : "+y+z")) },
            new AssetOption("Power", "On", "Off"),
            new AssetOption("Damage", BlocksProvider.Damages),
        },
        Build = r => BuildTiers(cat, types, r),
    };

    static AssetInstance BuildTiers(Catalogue cat, List<BlockType> types, BuildRequest r)
    {
        var s = new Structure(cat) { Name = types[0].Function };
        int rot = Math.Max(0, Array.IndexOf(BlocksProvider.Orients, r.Get("Orientation")));
        int damage = Math.Max(0, Array.IndexOf(BlocksProvider.Damages, r.Get("Damage")));
        int cursor = 0;
        foreach (BlockType t in types)
        {
            var (lo, hi) = Footprint.Box(t, rot);
            // Each stands on the ground, its own footprint clear of the last by a cell.
            var anchor = new Int3(cursor - lo.X, -lo.Y, -lo.Z);
            Block b = Building.Make(s, t, null, rot);
            b.Hp = s.MaxHp(b) * BlocksProvider.DamageHp[damage];
            s.Set(anchor, b);
            cursor += hi.X - lo.X + 2;
        }
        var asset = new StructureAsset(s) { Powered = r.Get("Power", "On") != "Off" };
        asset.FactsFn = () => TierFacts(s, types);
        Working.Add(asset);
        ComponentActs.Standing(asset, r.Get("Person", NoPerson) != NoPerson);
        return asset;
    }

    static IEnumerable<(string, string)> TierFacts(Structure s, List<BlockType> types)
    {
        foreach (BlockType t in types)
        {
            string stat = t.Thrust > 0 ? $"{t.Thrust:0.#} kN" : t.Power > 0 ? $"{t.Power:0.##} MW" : t.Fuel > 0 ? $"{t.Fuel:0} fuel" : t.Storage > 0 ? $"{t.Storage:0} SU" : t.Torque > 0 ? $"{t.Torque:0.#} torque" : t.Lift > 0 ? $"{t.Lift:0.#} kN lift" : "";
            yield return (t.Name, $"{t.Size.X} x {t.Size.Y} x {t.Size.Z} m, {t.Mass:0.##} t" + (stat == "" ? "" : ", " + stat));
        }
    }
}
