using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Invasions.Combat;
using Invasions.Director;
using Invasions.Model;

namespace Invasions.World;

/// <summary>
/// Glue between the director (which decides) and the scene (which shows and fights). Each physics
/// tick it reports who is home, the base's stats and how many invaders are left, advances the
/// director's clock, and acts on what the director decided: warnings to the visor and consoles,
/// sirens, spawning the force through the combat factory, ordering withdrawals. It also runs the
/// force-level tactics that aren't the director's business: falling back when the troops are spent,
/// and for raiders, running for the rides once the raid has gone on long enough.
/// </summary>
public partial class InvasionController : Node
{
    public const string HomeId = "home";

    public InvasionDirector Director;
    public BaseModel Model;
    public BaseView View;
    public CombatWorld World;
    public ICombatFactory Factory;
    public Player Player;
    public IReadOnlyList<EnemyBaseNode> EnemyBases = new List<EnemyBaseNode>();
    public Hud Hud;

    public Invasion Current => Director.Base(HomeId).Active;
    public int InvadersRemaining { get; private set; }
    public int TroopsOnFoot { get; private set; }
    public int CrewAndAboard { get; private set; }
    public int PlayersPresent { get; private set; }
    /// <summary>Raiders have had their time and are running for their rides.</summary>
    public bool RaidOver { get; private set; }
    /// <summary>Everything the director said, newest last, for tests and the debug log.</summary>
    public readonly List<DirectorEvent> Heard = new();
    /// <summary>Raised when the world's own settings change (the options), so they can be saved.</summary>
    public event System.Action SettingsChanged;

    sealed class PendingVehicle
    {
        public float At;
        public VehiclePlan Plan;
        public VehicleRoute Route;
    }

    readonly List<PendingVehicle> _pending = new();
    float _clock, _statsTimer, _labelTimer, _firstLanding = -1f;
    int _storageAtLanding;
    bool _retreating;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -5; // decide before soldiers and vehicles move this tick
        Model.OnlineChanged += online =>
        {
            if (online) Director.ReportCorePlaced(HomeId);
            else Director.ReportCoreDestroyed(HomeId);
        };
        foreach (EnemyBaseNode enemy in EnemyBases)
        {
            string id = enemy.Id;
            enemy.CoreDestroyed += () => Director.SetEnemyBaseState(id, EnemyBaseState.Destroyed);
        }
        Director.SetBaseStats(HomeId, Model.Stats());
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _clock += dt;

        // The presence rule needs to know who's home.
        PlayersPresent = Player.GlobalPosition.DistanceTo(World.BaseCentre) <= Model.Data.ZoneRadius ? 1 : 0;
        Director.SetPlayersPresent(HomeId, PlayersPresent);

        _statsTimer -= dt;
        if (_statsTimer <= 0f)
        {
            _statsTimer = 1f;
            Director.SetBaseStats(HomeId, Model.Stats());
        }

        Director.Tick(dt);
        foreach (DirectorEvent e in Director.DrainEvents()) Handle(e);

        SpawnDue();
        ManageForce();
        CountInvaders();
        Director.ReportInvadersRemaining(HomeId, InvadersRemaining);
        foreach (DirectorEvent e in Director.DrainEvents()) Handle(e);

        View.Sirens.SetOn(Current?.SirenOn ?? false);

