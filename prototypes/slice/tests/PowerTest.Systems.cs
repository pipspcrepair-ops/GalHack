using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Items.Bridge;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Planets.Gen;
using Slice.Game;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// Power phase 2 (design section 13, owner 15 Nov): solar panels charge the capacitors by daylight before any fuel burns;
/// a structure's shield takes hits before the hull, holds on a trickle of power and refills on full power plus dribbles
/// of fold crystal, only while it recharges; the drives spool on their power, then burn their jump fuel from core
/// storage, shields down; guns draw while they fire, their ammo from core storage; an outage drops the core's gravity.
/// </summary>
public partial class PowerTest
{
    /// <summary>What the shields and drives are doing, as the vessel tells its power.</summary>
    static double Systems(StructurePower p, Structure s, PowerState state, Block b) => state switch
    {
        PowerState.Up => p.ShieldUp && !p.ShieldRecharging ? 1 : 0,
        PowerState.Recharging => p.ShieldRecharging ? 1 : 0,
        PowerState.Engaged => p.Engaged(s.TypeOf(b).Function) ? 1 : 0,
        _ => 0,
    };

    static double MwOf(Structure s, string function) =>
        s.Blocks().Where(x => s.TypeOf(x.block).Function == function).Sum(x => -s.TypeOf(x.block).Power);

    void Sun(GameData data)
    {
        GD.Print("-- solar panels --");
        PowerRules rules = data.Power;
        Structure s = Build(data, "-1 0 0 generator-t1", "-2 0 0 container-t1", "0 0 -1 light", "0 1 0 solar-panel", "-1 1 0 solar-panel");
        var store = new SharedStorage(data.Items, "Sun", BuildingBridge.StorageCapacity(s));
        var p = new StructurePower(rules, s, store);
        Func<PowerState, Int3, Block, double> lit = (st, c, b) => st == PowerState.Lit ? 1 : 0;
        double panels = p.SolarMW;
        p.Sun = 1;
        p.Tick(1.0 / 60, true, false, lit);
        Check(panels > 0 && Math.Abs(p.OutputMW - panels) < 1e-9 && p.Supply == 1 && store.IsEmpty,
            $"two panels in full sun give their {panels * 1000:F1} kW with no fuel at all, and the light is lit");
        p.Sun = 0.4;
        p.Tick(1.0 / 60, true, false, lit);
        Check(Math.Abs(p.OutputMW - panels * 0.4) < 1e-9, $"under a lower sun they give its share: {p.OutputMW * 1000:F2} kW at 40%");
        p.Sun = 0;
        p.ChargeMJ = 0;
        p.Tick(1.0 / 60, true, false, lit);
        Check(p.Supply == 0 && p.OutputMW == 0, "at night with no fuel and the capacitor empty: no power");

        store.Add("biofuel", 5);
        p.ChargeMJ = p.CapacityMJ;
        p.Sun = 1;
        Run(p, 60, lit);
        Check(store.Count("biofuel") == 5 && p.BurningUnits == 0 && double.IsPositiveInfinity(p.SecondsLeft) && p.Usage < 1,
            $"in full sun, the sun covers the light first: a minute burns no fuel ({p.Usage:P0} of the supply in use)");
        p.Sun = 0;
        Run(p, 5, lit);
        Check(store.Count("biofuel") == 4 && p.Supply == 1, "after dark the generator takes over on its fuel");
    }

    void Shield(GameData data)
    {
        GD.Print("-- the shield --");
        PowerRules rules = data.Power;
        Structure s = Build(data, "1 0 0 generator-t1", "-1 0 0 container-t1", "2 0 0 shield-generator");
        var store = new SharedStorage(data.Items, "Shield", BuildingBridge.StorageCapacity(s));
        store.Add("biofuel", 20);
        store.Add("fold-crystal", 3);
        var p = new StructurePower(rules, s, store);
        Func<PowerState, Int3, Block, double> doing = (st, c, b) => Systems(p, s, st, b);
        double mw = MwOf(s, "shield"), most = rules.Shields["shield-generator"].Hp;
        p.Tick(1.0 / 60, true, false, doing);
        Check(p.ShieldMaxHp == most && p.ShieldRecharging && Math.Abs(p.DrawMW - mw) < 1e-12,
            $"a shield generator holds {most:F0} points and charges up from nothing on its full {mw * 1000:F1} kW");
        Run(p, 9, doing);
        Check(p.ShieldHp == most && !p.ShieldRecharging && Math.Abs(p.DrawMW - 0.1 * mw) < 1e-12 && store.Count("fold-crystal") == 3,
            $"full in 8 s at {rules.Shields["shield-generator"].Recharge:F0} a second; holding, it draws a tenth ({p.DrawMW * 1000:F2} kW); the dribble owed is under a crystal yet");
        double hull = p.Absorb(1000);
        Check(hull == 0 && p.ShieldHp == most - 1000, "a hit of 1,000 the shield takes whole: nothing reaches the hull");
        hull = p.Absorb(5000);
        Check(hull == 5000 - (most - 1000) && p.ShieldHp == 0 && !p.ShieldUp, $"one of 5,000 brings it down and {hull:F0} goes through to the hull");
        Run(p, 10, doing);
        Check(p.ShieldHp == 0 && p.DrawMW == 0, $"down, it waits out its {rules.ShieldCooldown:F0} s cooldown drawing nothing");
        Run(p, 15, doing);
        Check(p.ShieldHp == most && store.Count("fold-crystal") == 2,
            $"then refills; two full charges burnt {2 * most * rules.ShieldFoldPerHp:F1} crystal's worth: one crystal taken ({store.Count("fold-crystal")} left)");
        p.ShieldsOn = false;
        p.Tick(1.0 / 60, true, false, doing);
        Check(p.ShieldHp == 0 && p.Absorb(100) == 100 && p.DrawMW == 0, "switched off, it is down and draws nothing: hits go to the hull");
        p.ShieldsOn = true;
        store.Remove("fold-crystal", store.Count("fold-crystal"));
        Run(p, 20, doing);
        Check(p.ShieldHp == 0 && !p.ShieldRecharging, "on again with no fold crystal in core storage, it can't recharge");
        store.Add("fold-crystal", 1);
        foreach (ItemDef f in rules.Fuels) store.Remove(f.Id, store.Count(f.Id));
        p.BurningUnits = 0;
        p.ChargeMJ = 0;
        Run(p, 2, doing);
        Check(p.ShieldHp == 0, "with no power, no shield");
    }

