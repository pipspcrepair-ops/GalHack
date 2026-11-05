using System;
using System.Collections.Generic;
using System.Linq;
using GalHack.Deco;
using GalHack.Deco.Engine;
using GalHack.Items;
using Godot;

namespace DecoShowroom;

/// <summary>
/// The arrangement templates in use: each demo room (and each outdoor site) shown twice, as it was
/// used (before the disorder pass, fresh and powered) and as found (after the disorder pass for the
/// chosen state, a century or two on). Low walls mark the room and gaps mark its doorways; a dark
/// strip marks the lane a site keeps clear.
/// </summary>
public partial class RoomGallery : Node3D
{
    public DecoMeshCache Meshes;
    public DecoPoi Poi;
    public string State = "abandoned";
    public ulong Seed = 7;
    /// <summary>Where each demo's "before" and "after" stand, and its size.</summary>
    public readonly Dictionary<string, (Vector3 Before, Vector3 After, Vector2 Size)> Spots = new();
    public bool Rooms = true, Sites = true;
    public string Only;

    /// <summary>A demo room: id, set, size, doorways, what it shows, and the room recipe it uses.</summary>
    public record Demo(string Id, string Set, float W, float D, DecoDoor[] Doors, string Shows, string Recipe = null, DecoTech? Tech = null, bool Exposed = false);

    public static readonly Demo[] Demos =
    {
        new("office", "office", 10, 8, new[] { new DecoDoor(DecoWall.South, 2, 1.4f), new DecoDoor(DecoWall.East, 6, 1.4f) }, "desk stations, a meeting table"),
        new("canteen", "canteen", 12, 9, new[] { new DecoDoor(DecoWall.North, 6, 2f) }, "serving line, table groups with trays"),
        new("ward", "medical", 12, 8, new[] { new DecoDoor(DecoWall.West, 4, 1.6f), new DecoDoor(DecoWall.East, 4, 1.6f) }, "ward bays: bed, cabinet, IV stand, curtain"),
        new("armoury", "military", 10, 8, new[] { new DecoDoor(DecoWall.South, 5, 1.4f) }, "rack rows, cots, map table"),
        new("lab", "lab", 10, 8, new[] { new DecoDoor(DecoWall.North, 5, 1.4f) }, "lab stations, specimen tank"),
        new("cryo-bay", "cryo", 12, 7, new[] { new DecoDoor(DecoWall.West, 3.5f, 1.6f) }, "pod rows with consoles", Tech: DecoTech.High),
        new("terminal", "spaceport", 14, 10, new[] { new DecoDoor(DecoWall.South, 7, 2.4f), new DecoDoor(DecoWall.East, 3, 2f) }, "seat rows facing the board, check-in line"),
        new("chapel", "civic", 10, 12, new[] { new DecoDoor(DecoWall.South, 5, 2f) }, "pews facing the altar"),
        new("quarters", "residential", 10, 8, new[] { new DecoDoor(DecoWall.North, 2, 1.2f) }, "lounge group, beds"),
        new("bar", "resort", 12, 8, new[] { new DecoDoor(DecoWall.South, 9, 2f) }, "bar line with stools, cafe tables"),
        new("cellblock", "prison", 12, 7, new[] { new DecoDoor(DecoWall.West, 3.5f, 1.4f) }, "cell bays: bunk and toilet"),
        new("bridge", "ship", 10, 7, new[] { new DecoDoor(DecoWall.South, 5, 1.4f) }, "console stations, lockers, star chart", Tech: DecoTech.High),
        new("assembly-hall", "industrial", 16, 12, new[] { new DecoDoor(DecoWall.West, 6, 2.4f), new DecoDoor(DecoWall.East, 6, 2.4f) }, "an assembly line frozen mid-production", "assembly-hall"),
        new("warehouse", "industrial", 14, 10, new[] { new DecoDoor(DecoWall.South, 7, 3f) }, "a warehouse aisle of pallet racking", "warehouse"),
        new("barn", "farm", 12, 10, new[] { new DecoDoor(DecoWall.South, 6, 3f) }, "a barn: hay, sacks, tools, a cart", "barn", DecoTech.Low),
        new("feed-store", "farm", 10, 8, new[] { new DecoDoor(DecoWall.North, 5, 1.6f) }, "a feed store", "feed-store", DecoTech.Low),
        new("greenhouse", "farm", 12, 8, new[] { new DecoDoor(DecoWall.West, 4, 1.6f), new DecoDoor(DecoWall.East, 4, 1.6f) }, "greenhouse rows", "greenhouse", DecoTech.High),
        new("mine-gallery", "mine", 10, 8, new[] { new DecoDoor(DecoWall.North, 5, 2f) }, "crate stacks, drill, ore cart", Tech: DecoTech.Low),
        new("camp", "reaver", 10, 10, new[] { new DecoDoor(DecoWall.East, 5, 2f) }, "a gang camp round burn barrels", Tech: DecoTech.Low, Exposed: true),
        new("shrine", "alien", 10, 10, new[] { new DecoDoor(DecoWall.South, 5, 2f) }, "an altar circle"),
        new("vault", "precursor", 10, 10, new[] { new DecoDoor(DecoWall.South, 5, 2f) }, "a glyph plinth and its wardens"),
    };

