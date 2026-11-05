using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GalHack.Items;

namespace GalHack.Deco;

/// <summary>
/// Outdoor sites: roads, yards, airfields and rail lines dressed with abandoned vehicles, trains
/// and airliner wrecks. Each site runs north to south (along z) and keeps a lane clear end to end,
/// wide enough to drive (or walk, for rail and docks). In an old-world place the vehicles are rusted
/// hulks, doors flung open, grass through the cracks; the frontier gets carts and diesel, rich
/// worlds skimmers and maglevs. Deterministic from the site's seed.
/// </summary>
public sealed class DecoSites
{
    readonly DecoForge _forge;
    readonly DecoArrangements _arr;
    DecoCatalogue Cat => _forge.Catalogue;
    DecoRoom _room;
    DecoPlan _plan;
    DecoRng _rng;
    DecoRaster _raster;
    int _assemblies;
    DecoCover _drift;

    public DecoSites(DecoForge forge, DecoArrangements arr)
    {
        _forge = forge;
        _arr = arr;
    }

    /// <summary>A site's room: its size, the lane (as a doorway at each end) and the walker or driver that must get through.</summary>
    public DecoRoom Room(string site, ulong seed, DecoTech tech = DecoTech.Mid, string state = "abandoned")
    {
        DecoSiteDef def = _arr.Sites[site];
        float laneX = site switch
        {
            "evacuation-jam" or "roadblock" or "pile-up" => 6f,
            "car-park" => def.Width / 2,
            "depot-yard" => def.Width - 6f,
            "derelict-train" or "derailed-train" => 1.6f,
            "station-platform" => def.Width - 2f,
            "rail-depot" => def.Width - 2.5f,
            "airliner-crash" or "airliner-gate" => 3f,
            _ => def.Width / 2,
        };
        var room = new DecoRoom
        {
            Width = def.Width, Depth = def.Length, Height = 40, Site = site, Seed = seed, Tech = tech, State = state, Exposed = true,
            Agent = def.Lane, Set = "vehicles",
        };
        // The opening is wider than the lane by a margin and a raster cell, so the lane-wide walker always has a place to stand in it.
        float opening = MathF.Ceiling(def.Lane / 0.5f - 1e-4f) * 0.5f + 1f;
        if (site == "loading-dock")
        {
            room.Doors.Add(new DecoDoor(DecoWall.West, def.Length - 3f, opening));
            room.Doors.Add(new DecoDoor(DecoWall.East, def.Length - 3f, opening));
        }
        else
        {
            room.Doors.Add(new DecoDoor(DecoWall.North, laneX, opening));
            room.Doors.Add(new DecoDoor(DecoWall.South, laneX, opening));
        }
        return room;
    }

    public DecoPlan Plan(DecoRoom room)
    {
        _room = room;
        _plan = new DecoPlan { Room = room };
        _rng = new DecoRng(DecoHash.Combine(room.Seed, DecoHash.Fnv(room.Site)));
        _raster = new DecoRaster(room.Width, room.Depth, 0.5f);
        _assemblies = 0;
        _drift = room.Drift ?? (DecoHash.Mix(room.Seed ^ 0x5A4D) % 2 == 0 ? DecoCover.Sand : DecoCover.Snow);
        switch (room.Site)
        {
            case "evacuation-jam": Jam(); break;
            case "car-park": CarPark(); break;
            case "roadblock": Roadblock(); break;
            case "pile-up": PileUp(); break;
            case "depot-yard": DepotYard(); break;
            case "loading-dock": LoadingDock(); break;
            case "airliner-crash": AirlinerCrash(); break;
            case "airliner-gate": AirlinerGate(); break;
            case "derelict-train": Train(derailed: false); break;
            case "derailed-train": Train(derailed: true); break;
            case "station-platform": Station(); break;
            case "rail-depot": RailDepot(); break;
            default: throw new ArgumentException($"unknown site '{room.Site}'");
        }
        return _plan;
    }

