using System;
using System.Collections.Generic;
using System.Numerics;

namespace Encounters.Director;

/// <summary>What kind of space a zone is. Decks gate on it through the "kind:..." tag.</summary>
public enum ZoneKind { System, Orbit, Moon, Surface, Deep, Atmosphere }

public enum CircleKind
{
    /// <summary>NPC faction territory. Sets the faction mix of events spawned inside it.</summary>
    Territory,
    /// <summary>A point of interest (station, asteroid field...). Events can anchor to it.</summary>
    Poi,
    /// <summary>A player base. Nothing spawns inside it.</summary>
    Base,
    /// <summary>Nothing spawns inside it.</summary>
    NoSpawn,
}

/// <summary>A circle on the zone's X/Z plane: the plane the director map shows from above.</summary>
public sealed class ZoneCircle
{
    public CircleKind Kind { get; set; }
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Faction { get; set; } = "";
    public string Poi { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float R { get; set; }

    public Vector3 Center => new(X, 0f, Z);

    public bool BlocksSpawns => Kind is CircleKind.NoSpawn or CircleKind.Base;

    /// <summary>
    /// Circles reach up and down without limit: in space a no-spawn bubble that only covered a
    /// thin slice would let a ship spawn right above a player base.
    /// </summary>
    public bool Contains(Vector3 p, float margin = 0f)
    {
        float dx = p.X - X, dz = p.Z - Z, r = R + margin;
        return dx * dx + dz * dz <= r * r;
    }
}

/// <summary>
/// One zone: system space, a planet or moon orbit, a surface or a deep-space site. Its tags and
/// circles are the only thing the director, the map and content gating need to know about it.
/// </summary>
public sealed class ZoneDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ZoneKind Kind { get; set; }
    public string Region { get; set; } = "";
    public string Star { get; set; } = "";
    public int Danger { get; set; }
    public float Radius { get; set; } = 12000f;
    public List<string> Tags { get; set; } = new();
    public List<ZoneCircle> Circles { get; set; } = new();

    HashSet<string> _allTags;

    /// <summary>
    /// The explicit tags plus derived ones (kind:orbit, region:veyra-reach, star:k, danger:2,
    /// poi:station), so a deck condition can test any zone attribute the same way.
    /// </summary>
    public IReadOnlyCollection<string> AllTags => _allTags ??= BuildTags();

    public bool HasTag(string tag) => (_allTags ??= BuildTags()).Contains(tag);

    /// <summary>True if any of the alternatives is one of the zone's tags.</summary>
    public bool HasAnyTag(string[] alternatives)
    {
        foreach (string tag in alternatives)
            if (HasTag(tag)) return true;
        return false;
    }

    public bool InBounds(Vector3 p) => p.X * p.X + p.Z * p.Z <= Radius * Radius && MathF.Abs(p.Y) <= Radius;

    public bool BlocksSpawn(Vector3 p)
    {
        foreach (ZoneCircle circle in Circles)
            if (circle.BlocksSpawns && circle.Contains(p)) return true;
        return false;
    }

    /// <summary>Within <paramref name="clearance"/> of a player base's circle, measured from its edge.</summary>
    public bool NearBase(Vector3 p, float clearance)
    {
        foreach (ZoneCircle circle in Circles)
            if (circle.Kind == CircleKind.Base && circle.Contains(p, clearance)) return true;
        return false;
    }

    public ZoneCircle FindPoi(string poi)
    {
        foreach (ZoneCircle circle in Circles)
            if (circle.Kind == CircleKind.Poi && string.Equals(circle.Poi, poi, StringComparison.OrdinalIgnoreCase)) return circle;
        return null;
    }

    /// <summary>Distinct factions whose territory covers the point (within the margin), in data order.</summary>
    public void FactionsAt(Vector3 p, float margin, List<string> into)
    {
        into.Clear();
        foreach (ZoneCircle circle in Circles)
            if (circle.Kind == CircleKind.Territory && circle.Contains(p, margin) && !into.Contains(circle.Faction))
                into.Add(circle.Faction);
    }

    public bool InTerritoryOf(Vector3 p, string faction)
    {
        foreach (ZoneCircle circle in Circles)
            if (circle.Kind == CircleKind.Territory && circle.Faction == faction && circle.Contains(p)) return true;
        return false;
    }

    HashSet<string> BuildTags()
    {
        var tags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string tag in Tags) tags.Add(tag);
        tags.Add("kind:" + Kind.ToString().ToLowerInvariant());
        if (Region != "") tags.Add("region:" + Slug(Region));
        if (Star != "") tags.Add("star:" + Star.ToLowerInvariant());
        tags.Add("danger:" + Danger);
        foreach (ZoneCircle circle in Circles)
            if (circle.Kind == CircleKind.Poi && circle.Poi != "") tags.Add("poi:" + circle.Poi.ToLowerInvariant());
        return tags;
    }

    static string Slug(string text) => text.Trim().ToLowerInvariant().Replace(' ', '-');

    internal void Validate()
    {
        if (Id == "") throw new FormatException("zones: a zone has no id");
        if (Danger is < 0 or > 5) throw new FormatException($"zones: '{Id}' danger must be 0..5");
        if (Radius <= 0) throw new FormatException($"zones: '{Id}' needs a positive radius");
        foreach (ZoneCircle circle in Circles)
        {
            if (circle.R <= 0) throw new FormatException($"zones: '{Id}' has a circle with no radius");
            if (circle.Kind == CircleKind.Territory && circle.Faction == "")
                throw new FormatException($"zones: '{Id}' territory '{circle.Name}' has no faction");
            if (circle.Kind == CircleKind.Poi && circle.Poi == "")
                throw new FormatException($"zones: '{Id}' POI '{circle.Name}' has no poi kind");
        }
    }
}

public sealed class ZoneCatalog
{
    public List<ZoneDef> Zones { get; set; } = new();

    Dictionary<string, ZoneDef> _byId;

    public ZoneDef Get(string id) =>
        TryGet(id, out ZoneDef zone) ? zone : throw new KeyNotFoundException($"no zone '{id}'");

    public bool TryGet(string id, out ZoneDef zone)
    {
        _byId ??= BuildIndex();
        return _byId.TryGetValue(id, out zone);
    }

    Dictionary<string, ZoneDef> BuildIndex()
    {
        var index = new Dictionary<string, ZoneDef>();
        foreach (ZoneDef zone in Zones) index[zone.Id] = zone;
        return index;
    }

    public static ZoneCatalog Parse(string json)
    {
        var catalog = Json.Parse<ZoneCatalog>(json, "zones");
        var seen = new HashSet<string>();
        foreach (ZoneDef zone in catalog.Zones)
        {
            zone.Validate();
            if (!seen.Add(zone.Id)) throw new FormatException($"zones: duplicate id '{zone.Id}'");
        }
        return catalog;
    }
}
