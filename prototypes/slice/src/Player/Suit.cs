using System;
using System.Collections.Generic;
using GalHack.Physics;
using GalHack.Structures;
using GalHack.Terrain;
using Godot;
using PhysicsSandbox.Bridges;
using Planets.Gen;
using PlanetSurface.World;
using Slice.Game;
using Slice.Surface;
using Slice.Vessels;
using SurvivalVitals.Model;
using N = System.Numerics;

namespace Slice.Player;

/// <summary>
/// The survival suit: survival-vitals' model (food, oxygen, body temperature, radiation,
/// stamina, health, the replicator and the kit) worn on the generated planet. The planet, its
/// sky and its weather supply what the suit feels (<see cref="Exposure"/>); the survivor's
/// movement supplies how hard it is working. Every hit the physics module deals arrives here in
/// full; a fall can only hurt once the visor has flashed IMPACT.
/// </summary>
public sealed class Suit : IDamageSink
{
    public readonly SurvivalSim Sim;
    readonly SurfaceZone _zone;
    readonly SliceRules.ClimateRules _climate;

    public Vitals Vitals => Sim.Player.Vitals;
    public SurvivalVitals.Model.Inventory Kit => Sim.Player.Inventory;
    public TickReport Last => Sim.Last;
    public double HealthShare => Vitals.Health / Sim.Data.Vitals.Health.Max;
    public double FoodShare => Vitals.Food / Sim.Data.Vitals.Food.Max;
    public double OxygenShare => Sim.OxygenCapacity > 0 ? Vitals.Oxygen / Sim.OxygenCapacity : 0;
    public bool Dead => Sim.Player.Dead;

    /// <summary>
    /// In a sealed room aboard a structure (or the bunker): its own air, the room's (<see cref="RoomAir"/>).
    /// A command chair gives no air of its own: in the open, or in a leaking hull, the suit is all you have.
    /// </summary>
    public bool InSealedRoom;

    /// <summary>Somewhere that counts as a sealed room without being a structure's: the cryo bunker, heated and with air of its own.</summary>
    public Func<bool> Sheltered;

    /// <summary>The place whose closed room a spot (feet) is in, or null: open doors and holes let the outside in.</summary>
    public Func<Vector3, object> ClosedPlace;

    /// <summary>The place a spot (feet) is in at all, closed room or not.</summary>
    public Func<Vector3, object> InPlace;

    /// <summary>Where the survivor is breathing, for the visor's temperature readout.</summary>
    public RoomKind Room { get; private set; }

    /// <summary>The air of the room the survivor is in (a vessel's or base's sealed room, a place's shut one), or null.</summary>
    public RoomAir Air { get; private set; }

    /// <summary>What is outside instead of the planet (space, in orbit), or null on the surface.</summary>
    public Func<Pos, double, Exposure> Beyond;

    /// <summary>STAGING: tests' marked speed-up of rooms' air (1 in play).</summary>
    public double ClimateTimeScale = 1;

    readonly System.Runtime.CompilerServices.ConditionalWeakTable<object, RoomAir> _airOf = new();
    readonly System.Runtime.CompilerServices.ConditionalWeakTable<Vessel, Dictionary<Int3, RoomAir>> _roomsAir = new();
    double _airClock;

    /// <summary>Out of a storm's reach (the game's cover check, physics step only).</summary>
    public Func<bool> UnderCover;

    /// <summary>Lines for the visor (helmet sealing, bottles, hard landings, deaths).</summary>
    public event Action<string> Said;

    /// <summary>When each alert last came on, in survival time (tests check harm never comes first).</summary>
    public readonly Dictionary<Alert, double> WarnedAt = new();
    Alert _warning;

    /// <summary>Health lost, by cause, since the start (tests).</summary>
    public readonly Dictionary<DamageKind, double> Lost = new();

    float _miningLeft;

    public Suit(SurfaceZone zone, SurvivalData data, int seed)
    {
        _zone = zone;
        _climate = zone.Data.Rules.Climate;
        Sim = new SurvivalSim(data, seed) { Environment = Exposure };
    }

