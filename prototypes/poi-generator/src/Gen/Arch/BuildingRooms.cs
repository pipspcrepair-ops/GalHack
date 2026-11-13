#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>Rooms and features of a building: purposes, doors, furniture, keycards, breaches, caverns, roofs.</summary>
public sealed partial class Building
{
    // ---- purposes ------------------------------------------------------------------------

    IEnumerable<string> BandKeys(int s)
    {
        yield return "any";
        if (s == 0) yield return "ground";
        if (s >= 0) yield return "surface";
        if (s > 0 && s < Top) yield return "upper";
        if (s > 0 && s == Top) yield return "top";
        if (s > 0) yield return "above";
        if (s < 0)
        {
            yield return "basement";
            if (s == -1) yield return "shallow";
            if (s <= Bottom / 2 && s < -1) yield return "deep";
            if (s == Bottom) yield return "deepest";
        }
        else if (Levels == 0 && s == Top) yield return "deepest";
    }

    void AssignAll()
    {
        foreach (var plan in _plans.Values.Where(p => p.Pattern != "hut").OrderBy(p => p.S))
            Assign(plan.S, plan.Rooms.Where(r => !r.Secret && r.Def == null).ToList());
        // The prize: the deepest storey's room farthest from the stair becomes the vault.
        if (Spec.HoldsVault)
        {
            var deepest = _plans.Values.Where(p => p.Rooms.Any(r => !r.Secret)).OrderBy(p => p.S).FirstOrDefault();
            if (deepest != null)
            {
                var rooms = deepest.Rooms.Where(r => !r.Secret && !r.Hall).ToList();
                if (rooms.Count == 0) rooms = deepest.Rooms.Where(r => !r.Secret).ToList();
                var far = rooms.OrderByDescending(r => Math.Abs(r.Mid.x - (Cx + 2)) + Math.Abs(r.Mid.z - Zs) + (r.Area >= 16 ? 4 : 0)).First();
                var vault = rooms.FirstOrDefault(r => r.Def?.Id == "vault");
                if (vault != null && vault != far) (vault.Def, far.Def) = (far.Def, vault.Def);
                else if (vault == null) far.Def = Lib.Room("vault");
            }
        }
        foreach (var plan in _plans.Values)
            foreach (var r in plan.Rooms)
            {
                var def = r.Def ?? Lib.Room("storage");
                r.Def ??= def;
                C.Retag(r.Module, r.Secret ? ModuleKind.Room : def.Kind, r.Purpose);
            }
        var vaultRoom = _plans.Values.SelectMany(p => p.Rooms).FirstOrDefault(r => !r.Secret && r.Def?.Id == "vault");
        if (vaultRoom != null) Out.VaultModule = vaultRoom.Module;
        var bottom = _plans.Values.Where(p => p.Rooms.Any(r => !r.Secret)).OrderBy(p => p.S).FirstOrDefault();
        Out.DeepestModule = vaultRoom?.Module ?? bottom?.Rooms.First(r => !r.Secret).Module ?? _plans[Bottom].Corridor;
    }

    /// <summary>
    /// Gives each room a purpose: musts first to their best spot, then optional rooms, then
    /// fillers; then swaps any two rooms while that improves the plan's score.
    /// </summary>
    void Assign(int s, List<RoomPlan> rooms)
    {
        if (rooms.Count == 0) return;
        var musts = new List<RoomDef>();
        var optional = new List<RoomDef>();
        foreach (string key in BandKeys(s))
        {
            if (!P.Bands.TryGetValue(key, out var demands)) continue;
            foreach (var d in demands)
            {
                if (d.Room == "vault" && !Spec.HoldsVault) continue;
                var def = Lib.Room(d.Room);
                int n = Rng.Between(d.Min, d.Max);
                if (d.Must) { musts.Add(def); n--; }
                for (int i = 0; i < n; i++) optional.Add(def);
            }
        }
        Rng.Shuffle(optional);
        var wanted = musts.Concat(optional).ToList();
        var fill = P.Fill.Count > 0 ? P.Fill : new List<string> { "storage" };
        for (int i = 0; wanted.Count < rooms.Count; i++) wanted.Add(Lib.Room(fill[((i + s * 3 + 7) % fill.Count + fill.Count) % fill.Count]));
        if (wanted.Count > rooms.Count) wanted = wanted.Take(rooms.Count).ToList();

        foreach (var def in wanted)
        {
            RoomPlan? best = null;
            float bestScore = float.MinValue;
            foreach (var r in rooms.Where(r => r.Def == null))
            {
                float sc = Score(r, def);
                if (sc > bestScore) { bestScore = sc; best = r; }
            }
            if (best != null) best.Def = def;
        }
        for (int pass = 0; pass < 4; pass++)
        {
            bool improved = false;
            for (int i = 0; i < rooms.Count; i++)
                for (int j = i + 1; j < rooms.Count; j++)
                {
                    var a = rooms[i];
                    var b = rooms[j];
                    if (a.Def == b.Def) continue;
                    float before = Score(a, a.Def!) + Score(b, b.Def!);
                    (a.Def, b.Def) = (b.Def, a.Def);
                    float after = Score(a, a.Def!) + Score(b, b.Def!);
                    if (after > before + 0.01f) improved = true;
                    else (a.Def, b.Def) = (b.Def, a.Def);
                }
            if (!improved) break;
        }
    }

    static IEnumerable<RoomPlan> Neighbours(RoomPlan r) => r.Adj.Concat(r.Across).Where(n => !n.Secret).Distinct();

    /// <summary>How far a room stands from the middle of its storey's plan, as a share of the plan's half size (0 the middle, 1 its edge).</summary>
    float ToMiddle(RoomPlan r) => ToMiddle(r.Storey, r.Mid.x, r.Mid.z);