    // ---- choosing and placing ----------------------------------------------------------------------

    DecoAge Age => _room.AgeOrDefault;

    /// <summary>Vehicles playing one of the roles, of the site's tech level or older (nothing newer than the world could build).</summary>
    List<DecoPiece> Vehicles(params string[] roles) =>
        Cat.Pieces.Where(p => p.Has("vehicle") && roles.Any(p.Has) && (_room.Tech == null || p.Tech <= _room.Tech)).ToList();

    /// <summary>One of them: of the site's own tech level four times in five, an older hand-me-down otherwise.</summary>
    DecoPiece Any(List<DecoPiece> list)
    {
        if (list.Count == 0) return null;
        var own = _room.Tech == null ? list : list.Where(p => p.Tech == _room.Tech).ToList();
        var older = list.Where(p => !own.Contains(p)).ToList();
        var from = own.Count > 0 && (older.Count == 0 || _rng.Chance(0.8f)) ? own : older;
        return from[_rng.Int(from.Count)];
    }

    static readonly string[] Paints = { "paint-red", "paint-blue", "paint-cream", "paint-green", "paint-teal", "paint-grey", "paint-yellow", "paint-white" };

    /// <summary>How a vehicle (or wreck section) was found, by age: in use, a fresh wreck, or a century-old hulk.</summary>
    void Weather(DecoPlacement pl, DecoPiece p, float violence = 0f)
    {
        pl.Age = Age;
        pl.Exposed = true;
        if (Age == DecoAge.Fresh || _room.State == "active") return;
        var options = new List<(DecoState s, float w)>
        {
            (DecoState.Rusted, 5), (DecoState.Overgrown, 2.2f), (DecoState.Burnt, 0.8f + violence * 2), (DecoState.Broken, 0.6f + violence * 3), (DecoState.Buried, 0.4f),
        };
        options.RemoveAll(o => !p.Has(o.s));
        if (options.Count == 0) return;
        int k = _rng.PickWeighted(options.Select(o => o.w).ToList());
        pl.State = options[k].s;
        pl.Cover = pl.State == DecoState.Buried ? _drift : DecoCover.Vines;
        if (pl.State is DecoState.Rusted or DecoState.Overgrown && _forge.Model(p.Id).Parts.Any(x => x.Is(PartFlags.Door)) && _rng.Chance(0.4f)) pl.Doors = true;
    }

    /// <summary>Places a piece if it stays inside the site, clashes with nothing and keeps the lane open.</summary>
    DecoPlacement Put(DecoPiece p, float x, float z, float yaw, float y = 0, DecoFall fall = DecoFall.None, int assembly = -1, bool weather = true, float violence = 0, string palette = null, DecoState? state = null)
    {
        if (p == null) return null;
        var pl = new DecoPlacement { Piece = p.Id, X = x, Z = z, Yaw = yaw, Y = y, Fall = fall, Assembly = assembly, Palette = palette ?? p.Palette, Age = Age, Exposed = true };
        if (state is DecoState s) { pl.State = p.Has(s) ? s : DecoState.Intact; pl.Age = Age; }
        else if (weather) Weather(pl, p, violence);
        if (fall != DecoFall.None && !p.Has(DecoState.Knocked)) pl.Fall = DecoFall.None;
        DecoRect r = DecoLayout.Footprint(_forge, pl);
        if (!DecoLayout.Inside(_room, r)) { pl.State = DecoState.Intact; pl.Doors = false; r = DecoLayout.Footprint(_forge, pl); if (!DecoLayout.Inside(_room, r)) return Skip(pl, "outside the site"); }
        DecoPlacement hit = _plan.Placements.FirstOrDefault(o => DecoLayout.Clash(_forge, o, pl));
        if (hit != null) return Skip(pl, $"clashes with {hit}");
        bool blocks = DecoLayout.Blocks(_forge, pl);
        if (blocks)
        {
            _raster.Add(r, 1);
            if (!_raster.DoorsJoined(_room)) { _raster.Add(r, -1); return Skip(pl, "would block the lane"); }
        }
        _plan.Placements.Add(pl);
        return pl;
    }