    // ---- What the suit feels -----------------------------------------------------------------

    /// <summary>
    /// The planet at a place and time, as survival's model wants it: the generator's temperature
    /// field spread over the climate's range, the weather's own swing on top, warmer by day and
    /// colder with height; the sky's air for oxygen; the world's background radiation.
    /// </summary>
    public Exposure Exposure(Pos p, double time)
    {
        if (InSealedRoom && Room == RoomKind.Bunker) return new Exposure(_climate.SealedRoomC, 21, 0, VolumeKind.Room, "Sealed room", double.PositiveInfinity);
        if (InSealedRoom && Air != null)
        {
            // Its air while it lasts: a dead room on a world with none to breathe runs down to the world's.
            VolumeKind? held = Air.Oxygen >= Sim.Data.Vitals.Oxygen.BreathablePercent ? VolumeKind.Room : null;
            return new Exposure(Air.Temperature, Air.Oxygen, 0, held, Room == RoomKind.Sealed ? "Sealed room" : "Unpowered room", double.PositiveInfinity);
        }
        Exposure outside = Outside(p, time, out double raw);
        if (Room != RoomKind.Closed || Air == null) return outside;
        // A shut room in a place: its walls' share of the way to comfortable, and a climate node in it still holds its band.
        double t = Air.Temperature;
        if (outside.Temperature != raw) t = Math.Clamp(t, _zone.Data.Rules.ClimateNode.LowC, _zone.Data.Rules.ClimateNode.HighC);
        return outside with { Temperature = t, Place = "Indoors" };
    }

    /// <summary>The world's own air at a spot, whatever room the survivor is in (a burning climate node near it holding its band).</summary>
    public Exposure World(Pos p, double time) => Outside(p, time, out _);

    /// <summary>The world's own air at a spot (a burning climate node near it holding its band); raw is the temperature before the node.</summary>
    Exposure Outside(Pos p, double time, out double raw)
    {
        raw = _climate.SealedRoomC;
        if (Beyond != null)
        {
            Exposure beyond = Beyond(p, time);
            raw = beyond.Temperature;
            return beyond;
        }
        PlanetWorld planet = _zone.Planet;
        if (planet == null) return SurvivalVitals.Model.Exposure.Mild;
        double x = Mathx.WrapX(p.X, planet.Width), z = Mathx.Wrap(p.Z, planet.Height);
        float field = planet.Map.Sample(planet.Map.Temperature, x, z);
        double baseline = _climate.ColdestC + (_climate.HottestC - _climate.ColdestC) * field;
        // The weather model's temperature is its archetype's base plus the weather's own change;
        // only the change is added here (the base is already in the field).
        double weather = _zone.Weather.Sim == null ? 0 : _zone.Weather.Sim.Current.Temperature - _zone.Weather.Sim.Archetype.BaseTemperature;
        double day = _climate.DayNightSwing * Math.Clamp(_zone.Sky.ToSun.Y * 2f, -1f, 1f);
        double height = -_climate.LapsePerKm * Math.Max(0, p.Y) / 1000.0;
        string air = planet.Sky.Air.ToString();
        double oxygen = _climate.OxygenByAir.TryGetValue(air, out double o2) ? o2 : 21;
        if (p.Y > _zone.Physics.Zone.Ceiling) oxygen = 0;
        // Head under water: nothing to breathe, the suit on its own oxygen (or held breath).
        if (_zone.Water.Under(new Godot.Vector3((float)p.X, (float)p.Y + Survivor.EyeHeight, (float)p.Z), out _)) oxygen = 0;
        // The world's background, and what a nuclear crater and its ring left in the ground here.
        double radiation = (_climate.RadiationByArchetype.TryGetValue(planet.Spec.Archetype.ToString(), out double rad) ? rad : 0) + planet.RadiationAt(x, z);
        double temperature = baseline + weather + day + height;
        raw = temperature;
        var rules = _zone.Data.Rules.ClimateNode;
        var here = new Godot.Vector3((float)p.X, (float)p.Y, (float)p.Z);
        foreach (Device d in _zone.Devices)
            if (d.Burning && d.GlobalPosition.DistanceSquaredTo(here) <= rules.Radius * rules.Radius)
            {
                temperature = Math.Clamp(temperature, rules.LowC, rules.HighC);
                break;
            }
        return new Exposure(temperature, oxygen, radiation, null, planet.Palette.BiomeAt(x, z).ToString(), LavaMetres(p));
    }