        _labelTimer -= dt;
        if (_labelTimer <= 0f)
        {
            _labelTimer = 0.25f;
            UpdateConsoles();
            View.RefreshStorageLabels();
        }
    }

    // ---- Director events --------------------------------------------------------------

    void Handle(DirectorEvent e)
    {
        Heard.Add(e);
        GD.Print($"[director] {e}");
        switch (e.Kind)
        {
            case DirectorEventKind.InvasionScheduled:
                // The standing line on the visor follows whoever is coming.
                _selected = Director.Data.Factions.All.ToList().FindIndex(f => f.Id == Current?.Faction) is int i and >= 0 ? i : _selected;
                break;
            case DirectorEventKind.Warning:
                Hud?.Announce(e.Text, e.Level, 12f);
                break;
            case DirectorEventKind.ForceArrived:
                SpawnForce(Current);
                Hud?.Announce(Director.Describe("arriving", Current), 0);
                break;
            case DirectorEventKind.Withdraw:
                OrderWithdrawal();
                Hud?.Announce(e.Text, 0);
                break;
            case DirectorEventKind.InvasionEnded:
                _pending.Clear();
                _retreating = false;
                RaidOver = false;
                _firstLanding = -1f;
                Hud?.Announce(e.Text[(e.Text.IndexOf(':') + 1)..].Trim(), 0, 10f);
                break;
            case DirectorEventKind.SourceLost:
            case DirectorEventKind.InvasionsStopped:
            case DirectorEventKind.InvasionsResumed:
            case DirectorEventKind.BaseOffline:
            case DirectorEventKind.BaseOnline:
            case DirectorEventKind.StandingChanged:
            case DirectorEventKind.InvasionsToggled:
                Hud?.Announce(e.Text, 0);
                break;
        }
    }

    // ---- The force --------------------------------------------------------------------

    /// <summary>
    /// Queues the force's vehicles along the approach: ground vehicles from the source base's gate if
    /// it's close, everything else from the deploy line; each sets its troops down at its own drop
    /// distance (bikes ride right up, a growth breaks the surface a hundred metres out).
    /// </summary>
    void SpawnForce(Invasion inv)
    {
        ApproachData a = Director.Data.Approach;
        Vector3 centre = World.BaseCentre;
        GroundPos dir = GroundPos.FromBearing(inv.Source.Bearing, 1);
        var outward = new Vector3((float)dir.X, 0, (float)dir.Z);
        var right = new Vector3(-outward.Z, 0, outward.X);
        var sourcePos = new Vector3((float)inv.Source.Position.X, 0, (float)inv.Source.Position.Z);
        _firstLanding = -1f;

        foreach (VehiclePlan v in inv.Force.Vehicles)
        {
            float lateral = (float)v.Offset;
            Vector3 deploy = !v.Flies && inv.Source.Kind != SourceKind.Ship && inv.Source.Distance <= a.DeployDistance + 50
                ? sourcePos - outward * 40f + right * lateral
                : centre + outward * (float)a.DeployDistance + right * lateral;
            if (v.Flies) deploy.Y = 70f;
            float dropDistance = (float)(v.DropDistance ?? a.DropDistance);
            Vector3 drop = centre + outward * dropDistance + right * lateral * 0.8f;
            _pending.Add(new PendingVehicle
            {
                At = _clock + (float)v.Delay,
                Plan = v,
                Route = new VehicleRoute(deploy, drop, centre, outward, (float)a.GoneDistance),
            });
        }
    }

    void SpawnDue()
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            if (_pending[i].At > _clock) continue;
            IInvaderVehicle vehicle = Factory.SpawnVehicle(_pending[i].Plan, _pending[i].Route);
            vehicle.TroopDismounted += OnDismount;
            _pending.RemoveAt(i);
        }
    }

    void OnDismount(IInvaderVehicle vehicle, string role, Vector3 at)
    {
        IInvaderTroop troop = Factory.SpawnTroop(vehicle.Plan.Faction, role, at, World.BaseCentre - at);
        troop.Ride = vehicle;
        if (_firstLanding < 0f)
        {
            _firstLanding = _clock;
            _storageAtLanding = StorageLeft;
        }
        if (Current?.Phase == InvasionPhase.Withdrawing || RaidOver) troop.Withdraw(vehicle);
        else troop.Assault();
    }

    /// <summary>
    /// Tactics that aren't the director's business: once every soldier on foot is down the vehicles
    /// fall back; raiders run for their rides when their raid time is up; and while leaving, each
    /// vehicle goes once its soldiers are aboard or dead.
    /// </summary>
    void ManageForce()
    {
        Invasion inv = Current;
        if (inv == null) return;
        FactionData faction = Director.Data.Faction(inv.Faction);
        var vehicles = World.Vehicles.Where(v => !v.IsGone).ToList();

        // Grab and run: when the raid has had its time, or every storage container is broken open and
        // there is nothing left to grab (spill crates are too heavy to lift), the raiders head for
        // their rides, empty-handed.
        double raid = faction.Tactics.RaidSeconds;
        bool landed = _firstLanding >= 0f;
        bool timeUp = landed && _clock - _firstLanding >= raid;
        bool nothingLeft = landed && faction.Tactics.Targets == "storage" && _storageAtLanding > 0 && StorageLeft == 0;
        if (inv.Phase == InvasionPhase.Assault && !RaidOver && raid > 0 && (timeUp || nothingLeft))
        {
            RaidOver = true;
            OrderWithdrawal();
            Hud?.Announce(Director.Describe("raidOver", inv), 0);
        }

        if (inv.Phase == InvasionPhase.Assault && !_retreating && !RaidOver)
        {
            bool anyTroops = _pending.Count > 0 || World.Troops.Any(t => t.IsAlive && !t.IsGone) ||
                             vehicles.Any(v => v.TroopsAboard > 0);
            if (!anyTroops && vehicles.Count > 0 && _clock > 1f)
            {
                _retreating = true;
                foreach (IInvaderVehicle v in vehicles) SendHome(v);
                Hud?.Announce(Director.Describe("broken", inv), 0);
            }
        }
        else if (inv.Phase == InvasionPhase.Withdrawing || RaidOver)
        {
            foreach (IInvaderVehicle v in vehicles)
            {
                bool waiting = World.Troops.Any(t => t.Ride == v && t.IsAlive && !t.IsGone && t.State == TroopState.Withdrawing);
                if (!waiting) SendHome(v);
            }
        }
    }

    /// <summary>A vehicle that can drive leaves; a stranded one's crew climb out and walk away.</summary>
    void SendHome(IInvaderVehicle v)
    {
        if (!v.IsStranded)
        {
            v.Depart();
            return;
        }
        string crewRole = Director.Data.Faction(v.Plan.Faction).CrewRole;
        foreach (Vector3 p in v.BailOut())
        {
            IInvaderTroop crew = Factory.SpawnTroop(v.Plan.Faction, crewRole, p, p - World.BaseCentre);
            crew.Withdraw(null);
        }
    }

    void OrderWithdrawal()
    {
        _pending.Clear(); // vehicles that hadn't shown up yet turn back
        foreach (IInvaderVehicle v in World.Vehicles.Where(v => !v.IsGone).ToList())
        {
            if (v.IsStranded) SendHome(v);
            else v.AwaitPickup();
        }
        foreach (IInvaderTroop t in World.Troops.Where(t => t.IsAlive && !t.IsGone).ToList())
        {
            IInvaderVehicle ride = t.Ride is { IsGone: false, IsStranded: false } r ? r : null;
            t.Withdraw(ride);
        }
    }

    int StorageLeft => Model.Grid.Alive.Count(b => b.Kind == BlockKind.Container);

    void CountInvaders()
    {
        TroopsOnFoot = World.Troops.Count(t => t.IsAlive && !t.IsGone);
        CrewAndAboard = World.Vehicles.Where(v => !v.IsGone).Sum(v => v.Crew.AliveCount + v.TroopsAboard) +
                        _pending.Sum(p => p.Plan.Crew + p.Plan.Troops.Count);
        InvadersRemaining = TroopsOnFoot + CrewAndAboard;
    }

    // ---- Sources and standing ---------------------------------------------------------

    /// <summary>The scene's node for an enemy base the director knows.</summary>
    public EnemyBaseNode Node(EnemyBase e) => e == null ? null : EnemyBases.FirstOrDefault(n => n.Id == e.Id);

    /// <summary>Where the next invasion would come from, in a line: the nearest hostile base, or ship raids, or nothing.</summary>
    public string SourceSummary()
    {
        PlayerBase home = Director.Base(HomeId);
        EnemyBase e = Director.NearestSourceBase(home);
        if (e != null)
        {
            double d = home.Position.DistanceTo(e.Position);
            string compass = GroundPos.Compass(home.Position.BearingTo(e.Position));
            return string.Create(CultureInfo.InvariantCulture, $"{e.Name} ({Director.Data.Faction(e.Faction).Name}), {d:F0} m {compass}");
        }
        var ships = Director.ShipRaiders();
        return ships.Count > 0 ? $"ship raids ({string.Join(", ", ships.Select(f => f.Name))})" : "no hostile source";
    }

    /// <summary>Debug (key K): knocks out the base that would send the next invasion, as if its core were shot out.</summary>
    public string KnockOutSource()
    {
        EnemyBaseNode node = Node(Director.NearestSourceBase(Director.Base(HomeId)));
        if (node == null) return "No hostile base in range to knock out.";
        node.DestroyCore();
        return "";
    }

    int _selected;

    /// <summary>The faction the standing debug keys act on.</summary>
    public FactionData SelectedFaction => Director.Data.Factions.All[_selected % Director.Data.Factions.All.Count];

    /// <summary>Debug (key J): the next faction for the standing keys.</summary>
    public void SelectNextFaction() => _selected = (_selected + 1) % Director.Data.Factions.All.Count;

    /// <summary>Debug (minus and equals): wrong the selected faction on purpose, or do it a favour.</summary>
    public void ShiftSelectedStanding(double amount)
    {
        string id = SelectedFaction.Id;
        if (amount < 0) Director.Wrong(id, -amount, deliberate: true);
        else Director.Favour(id, amount);
    }

    /// <summary>
    /// The options' invasions switch. Off calls off what is incoming and pulls back what is under way
    /// (the director does that); the sirens stop once the last invader is gone. Saved with the world.
    /// </summary>
    public void SetInvasionsEnabled(bool on)
    {
        if (Director.InvasionsEnabled == on) return;
        Director.SetInvasionsEnabled(on);
        foreach (DirectorEvent e in Director.DrainEvents()) Handle(e);
        SettingsChanged?.Invoke();
    }

    // ---- Consoles ---------------------------------------------------------------------

    void UpdateConsoles()
    {
        Invasion inv = Current;
        string header, body;
        bool alarm = false;
        string baseName = Model.Data.Name.ToUpperInvariant();
        if (inv == null)
        {
            header = $"{baseName} DEFENCE NET";
            body = $"ALL QUIET\nSTORAGE NET  {Model.Loot.Storage.Total} ITEMS\nTURRETS  {Model.TurretsAlive} ON AUTO\n" +
                   (Director.InvasionsEnabled ? $"THREAT  {SourceSummary().ToUpperInvariant()}" : "INVASIONS  OFF");
        }
        else if (inv.Phase == InvasionPhase.Incoming)
        {
            alarm = inv.WarningLevel >= 3;
            header = $"WARNING {inv.WarningLevel} OF 3";
            body = $"{Director.Data.Faction(inv.Faction).Name.ToUpperInvariant()} ASSAULT\nFROM  {inv.Source.Name.ToUpperInvariant()}\n" +
                   $"BEARING  {inv.Source.BearingDegrees:000}° {inv.Source.Compass}\nARRIVAL  T-{Hud.Clock(inv.SecondsToArrival(Director.Now))}";
            if (inv.OnLongRangeScan && inv.Force != null) body += $"\nSCAN  {inv.Force.Tier.ToUpperInvariant()} · {inv.Force.Troops} TROOPS";
        }
        else if (inv.Phase == InvasionPhase.Assault)
        {
            alarm = true;
            header = "UNDER ATTACK";
            Block core = Model.Grid.Core;
            body = $"{Director.Data.Faction(inv.Faction).Tag}  {InvadersRemaining}\nFROM  {inv.Source.Compass}\nCORE  {(core == null ? 0 : (int)(core.Health01 * 100))}%\nSIRENS SOUNDING";
        }
        else
        {
            alarm = true;
            header = "INVADERS WITHDRAWING";
            body = $"REMAINING  {InvadersRemaining}\nSIRENS UNTIL THE LAST IS GONE";
        }
        foreach (ConsoleScreen console in View.Consoles)
            if (IsInstanceValid(console)) console.Show(header, body, alarm);
    }

    // ---- Debug and test hooks ---------------------------------------------------------

    /// <summary>Key I: start an invasion now, or if one is incoming, skip to its next warning.</summary>
    public string InvadeNow()
    {
        if (Current is { Phase: InvasionPhase.Incoming }) return Director.SkipToNextWarning(HomeId) ? "Skipped to the next warning." : "";
        if (Current != null) return "An invasion is already under way.";
        if (Director.StartInvasionNow(HomeId)) return "";
        if (!Director.InvasionsEnabled) return "No invasion: invasions are off (Esc: options).";
        return PlayersPresent == 0 ? "No invasion: nobody is in the base's zone." :
            !Model.Online ? "No invasion: the base is offline." : "No invasion: no hostile base in range and no ship raids.";
    }

    /// <summary>Runs the director straight to the force's arrival (screenshots and tests).</summary>
    public void SkipToArrival()
    {
        for (int i = 0; i < 4 && Current is { Phase: InvasionPhase.Incoming }; i++)
        {
            Director.SkipToNextWarning(HomeId);
            foreach (DirectorEvent e in Director.DrainEvents()) Handle(e);
        }
    }

    /// <summary>Spawns every queued vehicle now instead of on its stagger.</summary>
    public void SpawnPendingNow()
    {
        foreach (PendingVehicle p in _pending) p.At = _clock;
        SpawnDue();
    }
}