    /// <summary>Notes why a piece was left out (the plan's notes say what a site could not fit, and why).</summary>
    DecoPlacement Skip(DecoPlacement pl, string why)
    {
        _plan.Notes.Add($"left out {pl}: {why}");
        return null;
    }

    DecoPlacement PutVehicle(DecoPiece p, float x, float z, float yaw, float violence = 0, float flipChance = 0)
    {
        if (p == null) return null;
        string paint = p.Palette == "vehicles" ? Paints[_rng.Int(Paints.Length)] : p.Palette;
        DecoFall fall = p.Falls.Count > 0 && _rng.Chance(flipChance) ? p.Falls[_rng.Int(p.Falls.Count)] : DecoFall.None;
        return Put(p, x, z, yaw, 0, fall, -1, true, violence, paint)
            ?? (fall != DecoFall.None ? Put(p, x, z, yaw, 0, DecoFall.None, -1, true, violence, paint) : null);
    }

    // ---- roads -------------------------------------------------------------------------------------

    /// <summary>Power poles down one side of the road with three lines strung between them; one may be down.</summary>
    void PowerLine(float x, float z0, float z1, float spacing)
    {
        DecoPiece pole = Cat.Piece("power-pole");
        var poles = new List<int>();
        for (float z = z0; z <= z1; z += spacing)
        {
            bool down = poles.Count > 0 && _room.State != "active" && _rng.Chance(0.25f);
            DecoPlacement pl = down ? Put(pole, x, z, 0, 0, _rng.Chance(0.5f) ? DecoFall.Front : DecoFall.Back, -1, false, state: DecoState.Intact) : null;
            pl ??= Put(pole, x, z, 0, 0, DecoFall.None, -1, false, state: DecoState.Intact);
            if (pl == null) continue;
            pl.Age = Age;
            if (pl.Fall != DecoFall.None) pl.Note = "pole down";
            poles.Add(_plan.Placements.Count - 1);
        }
        for (int i = 0; i + 1 < poles.Count; i++)
            foreach (string a in new[] { "a", "b", "c" })
                StringLine(poles[i], poles[i + 1], a);
    }

    /// <summary>A wire between two placements' anchors: a sagging span, or a drop from the standing one if the other is down.</summary>
    void StringLine(int from, int to, string anchor)
    {
        DecoPlacement a = _plan.Placements[from], b = _plan.Placements[to];
        Vector3 pa = AnchorAt(a, anchor), pb = AnchorAt(b, anchor);
        var c = new DecoPlanCable { From = from, To = to, FromAnchor = anchor, ToAnchor = anchor, A = pa, B = pb, Slack = 0.015f, Radius = 0.02f };
        bool aDown = a.Fall != DecoFall.None, bDown = b.Fall != DecoFall.None;
        if (aDown && bDown) return;
        if (aDown || bDown)
        {
            // The standing pole still holds the wire; the rest lies along the ground towards the fallen one.
            if (aDown) { c.From = to; c.FromAnchor = anchor; c.A = pb; c.B = pa; }
            c.To = -1;
            c.Spanned = false;
        }
        _plan.Cables.Add(c);
    }

    /// <summary>A placed piece's named anchor in room coordinates.</summary>
    public Vector3 AnchorAt(DecoPlacement pl, string name)
    {
        DecoForge.Shape s = _forge.ShapeOf(pl.Piece, pl.State, pl.Fall, pl.Doors, pl.Age, pl.Exposed);
        return Vector3.Transform(s.Model.Anchors[name], pl.Transform);
    }

