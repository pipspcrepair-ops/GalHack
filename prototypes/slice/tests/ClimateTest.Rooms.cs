using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Physics.Engine;
using GalHack.Structures;
using Godot;
using PhysicsSandbox.Bridges;
using Slice.Places;
using Slice.Player;
using Slice.Vessels;
using SurvivalVitals.Model;
using WeatherKind = Weather.Model.WeatherKind;
using N = System.Numerics;
using PInt3 = PoiGenerator.Gen.Int3;

namespace Slice.Tests;

/// <summary>
/// Rooms' air: a powered sealed room eases to comfortable, an unpowered one holds then drifts to the
/// outside's, an open door lets the outside in; a place's shut room takes the edge off a blizzard;
/// the bunker is as it was.
/// </summary>
public partial class ClimateTest
{
    /// <summary>STAGING: rooms' air at this many times play speed, so the minutes pass in seconds of the run.</summary>
    const double AirSpeedUp = 8;

    Pos Here => new(Me.Body.WorldPosition.X, Me.Body.WorldPosition.Y, Me.Body.WorldPosition.Z);
    double RoomC => _game.Suit.Last.Exposure.Temperature;
    double OutsideC => _game.Suit.World(Here, _game.Suit.Sim.Time).Temperature;

    /// <summary>The cryo bunker as it always was: sealed, its own air at once.</summary>
    IEnumerable<int> Bunker()
    {
        foreach (int x in _drive.Wait(2)) yield return x;
        var climate = Zone.Data.Rules.Climate;
        Exposure e = _game.Suit.Last.Exposure;
        Check(_game.Suit.Room == RoomKind.Bunker && _game.Suit.InSealedRoom && e.Temperature == climate.SealedRoomC && e.OxygenPercent == 21,
            $"the bunker is sealed and heated at once: {e.Temperature:F0} Â°C, {e.OxygenPercent:F0}% (the world's {OutsideC:F0} Â°C)");
    }

    /// <summary>STAGING: the castaway kept whole through the long cold waits (rooms' air is tested here, not the body's).</summary>
    void Keep()
    {
        _game.Suit.Vitals.Health = _game.Suit.Sim.Data.Vitals.Health.Max;
        _game.Suit.Vitals.Normalise();
        _game.Suit.Vitals.Oxygen = _game.Suit.Sim.OxygenCapacity;
    }

    /// <summary>STAGING: the world's climate pinned to one temperature (a cold or a hot world) without leaving this one.</summary>
    static (double, double) Pin(Game.SliceRules.ClimateRules climate, double c)
    {
        var was = (climate.ColdestC, climate.HottestC);
        climate.ColdestC = climate.HottestC = c;
        return was;
    }

    /// <summary>The shut steel room (its generator powering it): warming and cooling with the power, holding with it off, its door let open.</summary>
    IEnumerable<int> RoomAir(Vessel room)
    {
        var climate = Zone.Data.Rules.Climate;
        var band = Zone.Data.Rules.ClimateNode;
        Suit suit = _game.Suit;
        Structure s = room.Structure;
        // The door into the east wall at its foot (the building rules: a wall block out, the door in).
        var doorCell = new Int3(5, 1, 0);
        Building.Remove(s, doorCell, null);
        Orientations.TryParse("+y-x", out int rot);
        PlaceError placed = Building.Place(s, doorCell, Building.Make(s, s.Catalogue.Type("door"), null, rot));
        room.ApplyEdits();
        Me.PlaceAboard(room, new N.Vector3(-2f * s.Grid.CellSize, s.Grid.CellSize * 0.5f + 0.05f, 0f));
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(30)) yield return x;
        Check(placed == PlaceError.None && room.Powered && suit.InSealedRoom && suit.Room == RoomKind.Sealed,
            $"a door in its wall, shut, and its generator on: sealed and powered ({placed}, powered {room.Powered}, {suit.Room}; aboard {Me.Aboard?.Name ?? "nothing"}, sealed rooms {GalHack.Structures.Rooms.Detect(s).Sealed.Count()}, door open {BlockDoors.IsOpen(room, doorCell)})");
        if (suit.Air == null) yield break;
        suit.ClimateTimeScale = AirSpeedUp;
        (double, double) was = (climate.ColdestC, climate.HottestC);

