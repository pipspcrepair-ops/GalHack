using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Arms;
using GalHack.Physics.Engine;
using Godot;
using Slice.Arms;
using Slice.Game;
using Slice.Items;
using Wildlife;
using HitKind = GalHack.Arms.HitKind;
using N = System.Numerics;

namespace Slice.Tests;

/// <summary>
/// Proof frames for the weapons, in daylight on a fresh world: every gun held in first person, every
/// gun's inventory icon on one sheet (rendered from its own model), and action shots of the arc, the
/// swarm, the cone, the mine and the charge. The camera is kept at least 1.2 m clear of any surface.
/// Run windowed (not a *Test scene, so the gate doesn't run it); it quits itself:
///   godot --path prototypes/slice res://tests/WeaponShots.tscn -- --out=DIR [--only=held|icons|tech|actions]
/// </summary>
public partial class WeaponShots : Node
{
    const int WatchdogMinutes = 12;
    Game.Game _game;
    Driver _drive;
    IEnumerator<int> _run;
    string _out = "weapon-shots", _only = "all";
    string _capture;
    bool _capturing, _done;
    readonly List<string> _saved = new();

    /// <summary>Is this set of frames asked for (--only takes a comma list: held,icons,tech,actions)?</summary>
    bool Wants(string set) => _only == "all" || Array.IndexOf(_only.Split(','), set) >= 0;

    ArmsHost Arms => _game.Arms;
    Vector3 Feet => _game.Survivor.WorldFeet;
    Vector3 Eye => _game.Survivor.WorldEye();