    void Jam()
    {
        PowerLine(1.1f, 4f, _room.Depth - 4f, 18f);
        var mix = Vehicles("car", "van", "pickup", "truck", "bus", "tractor", "cart", "hover", "emergency");
        float laneX = 10f, z = 2.5f;
        while (z < _room.Depth - 3f)
        {
            DecoPiece v = Any(mix);
            if (v == null) break;
            float len = v.Depth;
            float cz = z + len / 2;
            bool swerve = _rng.Chance(0.22f);
            float x = swerve ? laneX + _rng.Range(2.2f, 3.6f) : laneX + _rng.Range(-0.25f, 0.25f);
            float yaw = swerve ? _rng.Range(-35, 35) : _rng.Range(-4, 4);
            if (PutVehicle(v, x, cz, yaw, 0.1f, 0.03f) != null) z += len + _rng.Range(0.6f, 1.6f);
            else z += 1f;
        }
        // A couple slewed across the verge on the far side, and one abandoned in the clear lane's edge but not in it.
        for (int i = 0; i < 3; i++) PutVehicle(Any(mix), _rng.Range(1.0f, 2.2f), _rng.Range(5, _room.Depth - 5), _rng.Range(-60, 60), 0.2f, 0.15f);
        Litter(14);
    }

    void CarPark()
    {
        var cars = Vehicles("car", "van", "pickup", "hover");
        float aisle = _room.Width / 2;
        for (float z = 2.5f; z < _room.Depth - 2.5f; z += 2.9f)
            foreach (float s in new[] { -1f, 1f })
            {
                if (_rng.Chance(0.3f)) continue;
                DecoPiece c = Any(cars);
                if (c == null) continue;
                float x = aisle + s * (2.4f + 0.6f + c.Depth / 2);
                PutVehicle(c, x, z, s < 0 ? 90 : 270 + _rng.Range(-6, 6), 0, 0.02f);
            }
        foreach (float s in new[] { -1f, 1f })
            for (float z = 2f; z < _room.Depth; z += 5.8f) Put(Cat.Piece("jersey-barrier"), aisle + s * (_room.Width / 2 - 0.6f), z, 90, weather: false, state: DecoState.Intact);
        Litter(10);
    }

    void Roadblock()
    {
        PowerLine(1.1f, 3f, _room.Depth - 3f, 12f);
        DecoPiece barrier = Cat.Piece("jersey-barrier"), sand = Cat.Piece("sandbag-wall");
        float z = _room.Depth / 2;
        for (float x = 9f; x < _room.Width - 0.5f; x += 2.05f) Put(barrier, x, z, _rng.Range(-4, 4), state: Age == DecoAge.Fresh ? DecoState.Intact : (_rng.Chance(0.3f) ? DecoState.Overgrown : DecoState.Intact));
        Put(barrier, 1.6f, z + 0.5f, 25, state: DecoState.Intact);
        Put(barrier, 3.1f, z - 1.6f, -60, state: DecoState.Intact);
        for (float x = 9.5f; x < _room.Width - 1f; x += 1.6f) Put(sand, x, z + 1.6f, 0, weather: false, state: DecoState.Intact);
        var blockers = Vehicles("emergency", "truck", "pickup", "van");
        PutVehicle(Any(blockers), 11.5f, z + 6f, 80, 0.2f);
        PutVehicle(Any(blockers), 12.5f, z - 6f, 100, 0.2f);
        PutVehicle(Any(Vehicles("car", "van")), 10.5f, 4.5f, 10, 0.1f);
        Put(Cat.Piece("banner-stand"), 8.6f, z - 1.2f, 0, weather: false, state: DecoState.Intact);
        Litter(10);
    }

    void PileUp()
    {
        var mix = Vehicles("car", "van", "pickup", "truck", "bus", "hover");
        for (int i = 0, n = 0; i < 40 && n < 11; i++)
        {
            DecoPiece v = Any(mix);
            if (v == null) break;
            float x = _rng.Range(8.6f, _room.Width - 1.5f), z = _rng.Range(3, _room.Depth - 3);
            if (PutVehicle(v, x, z, _rng.Range(-80, 80), 1f, 0.3f) != null) n++;
        }
        Litter(18);
    }

