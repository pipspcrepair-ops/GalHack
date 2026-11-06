using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;
using GalHack.Physics;
using Godot;
using PoiGenerator.Gen;
using Slice.Places;
using Slice.Ui;
using Slice.Vessels;
using Layers = GalHack.Physics.Layers;
using Survivor = Slice.Player.Survivor;

namespace Slice.Game;

/// <summary>
/// The planet's places as part of the game: they stand up as real structures as you come near
/// (<see cref="PoiPlaces"/>), E opens their doors, takes their keycards and opens their caches,
/// the survival tool salvages them and breaks their hidden walls, ladders and lift shafts carry
/// you, their creatures and caves live and hunt, and their towns go about their day.
/// </summary>
public partial class Game
{
    public PoiPlaces Places { get; private set; }
    public Caves Caves { get; private set; }

    /// <summary>Door blocks on bases and vessels: the same doors, E and sliding leaves, opening for you on your own.</summary>
    public BlockDoors Doors { get; private set; }

    /// <summary>The keycards, lowest clearance first: a card opens its own doors and every lower one's.</summary>
    public static readonly string[] Clearances = { "staff", "science", "security", "command" };

    readonly HashSet<Town> _towns = new();

    void SetUpPlaces()
    {
        PlaceRules rules = PlaceRules.Shared;
        Places = new PoiPlaces
        {
            Name = "Places", Zone = Zone, Focus = Focus, Holds = HoldsClearance,
            BuildRadius = rules.BuildRadius, DropRadius = rules.DropRadius, BudgetMs = rules.BudgetMs,
            Fauna = new PlaceFauna.Deep(Zone.Data.Wildlife, Zone.Wildlife.Fauna, Zone.Planet),
        };
        Zone.AddChild(Places);
        Zone.Infra.WreckStood += Places.WreckStood;
        Zone.AddChild(new RuinLoot { Name = "Ruin loot", Zone = Zone, Focus = Focus });
        Caves = new Caves
        {
            Name = "Caves", Zone = Zone, Feet = () => Survivor.WorldFeet, Deep = Places.Fauna,
            OnFoot = () => Survivor.Seated == null && Survivor.Body.Frame == null && !Suit.Dead,
            Indoors = () => Places.PlaceAt(Survivor.WorldFeet) != null || Bunker.Inside(Survivor.WorldFeet, Zone),
        };
        Zone.AddChild(Caves);
        Doors = new BlockDoors { Name = "BlockDoors", Zone = Zone, Feet = () => Survivor.WorldFeet };
        Zone.AddChild(Doors);
        Tool.Places = Places;
        Tool.Salvaged += () => Progress.Earn(2, "salvage");
        Places.WallBroken += (p, s) => Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, p.WorldOf(s.WallCells[0], 1f), 0.5f, -2f);
        Places.Stood += OnPlaceStood;
        Places.CoreDestroyed += p => Say($"{p.Site.Name.ToUpperInvariant()}: ITS CORE IS DESTROYED", Tones.Good);
        Places.WarmUp(); // still loading: the first build's one-off costs are paid now
    }

    void OnPlaceStood(Place p)
    {
        if (p.Memory.Town is not Town t || !_towns.Add(t)) return;
        t.Aimed += from =>
        {
            Visor.Threat(from);
            Say("A GUARD IS TAKING AIM", Tones.Warning);
            Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, from, 0.5f, -4f);
        };
        t.Fired += (from, to, clear) =>
        {
            Shots.Fire(from, to, default, null);
            if (!clear || Suit.Dead) return;
            double lost = Suit.Hit(12, SurvivalVitals.Model.DamageKind.Hit);
            Visor.Hurt(from);
            Say($"Shot by a guard: -{lost:F0} health", Tones.Warning);
        };
        t.TurnedHostile += () => Say($"{p.Site.Name.ToUpperInvariant()} TURNS ON YOU: its people run for cover", Tones.Warning);
    }

    /// <summary>Whether the bag holds a keycard of this clearance or a higher one.</summary>
    bool HoldsClearance(string clearance)
    {
        int need = Array.IndexOf(Clearances, clearance);
        for (int i = Math.Max(0, need); i < Clearances.Length; i++)
            if (Pack.Storage.Count($"keycard-{Clearances[i]}") > 0) return true;
        return false;
    }

    /// <summary>
    /// Out of reach of whatever walks the surface: down in the ground (a cave, a hole, a basement)
    /// or under a place's roof. The wildlife patch reads the planet's surface, not rooms.
    /// </summary>
    public bool Sheltered()
    {
        Vector3 feet = Survivor.WorldFeet;
        if (feet.Y < Zone.GroundAt(feet.X, feet.Z).Y - 2.5f) return true;
        return Places?.PlaceAt(feet) is Place p && p.Roofed(feet);
    }

    // ---- Climbing ---------------------------------------------------------------------------------

    /// <summary>What holds the survivor up this tick: a ladder (W climbs), a lift shaft (Space and Ctrl ride it, easing level with a stop when let go).</summary>
    void Climb(Controls c)
    {
        Survivor s = Survivor;
        if (Places == null || s.Body.Frame != null) { s.Legs.Climb = ClimbMode.None; return; }
        ClimbMode mode = Places.ClimbAt(s.WorldFeet, out Place place, out int lift);
        s.Legs.Climb = mode;
        foreach (Place p in Places.Standing) p.Ride(p == place && mode == ClimbMode.Shaft ? lift : -1, place == p ? p.FromWorld(s.WorldFeet).Y : 0f);
        if (mode != ClimbMode.Shaft || s.Legs.Vertical != 0f) return;
        float y = place.FromWorld(s.WorldFeet).Y, stop = place.NearestStop(lift, y);
        s.Legs.Vertical = Mathf.Clamp((stop + 0.05f - y) * 1.5f, -0.5f, 0.5f);
    }

    // ---- E ------------------------------------------------------------------------------------------

    /// <summary>E on a keycard lying in reach: it goes in the bag. On a built door: it opens or shuts.</summary>
    bool UseKeycard(Vector3 eye, Vector3 dir)
    {
        if (Places == null) return false;
        if (Doors.Along(eye, dir, UseReach) is var (vessel, cell, _) && Doors.Use(vessel, cell)) return true;
        foreach (Place p in Places.Standing)
        {
            KeyCard k = p.KeyAlong(eye, dir, UseReach);
            if (k == null) continue;
            Bill left = Pack.Add($"keycard-{k.Clearance}", 1);
            if (!left.IsEmpty) { Say("No room in the bag", Tones.Caution); return true; }
            p.TakeKey(k);
            Say($"Taken: a {k.Clearance.ToUpperInvariant()} keycard. Doors marked {k.Clearance} open for you now", Tones.Good);
            Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, eye + dir, 1.8f, -12f);
            return true;
        }
        return false;
    }

    /// <summary>E on a place: a door opens or shuts (or says what it needs), a shaft door calls its car.</summary>
    bool UsePlace(LookHit look)
    {
        if (Places == null || !Places.Resolve(look.Collider, look.Point, look.Normal, out PoiPlaces.Hit h)) return false;
        if (h.Door != null)
        {
            var (ok, message) = h.Place.UseDoor(h.Door);
            if (message != "") Say(message, ok ? Tones.Good : Tones.Caution);
            h.Place.Memory.Entered = true;
            return true;
        }
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        if (CacheAt(h) is Slice.Items.Lootable loot)
        {
            Open(new OpenContainer { Title = loot.Name, Grid = loot.Grid, Loot = loot, Where = () => loot.Position });
            h.Place.Memory.Entered = true;
            return true;
        }
        if (b.Type == BlockType.Core)
        {
            Say("A faction's core: shoot it out to free the place (hijacking waits for the factions)", Tones.Caution);
            return true;
        }
        if (b.Type == BlockType.Terminal)
        {
            Say(h.Place.Inst.State == PoiState.Active ? "The terminal hums; its screen shows the doors on this level" : "The terminal is dead", Tones.Text);
            return true;
        }
        return false;
    }

    /// <summary>The cache a hit is on, if it was one.</summary>
    static Slice.Items.Lootable CacheAt(PoiPlaces.Hit h)
    {
        foreach (var (c, loot) in h.Place.Memory.Caches)
            if (c.Cell == h.Cell) return loot;
        return null;
    }

    /// <summary>What E would do on a place under the crosshair, for the prompt.</summary>
    string PlacePrompt(Vector3 eye, Vector3 dir, bool hit, LookHit look)
    {
        if (Places == null) return "";
        if (Doors.Along(eye, dir, UseReach) is var (_, _, open)) return open ? "E  Shut the door" : "E  Open the door";
        foreach (Place p in Places.Standing)
            if (p.KeyAlong(eye, dir, UseReach) is KeyCard k) return $"E  Take the {k.Clearance} keycard";
        if (!hit || !Places.Resolve(look.Collider, look.Point, look.Normal, out PoiPlaces.Hit h)) return "";
        if (CacheAt(h) is Slice.Items.Lootable cache) return $"E  Open the {cache.Name.ToLowerInvariant()}";
        if (h.Door == null) return "";
        Place.Door d = h.Door;
        if (d.Jammed) return "Jammed";
        if (d.Clearance != null && !Places.HasClearance(d.Clearance)) return $"LOCKED  {d.Clearance.ToUpperInvariant()} keycard";
        if (d.Lift >= 0) return "E  Lift door";
        return d.Target > 0.5f ? "E  Shut the door" : "E  Open the door";
    }
}
