using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using GalHack.Arms;
using GalHack.Items.Production;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using Slice.Game;
using Slice.Ui;
using Slice.Vessels;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace Slice.Tests;

/// <summary>
/// The structure's toolbar in a command chair (design: Visor HUD layout), on a fresh random world, through the game's
/// own keys and mouse. A landed vessel with two spotlights, two gatlings, a turret, a scanner, a portable fabricator,
/// an interior light and its core (gravity, life support): seated, the bar lists exactly those functions, equipment at
/// the left end and core systems at the right, and no doors, gear, force fields or shields. Each number switches the
/// real thing: the spotlights (not the light inside), the turrets, gravity, life support (the sealed air) and the
/// fabricators (a job waits where it stands and goes on). The scanner's slot scans; no shields slot without a shield. The gatlings' slot picked, the
/// trigger fires them: core storage's belts are spent, the shots leave the muzzles along the guns' facing at 600 m/s,
/// and they hit a wall ahead. Dragged on the Core tab, the arrangement is kept, survives a blueprint's save and load
/// (with the switches), and a structure built from it shows the same bar. Standing up brings back BAG.
///
/// Run: G:\GalHack\.tools\queue\test-q.ps1 -Project prototypes/slice -Scene res://tests/BarTest.tscn
/// </summary>
public partial class BarTest : Node
{
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();