    void DepotYard()
    {
        PowerLine(_room.Width - 1.1f, 3f, _room.Depth - 3f, 15f);
        var big = Vehicles("truck", "bus", "van");
        for (float x = 3f; x < _room.Width - 10f; x += 4.2f)
            for (float z = 4f; z < _room.Depth - 4f; z += 12.5f)
            {
                DecoPiece v = Any(big);
                if (v != null && _rng.Chance(0.8f)) PutVehicle(v, x, z + v.Depth / 2 - 4, _rng.Range(-3, 3), 0, 0);
            }
        PutVehicle(Any(Vehicles("industrial-vehicle")), _room.Width - 2.2f, 6f, 180);
        PutVehicle(Any(Vehicles("industrial-vehicle")), _room.Width - 2.4f, 20f, 175);
        for (int i = 0; i < 6; i++) Put(Cat.Piece(_rng.Chance(0.5f) ? "pallet-stack" : "cargo-crate"), _rng.Range(_room.Width - 4f, _room.Width - 1f), _rng.Range(26, _room.Depth - 2), _rng.Range(0, 90));
        Litter(12);
    }

    void LoadingDock()
    {
        DecoPiece dock = Cat.Piece("platform-edge");
        int asm = _assemblies++;
        for (float x = 2f; x < _room.Width - 1.9f; x += 4f) Put(dock, x, 1.5f, 90, assembly: asm, weather: false, state: DecoState.Intact);
        DecoPiece truck = Cat.Piece("box-truck");
        PutVehicle(truck, _room.Width / 2, 3.05f + truck.Depth / 2, 180);
        DecoPiece pallet = Cat.Piece("pallet-stack");
        foreach (float x in new[] { 3.5f, 6f, 15f, 18.5f }) Put(pallet, x, 1.3f, _rng.Range(-8, 8), 1.0f, assembly: -1, weather: true);
        Put(Cat.Piece("forklift"), 6f, 1.6f, 90, 1.0f);
        Put(Cat.Piece("forklift"), 16f, 13f, _rng.Range(0, 360));
        for (int i = 0; i < 4; i++) Put(Cat.Piece("cargo-crate"), _rng.Range(2, _room.Width - 2), _rng.Range(12, _room.Depth - 6), _rng.Range(0, 90));
        Litter(8);
    }

    // ---- airfields -------------------------------------------------------------------------------

    (string nose, string body, string torn, string wing, string tail, string engine) Liner() => _room.Tech == DecoTech.High
        ? ("skyliner-nose", "skyliner-body", "liner-cabin-torn", "skyliner-wing", "skyliner-tail", "skyliner-engine")
        : ("liner-nose", "liner-fuselage", "liner-cabin-torn", "liner-wing", "liner-tail", _room.Tech == DecoTech.Low ? "prop-engine" : "turbofan");

    void AirlinerCrash()
    {
        var (nose, body, torn, wing, tail, engine) = Liner();
        float cx = _room.Width / 2 + 3f;
        int asm = _assemblies++;
        foreach (float z in new[] { 14f, 34f, 54f }) Put(Cat.Piece("crash-scar"), cx + _rng.Range(-0.5f, 0.5f), z, _rng.Range(-3, 3), assembly: asm, state: Age == DecoAge.Fresh ? DecoState.Intact : (_rng.Chance(0.5f) ? DecoState.Overgrown : DecoState.Intact));
        // Along the scar: the tail where it first hit, then the fuselage broken in pieces, the nose furthest on.
        Put(Cat.Piece(tail), cx - 1.5f, 8f, 160 + _rng.Range(-15, 15), violence: 1);
        Put(Cat.Piece(body), cx + 0.6f, 25f, _rng.Range(-14, 14), violence: 1);
        Put(Cat.Piece(torn), cx - 0.4f, 38f, _rng.Range(-18, 18), violence: 1);
        Put(Cat.Piece(nose), cx + 1.2f, 58f, _rng.Range(-20, 20), violence: 1);
        Put(Cat.Piece(wing), cx + 1f, 47f, _rng.Range(-25, 25), state: Age == DecoAge.Fresh ? DecoState.Broken : DecoState.Broken);
        Put(Cat.Piece(engine), cx + 13f, 30f, _rng.Range(0, 360), violence: 1);
        Put(Cat.Piece(engine), cx - 11f, 52f, _rng.Range(0, 360), violence: 1);
        Put(Cat.Piece("landing-gear"), cx + 6f, 18f, _rng.Range(0, 360), fall: DecoFall.Left, violence: 1);
        for (int i = 0; i < 8; i++) Put(Cat.Piece("crash-debris"), cx + _rng.Range(-12, 12), _rng.Range(6, _room.Depth - 6), _rng.Range(0, 360), violence: 1);
        Litter(16);
    }

