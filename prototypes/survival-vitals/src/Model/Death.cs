using System.Collections.Generic;
using System.Linq;

namespace SurvivalVitals.Model;

/// <summary>
/// Something a player left in the world: the backpack dropped on death, or anything dropped by
/// hand. It stays where it is, marked on the map, until its owner picks it up. It never
/// despawns and never times out.
/// </summary>
public sealed class Backpack
{
    public int Id;
    public Pos Position;
    public double DroppedAt;
    public string Reason;
    public Inventory Contents;
    public bool OnMap = true;
}

/// <summary>
/// The permanent record of everything players have dropped. Rule 1: nothing a player builds,
/// drops or stores ever disappears. The only way out of this list is being picked up.
/// </summary>
public sealed class WorldStore
{
    readonly List<Backpack> _backpacks = new();
    int _nextId = 1;

    public IReadOnlyList<Backpack> Backpacks => _backpacks;

    public Backpack Drop(Pos at, Inventory contents, double now, string reason)
    {
        var pack = new Backpack { Id = _nextId++, Position = at, DroppedAt = now, Reason = reason, Contents = contents };
        _backpacks.Add(pack);
        return pack;
    }

    /// <summary>Moves a backpack's contents into the player's inventory; the empty pack goes.</summary>
    public Outcome Recover(Backpack pack, Inventory into)
    {
        if (!_backpacks.Contains(pack)) return Outcome.No("That backpack is already recovered.");
        int items = pack.Contents.ItemCount;
        into.Absorb(pack.Contents);
        _backpacks.Remove(pack);
        return Outcome.Done($"Backpack recovered: {items} items back in your inventory.");
    }
}

public enum RespawnKind { CloneChamber, HealingBooth, SurvivalTent, EscapePod }

/// <summary>A place a player can come back at.</summary>
public sealed class RespawnPoint
{
    public string Id;
    public string Name;
    public RespawnKind Kind;
    public Pos Position;   // the device
    public Pos Stand;      // where the player appears
    public bool IsHome;

    public bool CanBeHome => Kind is RespawnKind.CloneChamber or RespawnKind.HealingBooth;
}

public enum RespawnChoice { Home, NearestMedical, NearestTent, FreshStart }

public sealed record RespawnOption(RespawnChoice Choice, string Label, RespawnPoint Point, bool Available, string Detail);

/// <summary>The four ways back after dying, as the death screen offers them.</summary>
public static class Respawning
{
    public static List<RespawnOption> Options(Pos deathAt, IEnumerable<RespawnPoint> points)
    {
        var list = points.ToList();
        RespawnPoint home = list.FirstOrDefault(p => p.IsHome && p.CanBeHome);
        RespawnPoint medical = Nearest(deathAt, list.Where(p => p.CanBeHome));
        RespawnPoint tent = Nearest(deathAt, list.Where(p => p.Kind == RespawnKind.SurvivalTent));

        return new List<RespawnOption>
        {
            home != null
                ? new(RespawnChoice.Home, "Home base", home, true, $"{home.Name}, {deathAt.FlatDistanceTo(home.Position):F0} m away")
                : new(RespawnChoice.Home, "Home base", null, false, "No home set: press E at a clone chamber"),
            medical != null
                ? new(RespawnChoice.NearestMedical, "Nearest clone chamber or healing booth", medical, true, $"{medical.Name}, {deathAt.FlatDistanceTo(medical.Position):F0} m away")
                : new(RespawnChoice.NearestMedical, "Nearest clone chamber or healing booth", null, false, "None built"),
            tent != null
                ? new(RespawnChoice.NearestTent, "Nearest survival tent", tent, true, $"{tent.Name}, {deathAt.FlatDistanceTo(tent.Position):F0} m away")
                : new(RespawnChoice.NearestTent, "Nearest survival tent", null, false, "None pitched"),
            new(RespawnChoice.FreshStart, "Fresh start in a new escape pod", null, true, "Keeps level, unlocks, structures and standing"),
        };
    }

    static RespawnPoint Nearest(Pos from, IEnumerable<RespawnPoint> points) =>
        points.OrderBy(p => from.FlatDistanceTo(p.Position)).FirstOrDefault();
}
