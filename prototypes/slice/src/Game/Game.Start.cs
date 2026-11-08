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
/// The start of a game, kept apart from the rest so it can be swapped: the castaway wakes in the
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
        GD.Print($"start site: {World}; {site.FromLanding:F0} m from the landing site, {site.Steps} steps up{(site.Landmark is Vector3 l ? $", facing {site.Poi?.Name} {l.DistanceTo(Bunker.ExitOf(site)):F0} m off" : "")}");
        return Bunker.ExitOf(site);
    }

    /// <summary>The castaway in their cryopod, the kit in the locker, the bunker their respawn point.</summary>
    void WakeInBunker()
    {
        if (_site is not Bunker.Site site) throw new InvalidOperationException($"no start site for a bunker on {World} ({Zone.Planet.Pois.Count} places on the planet: {string.Join(", ", System.Linq.Enumerable.Select(Zone.Planet.Pois, q => $"{q.Name} {q.Instance?.State} {new Vector2(q.Origin.X - Zone.LandingSite.X, q.Origin.Z - Zone.LandingSite.Z).Length():F0} m off, {q.Siting}"))})");
        Bunker = Bunker.Dig(Zone, site);
        foreach (var (id, n) in Zone.Data.Rules.Materials.StartKit) Bunker.MineLocker.Loot.Storage.Add(id, n);
        Bunker.MineLocker.Loot.Grid.Reconcile();

        N.Vector3 stand = Bunker.CryoStand.ToNumerics();
        Survivor = Survivor.Spawn(Zone, stand);
        Vector3 outward = Bunker.CryoFront - Bunker.CryoStand;
        Survivor.Yaw = MathF.Atan2(-outward.X, -outward.Z);
        Survivor.Body.DamageSink = Suit = new Suit(Zone, Zone.Data.Survival(), (int)Zone.Spec.Seed);
        Suit.Sheltered = () => Bunker.Inside(Survivor.WorldFeet, Zone);
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

    /// <summary>Playing once the ground round the castaway is in, and what stands on it: nothing appears after you've come up the stairs.</summary>
    void Waking()
    {
        if (Survivor == null || !Zone.Streamer.ReadyAround(Survivor.WorldFeet, 1, needCollision: true)) return;
        _waiting ??= System.Diagnostics.Stopwatch.StartNew();
        Zone.Infra.Hurry = !WorldStanding;
        bool orbit = Spaceflight.LoadOrbit();
        if ((Zone.Infra.Hurry || !orbit) && _waiting.Elapsed.TotalSeconds < MostWait) return;
        Zone.Infra.Hurry = false;
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

    /// <summary>The castaway is awake and can move (fires once).</summary>
    public event Action Woke;
}