        // A cold world and a hot one: the room starts as the outside and eases to comfortable.
        foreach (double world in new[] { -40.0, 60.0 })
        {
            Pin(climate, world);
            foreach (int x in _drive.Wait(2)) yield return x;
            double outside = OutsideC;
            suit.Air.Temperature = outside; // STAGING: as if it had stood a long while unpowered
            foreach (int x in _drive.Wait(2)) yield return x;
            double start = RoomC;
            double begun = _game.Suit.Sim.Time, at5 = double.NaN, took = double.NaN;
            for (int i = 0; i < 60 * 30 && double.IsNaN(took); i++)
            {
                double t = (_game.Suit.Sim.Time - begun) * AirSpeedUp;
                if (double.IsNaN(at5) && t >= 5) at5 = RoomC;
                if (RoomC >= band.LowC && RoomC <= band.HighC) took = t;
                yield return 0;
            }
            Check(!double.IsNaN(took) && took <= 90 && Math.Abs(at5 - start) < Math.Abs(climate.SealedRoomC - start) * 0.3,
                $"on a {(world < 0 ? "cold" : "hot")} world ({outside:F0} Â°C outside) a powered room eases from {start:F0} Â°C: {at5:F0} Â°C after 5 s, comfortable ({band.LowC:F0}-{band.HighC:F0}) after {took:F0} s");
        }

        // Power off on the cold world: it holds, then drifts toward the outside, its oxygen too.
        Pin(climate, -40);
        string air = Zone.Planet.Sky.Air.ToString();
        bool hadO2 = climate.OxygenByAir.TryGetValue(air, out double o2);
        climate.OxygenByAir[air] = 4; // STAGING: thin air outside, so the room's own can be seen to run down
        for (int i = 0; i < 600 && Math.Abs(RoomC - climate.SealedRoomC) > 0.5; i++) yield return 0;
        room.PowerOn = false;
        double off = _game.Suit.Sim.Time;
        double Since() => (_game.Suit.Sim.Time - off) * AirSpeedUp;
        while (Since() < climate.RoomHoldSeconds * 0.9) { Keep(); yield return 0; }
        double held = RoomC, heldO2 = suit.Last.Exposure.OxygenPercent;
        Check(suit.Room == RoomKind.Unpowered && Math.Abs(held - climate.SealedRoomC) < 1 && heldO2 > 20,
            $"power off, the room holds its air: {held:F1} Â°C and {heldO2:F0}% after {Since():F0} s ({suit.Room})");
        while (Since() < climate.RoomHoldSeconds + climate.RoomDriftSeconds * 2) { Keep(); yield return 0; }
        double outsideNow = OutsideC, drifted = RoomC, driftedO2 = suit.Last.Exposure.OxygenPercent;
        double share = (climate.SealedRoomC - drifted) / (climate.SealedRoomC - outsideNow);
        Check(share > 0.7 && share < 1 && driftedO2 < 8,
            $"then drifts toward the outside: {drifted:F1} Â°C ({outsideNow:F0} outside, {share:P0} of the way) and {driftedO2:F1}% oxygen after {Since():F0} s");
        if (hadO2) climate.OxygenByAir[air] = o2;
        else climate.OxygenByAir.Remove(air);

