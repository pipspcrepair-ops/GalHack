using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// The one call POI dressing makes: give it a room (size, doorways, the room id and tags from the
/// room library, the kit, the POI's state and age, a seed) and it returns the placements, arranged
/// as the room was used and then left as the collapse left it. Also maps the room library's generic
/// props (bench, machine, shelf...) to pieces of the room's set.
/// </summary>
public sealed class DecoPoi
{
    public readonly DecoForge Forge;
    public readonly DecoArrangements Arrangements;
    readonly DecoPlanner _planner;
    readonly DecoDisorder _disorder;
    readonly DecoSites _sites;

    public DecoPoi(DecoForge forge, DecoArrangements arrangements)
    {
        Forge = forge;
        Arrangements = arrangements;
        _planner = new DecoPlanner(forge, arrangements);
        _disorder = new DecoDisorder(forge, arrangements);
        _sites = new DecoSites(forge, arrangements);
    }

    /// <summary>
    /// Dresses a room: the set and recipe come from the room's role and the place's tags (see
    /// <see cref="DecoArrangements.Resolve"/>) unless the room already names a set.
    /// </summary>
    public DecoPlan Dress(DecoRoom room, string kit = null, bool disorder = true)
    {
        if (room.Set == null) (room.Set, room.Recipe) = Arrangements.Resolve(room.RoomId, room.Tags, kit);
        else if (room.Recipe == null && room.RoomId != null && Arrangements.Recipes.ContainsKey(room.RoomId)) room.Recipe = room.RoomId;
        if (!Arrangements.Sets.ContainsKey(room.Set) && room.Recipe == null) (room.Set, room.Recipe) = Arrangements.Resolve(null, null, null);
        DecoPlan plan = _planner.Plan(room);
        if (disorder) _disorder.Apply(plan);
        return plan;
    }

    /// <summary>The site's room (size and lane) for a site id.</summary>
    public DecoRoom SiteRoom(string site, ulong seed, DecoTech tech = DecoTech.Mid, string state = "abandoned") => _sites.Room(site, seed, tech, state);

    /// <summary>Dresses an outdoor site (an evacuation jam, a crashed airliner, a derailed train...).</summary>
    public DecoPlan DressSite(DecoRoom site) => _sites.Plan(site);

    /// <summary>A named anchor of a placed piece in room coordinates (for stringing wires between placements).</summary>
    public System.Numerics.Vector3 AnchorAt(DecoPlacement pl, string name) => _sites.AnchorAt(pl, name);

    /// <summary>
    /// The piece a generic POI prop becomes in a set: one of the set's pieces playing one of the
    /// prop's roles (by seed), else the prop's fallback piece.
    /// </summary>
    public DecoPiece PropPiece(string prop, string set, ulong seed)
    {
        if (!Arrangements.Props.TryGetValue(prop, out var map)) return null;
        var candidates = Forge.Catalogue.InSet(set).Where(p => !p.Outdoor && p.Mount is DecoMount.Floor or DecoMount.Wall && map.Roles.Any(p.Has)).ToList();
        if (candidates.Count == 0) return Forge.Catalogue.Piece(map.Fallback);
        return candidates[(int)(DecoHash.Mix(seed ^ DecoHash.Fnv(prop)) % (ulong)candidates.Count)];
    }

    /// <summary>The POI room library's generic props.</summary>
    public static readonly string[] GenericProps = { "bench", "machine", "shelf", "locker", "crate", "table", "bunk", "bed", "pod", "tank", "statue", "crystal", "cell", "pillar", "barricade", "planter", "screen" };
}