    void AirlinerGate()
    {
        var (nose, body, _, wing, tail, engine) = Liner();
        bool high = _room.Tech == DecoTech.High;
        float cx = _room.Width / 2 + 4f, raise = 2.4f;
        int asm = _assemblies++;
        float z = 6f;
        DecoPiece n = Cat.Piece(nose), b = Cat.Piece(body), t = Cat.Piece(tail), w = Cat.Piece(wing), e = Cat.Piece(engine);
        // Intact but derelict: weathered, its doors open, standing on its gear.
        DecoState worn = Age == DecoAge.Fresh ? DecoState.Intact : (_rng.Chance(0.6f) ? DecoState.Rusted : DecoState.Overgrown);
        DecoPlacement Part(DecoPiece p, float x, float zz, float y, bool doors = false)
        {
            var pl = Put(p, x, zz, 0, y, assembly: asm, weather: false, state: p.Has(worn) ? worn : DecoState.Intact);
            if (pl != null) { pl.Doors = doors && Age != DecoAge.Fresh; pl.Cover = DecoCover.Vines; }
            return pl;
        }
        Part(n, cx, z + n.Depth / 2, raise);   // its door stays shut against the airbridge
        z += n.Depth;
        float wingZ = z + b.Depth * 0.9f;
        Part(b, cx, z + b.Depth / 2, raise, true);
        z += b.Depth;
        Part(b, cx, z + b.Depth / 2, raise);
        z += b.Depth;
        float tailZ = z + t.Depth / 2 - 0.2f;
        Part(t, cx, tailZ, raise);
        Part(w, cx, wingZ, raise + (high ? 0.4f : 0.7f));
        // Turbofans and propellers hang under the wings; a skyliner's ring thrusters sit either side of its tail.
        if (high) foreach (float s in new[] { -1f, 1f }) Part(e, cx + s * 4.2f, tailZ - 1.5f, raise + 1.85f - 1.92f);
        else foreach (float s in new[] { -1f, 1f }) Part(e, cx + s * 6.5f, wingZ - 1.2f, _room.Tech == DecoTech.Low ? 0.6f : 0.35f);
        DecoPiece gear = Cat.Piece("landing-gear");
        Part(gear, cx, 8f, 0);
        foreach (float s in new[] { -1f, 1f }) Part(gear, cx + s * 2.6f, wingZ + 1.2f, 0);
        // The airbridge docked at the nose door, and the stairs run up to the rear door behind the wing.
        float side = n.Width / 2 + 0.1f;
        DecoPart door = _forge.Model(n.Id).Parts.FirstOrDefault(p => p.Is(PartFlags.Door));
        float doorZ = 6f + n.Depth / 2 + (door != null ? door.Centre.Z : n.Depth * 0.1f);
        var bridge = Cat.Piece("airbridge");
        Put(bridge, cx - side - bridge.Depth / 2, doorZ, 270, weather: false, state: DecoState.Intact);
        var stairs = Cat.Piece("boarding-stairs");
        float rearZ = 6f + n.Depth + 2 * b.Depth - stairs.Width / 2 - 1.2f;
        Put(stairs, cx - side - stairs.Depth / 2, rearZ, 270, weather: false, state: worn == DecoState.Overgrown ? DecoState.Overgrown : (stairs.Has(worn) ? worn : DecoState.Intact));
        Litter(10);
    }

