using System.Collections.Generic;
using GalHack.Items;
using GalHack.Deco;
using PlanetSurface.Poi;
using PoiGenerator.Gen;
using Slice.Items;

namespace Slice.Places;

/// <summary>
/// Everything a place remembers for the whole game, whether or not it stands near you right now:
/// its caches (their contents), the keycards taken, which doors are open, the walls broken and the
/// blocks and pieces salvaged, its creatures and its people. The place's own block grid
/// (<see cref="PoiInstance.Blocks"/>) carries every block change, so a place built again later is
/// built as it was left. Nothing here is a scene of its own: a place stands in the surface world
/// like the ground does.
/// </summary>
public sealed class PlaceMemory
{
    public readonly PoiSite Site;
    public PoiInstance Inst => Site.Instance;

    public PlaceMemory(PoiSite site)
    {
        Site = site;
        Mood = site.Instance.Mood;
    }

    /// <summary>Each cache as a container in the zone (made the first time the place stands).</summary>
    public readonly Dictionary<LootCache, Lootable> Caches = new();
    public readonly HashSet<KeyCard> KeysTaken = new();
    /// <summary>Doors left open, by doorway index (-1 is the way in).</summary>
    public readonly Dictionary<int, bool> DoorsOpen = new();
    public readonly Dictionary<SecretRoom, float> WallDamage = new();
    public readonly HashSet<SecretRoom> Broken = new();
    /// <summary>Damage dealt to blocks being salvaged, by cell.</summary>
    public readonly Dictionary<Int3, float> BlockDamage = new();
    /// <summary>Deco pieces salvaged, by room (module) and placement index; and damage on the ones being worked.</summary>
    public readonly Dictionary<int, HashSet<int>> DecoTaken = new();
    public readonly Dictionary<(int Room, int Piece), float> DecoDamage = new();
    /// <summary>Where each lift's car stands (walk height).</summary>
    public float[] LiftY;

    /// <summary>The rooms the decorations library dresses, made once (worker): the generic props there are cleared.</summary>
    public Dictionary<int, PlaceRoom> Rooms;
    public bool Prepared;

    /// <summary>A block changed since it was generated: the planet's far drawing of it is stale, so this one stays.</summary>
    public bool Modified;
    /// <summary>The survivor has been inside.</summary>
    public bool Entered;
    /// <summary>A settlement's temper now (a peaceful one turns hostile if you shoot in it).</summary>
    public Mood Mood;
    /// <summary>A settlement's standing with you, -100 to 100: what you've done there moves it, and it sets how they treat you (<see cref="Town"/>).</summary>
    public float Standing;

    /// <summary>Fractions of salvage yields not yet a whole item.</summary>
    public readonly Dictionary<string, double> SalvageCarry = new();

    /// <summary>What in it fills only part of its cell, as it collides (<see cref="PlaceParts"/>): made once, kept as blocks and pieces go.</summary>
    public Wildlife.PartSet Parts;

    /// <summary>Its creatures (a sim of their own, kept while you are away) and its people.</summary>
    public PlaceFauna Fauna;
    public Town Town;
}

/// <summary>One room the decorations library dresses: where it sits (place cells) and its plan.</summary>
public sealed class PlaceRoom
{
    public int Module;
    /// <summary>The room's floor corner (the lowest x and z cell, walk level) in place cells.</summary>
    public Int3 Corner;
    /// <summary>The floor's height in the corner cell's level: 0 on a bare floor, a plate's top where the room is laid with them.</summary>
    public float Floor;
    public DecoRoom Room;
    public DecoPlan Plan;
    /// <summary>Placements that block walking (collide), by index; the rest are scenery.</summary>
    public readonly HashSet<int> Solid = new();
    /// <summary>The cells a blocking piece stands in (for walking routes): all of <see cref="Blocking"/>.</summary>
    public readonly HashSet<Int3> Cells = new();
    /// <summary>Each blocking piece's cells: those it stands in, and those whose middle a person can't reach for it.</summary>
    public readonly Dictionary<int, HashSet<Int3>> Blocking = new();
}
