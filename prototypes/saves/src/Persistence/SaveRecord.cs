namespace GalHack.Persistence;

/// <summary>
/// One saved thing: a structure, a container, a player, a ship, a dropped stack, or one zone's
/// terrain edits. Every record is written to its own file with its own checksum, so damage to
/// one can never spread to another.
///
/// Records are immutable. To change one, copy it with <c>with { ... }</c> and put the copy back
/// in the <see cref="RecordTable"/>, which marks it dirty for the next commit. Nothing can change
/// a record in place, so a commit running on another thread always sees one whole version of it.
/// </summary>
public sealed record SaveRecord
{
    /// <summary>Stable for the record's whole life. Lower-case letters, digits and dashes, so it is always a safe file name.</summary>
    public string Id { get; init; }

    /// <summary>What kind of thing it is ("container", "structure"...). Decides how the payload is read and migrated.</summary>
    public string Type { get; init; }

    /// <summary>
    /// Where it is, and the only location field there is. A record is in exactly one zone, so a
    /// zone crossing is one write of this field: it can never leave the thing in two zones, or none.
    /// </summary>
    public string ZoneId { get; init; }

    /// <summary>
    /// The ship it is aboard, or null. Things aboard live in that ship's cabin zone, so when the
    /// ship crosses to another zone they ride along without being written at all.
    /// </summary>
    public string ShipId { get; init; }

    /// <summary>The player who owns it, or null for things the world generated.</summary>
    public string Owner { get; init; }

    /// <summary>Set once any player has picked it up, moved it, opened it or built on it. Never cleared.</summary>
    public bool Touched { get; init; }

    /// <summary>The payload's format version for this type. Older ones are migrated when loaded.</summary>
    public int Version { get; init; } = 1;

    /// <summary>How many items it holds. The audit adds these up, so whoever builds a payload also counts it.</summary>
    public int Items { get; init; }

    /// <summary>How many blocks it is built from.</summary>
    public int Blocks { get; init; }

    /// <summary>The type-specific data: a JSON object, stored exactly as given.</summary>
    public string Payload { get; init; } = "{}";

    public bool PlayerOwned => Owner != null;

    /// <summary>Owned by or touched by a player: no cleanup may ever remove it.</summary>
    public bool IsProtected => Owner != null || Touched;

    /// <summary>Lower-case letters, digits and dashes, 1 to 64 long, not starting with a dash.</summary>
    public static bool IsValidId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || id[0] == '-') return false;
        foreach (char c in id)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')) return false;
        return true;
    }

    /// <summary>Why this record can't be saved as it is, or null if it can.</summary>
    public string Problem()
    {
        if (!IsValidId(Id)) return $"bad record id '{Id}' (use a-z, 0-9 and -)";
        if (string.IsNullOrEmpty(Type)) return $"{Id} has no type";
        if (string.IsNullOrEmpty(ZoneId)) return $"{Id} has no zone, and everything is somewhere";
        if (ShipId != null && !IsValidId(ShipId)) return $"{Id} is aboard a ship with a bad id '{ShipId}'";
        if (Items < 0 || Blocks < 0) return $"{Id} has a negative count";
        if (Version < 1) return $"{Id} has version {Version}";
        if (string.IsNullOrWhiteSpace(Payload) || Payload.TrimStart()[0] != '{') return $"{Id}'s payload is not a JSON object";
        return null;
    }
}