    // ---- rail ------------------------------------------------------------------------------------

    const float RailTop = 0.32f, GuideTop = 0.9f;

    (List<DecoPiece> stock, DecoPiece track, float top) RailKit(bool freight = false)
    {
        bool high = _room.Tech == DecoTech.High;
        DecoPiece track = Cat.Piece(high ? "maglev-guideway" : "rail-track");
        var stock = new List<DecoPiece>();
        if (high) { stock.Add(Cat.Piece("maglev-head")); for (int i = 0; i < 3; i++) stock.Add(Cat.Piece("maglev-coach")); }
        else
        {
            stock.Add(Cat.Piece("diesel-loco"));
            string[] cars = freight || _room.Tech == DecoTech.Low ? new[] { "box-wagon", "tank-wagon", "flat-wagon", "log-wagon" } : new[] { "passenger-coach" };
            for (int i = 0; i < 3; i++) stock.Add(Cat.Piece(cars[_rng.Int(cars.Length)]));
        }
        return (stock, track, high ? GuideTop : RailTop);
    }

    void Track(DecoPiece track, float x, int asm)
    {
        for (float z = 2f; z <= _room.Depth - 2f; z += 4f)
            Put(track, x, z, 0, assembly: asm, weather: false, state: Age == DecoAge.Fresh ? DecoState.Intact : (_rng.Chance(0.35f) && track.Has(DecoState.Overgrown) ? DecoState.Overgrown : (track.Has(DecoState.Rusted) && _rng.Chance(0.5f) ? DecoState.Rusted : DecoState.Intact)));
    }

    void Train(bool derailed)
    {
        var (stock, track, top) = RailKit();
        float tx = derailed ? 9f : _room.Width / 2 + 1.5f;
        int asm = _assemblies++;
        Track(track, tx, asm);
        float z = 3f;
        int car = 0;
        foreach (DecoPiece p in stock)
        {
            if (z + p.Depth > _room.Depth - 1f) break;
            if (!derailed)
            {
                var pl = Put(p, tx, z + p.Depth / 2, 0, top, assembly: asm);
                if (pl != null && pl.State is DecoState.Buried) pl.State = DecoState.Rusted;
            }
            else
            {
                // Jack-knifed: each car slews further off the line, the middle ones on their sides.
                float off = car == 0 ? 0 : _rng.Range(2.5f, 5f) * (car % 2 == 0 ? 1 : 1.6f);
                float yaw = car == 0 ? _rng.Range(-6, 6) : (car % 2 == 0 ? 1 : -1) * _rng.Range(18, 38);
                DecoFall fall = car > 0 && p.Has(DecoState.Knocked) && _rng.Chance(0.6f) ? DecoFall.Left : DecoFall.None;
                // Off a guideway that stands proud of the ground, a car lies clear of it; on rails it may lie across them.
                if (car > 0 && !track.WalkOver)
                {
                    var (lo, hi) = DecoLayout.LocalBounds(_forge, p, DecoState.Intact, fall, false);
                    var (near, _) = DecoLayout.RectOf(lo, hi, 0, 0, yaw).Bounds();
                    off = MathF.Max(off, track.Width / 2 - near.X + 0.5f);
                }
                Put(p, tx + off, z + p.Depth / 2, yaw, fall == DecoFall.None && car == 0 ? top : 0, fall, car == 0 ? asm : -1, violence: 1);
            }
            z += p.Depth + (derailed ? 2.2f : 0.4f);
            car++;
        }
        DecoPiece signal = Cat.Piece("rail-signal");
        Put(signal, tx - 2.6f, 6f, 0, weather: false, state: DecoState.Intact);
        if (derailed) Litter(14); else Litter(6);
    }

