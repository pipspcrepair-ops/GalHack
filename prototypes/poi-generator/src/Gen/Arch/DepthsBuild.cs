#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PoiGenerator.Gen;

/// <summary>The plan turned into blocks on the player's lattice: the head, the lined (or raw) spaces, the ways up and down, the doors, the rooms' things, the years' marks.</summary>
internal sealed partial class Depths
{
    static readonly Rgb Rubble = new(0x6a, 0x62, 0x5a), Bedroll = new(0x6a, 0x5a, 0x40), Wood = new(0x6e, 0x52, 0x38), CrateBrown = new(0x7a, 0x5c, 0x3a),
        Cryo = new(0x8f, 0xd8, 0xff), Linen = new(0xc8, 0xc4, 0xb8);

    /// <summary>Where each door's front cells are in the frame, for keeping its way clear.</summary>
    readonly List<(int U, int Y, int V, DSpace A, DSpace B)> _doorCells = new();

    void Build()
    {
        HeadBuild();
        Storeys();
        foreach (var s in Live) foreach (var (i, j, k) in s.Blocks()) if (j < 0) P.Dig(i, j, k, i, j, k);
        Line();
        Locks();
        foreach (var s in Live.Where(s => s.Run != null)) RunBuild(s);
        foreach (var l in _ladders) LadderBuild(l);
        foreach (var (shaft, stops) in _lifts) if (!shaft.Dead) LiftBuild(shaft, stops.Where(x => !x.Dead).ToList());
        foreach (var s in Live.Where(s => s.Mod < 0 && s.Kind != "lift" && !(s.Run != null))) ModuleOf(s);
        foreach (var j in Joints.ToList()) JointBuild(j);
        // The head opens on the way down.
        if (_top != null && _top.Mod >= 0) P.C.Link(_headModule, _top.Mod);
        // The lifts' doors join their landings once those have modules.
        foreach (var (shaft, stops) in _lifts) if (!shaft.Dead) LiftDoors(shaft, stops.Where(x => !x.Dead && x.Mod >= 0).ToList());
        foreach (var g in _galleries.Where(g => !g.Dead)) Deck(g);
        SpanBuild();
        CollapseBuild();
        foreach (var s in Live.Where(s => s.Def != null && !s.Secret && s.Mod >= 0)) Furnish(s);
        foreach (var s in Live.Where(s => s.Mod >= 0 && s.Def == null && !s.Secret && s.Kind is not ("stair" or "ramp" or "shaft" or "lift" or "breach"))) WayLights(s);
        BreachBuild();
        Keycards();
        Stories();
        Signatures();
        _vault = _vaultSpace?.Mod ?? -1;
        var deepest = Live.Where(s => s.Mod >= 0).OrderBy(s => s.Bottom).ThenByDescending(s => s == _vaultSpace).FirstOrDefault();
        _deepest = deepest?.Mod ?? -1;
    }

    // ---- paint -----------------------------------------------------------------------------------

    Stuff2 AlienStuff(int which) => (Fam?.Style ?? "") switch
    {
        "alien_library" => which == 0 ? new Stuff2(Material.Crystal, new Rgb(0x6a, 0x8c, 0x96)) : new Stuff2(Material.Glass, new Rgb(0x4a, 0x6a, 0x78)),
        "alien_hive" => which == 0 ? new Stuff2(Material.Chitin, new Rgb(0x5a, 0x48, 0x3e)) : new Stuff2(Material.Resin, new Rgb(0x6e, 0x52, 0x46)),
        _ => which == 0 ? new Stuff2(Material.Resin, new Rgb(0x8a, 0x86, 0x9a)) : new Stuff2(Material.Basalt, new Rgb(0x5a, 0x58, 0x66)),
    };

    Stuff2 Paint(DSpace s, Role role)
    {
        Fab f = P.F;
        if (Alien) return AlienStuff(role == Role.Floor ? 1 : 0);
        if (s.Lining == "timber") return role == Role.Floor ? f.Plinth : new Stuff2(Material.Timber, Wood);
        if (s.Lining == "steel" || s.Kind is "airlock" || s.Module == ModuleKind.Vault) return role == Role.Floor ? f.Plinth : f.Steel;
        if (s.Lining == "ice") return new Stuff2(Material.Glass, new Rgb(0xa8, 0xc8, 0xd8));
        if (role == Role.Floor) return s.Kind is "room" or "hall" or "chamber" ? f.Floor : f.Plinth;
        if (role == Role.Roof) return f.Roof;
        // A zone wears its own shade, so you know when you've passed a lock.
        Stuff2 wall = s.Kind is "corridor" or "landing" or "stair" or "ramp" or "ring" or "crawl" or "shaft" or "gallery" or "hub" ? f.Plinth : f.Wall;
        int zone = Def.Zones.IndexOf(s.Zone);
        return zone <= 0 ? wall : wall.Scale(1f - 0.08f * zone);
    }

    // ---- the head ----------------------------------------------------------------------------------

