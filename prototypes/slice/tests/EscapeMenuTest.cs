using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using DiegeticUi.Theme;
using Godot;
using Slice.Game;
using Slice.Ui;

namespace Slice.Tests;

/// <summary>
/// The escape menu, on a fresh random world, through the game's own keys and mouse
/// (<see cref="Controls"/>): Esc opens it and the world runs on behind it (the game never pauses:
/// the clock goes on, and a key pressed for the game, Tab, is the menu's while it is open); a second
/// Esc or RESUME closes it; with the Tab menu open,
/// Esc closes that instead. SAVE and LOAD do nothing yet; QUIT asks first and STAY (or Esc) keeps
/// you. A volume slider reaches its bus. A key rebound at its prompt drives its action from the new
/// key and not the old, a clash is found, Esc cancels a prompt, and the binding comes back from the
/// settings file. Sensitivity scales mouse look and invert flips it. Graphics changes reach the
/// camera, the viewport, the frame cap, the sun's shadows, the ground's streaming and the gamma,
/// are read back from the file, and RESET puts them all back. NEW GAME asks first, then tears the
/// world down for the start screen as at launch, twice, with nothing left running or held between.
///
/// Run: .\tools\test.ps1 -Project prototypes/slice -Scene res://tests/EscapeMenuTest.tscn
/// </summary>
public partial class EscapeMenuTest : Node
{
    const int WatchdogMinutes = 16;
    const string SettingsFile = "user://escape-menu-test.cfg";
    IEnumerator<int> _run;
    int _failures, _passes;
    readonly Stopwatch _clock = Stopwatch.StartNew();
    Game.Game _game;
    Driver _drive;
    Controls C => _game.Controls;
    EscapeMenu Esc => _game.Escape;

