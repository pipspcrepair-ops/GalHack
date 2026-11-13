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
/// (<see cref="PoiPlaces"/>), E opens their doors and their caches,
/// the survival tool salvages them and breaks their hidden walls, ladders and lift shafts carry
/// you, their creatures and caves live and hunt, and their towns go about their day.
/// </summary>
public partial class Game
{
    public PoiPlaces Places { get; private set; }

    /// <summary>The crashed capital ships, set down as real vessels as the survivor nears them.</summary>
    public Hulks Hulks { get; private set; }
    public Caves Caves { get; private set; }

    /// <summary>Door blocks on bases and vessels: the same doors, E and sliding leaves, opening for you on your own.</summary>
    public BlockDoors Doors { get; private set; }

    readonly HashSet<Town> _towns = new();

    void SetUpPlaces()
    {
        PlaceRules rules = PlaceRules.Shared;
        Places = new PoiPlaces
        {
            Name = "Places", Zone = Zone, Focus = Focus,
            BuildRadius = rules.BuildRadius, DropRadius = rules.DropRadius, BudgetMs = rules.BudgetMs, AheadSeconds = rules.AheadSeconds,
            Fauna = new PlaceFauna.Deep(Zone.Wildlife.Life, Zone.Planet, Zone.Wildlife.Livable),
        };
        Zone.AddChild(Places);
        Zone.Infra.WreckStood += Places.WreckStood;
        Zone.AddChild(new RuinLoot { Name = "Ruin loot", Zone = Zone, Focus = Focus });
        Hulks = new Hulks { Name = "Hulks", Zone = Zone, Focus = Focus, Places = Places };
        Zone.AddChild(Hulks);
        Caves = new Caves
        {
            Name = "Caves", Zone = Zone, Feet = () => Survivor.WorldFeet, Look = () => Survivor.WorldLook(), Deep = Places.Fauna,
            OnFoot = () => Survivor.Seated == null && Survivor.Body.Frame == null && !Suit.Dead,
            Indoors = () => Places.PlaceAt(Survivor.WorldFeet) != null || Bunker.Inside(Survivor.WorldFeet, Zone),
            // The bunker is dug rock, but it is not a cave: no cave creature is put in it, nor in rock near it.
            Keep = p => Bunker != null && Bunker.Over(p, Slice.Start.Bunker.KeepClear),
        };
        Zone.AddChild(Caves);
        Doors = new BlockDoors { Name = "BlockDoors", Zone = Zone, Feet = () => Survivor.WorldFeet };
        Zone.AddChild(Doors);
        Tool.Places = Places;
        Zone.Wildlife.SolidAt = Places.SolidAt;
        Zone.Light.Places = () => Places.Standing;
        Tool.Salvaged += () => Progress.Earn(2, "salvage");
        Places.WallBroken += (p, s) => Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Clank, p.WorldOf(s.WallCells[0], 1f), 0.5f, -2f);
        Places.Stood += OnPlaceStood;
        Places.Exploded += OnExploded;
        Places.WarmUp(); // still loading: the first build's one-off costs are paid now
    }

    /// <summary>The share of a hazard's damage the survivor takes: a suit stands it better than a wall.</summary>
    const float BlastOnSurvivor = 0.5f;

    /// <summary>
    /// A hazard went up in a place (a blasting crate, a fuel drum, gas): a flash, a bang and a cloud of grit,
    /// and the survivor, the creatures and the structures within its reach take it by how near they are, as
    /// a structure's blocks do (the place's own blocks already have). Its neighbours follow on their own.
    /// </summary>
    void OnExploded(Vector3 at, float radius, float damage)
    {
        float Falloff(float d) => 1f - d / radius * 0.75f;
        float near = (Survivor.WorldFeet + Vector3.Up * 0.9f).DistanceTo(at);
        if (near < radius)
        {
            Suit.Hit(damage * BlastOnSurvivor * Falloff(near), SurvivalVitals.Model.DamageKind.Hit);
            Hands?.Jolt(at, 0.6f * Falloff(near));
        }
        if (Zone.Wildlife?.Sim is { } sim)
            foreach (var c in sim.Creatures.Concat(Zone.Wildlife.Sites.SelectMany(s => s.Sim.Creatures)).ToList())
            {
                float d = Zone.Wildlife.WorldOf(c).DistanceTo(at);
                if (!c.Dead && d < radius) Zone.Wildlife.Damage(c, damage * Falloff(d));
            }
        var point = new System.Numerics.Vector3(at.X, at.Y, at.Z);
        foreach (Vessel v in Zone.Vessels)
            if (v.DistanceFromHull(v.Body.ToLocal(point), radius) < radius) v.Hits.Blast(point, radius, damage);
        var rng = new RandomNumberGenerator();
        for (int i = 0; i < 5; i++)
            Zone.Dust?.Puff(at + new Vector3(rng.RandfRange(-1f, 1f), rng.RandfRange(-0.3f, 0.8f), rng.RandfRange(-1f, 1f)) * radius * 0.25f, new Color(0.32f, 0.28f, 0.24f, 0.85f), 1.2f + radius * 0.2f);
        Zone.Dust?.Sparks(at, Vector3.Up, new Color(1f, 0.62f, 0.25f));
        Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Boom, at, 0.75f, 6f);
        SoundCues.Cue(at, 1.5f);
        var flash = new OmniLight3D { Name = "Blast flash", LightColor = new Color(1f, 0.72f, 0.4f), LightEnergy = 10f, OmniRange = radius * 3f };
        Zone.AddChild(flash);
        flash.GlobalPosition = at;
        Tween fade = flash.CreateTween();
        fade.TweenProperty(flash, "light_energy", 0f, 0.45f);
        fade.TweenCallback(Callable.From(flash.QueueFree));
    }

    void OnPlaceStood(Place p)
    {
        if (p.Memory.Town is not Town t || !_towns.Add(t)) return;
        t.Aimed += from =>
        {
            Visor.Threat(from);
            Alerts.Attack("hostile", from);
            Zone.Sfx?.Play(PlanetSurface.Fx.Sfx.Sound.Ping, from, 0.5f, -4f);
        };
        t.ShotAway += (from, to) => Shots.Fire(from, to, default, null);
        // Their own weapon, fired (or swung) from their own hands: the same shots and blows as the player's.
        t.ShotFired += (who, def, muzzle, dir, unsteady) => Arms?.FireFrom(def, muzzle, dir, unsteady, who, who.Now > 0.6f);
        // Where one falls, what they carried: their weapon and what's left of its ammo, and a little besides.
        t.Fell += who => PersonFell(t, who);
    }

    /// <summary>A townsperson's body: a container where they lie, holding their weapon, its ammo and their pockets.</summary>
    void PersonFell(Town t, Town.Person who)
    {
        if (who.Looks == null) return;
        GalHack.Items.Bill drops = t.Leaves(who);
        foreach (var kv in BodyDrops(who.Looks, Wildlife.Rng.For(who.Looks.Seed, "drop", (long)t.People.IndexOf(who)))) drops.Add(kv.Key, kv.Value);
        int[] size = Zone.Data.Rules.Containers.Corpse;
        Vector3 feet = t.World(who.Pos);
        if (EndPerson(t, who, out Vector3 left))
        {
            Remains(who.Looks, left, drops);
            return;
        }
        // The search target is the whole body as it lies: round its middle, a body's length across.
        Slice.Items.Lootable body = Zone.Loot.Create(Slice.Items.LootKind.Corpse, $"{Cap(who.Looks.CommonName)} body", feet + who.Forward * 0.4f, size[0], size[1], drops);
        body.Radius = MathF.Max(0.85f, (who.Looks.Figure?.Height ?? 1.7f) * 0.5f);
        body.Shape = () => PersonBalls(t, who); // (it ragdolls: the balls follow the body to where it comes to rest)
        PeopleBodies++;
    }

    /// <summary>Townsfolk bodies left to search (tests).</summary>
    public int PeopleBodies { get; private set; }

    /// <summary>
    /// Out of reach of whatever walks the surface: down in the ground (a cave, a hole, a basement)
    /// or under a place's roof. The wildlife patch reads the planet's surface, not rooms.
    /// </summary>
    public bool Sheltered() => Sheltered(Survivor.WorldFeet);

    /// <summary>The same for any spot in the zone: wind and tornadoes can't touch what stands there.</summary>
    public bool Sheltered(Vector3 feet)
    {
        if (feet.Y < Zone.GroundAt(feet.X, feet.Z).Y - 2.5f) return true;
        return Places?.PlaceAt(feet) is Place p && p.Roofed(feet);
    }

    /// <summary>
    /// Out of a storm's reach: in shelter, inside a vessel, or under something solid (a base's roof, an
    /// overhang). Only from a physics step.
    /// </summary>
    public bool UnderCover()
    {
        if (Sheltered() || Survivor.Seated != null || (Survivor.Aboard != null && !Survivor.OnOuterHull)) return true;
        Vector3 eye = Survivor.WorldEye();
        var ray = PhysicsRayQueryParameters3D.Create(eye, eye + Vector3.Up * 20f, Layers.World | Layers.Hull);
        return Zone.GetWorld3D().DirectSpaceState.IntersectRay(ray).Count > 0;
    }

    // ---- Climbing ---------------------------------------------------------------------------------

    /// <summary>What holds the survivor up this tick: a ladder (W or Space climbs, S or Ctrl goes down), a lift shaft (Space and Ctrl ride it, easing level with a stop when let go).</summary>
    void Climb(Controls c)
    {
        Survivor s = Survivor;
        // A ladder someone built (a base's, a vessel's) is climbed like a place's, inside its cabin or out.
        if (BuiltLadderAt(s, out LadderHold built))
        {
            s.Legs.Climb = ClimbMode.Ladder;
            s.Legs.Ladder = built;
            if (Places != null && s.Body.Frame == null) foreach (Place p in Places.Standing) p.Ride(-1, 0f);
            return;
        }
        s.Legs.Ladder = default;
        if (Places == null || s.Body.Frame != null) { s.Legs.Climb = ClimbMode.None; return; }
        ClimbMode mode = Places.ClimbAt(s.WorldFeet, out Place place, out int lift, out LadderHold ladder);
        s.Legs.Climb = mode;
        s.Legs.Ladder = ladder;
        foreach (Place p in Places.Standing) p.Ride(p == place && mode == ClimbMode.Shaft ? lift : -1, place == p ? p.FromWorld(s.WorldFeet).Y : 0f);
        if (mode != ClimbMode.Shaft || s.Legs.Vertical != 0f) return;
        float y = place.FromWorld(s.WorldFeet).Y, stop = place.NearestStop(lift, y);
        s.Legs.Vertical = Mathf.Clamp((stop + 0.05f - y) * 1.5f, -0.5f, 0.5f);
    }

    /// <summary>
    /// A ladder block of a built structure in the cell at the survivor's feet or just below (so its
    /// top rung carries them over), as the climber holds it: it looks out from the face it is mounted
    /// on, in the survivor's own world (the cabin's when they ride the structure, the zone's when they
    /// don't).
    /// </summary>
    bool BuiltLadderAt(Survivor s, out LadderHold ladder)
    {
        ladder = default;
        System.Numerics.Vector3 world = s.Body.WorldPosition;
        foreach (Vessel v in Zone.Vessels)
        {
            GalHack.Structures.Structure st = v.Structure;
            float cell = st.Grid.CellSize;
            bool riding = s.Body.Frame == v.Body;
            System.Numerics.Vector3 local = riding ? s.Body.Position : v.Body.ToLocal(world);
            GalHack.Structures.Int3 CellAt(float dy) => GalHack.Structures.Int3.Round((local + System.Numerics.Vector3.UnitY * dy) / cell);
            bool Ladder(GalHack.Structures.Int3 c, out GalHack.Structures.Block b) => st.TryGet(c, out b) && st.TypeOf(b).IsLadder;
            foreach (float dy in new[] { 0.1f, -0.45f })
            {
                GalHack.Structures.Int3 at = CellAt(dy);
                if (!Ladder(at, out GalHack.Structures.Block b)) continue;
                // Mounted by its +z face: it looks out the other way.
                GalHack.Structures.Int3 mount = GalHack.Structures.Dirs.Vec(GalHack.Structures.Orientations.ToWorld(b.Rot, GalHack.Structures.Dir.PosZ));
                var o = new System.Numerics.Vector3(-mount.X, -mount.Y, -mount.Z);
                var mid = new System.Numerics.Vector3(at.X, at.Y, at.Z) * cell;
                var behind = new GalHack.Structures.Int3(at.X + mount.X, at.Y + mount.Y, at.Z + mount.Z);
                ladder = new LadderHold
                {
                    Out = riding ? o : System.Numerics.Vector3.Transform(o, v.Body.Rotation),
                    Centre = riding ? mid : v.Body.ToParent(mid),
                    Backed = st.TryGet(behind, out GalHack.Structures.Block wall) && !st.TypeOf(wall).IsLadder,
                    Below = Ladder(CellAt(-0.1f), out _),
                    Top = dy < 0f,
                };
                return true;
            }
        }
        return false;
    }

    // ---- E ------------------------------------------------------------------------------------------

    /// <summary>E on a built door (a base's or a vessel's): it opens or shuts.</summary>
    bool UseBuiltDoor(Vector3 eye, Vector3 dir) =>
        Places != null && Doors.Along(eye, dir, UseReach) is var (vessel, cell, _) && Doors.Use(vessel, cell);

    /// <summary>E on a place: a door opens or shuts, a shaft door calls its car.</summary>
    bool UsePlace(LookHit look)
    {
        if (Places == null || !Places.Resolve(look.Collider, look.Point, look.Normal, out PoiPlaces.Hit h)) return false;
        if (h.Door != null)
        {
            var (ok, message) = h.Place.UseDoor(h.Door);
            if (!ok && message != "") Say(message, Tones.Caution); // only what stops you
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
            return true;
        }
        if (b.Type == BlockType.Terminal)
        {
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
        if (!hit || !Places.Resolve(look.Collider, look.Point, look.Normal, out PoiPlaces.Hit h)) return "";
        if (CacheAt(h) is Slice.Items.Lootable cache) return $"E  Open {cache.Name.ToLowerInvariant()}";
        if (h.Door == null) return "";
        Place.Door d = h.Door;
        if (d.Jammed) return "JAMMED";
        if (d.Lift >= 0) return "E  Lift door";
        return d.Target > 0.5f ? "E  Shut the door" : "E  Open the door";
    }
}
