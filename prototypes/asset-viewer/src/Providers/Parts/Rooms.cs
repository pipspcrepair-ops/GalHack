using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;
using Slice.Places;

namespace AssetViewer.Providers.Parts;

/// <summary>
/// The arrangement templates: a room furnished from a theme set's plan, a named room recipe, or an outdoor
/// site (a road jam, a car park, a crashed airliner, a derailed train), as the game dresses its places:
/// as used (before the disorder pass, fresh and powered) and as found (after it, a century on).
/// The seed rolls the arrangement and the disorder.
/// </summary>
public static class Rooms
{
    static readonly (float W, float D)[] Dims = { (10, 8), (7, 6), (14, 10) };

    public static IEnumerable<AssetEntry> Entries(Decor decor)
    {
        DecoArrangements arr = decor.Poi.Arrangements;
        foreach (string set in arr.Sets.Keys)
            yield return RoomEntry(decor, "room.set." + set, "Rooms/Theme sets", decor.Catalogue.Set(set).Name, set, null, 0);
        foreach (var (name, plan) in arr.Recipes)
            yield return RoomEntry(decor, "room.recipe." + name, "Rooms/Recipes", PartsData.Nice(name), plan.Set, name, 2);
        foreach (var (site, def) in arr.Sites)
            yield return SiteEntry(decor, site);
    }

    static AssetEntry RoomEntry(Decor decor, string id, string group, string name, string set, string recipe, int size) => new()
    {
        Id = id, Group = group, Name = name, Randomisable = true,
        Options = new[]
        {
            new AssetOption("View", DecoProvider.Views),
            new AssetOption("Found as", DecoProvider.FoundStates) { Rollable = true },
            new AssetOption("Tech", DecoProvider.Techs) { Rollable = true },
            new AssetOption("Size", DecoProvider.Sizes) { Default = size, Rollable = true },
        },
        Build = r => BuildRoom(decor, set, recipe, r),
    };

    public static AssetEntry SiteEntry(Decor decor, string site) => new()
    {
        Id = "site." + site, Group = "Sites", Name = PartsData.Nice(site), Randomisable = true,
        Options = new[]
        {
            new AssetOption("View", DecoProvider.Views),
            new AssetOption("Found as", DecoProvider.FoundStates) { Rollable = true },
            new AssetOption("Tech", DecoProvider.Techs) { Rollable = true },
        },
        Build = r => BuildSite(decor, site, r),
    };

    static DecoTech TechOf(BuildRequest r) => Enum.TryParse(r.Get("Tech", "Mid"), out DecoTech t) ? t : DecoTech.Mid;

    static AssetInstance BuildRoom(Decor decor, string set, string recipe, BuildRequest r)
    {
        var (w, d) = Dims[Math.Max(0, Array.IndexOf(DecoProvider.Sizes, r.Get("Size")))];
        string found = r.Get("Found as", "abandoned");
        DecoRoom Make(string state) => new()
        {
            Width = w, Depth = d, Set = set, Recipe = recipe, RoomId = recipe ?? set, Tech = TechOf(r), Seed = r.Seed, State = state,
            Doors = new List<DecoDoor> { new(DecoWall.South, w / 2, 1.6f), new(DecoWall.East, d / 2, 1.4f) },
        };
        DecoPlan used = null, after = null;
        string view = r.Get("View", DecoProvider.Views[0]);
        if (view != DecoProvider.Views[0])
        {
            DecoRoom room = Make("active");
            room.Age = DecoAge.Fresh;
            used = decor.Poi.Dress(room, disorder: false);
            foreach (DecoPlacement pl in used.Placements) { pl.Powered = true; pl.Age = DecoAge.Fresh; }
        }
        if (view != DecoProvider.Views[1]) after = decor.Poi.Dress(Make(found));
        return Show(decor, used, after, site: false, r);
    }

    static AssetInstance BuildSite(Decor decor, string site, BuildRequest r)
    {
        DecoTech tech = TechOf(r);
        string found = r.Get("Found as", "abandoned");
        ulong seed = r.Seed + DecoHash.Fnv(site) % 10000;
        DecoPlan used = null, after = null;
        string view = r.Get("View", DecoProvider.Views[0]);
        if (view != DecoProvider.Views[0])
        {
            DecoRoom fresh = decor.Poi.SiteRoom(site, seed, tech, "active");
            fresh.Age = DecoAge.Fresh;
            used = decor.Poi.DressSite(fresh);
        }
        if (view != DecoProvider.Views[1]) after = decor.Poi.DressSite(decor.Poi.SiteRoom(site, seed, tech, found));
        return Show(decor, used, after, site: true, r);
    }

    static AssetInstance Show(Decor decor, DecoPlan used, DecoPlan after, bool site, BuildRequest r)
    {
        var asset = new ModelAsset { Name = "Room" };
        DecoPlan first = used ?? after, second = used != null ? after : null;
        float gap = site ? 6f : 3f;
        float width = first.Room.Width + (second != null ? second.Room.Width + gap : 0f);
        float depth = Mathf.Max(first.Room.Depth, second?.Room.Depth ?? 0f);
        float x = -width * 0.5f;
        void Place(DecoPlan plan)
        {
            var holder = new Node3D { Position = new Vector3(x, 0, -plan.Room.Depth * 0.5f) };
            asset.AddChild(holder);
            holder.AddChild(DecoPlanView.Create(decor.Meshes, plan, collide: false, lights: !site));
            Floor(holder, plan.Room, site);
            if (site) Lane(holder, plan.Room); else Walls(holder, plan.Room);
            x += plan.Room.Width + gap;
        }
        Place(first);
        if (second != null) Place(second);
        asset.Stage = new StageSpec { Ground = false };
        asset.SetSpins(false);
        asset.FactsFn = () => Facts(decor, used, after);
        asset.Grow(asset);
        return asset;
    }