    public static readonly (string Site, DecoTech Tech)[] SiteDemos =
    {
        ("evacuation-jam", DecoTech.Mid), ("car-park", DecoTech.Mid), ("roadblock", DecoTech.Mid), ("pile-up", DecoTech.Mid),
        ("depot-yard", DecoTech.Mid), ("loading-dock", DecoTech.Mid), ("airliner-crash", DecoTech.Mid), ("airliner-gate", DecoTech.Mid),
        ("derelict-train", DecoTech.Low), ("derailed-train", DecoTech.Mid), ("station-platform", DecoTech.High), ("rail-depot", DecoTech.Low),
    };

    Node3D _content;

    public override void _Ready() => Build();

    public static DecoRoom DemoRoom(Demo d, ulong seed, string state) => new()
    {
        Width = d.W, Depth = d.D, RoomId = d.Recipe ?? d.Id, Set = d.Set, Doors = d.Doors.ToList(), Tech = d.Tech, Exposed = d.Exposed,
        Seed = seed + (ulong)d.Id.Length * 131 + DecoHash.Fnv(d.Id) % 1000, State = state,
    };

    public void Build()
    {
        _content?.QueueFree();
        _content = new Node3D { Name = "Content" };
        AddChild(_content);
        Spots.Clear();
        float z = 0;
        if (Rooms && Demos.Any(d => Only == null || Only == d.Id))
        {
            Showroom.Text(_content, $"ROOMS: AS USED / AS FOUND ({State.ToUpperInvariant()}, A CENTURY ON)", new Vector3(-1, 0.02f, z - 3f), 1.0f, flat: true, left: true);
            int col = 0;
            float rowDepth = 0;
            foreach (Demo d in Demos)
            {
                if (Only != null && Only != d.Id) continue;
                DecoRoom room = DemoRoom(d, Seed, "active");
                room.Age = DecoAge.Fresh;
                DecoPlan before = Poi.Dress(room, disorder: false);
                foreach (DecoPlacement pl in before.Placements) { pl.Powered = true; pl.Age = DecoAge.Fresh; }
                DecoPlan after = Poi.Dress(DemoRoom(d, Seed, State));
                float x = -(col * 2 * (14 + 4) + 2);
                var pb = new Vector3(x - d.W, 0, z);
                var pa = new Vector3(x - 2 * d.W - 3, 0, z);
                Show(before, pb, $"{d.Id}: {d.Shows}", "as used");
                Show(after, pa, "", $"as found: {State}");
                Spots[d.Id] = (pb, pa, new Vector2(d.W, d.D));
                rowDepth = Math.Max(rowDepth, d.D);
                if (++col == 3) { col = 0; z += rowDepth + 6; rowDepth = 0; }
            }
            z += rowDepth + 12;
        }
        if (Sites && SiteDemos.Any(s => Only == null || Only == s.Site))
        {
            Showroom.Text(_content, "SITES: IN USE / ABANDONED A CENTURY OR TWO", new Vector3(-1, 0.02f, z - 3f), 1.4f, flat: true, left: true);
            float x = -2;
            float rowDepth = 0;
            int col = 0;
            foreach (var (site, tech) in SiteDemos)
            {
                if (Only != null && Only != site) continue;
                DecoRoom fresh = Poi.SiteRoom(site, Seed + DecoHash.Fnv(site) % 10000, tech, "active");
                fresh.Age = DecoAge.Fresh;
                DecoRoom old = Poi.SiteRoom(site, fresh.Seed, tech, State);
                DecoPlan before = Poi.DressSite(fresh), after = Poi.DressSite(old);
                var pb = new Vector3(x - fresh.Width, 0, z);
                var pa = new Vector3(x - 2 * fresh.Width - 4, 0, z);
                Show(before, pb, $"{site} ({tech.ToString().ToLowerInvariant()} tech)", "in use", site: true);
                Show(after, pa, "", "abandoned", site: true);
                Spots[site] = (pb, pa, new Vector2(fresh.Width, fresh.Depth));
                x -= 2 * fresh.Width + 14;
                rowDepth = Math.Max(rowDepth, fresh.Depth);
                if (++col == 2) { col = 0; x = -2; z += rowDepth + 14; rowDepth = 0; }
            }
        }
    }

