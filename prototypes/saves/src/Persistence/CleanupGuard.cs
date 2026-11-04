namespace GalHack.Persistence;

/// <summary>
/// The rule behind "no silent cleanup, ever": the game may tidy up things the world generated
/// that nobody has touched (a derelict nobody visited, say), and nothing else. Anything a player
/// owns or has touched is refused, whatever the reason given.
/// </summary>
public static class CleanupGuard
{
    /// <summary>Why this record must never be cleaned up, or null if it may be.</summary>
    public static string Refusal(SaveRecord record)
    {
        if (record.Owner != null)
            return $"refused: {record.Id} belongs to {record.Owner}, and nothing a player owns is ever cleaned up";
        if (record.Touched)
            return $"refused: {record.Id} has been touched by a player, and touched things are never cleaned up";
        return null;
    }
}
