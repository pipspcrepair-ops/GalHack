using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GalHack.Persistence;
using GalHack.Records;
using Godot;
using Saves.Zones;

namespace Saves;

/// <summary>
/// The playable prototype. Opens the save (or starts a world), loads the zone the player is in,
/// and runs the seams: climbing past the atmosphere's edge preloads orbit in the background and
/// plays a tunnel until it is ready, then the ship crosses, record first; descending does the
/// reverse and comes down where it crossed. F5 saves, F9 reloads, F7 opens the host console with
/// the backups, and F8 kills the game in the middle of a save so the next launch must recover.
///   godot --path prototypes/saves -- [--save-dir=PATH] [--new] [--slow-pc=SECONDS] [--chaos-after=SECONDS]
/// </summary>
public partial class Main : Node
{
    enum Phase { Flying, Tunnel, FadeOut }

    public Saver Saver { get; private set; }
    public ZoneManager Zones { get; private set; }
    public Places Places { get; private set; }
    public Hud Hud { get; private set; }
    public Player Player { get; private set; }
    public Hands Hands { get; private set; }
    public string SaveFolder { get; private set; }

    /// <summary>The ship the player is aboard (or last boarded).</summary>
    public Ship Ship => Player?.Ship;

    // Arguments that must act once per run, not again on every F9 reload.
    static bool _argsUsed;
    static double _chaosAfter = -1;
    static double _slowPc;
    static string _saveDir;

    Phase _phase = Phase.Flying;
    string _transitTo;
    bool _climbing;
    double _transitTime, _preloadStarted = -1;
    string _lastCrossing = "";
    string _message = "";
    double _messageTime;
    bool _airborne;
    long _countsStamp = -1;
    string _countsText = "";
    double _chaosTimer;

    public override void _Ready()
    {
        ReadArguments();
        Hud = new Hud { Name = "Hud" };
        AddChild(Hud);
        Hud.Command += RunCommand;

        Saver = new Saver { Name = "Saver" };
        AddChild(Saver);
        LoadResult load = Saver.Open(SaveFolder);

        Places = new Places { Table = Saver.Table };
        Zones = new ZoneManager
        {
            Name = "Zones",
            Content = Places,
            Table = Saver.Table,
            CommitNow = (why, faults) => Saver.SaveNow(why, faults),
            SimulatedSlowness = _slowPc,
        };
        AddChild(Zones);
        Saver.BeforeSave += () =>
        {
            foreach (Zone zone in Zones.Live) if (zone.IsReady) Places.Capture(zone);
        };

        if (!load.Ok)
        {
            ShowRefusal(load);
            return;
        }

        Player = new Player { Name = "Player", RecordId = StarterWorld.PlayerId };
        Player.Said += Say;
        Player.FrameChanged += OnFrameChanged;
        Places.LocalPlayer = Player;
        Hands = new Hands(() => Saver.Table, () => Zones.Active, Places, Player);
        Hands.Said += Say;
        Player.SeatRefusal = () => Hands.Carrying != null ? "set the crate down before you take the seat" : null;

        Zone zone = Zones.LoadNow(PlaceZoneOfPlayer());
        if (!Player.IsInsideTree())
        {
            ShowRefusal(null, $"the player's record did not come back in {zone.ZoneId}");
            return;
        }
        Activate(zone);
        GetViewport().SizeChanged += () => Activate(Zones.Active);

        Say(load.RecoveredFromInterruptedSave
            ? $"RECOVERED: {load.Summary()}"
            : $"Loaded: {load.Summary()}", load.RecoveredFromInterruptedSave ? Hud.Good : Hud.Ink, 8);
        GD.Print(load.Summary());
    }

