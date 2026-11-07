using System.Collections.Generic;

namespace Slice.Ui;

/// <summary>Which level the Map tab shows, smallest first.</summary>
public enum MapLevel { Local, World, HighOrbit, System, Galaxy }

/// <summary>
/// The map's one hierarchy over one set of data: the galaxy holds every star, a star's system holds
/// its bodies, high orbit holds a world and what is round it, and the world holds the ground under
/// you. It works both ways: down from the galaxy to any star's system and on to a world, and back up
/// from any spot, each level showing where you are.
///
/// A star is known at every level above its worlds whether or not anyone has been there: an
/// unvisited star's system is only its catalogue entry, generated on the first visit, so the trail
/// goes no lower than that until you are there.
/// </summary>
public sealed class MapTrail
{
    /// <summary>The star you are at.</summary>
    public int You = -1;

    /// <summary>The star the levels under the galaxy are looking at.</summary>
    public int Star = -1;

    public MapLevel Level { get; private set; } = MapLevel.Local;

    public bool AtYourStar => Star == You;

    /// <summary>Goes straight to a level. Under the system level there is only where you are.</summary>
    public void Open(MapLevel level)
    {
        Level = level;
        if (level < MapLevel.System || Star < 0) Star = You;
    }

    /// <summary>Drills into a star from the galaxy.</summary>
    public void OpenStar(int star)
    {
        Star = star;
        Level = MapLevel.System;
    }

    /// <summary>One level in. False at the ground under you, or at a system nobody has charted.</summary>
    public bool Down()
    {
        if (!CanGoDown) return false;
        Level--;
        return true;
    }

    public bool CanGoDown => Level != MapLevel.Local && (Level != MapLevel.System || AtYourStar);

    /// <summary>One level out. False at the galaxy.</summary>
    public bool Up()
    {
        if (Level == MapLevel.Galaxy) return false;
        Level++;
        return true;
    }

    /// <summary>The levels from the galaxy down to this one, each with its name.</summary>
    public List<(MapLevel level, string name)> Crumbs(string starName)
    {
        var list = new List<(MapLevel, string)> { (MapLevel.Galaxy, "GALAXY") };
        if (Level == MapLevel.Galaxy) return list;
        list.Add((MapLevel.System, starName.ToUpperInvariant()));
        if (Level == MapLevel.System) return list;
        list.Add((MapLevel.HighOrbit, "ORBIT"));
        if (Level == MapLevel.HighOrbit) return list;
        list.Add((MapLevel.World, "WORLD"));
        if (Level == MapLevel.World) return list;
        list.Add((MapLevel.Local, "HERE"));
        return list;
    }

    public static string Name(MapLevel level) => level switch
    {
        MapLevel.Local => "HERE", MapLevel.World => "WORLD", MapLevel.HighOrbit => "HIGH ORBIT", MapLevel.System => "SYSTEM", _ => "GALAXY",
    };
}