    float ToMiddle(int storey, float px, float pz)
    {
        var foot = _foot[storey];
        int x0 = W, x1 = 0, z0 = D, z1 = 0;
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
                if (foot[x, z]) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); z0 = Math.Min(z0, z); z1 = Math.Max(z1, z); }
        return Math.Max(Math.Abs(px - (x0 + x1) / 2f) / Math.Max(1f, (x1 - x0) / 2f), Math.Abs(pz - (z0 + z1) / 2f) / Math.Max(1f, (z1 - z0) / 2f));
    }

    float Score(RoomPlan r, RoomDef def)
    {
        float s = 0f;
        int w = Math.Max(r.Width, r.Depth);
        s += w >= def.WMin && w <= def.WMax + 2 ? 2f : -0.5f * (w < def.WMin ? def.WMin - w : w - def.WMax);
        // Its floor area as a room of its purpose has (data/rooms.txt "area", from the references): a cell block isn't a cupboard.
        if (def.AMax > 0)
        {
            float a = r.Area;
            s += a >= def.AMin * 0.75f && a <= def.AMax * 1.5f ? 2f : -6f * (a < def.AMin ? (def.AMin - a) / def.AMin : (a - def.AMax) / def.AMax);
        }
        if (def.Kind == ModuleKind.Hall && r.Area >= 60) s += 1.5f;
        float toCore = (Math.Abs(r.Mid.x - (Cx + 2)) + Math.Abs(r.Mid.z - Zs)) / (W + D), toEntrance = r.Mid.x / W;
        switch (def.Prefer)
        {
            case "entrance": s += Hut ? -toCore * 3f : -toEntrance * 4f; break;
            case "end": s += toCore * 3f + toEntrance * 1.5f; break;
            case "core": s -= toCore * 3f; break;
            case "middle": s -= ToMiddle(r) * 12f; break;
        }
        foreach (var n in Neighbours(r))
        {
            if (n.Def == null) continue;
            if (def.Near.Contains(n.Def.Id) || n.Def.Near.Contains(def.Id)) s += 1.5f;
            if (def.Avoid.Contains(n.Def.Id) || n.Def.Avoid.Contains(def.Id)) s -= 2.5f;
            if (n.Def == def && def.Has("cluster")) s += 1f;
        }
        return s;
    }

    void Notes(Plan plan)
    {
        var real = plan.Rooms.Where(r => !r.Secret && !r.Walled && r.Def != null).ToList();
        foreach (var r in real)
        {
            var def = r.Def!;
            var neighbours = Neighbours(r).Where(n => n.Def != null).ToList();
            bool possible = def.Near.Any(n => real.Any(o => o != r && o.Def!.Id == n));
            C.X.Rooms.Add(new RoomNote
            {
                Module = r.Module, Purpose = def.Id, Storey = plan.S,
                HasNearRule = def.Near.Count > 0 && possible,
                NearSatisfied = neighbours.Any(n => def.Near.Contains(n.Def!.Id) || n.Def!.Near.Contains(def.Id)),
                AvoidBroken = neighbours.Any(n => def.Avoid.Contains(n.Def!.Id)),
                Neighbours = neighbours.Select(n => n.Def!.Id).ToList(),
            });
        }
    }

    // ---- doors --------------------------------------------------------------------------

    static Dir DirOf((int dx, int dz) d) => d switch { (1, 0) => Dir.East, (-1, 0) => Dir.West, (0, 1) => Dir.South, _ => Dir.North };

    bool Solidish(Plan plan, int x, int z) => x < 0 || z < 0 || x >= W || z >= D || plan.K[x, z] is Wall or Ext or Solid or Core or OutK;

    /// <summary>
    /// Every place a two-wide door could go from a room through one wall cell: both door cells in the
    /// wall, the room on one side and the target (corridor, or another room) on the other, solid jambs.
    /// </summary>
    List<((int x, int z) a, (int x, int z) b, (int dx, int dz) d)> DoorSpots(Plan plan, RoomPlan r, Func<int, int, bool> target)
    {
        var list = new List<((int, int), (int, int), (int, int))>();
        var K = plan.K;
        foreach (var (x, z) in r.Cells)
            foreach (var d in Four)
            {
                int wx = x + d.dx, wz = z + d.dz, ox = x + 2 * d.dx, oz = z + 2 * d.dz;
                if (wx < 0 || wz < 0 || wx >= W || wz >= D || K[wx, wz] != Wall) continue;
                if (ox < 0 || oz < 0 || ox >= W || oz >= D || !target(ox, oz)) continue;
                var p = (dx: Math.Abs(d.dz), dz: Math.Abs(d.dx));
                int wx2 = wx + p.dx, wz2 = wz + p.dz;
                if (wx2 >= W || wz2 >= D || K[wx2, wz2] != Wall) continue;
                if (!r.Contains(x + p.dx, z + p.dz) || !target(ox + p.dx, oz + p.dz)) continue;
                if (!Solidish(plan, wx - p.dx, wz - p.dz) || !Solidish(plan, wx2 + p.dx, wz2 + p.dz)) continue;
                list.Add(((wx, wz), (wx2, wz2), d));
            }
        return list;
    }

    int MakeDoor(Plan plan, ((int x, int z) a, (int x, int z) b, (int dx, int dz) d) spot, int from, int to)
    {
        var cells = new List<Int3>();
        foreach (var (x, z) in new[] { spot.a, spot.b })
        {
            plan.K[x, z] = DoorK; // a doorway from now on: never a jamb, never another room's corridor
            for (int k = 0; k < DoorSize.Tall; k++) cells.Add(L(x, plan.Y + k, z));
            // The wall closes over the standard door, up to the storey's ceiling.
            for (int k = DoorSize.Tall; k < Pitch - 1; k++) C.Set(L(x, plan.Y + k, z), InnerWall(L(x, plan.Y + k, z)));
        }
        return C.AddDoorway(cells, R(DirOf(spot.d)), from, to);
    }

    /// <summary>
    /// Doors: each room onto its corridor where it touches one; rooms that don't (an enfilade,
    /// the back of a deep block) through the next room; anything still shut off becomes a hidden
    /// room behind a breakable wall, or is walled up. Rooms that belong together get a door between.
    /// </summary>
    void BuildDoors()
    {
        foreach (var plan in _plans.Values.Where(p => p.Pattern != "hut").OrderBy(p => p.S))
        {
            var K = plan.K;
            var reached = new HashSet<RoomPlan>();
            foreach (var r in plan.Rooms.Where(r => !r.Secret))
            {
                var spots = DoorSpots(plan, r, (x, z) => K[x, z] == Corr);
                if (spots.Count == 0) continue;
                var mid = r.Mid;
                var spot = spots.OrderBy(s => Math.Abs(s.a.x + 0.5f - mid.x) + Math.Abs(s.a.z + 0.5f - mid.z) + Rng.Float(0f, 1.5f)).First();
                r.Door = MakeDoor(plan, spot, plan.Corridor, r.Module);
                reached.Add(r);
            }
            // Room through room.
            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var r in plan.Rooms.Where(r => !r.Secret && !reached.Contains(r)))
                {
                    var from = r.Adj.Where(reached.Contains).OrderByDescending(n => n.Def != null && r.Def != null && (n.Def.Near.Contains(r.Def.Id) || r.Def.Near.Contains(n.Def.Id))).ToList();
                    foreach (var n in from)
                    {
                        var spots = DoorSpots(plan, r, (x, z) => plan.R[x, z] == plan.Rooms.IndexOf(n));
                        if (spots.Count == 0) continue;
                        r.Door = MakeDoor(plan, spots[spots.Count / 2], n.Module, r.Module);
                        reached.Add(r);
                        grew = true;
                        break;
                    }
                }
            }
            // Shut off: hidden if it can break through into a reached room, else filled in.
            foreach (var r in plan.Rooms.Where(r => !r.Secret && !r.Walled && !reached.Contains(r)).ToList())
            {
                if (r.Area >= 6 && r.Adj.Any(reached.Contains)) { r.Secret = true; C.Retag(r.Module, ModuleKind.Room, r.Purpose); continue; }
                foreach (var (x, z) in r.Cells)
                {
                    K[x, z] = Solid;
                    for (int k = 0; k < 3; k++) C.Set(L(x, plan.Y + k, z), InnerWall(L(x, plan.Y + k, z)));
                }
                C.Retag(r.Module, ModuleKind.Room, "walled up");
                C.X.Story.Add(new StoryProp("walled up", L(r.X0, plan.Y, r.Z0), "A room on the plan that someone bricked up."));
                r.Walled = true; // kept in the list: the plan grid indexes rooms by position
                C.Modules[r.Module].Shape.Occupied.Clear();
            }
            // Rooms that belong together get a door between them, as well as their own.
            foreach (var r in plan.Rooms.Where(r => !r.Secret && r.Door >= 0 && r.Def?.Lock == null))
                foreach (var n in r.Adj.Where(n => !n.Secret && !n.Walled && n.Def != null && n.Def.Lock == null && r.Module < n.Module))
                {
                    if (!(r.Def!.Near.Contains(n.Def!.Id) || n.Def.Near.Contains(r.Def.Id)) || !Rng.Chance(0.6f)) continue;
                    var spots = DoorSpots(plan, r, (x, z) => plan.R[x, z] == plan.Rooms.IndexOf(n));
                    if (spots.Count > 0) MakeDoor(plan, spots[spots.Count / 2], r.Module, n.Module);
                }
            foreach (var r in plan.Rooms.Where(r => r.Secret)) r.SecretFrom = r.Adj.Where(n => !n.Secret && reached.Contains(n)).OrderByDescending(n => n.Area).FirstOrDefault();
        }
    }

    /// <summary>
    /// A hidden room: its wall to a reachable neighbour is a breakable section with tells on the
    /// near side (cracks, a draft, scuffs on the floor in front, and the plan's own gap).
    /// </summary>
    void Hidden(RoomPlan r, Plan plan)
    {
        var from = r.SecretFrom;
        if (from == null || from.Module < 0)
        {
            // Nothing to break in from: wall it up.
            foreach (var (x, z) in r.Cells)
                for (int k = 0; k < 3; k++) C.Set(L(x, plan.Y + k, z), InnerWall(L(x, plan.Y + k, z)));
            C.Modules[r.Module].Shape.Occupied.Clear();
            r.Secret = false;
            r.Module = -1;
            return;
        }
        int fromIndex = plan.Rooms.IndexOf(from);
        var spots = DoorSpots(plan, r, (x, z) => plan.R[x, z] == fromIndex);
        if (spots.Count == 0)
        {
            foreach (var (x, z) in r.Cells)
                for (int k = 0; k < 3; k++) C.Set(L(x, plan.Y + k, z), InnerWall(L(x, plan.Y + k, z)));
            C.Modules[r.Module].Shape.Occupied.Clear();
            r.Module = -1;
            return;
        }
        var spot = spots[spots.Count / 2];
        int y = plan.Y;
        var secret = new SecretRoom { Module = r.Module, FromModule = from.Module, Facing = R(DirOf(spot.d)) };
        int v = 0;
        foreach (var (x, z) in new[] { spot.a, spot.b })
            for (int k = 0; k < 3; k++)
            {
                Int3 p = L(x, y + k, z);
                var b = S.InnerWall.Make(p);
                b.Type = BlockType.Breakable;
                b.Role = Role.Secret;
                b.Color = b.Color.Scale(0.9f).Lerp(new Rgb(0x8a, 0x7a, 0x60), 0.12f);
                b.Variant = (byte)(1 + v++);
                C.Set(p, b);
                secret.WallCells.Add(p);
            }
        // Tells in front of it, on the near side.
        var (dx, dz) = spot.d;
        Int3 front = L(spot.a.x + dx, y, spot.a.z + dz), front2 = L(spot.b.x + dx, y, spot.b.z + dz);
        secret.TellCells.Add(front);
        secret.TellCells.Add(front2);
        secret.Tells.Add("cracked wall panel, a shade off the rest");
        secret.Tells.Add("a draft through the cracks");
        secret.Tells.Add("scuff marks on the floor in front");
        secret.Tells.Add("the plan has a gap here no door explains");
        C.Set(front, new Block(BlockType.Scorch, Material.Ash, new Rgb(0x3a, 0x30, 0x28), Role.Story, Dir.North, 2));
        C.X.Sounds.Add(new SoundHook("draft", front2 + Int3.Up, 0.6f, r.Storey));
        C.X.Secrets.Add(secret);
        C.Link(from.Module, r.Module);
        // Inside: whoever hid here left their stash, and their bedroll.
        var inside = r.Cells.OrderByDescending(c => Math.Abs(c.x - spot.a.x) + Math.Abs(c.z - spot.a.z)).ToList();
        C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = L(inside[0].x, y, inside[0].z), Module = r.Module, Sure = true, Secret = true });
        if (inside.Count > 3) C.Set(L(inside[2].x, y, inside[2].z), new Block(BlockType.Scatter, Material.Cloth, new Rgb(0x6a, 0x5a, 0x40), Role.Story));
        C.X.Story.Add(new StoryProp("hidden stash", L(inside[0].x, y, inside[0].z), "Someone walled this space off and kept their best things here."));
    }

    /// <summary>The vault must have a door: if its room was walled up, the next deepest room with a door becomes it.</summary>
    void SeatVault()
    {
        if (!Spec.HoldsVault) return;
        var rooms = _plans.Values.SelectMany(p => p.Rooms).Where(r => !r.Secret && r.Module >= 0 && r.Door >= 0).ToList();
        if (rooms.Any(r => r.Def?.Id == "vault")) return;
        var pick = rooms.OrderBy(r => r.Storey).ThenByDescending(r => Math.Abs(r.Mid.x - (Cx + 2)) + Math.Abs(r.Mid.z - Zs)).FirstOrDefault();
        if (pick == null) return;
        pick.Def = Lib.Room("vault");
        C.Retag(pick.Module, ModuleKind.Vault, "vault");
        Out.VaultModule = pick.Module;
        if (Out.DeepestModule < 0 || C.Modules[Out.DeepestModule].Shape.Occupied.Count == 0) Out.DeepestModule = pick.Module;
    }

    // ---- storey events ------------------------------------------------------------------

    /// <summary>
    /// A hole down to the floor below in some rooms: the slab gave way. Warned by cracks round
    /// its edge, inside the room, never in a doorway's path, and always with floor round it.
    /// </summary>
    void Holes()
    {
        foreach (var plan in _plans.Values.Where(p => p.S > Bottom && p.Pattern != "hut").OrderBy(p => p.S))
        {
            if (!Rng.Chance(0.3f + Fam.Ominous * 0.2f)) continue;
            var below = _plans.GetValueOrDefault(plan.S - 1);
            if (below == null) continue;
            foreach (var r in plan.Rooms.Where(r => !r.Secret && !r.Walled && r.Def?.Id != "vault" && r.Area >= 30).OrderBy(_ => Rng.Float()))
            {
                // A 2 x 2 patch with room all round it, over open floor below.
                var spot = r.Cells.FirstOrDefault(c =>
                {
                    for (int dz = -1; dz <= 2; dz++)
                        for (int dx = -1; dx <= 2; dx++)
                            if (!r.Contains(c.x + dx, c.z + dz)) return false;
                    for (int dz = 0; dz <= 1; dz++)
                        for (int dx = 0; dx <= 1; dx++)
                            if (below.K[c.x + dx, c.z + dz] != Free) return false; // over a room, never a corridor
                    return true;
                });
                if (spot == default) continue;
                var hz = new Hazard { Kind = "drop", Center = L(spot.x, plan.Y, spot.z), Radius = 1.5f, Strength = Pitch, Warning = "The floor has fallen through here: a drop to the storey below." };
                for (int dz = 0; dz <= 1; dz++)
                    for (int dx = 0; dx <= 1; dx++)
                    {
                        Int3 p = L(spot.x + dx, plan.Y - 1, spot.z + dz);
                        C.Clear(p);
                        plan.Holes.Add((spot.x + dx, spot.z + dz));
                        hz.Cells.Add(p + Int3.Up);
                    }
                // Cracks round the edge: the warning you see before you're on it.
                for (int dz = -1; dz <= 2; dz++)
                    for (int dx = -1; dx <= 2; dx++)
                    {
                        Int3 p = L(spot.x + dx, plan.Y - 1, spot.z + dz);
                        var b = C.Get(p);
                        if (b.IsAir) continue;
                        b.Color = b.Color.Lerp(new Rgb(0x1a, 0x18, 0x16), 0.55f);
                        C.Set(p, b);
                    }
                C.X.Hazards.Add(hz);
                C.X.Sounds.Add(new SoundHook("creak", L(spot.x, plan.Y + 1, spot.z), 0.5f, plan.S));
                C.X.Story.Add(new StoryProp("fallen floor", L(spot.x, plan.Y, spot.z), "The floor gave way here. You can see the room below."));
                break;
            }
        }
    }

    // ---- furniture -----------------------------------------------------------------------

    static readonly Rgb Wood = new(0x6a, 0x4e, 0x36), CrateBrown = new(0x6e, 0x5a, 0x40), Linen = new(0xd6, 0xd8, 0xd2), Cryo = new(0x8f, 0xd8, 0xff);

    Block Prop(string kind, Int3 p, Dir facing)
    {
        Rgb accent = S.AccentColor, machine = S.Machine.Colors[0];
        return kind switch
        {
            "bench" => new Block(BlockType.Bench, Material.Alloy, machine.Lerp(new Rgb(0xb8, 0xbc, 0xc0), 0.4f), Role.Prop, facing),
            "machine" => new Block(BlockType.Machine, Material.Alloy, machine, Role.Prop, facing),
            "shelf" => new Block(BlockType.Shelf, Material.Steel, machine.Scale(0.8f), Role.Prop, facing),
            "locker" => new Block(BlockType.Locker, Material.Steel, accent.Lerp(new Rgb(0x50, 0x58, 0x60), 0.6f), Role.Prop, facing),
            "crate" => new Block(BlockType.Crate, Material.Scrap, CrateBrown.Lerp(accent, 0.2f), Role.Prop, facing),
            "table" => new Block(BlockType.Table, Material.Timber, Wood, Role.Prop, facing),
            "bunk" => new Block(BlockType.Bunk, Material.Cloth, (S.Kit.BySymbol('b')?.Colors[0]) ?? new Rgb(0x4f, 0x6d, 0x7a), Role.Prop, facing),
            "bed" => new Block(BlockType.Bed, Material.Cloth, Linen, Role.Prop, facing),
            "pod" => new Block(BlockType.Pod, Material.Lumen, Cryo, Role.Prop, facing),
            "tank" => new Block(BlockType.Tank, Material.Steel, machine.Lerp(accent, 0.25f), Role.Prop, facing),
            "statue" => new Block(BlockType.Statue, Material.Basalt, new Rgb(0x4a, 0x48, 0x50), Role.Prop, facing),
            "crystal" => new Block(BlockType.Crystal, Material.Crystal, new Rgb(0x80, 0xff, 0xe0), Role.Prop, facing),
            "cell" => new Block(BlockType.Lattice, Material.Steel, new Rgb(0x50, 0x54, 0x5a), Role.Prop, facing),
            "pillar" => new Block(BlockType.Pillar, Material.Concrete, S.Wall.Colors[0], Role.Prop, facing),
            "barricade" => new Block(BlockType.Barricade, Material.Timber, Wood.Scale(0.8f), Role.Story, facing),
            "plant" => new Block(BlockType.Plant, Material.Plant, new Rgb(0x4f, 0x8a, 0x3a), Role.Prop, facing),
            "planter" => new Block(BlockType.Crate, Material.Soil, new Rgb(0x4a, 0x3a, 0x2a), Role.Prop, facing),
            "screen" => new Block(BlockType.Pane, Material.Glass, new Rgb(0x9f, 0xd3, 0xe0), Role.Prop, facing),
            _ => new Block(BlockType.Crate, Material.Scrap, CrateBrown, Role.Prop, facing),
        };
    }

    string PickProp(RoomDef def)
    {
        if (def.Props.Count == 0) return "crate";
        int i = Rng.PickWeighted(def.Props.Select(p => p.Weight).ToList());
        return def.Props[Math.Max(0, i)].Prop;
    }

    void Furnish(RoomPlan r, Plan plan)
    {
        var def = r.Def ?? Lib.Room("storage");
        int y = plan.Y;
        var interior = r.Cells;
        var inRoom = new HashSet<(int, int)>(interior);
        // Keep clear in front of every doorway into the room (two cells deep), and by the hidden walls' tells.
        var keep = new HashSet<(int, int)>();
        foreach (var d in C.Doorways.Where(d => d.ModuleA == r.Module || d.ModuleB == r.Module))
            foreach (Int3 dc in d.Cells.Where(c => c.Y == y))
                foreach (var (x, z) in interior)
                    if (L(x, y, z).Manhattan(dc) <= 2) keep.Add((x, z));
        foreach (var s in C.X.Secrets.Where(s => s.FromModule == r.Module))
            foreach (Int3 t in s.TellCells)
                foreach (var (x, z) in interior)
                    if (L(x, y, z) == t) keep.Add((x, z));
        var blocked = new HashSet<(int, int)>();
        var needsAccess = new List<(int x, int z)>();
        // Whatever already stands here (a rubble slope from a collapse) is in the way; the foot of a slope stays clear.
        foreach (var (x, z) in interior)
        {
            if (plan.Holes.Contains((x, z))) { blocked.Add((x, z)); continue; }
            var b = C.Get(L(x, y, z));
            if (b.IsAir || !BlockInfo.Collides(b.Type)) continue;
            blocked.Add((x, z));
            if (BlockInfo.IsSlope(b.Type))
                foreach (var (fx, fz) in interior)
                    if (L(fx, y, fz) == L(x, y, z) - b.Facing.Vec()) keep.Add((fx, fz));
        }
        bool Perimeter((int x, int z) c) => Four.Any(d => !inRoom.Contains((c.x + d.dx, c.z + d.dz)));
        Dir InwardOf((int x, int z) c)
        {
            foreach (var d in Four)
                if (!inRoom.Contains((c.x - d.dx, c.z - d.dz)) && inRoom.Contains((c.x + d.dx, c.z + d.dz))) return R(DirOf(d));
            return R(Dir.North);
        }

        bool Connected()
        {
            var start = interior.FirstOrDefault(c => keep.Contains(c) && !blocked.Contains(c));
            if (start == default) start = interior.FirstOrDefault(c => !blocked.Contains(c));
            if (start == default) return false;
            var seen = new HashSet<(int, int)> { start };
            var q = new Queue<(int x, int z)>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var (x, z) = q.Dequeue();
                foreach (var (dx, dz) in Four)
                {
                    var n = (x + dx, z + dz);
                    if (inRoom.Contains(n) && !blocked.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            if (!interior.All(c => blocked.Contains(c) || seen.Contains(c))) return false;
            foreach (var (ax, az) in needsAccess)
                if (!new[] { (ax + 1, az), (ax - 1, az), (ax, az + 1), (ax, az - 1) }.Any(seen.Contains)) return false;
            return true;
        }

        bool Take((int x, int z) c)
        {
            if (keep.Contains(c) || blocked.Contains(c)) return false;
            blocked.Add(c);
            if (Connected()) return true;
            blocked.Remove(c);
            return false;
        }

        var perimeter = interior.Where(Perimeter).ToList();
        Rng.Shuffle(perimeter);
        var corners = perimeter.OrderByDescending(c => Four.Count(d => !inRoom.Contains((c.x + d.dx, c.z + d.dz)))).ToList();

        int loot = Rng.Between(def.LootMin, def.LootMax) + (def.Id == "vault" ? Math.Min(3, (int)Rarity) : 0);
        foreach (var c in corners)
        {
            if (loot <= 0) break;
            needsAccess.Add(c);
            if (!Take(c)) { needsAccess.Remove(c); continue; }
            C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
            loot--;
        }
        int terms = Rng.Between(def.TerminalMin, def.TerminalMax);
        foreach (var c in perimeter)
        {
            if (terms <= 0) break;
            needsAccess.Add(c);
            if (!Take(c)) { needsAccess.Remove(c); continue; }
            C.Slots.Add(new Slot { Kind = SlotKind.Terminal, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
            terms--;
        }
        // A program with a core vault keeps its core there (rule E5, owner 13 Nov), whatever other core room came first.
        bool coreVault = def.Id == "core-vault";
        if (def.Has("core") && Fam.Core == CoreRule.Owned && (C.CoreModule < 0 || coreVault) && Spec.MayHoldCore)
        {
            if (coreVault) C.Slots.RemoveAll(s => s.Kind == SlotKind.Core);
            // (In a core vault, the spot nearest the middle of the plan: the core sits deep in the base.)
            var spot = interior.Where(c => !Perimeter(c) && !keep.Contains(c)).OrderBy(c => coreVault ? ToMiddle(r.Storey, c.x, c.z) : Math.Abs(c.x - r.Mid.x) + Math.Abs(c.z - r.Mid.z)).FirstOrDefault();
            if (spot != default) needsAccess.Add(spot);
            if (spot != default && Take(spot))
            {
                C.Slots.Add(new Slot { Kind = SlotKind.Core, Cell = L(spot.x, y, spot.z), Module = r.Module, Sure = true });
                C.CoreModule = r.Module;
            }
        }
        foreach (var c in perimeter)
        {
            if (blocked.Contains(c) || !Rng.Chance(def.Fill)) continue;
            if (!Take(c)) continue;
            string prop = PickProp(def);
            Int3 p = L(c.x, y, c.z);
            C.Set(p, Prop(prop, p, InwardOf(c)));
            if (prop is "shelf" or "tank" && Rng.Chance(0.5f)) C.Set(p + Int3.Up, Prop(prop == "shelf" ? "crate" : "tank", p + Int3.Up, InwardOf(c)));
            if (prop == "planter") C.Set(p + Int3.Up, Prop("plant", p + Int3.Up, Dir.North));
        }
        // Islands: rows of pods or beds with aisles, tables in the middle.
        if (def.Island.Count > 0 && r.Width >= 6 && r.Depth >= 5)
        {
            string island = def.Island[Rng.Range(0, def.Island.Count)];
            foreach (var c in interior.OrderBy(c => c.z).ThenBy(c => c.x))
            {
                bool deep = true;
                for (int dz = -1; dz <= 1 && deep; dz++)
                    for (int dx = -1; dx <= 1 && deep; dx++)
                        if (!inRoom.Contains((c.x + dx, c.z + dz))) deep = false;
                if (!deep || (c.z - r.Z0) % 3 == 0 || (c.x - r.X0) % 4 == 0) continue;
                if (!Take(c)) continue;
                Int3 p = L(c.x, y, c.z);
                C.Set(p, Prop(island, p, R((c.z - r.Z0) % 3 == 1 ? Dir.North : Dir.South)));
            }
        }
        var free = interior.Where(c => !blocked.Contains(c) && !keep.Contains(c)).ToList();
        Rng.Shuffle(free);
        int spawns = Rng.Between(def.SpawnMin, def.SpawnMax);
        foreach (var c in free.Take(spawns)) C.Slots.Add(new Slot { Kind = SlotKind.Spawn, Cell = L(c.x, y, c.z), Module = r.Module, Sure = true });
        var storySpot = free.Skip(spawns).FirstOrDefault(Perimeter);
        if (storySpot != default) C.Slots.Add(new Slot { Kind = SlotKind.Prop, Cell = L(storySpot.x, y, storySpot.z), Module = r.Module, Sure = true });
        if (def.Kind is ModuleKind.Hall or ModuleKind.Vault && free.Count > spawns + 2)
            C.Slots.Add(new Slot { Kind = SlotKind.Boss, Cell = L(free[^1].x, y, free[^1].z), Module = r.Module, Sure = true });

        if (def.Light != "none")
        {
            // A sconce in a wall beside the room, and a lamp for the view.
            foreach (var c in perimeter)
            {
                bool done = false;
                foreach (var d in Four)
                {
                    if (inRoom.Contains((c.x + d.dx, c.z + d.dz))) continue;
                    Int3 sc = L(c.x + d.dx, y + 2, c.z + d.dz);
                    var sb = C.Get(sc);
                    if (sb.Type == BlockType.Cube && sb.Role == Role.Wall && C.Occ.Contains(L(c.x + 2 * d.dx, y + 2, c.z + 2 * d.dz))) { C.Set(sc, S.Light.Make(sc)); done = true; break; }
                }
                if (done) break;
            }
            C.X.Lights.Add(new LightSpot
            {
                Cell = L((int)r.Mid.x, y + 2, (int)r.Mid.z), Color = LightColor(def.Light), Module = r.Module, Storey = r.Storey,
                Mode = def.Light == "red" ? LightMode.Emergency : def.Light == "glyph" ? LightMode.Pulse : LightMode.Steady,
                Energy = def.Light == "dim" ? 0.8f : 1.3f, Range = Math.Max(r.Width, r.Depth) * 0.9f + 2f,
            });
        }
        if (def.Has("hum")) C.X.Sounds.Add(new SoundHook("hum", L((int)r.Mid.x, y + 1, (int)r.Mid.z), 0.8f, r.Storey));
    }

    Rgb LightColor(string kind) => kind switch
    {
        "cold" => new Rgb(0xe6, 0xf2, 0xff),
        "dim" => new Rgb(0xc8, 0xb0, 0x90),
        "red" => new Rgb(0xff, 0x30, 0x20),
        "green" => new Rgb(0x9c, 0xff, 0x8a),
        "glyph" => new Rgb(0x80, 0xff, 0xe0),
        "cryo" => new Rgb(0x8f, 0xd8, 0xff),
        _ => S.Kit.Light,
    };

    // ---- keycards ----------------------------------------------------------------------

    static readonly string[] Clearances = { "staff", "science", "security", "command" };

    /// <summary>
    /// Secure rooms get locked doors; each clearance's keycard lies in a room you can reach with
    /// every lock still shut, on the same storey or above, where someone would have carried it.
    /// </summary>
    void Locks()
    {
        var all = _plans.Values.SelectMany(p => p.Rooms).Where(r => !r.Secret && r.Def != null && r.Door >= 0 && r.Module >= 0).ToList();
        var locked = new List<(RoomPlan Room, string Clearance)>();
        foreach (var r in all)
        {
            string? c = r.Def!.Lock;
            if (r.Def.Id == "vault") c ??= "command";
            if (c == null) continue;
            locked.Add((r, c));
            C.X.Locks.Add(new DoorLock { Doorway = r.Door, Clearance = c, Blast = r.Def.Blast });
        }
        if (locked.Count == 0) return;
        // What can be walked to with every lock shut.
        var lockedDoors = new HashSet<int>(locked.Select(l => l.Room.Door));
        var shut = new HashSet<(int, int)>(lockedDoors.Select(i => (C.Doorways[i].ModuleA, C.Doorways[i].ModuleB)));
        var adj = new Dictionary<int, List<int>>();
        foreach (var (a, b) in C.Edges)
        {
            if (shut.Contains((a, b)) || shut.Contains((b, a))) continue;
            (adj.TryGetValue(a, out var la) ? la : adj[a] = new List<int>()).Add(b);
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = new List<int>()).Add(a);
        }
        var open = new HashSet<int> { Out.EntranceModule };
        var q = new Queue<int>();
        q.Enqueue(Out.EntranceModule);
        while (q.Count > 0)
        {
            int m = q.Dequeue();
            if (!adj.TryGetValue(m, out var next)) continue;
            foreach (int n in next) if (open.Add(n)) q.Enqueue(n);
        }
        foreach (string clearance in Clearances)
        {
            var doors = locked.Where(l => l.Clearance == clearance).ToList();
            if (doors.Count == 0) continue;
            int shallowest = doors.Max(l => l.Room.Storey);
            var candidates = all.Where(r => open.Contains(r.Module) && !locked.Any(l => l.Room == r))
                .OrderByDescending(r => r.Storey >= shallowest ? 1 : 0).ThenByDescending(r => r.Def!.Has("keyspot") ? 2 : 0)
                .ThenBy(r => Math.Abs(r.Storey - shallowest)).ThenBy(_ => Rng.Float()).ToList();
            if (candidates.Count == 0) continue;
            foreach (var room in candidates)
            {
                var plan = _plans[room.Storey];
                int y = plan.Y;
                Int3? spot = null;
                foreach (var (x, z) in room.Cells.OrderBy(c => Rng.Float()))
                {
                    Int3 p = L(x, y, z);
                    if (C.Get(p).IsAir && !C.Get(p + Int3.Down).IsAir && !C.Slots.Any(s => s.Cell == p) && !C.Doorways.Any(d => d.Cells.Any(dc => dc.Manhattan(p) <= 2))) { spot = p; break; }
                }
                if (spot is not Int3 sp) continue;
                C.Set(sp, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x7a, 0x74, 0x64), Role.Story, R(Dir.North), 2));
                C.X.Keys.Add(new KeyCard { Clearance = clearance, Cell = sp, Module = room.Module, On = "remains" });
                C.X.Story.Add(new StoryProp("keycard", sp, $"A {clearance} keycard, still clipped to what's left of its owner."));
                break;
            }
        }
    }

    // ---- below ground ------------------------------------------------------------------

    /// <summary>
    /// The deepest storey breaks into a natural cave at a corridor's end against the outer wall:
    /// the plan stops making sense here. Flat-floored so it walks, with a ragged roof and walls.
    /// </summary>
    void Cavern()
    {
        var plan = _plans[Bottom];
        int y = plan.Y;
        var K = plan.K;
        // A corridor cell whose outward neighbour is the outer wall with rock beyond, as far from the stair as it gets.
        (int x, int z, int dx, int dz)? best = null;
        float far = -1f;
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (K[x, z] != Corr) continue;
                foreach (var (dx, dz) in Four)
                {
                    int wx = x + dx, wz = z + dz, ox = x + 2 * dx, oz = z + 2 * dz;
                    int px = Math.Abs(dz), pz = Math.Abs(dx);
                    if (wx < 0 || wz < 0 || wx >= W || wz >= D || K[wx, wz] != Ext) continue;
                    if (InFoot(Bottom, ox, oz) || x + px >= W || z + pz >= D || K[x + px, z + pz] != Corr || K[wx + px, wz + pz] != Ext) continue;
                    float dd = Math.Abs(x - Cx) + Math.Abs(z - Zs);
                    if (dd > far) { far = dd; best = (x, z, dx, dz); }
                }
            }
        if (best is not { } b) return;
        var (bx, bz, ddx, ddz) = b;
        int pX = Math.Abs(ddz), pZ = Math.Abs(ddx);
        var cells = new List<Int3>();
        // Break through the outer wall.
        for (int k = 0; k < 3; k++)
            for (int i = 0; i < 2; i++)
            {
                Int3 p = L(bx + ddx + pX * i, y + k, bz + ddz + pZ * i);
                C.Clear(p);
                cells.Add(p);
                if (k == 0) C.X.CorridorCells.Remove(p);
            }
        int len = Rng.Between(12, 20);
        float phase = Rng.Float(0f, 6.28f);
        for (int t = 2; t < len; t++)
        {
            float u = t / (float)len;
            int half = Math.Max(1, 2 + (int)(MathF.Sin(u * 3.1f) * 4f + MathF.Sin(phase + t * 0.7f) * 1.2f));
            int wobble = (int)MathF.Round(MathF.Sin(phase * 2 + t * 0.35f) * 2f);
            int height = 3 + (int)(MathF.Sin(u * 3.1f) * 5f) + (t % 3 == 0 ? 1 : 0);
            for (int side = -half + wobble; side <= 1 + half + wobble; side++)
                for (int k = 0; k < Math.Max(3, height); k++)
                {
                    int lx = bx + ddx * t + pX * side, lz = bz + ddz * t + pZ * side;
                    Int3 p = L(lx, y + k, lz);
                    if (C.Blocks.ContainsKey(p) || C.Occ.Contains(p)) continue;
                    C.Clear(p);
                    cells.Add(p);
                }
        }
        var floorCells = cells.Where(p => p.Y == y).ToList();
        int cave = C.AddModule(ModuleKind.Cave, "cavern", Bottom, cells.Distinct().ToList());
        Out.Modules.Add(cave);
        C.Link(plan.Corridor, cave);
        Int3 mouth = L(bx, y, bz);
        var farCells = floorCells.OrderByDescending(p => p.Manhattan(mouth)).ToList();
        C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = farCells[0], Module = cave, Sure = true });
        C.Slots.Add(new Slot { Kind = SlotKind.Boss, Cell = farCells[Math.Min(farCells.Count - 1, 6)], Module = cave, Sure = true });
        foreach (Int3 sp in farCells.Skip(3).Where((_, i) => i % 9 == 0).Take(3))
            C.Slots.Add(new Slot { Kind = SlotKind.Spawn, Cell = sp, Module = cave, Sure = true });
        C.X.Lights.Add(new LightSpot { Cell = farCells[0] + Int3.Up * 2, Color = new Rgb(0xb8, 0xf0, 0x60), Mode = LightMode.Bloom, Energy = 1.4f, Range = 10f, Module = cave, Storey = Bottom });
        C.X.Sounds.Add(new SoundHook("breathing", farCells[0] + Int3.Up, 0.7f, Bottom));
        C.X.Story.Add(new StoryProp("breakthrough", mouth, "The wall at the end of the corridor was dug out from the other side."));
        Out.DeepestModule = cave;
        if (Spec.HoldsVault && !_plans[Bottom].Rooms.Any(r => r.Def?.Id == "vault")) Out.VaultModule = cave;
    }

    /// <summary>
    /// The ground and the ceiling caved in over a room in the top basement: a hole in the bunker
    /// roof with a rubble slope down into the room, daylight and weather falling in.
    /// </summary>
    void CollapseBreach()
    {
        if (!_plans.TryGetValue(-1, out var plan)) return;
        int y = plan.Y, rise = -y;
        var hut = HutRect();
        foreach (var room in plan.Rooms.Where(r => !r.Secret && !r.Walled && r.Def?.Id != "vault").OrderBy(_ => Rng.Float()))
        {
            // A straight run along x, two wide, rise + 2 long, inside the room and clear of the stair house above.
            foreach (var (sx, sz) in room.Cells.OrderBy(_ => Rng.Float()))
            {
                bool ok = true;
                for (int i = -1; i <= rise + 1 && ok; i++)
                    for (int dz = 0; dz < 2 && ok; dz++)
                    {
                        int x = sx + i, z = sz + dz;
                        if (!room.Contains(x, z)) ok = false;
                        // never over a doorway's way in
                        Int3 at = L(x, y, z);
                        if (C.Doorways.Any(d => d.Cells.Any(c => c.Y == y && c.Manhattan(at) <= 2))) ok = false;
                        if (x >= hut.x0 - 2 && x <= hut.x1 + 2 && z >= hut.z0 - 2 && z <= hut.z1 + 2) ok = false;
                    }
                if (!ok) continue;
                Breach(room, plan, sx, sz, rise);
                return;
            }
        }
    }

    void Breach(RoomPlan room, Plan plan, int x0, int z0, int rise)
    {
        int y = plan.Y;
        var breach = new Breach { Module = room.Module };
        var rubble = new Rgb(0x6a, 0x60, 0x58);
        for (int i = 0; i < rise; i++)
            for (int dz = 0; dz < 2; dz++)
            {
                int x = x0 + i, z = z0 + dz;
                for (int k = 0; k < i; k++)
                {
                    Int3 fill = L(x, y + k, z);
                    C.Set(fill, new Block(BlockType.Cube, Material.Rock, rubble.Scale(0.85f + 0.1f * (k % 2)), Role.Debris));
                }
                Int3 ramp = L(x, y + i, z);
                C.Set(ramp, new Block(BlockType.Ramp, Material.Rock, rubble, Role.Debris, R(Dir.East)));
                for (int k = 1; k <= 3; k++)
                {
                    Int3 h = L(x, y + i + k, z);
                    if (h.Y < 0) C.Clear(h);
                    else C.Remove(h);
                    if (h.Y <= -1) breach.Cells.Add(h);
                }
            }
        // Widen the hole: the slab above the slope and beside it has fallen in.
        for (int x = x0 - 1; x < x0 + rise; x++)
            for (int dz = -1; dz <= 2; dz++)
            {
                int z = z0 + dz;
                if (!room.Contains(x, z)) continue;
                Int3 ceil = L(x, -1, z);
                if (!C.Get(ceil + Int3.Up).IsAir) continue;
                if (C.Get(ceil).Role is Role.Floor or Role.Roof)
                {
                    C.Clear(ceil);
                    breach.Cells.Add(ceil);
                }
            }
        // Cracked slab round the lip, rubble on the surface: a strange hole you can see from afar.
        for (int x = x0 - 3; x <= x0 + rise + 2; x++)
            for (int dz = -3; dz <= 4; dz++)
            {
                Int3 lip = L(x, -1, z0 + dz);
                var b = C.Get(lip);
                if (b.Role == Role.Floor && Hash.Cell(lip, 91) % 3 == 0)
                {
                    b.Color = b.Color.Lerp(new Rgb(0x2a, 0x24, 0x20), 0.5f);
                    C.Set(lip, b);
                }
                Int3 top = lip + Int3.Up;
                bool onSlope = dz is 0 or 1 && x >= x0 - 1 && x <= x0 + rise + 1;
                if (!onSlope && C.Get(top).IsAir && !C.Occ.Contains(top) && !C.Get(lip).IsAir && Hash.Cell(top, 92) % 5 == 0 && !breach.Cells.Contains(lip))
                    C.Set(top, new Block(BlockType.Rubble, Material.Rock, rubble, Role.Debris));
            }
        breach.SurfaceCell = L(x0 + rise, 0, z0);
        C.Remove(breach.SurfaceCell);
        C.Remove(breach.SurfaceCell + Int3.Up);
        C.X.Breaches.Add(breach);
        C.X.Sounds.Add(new SoundHook("wind", L(x0 + 1, y + 2, z0), 0.7f, -1));
        C.X.Story.Add(new StoryProp("collapse", L(x0, y, z0), "The roof came down here. Daylight and weather have been getting in ever since."));
    }

    // ---- the roof and the levels ----------------------------------------------------------

    void Roofscape()
    {
        if (Hut || Floors == 0) return;
        int y = WalkY(Top) + 4; // standing on the roof
        if (P.Roof == "pad")
        {
            // A landing pad painted on the roof: a ring and a cross.
            int cxr = Math.Min(Wm - 6, Wm * 3 / 4), czr = Zs + 4;
            for (int dz = -4; dz <= 4; dz++)
                for (int dx = -4; dx <= 4; dx++)
                {
                    int x = cxr + dx, z = czr + dz;
                    if (!InFoot(Top, x, z)) continue;
                    float r = MathF.Sqrt(dx * dx + dz * dz);
                    bool mark = (r > 3.2f && r < 4.3f) || (dx == 0 && Math.Abs(dz) <= 2) || (dz == 0 && Math.Abs(dx) <= 2);
                    if (!mark) continue;
                    Int3 p = L(x, y - 1, z);
                    if (C.Get(p).Role == Role.Roof) C.Set(p, new Block(BlockType.Cube, Material.Concrete, new Rgb(0xe8, 0xd0, 0x40), Role.Roof));
                }
            C.X.Markers.Add(new Marker("landing-pad", L(cxr, y, czr)));
        }
        if (P.Crown == "broken" && !(Fam.Style ?? "").StartsWith("alien_", StringComparison.Ordinal)) BrokenCrown(y);   // an alien cathedral grows a spire instead
        if (P.Crown == "broken" && !(Fam.Style ?? "").StartsWith("alien_", StringComparison.Ordinal)) TornEdges();
        // Plant on the roof: tanks, a mast with a beacon (a light you see from far off).
        // The mast stands where there is roof under it (a tower that shed a corner keeps it on what is left).
        var mastAt = new[] { (Wm - 3, Zm + 2), (2, Zm + 2), (Wm - 3, Zm + Dm - 3), (2, Zm + Dm - 3) }.FirstOrDefault(c => InFoot(Top, c.Item1, c.Item2) && !C.Get(L(c.Item1, y - 1, c.Item2)).IsAir, (-1, -1));
        if (mastAt.Item1 >= 0 && !(Fam.Style ?? "").StartsWith("alien_", StringComparison.Ordinal))   // no radio mast on an alien place
        {
            // On a torn tower the mast leans, stepping over every few metres as it climbs.
            Int3 mast = L(mastAt.Item1, y, mastAt.Item2);
            Int3 lean = P.Crown == "broken" ? L(mastAt.Item1 - 1, y, mastAt.Item2) - mast : new Int3(0, 0, 0);
            int tall = 6 + Math.Min(Floors, 12);
            var steel = new Block(BlockType.Pillar, Material.Steel, new Rgb(0x60, 0x64, 0x68), Role.Decor);
            for (int k = 0; k < tall; k++)
            {
                C.Set(mast + Int3.Up * k, steel);
                if (k > 3 && k % 4 == 0 && lean != new Int3(0, 0, 0)) { mast += lean; C.Set(mast + Int3.Up * k, steel); }
            }
            C.Set(mast + Int3.Up * tall, new Block(BlockType.Cube, Material.Lumen, new Rgb(0xff, 0x40, 0x30), Role.Light));
            C.X.Lights.Add(new LightSpot { Cell = mast + Int3.Up * tall, Color = new Rgb(0xff, 0x40, 0x30), Mode = LightMode.Beacon, Energy = 2f, Range = 14f, Storey = Top + 1 });
        }
        for (int i = 0; i < 2; i++)
        {
            Int3 t = L(3 + i * 2, y, Zm + Dm - 3);
            if (!InFoot(Top, 3 + i * 2, Zm + Dm - 3)) continue;
            C.Set(t, new Block(BlockType.Tank, Material.Steel, new Rgb(0x8a, 0x8e, 0x90), Role.Decor));
            C.Set(t + Int3.Up, new Block(BlockType.Tank, Material.Steel, new Rgb(0x8a, 0x8e, 0x90), Role.Decor));
        }
        Int3 ac = L(Wm / 2, y, Zm + Dm - 3);
        if (InFoot(Top, Wm / 2, Zm + Dm - 3)) C.Set(ac, new Block(BlockType.Machine, Material.Alloy, new Rgb(0x7a, 0x80, 0x88), Role.Decor));
    }

    /// <summary>Where the top storeys tore away: snapped columns and rebar standing on the exposed slabs below.</summary>
    void TornEdges()
    {
        var steel = new Rgb(0x4a, 0x4c, 0x50);
        for (int s = Math.Max(1, Top - 3); s <= Top; s++)
            for (int z = 0; z < D; z++)
                for (int x = 0; x < W; x++)
                {
                    if (InFoot(s, x, z) || !InFoot(s - 1, x, z)) continue;
                    bool edge = InFoot(s, x + 1, z) || InFoot(s, x - 1, z) || InFoot(s, x, z + 1) || InFoot(s, x, z - 1);
                    uint h = Hash.Cell(new Int3(x, s, z), 617);
                    if (!(edge && h % 3 == 0) && h % 11 != 0) continue;
                    Int3 at = L(x, WalkY(s), z);
                    if (C.Get(at - Int3.Up).IsAir || !C.Get(at).IsAir || C.Occ.Contains(at)) continue;
                    int tall = 1 + (int)(h % 3);
                    for (int k = 0; k < tall; k++) C.Set(at + Int3.Up * k, new Block(BlockType.Pillar, Material.Steel, steel, Role.Wall));
                }
    }

    /// <summary>
    /// The top torn off: over one side of the roof (cut on a slant), girders stand at ragged heights
    /// with stubs of slab still clinging to them, and the outer walls run on up in broken teeth. A
    /// skyline that reads as wrong from far off. Kept clear of the stair core and the roof pad.
    /// </summary>
    void BrokenCrown(int y)
    {
        var steel = new Rgb(0x4a, 0x4c, 0x50);
        float slope = Rng.Float(-0.8f, 0.8f), cut = Rng.Float(0.3f, 0.55f) * Wm;
        bool west = Rng.Chance(0.5f);
        bool Torn(int x, int z) => (west ? Wm - 1 - x : x) + slope * (z - Zm) > cut;
        bool Clear(int x, int z) => !(x >= Cx - 3 && x <= CoreEnd + 3 && z <= Zs + 4) && !(P.Roof == "pad" && Math.Abs(x - Math.Min(Wm - 6, Wm * 3 / 4)) <= 5 && Math.Abs(z - (Zs + 4)) <= 5)
                                      && !(x >= Wm - 4 && z <= Zm + 3);
        for (int z = 0; z < D; z++)
            for (int x = 0; x < W; x++)
            {
                if (!InFoot(Top, x, z) || !Torn(x, z) || !Clear(x, z)) continue;
                Int3 roof = L(x, y - 1, z);
                if (C.Get(roof).IsAir) continue;
                bool edge = !InFoot(Top, x - 1, z) || !InFoot(Top, x + 1, z) || !InFoot(Top, x, z - 1) || !InFoot(Top, x, z + 1);
                uint h = Hash.Cell(new Int3(x, Top, z), 501);
                if (x % 4 == 0 && z % 4 == 0)
                {
                    // A girder, taller the further into the torn side, with a stub of slab at its head.
                    int tall = 3 + (int)(h % 6) + (int)(((west ? Wm - 1 - x : x) - cut) / 4);
                    for (int k = 0; k < tall; k++) C.Set(L(x, y + k, z), new Block(BlockType.Pillar, Material.Steel, steel, Role.Wall));
                    if (h % 3 == 0)
                        foreach (var (dx, dz) in new[] { (1, 0), (2, 0), (1, 1) })
                            if (InFoot(Top, x + dx, z + dz)) C.Set(L(x + dx, y + tall - 1, z + dz), new Block(BlockType.Cube, Material.Concrete, S.Floor.Colors[0].Scale(0.8f), Role.Wall));
                }
                else if (edge && h % 4 != 0)
                {
                    // Broken teeth of the outer wall.
                    int tooth = 1 + (int)(h % 4);
                    for (int k = 0; k < tooth; k++) C.Set(L(x, y + k, z), S.Wall.Make(L(x, y + k, z)));
                }
            }
    }

    void Levelinfo()
    {
        for (int s = Bottom; s <= StairTop; s++)
        {
            if (C.X.Levels.Any(l => l.Storey == s)) continue;
            string name = s switch
            {
                0 => "ground floor",
                > 0 when s > Top => "roof",
                > 0 => $"floor {s}",
                _ => s == Bottom ? $"sublevel {-s} (deepest)" : $"sublevel {-s}",
            };
            C.X.Levels.Add(new LevelInfo { Storey = s, WalkY = WalkY(s), Name = name });
        }
        C.X.Levels.Sort((a, b) => a.Storey.CompareTo(b.Storey));
    }
}