    public override void _Ready()
    {
        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.Thread.Sleep(TimeSpan.FromMinutes(WatchdogMinutes));
            Console.Error.WriteLine($"FAIL  the run took over {WatchdogMinutes} minutes: watchdog");
            System.Environment.Exit(1);
        }) { IsBackground = true };
        thread.Start();
        ProcessMode = ProcessModeEnum.Always;
        ProcessPhysicsPriority = 100;
        _run = Scenario().GetEnumerator();
    }

    public override void _PhysicsProcess(double delta)
    {
        bool more;
        try { more = _run.MoveNext(); }
        catch (Exception e)
        {
            Fail($"scenario threw: {e}");
            more = false;
        }
        if (more) return;
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SettingsFile));
        GetTree().Paused = false;
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

    void Fail(string what) => Check(false, what);

    IEnumerable<int> Wait(int ticks)
    {
        for (int i = 0; i < ticks; i++) yield return 0;
    }

    IEnumerable<int> Tap(Key key)
    {
        C.Tap(key);
        yield return 0;
        yield return 0;
    }

    /// <summary>Clicks an escape-menu widget with the game's mouse, at a share of its width (a slider's value).</summary>
    IEnumerable<int> Click(string id, float along = 0.5f)
    {
        for (int i = 0; i < 4 && Esc.RectOf(id) == null; i++) yield return 0;
        if (Esc.RectOf(id) is not Rect2 r)
            throw new InvalidOperationException($"no widget '{id}' on the escape menu ({string.Join(", ", Esc.Root.WidgetIds.Take(60))})");
        C.Mouse = r.Position + new Vector2(r.Size.X * along, r.Size.Y * 0.5f);
        yield return 0;
        C.PrimaryPressed = true;
        C.Primary = true;
        yield return 0;
        C.Primary = false;
        C.PrimaryReleased = true;
        yield return 0;
        yield return 0;
    }

    /// <summary>Where on a slider's track a value sits.</summary>
    static float Along(float value, float min, float max) => (value - min) / (max - min);

    IEnumerable<int> Scenario()
    {
        DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SettingsFile));
        _game = new Game.Game { Interactive = false, SkipIntro = true, SettingsPath = SettingsFile };
        AddChild(_game);
        _drive = new Driver(_game);
        foreach (int x in _drive.UntilPlaying()) yield return x;
        GD.Print($"-- {_game.World} (a fresh game: rolled its own) --");
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = true;
        foreach (int x in Wait(30)) yield return x;

        // The suit terminal's dot and degree sign, now correctly encoded, are in its fonts.
        Check(UiTheme.Label.HasChar('·') && UiTheme.Label.HasChar('°') && UiTheme.Display.HasChar('·'),
            "the menus' fonts have the middle dot and the degree sign (the suit terminal shows them, not \"A-hat\" pairs)");

        foreach (int x in OpenRunning()) yield return x;
        foreach (int x in Buttons()) yield return x;
        foreach (int x in Volume()) yield return x;
        foreach (int x in Rebind()) yield return x;
        foreach (int x in Look()) yield return x;
        foreach (int x in GraphicsApply()) yield return x;

        // With the Tab menu open, Esc is the Tab menu's: it closes, and the escape menu stays shut.
        foreach (int x in _drive.Tap(Key.Tab)) yield return x;
        foreach (int x in Wait(2)) yield return x;
        bool tabOpen = _game.Menu.IsOpen;
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(tabOpen && !_game.Menu.IsOpen && !Esc.IsOpen && !GetTree().Paused, "with the Tab menu open, Esc closes it and nothing else");
        foreach (int x in NewGames()) yield return x;
        _game.QueueFree();
        foreach (int x in Wait(10)) yield return x;
    }

    /// <summary>
    /// What is left running, counted on the start screen: nodes (and of them orphans, and those beside the test under the
    /// root: the shared deco animator, made once the first deco piece shows), sound players, the music's and thrusters'
    /// workers, threads, memory.
    /// </summary>
    readonly record struct Left(long Nodes, long Orphans, int Beside, string BesideNames, int Players, int Workers, int Threads, int PoolThreads, long ManagedMb, long StaticMb)
    {
        public long Own => Nodes - Beside;
        public override string ToString() => $"{Nodes} nodes ({Orphans} orphans, {Beside} beside the test{(Beside > 0 ? $": {BesideNames}" : "")}), {Players} sound players, {Workers} sound workers, {Threads} threads ({PoolThreads} pooled), {ManagedMb} MB managed, {StaticMb} MB engine";
    }

    /// <summary>The nodes that may stand beside the test under the root: made once, shared by every game.</summary>
    static readonly string[] SharedNodes = { "DecoAnimator" };

    Left Count()
    {
        System.Runtime.GCSettings.LargeObjectHeapCompactionMode = System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect();
        int players = GetTree().Root.FindChildren("*", "", true, false).Count(n => n is AudioStreamPlayer or AudioStreamPlayer2D or AudioStreamPlayer3D);
        var beside = GetTree().Root.GetChildren().Where(n => n != this).ToList();
        using var process = Process.GetCurrentProcess();
        return new Left((long)Performance.GetMonitor(Performance.Monitor.ObjectNodeCount), (long)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            beside.Sum(n => 1 + n.FindChildren("*", "", true, false).Count), string.Join(", ", beside.Select(n => n.Name.ToString())), players,
            SliceMusic.Workers + ThrusterPlayer.Workers, process.Threads.Count, System.Threading.ThreadPool.ThreadCount, GC.GetTotalMemory(false) >> 20, (long)(OS.GetStaticMemoryUsage() >> 20));
    }

    /// <summary>Weak handles on a game, its zone and its planet, so the test itself holds none of them.</summary>
    static (WeakReference Game, WeakReference Zone, WeakReference Planet) Weakly(Game.Game g) => (new WeakReference(g), new WeakReference(g.Zone), new WeakReference(g.Zone.Planet));

    static bool Gone(WeakReference w) => w.Target is not GodotObject o || !GodotObject.IsInstanceValid(o);

    /// <summary>
    /// NEW GAME, three times in a row, through the menu's own mouse: it asks first (NO and Esc keep you); YES writes
    /// the settings, tears this world down whole and puts up the start screen as at launch; a preset and WAKE UP land
    /// in a fresh world that plays. Each time the start screen is reached the same nodes are left, no sound and no
    /// workers run, and nothing holds the last world; from the second on (the engine's own caches warm in the first
    /// two worlds) no memory builds up.
    /// </summary>
    IEnumerable<int> NewGames()
    {
        GD.Print("-- NEW GAME, three times --");
        var seeds = new List<ulong> { _game.World.Seed };
        var screens = new List<Left>();
        (string Id, float Depth)[] presets = { ("mid", 0.5f), ("core", 0.9f) };
        // -- --newgames=N: that many in a row (a soak for what builds up); three in a plain run.
        int cycles = 3;
        foreach (string a in OS.GetCmdlineUserArgs())
            if (a.StartsWith("--newgames=")) int.TryParse(a["--newgames=".Length..], out cycles);
        for (int k = 0; k < cycles; k++)
        {
            var preset = presets[k % presets.Length];
            foreach (int x in Tap(Key.Escape)) yield return x;
            if (k == 0)
            {
                foreach (int x in Click("esc:new")) yield return x;
                bool asks = Esc.Root.Asking == EscapeQuestion.NewGame && Esc.RectOf("esc:new-yes") != null && Esc.RectOf("esc:new-no") != null;
                foreach (int x in Click("esc:options")) yield return x;
                bool blocked = Esc.Root.Page == EscapePage.World;
                foreach (int x in Click("esc:new-no")) yield return x;
                bool no = Esc.IsOpen && Esc.Root.Asking == EscapeQuestion.None && _game.Next == null;
                foreach (int x in Click("esc:new")) yield return x;
                foreach (int x in Tap(Key.Escape)) yield return x;
                Check(asks && blocked && no && Esc.IsOpen && Esc.Root.Asking == EscapeQuestion.None && _game.Next == null,
                    "NEW GAME asks first: YES or NO, nothing under it clickable; NO and Esc keep this world, the menu still open");
                // A setting changed a moment before (not yet written): NEW GAME writes it before the world goes.
                foreach (int x in Click("esc:options")) yield return x;
                foreach (int x in Click("opt:graphics")) yield return x;
                foreach (int x in Click("opt:fov", Along(90f, GameSettings.MinFov, GameSettings.MaxFov))) yield return x;
                foreach (int x in Click("esc:options")) yield return x;
            }
            var held = Weakly(_game);
            string name = _game.Name;
            foreach (int x in Click("esc:new")) yield return x;
            foreach (int x in Click("esc:new-yes")) yield return x;
            Game.Game next = _game.Next;
            if (next == null)
            {
                Fail("YES started no new game");
                yield break;
            }
            _game = next;
            _drive = new Driver(next);

            // The start screen, and the last world let go of: freed, and (after a collection) held by nothing.
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalSeconds < 30 && (next.StartScreen?.Root == null || !next.Settled || held.Game.IsAlive || held.Zone.IsAlive || held.Planet.IsAlive))
            {
                if (next.Settled && Engine.GetPhysicsFrames() % 30 == 0) GC.Collect();
                yield return 0;
            }
            StartRoot start = next.StartScreen?.Root;
            Check(start != null && next.Zone == null && next.IsInsideTree() && (name.Contains('@') || next.Name == name) && Gone(held.Game) && Gone(held.Zone)
                  && start.Pickers.Count == 11 && start.CurrentPreset == "default" && start.Seed == "" && GetChildren().OfType<Game.Game>().Count() == 1,
                $"NEW GAME {k + 1}, YES: the world torn down and freed, and in its place the start screen as at launch (preset {start?.CurrentPreset}, {start?.Pickers.Count} options and the seed)");
            Check(!held.Game.IsAlive && !held.Zone.IsAlive && !held.Planet.IsAlive,
                $"NEW GAME {k + 1}: nothing holds the last game, its zone or its planet after a collection (game {held.Game.IsAlive}, zone {held.Zone.IsAlive}, planet {held.Planet.IsAlive}; {watch.Elapsed.TotalSeconds:F1} s)");
            Check(next.Settings.Fov == 90f, $"NEW GAME {k + 1}: the settings were written first and read again (field of view {next.Settings.Fov:F0})");
            // A few seconds on the start screen, as a player takes to choose: the last world's work still on worker threads ends.
            while (watch.Elapsed.TotalSeconds < 4) yield return 0;
            GC.Collect(); // what was dropped is finalized on its own thread, then collected again as it is counted
            foreach (int x in Wait(20)) yield return x;
            Left left = Count();
            screens.Add(left);
            GD.Print($"      (start screen {k + 1}: {left})");
            Check(left.Players == 0 && left.Workers == 0, $"NEW GAME {k + 1}: no sound left playing and no sound worker running ({left.Players} players, {left.Workers} workers)");
            if (start == null) yield break;

            // A preset and WAKE UP: a fresh world, as from launch.
            start.Click($"start:preset-{preset.Id}");
            bool picked = start.CurrentPreset == preset.Id;
            start.Click("start:wake");
            foreach (int x in _drive.UntilPlaying()) yield return x;
            float depth = _game.Zone.Planet?.Depth ?? -1f;
            Check(picked && _game.StartScreen == null && !seeds.Contains(_game.World.Seed) && MathF.Abs(depth - preset.Depth) < 0.051f,
                $"NEW GAME {k + 1}: {preset.Id.ToUpperInvariant()} and WAKE UP land in a fresh world: {_game.World}, depth {depth:0.00}");
            seeds.Add(_game.World.Seed);
            _game.Zone.Weather.Bridge.Calm = true;
            _game.Zone.Wildlife.Frozen = true;

            // It plays: the clock runs, the keys reach it, its menus open, its sound runs.
            double clock = _game.Stats.Seconds;
            bool lit = _game.Torch.Visible;
            _game.Press(Key.L);
            foreach (int x in Wait(30)) yield return x;
            bool torch = _game.Torch.Visible != lit;
            _game.Press(Key.L);
            foreach (int x in Tap(Key.Escape)) yield return x;
            bool opens = Esc.IsOpen;
            foreach (int x in Tap(Key.Escape)) yield return x;
            Check(_game.Stats.Seconds > clock + 0.4 && torch && opens && !Esc.IsOpen && _game.Camera.Fov == 90f && SliceMusic.Workers + ThrusterPlayer.Workers > 0,
                $"NEW GAME {k + 1}: it plays: the clock runs, the torch key works, Esc opens and closes the menu, the view keeps the setting ({_game.Camera.Fov:F0}), {SliceMusic.Workers + ThrusterPlayer.Workers} sound workers running");
        }
        if (screens.Count >= 2)
        {
            Left a = screens[0], b = screens[^1];
            bool sharedOnly = screens.All(s => s.Beside == 0 || s.BesideNames.Split(", ").All(SharedNodes.Contains));
            Check(b.Own == a.Own && sharedOnly && b.Orphans == a.Orphans && b.Threads - b.PoolThreads <= a.Threads - a.PoolThreads + 2,
                $"{screens.Count} NEW GAMEs in a row leave the same: {a.Own} then {b.Own} nodes of the game and the test (beside them only {string.Join(", ", SharedNodes)}), {a.Orphans} then {b.Orphans} orphans, {a.Threads - a.PoolThreads} then {b.Threads - b.PoolThreads} threads outside the pool");
        }
        if (screens.Count >= 3)
        {
            // A world kept in the shared caches was 45 MB or more of managed memory a game: none builds up once warm.
            Left a = screens[1], b = screens[^1];
            Check(b.ManagedMb <= a.ManagedMb + 16 && b.StaticMb <= a.StaticMb + 24,
                $"and from the second on no memory builds up: managed {string.Join(", ", screens.Select(s => s.ManagedMb))} MB, engine {string.Join(", ", screens.Select(s => s.StaticMb))} MB");
        }
    }

    /// <summary>Esc: open, the world running on behind it; Esc again: closed.</summary>
    IEnumerable<int> OpenRunning()
    {
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(Esc.IsOpen && !GetTree().Paused && Esc.Root.Page == EscapePage.World, "Esc opens the escape menu, on this world's card, and nothing pauses");
        double clock = _game.Stats.Seconds;
        foreach (int x in Wait(30)) yield return x;
        Check(_game.Stats.Seconds > clock + 0.4, $"the world runs on while it is open ({_game.Stats.Seconds - clock:F2} s in 30 ticks)");
        foreach (int x in Tap(Key.Tab)) yield return x;
        Check(Esc.IsOpen && !_game.Menu.IsOpen, "a key pressed while it is open is the menu's: Tab opens nothing under it");
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(!Esc.IsOpen && !GetTree().Paused, "a second Esc closes it");
    }

    /// <summary>SAVE and LOAD are there and do nothing; QUIT asks, STAY and Esc keep you; RESUME closes.</summary>
    IEnumerable<int> Buttons()
    {
        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:save")) yield return x;
        foreach (int x in Click("esc:load")) yield return x;
        Check(Esc.IsOpen && Esc.Root.Page == EscapePage.World && !Esc.Root.Confirming, "SAVE and LOAD are on the menu, and greyed: a click does nothing yet");
        foreach (int x in Click("esc:quit")) yield return x;
        Check(Esc.Root.Confirming && Esc.RectOf("esc:quit-yes") != null && Esc.RectOf("esc:quit-no") != null, "QUIT TO DESKTOP asks first: QUIT or STAY");
        foreach (int x in Click("esc:options")) yield return x;
        Check(Esc.Root.Page == EscapePage.World, "under the question nothing else can be clicked");
        foreach (int x in Click("esc:quit-no")) yield return x;
        Check(!Esc.Root.Confirming && Esc.IsOpen, "STAY takes the question away, the menu still open");
        foreach (int x in Click("esc:quit")) yield return x;
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(!Esc.Root.Confirming && Esc.IsOpen, "Esc backs out of the question first");
        foreach (int x in Click("esc:resume")) yield return x;
        Check(!Esc.IsOpen && !GetTree().Paused, "RESUME closes the menu and the world runs");
    }

    /// <summary>A volume slider: the setting and its bus.</summary>
    IEnumerable<int> Volume()
    {
        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:sound")) yield return x;
        SliceAudio audio = _game.Audio;
        float original = audio.Settings.Get(AudioSettings.Music), master = audio.Settings.Get(AudioSettings.Master);
        foreach (int x in Click("opt:volume-music", 0.25f)) yield return x;
        float music = audio.Settings.Get(AudioSettings.Music);
        float bus = Mathf.DbToLinear(AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Music")));
        Check(MathF.Abs(music - 0.25f) < 0.02f && MathF.Abs(bus - music) < 0.01f, $"the music slider a quarter along: music {music:F2}, its bus {bus:F2} (master {master:F2})");
        audio.SetVolume(AudioSettings.Music, original); // the run leaves the volumes as it found them
        foreach (int x in Tap(Key.Escape)) yield return x;
    }

    /// <summary>The head torch moved from L to K at its prompt: K lights it, L does nothing; a clash shows; Esc cancels a prompt; the file keeps it.</summary>
    IEnumerable<int> Rebind()
    {
        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:controls")) yield return x;
        KeyBindings keys = _game.Settings.Keys;
        foreach (int x in Click("key:torch")) yield return x;
        Check(Esc.Root.Options.Capturing == "torch", "a click on the torch's key asks for a new one");
        foreach (int x in Tap(Key.K)) yield return x;
        Check(keys["torch"] == Key.K && Esc.Root.Options.Capturing == null && Esc.IsOpen, "K pressed at the prompt: the torch is on K, the menu still open");
        foreach (int x in Click("key:view")) yield return x;
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(keys["view"] == Key.V && Esc.Root.Options.Capturing == null && Esc.IsOpen, "Esc at a prompt cancels it: third person stays on V, the menu open");
        foreach (int x in Click("key:map")) yield return x;
        foreach (int x in Tap(Key.K)) yield return x;
        Check(keys.Clashes("torch").Any(a => a.Id == "map") && keys.HasClashes, "the map put on K too is a clash, found on both");
        foreach (int x in Click("key:map")) yield return x;
        foreach (int x in Tap(Key.M)) yield return x;
        Check(!keys.HasClashes, "the map back on M: no clash");
        foreach (int x in Tap(Key.Escape)) yield return x;

        bool lit = _game.Torch.Visible;
        _game.Press(Key.L);
        foreach (int x in Wait(2)) yield return x;
        bool afterL = _game.Torch.Visible;
        _game.Press(Key.K);
        foreach (int x in Wait(2)) yield return x;
        bool afterK = _game.Torch.Visible;
        Check(afterL == lit && afterK != lit, $"in play, L no longer works the torch and K does (lit {lit}, after L {afterL}, after K {afterK})");
        _game.Press(Key.K);
        foreach (int x in Wait(2)) yield return x;
        GameSettings read = GameSettings.Load(SettingsFile);
        Check(read.Keys["torch"] == Key.K && read.Keys["map"] == Key.M && read.Keys["forward"] == Key.W, "read back from the settings file: the torch on K, the rest as they were");
    }

    /// <summary>Sensitivity doubles the turn a mouse movement gives; invert flips looking up and down.</summary>
    IEnumerable<int> Look()
    {
        float Turned(float before) => Mathf.Wrap(before - _game.Survivor.Yaw, -MathF.PI, MathF.PI);
        float yaw = _game.Survivor.Yaw;
        C.Look = new Vector2(40f, 0f);
        foreach (int x in Wait(2)) yield return x;
        float at1 = Turned(yaw);
        float pitch = _game.Survivor.Pitch;
        C.Look = new Vector2(0f, -40f);
        foreach (int x in Wait(2)) yield return x;
        float up1 = _game.Survivor.Pitch - pitch;

        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:mouse")) yield return x;
        foreach (int x in Click("opt:sensitivity", Along(2f, GameSettings.MinSense, GameSettings.MaxSense))) yield return x;
        foreach (int x in Click("opt:invert-on")) yield return x;
        float sense = _game.Settings.Sensitivity;
        foreach (int x in Click("esc:resume")) yield return x;

        yaw = _game.Survivor.Yaw;
        C.Look = new Vector2(40f, 0f);
        foreach (int x in Wait(2)) yield return x;
        float at2 = Turned(yaw);
        pitch = _game.Survivor.Pitch;
        C.Look = new Vector2(0f, -40f);
        foreach (int x in Wait(2)) yield return x;
        float up2 = _game.Survivor.Pitch - pitch;
        Check(MathF.Abs(sense - 2f) < 0.001f && at1 > 0f && MathF.Abs(at2 / at1 - 2f) < 0.02f,
            $"sensitivity {sense:F2}x: 40 pixels of mouse turn the view {at1 * 180 / MathF.PI:F2} degrees at 1x and {at2 * 180 / MathF.PI:F2} at 2x");
        Check(up1 > 0f && up2 < 0f && MathF.Abs(up2 / up1 + 2f) < 0.02f, $"invert look: mouse forward looked up {up1:F4} rad, now down {up2:F4}");

        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:mouse")) yield return x;
        foreach (int x in Click("opt:reset")) yield return x;
        Check(_game.Settings.Sensitivity == 1f && !_game.Settings.InvertY && _game.LookScale == Game.Game.BaseLook, "RESET MOUSE: 1x, not inverted");
        foreach (int x in Tap(Key.Escape)) yield return x;
    }

    /// <summary>Each graphics setting reaches what it sets, is read back from the file, and RESET undoes them all.</summary>
    IEnumerable<int> GraphicsApply()
    {
        Viewport view = GetViewport();
        Brightness gamma = _game.GetNode<Brightness>("Brightness");
        DirectionalLight3D sun = _game.Surface.Sky.Sun;
        var streamer = _game.Surface.Streamer;
        Check(_game.Camera.Fov == 75f && view.Scaling3DScale == 1f && Engine.MaxFps == 0 && sun.DirectionalShadowMaxDistance == 320f && streamer.VoxelRadius == 5 && !gamma.Visible,
            "the defaults are the game as it was: 75 degrees, full scale, no cap, the sun's shadows to 320 m, 80 m of ground, no gamma pass");

        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:graphics")) yield return x;
        foreach (int x in Click("opt:fov", Along(90f, GameSettings.MinFov, GameSettings.MaxFov))) yield return x;
        foreach (int x in Click("opt:render-scale", 0.5f)) yield return x;
        foreach (int x in Click("opt:fps-60")) yield return x;
        foreach (int x in Click("opt:shadows-low")) yield return x;
        foreach (int x in Click("opt:ground-112-m")) yield return x;
        foreach (int x in Click("opt:gamma", Along(1.3f, GameSettings.MinGamma, GameSettings.MaxGamma))) yield return x;
        foreach (int x in Click("opt:vsync-off")) yield return x;
        foreach (int x in Click("opt:window-borderless")) yield return x;
        Vector2I before = _game.Settings.Resolution;
        foreach (int x in Click("opt:res-down")) yield return x;
        bool resolutionLocked = _game.Settings.Resolution == before && _game.Settings.Window == WindowKind.Borderless;
        foreach (int x in Click("opt:window-windowed")) yield return x;
        foreach (int x in Click("opt:res-down")) yield return x;
        GameSettings s = _game.Settings;
        Check(_game.Camera.Fov == 90f, $"FIELD OF VIEW: the camera at {_game.Camera.Fov:F1} degrees, while the menu is still open");
        Check(MathF.Abs(view.Scaling3DScale - 0.75f) < 0.001f, $"RENDER SCALE halfway: the 3D view at {view.Scaling3DScale:F2}");
        Check(Engine.MaxFps == 60, $"FRAME RATE CAP 60: the engine's cap {Engine.MaxFps}");
        Check(sun.DirectionalShadowMaxDistance == 120f && sun.DirectionalShadowMode == DirectionalLight3D.ShadowMode.Parallel2Splits && _game.Surface.Sky.NightLight.DirectionalShadowMaxDistance == 120f,
            $"SHADOWS LOW: the sun's and the night light's shadows to {sun.DirectionalShadowMaxDistance:F0} m in {sun.DirectionalShadowMode}");
        Check(streamer.VoxelRadius == 7, $"GROUND DETAIL 112 M: the streamer's radius {streamer.VoxelRadius} columns");
        Check(gamma.Visible && MathF.Abs(gamma.Gamma - 1.3f) < 0.001f, $"BRIGHTNESS 1.30: the gamma pass on at {gamma.Gamma:F2}");
        Check(!s.Vsync && s.Window == WindowKind.Windowed && resolutionLocked && s.Resolution != before && s.Resolution.X * s.Resolution.Y < before.X * before.Y,
            $"VSYNC off; RESOLUTION locked while BORDERLESS, then WINDOWED {before.X}x{before.Y} down to {s.Resolution.X}x{s.Resolution.Y} (the window itself is the player's, never a test's, and headless has none)");

        foreach (int x in Click("opt:view")) yield return x;
        bool viewing = Esc.Root.Viewing && Esc.RectOf("opt:fov") == null;
        foreach (int x in Tap(Key.Escape)) yield return x;
        Check(viewing && !Esc.Root.Viewing && Esc.IsOpen && Esc.RectOf("opt:fov") != null, "VIEW THE WORLD puts the menu aside, and Esc brings the options back (the menu still open)");
        foreach (int x in Tap(Key.Escape)) yield return x;
        // The streamer lays its ring out again: ground further out than the old radius could reach comes in.
        var watch = Stopwatch.StartNew();
        while (streamer.CompleteRadius < 7 && watch.Elapsed.TotalSeconds < 90) yield return 0;
        Check(streamer.CompleteRadius >= 7, $"the ground streams out to the new radius after resuming ({streamer.CompleteRadius} columns complete, {watch.Elapsed.TotalSeconds:F1} s)");
        foreach (int x in Wait(5)) yield return x;
        Check(_game.Camera.Fov == 90f, $"in play the view keeps the setting ({_game.Camera.Fov:F1} degrees)");

        GameSettings read = GameSettings.Load(SettingsFile);
        Check(read.Fov == 90f && read.RenderScale == 0.75f && read.FpsCap == 60 && read.Shadows == ShadowLevel.Low && read.GroundDetail == 7
              && MathF.Abs(read.Gamma - 1.3f) < 0.001f && !read.Vsync && read.Resolution == s.Resolution,
            "read back from the settings file: every graphics setting as it was left");

        foreach (int x in Tap(Key.Escape)) yield return x;
        foreach (int x in Click("esc:options")) yield return x;
        foreach (int x in Click("opt:graphics")) yield return x;
        foreach (int x in Click("opt:reset")) yield return x;
        Check(_game.Camera.Fov == 75f && view.Scaling3DScale == 1f && Engine.MaxFps == 0 && sun.DirectionalShadowMaxDistance == 320f && streamer.VoxelRadius == 5 && !gamma.Visible && s.Vsync,
            "RESET GRAPHICS: all of it back to the defaults");
        foreach (int x in Click("opt:controls")) yield return x;
        foreach (int x in Click("opt:reset")) yield return x;
        Check(_game.Settings.Keys.IsDefault, "RESET KEYS: every key back on its default");
        foreach (int x in Click("esc:resume")) yield return x;
    }
}