    static readonly float[] LavaRings = { 1f, 2f, 3f, 5f, 8f, 12f };
    bool _riding;

    /// <summary>
    /// How far the nearest lava is, metres: 0 standing in it, the ring it first shows on, or nowhere
    /// near. Someone in a seat or on a bike is carried over it, and only what lies around their
    /// own height counts: not the lava at the foot of a cliff you stand on.
    /// </summary>
    double LavaMetres(Pos p)
    {
        PlanetWater liquid = _zone.Water;
        if (liquid == null || !liquid.IsLava || _riding) return double.PositiveInfinity;
        float x = (float)p.X, y = (float)p.Y, z = (float)p.Z;
        if (liquid.LiquidAt(x, z, out float surface) && y <= surface + 0.6f) return 0;
        foreach (float r in LavaRings)
            for (int i = 0; i < 12; i++)
            {
                float a = i * MathF.Tau / 12f;
                if (liquid.LiquidAt(x + MathF.Cos(a) * r, z + MathF.Sin(a) * r, out surface) && surface > y - 6f) return r;
            }
        return double.PositiveInfinity;
    }

    Structure _roomsOf;
    int _roomsVersion = -1;
    readonly Dictionary<Int3, Room> _roomAt = new();

    /// <summary>The room (sealed or leaking) the survivor's chest is in, of the structure they sit in or stand aboard, this tick.</summary>
    Room _roomHere;

    /// <summary>Is the survivor in a sealed room of the structure they sit in or stand aboard? (Building's room check, cached per structure version.)</summary>
    bool InRoom(Survivor survivor)
    {
        _roomHere = null;
        Vessel v = survivor.Seated ?? survivor.Aboard;
        if (v == null) return false;
        Structure s = v.Structure;
        if (s != _roomsOf || s.Version != _roomsVersion)
        {
            _roomsOf = s;
            _roomsVersion = s.Version;
            _roomAt.Clear();
            foreach (Room room in Rooms.Detect(s).Rooms)
                foreach (Int3 cell in room.Cells) _roomAt[cell] = room;
        }
        if (_roomAt.Count == 0) return false;
        N.Vector3 chest = v.Body.ToLocal(survivor.Body.WorldPosition + survivor.Body.Up * 1f);
        if (survivor.Seated != null && v.Seat is N.Vector3 seat) chest = seat;
        _roomHere = _roomAt.GetValueOrDefault(StructureBridge.CellAt(s, chest));
        return _roomHere is { Sealed: true };
    }

    /// <summary>A room's name among its structure's: its least cell (the same while the room stands as it is).</summary>
    static Int3 KeyOf(Room room)
    {
        Int3 k = room.Cells[0];
        foreach (Int3 c in room.Cells)
            if (c.Y < k.Y || c.Y == k.Y && (c.Z < k.Z || c.Z == k.Z && c.X < k.X)) k = c;
        return k;
    }

    // ---- A room's air ---------------------------------------------------------------------------

