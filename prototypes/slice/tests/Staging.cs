using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GalHack.Physics;
using GalHack.Physics.Engine;
using Godot;
using Planets.Gen;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using N = System.Numerics;
using WeatherKind = Weather.Model.WeatherKind;

namespace Slice.Tests;

/// <summary>
/// The screenshot views: each stages one moment of the loop through the game itself, with its
/// own controls (tests/Driver) wherever a player's hands would do it. Views: pod, dig, build,
/// liftoff, flying, cockpit.
/// </summary>
public sealed partial class Staging
{
    readonly Game.Game _game;
    readonly Driver _drive;
    readonly Dictionary<string, string> _args;

    public Staging(Game.Game game, Dictionary<string, string> args)
    {
        _game = game;
        _drive = new Driver(game);
        _args = args;
    }

    public IEnumerable<int> Run(string view)
    {
        foreach (int x in _drive.UntilPlaying()) yield return x;
        _game.Zone.Weather.Bridge.Calm = true;
        _game.Zone.Wildlife.Frozen = view != "dig";
        foreach (int x in Play.MakeTool()) yield return x;
        // Every view but the bunker's own starts out on the surface: up the stairs first, on foot.
        if (view is not ("bunker" or "climb" or "bunkershot" or "bunkerclose"))
            foreach (int x in _drive.ClimbOut(_game.Bunker)) yield return x;
        IEnumerable<int> script = view switch
        {
            "bunker" => BunkerView(),
            "skysweep" => SkySweep(),
            "climb" => Climb(),
            "dig" => Dig(),
            "build" => Build(half: true),
            "liftoff" => Liftoff(),
            "flying" => Flying(),
            "cockpit" => Cockpit(),
            "menu" => Menu(),
            "settings" => SettingsView(),
            "escmenu" => EscapeMenuShots(),
            "escnewgame" => EscapeNewGameShots(),
            "blueprints" => BlueprintShots(),
            "stock" => StockShots(),
            "uiround" => UiRoundShots(),
            "notices" => NoticesShots(),
            "markers" => MarkersShot(),
            "corescan" => CoreScanShot(),
            "fabicons" => FabIconShots(),
            "loot" => Loot(),
            "bolt" => Bolt(),
            "bunkershot" => BunkerShot(),
            "torch" => Torch(),
            "base" => BaseView(),
            "blockbase" => BaseView(survival: true),
            "constructor" => ConstructorBlocks(),
            "machinescreen" => MachineScreenView(),
            "hand" => HandView(),
            "storage" => StorageFilter(),
            "vessel" => VesselTakeoff(),
            "damage-crash" => DamageCrash(),
            "damage-gunfire" => DamageGunfire(),
            "damage-wall" => DamageWall(),
            "damage-repair" => DamageRepair(),
            "thruster-hull" => ThrusterHull(inside: false),
            "thruster-interior" => ThrusterHull(inside: true),
            "ruin" => RuinView(),
            "place" => PlaceApproach(),
            "interior" => PlaceInterior(),
            "furnished" => FurnishedView(),
            "tell" => PlaceTell(),
            "cache" => PlaceCache(),
            "town" => TownView(),
            "facades" => FacadeView(),
            "salvage" => Salvage(),
            "hoverbike" => HoverBikeView(),
            "vehground" => VehGroundView(),
            "bunkerclose" => BunkerClose(),
            "remotebar" => RemoteBarView(),
            "decogallery" => DecoGalleryView(),
            "faces" => FacesView(),
            "cores" => CoresView(),
            "lightpanel" => LightPanelView(),
            "natural" => Natural(),
            "flora" => FloraProof(),
            "flora-trees" => FloraTrees(),
            "florareach" => FloraReachView(),
            "shadows" => ShadowView(),
            "plantshadows" => PlantShadows(),
            "plantshadows-ab" => PlantShadowsAb(),
            "crouch" => CrouchView(),
            "seated" => SeatedView(),
            "sleeper" => SleeperView(),
            "sleeperface" => SleeperFaceView(),
            "folk" => FolkView(),
            "crowd" => CrowdView(),
            "peoples" => PeoplesView(),
            "outfits" => OutfitsView(),
            "townnight" => TownNightView(),
            "checkpoint" => CheckpointView(),
            "wheel" => WheelView(),
            "answer" => AnswerView(),
            "farfolk" => FarFolkView(),
            "fauna" => FaunaView(),
            "peopleperf" => PeoplePerf(),
            "dark" => Dark(),
            "monsoon" => MonsoonCost(),
            "funnel" => FunnelShot(),
            "cirruslog" => CirrusLog(),
            "storm" => StormShot(),
            "front" => FrontShot(),
            "lightning" => LightningShot(),
            "spout" => SpoutShot(),
            "regionfront" => RegionFront(),
            "plainstornado" => PlainsTornado(),
            "windbodies" => WindBodies(),
            "treepath" => TreePath(),
            "tornadowarning" => TornadoWarning(),
            "frontgrowth" => FrontGrowth(),
            "cloudgrain" => CloudGrain(),
            "nightclouds" => NightClouds(),
            "skylevel" => SkyLevel(),
            "sunset" => Sunset(),
            "underwater" => UnderwaterShot(),
            "rain" => RainShot(),
            "dust" => DustShot(),
            "puff" => PuffShot(),
            "stormcell" => StormCellShot(),
            "clouds" => CloudTypes(),
            "farland" => FarLandView(),
            "city" => CityView(),
            "strike" => StrikeView(),
            "craters" => CraterViews(),
            "horizon" => HorizonView(),
            "loadrun" => LoadRun(),
            "approach" => ApproachView(),
            "popin" => PopInView(),
            "husk-grab" => HuskGrab(),
            "newgame" => NewGameShots(),
            "galaxy-depth" => GalaxyDepth(),
            "husk-den" => HuskDen(),
            "own" => OwnView(),
            "own-inside" => OwnInside(),
            "husk-worms" => HuskWorms(),
            "husk-carrier" => HuskCarrier(),
            "layers-land" => LayersLand(),
            "layers-mark" => LayersMark(),
            "icons-bag" => IconBagView(),
            "icons-sheet" => IconSheetView(),
            "icons-hands" => IconHandsView(),
            "blockdetail" => BlockDetailView(),
            "placesurf" => PlaceSurfView(),
            "decorooms" => DecoRooms(),
            "dress" => DressView(),
            "hulk" => HulkView(),
            "visor" => VisorView(),
            "feet" => FeetView(),
            "zfight" => ZFightView(),
            "tiershots" => TierShots(),
            _ => DoorViews(view),
        };
        foreach (int x in script) yield return x;
        if (_args.TryGetValue("hud", out string hud)) _game.Visor.Visible = hud != "0";
    }