    void HeadBuild()
    {
        var (i0, k0, i1, k1, h) = _head;
        Fab f = P.F;
        Stuff2 wall = _headKind switch
        {
            // Corrugated iron over the shaft (timber in the open rots and brings down what hangs from it).
            "headframe" => Tech == TechLevel.Low ? new Stuff2(Material.Rust, new Rgb(0x7a, 0x52, 0x3c)) : new Stuff2(Material.Steel, new Rgb(0x6a, 0x6e, 0x70)),
            "maw" => AlienStuff(0),
            "hatch" => f.Plinth,
            _ => f.Wall,
        };
        if (Alien) wall = AlienStuff(0);
        Stuff2 roof = Alien ? AlienStuff(1) : _headKind == "headframe" ? wall.Scale(0.85f) : f.Roof;
        string name = _headKind switch { "hood" => "blockhouse", "portal" => "portal", "headframe" => "shaft-house", "maw" => "maw", "hatch" => "hatch", _ => "entrance" };
        // No weather box: that's for roadside ruins, whose decay takes down everything thin (ladders too).
        _headModule = P.Hall(name, ModuleKind.Hall, 0, i0, k0, i1, k1, 0, h, wall, roof, weather: false);
        int door = (i0 + i1) / 2;
        // The door clear of the way down's top.
        if (_top?.Run is { } run && door >= run.I - 1 && door <= run.I + run.Width) door = run.I - 2 >= i0 + 1 ? run.I - 2 : run.I + run.Width + 1;
        door = Math.Clamp(door, i0 + 1, i1 - 1);
        P.Door(door, 0, k0, true, _headModule, _yard);
        P.Lamp(2 * door + 1, 2 * h - 1, 2 * k0 + 4, _headModule, 0.9f, 9f);
        switch (_headKind)
        {
            case "headframe":
                P.Gable(i0, k0, i1, k1, h + 1, R.Chance(0.5f), roof, wall, 0);
                break;
            case "maw":
                P.Cap((i0 + i1 + 1) / 2.0, (k0 + k1 + 1) / 2.0, Math.Min(i1 - i0, k1 - k0) / 2.0 + 0.5, h + 1, AlienStuff(1));
                break;
            case "portal":
                // A low, heavy front under an earth-coloured berm, ridged across.
                P.Gable(i0, k0, i1, k1, h + 1, true, new Stuff2(Material.Concrete, new Rgb(0x6a, 0x62, 0x52)), wall, 0);
                break;
            default:
                // Vents breathing from below and a mast whose red lamp still blinks.
                for (int n = 0; n < 2; n++)
                {
                    int vi = n == 0 ? i0 + 1 : i1 - 1, vk = k1 - 1, vh = R.Between(2, 3);
                    for (int y = 0; y < vh; y++) P.Put(2 * vi, 2 * (h + 1) + y, 2 * vk, new Block(BlockType.Cube, Material.Steel, new Rgb(0x6a, 0x6c, 0x68).Scale(0.85f + 0.05f * (y % 2)), Role.Decor));
                    P.Put(2 * vi, 2 * (h + 1) + vh, 2 * vk, new Block(BlockType.Tank, Material.Steel, new Rgb(0x4a, 0x4c, 0x48), Role.Decor));
                    P.C.X.Markers.Add(new Marker("vent", P.Cell(2 * vi, 2 * (h + 1) + vh + 1, 2 * vk), "a thin breath of air from below"));
                    P.C.X.Sounds.Add(new SoundHook("vent-hum", P.Cell(2 * vi, 2 * (h + 1), 2 * vk), 0.4f, 0));
                }
                break;
        }
        // What marks it from afar: a mast by its back corner whose red lamp still blinks (an alien spire over a maw),
        // as tall as the family's landmark wants and taller the deeper the place goes.
        int rise = Math.Max(Fam?.Rise ?? 12, 10 + 2 * Levels.Count);
        int mu = 2 * i1 + 3, mv = 2 * k1 + 1;
        bool clear = true;
        for (int y = 0; y <= rise && clear; y++) clear = P.Get(mu, y, mv).IsAir && !P.Claimed(mu, y, mv);
        if (!clear) { mu = 2 * i0 - 4; clear = true; for (int y = 0; y <= rise && clear; y++) clear = P.Get(mu, y, mv).IsAir && !P.Claimed(mu, y, mv); }
        if (clear)
        {
            Stuff2 mast = Alien ? AlienStuff(0) : new Stuff2(Material.Steel, new Rgb(0x5a, 0x5c, 0x60));
            for (int y = 0; y < rise; y++) P.Put(mu, y, mv, new Block(Alien ? BlockType.Rib : y % 4 == 3 ? BlockType.Lattice : BlockType.Pillar, mast.M, mast.C, Role.Decor));
            Rgb lamp = Alien ? new Rgb(0x80, 0xff, 0xe0) : new Rgb(0xff, 0x30, 0x20);
            P.Put(mu, rise, mv, new Block(BlockType.Cube, Alien ? Material.Glyph : Material.Lumen, lamp, Role.Light));
            P.C.X.Lights.Add(new LightSpot { Cell = P.Cell(mu, rise, mv), Color = lamp, Mode = Alien ? LightMode.Pulse : LightMode.Beacon, Energy = 2.2f, Range = 14f });
        }
    }

    // ---- levels and storeys --------------------------------------------------------------------------

    void Storeys()
    {
        for (int n = 0; n < Levels.Count; n++) P.Level(-(n + 1), Levels[n].J, $"level {n + 1}");
        foreach (var s in Live)
        {
            if (s.Level >= 0 && s.Level < Levels.Count) { s.Storey = -(s.Level + 1); continue; }
            int n = Levels.FindIndex(l => l.J <= s.Bottom + 1);
            s.Storey = n < 0 ? -Math.Max(1, Levels.Count) : -(n + 1);
        }
    }

    // ---- the lining ----------------------------------------------------------------------------------