    /// <summary>
    /// The room's air for this tick. The bunker is as it always was. A vessel's or base's sealed room
    /// eases to comfortable while the structure is powered, holds a while once it is not, then drifts
    /// to the outside's temperature and oxygen. A place's shut room goes part of the way from the
    /// outside's temperature to comfortable. A room left open takes the outside's air at once.
    /// </summary>
    void Breathe(float dt, Survivor survivor)
    {
        _airClock += dt * ClimateTimeScale;
        Vessel v = survivor.Seated ?? survivor.Aboard;
        if (Beyond == null && (Sheltered?.Invoke() ?? false))
        {
            (Room, Air) = (RoomKind.Bunker, null);
            return;
        }
        N.Vector3 at = survivor.Body.WorldPosition;
        var here = new Pos(at.X, at.Y, at.Z);
        if (v != null)
        {
            Exposure world = Outside(here, Sim.Time, out double raw);
            bool powered = v.Powered;
            Dictionary<Int3, RoomAir> rooms = _roomsAir.GetValue(v, _ => new Dictionary<Int3, RoomAir>());
            // A door open or the hull holed: a room that is no longer sealed takes the outside's air.
            foreach (var (key, was) in rooms)
                if (!_roomAt.TryGetValue(key, out Room r) || !r.Sealed) was.Let(raw, world.OxygenPercent, _airClock);
            if (!InSealedRoom)
            {
                (Room, Air) = (RoomKind.None, null);
                return;
            }
            Int3 name = KeyOf(_roomHere);
            if (!rooms.TryGetValue(name, out RoomAir air))
                rooms[name] = air = new RoomAir(powered ? _climate.SealedRoomC : raw, powered ? 21 : world.OxygenPercent, _airClock);
            air.Advance(_airClock, powered, raw, world.OxygenPercent, _climate);
            (Room, Air) = (powered ? RoomKind.Sealed : RoomKind.Unpowered, air);
            return;
        }
        Godot.Vector3 feet = survivor.WorldFeet;
        if (Beyond != null)
        {
            (Room, Air) = (RoomKind.None, null);
            return;
        }
        if (ClosedPlace?.Invoke(feet) is { } place)
        {
            Outside(here, Sim.Time, out double raw);
            double target = raw + (_climate.SealedRoomC - raw) * _climate.PlaceShelter;
            RoomAir air = _airOf.GetValue(place, _ => new RoomAir(target, 0, _airClock));
            air.Toward(_airClock, target, _climate.PlaceEaseSeconds);
            (Room, Air) = (RoomKind.Closed, air);
            return;
        }
        if (InPlace?.Invoke(feet) is { } opened && _airOf.TryGetValue(opened, out RoomAir left))
        {
            Outside(here, Sim.Time, out double raw);
            left.Let(raw, 0, _airClock); // its door open or a wall gone: the room is the weather's
        }
        (Room, Air) = (RoomKind.None, null);
    }

    // ---- Each tick ------------------------------------------------------------------------------

    /// <summary>Advances the suit by a tick of the survivor's work: walking, running, digging, falling.</summary>
    public TickReport Tick(float dt, Survivor survivor)
    {
        N.Vector3 at = survivor.Body.WorldPosition;
        Sim.Player.Position = new Pos(at.X, at.Y, at.Z);
        InSealedRoom = InRoom(survivor) || (Beyond == null && (Sheltered?.Invoke() ?? false));
        Breathe(dt, survivor);
        _riding = survivor.Seated != null || survivor.OnBike != null;
        _miningLeft -= dt;
        Activity activity = new(PaceOf(survivor), Mining: _miningLeft > 0f, FallSpeed: survivor.ImpactSpeed);
        double before = Vitals.Health;
        TickReport report = Sim.Tick(dt, activity);
        if (Vitals.Health < before) Count(DamageKindFor(report.Harm), before - Vitals.Health);
        Outdoors(dt, survivor, report);
        foreach (Alert a in Enum.GetValues<Alert>())
            if (a != Alert.None && (report.Warnings & a) != 0 && (_warning & a) == 0) WarnedAt[a] = Sim.Time;
        _warning = report.Warnings;
        // The model's own lines (helmet sealed, oxygen low, a hard landing) stay off the visor: the vitals show them.
        return report;
    }

    /// <summary>
    /// What the suit can't keep out, on top of survival's slow vitals, all warned on the visor as it
    /// comes on. A blizzard or a fire storm, for anyone left out in it.
    /// </summary>
    void Outdoors(float dt, Survivor survivor, TickReport report)
    {
        if (Dead || _zone.Weather.Sim is not { } weather) return;
        IceWater(dt, survivor, report);
        float exposure = weather.Here.Exposure; // the region's: a blizzard that falls as rain down here does not freeze you
        if (exposure < _climate.StormWarnFrom || InSealedRoom) return;
        if (_zone.Water?.Under(survivor.WorldEye(), out _) == true || (UnderCover?.Invoke() ?? false)) return;
        bool heat = weather.Data.State(weather.Dominant).ExposureLabel == "heat";
        Alert alert = heat ? Alert.Heat : Alert.Cold;
        report.Warnings |= alert;
        if (exposure < _climate.StormHarmFrom) return;
        report.Harm |= alert;
        report.Damage += Hit(_climate.StormHealthPerSecond * exposure * dt, heat ? DamageKind.Heat : DamageKind.Cold);
    }