        // Power back on, then the door held open: the outside comes straight in.
        room.PowerOn = true;
        for (int i = 0; i < 60 * 20 && RoomC < band.LowC; i++) yield return 0;
        double warm = RoomC;
        Func<Vector3> feet = _game.Doors.Feet;
        Vector3 inDoor = room.ToWorld(StructureBridge.CellCenter(s, doorCell)) - Vector3.Up * 0.9f;
        _game.Doors.Feet = () => inDoor; // STAGING: held open as if someone stood in it
        _game.Doors.Use(room, doorCell);
        foreach (int x in _drive.Wait(60)) yield return x;
        double opened = RoomC, outsideOpen = OutsideC;
        Check(BlockDoors.IsOpen(room, doorCell) && !suit.InSealedRoom && Math.Abs(opened - outsideOpen) < 0.5 && suit.Room == RoomKind.None,
            $"its door open, the room leaks and the outside comes in: {warm:F0} Â°C to {opened:F0} Â°C ({outsideOpen:F0} outside)");
        _game.Doors.Feet = feet;
        // Out of the doorway, nobody shuts it: it shuts itself on the timer, and the room is sealed again.
        double clearAt = _game.Suit.Sim.Time;
        for (int i = 0; i < 60 * 12 && BlockDoors.IsOpen(room, doorCell); i++) yield return 0;
        double shutAfter = _game.Suit.Sim.Time - clearAt;
        foreach (int x in _drive.Wait(10)) yield return x;
        Check(!BlockDoors.IsOpen(room, doorCell) && shutAfter >= DoorWays.AutoClose - 0.5 && suit.InSealedRoom && RoomC < band.LowC,
            $"it shuts itself {shutAfter:F1} s after its doorway is clear (timer {DoorWays.AutoClose:F0} s), sealed again, from the cold that came in: {RoomC:F0} °C");
        for (int i = 0; i < 60 * 30 && RoomC < band.LowC; i++) yield return 0;
        Check(suit.InSealedRoom && RoomC >= band.LowC, $"and its air recovers: {RoomC:F0} °C ({band.LowC:F0}-{band.HighC:F0} comfortable)");

        // Space outside, by the same rule (STAGING: the orbit's own outside set on the suit, as Spaceflight sets it on the way up).
        suit.Beyond = _game.Spaceflight.Space;
        double vacuumC = _game.Spaceflight.Space(Here, suit.Sim.Time).Temperature;
        for (int i = 0; i < 60 * 30 && Math.Abs(RoomC - climate.SealedRoomC) > 0.5; i++) { Keep(); yield return 0; }
        double spaceHeld = RoomC, spaceO2 = suit.Last.Exposure.OxygenPercent;
        Check(suit.Room == RoomKind.Sealed && Math.Abs(spaceHeld - climate.SealedRoomC) <= 0.5 && spaceO2 > 20.5,
            $"in orbit a powered sealed room holds {spaceHeld:F1} °C and {spaceO2:F0}% ({vacuumC:F0} °C and no air outside)");
        room.PowerOn = false;
        off = suit.Sim.Time;
        while (Since() < climate.RoomHoldSeconds + climate.RoomDriftSeconds * 2) { Keep(); yield return 0; }
        double spaceDrift = RoomC, spaceDriftO2 = suit.Last.Exposure.OxygenPercent;
        double spaceShare = (climate.SealedRoomC - spaceDrift) / (climate.SealedRoomC - vacuumC);
        Check(suit.Room == RoomKind.Unpowered && spaceShare > 0.7 && spaceDriftO2 < 4 && suit.Last.Exposure.Volume == null,
            $"power off in orbit, it drifts toward space: {spaceDrift:F0} °C ({spaceShare:P0} of the way to {vacuumC:F0}) and {spaceDriftO2:F1}% oxygen after {Since():F0} s");
        suit.Beyond = null;
        room.PowerOn = true;