    /// <summary>A block of wall, floor or roof round every lined space, where no space's inside is; raw rock stays the planet's.</summary>
    void Line()
    {
        var done = new HashSet<(int, int, int)>();
        foreach (var s in Live.Where(s => !s.Raw))
            foreach (var (i, j, k) in s.Blocks())
            {
                if (j >= 0) continue;
                for (int dj = -1; dj <= 1; dj++)
                    for (int dk = -1; dk <= 1; dk++)
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = i + di, nj = j + dj, nk = k + dk;
                            if (nj >= 0 || G.Interior(ni, nj, nk) || !done.Add((ni, nj, nk))) continue;
                            // Under someone's floor it's a floor; over someone's head a roof; else a wall.
                            Role role = G.Interior(ni, nj + 1, nk) ? Role.Floor : G.Interior(ni, nj - 1, nk) ? Role.Roof : Role.Wall;
                            var owner = role == Role.Floor ? S[G.Owner(ni, nj + 1, nk)] : s;
                            P.Fill(ni, nj, nk, ni, nj, nk, Plot.Cube(Paint(owner.Raw ? s : owner, role), role));
                        }
            }
    }

    // ---- locks -------------------------------------------------------------------------------------

    /// <summary>Doors that want a card or a blast door: the vault's, a room's whose library entry locks it, the blast locks between zones.</summary>
    void Locks()
    {
        foreach (var j in Joints.Where(j => j.Kind is "door" or "locked"))
        {
            var room = j.B;
            if (room.Def is not { } def) continue;
            if (def.Id == "vault") { j.Kind = Culture is "orderly" && R.Chance(0.4f) || Def.Blast > 0.5f && R.Chance(0.5f) ? "blast" : "locked"; j.Clearance = "command"; }
            else if (def.Blast) { j.Kind = "blast"; j.Clearance = def.Lock ?? "security"; }
            else if (def.Lock != null) { j.Kind = "locked"; j.Clearance = def.Lock; }
        }
        foreach (var j in Joints.Where(j => j.Kind == "locked" && j.Clearance == null)) j.Clearance = "command";
        foreach (var j in Joints.Where(j => j.Kind == "blast" && j.Clearance == null)) j.Clearance = "security";
    }

    // ---- stairs and ramps ------------------------------------------------------------------------------

    void RunBuild(DSpace s)
    {
        var (i, k, d, rise, width) = s.Run!.Value;
        int j = s.Bottom;
        int mod = s.Ramp ? RampFlight(s, i, k, d, j, rise, width) : P.Flight(i, k, d, j, rise, width, "stair", s.Storey);
        // The flight's headroom over it: the rest of its columns' air joins its module.
        var rest = new List<Int3>();
        foreach (var (bi, bj, bk) in s.Blocks())
        {
            if (bj >= 0) continue;
            for (int y = 2 * bj; y < 2 * bj + 2; y++)
                for (int v = 2 * bk; v < 2 * bk + 2; v++)
                    for (int u = 2 * bi; u < 2 * bi + 2; u++)
                        if (!P.Claimed(u, y, v) && P.Get(u, y, v).IsAir) rest.Add(P.Cell(u, y, v));
        }
        if (rest.Count > 0)
        {
            P.C.Reshape(mod, P.C.Modules[mod].Cells.Concat(rest).ToList());
            P.Claim(rest);
        }
        s.Mod = mod;
    }

    /// <summary>A ramp, as a flight is laid (a metre up for a metre along, solid under each upper cell), of the player's ramp blocks.</summary>
    int RampFlight(DSpace s, int i, int k, Dir d, int j, int rise, int width)
    {
        int y0 = 2 * j, n = 2 * rise;
        Stuff2 stuff = Alien ? AlienStuff(1) : s.Raw ? new Stuff2(Material.Rock, Rubble.Scale(1.05f)) : P.F.Floor;
        var cells = new List<(int U, int Y, int V)>();
        for (int t = 0; t < n; t++)
            for (int w = 0; w < 2 * width; w++)
            {
                var (u, v) = d switch
                {
                    Dir.East => (2 * i + t, 2 * k + w),
                    Dir.West => (2 * i + 1 - t, 2 * k + w),
                    Dir.South => (2 * i + w, 2 * k + t),
                    _ => (2 * i + w, 2 * k + 1 - t),
                };
                int y = y0 + t;
                if (t > 0) P.Put(u, y - 1, v, Plot.Cube(stuff.Scale(0.9f), Role.Traverse));
                P.Put(u, y, v, new Block(BlockType.Ramp, stuff.M, stuff.C, Role.Traverse, d));
                cells.Add((u, y, v));
                for (int up = 1; up <= 4; up++)
                {
                    P.Air(u, y + up, v);
                    cells.Add((u, y + up, v));
                }
            }
        return P.C.AddModule(ModuleKind.Stair, "ramp", s.Storey, ClaimFree(cells));
    }

    /// <summary>The cells no room has yet, claimed for one.</summary>
    List<Int3> ClaimFree(IEnumerable<(int U, int Y, int V)> cells)
    {
        var free = new List<Int3>();
        var seen = new HashSet<(int, int, int)>();
        foreach (var c in cells)
            if (seen.Add(c) && !P.Claimed(c.U, c.Y, c.V)) free.Add(P.Cell(c.U, c.Y, c.V));
        if (free.Count == 0) throw new InvalidOperationException("a way up with no cells of its own");
        P.Claim(free);
        return free;
    }

    // ---- ladders and lifts ----------------------------------------------------------------------------

    void LadderBuild((int I, int K, Dir Back, int Bottom, int Top) l)
    {
        // The rungs against the wall behind, in the corner cell if a second wall is beside it.
        int u = 2 * l.I, v = 2 * l.K;
        switch (l.Back)
        {
            case Dir.North: v = 2 * l.K; u = G.Interior(l.I - 1, l.Bottom, l.K) ? 2 * l.I + 1 : 2 * l.I; break;
            case Dir.South: v = 2 * l.K + 1; u = G.Interior(l.I - 1, l.Bottom, l.K) ? 2 * l.I + 1 : 2 * l.I; break;
            case Dir.West: u = 2 * l.I; v = G.Interior(l.I, l.Bottom, l.K - 1) ? 2 * l.K + 1 : 2 * l.K; break;
            default: u = 2 * l.I + 1; v = G.Interior(l.I, l.Bottom, l.K - 1) ? 2 * l.K + 1 : 2 * l.K; break;
        }
        int y0 = 2 * l.Bottom, y1 = 2 * l.Top - 1;
        P.Ladder(u, y0, y1, v, l.Back);
        // The hole at the top is grated over but for the ladder's own cell.
        for (int dv = 0; dv < 2; dv++)
            for (int du = 0; du < 2; du++)
            {
                int gu = 2 * l.I + du, gv = 2 * l.K + dv;
                if (gu == u && gv == v) continue;
                P.Put(gu, y1, gv, new Block(BlockType.Lattice, P.F.Grating.M, P.F.Grating.C, Role.Floor));
            }
    }

    void LiftBuild(DSpace shaft, List<DSpace> stops)
    {
        if (stops.Count < 2) return;
        stops = stops.OrderBy(s => s.J).ToList();
        var cells = new List<(int, int, int)>();
        foreach (var st in stops)
            for (int v = 2 * shaft.K0; v < 2 * shaft.K0 + 2; v++)
                for (int u = 2 * shaft.I0; u < 2 * shaft.I0 + 2; u++) cells.Add((u, 2 * st.J, v));
        shaft.Mod = P.C.AddModule(ModuleKind.Lift, "lift", stops[0].Storey, ClaimFree(cells));
        _liftStops[shaft] = stops;
    }

    readonly Dictionary<DSpace, List<DSpace>> _liftStops = new();

    void LiftDoors(DSpace shaft, List<DSpace> stops)
    {
        if (shaft.Mod < 0 || !_liftStops.TryGetValue(shaft, out var all)) return;
        var lift = new LiftShaft();
        for (int v = 2 * shaft.K0; v < 2 * shaft.K0 + 2; v++)
            for (int u = 2 * shaft.I0; u < 2 * shaft.I0 + 2; u++) lift.Footprint.Add(P.Cell(u, 0, v) with { Y = 0 });
        foreach (var st in all)
        {
            if (st.Mod < 0) return;
            int d = -1;
            foreach (Dir dir in Dirs.Horizontal)
            {
                var (di, dk) = V(dir);
                int wi = shaft.I0 + di, wk = shaft.K0 + dk;
                if (!st.Has(wi + di, wk + dk) || st.FloorAt(wi + di, wk + dk) != st.J || G.Interior(wi, st.J, wk)) continue;
                d = P.Door(wi, st.J, wk, dir is Dir.North or Dir.South, st.Mod, shaft.Mod);
                break;
            }
            if (d < 0) return;
            lift.StopWalkY.Add(2 * st.J);
            lift.StopDoors.Add(d);
        }
        P.C.X.Lifts.Add(lift);
    }

    // ---- modules and joints -------------------------------------------------------------------------

    void ModuleOf(DSpace s)
    {
        var cells = new List<(int U, int Y, int V)>();
        foreach (var (i, j, k) in s.Blocks())
        {
            if (j >= 0) continue;
            for (int y = 2 * j; y < 2 * j + 2; y++)
                for (int v = 2 * k; v < 2 * k + 2; v++)
                    for (int u = 2 * i; u < 2 * i + 2; u++)
                        if (!P.Claimed(u, y, v)) cells.Add((u, y, v));
        }
        if (cells.Count == 0) return;
        ModuleKind kind = s.Secret ? ModuleKind.Room : s.Module;
        string purpose = s.Secret ? "hidden stash" : s.Def?.Id ?? s.Purpose;
        s.Mod = P.RoomOf(kind, purpose, s.Storey, cells);
    }

    void JointBuild(DJoint j)
    {
        int a = j.A.Mod, b = j.B.Mod;
        if (j.A.Dead || j.B.Dead) return;
        if (j.A == _span || j.B == _span)
        {
            if (a < 0 || b < 0) return;
        }
        switch (j.Kind)
        {
            case "open":
                P.C.Link(a, b);
                break;
            case "door" or "hazard" or "hatch" or "locked" or "blast":
            {
                if (a < 0 || b < 0) return;
                Frame(j);
                int d = P.Door(j.I, j.J, j.K, j.AlongI, a, b);
                if (j.Kind is "hazard" or "blast") P.C.Doorways[d].Hazard = true;
                if (j.Kind is "locked" or "blast") P.C.X.Locks.Add(new DoorLock { Doorway = d, Clearance = j.Clearance ?? "security", Blast = j.Kind == "blast" });
                RecordDoor(j);
                break;
            }
            case "secret":
                if (a < 0 || b < 0) return;
                SecretWall(j);
                break;
            case "broken":
            {
                if (a < 0 || b < 0) return;
                // Smashed through: the wall's block dug out, its rubble kicked aside.
                P.Dig(j.I, j.J, j.K, j.I, j.J, j.K);
                P.C.Link(a, b);
                RecordDoor(j);
                break;
            }
            case "sealed":
            {
                // Welded shut in its socket: a door you can see and never open.
                for (int s = 0; s < 2; s++)
                    for (int y = 2 * j.J; y < 2 * j.J + DoorSize.Tall; y++)
                    {
                        int u = j.AlongI ? 2 * j.I + s : 2 * j.I, v = j.AlongI ? 2 * j.K : 2 * j.K + s;
                        P.Put(u, y, v, new Block(BlockType.SealedDoor, Material.Steel, new Rgb(0x5a, 0x5e, 0x62), Role.Wall, j.AlongI ? Dir.South : Dir.East));
                    }
                P.C.X.Story.Add(new StoryProp("sealed", P.Cell(2 * j.I, 2 * j.J, 2 * j.K), "Welded shut from this side. Whatever was behind it, someone wanted it to stay there."));
                break;
            }
        }
    }

    /// <summary>A door cut in raw rock gets a frame: its head and jambs set in timber (a mine's) or dressed stone, so it hangs in something.</summary>
    void Frame(DJoint j)
    {
        Stuff2 frame = Def.Lining == "timber" ? new Stuff2(Material.Timber, Wood) : Alien ? AlienStuff(0) : new Stuff2(Material.Rock, Rubble.Scale(1.15f));
        int li = j.AlongI ? 1 : 0, lk = j.AlongI ? 0 : 1;
        foreach (var (i, jj, k) in new[] { (j.I, j.J + 1, j.K), (j.I - li, j.J, j.K - lk), (j.I + li, j.J, j.K + lk), (j.I - li, j.J + 1, j.K - lk), (j.I + li, j.J + 1, j.K + lk) })
        {
            if (jj >= 0 || G.Interior(i, jj, k) || !P.Get(2 * i, 2 * jj, 2 * k).IsAir) continue;
            P.Fill(i, jj, k, i, jj, k, Plot.Cube(frame, Role.Wall));
        }
    }

    void RecordDoor(DJoint j)
    {
        for (int s = 0; s < 2; s++)
        {
            int u = j.AlongI ? 2 * j.I + s : 2 * j.I, v = j.AlongI ? 2 * j.K : 2 * j.K + s;
            _doorCells.Add((u, 2 * j.J, v, j.A, j.B));
            _doorCells.Add((j.AlongI ? u : u + 1, 2 * j.J, j.AlongI ? v + 1 : v, j.A, j.B));
        }
    }

    /// <summary>A hidden room's weak wall: the door-sized block of wall made breakable, its tells in front on the near side, a stash inside.</summary>
    void SecretWall(DJoint j)
    {
        var secret = new SecretRoom { Module = j.B.Mod, FromModule = j.A.Mod };
        Rgb paint = Paint(j.A, Role.Wall).C;
        int v0 = 0;
        for (int y = 2 * j.J; y < 2 * j.J + 2; y++)
            for (int dv = 0; dv < 2; dv++)
                for (int du = 0; du < 2; du++)
                {
                    int u = 2 * j.I + du, v = 2 * j.K + dv;
                    var b = new Block(BlockType.Breakable, Material.Concrete, paint.Scale(0.9f).Lerp(new Rgb(0x8a, 0x7a, 0x60), 0.12f), Role.Secret) { Variant = (byte)(1 + v0++) };
                    P.Put(u, y, v, b);
                    secret.WallCells.Add(P.Cell(u, y, v));
                }
        // Which way from the hidden room out through the wall, and the cells in front of it on the near side.
        var (ai, ak) = j.A.Foot.Where(c => Math.Abs(c.I - j.I) + Math.Abs(c.K - j.K) == 1).DefaultIfEmpty((j.I, j.K)).First();
        int fi = ai - j.I, fk = ak - j.K;
        secret.Facing = P.Turn(DirOf(fi, fk));
        foreach (int s in new[] { 0, 1 })
        {
            int u = fi != 0 ? (fi > 0 ? 2 * ai : 2 * ai + 1) : 2 * j.I + s;
            int v = fk != 0 ? (fk > 0 ? 2 * ak : 2 * ak + 1) : 2 * j.K + s;
            secret.TellCells.Add(P.Cell(u, 2 * j.J, v));
            _doorCells.Add((u, 2 * j.J, v, j.A, j.B));
            if (s == 0) P.Put(u, 2 * j.J, v, new Block(BlockType.Scorch, Material.Ash, new Rgb(0x3a, 0x30, 0x28), Role.Story, Dir.North, 2));
        }
        secret.Tells.Add("cracked wall panel, a shade off the rest");
        secret.Tells.Add("a draft through the cracks");
        secret.Tells.Add("scuff marks on the floor in front");
        secret.Tells.Add("the plan has a gap here no door explains");
        P.C.X.Sounds.Add(new SoundHook("draft", secret.TellCells[^1] + Int3.Up, 0.6f, j.A.Storey));
        P.C.X.Secrets.Add(secret);
        P.C.Link(j.A.Mod, j.B.Mod);
        // Inside: whoever hid here left their stash, and their bedroll, in the far corner.
        var far = j.B.Foot.OrderByDescending(c => Math.Abs(c.I - j.I) + Math.Abs(c.K - j.K)).First();
        P.C.Slots.Add(new Slot { Kind = SlotKind.Loot, Cell = P.Cell(2 * far.I, 2 * j.B.J, 2 * far.K), Module = j.B.Mod, Sure = true, Secret = true });
        P.Put(2 * far.I + 1, 2 * j.B.J, 2 * far.K + 1, new Block(BlockType.Scatter, Material.Cloth, Bedroll, Role.Story));
        P.C.X.Story.Add(new StoryProp("hidden stash", P.Cell(2 * far.I, 2 * j.B.J, 2 * far.K), "Someone walled this space off and kept their best things here."));
    }

    // ---- galleries and the silo --------------------------------------------------------------------

    /// <summary>A gallery's deck of open grating over the hall's top cell, a rail along its open side.</summary>
    void Deck(DSpace g)
    {
        Block deck = new(BlockType.Lattice, P.F.Grating.M, P.F.Grating.C, Role.Floor);
        Block rail = new(BlockType.Rail, Tech == TechLevel.Low ? P.F.Steel.M : Material.Steel, Tech == TechLevel.Low ? P.F.Steel.C : P.F.Hazard, Role.Decor);
        int y = 2 * g.J - 1;
        foreach (var (i, k) in g.Foot)
            for (int dv = 0; dv < 2; dv++)
                for (int du = 0; du < 2; du++) P.Put(2 * i + du, y, 2 * k + dv, deck);
        foreach (var (i, k, drop) in g.Rail)
            for (int s = 0; s < 2; s++)
            {
                var (u, v) = drop switch
                {
                    Dir.East => (2 * i + 1, 2 * k + s),
                    Dir.West => (2 * i, 2 * k + s),
                    Dir.South => (2 * i + s, 2 * k + 1),
                    _ => (2 * i + s, 2 * k),
                };
                P.Put(u, y + 1, v, rail);
            }
    }

    /// <summary>The silo: a gallery ring inside the shaft at every level that opens on it, the missile standing in the middle, the lid flush with the ground.</summary>
    void SpanBuild()
    {
        if (_span == null || _span.Dead) return;
        int ci = _spanCi, ck = _spanCk, r = _spanR;
        bool Ring(int i, int k) => _span.Has(i, k) && !Plot.In(ci + 0.5, ck + 0.5, r - 1.55, i, k);
        Block deck = new(BlockType.Lattice, P.F.Grating.M, P.F.Grating.C, Role.Floor);
        Block rail = new(BlockType.Rail, Material.Steel, P.F.Hazard, Role.Decor);
        foreach (int j in _spanGalleries.Distinct())
        {
            if (j <= _span.J) continue;
            int y = 2 * j - 1;
            foreach (var (i, k) in _span.Foot.Where(c => Ring(c.I, c.K)))
            {
                for (int dv = 0; dv < 2; dv++)
                    for (int du = 0; du < 2; du++) P.Put(2 * i + du, y, 2 * k + dv, deck);
                foreach (Dir od in Dirs.Horizontal)
                {
                    var (oi, ok) = V(od);
                    if (!_span.Has(i + oi, k + ok) || Ring(i + oi, k + ok)) continue;
                    for (int s = 0; s < 2; s++)
                    {
                        var (u, v) = od switch { Dir.East => (2 * i + 1, 2 * k + s), Dir.West => (2 * i, 2 * k + s), Dir.South => (2 * i + s, 2 * k + 1), _ => (2 * i + s, 2 * k) };
                        P.Put(u, y + 1, v, rail);
                    }
                }
            }
        }
        // The missile: a tall body on the floor, its nose under the lid. Structure, not a prop: a room's dressing
        // clears the generator's props off its floor, and would leave the rest of it hanging.
        var body = new Stuff2(Material.Alloy, new Rgb(0xd8, 0xd6, 0xd0));
        int topJ = _span.Top - 2;
        for (int j = _span.J; j <= topJ; j++)
            for (int k = ck - 1; k <= ck; k++)
                for (int i = ci - 1; i <= ci; i++)
                    P.Fill(i, j, k, i, j, k, new Block(BlockType.Cube, body.M, (j - _span.J) % 6 == 5 ? new Rgb(0x2a, 0x2a, 0x2c) : body.C, Role.Decor));
        P.Cap(ci, ck, 1.0, topJ + 1, body);
        // The lid, flush with the ground: two halves with a hazard ring.
        for (int k = ck - r - 1; k <= ck + r + 1; k++)
            for (int i = ci - r - 1; i <= ci + r + 1; i++)
            {
                if (!Plot.In(ci + 0.5, ck + 0.5, r + 1.45, i, k)) continue;
                bool rim = !Plot.In(ci + 0.5, ck + 0.5, r + 0.3, i, k);
                Rgb c = rim ? ((i + k & 1) == 0 ? P.F.Hazard : P.F.Black) : (k == ck ? new Rgb(0x30, 0x32, 0x34) : new Rgb(0x6a, 0x6e, 0x72));
                P.Fill(i, -1, k, i, -1, k, new Block(BlockType.Cube, Material.Steel, c, Role.Roof));
            }
        P.C.X.Story.Add(new StoryProp("silo", P.Cell(2 * ci, 2 * _span.J, 2 * ck), "It never flew. The lid is still shut over it."));
    }

    // ---- the years' marks ----------------------------------------------------------------------------

    void CollapseBuild()
    {
        foreach (var (c, cols) in _collapses)
        {
            if (c.Dead) continue;
            foreach (var (i, k) in cols)
            {
                int top = c.FloorAt(i, k) + c.HeightAt(i, k) - 1;
                for (int j = c.FloorAt(i, k); j <= top && j < 0; j++)
                    P.Fill(i, j, k, i, j, k, new Block(BlockType.Cube, Material.Rock, Rubble.Scale(0.85f + 0.1f * ((i + j + k) & 1)), Role.Debris));
            }
            // Loose rubble spilled either side of the fall.
            foreach (var (i, k) in cols)
                foreach (Dir d in Dirs.Horizontal)
                {
                    var (di, dk) = V(d);
                    int ni = i + di, nk = k + dk;
                    if (!c.Has(ni, nk) || cols.Contains((ni, nk))) continue;
                    if (Hash.Cell(new Int3(ni, 0, nk), 0xC011) % 2 == 0) P.Put(2 * ni + ((di + 1) / 2 == 1 ? 0 : 1), 2 * c.J, 2 * nk + (dk > 0 ? 0 : 1), new Block(BlockType.Rubble, Material.Rock, Rubble, Role.Debris));
                }
            var (mi, mk) = cols[cols.Count / 2];
            P.C.X.Story.Add(new StoryProp("collapse", P.Cell(2 * mi, 2 * c.J, 2 * mk), "The roof came down here. Whoever came after went round."));
            P.C.X.Sounds.Add(new SoundHook("drip", P.Cell(2 * mi, 2 * c.J + 2, 2 * mk), 0.5f, c.Storey));
        }
    }

    /// <summary>The breach from the surface: its lip where you stand, the cells open to the sky, rubble round the hole, the wind in it.</summary>
    void BreachBuild()
    {
        if (_breach is not { } b || b.Room.Dead || b.Slope.Dead || b.Room.Mod < 0) return;
        var breach = new Breach { Module = b.Room.Mod, SurfaceCell = P.Cell(2 * b.I, 0, 2 * b.K) };
        foreach (var (i, j, k) in b.Slope.Blocks())
            if (j >= -3)
                for (int y = 2 * j; y < 2 * j + 2; y++)
                    for (int v = 2 * k; v < 2 * k + 2; v++)
                        for (int u = 2 * i; u < 2 * i + 2; u++)
                            if (P.Get(u, y, v).IsAir) breach.Cells.Add(P.Cell(u, y, v));
        P.C.X.Breaches.Add(breach);
        // Rubble kicked up round the lip, never on the slope itself.
        foreach (var (i, k) in b.Slope.Foot.Where(c => b.Slope.FloorAt(c.I, c.K) >= -3))
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                int ni = i + di, nk = k + dk;
                if (b.Slope.Has(ni, nk) || G.Interior(ni, -1, nk) || Hash.Cell(new Int3(ni, 0, nk), 0xB2EA) % 3 != 0) continue;
                int u = 2 * ni + (Hash.Cell(new Int3(ni, 1, nk), 5) % 2 == 0 ? 0 : 1), v = 2 * nk + (Hash.Cell(new Int3(ni, 2, nk), 5) % 2 == 0 ? 0 : 1);
                if (P.Get(u, 0, v).IsAir && !P.Claimed(u, 0, v)) P.Lay(u, 0, v, new Block(BlockType.Rubble, Material.Rock, Rubble, Role.Debris));
            }
        P.C.X.Sounds.Add(new SoundHook("wind", P.Cell(2 * b.Slope.I0, 2 * b.Room.J + 2, 2 * b.Slope.K0), 0.7f, b.Room.Storey));
        P.C.X.Story.Add(new StoryProp("collapse", breach.SurfaceCell, "The roof came down here. Daylight and weather have been getting in ever since."));
    }

    void Stories()
    {
        foreach (var (kind, s) in _stories)
        {
            if (s.Dead || s.Mod < 0) continue;
            var mid = s.Foot.OrderBy(c => Math.Abs(c.I - s.Mid.I) + Math.Abs(c.K - s.Mid.K)).First();
            Int3 at = P.Cell(2 * mid.I, 2 * s.Bottom, 2 * mid.K);
            switch (kind)
            {
                case "cave":
                    P.C.X.Story.Add(new StoryProp("breakthrough", at, "The wall was smashed through into a cave nobody planned for."));
                    P.C.X.Sounds.Add(new SoundHook("drip", at + Int3.Up * 2, 0.6f, s.Storey));
                    foreach (var c in s.Foot.Where(c => Hash.Cell(new Int3(c.I, 3, c.K), 0xCA7E) % 11 == 0).Take(4))
                        if (P.Get(2 * c.I, 2 * s.FloorAt(c.I, c.K), 2 * c.K).IsAir)
                            P.Put(2 * c.I, 2 * s.FloorAt(c.I, c.K), 2 * c.K, new Block(BlockType.Rubble, Material.Rock, Rubble.Scale(0.9f), Role.Debris));
                    break;
                case "squat":
                    P.C.X.Story.Add(new StoryProp("squatters", at, "Somebody dug through here by hand, and lived at the end of it a while."));
                    P.Put(2 * mid.I, 2 * s.Bottom, 2 * mid.K, new Block(BlockType.Scatter, Material.Cloth, Bedroll, Role.Story));
                    break;
                case "dug-round":
                    P.C.X.Story.Add(new StoryProp("dug round", at, "A way dug round the fall, by hand, with whatever they had."));
                    break;
            }
        }
    }

    // ---- furnishing ---------------------------------------------------------------------------------

    /// <summary>A room's things from its library entry: caches in the corners, terminals and furniture round the walls, rows across a big one, its light, a few left standing where they fell.</summary>
    void Furnish(DSpace s)
    {
        var def = s.Def!;
        int y = 2 * s.J;
        var floor = new HashSet<(int U, int V)>();
        foreach (var (i, k) in s.Foot)
            if (s.FloorAt(i, k) == s.J)
                for (int dv = 0; dv < 2; dv++)
                    for (int du = 0; du < 2; du++) floor.Add((2 * i + du, 2 * k + dv));
        // Kept clear: two cells in from every door and opening, and the foot of anything climbed.
        var keep = new HashSet<(int, int)>();
        foreach (var (u, yy, v, a, b) in _doorCells)
            if ((a == s || b == s) && yy == y)
                foreach (var c in floor)
                    if (Math.Abs(c.U - u) + Math.Abs(c.V - v) <= 2) keep.Add(c);
        foreach (var j in Joints.Where(j => (j.A == s || j.B == s) && j.Kind == "open"))
        {
            var o = j.A == s ? j.B : j.A;
            foreach (var (i, k) in s.Foot)
                foreach (Dir d in Dirs.Horizontal)
                {
                    var (di, dk) = V(d);
                    if (!o.Has(i + di, k + dk)) continue;
                    for (int dv = 0; dv < 2; dv++)
                        for (int du = 0; du < 2; du++)
                            for (int back = 0; back < 2; back++) keep.Add((2 * i + du - di * back, 2 * k + dv - dk * back));
                }
        }
        var blocked = new HashSet<(int, int)>();
        foreach (var c in floor)
        {
            var here = P.Get(c.U, y, c.V);
            if (!here.IsAir && BlockInfo.Collides(here.Type)) blocked.Add(c);
            // A ladder's foot and a hole's edge stay clear.
            if (here.Type == BlockType.Ladder || !s.Raw && P.Get(c.U, y - 1, c.V).IsAir) keep.Add(c);
        }
        var needs = new List<(int, int)>();
        bool Perimeter((int U, int V) c) => Dirs.Horizontal.Any(d => { var (di, dk) = V(d); return !floor.Contains((c.U + di, c.V + dk)); });
        Dir Inward((int U, int V) c)
        {
            foreach (Dir d in Dirs.Horizontal)
            {
                var (di, dk) = V(d);
                if (!floor.Contains((c.U - di, c.V - dk)) && floor.Contains((c.U + di, c.V + dk))) return d;
            }
            return Dir.North;
        }
        bool Connected()
        {
            var start = floor.FirstOrDefault(c => keep.Contains(c) && !blocked.Contains(c));
            if (start == default) start = floor.FirstOrDefault(c => !blocked.Contains(c));
            if (start == default) return false;
            var seen = new HashSet<(int, int)> { start };
            var q = new Queue<(int U, int V)>();
            q.Enqueue(start);
            while (q.Count > 0)
            {
                var (u, v) = q.Dequeue();
                foreach (Dir d in Dirs.Horizontal)
                {
                    var (di, dk) = V(d);
                    var n = (u + di, v + dk);
                    if (floor.Contains(n) && !blocked.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
            }
            if (!floor.All(c => blocked.Contains(c) || seen.Contains(c))) return false;
            return needs.All(a => Dirs.Horizontal.Any(d => { var (di, dk) = V(d); return seen.Contains((a.Item1 + di, a.Item2 + dk)); }));
        }
        bool Take((int, int) c)
        {
            if (keep.Contains(c) || blocked.Contains(c) || !floor.Contains(c)) return false;
            blocked.Add(c);
            if (Connected()) return true;
            blocked.Remove(c);
            return false;
        }
        var perimeter = floor.Where(Perimeter).OrderBy(c => Hash.Cell(new Int3(c.U, 7, c.V), (uint)s.Id)).ToList();
        var corners = perimeter.OrderByDescending(c => Dirs.Horizontal.Count(d => { var (di, dk) = V(d); return !floor.Contains((c.U + di, c.V + dk)); })).ToList();
        int loot = R.Between(def.LootMin, def.LootMax) + (def.Id == "vault" ? Math.Max(1, Math.Min(3, (int)Rarity)) : 0);
        foreach (var c in corners)
        {
            if (loot <= 0) break;
            needs.Add(c);
            if (!Take(c)) { needs.Remove(c); continue; }
            P.Cache(c.U, y, c.V, s.Mod);
            loot--;
        }
        int terms = R.Between(def.TerminalMin, def.TerminalMax);
        foreach (var c in perimeter)
        {
            if (terms <= 0) break;
            needs.Add(c);
            if (!Take(c)) { needs.Remove(c); continue; }
            P.Terminal(c.U, y, c.V, s.Mod);
            terms--;
        }
        if (Fam?.Core == CoreRule.Owned && P.C.CoreModule < 0 && (def.Has("core") || def.Id is "command" or "control"))
        {
            var spot = floor.Where(c => !Perimeter(c) && !keep.Contains(c)).OrderBy(c => Math.Abs(c.U - 2 * s.Mid.I) + Math.Abs(c.V - 2 * s.Mid.K)).FirstOrDefault();
            if (spot != default) needs.Add(spot);
            if (spot != default && Take(spot))
            {
                P.C.Slots.Add(new Slot { Kind = SlotKind.Core, Cell = P.Cell(spot.Item1, y, spot.Item2), Module = s.Mod, Sure = true });
                P.C.CoreModule = s.Mod;
            }
        }
        foreach (var c in perimeter)
        {
            if (blocked.Contains(c) || !R.Chance(def.Fill)) continue;
            if (!Take(c)) continue;
            string prop = PickProp(def);
            P.Put(c.U, y, c.V, Prop(prop, Inward(c)));
            if (prop is "shelf" or "tank" && R.Chance(0.5f) && s.HeightAt(c.U >> 1, c.V >> 1) >= 2) P.Put(c.U, y + 1, c.V, Prop(prop == "shelf" ? "crate" : "tank", Inward(c)));
        }
        // Rows across the middle of a big room: pods, beds, tables, cells.
        if (def.Island.Count > 0 && s.W >= 3 && s.D >= 3)
        {
            string island = def.Island[R.Range(0, def.Island.Count)];
            bool rowsAlongI = s.W >= s.D;
            foreach (var c in floor.OrderBy(c => c.V).ThenBy(c => c.U))
            {
                bool deep = true;
                for (int dz = -1; dz <= 1 && deep; dz++)
                    for (int dx = -1; dx <= 1 && deep; dx++)
                        if (!floor.Contains((c.U + dx, c.V + dz))) deep = false;
                int across = rowsAlongI ? c.V - 2 * s.K0 : c.U - 2 * s.I0, along = rowsAlongI ? c.U - 2 * s.I0 : c.V - 2 * s.K0;
                if (!deep || across % 3 == 0 || along % 4 == 0) continue;
                if (!Take(c)) continue;
                P.Put(c.U, y, c.V, Prop(island, rowsAlongI ? (across % 3 == 1 ? Dir.North : Dir.South) : (across % 3 == 1 ? Dir.West : Dir.East)));
            }
        }
        var free = floor.Where(c => !blocked.Contains(c) && !keep.Contains(c)).OrderBy(c => Hash.Cell(new Int3(c.U, 9, c.V), (uint)s.Id)).ToList();
        int spawns = R.Between(def.SpawnMin, def.SpawnMax);
        foreach (var c in free.Take(spawns)) P.Spawn(c.U, y, c.V, s.Mod);
        var story = free.Skip(spawns).FirstOrDefault(Perimeter);
        if (story != default) P.C.Slots.Add(new Slot { Kind = SlotKind.Prop, Cell = P.Cell(story.Item1, y, story.Item2), Module = s.Mod, Sure = true });
        if ((def.Kind is ModuleKind.Hall or ModuleKind.Vault || s.Module == ModuleKind.Hall) && free.Count > spawns + 2)
            P.C.Slots.Add(new Slot { Kind = SlotKind.Boss, Cell = P.Cell(free[^1].Item1, y, free[^1].Item2), Module = s.Mod, Sure = true });
        if (def.Light != "none") RoomLight(s, def);
        if (def.Has("hum")) P.C.X.Sounds.Add(new SoundHook("hum", P.Cell((int)(2 * s.Mid.I), y + 1, (int)(2 * s.Mid.K)), 0.8f, s.Storey));
    }

    void RoomLight(DSpace s, RoomDef def)
    {
        Rgb color = def.Light switch
        {
            "cold" => new Rgb(0xe6, 0xf2, 0xff), "dim" => new Rgb(0xc8, 0xb0, 0x90), "red" => new Rgb(0xff, 0x30, 0x20), "green" => new Rgb(0x9c, 0xff, 0x8a),
            "glyph" => new Rgb(0x80, 0xff, 0xe0), "cryo" => new Rgb(0x8f, 0xd8, 0xff), _ => P.F.Lamp,
        };
        LightMode mode = def.Light == "red" ? LightMode.Emergency : def.Light == "glyph" || Alien ? LightMode.Pulse : LightMode.Steady;
        // A lamp in the ceiling over the middle, and more down a long room.
        var cols = s.Foot.Where(c => s.FloorAt(c.I, c.K) == s.J).ToList();
        int lamps = Math.Clamp(s.Area / 40 + 1, 1, 4);
        foreach (var c in cols.OrderBy(c => Math.Abs(c.I - s.Mid.I) + Math.Abs(c.K - s.Mid.K)).Where((_, n) => n % Math.Max(1, cols.Count / lamps) == 0).Take(lamps))
        {
            int top = s.FloorAt(c.I, c.K) + s.HeightAt(c.I, c.K);
            if (!Roofed(c.I, top, c.K)) continue;
            P.Lamp(2 * c.I, 2 * top, 2 * c.K, s.Mod, def.Light == "dim" ? 0.8f : 1.3f, Math.Max(s.W, s.D) * 1.8f + 4f, mode);
            P.C.X.Lights[^1].Color = color;
            P.C.X.Lights[^1].Storey = s.Storey;
        }
    }

    /// <summary>The ways' own light: a lamp in the roof every few blocks, timber sets and hung lamps down a drift, a glow in an alien way.</summary>
    void WayLights(DSpace s)
    {
        var cols = s.Foot.Where(c => s.FloorAt(c.I, c.K) == s.J).OrderBy(c => c.I).ThenBy(c => c.K).ToList();
        if (cols.Count == 0) return;
        int every = s.Kind is "hall" or "chamber" or "hub" ? 12 : s.Raw ? 10 : 7;
        int n = 0;
        foreach (var c in cols)
        {
            if ((Math.Abs(c.I * 3 + c.K * 5) % every) != 0 || n++ > 12) continue;
            int top = s.FloorAt(c.I, c.K) + s.HeightAt(c.I, c.K);
            if (!Roofed(c.I, top, c.K)) continue;
            if (s.Raw && !Alien && Def.Raw > 0.5f)
            {
                // A timber set: posts up both walls of the drift and a cap across under the roof, a lamp hung from it.
                Stuff2 timber = new(Material.Timber, Wood);
                P.Put(2 * c.I + 1, 2 * top - 1, 2 * c.K + 1, Plot.Cube(timber, Role.Decor));
            }
            P.Lamp(2 * c.I, 2 * top, 2 * c.K, s.Mod, s.Raw ? 0.9f : 1.1f, s.Kind is "hall" or "chamber" ? 16f : 11f, Alien ? LightMode.Pulse : LightMode.Steady);
            P.C.X.Lights[^1].Storey = s.Storey;
            if (Alien) P.C.X.Lights[^1].Color = new Rgb(0x80, 0xff, 0xe0);
        }
        // Pipes along the roof of a built corridor, against its wall, clear of anything already there (a ladder's shaft, a lamp).
        if (!s.Raw && !Alien && s.Kind is "corridor" or "ring" && Tech != TechLevel.Low && s.IsBox && s.H >= 3 && Math.Max(s.W, s.D) >= 6)
        {
            bool alongI = s.W >= s.D;
            int y = 2 * (s.J + s.H) - 1;
            Dir run = alongI ? Dir.East : Dir.South;
            for (int t = 0; t < 2 * (alongI ? s.W : s.D); t++)
            {
                int u = alongI ? 2 * s.I0 + t : 2 * s.I0, v = alongI ? 2 * s.K0 : 2 * s.K0 + t;
                int bi = u >> 1, bk = v >> 1;
                if (y >= 0 || !P.Get(u, y, v).IsAir || !Roofed(bi, s.J + s.H, bk) || P.Get(u, y - 1, v).Type == BlockType.Ladder) continue;
                P.Put(u, y, v, new Block(BlockType.Pipe, P.F.Pipe.M, P.F.Pipe.C, Role.Decor, run));
            }
        }
    }

    /// <summary>Whether a block over a space is its roof (lining or rock), not an opening into another space (a shaft going up).</summary>
    bool Roofed(int i, int j, int k) => j < 0 && !G.Interior(i, j, k);

    string PickProp(RoomDef def)
    {
        if (def.Props.Count == 0) return "crate";
        int i = R.PickWeighted(def.Props.Select(p => p.Weight).ToList());
        return def.Props[Math.Max(0, i)].Prop;
    }

    Block Prop(string kind, Dir facing)
    {
        Rgb machine = P.F.Steel.C, accent = P.F.Accent.C;
        if (Alien) { machine = AlienStuff(0).C; accent = AlienStuff(1).C; }
        return kind switch
        {
            "bench" => new Block(BlockType.Bench, Material.Alloy, machine.Lerp(new Rgb(0xb8, 0xbc, 0xc0), 0.4f), Role.Prop, facing),
            "machine" => new Block(BlockType.Machine, Material.Alloy, machine, Role.Prop, facing),
            "shelf" => new Block(BlockType.Shelf, Material.Steel, machine.Scale(0.8f), Role.Prop, facing),
            "locker" => new Block(BlockType.Locker, Material.Steel, accent.Lerp(new Rgb(0x50, 0x58, 0x60), 0.6f), Role.Prop, facing),
            "crate" => new Block(BlockType.Crate, Material.Scrap, CrateBrown.Lerp(accent, 0.2f), Role.Prop, facing),
            "table" => new Block(BlockType.Table, Material.Timber, Wood, Role.Prop, facing),
            "bunk" => new Block(BlockType.Bunk, Material.Cloth, new Rgb(0x4f, 0x6d, 0x7a), Role.Prop, facing),
            "bed" => new Block(BlockType.Bed, Material.Cloth, Linen, Role.Prop, facing),
            "pod" => new Block(BlockType.Pod, Material.Lumen, Cryo, Role.Prop, facing),
            "tank" => new Block(BlockType.Tank, Material.Steel, machine.Lerp(accent, 0.25f), Role.Prop, facing),
            "statue" => new Block(BlockType.Statue, Material.Basalt, new Rgb(0x4a, 0x48, 0x50), Role.Prop, facing),
            "crystal" => new Block(BlockType.Crystal, Material.Crystal, new Rgb(0x80, 0xff, 0xe0), Role.Prop, facing),
            "cell" => new Block(BlockType.Lattice, Material.Steel, new Rgb(0x50, 0x54, 0x5a), Role.Prop, facing),
            "pillar" => new Block(BlockType.Pillar, Material.Concrete, P.F.Wall.C, Role.Prop, facing),
            "barricade" => new Block(BlockType.Barricade, Material.Timber, Wood.Scale(0.8f), Role.Story, facing),
            "planter" => new Block(BlockType.Crate, Material.Soil, new Rgb(0x4a, 0x3a, 0x2a), Role.Prop, facing),
            "screen" => new Block(BlockType.Pane, Material.Glass, new Rgb(0x9f, 0xd3, 0xe0), Role.Prop, facing),
            _ => new Block(BlockType.Crate, Material.Scrap, CrateBrown, Role.Prop, facing),
        };
    }

    // ---- keycards ----------------------------------------------------------------------------------

    /// <summary>Each clearance's card lies in a room you can walk to with every lock still shut, on remains, where someone carried it.</summary>
    void Keycards()
    {
        if (P.C.X.Locks.Count == 0) return;
        var shut = new HashSet<(int, int)>();
        foreach (var l in P.C.X.Locks)
        {
            var d = P.C.Doorways[l.Doorway];
            shut.Add((d.ModuleA, d.ModuleB));
        }
        var adj = new Dictionary<int, List<int>>();
        foreach (var (a, b) in P.C.Edges)
        {
            if (shut.Contains((a, b)) || shut.Contains((b, a))) continue;
            (adj.TryGetValue(a, out var la) ? la : adj[a] = new List<int>()).Add(b);
            (adj.TryGetValue(b, out var lb) ? lb : adj[b] = new List<int>()).Add(a);
        }
        var open = new HashSet<int>();
        int start = _yard >= 0 ? _yard : _headModule;
        var q = new Queue<int>();
        open.Add(start);
        q.Enqueue(start);
        while (q.Count > 0)
        {
            int m = q.Dequeue();
            if (!adj.TryGetValue(m, out var next)) continue;
            foreach (int n in next) if (open.Add(n)) q.Enqueue(n);
        }
        foreach (string clearance in P.C.X.Locks.Select(l => l.Clearance).Distinct().ToList())
        {
            var rooms = Live.Where(s => s.Mod >= 0 && open.Contains(s.Mod) && !s.Secret && s.Kind is "room" or "hall" or "landing" or "corridor" or "hub")
                .OrderByDescending(s => s.Def?.Has("keyspot") == true ? 1 : 0).ThenBy(s => s.Kind == "room" ? 0 : 1).ThenBy(_ => R.Float()).ToList();
            foreach (var room in rooms)
            {
                int y = 2 * room.J;
                (int U, int V)? spot = null;
                foreach (var (i, k) in room.Foot.OrderBy(_ => R.Float()))
                {
                    if (room.FloorAt(i, k) != room.J) continue;
                    int u = 2 * i + R.Range(0, 2), v = 2 * k + R.Range(0, 2);
                    // A free floor cell: nothing on it, and floor (or the rock) under it.
                    if (!P.Get(u, y, v).IsAir || !room.Raw && P.Get(u, y - 1, v).IsAir) continue;
                    if (_doorCells.Any(d => Math.Abs(d.U - u) + Math.Abs(d.V - v) <= 2 && d.Y == y)) continue;
                    var cell = P.Cell(u, y, v);
                    if (P.C.Slots.Any(sl => sl.Cell == cell)) continue;
                    spot = (u, v);
                    break;
                }
                if (spot is not { } sp) continue;
                P.Put(sp.U, y, sp.V, new Block(BlockType.Remains, Material.Cloth, new Rgb(0x7a, 0x74, 0x64), Role.Story, Dir.North, 2));
                P.C.X.Keys.Add(new KeyCard { Clearance = clearance, Cell = P.Cell(sp.U, y, sp.V), Module = room.Mod, On = "remains" });
                P.C.X.Story.Add(new StoryProp("keycard", P.Cell(sp.U, y, sp.V), $"A {clearance} keycard, still clipped to what's left of its owner."));
                break;
            }
        }
    }

    // ---- signatures -----------------------------------------------------------------------------------

    /// <summary>Each level's plan in short, as the architect's storeys are (shape/part/area/corridors/junctions/ends/rooms/holes/rooms), for the sameness tests.</summary>
    void Signatures()
    {
        int index = P.C.X.Plans.Select(p => p.Building).DefaultIfEmpty(-1).Max() + 1;
        string shape = Alien ? "alien" : Def.Raw > 0.5f ? "raw" : _stacked ? "stacked" : "stepped";
        foreach (var lv in Levels)
        {
            var spaces = lv.Spaces.Where(s => !s.Dead).ToList();
            int area = spaces.Sum(s => s.Area);
            var ways = spaces.Where(s => s.Module == ModuleKind.Corridor).ToList();
            int corr = ways.Sum(s => s.Area);
            int junctions = ways.Count(w => Joints.Count(j => j.A == w || j.B == w) >= 3);
            int ends = ways.Count(w => Joints.Count(j => j.A == w || j.B == w) == 1);
            var rooms = spaces.Where(s => s.Module is ModuleKind.Room or ModuleKind.Hall or ModuleKind.Vault or ModuleKind.Cave).ToList();
            string list = string.Join(",", rooms.OrderBy(r => r.I0).ThenBy(r => r.K0).Select(r => $"{r.I0}.{r.K0}.{r.W}x{r.D}{(r.Secret ? "s" : "")}"));
            string sig = $"{shape}/{lv.Part}/a{area}/c{corr}/j{junctions}/e{ends}/r{rooms.Count}/h{_collapses.Count(c => c.S.Level == lv.Index)}/{list}@{lv.J}";
            P.C.X.Plans.Add((index, -(lv.Index + 1), sig));
        }
    }
}