    void ReadArguments()
    {
        bool fresh = false;
        if (!_argsUsed)
        {
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--save-dir=")) _saveDir = arg["--save-dir=".Length..];
                else if (arg == "--new") fresh = true;
                else if (arg.StartsWith("--slow-pc=")) _slowPc = double.Parse(arg["--slow-pc=".Length..], System.Globalization.CultureInfo.InvariantCulture);
                else if (arg.StartsWith("--chaos-after=")) _chaosAfter = double.Parse(arg["--chaos-after=".Length..], System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        SaveFolder = Path.GetFullPath(_saveDir ?? DataPaths.World);
        if (fresh && Directory.Exists(SaveFolder))
        {
            // A new world never deletes the old one: it is moved aside, whole.
            string aside = $"{SaveFolder}-old-{DateTime.Now:yyyyMMdd-HHmmss}";
            Directory.Move(SaveFolder, aside);
            GD.Print($"--new: moved the old save to {aside}");
        }
        _argsUsed = true;
        _chaosTimer = _chaosAfter;
        _chaosAfter = -1;
    }

    /// <summary>The place zone the player's record is in: a cabin is wherever its ship is.</summary>
    string PlaceZoneOfPlayer()
    {
        SaveRecord me = Saver.Table.Get(StarterWorld.PlayerId);
        return me.ShipId != null ? Saver.Table.Get(me.ShipId).ZoneId : me.ZoneId;
    }

    void Activate(Zone zone)
    {
        if (zone == null) return;
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Zones.SetActive(zone, new Vector2I((int)size.X, (int)size.Y));
        Hud.Screen.Texture = zone.GetTexture();
        if (Player != null)
        {
            Player.MainWorld = zone.Root;
            Player.Ships = zone.Spawned.Values.OfType<Ship>().ToList();
        }
    }

    void ShowRefusal(LoadResult load, string reason = null)
    {
        string why = reason ?? string.Join("\n", load.Problems.Take(6));
        Hud.Say("LOAD REFUSED: the save is damaged, and nothing was changed.\n" + why +
                "\n\nPress F7 and type  restore 1  to restore the newest backup.", Hud.Bad);
        GD.PrintErr(load?.Summary() ?? reason);
        Hud.OpenConsole();
        ListBackups();
    }

    // ---- Every frame ----------------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_chaosTimer > 0 && (_chaosTimer -= delta) <= 0) Say(Saver.ChaosKill(System.Environment.TickCount), Hud.Bad, 30);
        if (Player == null || Zones.Active == null)
        {
            UpdateStatus();
            return;
        }

        Zones.SetOccupied(new[] { Zones.Active.ZoneId });
        Player.Ships = Zones.Active.Spawned.Values.OfType<Ship>().ToList();
        UpdateFlight(delta);
        if (_messageTime > 0 && (_messageTime -= delta) <= 0 && _phase == Phase.Flying) Hud.Say("", Hud.Ink);
        UpdateStatus();
    }

    void UpdateFlight(double delta)
    {
        Ship ship = Ship;
        bool aboard = ship != null && Player.CurrentMode != Player.Mode.Outside && ship.GetParent() == Zones.Active.Root;
        string here = Zones.Active.ZoneId;

        switch (_phase)
        {
            case Phase.Flying:
                if (!aboard) return;
                if (here == ZoneIds.Surface)
                {
                    float altitude = ship.Position.Y;
                    Places.SetAltitude(here, altitude);
                    if (altitude > SurfaceWatch && ship.Velocity.Y > 0) StartPreload(ZoneIds.Orbit);
                    if (altitude >= Places.SurfaceEdgeAltitude) BeginTunnel(ZoneIds.Orbit, climbing: true);
                    if (altitude > 3f) _airborne = true;
                    else if (_airborne && altitude <= Ship.GroundClearance + 0.05f)
                    {
                        _airborne = false; // touched down: the landing leaves a scorch mark, a terrain edit like any other
                        WorldOps.Scorch(Saver.Table, StarterWorld.SurfaceTerrain, ZoneIds.Surface, ship.Position.X, ship.Position.Z, 7);
                        Places.Respawn(Zones.Active, StarterWorld.SurfaceTerrain);
                    }
                }
                else if (here == ZoneIds.Orbit)
                {
                    float altitude = Places.OrbitAltitude(ship.Position);
                    float radial = ship.Velocity.Dot(Places.OrbitUp(ship.Position));
                    if (altitude < Places.OrbitPreloadAltitude && radial < 0) StartPreload(ZoneIds.Surface);
                    if (altitude <= Places.OrbitEdgeAltitude) BeginTunnel(ZoneIds.Surface, climbing: false);
                }
                break;

            case Phase.Tunnel:
                _transitTime += delta;
                Hud.Tunnel.Strength = Mathf.Min(1f, (float)(_transitTime / 0.35));
                Zone to = Zones.Preload(_transitTo);
                string verb = _climbing ? "CLIMBING TO ORBIT" : "RE-ENTRY";
                Hud.Say(to == null
                    ? $"{verb}\nwaiting for room to load {_transitTo}"
                    : $"{verb}\n{_transitTo} {(to.IsReady ? "ready" : $"loading {to.Progress * 100:F0}%")}  ·  {_transitTime:F1} s", Hud.Ink);
                if (_transitTime >= Tunnel.MinimumSeconds && to != null && to.IsReady) Cross(to);
                break;

            case Phase.FadeOut:
                Hud.Tunnel.Strength = Mathf.Max(0f, Hud.Tunnel.Strength - (float)delta / 0.6f);
                if (Hud.Tunnel.Strength <= 0f)
                {
                    _phase = Phase.Flying;
                    Say(_lastCrossing, Hud.Good, 5);
                }
                break;
        }
    }

    /// <summary>On the surface, "nearing the edge" starts this far up.</summary>
    static float SurfaceWatch => Places.SurfacePreloadAltitude;

    void StartPreload(string zoneId)
    {
        if (Zones.Get(zoneId) == null) _preloadStarted = Zones.Clock;
        Zones.Preload(zoneId);
    }

    void BeginTunnel(string to, bool climbing)
    {
        StartPreload(to);
        _phase = Phase.Tunnel;
        _transitTo = to;
        _climbing = climbing;
        _transitTime = 0;
        Ship.Frozen = true;
        Hud.Tunnel.Climbing = climbing;
    }

    /// <summary>The seam itself: write the ship's record into the new zone and commit, then move the node.</summary>
    void Cross(Zone to)
    {
        Ship ship = Ship;
        Zone from = Zones.Active;
        Transform3D placement;
        Vector3 velocity;
        if (_climbing)
        {
            Vector3 point = Places.OrbitPointAbove(ship.Position.X, ship.Position.Z, Places.OrbitArrivalAltitude);
            Vector3 up = Places.OrbitUp(point);
            var yaw = new Basis(Vector3.Up, ship.Rotation.Y);
            placement = new Transform3D(new Basis(new Quaternion(Vector3.Up, up)) * yaw, point);
            velocity = up * 8f;
        }
        else
        {
            Vector2 below = Places.SurfacePointBelow(ship.Position);
            Vector3 up = Places.OrbitUp(ship.Position);
            Vector3 forward = new Basis(new Quaternion(up, Vector3.Up)) * -ship.GlobalBasis.Z;
            forward = new Vector3(forward.X, 0, forward.Z);
            if (forward.LengthSquared() < 0.01f) forward = Vector3.Forward;
            placement = new Transform3D(Basis.LookingAt(forward.Normalized(), Vector3.Up), new Vector3(below.X, Places.SurfaceArrivalAltitude, below.Y));
            velocity = Vector3.Down * 12f;
        }
        Quaternion q = placement.Basis.GetRotationQuaternion();
        double[] rotation = { Math.Round(q.X, 4), Math.Round(q.Y, 4), Math.Round(q.Z, 4), Math.Round(q.W, 4) };

        bool crossed = Zones.Cross(ship.RecordId, ship, from, to,
            writeRecord: () => WorldOps.Place(Saver.Table, ship.RecordId, to.ZoneId, null, Places.Round(placement.Origin), rotation: rotation),
            placeNode: node =>
            {
                ship.Transform = placement;
                ship.Velocity = velocity;
                ship.LocalAngularVelocity = Vector3.Zero;
                ship.InSpace = to.ZoneId != ZoneIds.Surface;
                ship.ResetPhysicsInterpolation();
            });

        ship.Frozen = false;
        _phase = Phase.FadeOut;
        if (!crossed)
        {
            ship.Velocity = _climbing ? Vector3.Down * 20f : Places.OrbitUp(ship.Position) * 20f; // back away from the edge
            Say(Zones.Trouble ?? Saver.LastError ?? "could not cross", Hud.Bad, 8);
            return;
        }
        Activate(to);
        _airborne = true;
        double readyAfter = _preloadStarted >= 0 ? to.ReadyAt - _preloadStarted : 0;
        _lastCrossing = $"crossed to {to.ZoneId}: it was ready {readyAfter:F1} s after preloading began; tunnel {_transitTime:F1} s" +
                        (_transitTime > Tunnel.MinimumSeconds + 0.1 ? " (waited for the zone)" : " (the minimum)");
        _preloadStarted = -1;
        GD.Print(_lastCrossing);
    }

    /// <summary>The player stepped on or off a ship: their record (and any crate they carry) moves with them.</summary>
    void OnFrameChanged()
    {
        Zone zone = Zones.Active;
        bool aboard = Player.CurrentMode != Player.Mode.Outside;
        WorldOps.Place(Saver.Table, Player.RecordId,
            aboard ? ZoneIds.Cabin(Player.Ship.RecordId) : zone.ZoneId,
            aboard ? Player.Ship.RecordId : null,
            Places.Round(Player.GlobalPosition), yaw: Math.Round(Player.Yaw, 3));
        Hands.FollowPlayer();
    }

    // ---- Input ------------------------------------------------------------------------------

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || !Hud.ConsoleOpen) return;
        if (key.PhysicalKeycode is Key.Escape or Key.Quoteleft or Key.F7)
        {
            Hud.CloseConsole();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.PhysicalKeycode)
            {
                case Key.F5:
                    Saver.SaveSoon();
                    Say("saving...", Hud.Ink, 2);
                    return;
                case Key.F9:
                    Reload();
                    return;
                case Key.F7:
                case Key.Quoteleft:
                    Hud.OpenConsole();
                    if (key.PhysicalKeycode == Key.F7) ListBackups();
                    GetViewport().SetInputAsHandled();
                    return;
                case Key.F8:
                    Say(Saver.ChaosKill(System.Environment.TickCount), Hud.Bad, 30);
                    return;
                case Key.E or Key.Z or Key.B or Key.X or Key.M when Hands != null && _phase == Phase.Flying:
                    Hands.Handle(key.PhysicalKeycode);
                    return;
            }
        }
        Player?.HandleInput(e);
    }

    /// <summary>F9: back to what is on disk. A reload is a restart of the scene: nothing unsaved survives it.</summary>
    void Reload()
    {
        Saver.FinishPending(wait: true);
        GetTree().ReloadCurrentScene();
    }

    // ---- The host console --------------------------------------------------------------------

    void RunCommand(string line)
    {
        string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (words[0].ToLowerInvariant())
        {
            case "help":
                Hud.Print("save · reload · audit · backups · backup · restore N · zones\ncleanup ID · mode ID base|capital-ship · chaos");
                break;
            case "save":
                Hud.Print(Saver.SaveNow("the console") ? $"saved: {Saver.LastCommit}" : $"save failed: {Saver.LastError}");
                break;
            case "reload":
            case "load":
                Reload();
                break;
            case "audit":
                Saver.FinishPending(wait: true);
                LoadResult check = new SaveStore(SaveFolder, GameRecords.Schemas()).Load();
                Hud.Print(check.Summary());
                break;
            case "backups":
                ListBackups();
                break;
            case "backup":
                if (Saver.Store.IsOpen && Saver.SaveNow("a manual backup")) Hud.Print($"backed up: {Saver.Store.CreateBackup("manual")}");
                break;
            case "restore":
                Restore(words.Length > 1 ? words[1] : "1");
                break;
            case "zones":
                foreach (Zone z in Zones.Live) Hud.Print($"{z.ZoneId}: {z.State}, {z.Spawned.Count} records, lingers {z.LingerLeft:F0} s");
                break;
            case "cleanup" when words.Length > 1:
                if (!Saver.Table.TryGet(words[1], out _)) { Hud.Print($"there is no record {words[1]}"); break; }
                string refusal = Saver.Table.Cleanup(words[1]);
                if (refusal == null && Zones.Active != null) Places.Despawn(Zones.Active, words[1]);
                Hud.Print(refusal ?? $"{words[1]} was world-generated and untouched, so it was cleaned up");
                break;
            case "mode" when words.Length > 2:
                try
                {
                    WorldOps.SwitchCoreMode(Saver.Table, words[1], words[2]);
                    if (Zones.Active != null) Places.Respawn(Zones.Active, words[1]);
                    Hud.Print($"{words[1]}'s core is in {words[2]} mode: every block and crate stays put");
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or KeyNotFoundException)
                {
                    Hud.Print(ex.Message);
                }
                break;
            case "chaos":
                Say(Saver.ChaosKill(System.Environment.TickCount), Hud.Bad, 30);
                break;
            default:
                Hud.Print("unknown command; try help");
                break;
        }
    }

    void ListBackups()
    {
        List<BackupInfo> backups = Saver.Store.ListBackups();
        if (backups.Count == 0)
        {
            Hud.Print("there are no backups");
            return;
        }
        Hud.Print($"{backups.Count} backups, newest first (restore N):");
        for (int i = 0; i < Math.Min(backups.Count, 15); i++)
            Hud.Print($"  {i + 1,2}. {backups[i].TakenAtUtc.ToLocalTime():MMM dd HH:mm:ss}  {backups[i].Kind,-10} generation {backups[i].Generation}");
    }

    void Restore(string which)
    {
        List<BackupInfo> backups = Saver.Store.ListBackups();
        BackupInfo pick = int.TryParse(which, out int n) && n >= 1 && n <= backups.Count
            ? backups[n - 1]
            : backups.FirstOrDefault(b => b.Name == which);
        if (pick == null)
        {
            Hud.Print($"no backup {which}; try backups");
            return;
        }
        Saver.FinishPending(wait: true);
        LoadResult restored = Saver.Store.Restore(pick.Name);
        if (!restored.Ok)
        {
            Hud.Print(restored.Summary());
            return;
        }
        GD.Print($"restored {pick.Name}: {restored.Summary()}");
        GetTree().ReloadCurrentScene();
    }

    // ---- The HUD -------------------------------------------------------------------------------

    void Say(string text) => Say(text, Hud.Ink, 4);

    void Say(string text, Color color, double seconds)
    {
        _message = text;
        _messageTime = seconds;
        if (_phase != Phase.Tunnel) Hud.Say(text, color);
    }

    void UpdateStatus()
    {
        var lines = new List<string>();
        RecordTable table = Saver.Table;
        if (Zones.Active != null && Player != null)
        {
            Ship ship = Ship;
            string where = Player.CurrentMode switch
            {
                Player.Mode.Piloting => $"piloting {ship?.Name}",
                Player.Mode.Aboard => $"aboard {ship?.Name}",
                _ => "on foot",
            };
            string height = ship == null ? "" : Zones.Active.ZoneId == ZoneIds.Orbit
                ? $"{Places.OrbitAltitude(ship.Position):F0} m above the planet"
                : $"{ship.Position.Y:F0} m up";
            lines.Add($"Zone {Zones.Active.ZoneId} (drawn) · {where} · ship {height}, {ship?.Speed:F0} m/s");
        }
        lines.Add($"Live zones ({Zones.Live.Count}/{Zones.MaxLive}): " + string.Join("  ·  ", Zones.Live.Select(DescribeZone)));

        SaveStore store = Saver.Store;
        string last = Saver.LastCommit == null ? "" : $" · last save {Saver.LastCommit.Written} records in {Saver.LastCommit.Elapsed.TotalMilliseconds:F0} ms at {Saver.LastCommitAt:HH:mm:ss}";
        lines.Add($"Save: generation {store?.Generation}{last} · {(Saver.Saving ? "saving now" : $"autosave in {Saver.UntilAutosave:F0} s")} · {table?.PendingChanges ?? 0} unsaved");
        if (Saver.LastError != null) lines.Add("SAVE PROBLEM: " + Saver.LastError);

        LoadResult load = Saver.LastLoad;
        if (load != null)
        {
            lines.Add(load.Ok
                ? $"Audit on load: OK · {load.Checked} checksums verified · {load.Found.OwnedOf(RecordTypes.Structure)} structures, {load.Found.Blocks} blocks, {load.Found.Items} items, all as the manifest promised"
                : $"Audit on load: REFUSED · {load.Problems.Count} problem(s)");
            if (load.RecoveredFromInterruptedSave)
                lines.Add($"Recovered: {(load.UnfinishedFilesRemoved > 0 || load.TempManifestFound ? $"an interrupted save was rolled back to generation {load.Generation} ({load.UnfinishedFilesRemoved} unfinished files removed)" : $"an interrupted clean-up was finished ({load.LeftoverFilesRemoved} old files removed)")}");
        }
        if (table != null)
        {
            if (table.ChangeStamp != _countsStamp)
            {
                _countsStamp = table.ChangeStamp;
                _countsText = ItemTotals(table);
            }
            lines.Add(_countsText);
            if (Zones.Active?.AuditProblems.Count > 0) lines.Add("ZONE AUDIT: " + Zones.Active.AuditProblems[0]);
            if (Hands?.Carrying != null) lines.Add($"Carrying {Hands.Carrying}");
        }
        if (!string.IsNullOrEmpty(_lastCrossing)) lines.Add("Last seam: " + _lastCrossing);
        lines.Add($"{Engine.GetFramesPerSecond()} fps");
        Hud.Status.Text = string.Join("\n", lines);

        Hud.Keys.Text =
            "WASD move · mouse look / steer · Space jump / up · Ctrl crouch / down · Shift sprint / boost · F pilot seat · Q/E roll · V third person\n" +
            "E pick up, carry or set down · Z drop 5 items · B build a block (1 plate) · X take a block off · M switch a core's mode\n" +
            "F5 save · F9 reload · F7 backups and console · F8 CHAOS: kill the game mid-save · Esc free the mouse";
    }

    string DescribeZone(Zone z)
    {
        if (!z.IsReady) return $"{z.ZoneId} [loading {z.Progress * 100:F0}%]";
        if (z == Zones.Active) return $"{z.ZoneId} [here]";
        TimeSpan left = TimeSpan.FromSeconds(Math.Max(0, z.LingerLeft));
        return $"{z.ZoneId} [empty, unloads in {left:m\\:ss}]";
    }

    static string ItemTotals(RecordTable table)
    {
        var byKind = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (SaveRecord r in table.All)
            if (WorldOps.HoldsItems(r) && r.Items > 0)
                foreach (ItemStack s in WorldOps.StacksOf(r)) byKind[s.Item] = byKind.GetValueOrDefault(s.Item) + s.Count;
        AuditCounts counts = AuditCounts.Of(table.All);
        string inventory = table.TryGet(StarterWorld.PlayerId, out SaveRecord me) ? Stacks.Describe(WorldOps.StacksOf(me)) : "?";
        return $"Items {counts.Items} ({string.Join(", ", byKind.Select(kv => $"{kv.Key} {kv.Value}"))}) · blocks {counts.Blocks} · records {counts.Records} · your pack: {inventory}";
    }
}
