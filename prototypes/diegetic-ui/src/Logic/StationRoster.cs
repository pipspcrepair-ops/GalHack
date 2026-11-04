using System;
using System.Collections.Generic;
using System.Linq;

namespace DiegeticUi.Logic;

public enum Station { Pilot, Navigation, Gunnery, Engineering, Scanning }

/// <summary>
/// Who does what on the bridge. Roles are dragged onto crew right on the ship console.
///
/// The rules a small crew needs, and no more:
/// - There is always exactly one pilot. Handing the role to someone else moves it; it can
///   never simply be dropped, or the ship would be flying itself.
/// - Gunnery can be shared: the turrets are split as evenly as possible between gunners.
/// - Navigation, engineering and scanning have at most one holder each; assigning moves them.
/// - One person may hold several roles.
/// </summary>
public sealed class StationRoster
{
    readonly Dictionary<Station, List<string>> _holders = new();

    public IReadOnlyList<string> Crew { get; }
    public int Turrets { get; }
    public int Version { get; private set; }

    /// <summary>Why the last change was refused, for the console to show. Null if it wasn't.</summary>
    public string LastRefusal { get; private set; }

    public StationRoster(IReadOnlyList<string> crew, int turrets = 4)
    {
        if (crew.Count == 0) throw new ArgumentException("a ship needs at least one crew member to pilot it");
        Crew = crew;
        Turrets = turrets;
        foreach (Station s in Enum.GetValues<Station>()) _holders[s] = new List<string>();
        _holders[Station.Pilot].Add(crew[0]);
    }

    public IReadOnlyList<string> Holders(Station station) => _holders[station];

    public IEnumerable<Station> RolesOf(string crew) =>
        Enum.GetValues<Station>().Where(s => _holders[s].Contains(crew));

    public static bool Shared(Station station) => station == Station.Gunnery;

    public bool Assign(Station station, string crew)
    {
        if (!Crew.Contains(crew)) return Refuse($"{crew} IS NOT ABOARD");
        List<string> holders = _holders[station];
        if (holders.Contains(crew)) return Accept();
        if (!Shared(station)) holders.Clear(); // single-holder roles move
        holders.Add(crew);
        return Accept();
    }

    public bool Unassign(Station station, string crew)
    {
        if (station == Station.Pilot) return Refuse("THE SHIP NEEDS A PILOT: GIVE THE ROLE TO SOMEONE ELSE");
        if (!_holders[station].Remove(crew)) return Refuse(null);
        return Accept();
    }

    /// <summary>
    /// The turrets this crew member controls. Split evenly; the first gunners take any
    /// remainder, so 4 turrets over 3 gunners is 2, 1, 1.
    /// </summary>
    public int TurretsFor(string crew)
    {
        List<string> gunners = _holders[Station.Gunnery];
        int index = gunners.IndexOf(crew);
        if (index < 0) return 0;
        return Turrets / gunners.Count + (index < Turrets % gunners.Count ? 1 : 0);
    }

    bool Accept()
    {
        LastRefusal = null;
        Version++;
        return true;
    }

    bool Refuse(string why)
    {
        LastRefusal = why;
        Version++;
        return false;
    }
}
