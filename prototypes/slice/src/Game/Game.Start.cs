using System;
using GalHack.Items;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Slice.Player;
using Slice.Start;
using Slice.Ui;
using SurvivalVitals.Model;
using Survivor = Slice.Player.Survivor;
using N = System.Numerics;

namespace Slice.Game;

/// <summary>
/// The start of a game, kept apart from the rest so it can be swapped: the sleeper wakes in the
/// one working cryopod of a bunker buried at the start site, gets up (F opens the pod), takes the
/// starting kit from the locker and climbs the stairwell out to the surface.
/// </summary>
public partial class Game
{
    /// <summary>The bunker the game starts in.</summary>
    public Bunker Bunker { get; private set; }

    /// <summary>Where the start site's stairs come out on the ground (the start, for anything that needs one).</summary>
    public Vector3 Start => Bunker?.Outside ?? (Zone.IsBuilt ? Zone.LandingSite : Vector3.Zero);

    Bunker.Site? _site;

    /// <summary>Before any ground streams in: the bunker's site on this world, near the generator's landing site. Its way out is where the world centres.</summary>
    Vector3? PickStart()
    {
        _site = Bunker.FindSite(Zone, Bunker.TemperateNear(Zone, Zone.LandingSite));
        if (_site is not Bunker.Site site) return null;
        Bunker.Reserve(Zone, site);
        GD.Print($"start site: {World}; {site.FromLanding:F0} m from the landing site, {site.Steps} steps up{(site.Landmark is Vector3 l ? $", facing {site.Poi?.Name} {l.DistanceTo(Bunker.ExitOf(site)):F0} m off" : "")}");
        return Bunker.ExitOf(site);
    }

    /// <summary>Creative: every player weapon (guns and the rest) and the ammo each mode takes, in the locker.</summary>
    void StockArmoury(SharedStorage locker)
    {
        ItemCatalogue items = Zone.Data.Items;
        foreach (GalHack.Arms.WeaponDef def in Zone.Data.Arms.All)
        {
            if (items.Has(def.Item)) locker.Add(def.Item, def.Ammo == def.Item ? 20 : 1); // a mine is its own ammo
            foreach (GalHack.Arms.WeaponDef d in System.Linq.Enumerable.Prepend(def.Modes, def))
                if (d.Ammo != "" && d.Ammo != def.Item && items.Has(d.Ammo) && locker.Count(d.Ammo) == 0) locker.Add(d.Ammo, 20);
        }
    }

    /// <summary>The sleeper in their cryopod, the kit in the locker, the bunker their respawn point.</summary>
    void WakeInBunker()
    {
        if (_site is not Bunker.Site site) throw new InvalidOperationException($"no start site for a bunker on {World} ({Zone.Planet.Pois.Count} places on the planet: {string.Join(", ", System.Linq.Enumerable.Select(Zone.Planet.Pois, q => $"{q.Name} {q.State} {new Vector2(q.Origin.X - Zone.LandingSite.X, q.Origin.Z - Zone.LandingSite.Z).Length():F0} m off, {q.Siting}"))})");
        Bunker = Bunker.Dig(Zone, site);
        Zone.Wildlife?.SetBunker(Bunker);
        foreach (var (id, n) in Zone.Data.Rules.Materials.StartKit) Bunker.MineLocker.Loot.Storage.Add(id, n);
        if (Mode == GameMode.Creative) StockArmoury(Bunker.MineLocker.Loot.Storage);
        Bunker.MineLocker.Loot.Grid.Reconcile();

        N.Vector3 stand = Bunker.CryoStand.ToNumerics();
        Survivor = Survivor.Spawn(Zone, stand);
        Vector3 outward = Bunker.CryoFront - Bunker.CryoStand;
        Survivor.Yaw = MathF.Atan2(-outward.X, -outward.Z);
        Survivor.Body.DamageSink = Suit = new Suit(Zone, Zone.Data.Survival(), (int)Zone.Spec.Seed);
        Suit.Sheltered = () => Bunker.Inside(Survivor.WorldFeet, Zone);
        Suit.ClosedPlace = feet => Places?.PlaceAt(feet) is Slice.Places.Place p && p.Ready && p.Closed(feet) ? p : null;
        Suit.InPlace = feet => Places?.PlaceAt(feet);
        Suit.Said += line => Say(line, Tones.Caution);
        N.Vector3 front = Bunker.CryoFront.ToNumerics();
        Suit.Sim.RespawnPoints.Add(new RespawnPoint
        {
            Id = "bunker", Name = "Cryo bunker", Kind = RespawnKind.SurvivalTent,
            Position = new Pos(front.X, front.Y, front.Z), Stand = new Pos(front.X, front.Y, front.Z),
        });
        // The survival kit's "escape pod" rations are the locker's.
        Suit.Sim.Pods[0].Position = new Pos(front.X, front.Y, front.Z);
        if (SkipIntro) Bunker.Mine.OpenLid();
    }

    /// <summary>The longest the loading screen waits for what stands round the start, seconds.</summary>
    const double MostWait = 30;
    System.Diagnostics.Stopwatch _waiting;

    /// <summary>Whether everything that stands round the start is in: a place's blocks, the roads' ruins, wrecks and bridges.</summary>
    public bool WorldStanding => Zone.Pois.Built && Zone.Infra.Planned && Zone.Infra.Built;

    /// <summary>Playing once the ground round the sleeper is in, and what stands on it: nothing appears after you've come up the stairs.</summary>
    void Waking()
    {
        if (Survivor == null || !Zone.Streamer.ReadyAround(Survivor.WorldFeet, 1, needCollision: true)) return;
        _waiting ??= System.Diagnostics.Stopwatch.StartNew();
        Zone.Infra.Hurry = !WorldStanding;
        bool orbit = Spaceflight.LoadOrbit();
        if ((Zone.Infra.Hurry || !orbit) && _waiting.Elapsed.TotalSeconds < MostWait) return;
        Zone.Infra.Hurry = false;
        Spaceflight.ChartStartPlace(); // on the compass and the map with the bunker, from the first moment
        PlanetSurface.Poi.PoiSite.Loading = false;   // the rest of the places are built from here on, nearest first (PoiBuilds)
        Phase = Phase.Playing;
        Woke?.Invoke();
    }

    /// <summary>F in the cryopod: its lid swings open.</summary>
    bool OpenCryopod()
    {
        if (Bunker?.Mine is not CryoPod pod || pod.Open) return false;
        if (Survivor.WorldFeet.DistanceTo(Bunker.CryoStand) > 1.5f) return false;
        pod.OpenLid();
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, Bunker.CryoStand + Vector3.Up * 1.5f, 0.6f, -10f);
        return true;
    }

    /// <summary>Back in the bunker after dying: out of the cryopod, standing.</summary>
    void WakeAgain()
    {
        Survivor.PlaceOutside(Bunker.CryoFront.ToNumerics());
        Vector3 outward = Bunker.CryoFront - Bunker.CryoStand;
        Survivor.Yaw = MathF.Atan2(-outward.X, -outward.Z);
    }

    /// <summary>The sleeper is awake and can move (fires once).</summary>
    public event Action Woke;
}
