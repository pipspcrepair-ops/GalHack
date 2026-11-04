using System.Collections.Generic;
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
/// sirens, spawning the force through the combat factory, ordering withdrawals.
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
    public Garrison Garrison;
    public Hud Hud;

    public Invasion Current => Director.Base(HomeId).Active;
    public int InvadersRemaining { get; private set; }
    public int TroopsOnFoot { get; private set; }
    public int CrewAndAboard { get; private set; }
    public int PlayersPresent { get; private set; }
    /// <summary>Everything the director said, newest last, for tests and the debug log.</summary>
    public readonly List<DirectorEvent> Heard = new();

    sealed class PendingVehicle
    {
        public float At;
        public VehiclePlan Plan;
        public VehicleRoute Route;
    }

    readonly List<PendingVehicle> _pending = new();
    float _clock, _statsTimer, _labelTimer;
    bool _retreating;

    public override void _Ready()
    {
        ProcessPhysicsPriority = -5; // decide before soldiers and vehicles move this tick
        Model.OnlineChanged += online =>
        {
            if (online) Director.ReportCorePlaced(HomeId);
            else Director.ReportCoreDestroyed(HomeId);
        };
        Garrison.CoreDestroyed += () => Director.SetEnemyBaseState(Garrison.Id, EnemyBaseState.Destroyed);
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
            case DirectorEventKind.Warning:
                Hud?.Announce(e.Text, e.Level, 12f);
                break;
            case DirectorEventKind.ForceArrived:
                SpawnForce(Current);
                Hud?.Announce($"{Director.Data.Attacker} force arriving from {Current.Source.Compass}: {Current.Force.Troops} troops in {Current.Force.VehicleSummary(Director.Data)}.", 0);
                break;
            case DirectorEventKind.Withdraw:
                OrderWithdrawal();
                Hud?.Announce(e.Text, 0);
                break;
            case DirectorEventKind.InvasionEnded:
                _pending.Clear();
                _retreating = false;
                Hud?.Announce(e.Text[(e.Text.IndexOf(':') + 1)..].Trim(), 0, 10f);
                break;
            case DirectorEventKind.SourceLost:
            case DirectorEventKind.InvasionsStopped:
            case DirectorEventKind.InvasionsResumed:
            case DirectorEventKind.BaseOffline:
            case DirectorEventKind.BaseOnline:
                Hud?.Announce(e.Text, 0);
                break;
        }
    }

    // ---- The force --------------------------------------------------------------------

    /// <summary>Queues the force's vehicles along the approach: ground vehicles from the source base's gate if it's close, everything else from the deploy line.</summary>
    void SpawnForce(Invasion inv)
    {
        ApproachData a = Director.Data.Approach;
        Vector3 centre = World.BaseCentre;
        GroundPos dir = GroundPos.FromBearing(inv.Source.Bearing, 1);
        var outward = new Vector3((float)dir.X, 0, (float)dir.Z);
        var right = new Vector3(-outward.Z, 0, outward.X);
        var sourcePos = new Vector3((float)inv.Source.Position.X, 0, (float)inv.Source.Position.Z);

        foreach (VehiclePlan v in inv.Force.Vehicles)
        {
            float lateral = (float)v.Offset;
            Vector3 deploy = !v.Flies && inv.Source.Kind != SourceKind.Ship && inv.Source.Distance <= a.DeployDistance + 50
                ? sourcePos - outward * 40f + right * lateral
                : centre + outward * (float)a.DeployDistance + right * lateral;
            if (v.Flies) deploy.Y = 70f;
            Vector3 drop = centre + outward * (float)a.DropDistance + right * lateral * 0.8f;
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
        IInvaderTroop troop = Factory.SpawnTroop(role, at, World.BaseCentre - at);
        troop.Ride = vehicle;
        if (Current?.Phase == InvasionPhase.Withdrawing) troop.Withdraw(vehicle);
        else troop.Assault();
    }

    /// <summary>
    /// Tactics that aren't the director's business: once every soldier on foot is down the vehicles
    /// fall back, and while withdrawing each vehicle leaves once its soldiers are aboard or dead.
    /// </summary>
    void ManageForce()
    {
        Invasion inv = Current;
        if (inv == null) return;
        var vehicles = World.Vehicles.Where(v => !v.IsGone).ToList();

        if (inv.Phase == InvasionPhase.Assault && !_retreating)
        {
            bool anyTroops = _pending.Count > 0 || World.Troops.Any(t => t.IsAlive && !t.IsGone) ||
                             vehicles.Any(v => v.TroopsAboard > 0);
            if (!anyTroops && vehicles.Count > 0 && _clock > 1f)
            {
                _retreating = true;
                foreach (IInvaderVehicle v in vehicles) SendHome(v);
                Hud?.Announce("The assault is broken: Vharn vehicles are falling back.", 0);
            }
        }
        else if (inv.Phase == InvasionPhase.Withdrawing)
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
        foreach (Vector3 p in v.BailOut())
        {
            IInvaderTroop crew = Factory.SpawnTroop("crew", p, p - World.BaseCentre);
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

    void CountInvaders()
    {
        TroopsOnFoot = World.Troops.Count(t => t.IsAlive && !t.IsGone);
        CrewAndAboard = World.Vehicles.Where(v => !v.IsGone).Sum(v => v.Crew.AliveCount + v.TroopsAboard) +
                        _pending.Sum(p => p.Plan.Crew + p.Plan.Troops.Count);
        InvadersRemaining = TroopsOnFoot + CrewAndAboard;
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
                   (Garrison.Destroyed ? "OKKAR-9  DESTROYED" : "OKKAR-9  ACTIVE 600 M NE");
        }
        else if (inv.Phase == InvasionPhase.Incoming)
        {
            alarm = inv.WarningLevel >= 3;
            header = $"WARNING {inv.WarningLevel} OF 3";
            body = $"{Director.Data.Attacker.ToUpperInvariant()} ASSAULT\nFROM  {inv.Source.Name.ToUpperInvariant()}\n" +
                   $"BEARING  {inv.Source.BearingDegrees:000}° {inv.Source.Compass}\nARRIVAL  T-{Hud.Clock(inv.SecondsToArrival(Director.Now))}";
            if (inv.OnLongRangeScan && inv.Force != null) body += $"\nSCAN  {inv.Force.Tier.ToUpperInvariant()} · {inv.Force.Troops} TROOPS";
        }
        else if (inv.Phase == InvasionPhase.Assault)
        {
            alarm = true;
            header = "UNDER ATTACK";
            Block core = Model.Grid.Core;
            body = $"INVADERS  {InvadersRemaining}\nFROM  {inv.Source.Compass}\nCORE  {(core == null ? 0 : (int)(core.Health01 * 100))}%\nSIRENS SOUNDING";
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
        return PlayersPresent == 0 ? "No invasion: nobody is in the base's zone." :
            !Model.Online ? "No invasion: the base is offline." : "No invasion: no Vharn source and ship raids are off.";
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