    public override void _Ready()
    {
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            int eq = arg.IndexOf('=');
            if (eq < 3 || !arg.StartsWith("--")) continue;
            string v = arg[(eq + 1)..];
            switch (arg[2..eq])
            {
                case "out": _out = v; break;
                case "only": _only = v; break;
            }
        }
        Directory.CreateDirectory(_out);
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the shots took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_capturing) return;
        if (_capture != null) { Capture(_capture); _capture = null; return; }
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            GD.PrintErr($"weapon shots failed: {e}");
            Wildlife.Shutdown.Quit(this, 1);
            return;
        }
        if (!more && !_done)
        {
            _done = true;
            GD.Print($"saved {_saved.Count} frames to {_out}");
            Wildlife.Shutdown.Quit(this, 0);
        }
    }

    async void Capture(string name)
    {
        _capturing = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        string path = Path.Combine(_out, name + ".png");
        Error err = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print(err == Error.Ok ? $"saved {path}" : $"could not save {path}: {err}");
        if (err == Error.Ok) _saved.Add(path);
        _capturing = false;
    }

    /// <summary>Asks for a frame to be saved; the next ticks wait for it. A frozen shot holds the shots' looks still while it is taken (a blink of lightning lasts a few frames).</summary>
    IEnumerable<int> Shot(string name, bool freeze = false)
    {
        if (freeze) Arms.Fx.SetProcess(false);
        _capture = name;
        yield return 0;
        while (_capture != null || _capturing) yield return 0;
        Arms.Fx.SetProcess(true);
    }

    /// <summary>Is the eye at least 1.2 m from every surface around it?</summary>
    bool Clear()
    {
        N.Vector3 e = Eye.ToNumerics();
        for (int i = 0; i < 26; i++)
        {
            float a = i * 2.39996f, y = 1f - 2f * (i + 0.5f) / 26f, r = MathF.Sqrt(1f - y * y);
            var d = new N.Vector3(MathF.Cos(a) * r, y, MathF.Sin(a) * r);
            if (Arms.Sweep(e, e + d * 1.2f, null, 0, 0f, out ShotHit h) && h.Kind is HitKind.Ground or HitKind.Block) return false;
        }
        return true;
    }

    Species _kind;

    Creature Animal(Vector3 at)
    {
        var wild = _game.Zone.Wildlife;
        if (_kind == null)
        {
            Species s = wild.Fauna.Species.FirstOrDefault(x => x.Rig == RigKind.Legged && x.Domestic == DomesticKind.None && !x.Flies && x.Body.Size is > 0.9f and < 2.4f)
                        ?? wild.Fauna.Species.First(x => !x.Flies);
            _kind = s.Copy();
            _kind.Stats.MaxHealth = 5000f;
            _kind.Stats.Damage = 0f;
            _kind.Stats.Hunts = false;
        }
        Creature c = wild.Sim.Spawn(_kind, wild.PatchLocal(_game.Zone.GroundAt(at.X, at.Z)));
        c.Health = 5000f;
        return c;
    }

    Vector3 Centre(Creature c)
    {
        _game.Zone.Wildlife.SimOf(c, out Vector3 o, out _);
        return c.Rig.BoundsCenter + o;
    }

    WeaponDef Arm(string id)
    {
        WeaponDef d = Arms.Table.Get(id);
        PlayerInventory pack = _game.Pack;
        int want = d.Ammo == d.Item ? 6 : 1;
        if (pack[d.Item] < want) pack.Storage.Add(d.Item, want - pack[d.Item]);
        for (int i = 1; i < pack.Grid.Count; i++)
            if (pack.Grid[i].Item == d.Item) { pack.Grid[i].Item = null; pack.Grid[i].Count = 0; }
        pack.Grid[0].Item = d.Item;
        pack.Grid[0].Count = Math.Min(pack[d.Item], Math.Max(1, _game.Zone.Data.Items.Item(d.Item).StackSize));
        foreach (WeaponDef m in d.Modes)
            if (m.Ammo != d.Item && pack[m.Ammo] < 20) pack.Storage.Add(m.Ammo, 20);
        pack.Grid.Reconcile();
        pack.Selected = 0;
        Gun gun = Arms.GunFor(d);
        if (gun.Loaded == 0) gun.LoadNow();
        return d;
    }

    IEnumerable<int> Pull(Vector3 at, int ticks)
    {
        foreach (int x in _drive.LookAt(at, 6)) yield return x;
        _game.Controls.PrimaryPressed = true;
        for (int i = 0; i < ticks; i++)
        {
            _drive.Face(at);
            _game.Controls.Primary = true;
            yield return 0;
        }
        _game.Controls.Primary = false;
    }

    IEnumerable<int> Scenario()
    {
        _game = new Game.Game { Interactive = false, SkipIntro = true, World = WorldChoice.FromArgs(OS.GetCmdlineUserArgs(), WorldChoice.Tested) };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Weather.Sim.Snap(Weather.Model.WeatherKind.Clear);
        _game.Zone.Sky.TimeOfDay = 0.42;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        // Out into the open, away from the bunker, where the eye is well clear of everything.
        Vector3 away = Feet - _game.Bunker.At(Vector3.Zero);
        away.Y = 0f;
        away = away.LengthSquared() < 1e-4f ? Vector3.Forward : away.Normalized();
        for (int tries = 0; tries < 24; tries++)
        {
            float turn = tries * 0.55f, dist = 14f + tries * 2f;
            Vector3 dir = away.Rotated(Vector3.Up, turn);
            Vector3 g = _game.Zone.GroundAt(Feet.X + dir.X * dist, Feet.Z + dir.Z * dist);
            _game.Survivor.PlaceOutside(new N.Vector3(g.X, g.Y + 0.2f, g.Z));
            foreach (int x in _drive.Wait(30)) yield return x;
            if (Clear() && !_game.Zone.Water.LiquidAt(g.X, g.Z, out _)) break;
        }
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (Creature c in _game.Zone.Wildlife.Sim.Creatures.ToList()) if (c.Position.DistanceTo(_game.Zone.Wildlife.PatchLocal(Feet)) < 60f) _game.Zone.Wildlife.Sim.Remove(c);
        // Plants close by would stand in front of every shot: they are cut, as the tool cuts them.
        int cut = 0;
        foreach (var plant in _game.Zone.Streamer.FloraNear(Feet, 12f).ToList())
        {
            if (_game.Zone.Planet.Felled.Contains(plant.Id)) continue;
            _game.Zone.Planet.Felled.Add(plant.Id);
            _game.Zone.Streamer.Fell(plant.Id);
            cut++;
        }
        foreach (int x in _drive.Wait(10)) yield return x;
        GD.Print($"camera clear of surfaces by 1.2 m: {Clear()}; {cut} plants cut for a clear view");
        // A heading over open ground with the sun not in the eyes.
        Vector3 view = BestView();

        if (Wants("held"))
            foreach (WeaponDef d in Arms.Table.All.Where(d => _game.Zone.Data.Items.Has(d.Item))) // (bare hands are no item)
            {
                Arm(d.Id);
                Vector3 look = _game.Zone.GroundAt(Feet.X + view.X * 14f, Feet.Z + view.Z * 14f) + Vector3.Up * 1.2f;
                foreach (int x in _drive.LookAt(look, 40)) yield return x;
                foreach (int x in Shot($"held-{d.Id}")) yield return x;
            }

        if (Wants("icons")) foreach (int x in Icons()) yield return x;
        if (Wants("tech")) foreach (int x in TechTab()) yield return x;
        if (Wants("actions")) foreach (int x in Actions(view)) yield return x;
    }

    Vector3 BestView()
    {
        Vector3 best = Vector3.Forward;
        float score = float.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            var d = new Vector3(MathF.Sin(i * MathF.Tau / 16f), 0f, MathF.Cos(i * MathF.Tau / 16f));
            float worst = 0f;
            for (float r = 2f; r < 20f; r += 2f) worst = MathF.Max(worst, MathF.Abs(_game.Zone.GroundAt(Feet.X + d.X * r, Feet.Z + d.Z * r).Y - Feet.Y));
            Vector3 end = Eye + d * 18f;
            if (Arms.Sweep(Eye.ToNumerics(), end.ToNumerics(), null, 0, 0f, out _)) worst += 50f;
            if (worst < score) { score = worst; best = d; }
        }
        return best;
    }

    // ---- The icon sheet ----------------------------------------------------------------------------

    IEnumerable<int> Icons()
    {
        var icons = _game.Icons;
        var items = _game.Zone.Data.Items;
        List<WeaponDef> all = Arms.Table.All.Where(d => items.Has(d.Item)).ToList();
        foreach (WeaponDef d in all) icons.Get(items.Item(d.Item));
        for (int i = 0; i < 600 && all.Any(d => icons.Rendered(d.Item) == null); i++)
        {
            foreach (WeaponDef d in all) icons.Get(items.Item(d.Item));
            yield return 0;
        }
        var layer = new CanvasLayer { Layer = 100 };
        var back = new ColorRect { Color = new Color(0.09f, 0.1f, 0.12f), Position = Vector2.Zero, Size = new Vector2(1600, 900) };
        layer.AddChild(back);
        const int cell = 360, h = 280;
        for (int i = 0; i < all.Count; i++)
        {
            WeaponDef d = all[i];
            int col = i % 4, row = i / 4;
            var at = new Vector2(80 + col * cell, 40 + row * h);
            layer.AddChild(new ColorRect { Color = new Color(0.16f, 0.17f, 0.2f), Position = at, Size = new Vector2(cell - 30, h - 30) });
            Texture2D t = icons.Rendered(d.Item);
            if (t != null) layer.AddChild(new TextureRect { Texture = t, Position = at + new Vector2((cell - 30 - 192) / 2f, 8), Size = new Vector2(192, 192), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale });
            var label = new Label { Text = $"{d.Code}  {d.Name}", Position = at + new Vector2(12, 208), Size = new Vector2(cell - 54, 30), HorizontalAlignment = HorizontalAlignment.Center };
            label.AddThemeFontSizeOverride("font_size", 20);
            layer.AddChild(label);
        }
        AddChild(layer);
        foreach (int x in _drive.Wait(4)) yield return x;
        GD.Print($"icons rendered: {all.Count(d => icons.Rendered(d.Item) != null)} of {all.Count}");
        foreach (int x in Shot("icons")) yield return x;
        layer.QueueFree();
        yield return 0;
    }

    // ---- The Weapons tab of the tech tree --------------------------------------------------------

    IEnumerable<int> TechTab()
    {
        _game.Menu.Open(Slice.Ui.MenuTab.Tech);
        _game.Menu.Root.Tech.Tab = "weapons";
        _game.Menu.Root.Tech.Group = null;
        // The tiles' icons render a few at a time: give the whole tab time to fill.
        foreach (int x in _drive.Wait(240)) yield return x;
        foreach (int x in Shot("tech-weapons")) yield return x;
        _game.Menu.Close();
        foreach (int x in _drive.Wait(5)) yield return x;
    }

    // ---- Action shots ------------------------------------------------------------------------------

    IEnumerable<int> Actions(Vector3 view)
    {
        Vector3 side = view.Cross(Vector3.Up).Normalized();

        // The arc: a fork of lightning through three animals.
        WeaponDef fork = Arm("fork-gun");
        Creature a = Animal(Feet + view * 9f), b = Animal(Feet + view * 11f + side * 2.5f), c = Animal(Feet + view * 12f - side * 2.5f);
        foreach (int x in _drive.Wait(10)) yield return x;
        int jumps = Arms.ArcJumps;
        foreach (int x in Pull(Centre(a), 1)) yield return x;
        for (int i = 0; i < 30 && Arms.ArcJumps == jumps; i++) yield return 0;
        foreach (int x in Shot("action-arc", true)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;

        // The swarm: hornets out of the hive, off toward the animal.
        WeaponDef hive = Arm("hornet-hive");
        foreach (int x in Pull(Centre(a) + side * 1.5f, 36)) yield return x;
        foreach (int x in Shot("action-swarm", true)) yield return x;
        foreach (int x in _drive.Wait(240)) yield return x;
        foreach (Creature k in new[] { a, b, c }) _game.Zone.Wildlife.Sim.Remove(k);

        // The cone: the thrower held on an animal close by.
        WeaponDef thrower = Arm("pressure-thrower");
        Creature burnt = Animal(Feet + view * 4.5f);
        foreach (int x in _drive.Wait(10)) yield return x;
        foreach (int x in Pull(Centre(burnt), 30)) yield return x;
        _game.Controls.Primary = true;
        foreach (int x in Shot("action-cone", true)) yield return x;
        _game.Controls.Primary = false;
        foreach (int x in _drive.Wait(90)) yield return x;
        _game.Zone.Wildlife.Sim.Remove(burnt);

        // The mine: thrown, holding to the ground, its eye blinking; then something comes too close.
        WeaponDef mine = Arm("prox-mine");
        Vector3 spot = _game.Zone.GroundAt(Feet.X + view.X * 2.8f, Feet.Z + view.Z * 2.8f);
        int placed = Arms.Placements;
        foreach (int x in Pull(spot + Vector3.Up * 0.8f, 1)) yield return x;
        for (int i = 0; i < 180 && Arms.Placements == placed; i++) yield return 0;
        int slot = Array.FindIndex(Arms.Pool.Shots, s => s.Live && s.Def.Base.Id == mine.Id && s.State == ShotState.Armed);
        Vector3 at = slot >= 0 ? Arms.Pool.Shots[slot].Pos.ToGodot() : spot;
        // Put it away (an empty slot): the shot is of the mine on the ground.
        for (int i = PlayerInventory.ToolbarSlots - 1; i > 0; i--)
            if (_game.Pack.Grid[i].Item == null && _game.Pack.Grid[i].Block == null) { _game.Pack.Selected = i; break; }
        foreach (int x in _drive.LookAt(at, 200)) yield return x;
        foreach (int x in Shot("action-mine")) yield return x;
        int trips = Arms.Trips;
        Creature walker = Animal(at + view * 1.1f);
        for (int i = 0; i < 90 && Arms.Trips == trips; i++) { _drive.Face(at); yield return 0; }
        foreach (int x in _drive.Wait(2)) yield return x;
        foreach (int x in Shot("action-mine-blast", true)) yield return x;
        _game.Zone.Wildlife.Sim.Remove(walker);
        foreach (int x in _drive.Wait(90)) yield return x;

        // The charge: the stage rifle held to its fourth stage, rings lit; then the slug away.
        WeaponDef rail = Arm("stage-rifle");
        Vector3 far = _game.Zone.GroundAt(Feet.X + view.X * 30f, Feet.Z + view.Z * 30f) + Vector3.Up * 1f;
        foreach (int x in _drive.LookAt(far, 10)) yield return x;
        _game.Controls.PrimaryPressed = true;
        for (int i = 0; i < 95; i++) { _drive.Face(far); _game.Controls.Primary = true; yield return 0; }
        GD.Print($"charge stage {Arms.InHand?.Stage}");
        foreach (int x in Shot("action-charge")) yield return x;
        _game.Controls.Primary = false;
        yield return 0;
        yield return 0;
        foreach (int x in Shot("action-charge-release")) yield return x;
    }
}