    void Show(DecoPlan plan, Vector3 at, string title, string caption, bool site = false)
    {
        var holder = new Node3D { Position = at };
        _content.AddChild(holder);
        DecoRoom room = plan.Room;
        holder.AddChild(DecoPlanView.Create(Meshes, plan, collide: !site, lights: !site));
        Showroom.Floor(holder, new Vector3(room.Width / 2, 0, room.Depth / 2), new Vector2(room.Width, room.Depth), site ? GroundMaterial : null);
        if (!site) Walls(holder, room);
        else Lane(holder, room);
        if (title.Length > 0) Showroom.Text(holder, title.ToUpperInvariant(), new Vector3(room.Width, 0.02f, -1.4f), site ? 1.1f : 0.55f, flat: true, left: true);
        Showroom.Text(holder, caption, new Vector3(room.Width / 2, 0.02f, room.Depth + 0.9f), site ? 1.0f : 0.45f, flat: true);
    }

    static StandardMaterial3D _wall, _lane, _ground;
    static StandardMaterial3D WallMaterial => _wall ??= new StandardMaterial3D { AlbedoColor = new Color("6e6862"), Roughness = 0.95f };
    static StandardMaterial3D LaneMaterial => _lane ??= new StandardMaterial3D { AlbedoColor = new Color("3a3836"), Roughness = 0.95f };
    public static StandardMaterial3D GroundMaterial => _ground ??= new StandardMaterial3D { AlbedoColor = new Color("6e6048"), Roughness = 1f };

    /// <summary>Low walls round the room (so the camera sees in), with gaps at the doorways.</summary>
    static void Walls(Node3D holder, DecoRoom room)
    {
        const float h = 0.6f, t = 0.2f;
        foreach (DecoWall w in new[] { DecoWall.North, DecoWall.South, DecoWall.West, DecoWall.East })
        {
            float len = w is DecoWall.North or DecoWall.South ? room.Width : room.Depth;
            var gaps = room.Doors.Where(d => d.Wall == w).Select(d => (lo: d.Offset - d.Width / 2, hi: d.Offset + d.Width / 2)).OrderBy(g => g.lo).ToList();
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
                    holder.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = c, MaterialOverride = WallMaterial });
                }
                s = g.hi;
            }
        }
    }

    /// <summary>The clear lane a site keeps, drawn on the ground between its two ends.</summary>
    static void Lane(Node3D holder, DecoRoom room)
    {
        if (room.Doors.Count < 2) return;
        var (a, _) = room.DoorPoint(room.Doors[0]);
        var (b, _) = room.DoorPoint(room.Doors[1]);
        bool alongZ = room.Doors[0].Wall is DecoWall.North or DecoWall.South;
        float w = room.Agent;
        Vector3 size = alongZ ? new Vector3(w, 0.02f, room.Depth) : new Vector3(room.Width, 0.02f, w);
        holder.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, Position = new Vector3((a.X + b.X) / 2, 0.01f, (a.Y + b.Y) / 2), MaterialOverride = LaneMaterial });
    }

    public (Vector3, Vector3) Overview() => (GlobalPosition + new Vector3(-60, 70, -40), GlobalPosition + new Vector3(-60, 0, 30));

    /// <summary>A camera over one demo: both copies side by side.</summary>
    public (Vector3, Vector3) View(string id)
    {
        var (before, after, size) = Spots[id];
        Vector3 mid = GlobalPosition + (before + after) / 2 + new Vector3(size.X / 2, 0, size.Y / 2);
        float span = MathF.Max(size.X * 2 + 4, size.Y * 1.15f);
        return (mid + new Vector3(0, span * 0.5f, -span * 0.42f), mid + new Vector3(0, 0, size.Y * 0.06f));
    }

    /// <summary>One copy of a demo up close; a long site from its near end, looking down the road or line.</summary>
    public (Vector3, Vector3) Close(string id, bool after)
    {
        var (before, aft, size) = Spots[id];
        Vector3 c = GlobalPosition + (after ? aft : before) + new Vector3(size.X / 2, 0, size.Y / 2);
        if (size.Y > size.X * 1.4f)
            return (c + new Vector3(0, size.X * 0.45f + 5, -size.Y * 0.5f - 3), c + new Vector3(0, 0, size.Y * 0.08f));
        float span = MathF.Max(size.X, size.Y);
        return (c + new Vector3(0, span * 0.62f, -span * 0.55f), c + new Vector3(0, 0, span * 0.05f));
    }

    public void Reseed() { Seed = Seed * 6364136223846793005UL + 1442695040888963407UL; Build(); }

    public void SetState(int s)
    {
        State = new[] { "active", "abandoned", "infested", "ruined" }[Math.Clamp(s, 0, 3)];
        Build();
    }
}