    /// <summary>Ice water: the cold goes straight through the suit. The visor warns on going in, the health follows.</summary>
    void IceWater(float dt, Survivor survivor, TickReport report)
    {
        if (_riding || survivor.Body.Water.Fraction < 0.35f || report.Exposure.Temperature > _climate.IceWaterC)
        {
            _inIce = 0f;
            return;
        }
        _inIce += dt;
        report.Warnings |= Alert.Cold;
        if (_inIce < _climate.IceWaterWarnSeconds) return;
        report.Harm |= Alert.Cold;
        report.Damage += Hit(_climate.IceWaterHealthPerSecond * dt, DamageKind.Cold);
    }

    float _inIce;

    /// <summary>Working the tool: food drains faster for a moment (survival's mining rule).</summary>
    public void Worked() => _miningLeft = 0.6f;

    static Pace PaceOf(Survivor s)
    {
        if (s.Seated != null) return Pace.Idle;
        N.Vector3 v = s.Body.Velocity - s.Body.Up * N.Vector3.Dot(s.Body.Velocity, s.Body.Up);
        float speed = v.Length();
        if (speed < 0.5f) return Pace.Idle;
        if (s.Legs.Sprint && speed > s.Legs.WalkSpeed + 0.5f) return Pace.Sprint;
        return Pace.Walk;
    }

    static DamageKind DamageKindFor(Alert harm) =>
        (harm & Alert.Breath) != 0 ? DamageKind.Suffocation
        : (harm & Alert.Cold) != 0 ? DamageKind.Cold
        : (harm & Alert.Heat) != 0 ? DamageKind.Heat
        : (harm & Alert.Radiation) != 0 ? DamageKind.Radiation
        : (harm & Alert.Food) != 0 ? DamageKind.Starvation
        : (harm & Alert.Lava) != 0 ? DamageKind.Lava
        : DamageKind.Hit;

    void Count(DamageKind kind, double amount) => Lost[kind] = (Lost.TryGetValue(kind, out double had) ? had : 0) + amount;

    // ---- Damage --------------------------------------------------------------------------------

    /// <summary>
    /// The physics module's damage (the one formula, uncapped), as a share of the body's
    /// toughness, taken from health in full. Landing from a fall the visor flashed for is a fall
    /// (suit armor and boots soften it, survival's own rule); anything else is a hit.
    /// </summary>
    public float Take(Body body, float raw)
    {
        if (raw <= 0f || Dead) return 0f;
        bool fall = (Last.Warnings & Alert.Fall) != 0; // the visor was flashing IMPACT
        double health = raw / body.Integrity * Sim.Data.Vitals.Health.Max;
        double lost = Hit(health, fall ? DamageKind.Fall : DamageKind.Hit);
        return (float)(lost / Sim.Data.Vitals.Health.Max * body.Integrity);
    }

    /// <summary>Health lost to a hit of some kind (a creature's bite, a strike), armor applied.</summary>
    public double Hit(double amount, DamageKind kind)
    {
        if (Dead) return 0;
        double lost = Vitals.Damage(amount, kind, Sim.Mods);
        Count(kind, lost);
        if (Vitals.Dead)
        {
            int events = Sim.Events.Count;
            Sim.Tick(0, default); // lets the model notice: the backpack and the death message
            for (int i = events; i < Sim.Events.Count; i++) Said?.Invoke(Sim.Events[i]);
        }
        return lost;
    }

    public double TotalLost
    {
        get
        {
            double t = 0;
            foreach (double v in Lost.Values) t += v;
            return t;
        }
    }
}