    static IEnumerable<(string, string)> Facts(Decor decor, DecoPlan used, DecoPlan after)
    {
        DecoPlan p = after ?? used;
        DecoRoom room = p.Room;
        yield return (room.Outdoor ? "Site" : "Room", $"{room.Width:0.#} x {room.Depth:0.#} m, {room.Doors.Count} doorways");
        if (!room.Outdoor) yield return ("Set / recipe", $"{room.Set}{(room.Recipe != null ? " / " + room.Recipe : "")}");
        if (used != null) yield return ("As used", $"{used.Placements.Count} pieces, {used.Cables.Count} wires");
        if (after != null)
        {
            yield return ("As found", $"{after.Placements.Count} pieces, {after.Cables.Count} wires, {room.State}");
            int knocked = after.Placements.Count(x => x.State == DecoState.Knocked), broken = after.Placements.Count(x => x.State == DecoState.Broken);
            int burnt = after.Placements.Count(x => x.State == DecoState.Burnt), grown = after.Placements.Count(x => x.State == DecoState.Overgrown);
            yield return ("Disorder", $"{knocked} knocked, {broken} broken, {burnt} burnt, {grown} overgrown");
        }
        yield return ("Tech", room.Tech?.ToString() ?? "mid");
    }

    // ---- The room's floor and walls ---------------------------------------------------------------

    static StandardMaterial3D _floor, _lane, _ground, _wall;
    static StandardMaterial3D FloorMat => _floor ??= new StandardMaterial3D { AlbedoColor = new Color("4a4640"), Roughness = 0.95f };
    static StandardMaterial3D GroundMat => _ground ??= new StandardMaterial3D { AlbedoColor = new Color("6e6048"), Roughness = 1f };
    static StandardMaterial3D LaneMat => _lane ??= new StandardMaterial3D { AlbedoColor = new Color("3a3836"), Roughness = 0.95f };
    static StandardMaterial3D WallMat => _wall ??= new StandardMaterial3D { AlbedoColor = new Color("6e6862"), Roughness = 0.95f };

    static void Floor(Node3D holder, DecoRoom room, bool site) =>
        holder.AddChild(new MeshInstance3D { Name = "Floor", Mesh = new BoxMesh { Size = new Vector3(room.Width + 0.2f, 0.1f, room.Depth + 0.2f) }, Position = new Vector3(room.Width / 2, -0.05f, room.Depth / 2), MaterialOverride = site ? GroundMat : FloorMat });

    static void Walls(Node3D holder, DecoRoom room)
    {
        const float h = 0.6f, t = 0.2f;
        foreach (DecoWall w in new[] { DecoWall.North, DecoWall.South, DecoWall.West, DecoWall.East })
        {
            float len = w is DecoWall.North or DecoWall.South ? room.Width : room.Depth;
            var gaps = room.Doors.Where(dr => dr.Wall == w).Select(dr => (lo: dr.Offset - dr.Width / 2, hi: dr.Offset + dr.Width / 2)).OrderBy(g => g.lo).ToList();
            float s = -t;
            foreach (var g in gaps.Append((lo: len + t, hi: len + t)))
            {
                if (g.lo > s + 0.01f)
                {
                    float a = s, b = g.lo, mid = (a + b) / 2, l = b - a;
                    Vector3 c = w switch
                    {
                        DecoWall.North => new Vector3(mid, h / 2, -t / 2), DecoWall.South => new Vector3(mid, h / 2, room.Depth + t / 2),
                        DecoWall.West => new Vector3(-t / 2, h / 2, mid), _ => new Vector3(room.Width + t / 2, h / 2, mid),
                    };
                    Vector3 size = w is DecoWall.North or DecoWall.South ? new Vector3(l, h, t) : new Vector3(t, h, l);
                    holder.AddChild(new MeshInstance3D { Name = "Wall", Mesh = new BoxMesh { Size = size }, Position = c, MaterialOverride = WallMat });
                }
                s = g.hi;
            }
        }
    }

    static void Lane(Node3D holder, DecoRoom room)
    {
        if (room.Doors.Count < 2) return;
        var (a, _) = room.DoorPoint(room.Doors[0]);
        var (b, _) = room.DoorPoint(room.Doors[1]);
        bool alongZ = room.Doors[0].Wall is DecoWall.North or DecoWall.South;
        Vector3 size = alongZ ? new Vector3(room.Agent, 0.02f, room.Depth) : new Vector3(room.Width, 0.02f, room.Agent);
        holder.AddChild(new MeshInstance3D { Name = "Lane", Mesh = new BoxMesh { Size = size }, Position = new Vector3((a.X + b.X) / 2, 0.011f, (a.Y + b.Y) / 2), MaterialOverride = LaneMat });
    }
}