    void Drives(GameData data)
    {
        GD.Print("-- the drives --");
        PowerRules rules = data.Power;
        Structure s = Build(data, "1 0 0 generator-t1", "-1 0 0 container-t1", "2 0 0 shield-generator", "0 1 0 warp-drive", "0 3 0 hyperdrive-t1");
        var store = new SharedStorage(data.Items, "Drives", BuildingBridge.StorageCapacity(s));
        store.Add("biofuel", 50);
        store.Add("warp-cell", 5);
        store.Add("fold-crystal", 3);
        var p = new StructurePower(rules, s, store);
        Func<PowerState, Int3, Block, double> doing = (st, c, b) => Systems(p, s, st, b);
        Run(p, 10, doing);
        Check(p.ShieldUp && p.Engage("warp-drive", 3.2) == "SHIELDS UP", "with its shield up, the warp drive won't engage");
        p.ShieldsOn = false;
        p.Tick(1.0 / 60, true, false, doing);
        Check(p.Engage("warp-drive", 6) == "NO FUEL" && p.Engage("jump-gate", 1) == "NOT FITTED", "shields down: 6 AU is past its 5 warp cells, and there's no such drive as a jump gate");
        DriveFuel warp = rules.Drives["warp-drive"], fold = rules.Drives["hyperdrive"];
        Check(p.Engage("warp-drive", 3.2) == null && p.Spooling == "warp-drive", "3.2 AU: the warp drive spools");
        p.Tick(1.0 / 60, true, false, doing);
        double mw = MwOf(s, "warp-drive");
        Check(Math.Abs(p.DrawMW - mw) < 1e-9, $"spooling, it draws its {mw:F2} MW from the capacitors");
        Run(p, warp.Spool - 0.5, doing);
        Check(p.Spooling != null && store.Count("warp-cell") == 5, $"nothing burns while it spools ({p.SpoolLeft:F1} s left of {warp.Spool:F0})");
        Run(p, 1, doing);
        Check(p.Spooling == null && p.LastJump == ("warp-drive", 3.2, 4) && store.Count("warp-cell") == 1 && p.DrawMW == 0,
            $"spooled, it jumps and burns 4 warp cells for 3.2 AU ({store.Count("warp-cell")} left), and draws nothing after");
        Check(p.Engage("hyperdrive", 2.5) == null, "2.5 LY: the hyperdrive spools");
        Run(p, fold.Spool + 0.5, doing);
        Check(p.LastJump == ("hyperdrive", 2.5, 3) && store.Count("fold-crystal") == 0 && p.Range("hyperdrive") is { Reach: 0, Unit: "LY" },
            $"after its {fold.Spool:F0} s it burns 3 fold crystals for 2.5 LY, and reads 0 LY left");
        Check(p.Engage("warp-drive", 1) == null, "the last warp cell's AU: spooling");
        p.Tick(1.0 / 60, false, false, doing);
        Check(p.Spooling == null && store.Count("warp-cell") == 1, "power switched off mid-spool: it winds down and nothing burns");
    }

    // ---- In the world ------------------------------------------------------------------------------------