    public override void _Ready()
    {
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Check(false, $"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        GD.Print(_failures == 0 ? $"ALL PASSED ({_passes} checks in {_clock.Elapsed.TotalSeconds:F1} s)" : $"{_failures} CHECK(S) FAILED ({_passes} passed)");
        SetPhysicsProcess(false);
        Wildlife.Shutdown.Quit(this, _failures == 0 ? 0 : 1);
    }

    void Check(bool ok, string what)
    {
        if (ok) _passes++;
        else _failures++;
        GD.Print($"  {(ok ? "ok   " : "FAIL ")} {what}");
    }

    /// <summary>The bar's default order on the rig: equipment from the left, core systems from the right.</summary>
    static readonly string[] Expected = { "spotlights", "guns.gatling", "turrets", "scanner", null, null, "fabricators", "life-support", "gravity" };

    /// <summary>The rig's chair, the cells its guns, spotlights and fabricator stand on.</summary>
    public static readonly Int3 Chair = new(0, 1, -1), Fabricator = new(2, 1, 2), Light = new(-2, 1, 2), Turret = new(3, 1, -3);
    public static readonly Int3[] Guns = { new(-2, 0, -4), new(2, 0, -4) }, Spots = { new(-1, 0, -4), new(1, 0, -4) };

    /// <summary>
    /// The test's vessel: a 7 x 7 iron floor, its core, generator and two containers at the back, a command chair facing
    /// ahead (-z), two spotlights and two gatlings on the floor's front edge, a turret and a scanner on its front corners,
    /// a portable fabricator and a light inside; landed near a site, heading north. With <paramref name="wall"/>,
    /// a target wall of iron plates eight deep on a base, that far ahead.
    /// </summary>
    public static Vessel Rig(Game.Game game, Vector3 site, float wall, out Vessel target)
    {
        float heading = Clearest(game, site, wall);
        Catalogue cat = game.Zone.Data.Catalogue;
        var s = new Structure(cat) { Name = "Bar Skiff", Mode = CoreMode.Flight };
        Block Make(string id) => Building.Make(s, cat.Type(id));
        for (int x = -3; x <= 3; x++)
            for (int z = -3; z <= 3; z++) s.Set(new Int3(x, 0, z), Make("iron-plated"));
        s.Set(new Int3(-3, 1, 3), Building.Make(s, cat.CoreType));
        s.Set(new Int3(3, 1, 3), Make("generator-t1"));
        s.Set(new Int3(-3, 1, 1), Make("container-t1"));
        s.Set(new Int3(-3, 1, 2), Make("container-t1"));
        s.Set(Chair, Make("command-chair"));
        foreach (Int3 g in Guns) s.Set(g, Make("gatling"));
        foreach (Int3 p in Spots) s.Set(p, Make("spotlight"));
        s.Set(Turret, Make("turret"));
        s.Set(new Int3(-3, 1, -3), Make("scanner-t1"));
        s.Set(Fabricator, Make("portable-fabricator"));
        s.Set(Light + new Int3(0, 0, 1), Make("iron-plated"));
        s.Set(Light, Make("light"));
        s.DirtyChunks.Clear();
        Vessel v = Vessel.Spawn(game.Zone, s, (site + Vector3.Up * 2f).ToNumerics(), heading, landed: true);
        target = null;
        if (wall <= 0f) return v;
        // From under the ground at its foot to 6 m over the site's, wherever the ground between falls away.
        Vector3 at = site + Ahead(heading) * wall;
        float siteY = game.Zone.GroundAt(site.X, site.Z).Y;
        at.Y = MathF.Min(game.Zone.GroundAt(at.X, at.Z).Y, siteY) - 2f;
        int top = (int)MathF.Ceiling((siteY + 6f - at.Y) / cat.Grid.CellSize);
        var w = new Structure(cat) { Name = "Bar Target" };
        // Eight deep: a gatling's round takes an iron plated block (160 against 158), and the next goes on through the hole.
        for (int x = -6; x <= 6; x++)
            for (int y = 0; y <= top; y++)
                for (int z = 0; z < 8; z++) w.Set(new Int3(x, y, -z), Building.Make(w, cat.Type("iron-plated")));
        w.Set(new Int3(0, 0, 1), Building.Make(w, cat.CoreType));
        w.DirtyChunks.Clear();
        target = Vessel.SpawnBase(game.Zone, w, at.ToNumerics(), heading);
        return v;
    }

    /// <summary>Ahead along a heading, level.</summary>
    static Vector3 Ahead(float heading) => GalHack.Physics.MathX.Rotate(GalHack.Physics.MathX.Yaw(heading), -N.Vector3.UnitZ).ToGodot();

    /// <summary>Of sixteen headings from a site, the one whose ground rises least over the first <paramref name="reach"/> metres (the guns fire level, just above the ground).</summary>
    static float Clearest(Game.Game game, Vector3 site, float reach)
    {
        float baseY = game.Zone.GroundAt(site.X, site.Z).Y, best = 0f, lowest = float.MaxValue;
        for (int k = 0; k < 16; k++)
        {
            float h = k * MathF.Tau / 16f, rise = float.MinValue;
            Vector3 ahead = Ahead(h);
            for (float d = 4f; d <= reach; d += 1f)
            {
                Vector3 p = site + ahead * d;
                rise = MathF.Max(rise, game.Zone.GroundAt(p.X, p.Z).Y - baseY);
            }
            if (rise < lowest) (lowest, best) = (rise, h);
        }
        return best;
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        var play = new Playthrough(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;

        foreach (var k in _game.Zone.Data.Rules.StructureBar.Kinds)
        {
            bool known = true;
            try { _ = Visor.IconOf(k); _ = StructureBar.ActOf(k); }
            catch (Exception) { known = false; }
            if (!known) Check(false, $"bar kind '{k.Id}' has an icon drawn by code and an act ({k.Icon}, {k.Act})");
        }
        Check(_game.Zone.Data.Rules.StructureBar.Kinds.All(k => k.Functions.All(f => f is not ("door" or "gear" or "field-emitter"))),
            "no doors, landing gear or force fields among the bar's kinds (owner: they stay off the bar)");

        Vector3 site = play.FlatSpot(_game.Start, 40f, span: 34f);
        _game.Survivor.PlaceOutside((site + new Vector3(8f, 2f, 6f)).ToNumerics());
        Vessel v = Rig(_game, site, 20f, out Vessel target);
        Structure s = v.Structure;
        foreach (int x in _drive.Wait(30)) yield return x;
        v.TakeChair(Chair);
        Check(_game.Survivor.Sit(v) && _game.Survivor.Seated == v, "seated in the command chair");
        foreach (int x in _drive.Wait(20)) yield return x;

        // ---- What fills it ------------------------------------------------------------------------------
        string[] keys = v.Bar.Arranged().Select(f => f?.Key).ToArray();
        Check(keys.SequenceEqual(Expected), $"the bar lists exactly the rig's functions, equipment left and core systems right: {Show(keys)}");
        BarShown?[] shown = _game.Visor.BarSlots(v);
        Check(shown[1] is BarShown gun && gun.Label == "GATLING" && gun.Icon == HudIcon.Gun && shown[8] is BarShown grav && grav.Icon == HudIcon.Gravity && grav.Value == "ON",
            $"the visor shows each slot's label, icon and state ({string.Join(", ", shown.Select(b => b is BarShown x ? $"{x.Label} {x.Icon} {x.Value}" : "-"))})");
        Check(v.Bar.Functions.All(f => f.Kind.Id != "shields"), "no shields slot until the structure has a shield generator");

        int Slot(string key) => Array.IndexOf(keys, key);
        IEnumerable<int> Press(string key) => _drive.Tap(Key.Key1 + Slot(key));

        // ---- Toggles switch the real devices ------------------------------------------------------------
        bool SpotsLit() => Spots.All(p => !s.Get(p).Off);
        Check(SpotsLit() && !s.Get(Light).Off, "spotlights and the light inside are lit");
        foreach (int x in Press("spotlights")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(Spots.All(p => s.Get(p).Off) && !s.Get(Light).Off && _game.Visor.BarSlots(v)[Slot("spotlights")]?.Value == "OFF",
            "its number switches both spotlights off, and only them: the light inside stays lit");
        foreach (int x in Press("spotlights")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(SpotsLit(), "and on again");

        // A turret switched off looks off: its status light dark, its barrels parked low in the dome; on, run out level.
        BlockType turret = s.Catalogue.Type("turret");
        string Barrels() => s.Get(Turret) is var tb && turret.Parts.Any(p => p.OffOnly && p.Shows(tb)) && !turret.Parts.Any(p => p.OnOnly && p.Shows(tb)) ? "parked" : "run out";
        double use = v.StructureStats.PowerUse;
        Check(!s.Get(Turret).Off && Barrels() == "run out", "the turret is on, its barrels run out");
        foreach (int x in Press("turrets")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(s.Get(Turret).Off && Barrels() == "parked" && v.StructureStats.PowerUse < use && MountedNone(),
            $"the turrets' number switches them off: barrels parked, light dark, {use - v.StructureStats.PowerUse:0.###} MW less drawn; they never fire from the trigger");
        foreach (int x in Press("turrets")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!s.Get(Turret).Off && Barrels() == "run out", "and on again, ready");

        foreach (int x in Press("gravity")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!s.GravityOn && v.Body.Core?.On == false, $"gravity off: the core's switch and its field ({v.Body.Core?.On})");
        foreach (int x in Press("gravity")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(s.GravityOn && v.Body.Core?.On == true, "gravity on again");

        Check(v.Powered && v.LifeSupport, "life support runs on a powered structure");
        foreach (int x in Press("life-support")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(!v.SystemOn(StructureBar.LifeSupport) && !v.LifeSupport && !_game.Suit.HoldsAir(v), "life support off: the structure's air is no longer kept");
        foreach (int x in Press("life-support")) yield return x;

        // The fabricator: a job under way waits where it stands while they are off, and goes on when they are on.
        Constructor fab = v.Machines.TryGetValue(Fabricator, out Constructor c) ? c : null;
        Check(fab != null, "the portable fabricator is the structure's constructor");
        if (fab != null)
        {
            fab.Lock = null;
            var item = _game.Zone.Data.Items.Items.FirstOrDefault(i => i.Recipe != null && fab.Knows(i) && i.Recipe.Seconds >= 3 && i.Recipe.Inputs.All(p => p.Item.Category == GalHack.Items.ItemCategory.Raw));
            if (item != null) foreach (var (input, n) in item.Recipe.Inputs) v.Storage.Add(input.Id, n * 4);
            OrderResult order = item == null ? default : fab.Order(item.Id, 2);
            Check(order.Ok, $"it takes an order for {item?.Name ?? "nothing it can make"} ({order.Message})");
            foreach (int x in play.WaitFor(() => fab.Current?.Progress > 0, 10)) yield return x;
            foreach (int x in Press("fabricators")) yield return x;
            foreach (int x in _drive.Wait(2)) yield return x;
            double held = fab.Current?.Progress ?? -1;
            foreach (int x in _drive.Wait(60)) yield return x;
            Check(held > 0 && fab.Current?.Progress == held && !v.SystemOn(StructureBar.Fabricators), $"fabricators off: the job waits where it stands ({held:F2})");
            foreach (int x in Press("fabricators")) yield return x;
            foreach (int x in _drive.Wait(30)) yield return x;
            Check((fab.Current?.Progress ?? 99) > held || fab.Orders.Count == 0 || fab.Orders[0].RunsDone > 0, $"on again: it goes on ({fab.Current?.Progress:F2})");
        }

        // The scanner's slot runs the structure's scan at its scanner's range.
        double waitBefore = _game.ScanWait;
        foreach (int x in Press("scanner")) yield return x;
        foreach (int x in _drive.Wait(2)) yield return x;
        Check(waitBefore <= 0 && _game.ScanWait > 0 && _game.ScanRangeOf(v) > 0, $"the scanner's number runs a scan at its {_game.ScanRangeOf(v):F0} m ({_game.Pings.Count} places marked)");

        // ---- Firing ---------------------------------------------------------------------------------------
        const string Belt = "belt-12mm";
        v.Storage.Add(Belt, 3);
        int belts = v.Storage.Count(Belt), fired = _game.Arms.MountedFired;
        int onTarget = 0, ground = _game.Arms.GroundHits, blocks = _game.Arms.BlockHits;
        var met = new List<string>();
        void Dealt(GalHack.Arms.HitKind kind, object thing, float amount)
        {
            if (kind == GalHack.Arms.HitKind.Block && thing == target) onTarget++;
            else if (met.Count < 4) met.Add($"{kind} {(thing as Vessel)?.Name ?? thing?.GetType().Name}");
        }
        _game.Arms.Dealt += Dealt;
        foreach (int x in _drive.Wait(1)) yield return x;
        _game.Controls.Primary = true;
        _game.Controls.PrimaryPressed = true;
        foreach (int x in _drive.Wait(3)) yield return x;
        Check(_game.Arms.MountedFired == fired, "with no gun picked, the trigger fires nothing");
        _game.Controls.Primary = false;
        foreach (int x in Press("guns.gatling")) yield return x;
        Check(v.Bar.Picked.Contains("guns.gatling") && _game.Visor.BarSlots(v)[Slot("guns.gatling")]?.On == true, "the gatlings' number picks them");
        var muzzles = v.Bar.Muzzles().ToList();
        Vector3 ahead = v.ToWorld(N.Vector3.Zero) - v.ToWorld(new N.Vector3(0f, 0f, 1f));
        ahead = ahead.Normalized();
        _game.Controls.Primary = true;
        _game.Controls.PrimaryPressed = true;
        yield return 0;
        bool leftMuzzle = false;
        string where = "no shot seen";
        for (int t = 0; t < 4 && !leftMuzzle; t++)
        {
            _game.Controls.Primary = true;
            foreach (ref readonly Shot shot in _game.Arms.Pool.Shots.AsSpan())
            {
                if (!shot.Live || shot.Def.Item != "gatling") continue;
                Vector3 p = shot.Pos.ToGodot(), vel = shot.Vel.ToGodot();
                foreach (Vector3 m in muzzles)
                {
                    Vector3 d = p - m;
                    float along = d.Dot(ahead), off = (d - ahead * along).Length();
                    where = $"{along:F1} m ahead of a muzzle, {off:F2} m off its line, {vel.Length():F0} m/s";
                    if (along >= 0f && along < 25f && off < 0.4f && Math.Abs(vel.Length() - 600f) < 15f && vel.Normalized().Dot(ahead) > 0.99f) { leftMuzzle = true; break; }
                }
                if (leftMuzzle) break;
            }
            yield return 0;
        }
        Check(leftMuzzle, $"the shots leave a gatling's muzzle along its facing at about 600 m/s ({where})");
        for (int t = 0; t < 70; t++)
        {
            _game.Controls.Primary = true;
            yield return 0;
        }
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(20)) yield return x;
        _game.Arms.Dealt -= Dealt;
        int shots = _game.Arms.MountedFired - fired;
        Check(shots >= 8 && v.Storage.Count(Belt) == belts - 2, $"a second's burst: {shots} rounds from the two gatlings, a belt each from core storage ({belts} to {v.Storage.Count(Belt)})");
        Vector3 wallAt = target.ToWorld(new N.Vector3(0f, 3f, 0f));
        Check(onTarget >= shots / 2, $"and they hit the wall {target?.Name} 20 m ahead: {onTarget} of {shots} on its blocks"
            + $" (else {string.Join(", ", met)}; ground hits {_game.Arms.GroundHits - ground}, block hits {_game.Arms.BlockHits - blocks}; muzzle {muzzles[0]}, wall {wallAt}; from the muzzle ahead: {_game.Arms.Probe(muzzles[0], muzzles[0] + ahead * 40f)})");

        // ---- Arranging, saving and loading ----------------------------------------------------------------
        foreach (int x in play.OpenMenu(MenuTab.Core)) yield return x;
        foreach (int x in _drive.DragWidget($"core:bar:{Slot("spotlights")}", "core:bar:4")) yield return x;
        foreach (int x in play.CloseMenu()) yield return x;
        string[] moved = v.Bar.Arranged().Select(f => f?.Key).ToArray();
        Check(moved[4] == "spotlights" && moved[0] == null && s.Bar != null && s.Bar[4] == "spotlights",
            $"dragged on the Core tab, the spotlights move to slot 5 and the arrangement is kept with the structure: {Show(moved)}");
        v.SwitchBlocks(t => t.IsTurret, false);
        v.SetSystem(StructureBar.Fabricators, false);
        string text = Slice.Construction.BlueprintLibrary.Text(s, "Bar Trip");
        Structure back = Blueprint.Load(text, s.Catalogue, out List<string> errors);
        Check(errors.Count == 0 && back != null && back.Bar.SequenceEqual(s.Bar) && back.SystemsOff.SetEquals(s.SystemsOff) && Blueprint.Save(back) == Blueprint.Save(Slice.Construction.BlueprintLibrary.Design(s, "Bar Trip")),
            $"the arrangement and the switches survive a blueprint's save and load ({string.Join("; ", errors)}; off: {string.Join(" ", back?.SystemsOff ?? new SortedSet<string>())})");
        if (back != null)
        {
            Vessel copy = Vessel.Spawn(_game.Zone, back, (site + new Vector3(40f, 3f, 0f)).ToNumerics(), 0f, landed: true);
            foreach (int x in _drive.Wait(5)) yield return x;
            Check(copy.Bar.Arranged().Select(f => f?.Key).SequenceEqual(moved) && !copy.SystemOn(StructureBar.Fabricators) && copy.Structure.Get(Turret).Off, "a structure built from the blueprint shows the same bar and switches");
        }

        // ---- Standing up ---------------------------------------------------------------------------------
        foreach (int x in _drive.Tap(Key.F)) yield return x;
        foreach (int x in _drive.Wait(5)) yield return x;
        Check(_game.Survivor.Seated == null && _game.Pack.Bar == Slice.Items.BarSource.Personal, "standing up brings back BAG");
    }

    bool MountedNone() => _game.Zone.Data.Mounts.OfItem("turret") == null;

    static string Show(string[] keys) => string.Join(" | ", keys.Select(k => k ?? "-"));
}