        (climate.ColdestC, climate.HottestC) = was;
        suit.ClimateTimeScale = 1;
    }

    /// <summary>A shut room in a place in a blizzard: warmer than out there, not comfortable; its door open, it is out there.</summary>
    IEnumerable<int> PlaceRoom()
    {
        var climate = Zone.Data.Rules.Climate;
        var band = Zone.Data.Rules.ClimateNode;
        var planet = Zone.Planet;
        Vector3 from = Me.WorldFeet;
        Place place = null;
        Place.Door door = null;
        PInt3 stand = default;
        foreach (var site in planet.Pois.OrderBy(q => planet.NearestCopy(new Vector3(q.Origin.X, from.Y, q.Origin.Z), from).DistanceTo(from)).Take(6))
        {
            Vector3 origin = planet.NearestCopy(new Vector3(site.Origin.X, from.Y, site.Origin.Z), from);
            // STAGING: set down by it, for it to stand up.
            Me.PlaceOutside((Open(origin + new Vector3(70f, 0f, 0f)) + Vector3.Up * 0.3f).ToNumerics());
            Place p = null;
            for (int i = 0; i < 60 * 40 && p is not { Ready: true }; i++)
            {
                p = _game.Places.PlaceOf(site);
                yield return 0;
            }
            if (p is not { Ready: true }) continue;
            foreach (Place.Door d in p.Doors)
            {
                if (d.Jammed || d.Lift >= 0 || d.Cells == null || d.Cells.Count == 0) continue;
                var (a, b) = p.DoorSides(d);
                foreach (PInt3 side in new[] { a, b })
                {
                    if (!p.Inst.Blocks.Get(side).IsAir || p.Inst.Blocks.Get(side + PInt3.Down).IsAir) continue;
                    float open = d.Open;
                    d.Open = 0f;
                    bool shut = p.ClosedRoom(side + PInt3.Up);
                    d.Open = 1f;
                    bool leaks = !p.ClosedRoom(side + PInt3.Up);
                    d.Open = open;
                    if (!shut || !leaks) continue;
                    (place, door, stand) = (p, d, side);
                    break;
                }
                if (place != null) break;
            }
            if (place != null) break;
        }
        if (place == null)
        {
            Check(false, "a place near with a shut room behind a door (6 nearest tried)");
            yield break;
        }
        // Shut, the castaway inside, a blizzard on a cold world outside.
        door.Target = 0f;
        for (int i = 0; i < 240 && !door.IsShut; i++) yield return 0;
        Me.PlaceOutside(place.WorldOf(stand, 0.05f).ToNumerics());
        Weather.Model.WeatherSim sky = Zone.Weather.Sim;
        var land = sky.Land;
        sky.Land = null;
        sky.Snap(WeatherKind.Blizzard);
        var was = Pin(climate, -30);
        _game.Suit.ClimateTimeScale = AirSpeedUp;
        foreach (int x in _drive.UntilSettled()) yield return x;
        foreach (int x in _drive.Wait(60)) yield return x;
        double inside = RoomC, outside = OutsideC;
        Check(_game.Suit.Room == RoomKind.Closed && inside > outside + 3 && inside < band.LowC,
            $"in {place.Site.Name}'s shut room in a blizzard: {inside:F0} Â°C, between the {outside:F0} Â°C out there and comfortable ({band.LowC:F0}) ({_game.Suit.Room})");
        door.Target = 1f;
        for (int i = 0; i < 240 && door.IsShut; i++) yield return 0;
        foreach (int x in _drive.Wait(45)) yield return x;
        double opened = RoomC;
        Check(_game.Suit.Room == RoomKind.None && Math.Abs(opened - OutsideC) < 0.5,
            $"its door open, it is the blizzard's: {opened:F0} Â°C ({OutsideC:F0} outside)");
        door.Target = 0f;
        (climate.ColdestC, climate.HottestC) = was;
        _game.Suit.ClimateTimeScale = 1;
        sky.Snap(WeatherKind.Clear);
        sky.Land = land;
        foreach (int x in OnOpenGround(Me.WorldFeet + new Vector3(80f, 0f, 0f))) yield return x;
    }
}