    IEnumerable<int> Armed(GameData data)
    {
        GD.Print("-- a base with a gun, a shield, a solar panel and gravity --");
        Structure s = Build(data, "1 0 0 generator-t1", "-1 0 0 container-t1", "0 0 -1 gatling", "2 0 0 shield-generator", "0 1 0 solar-panel", "-2 0 0 iron-plated");
        s.Name = "Armed base";
        s.GravityOn = true;
        Vector3 spot = new Playthrough(_game).FlatSpot(_game.Survivor.WorldFeet, 50f, span: 10f);
        Vessel v = Vessel.SpawnBase(_game.Zone, s, (spot + Vector3.Up * 0.5f).ToNumerics(), 0f);
        v.Storage.Add("biofuel", 40);
        v.Storage.Add("belt-12mm", 2);
        v.Storage.Add("fold-crystal", 2);
        foreach (int x in _drive.Wait(60 * 10)) yield return x;
        StructurePower p = v.Power;
        Check(v.Powered && v.Body.Core.On && p.ShieldHp == p.ShieldMaxHp && p.ShieldMaxHp > 0,
            $"set down with fuel in its storage: powered, its gravity on, its shield full ({p.ShieldHp:F0})");

        // The sun on its panel: the sun's height here, less the cloud.
        float height = _game.Zone.Sky.ToSun.Y;
        Check(p.Sun >= 0 && p.Sun <= Math.Max(0, height) + 1e-6 && (height > 0 || p.Sun == 0) && Math.Abs(p.SolarNowMW - p.SolarMW * p.Sun) < 1e-12,
            $"its panel gets the sun's share: the sun {height:F2} high, {p.Sun:P0} of full sun, {p.SolarNowMW * 1000:F2} kW");

        // The gun: it draws while it fires, its belts from core storage.
        BarSlot gun = v.Bar.Arranged().FirstOrDefault(f => f?.Act == BarAct.Select);
        if (!Check(gun != null, "the gatling has its slot on the chair bar")) yield break;
        v.Bar.Picked.Add(gun.Key);
        int rounds = v.Bar.Rounds(gun);
        double gunMw = MwOf(v.Structure, "weapon"), firing = 0;
        for (int t = 0; t < 60; t++)
        {
            v.Bar.Fire(_game.Arms, 1f / 60, true, t == 0);
            yield return 0;
            firing = Math.Max(firing, p.DrawMW);
        }
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(firing >= gunMw - 1e-9 && p.DrawMW < gunMw && v.Bar.Rounds(gun) < rounds && v.Storage.Count("belt-12mm") < 2,
            $"firing a second, the gatling draws its {gunMw * 1000:F0} kW ({firing * 1000:F1} in all), nothing after; {rounds - v.Bar.Rounds(gun)} rounds gone, a belt taken from core storage");

        // A hit: the shield takes it, then with the shield off the hull does.
        Int3 at = new(-2, 0, 0);
        float before = v.Structure.Get(at).Hp;
        double shield = p.ShieldHp;
        v.Hits.Shot(at, v.ToWorld(StructureBridge.CellCenter(v.Structure, at)).ToNumerics(), -N.Vector3.UnitX, 160f, DamageType.Kinetic);
        foreach (int x in _drive.Wait(5)) yield return x;
        string ShieldSlot() => _game.Visor.BarSlots(v).FirstOrDefault(x => x?.Icon == Slice.Ui.HudIcon.Shield)?.Value;
        Check(p.ShieldHp == shield - 160 && v.Structure.Get(at).Hp == before && ShieldSlot() == $"{100 * p.ShieldHp / p.ShieldMaxHp:F0}%",
            $"a 160 round on the hull is the shield's: {shield:F0} to {p.ShieldHp:F0}, the plate untouched; the bar's SHIELD slot reads {ShieldSlot()}");
        ShieldShell shell = v.Node.GetChildren().OfType<ShieldShell>().FirstOrDefault();
        Check(shell is { Visible: true, Rippling: true }, "the shield ripples where it was struck: its shell shows");
        foreach (int x in _drive.Wait(60)) yield return x;
        Check(shell is { Visible: false, Rippling: false }, "and fades out within a second, hidden till the next hit");
        v.SetSystem(StructureBar.Shields, false);
        foreach (int x in _drive.Wait(2)) yield return x;
        v.Hits.Shot(at, v.ToWorld(StructureBridge.CellCenter(v.Structure, at)).ToNumerics(), -N.Vector3.UnitX, 160f, DamageType.Kinetic);
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(p.ShieldHp == 0 && ShieldSlot() == "OFF" && v.Structure.Get(at).Hp < before,
            $"the bar's SHIELD switched off: down, and the next round hits the plate ({before:F0} to {v.Structure.Get(at).Hp:F0})");
        v.SetSystem(StructureBar.Shields, true);

        // An outage drops the gravity; power back brings it back. The panel comes off first, so the sun can't keep it going.
        Building.Remove(v.Structure, new Int3(0, 1, 0), null);
        v.ApplyEdits();
        foreach (ItemDef f in data.Power.Fuels) v.Storage.Remove(f.Id, v.Storage.Count(f.Id));
        p.BurningUnits = 0;
        p.ChargeMJ = 0; // STAGING: run dry at once
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(!v.Powered && !v.Body.Core.On && v.Structure.GravityOn, "out of fuel, its panel gone: no power, and its gravity fails though still switched on");
        v.Storage.Add("biofuel", 5);
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(v.Powered && v.Body.Core.On, "fuel back in its storage: power, and its gravity with it");
        _game.Zone.Remove(v);
    }
}