    /// <summary>The visor's glass at night, frosted and pulsing red at once (cold and hurt): the edge effects blended.</summary>
    IEnumerable<int> VisorView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.9);
        _game.Visor.PinnedGlass = ((float)Num("frost", 0.7), (float)Num("harm", 1.0));
        foreach (int x in _drive.Wait(60)) yield return x;
    }

    /// <summary>Inside the bunker: the sleeper out of the one working cryopod, the dead sleepers in theirs.</summary>
    IEnumerable<int> BunkerView()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.3);
        Slice.Start.Bunker b = _game.Bunker;
        foreach (int x in _drive.WalkTo(b.CryoFront + (b.RoomCentre - b.CryoFront) * 0.4f, 0.3f, 10f)) yield return x;
        Vector3 eye = b.At(new Vector3(-2.7f, 2.3f, -1.9f));
        _game.FixedCamera = new Transform3D(Basis.LookingAt(b.At(new Vector3(0.6f, 1.0f, 1.6f)) - eye, Vector3.Up), eye);
        foreach (int x in _drive.LookAt(b.Locker, 10)) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
    }

    /// <summary>Climbing the bunker's stairwell, the fallen-in top open to the morning sky.</summary>
    IEnumerable<int> Climb()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.31);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Slice.Start.Bunker b = _game.Bunker;
        var way = b.WayOut;
        int upTo = Math.Max(3, way.Count - 3);
        for (int i = 0; i < upTo; i++) foreach (int x in _drive.WalkTo(way[i], 0.45f, 30f)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.LookAt(b.Outside + Vector3.Up * 3.5f, 20)) yield return x;
    }

    /// <summary>First person, digging into an iron deposit: the visor counting the ore in.</summary>
    IEnumerable<int> Dig()
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.33);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        Vector3 ore = DepositFinder.Nearest(_game.Zone, DepositKind.Iron, _game.Survivor.WorldFeet, 600f) ?? _game.Survivor.WorldFeet;
        Vector3 toOre = ore - _game.Survivor.WorldFeet;
        toOre.Y = 0f;
        Vector3 stand = ore - toOre.Normalized() * 4.5f;
        foreach (int x in _drive.WalkTo(stand, 1.2f, 60f)) yield return x;
        foreach (int x in _drive.HoldPrimary(ore + Vector3.Up * 0.5f, 70)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.HoldPrimary(ore, 14)) yield return x;
        _game.Controls.Primary = true;
        foreach (int x in _drive.LookAt(ore, 4)) yield return x;
    }

    /// <summary>A blueprint set down and built part way, the rest of it a ghost.</summary>
    IEnumerable<int> Build(bool half)
    {
        _game.Zone.Sky.TimeOfDay = Num("time", 0.34);
        _game.Zone.Weather.Sim.Snap(WeatherKind.Clear);
        // Staging shortcut: the Skiff's parts in the bag and the tech open (the screenshots show building, not gathering).
        foreach (var (item, n) in _game.Builder.TotalCost(_game.StarterPlan)) _game.Pack.Add(item, n);
        _game.Progress.Creative = true;
        _game.Builder.Project();
        Vector3 pod = _game.Start;
        Vector3 pad = default;
        Vessel v = null;
        for (int attempt = 0; attempt < 3 && v == null; attempt++)
        {
            pad = FlatSpot(pod, 16f + attempt * 14f);
            Vector3 stand = pad + (pad - pod).Normalized() * 8f;
            foreach (int x in _drive.WalkTo(stand, 1.2f, 30f)) yield return x;
            foreach (int x in _drive.LookAt(pad, 6)) yield return x;
            // The ground's collision may still be coming in where you look: a click before the ghost shows sets nothing down.
            for (int i = 0; i < 300 && !_game.Builder.PlanShown; i++)
                foreach (int x in _drive.LookAt(pad, 1)) yield return x;
            foreach (int x in _drive.Click()) yield return x;
            v = _game.Builder.Underway;
            if (v == null) GD.Print($"(the blueprint set nothing down at {pad}, feet {_game.Survivor.WorldFeet}: {_game.Builder.Verdict})");
        }
        if (v == null) throw new InvalidOperationException("the blueprint would not go down anywhere near the start: see the lines above");
        int stop = half ? _game.StarterPlan.Count * 11 / 20 : int.MaxValue;
        foreach (int x in _drive.HoldPrimary(v.Body.Position.ToGodot(), 3000, () => _game.Builder.Underway == null || v.Structure.Count >= stop)) yield return x;
        foreach (int x in _drive.UntilSettled()) yield return x;
        // Step round to a three-quarter view of it.
        Vector3 side = v.Body.ToParent(new N.Vector3(4.5f, 0f, -4.5f)).ToGodot();
        foreach (int x in _drive.WalkTo(side, 0.8f, 20f)) yield return x;
        foreach (int x in _drive.LookAt(v.Body.Position.ToGodot() + Vector3.Up * 0.4f, 10)) yield return x;
    }

    Vessel BuiltSkiff()
    {
        foreach (Vessel v in _game.Zone.Vessels)
            if (!v.IsRuin && v.Structure.Count == _game.StarterPlan.Count) return v;
        return null;
    }

    /// <summary>The finished Skiff lifting off over the land, seen from the ground.</summary>
    IEnumerable<int> Liftoff()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        Vector3 ground = v.Body.Position.ToGodot();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(55)) yield return x;
        _game.Controls.ReleaseAll();
        foreach (int x in _drive.Wait(5)) yield return x;
        Vector3 at = v.Body.Position.ToGodot();
        float heading = (float)Num("heading", 215) * Mathf.Pi / 180f;
        Vector3 eye = ground + new Vector3(MathF.Sin(heading), 0f, MathF.Cos(heading)) * 17f;
        eye.Y = MathF.Max(_game.Zone.GroundAt(eye.X, eye.Z).Y + 2.5f, at.Y - 3f);
        _game.FixedCamera = new Transform3D(Basis.LookingAt(at + Vector3.Down * 1.5f - eye, Vector3.Up), eye);
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(8)) yield return x;
    }

    /// <summary>In flight over the planet, a chase view.</summary>
    IEnumerable<int> Flying()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Visor.Visible = false;
        _game.ThirdPerson = true;
        _game.Controls.Hold(Key.Space);
        foreach (int x in _drive.Wait(150)) yield return x;
        _game.Controls.Hold(Key.Space, false);
        float turn = (float)Num("turn", 1.2f);
        for (int i = 0; i < 360; i++)
        {
            _game.Controls.Hold(Key.W);
            _game.Controls.Hold(Key.Space, v.AboveGround < 70f);
            _game.Controls.Look = new Vector2(i < 60 ? turn : 0f, 0f);
            yield return 0;
        }
        foreach (int x in _drive.UntilSettled(20)) yield return x;
    }

    /// <summary>In the Skiff's seat, the console showing its four numbers.</summary>
    IEnumerable<int> Cockpit()
    {
        foreach (int x in Build(half: false)) yield return x;
        Vessel v = BuiltSkiff();
        foreach (int x in _drive.Board(v)) yield return x;
        _game.Survivor.Pitch = -0.32f;
        foreach (int x in _drive.Wait(40)) yield return x;
    }

    Vector3 FlatSpot(Vector3 near, float distance) => Play.FlatSpot(near, distance, span: 10f);

    double Num(string key, double fallback) => _args.TryGetValue(key, out string s) ? double.Parse(s, CultureInfo.InvariantCulture) : fallback;
}