    void Station()
    {
        var (stock, track, top) = RailKit();
        float tx = 5f;
        int asm = _assemblies++;
        Track(track, tx, asm);
        DecoPiece platform = Cat.Piece("platform-edge"), canopy = Cat.Piece("station-canopy");
        float px = tx + 3.6f;
        for (float z = 4f; z < _room.Depth - 2f; z += 4f) Put(platform, px, z, 0, assembly: asm, weather: false, state: DecoState.Intact);
        for (float z = 8f; z < _room.Depth - 6f; z += 8f) Put(canopy, px, z, 0, 0.92f, assembly: asm, weather: false, state: Age == DecoAge.Fresh ? DecoState.Intact : DecoState.Broken);
        float zz = 3f;
        foreach (DecoPiece p in stock)
        {
            if (zz + p.Depth > _room.Depth - 1f) break;
            var pl = Put(p, tx, zz + p.Depth / 2, 0, top, assembly: asm);
            if (pl != null) { if (pl.State is DecoState.Buried) pl.State = DecoState.Rusted; pl.Doors = Age != DecoAge.Fresh; }
            zz += p.Depth + 0.4f;
        }
        for (int i = 0; i < 4; i++) Put(Cat.Piece(_rng.Chance(0.5f) ? "seat-row" : "suitcase"), px + _rng.Range(-0.3f, 0.8f), _rng.Range(6, _room.Depth - 6), 270, 0.92f);
        Litter(6);
    }

    void RailDepot()
    {
        var (_, track, top) = RailKit(freight: true);
        int asm = _assemblies++;
        string[] wagons = { "box-wagon", "tank-wagon", "flat-wagon", "log-wagon", "diesel-loco" };
        for (int i = 0; i < 4; i++)
        {
            float tx = 3f + i * 5f;
            Track(track, tx, asm);
            Put(Cat.Piece("buffer-stop"), tx, 1.05f, 0, assembly: asm, weather: false, state: DecoState.Intact);
            float z = 2.6f;
            while (z < _room.Depth - 8f)
            {
                DecoPiece w = Cat.Piece(_room.Tech == DecoTech.High ? "maglev-coach" : wagons[_rng.Int(wagons.Length)]);
                if (_rng.Chance(0.25f)) { z += 6f; continue; }
                if (Put(w, tx, z + w.Depth / 2, 0, top, assembly: asm) != null) z += w.Depth + 0.5f;
                else z += 2f;
            }
        }
        Put(Cat.Piece("rail-signal"), _room.Width - 5f, 8f, 0, weather: false, state: DecoState.Intact);
        Put(Cat.Piece("rail-signal"), _room.Width - 5f, 30f, 0, weather: false, state: DecoState.Intact);
        PowerLine(_room.Width - 1.1f, 3f, _room.Depth - 3f, 17f);
        Litter(10);
    }

    /// <summary>Scattered junk: crates, barrels, suitcases dropped as people fled; never in the clear lane.</summary>
    void Litter(int count)
    {
        string[] junk = _room.Site is "airliner-crash" ? new[] { "suitcase", "crash-debris" } : new[] { "suitcase", "oil-barrel", "cargo-crate", "ammo-crate", "feed-sacks" };
        for (int i = 0, tries = 0; i < count && tries < count * 4; tries++)
        {
            DecoPiece p = Cat.Piece(junk[_rng.Int(junk.Length)]);
            DecoFall fall = p.Falls.Count > 0 && _rng.Chance(0.5f) ? p.Falls[_rng.Int(p.Falls.Count)] : DecoFall.None;
            if (Put(p, _rng.Range(1, _room.Width - 1), _rng.Range(1, _room.Depth - 1), _rng.Range(0, 360), 0, fall) != null) i++;
        }
    }
}
