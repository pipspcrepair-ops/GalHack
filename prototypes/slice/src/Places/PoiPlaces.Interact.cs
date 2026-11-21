using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GalHack.Deco;
using GalHack.Items;
using Godot;
using PoiGenerator.Gen;
using Slice.Game;

namespace Slice.Places;

/// <summary>The slice's numbers for places (data/places.json).</summary>
public sealed class PlaceRules
{
    public float BuildRadius { get; set; } = 320f;
    public float DropRadius { get; set; } = 460f;
    public double BudgetMs { get; set; } = 2.5;
    public float AheadSeconds { get; set; } = 6f;
    /// <summary>A place with more decorations than this is dressed room by room as you near each room: rooms within the radius stand, past the far one they go.</summary>
    public int DressAllUnder { get; set; } = 300;
    public float DressRadius { get; set; } = 64f;
    public float UndressRadius { get; set; } = 84f;
    /// <summary>A cube of a place within this of the eye is drawn in full (past the far one it goes back to the merged drawing).</summary>
    public float NearRadius { get; set; } = 64f;
    public float NearDrop { get; set; } = 90f;
    public SalvageRules Salvage { get; set; } = new();

    public sealed class SalvageRules
    {
        public float ToolDamage { get; set; } = 60f;
        public double ReturnRate { get; set; } = 0.5;
        public Dictionary<string, Dictionary<string, double>> Materials { get; set; } = new();
    }

    static PlaceRules _shared;
    public static PlaceRules Shared => _shared ??= JsonSerializer.Deserialize<PlaceRules>(File.ReadAllText(GameData.Own("data/places.json")),
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
}

/// <summary>
/// Something outside a place's own blocks that the Sleeper tool can salvage: the POI and
/// infrastructure agent's flavour ruins and vehicle wrecks register here. It holds a finite
/// bill; each <see cref="HitPoints"/> of work takes the next share of it, and once it is empty
/// <see cref="Emptied"/> runs (take the wreck's last panels away, say).
/// </summary>
public sealed class SalvageSite
{
    public string Name = "Wreck";
    /// <summary>What the tool's ray meets (any collision object on a layer the tool aims at).</summary>
    public CollisionObject3D Body;
    public Bill Left = new();
    public int Shares = 6;
    public float HitPoints = 300f;
    public Action<SalvageSite> Emptied;
    internal float Damage;
    internal int Taken;
    public bool Empty => Left.IsEmpty;
}

public partial class PoiPlaces
{
    public PlaceRules Rules = PlaceRules.Shared;

    readonly Dictionary<ulong, SalvageSite> _salvage = new();

    /// <summary>Registers something the tool can salvage (flavour ruins, vehicle wrecks).</summary>
    public void AddSalvage(SalvageSite site) => _salvage[site.Body.GetInstanceId()] = site;
    public void RemoveSalvage(SalvageSite site) => _salvage.Remove(site.Body.GetInstanceId());
    /// <summary>Things the tool can salvage standing round the world right now (tests).</summary>
    public IEnumerable<SalvageSite> SalvageSites => _salvage.Values;

    /// <summary>A site of abandoned vehicles stands: each is the tool's to take apart for what it's made of, and once stripped it stays gone.</summary>
    public void WreckStood(PlanetSurface.Infra.WreckSite site, GalHack.Deco.Engine.DecoPlanView view)
    {
        Decor decor = Decor.Shared;
        if (decor == null) return;
        for (int i = 0; i < view.Nodes.Count; i++)
        {
            GalHack.Deco.Engine.DecoNode node = view.Nodes[i];
            if (node.Body == null) continue;
            DecoPiece piece = decor.Catalogue.Piece(node.Look.Piece);
            Bill bill = piece.Salvage != null ? decor.Catalogue.SalvageYield(piece.Salvage) : piece.Bill;
            var left = new Bill();
            foreach (var (id, n) in bill)
                if (Zone.Data.Items.Has(id)) left.Add(id, n);
            if (left.IsEmpty) continue;
            long id2 = PlanetSurface.Infra.InfraLayer.WreckId(site, view.Plan.Placements.IndexOf(node.Placement ?? view.Plan.Placements[Math.Min(i, view.Plan.Placements.Count - 1)]));
            var s = new SalvageSite { Name = piece.Name, Body = node.Body, Left = left, Shares = 4, HitPoints = (piece.Hp > 0 ? piece.Hp : 400f) / 4f };
            s.Emptied = _ =>
            {
                Zone.Planet.Felled.Add(id2);
                RemoveSalvage(s);
                node.QueueFree();
            };
            AddSalvage(s);
            node.TreeExiting += () => RemoveSalvage(s);
        }
    }

    public SalvageSite SalvageOf(GodotObject collider) => collider != null && _salvage.TryGetValue(collider.GetInstanceId(), out SalvageSite s) ? s : null;

    /// <summary>Blocks and pieces salvaged so far, and hidden walls broken (tests and stats).</summary>
    public int Salvaged { get; private set; }
    public int WallsBroken { get; private set; }

    /// <summary>A hidden room's wall broke open (the place, the room).</summary>
    public event Action<Place, SecretRoom> WallBroken;

    /// <summary>What the crosshair says over a place's block or piece with the tool in gather mode: only what can be taken.</summary>
    public string Label(Hit h)
    {
        if (h.Piece != null) return h.Piece.Piece.Name.ToUpperInvariant();
        if (h.Door != null) return "";
        if (h.Place.SecretAt(h.Cell) != null) return "CRACKED PANEL";
        // A plain wall says nothing (a clean crosshair) until you are working it.
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        return h.Place.Memory.BlockDamage.ContainsKey(h.Cell) && Yield(b) != null ? b.Material.ToString().ToUpperInvariant() : "";
    }

    /// <summary>What one block gives back, before the return rate (null: the tool can't take it).</summary>
    public Dictionary<string, double> Yield(Block b)
    {
        if (b.IsAir || b.Type is BlockType.Door or BlockType.Cache or BlockType.Core or BlockType.Terminal or BlockType.SealedDoor) return null;
        if (!Rules.Salvage.Materials.TryGetValue(b.Material.ToString().ToLowerInvariant(), out var per)) return null;
        float share = b.Type switch
        {
            BlockType.Cube or BlockType.Breakable => 1f,
            BlockType.Ramp or BlockType.Stair or BlockType.Half or BlockType.Chamfer => 0.5f,
            BlockType.Corner or BlockType.ChamferCorner => 1f / 6f,
            BlockType.InvCorner or BlockType.ChamferInvCorner => 5f / 6f,
            BlockType.Quarter => 0.25f,
            // (a round thing's rim, by the share of the cube its plan keeps)
            BlockType.Wedge => 0.5f,
            BlockType.WedgeTip => 0.125f,
            BlockType.WedgeClip => 0.875f,
            BlockType.Slant or BlockType.SlantM => 0.25f,
            BlockType.SlantFill or BlockType.SlantFillM => 0.75f,
            BlockType.RoundOut => MathF.PI / 4f,
            BlockType.RoundIn => 1f - MathF.PI / 4f,
            BlockType.RoundSlope => 0.5f, // (cut by its round's profile, about half the cube on the whole)
            BlockType.Partition or BlockType.Beam => 0.2f,
            BlockType.Column => 0.5f,
            BlockType.Rib => 0.4f,
            BlockType.Pillar => 0.25f,
            BlockType.Lattice => 0.3f,
            BlockType.Pane => 0.17f,
            BlockType.Machine or BlockType.Tank or BlockType.Shelf or BlockType.Locker or BlockType.Crate => 0.6f,
            _ => 0.3f,
        };
        return per.ToDictionary(kv => kv.Key, kv => kv.Value * share);
    }

    /// <summary>A place's block's hit points and its most (worn by the tool and shots), or null for dressing, a door or air.</summary>
    public (float Hp, float Max)? Health(Hit h)
    {
        if (h.Piece != null || h.Door != null) return null;
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        if (b.IsAir) return null;
        float max = BlockInfo.HitPoints(b);
        return (MathF.Max(0f, max - h.Place.Memory.BlockDamage.GetValueOrDefault(h.Cell)), max);
    }

    /// <summary>
    /// One dig pulse of the Sleeper tool on a place: a hidden room's wall breaks (60 points, one
    /// pulse), a piece of the room's dressing or a block comes away once its hit points are worked
    /// through, giving its materials (the piece's salvage class or bill, the block's material). In
    /// a town that is anyone's but yours, the people take it badly.
    /// </summary>
    public (Bill Got, string Message) Work(Hit h, float damage)
    {
        PlaceMemory m = h.Place.Memory;
        if (m.Town != null && m.Town.Covers(h.Place.WorldOf(h.Cell)) && m.Mood != Mood.Hostile)
        {
            m.Town.Provoked();
            return (null, "TOWN · PROVOKED");
        }
        if (h.Place.SecretAt(h.Cell) is SecretRoom secret)
            return (null, Hurt(h.Place, secret, damage) ? "PANEL BROKEN · HIDDEN ROOM" : "PANEL CRACKED");
        if (h.Piece != null) return WorkPiece(h, damage);
        if (h.Door != null) return (null, "");
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        Dictionary<string, double> yield = Yield(b);
        if (yield == null) return (null, b.IsAir ? "" : "TOOL CAN'T CUT");
        float hp = BlockInfo.HitPoints(b);
        float done = m.BlockDamage.GetValueOrDefault(h.Cell) + damage;
        if (done < hp)
        {
            m.BlockDamage[h.Cell] = done;
            return (null, "");
        }
        m.BlockDamage.Remove(h.Cell);
        h.Place.Inst.Blocks.Set(h.Cell, Block.Air);
        Removed(h.Place, new List<Int3> { h.Cell });
        Salvaged++;
        return (Pay(m, yield.Select(kv => (kv.Key, kv.Value))), $"Salvaged: {b.Material.ToString().ToLowerInvariant()} {b.Type.ToString().ToLowerInvariant()}");
    }

    static float HpOf(DecoPiece piece) => piece.Hp > 0 ? piece.Hp : 120f;

    /// <summary>
    /// A gather blast's wear on a place's door or piece of dressing: <paramref name="damage"/> hit points off it,
    /// and what that share of it is worth (item, amount before the tool's extract share). Worked through, it
    /// comes away as it does otherwise: a piece taken from its room (a hazard goes up), a door out of its doorway.
    /// Anything else (a plain block, a lift's door) takes nothing and gives nothing.
    /// </summary>
    public IEnumerable<(string Id, double Amount)> Wear(Hit h, float damage)
    {
        PlaceMemory m = h.Place.Memory;
        Dictionary<string, double> whole;
        float hp, done, dealt;
        if (h.Piece != null)
        {
            var key = (h.Piece.Room.Module, h.Piece.Index);
            hp = HpOf(h.Piece.Piece);
            done = m.DecoDamage.GetValueOrDefault(key) + damage;
            dealt = MathF.Min(damage, hp - (done - damage));
            whole = (h.Piece.Piece.Salvage != null ? Decor.Shared.Catalogue.SalvageYield(h.Piece.Piece.Salvage) : h.Piece.Piece.Bill).ToDictionary(kv => kv.Key, kv => (double)kv.Value);
            if (done < hp) m.DecoDamage[key] = done;
            else if (h.Piece.Piece.Blast != null) Detonate(h.Place, h.Piece);
            else
            {
                m.DecoDamage.Remove(key);
                h.Place.TakePiece(h.Piece);
                Salvaged++;
            }
        }
        else if (h.Door is { Lift: < 0 } door && h.Place.Inst.Blocks.Get(door.Cells.First(c => h.Place.Inst.Blocks.Get(c).Type == BlockType.Door)) is var b)
        {
            hp = BlockInfo.HitPoints(b) * door.Cells.Count;
            done = m.DoorDamage.GetValueOrDefault(door.Index) + damage;
            dealt = MathF.Min(damage, hp - (done - damage));
            whole = Rules.Salvage.Materials.TryGetValue(b.Material.ToString().ToLowerInvariant(), out var per) ? per.ToDictionary(kv => kv.Key, kv => kv.Value * door.Cells.Count * 0.5) : new();
            if (done < hp) m.DoorDamage[door.Index] = done;
            else
            {
                m.DoorDamage.Remove(door.Index);
                h.Place.TakeDoor(door);
                Salvaged++;
            }
        }
        else return Enumerable.Empty<(string, double)>();
        return whole.Select(kv => (kv.Key, kv.Value * dealt / hp));
    }

    (Bill, string) WorkPiece(Hit h, float damage)
    {
        DecoPiece piece = h.Piece.Piece;
        PlaceMemory m = h.Place.Memory;
        var key = (h.Piece.Room.Module, h.Piece.Index);
        float hp = HpOf(piece);
        float done = m.DecoDamage.GetValueOrDefault(key) + damage;
        if (done < hp)
        {
            m.DecoDamage[key] = done;
            return (null, "");
        }
        m.DecoDamage.Remove(key);
        h.Place.TakePiece(h.Piece);
        Salvaged++;
        Bill bill = piece.Salvage != null ? Decor.Shared.Catalogue.SalvageYield(piece.Salvage) : piece.Bill;
        return (Pay(m, bill.Select(kv => (kv.Key, (double)kv.Value))), $"Salvaged: {piece.Name}");
    }

    /// <summary>The return rate applied, with fractions carried over in the place's memory, as whole items.</summary>
    Bill Pay(PlaceMemory m, IEnumerable<(string Id, double Amount)> yield)
    {
        var bill = new Bill();
        foreach (var (id, amount) in yield)
        {
            if (!Zone.Data.Items.Has(id)) continue;
            double have = m.SalvageCarry.GetValueOrDefault(id) + amount * Rules.Salvage.ReturnRate;
            int whole = (int)Math.Floor(have + 1e-9);
            m.SalvageCarry[id] = have - whole;
            if (whole > 0) bill.Add(id, whole);
        }
        return bill;
    }

    /// <summary>A pulse of work on a registered salvage site (a flavour ruin, a vehicle wreck).</summary>
    public (Bill Got, string Message) Work(SalvageSite s, float damage)
    {
        if (s.Empty) return (null, $"{s.Name} · EMPTY");
        s.Damage += damage;
        if (s.Damage < s.HitPoints) return (null, "");
        s.Damage = 0f;
        s.Taken++;
        var share = new Bill();
        int left = Math.Max(1, s.Shares - s.Taken + 1);
        foreach (var (id, n) in s.Left.ToList())
        {
            int take = Math.Max(1, (int)Math.Ceiling(n / (double)left));
            share.Add(id, Math.Min(n, take));
        }
        s.Left = s.Left.Minus(share);
        Salvaged++;
        if (s.Empty) s.Emptied?.Invoke(s);
        return (share, s.Empty ? $"{s.Name} · STRIPPED" : $"SALVAGED · {s.Name}");
    }

    /// <summary>
    /// A bolt struck a place: a hidden room's cracked wall gives (a few shots open it), a faction's core can
    /// be shot out, and a hazard (a blasting crate, a fuel drum) shot through its hit points goes up. True if a wall broke.
    /// </summary>
    public bool Shot(Hit h, float damage)
    {
        if (h.Piece != null)
        {
            if (h.Piece.Piece.Blast == null) return false; // a bolt marks the rest of the dressing; the tool takes it apart
            var key = (h.Piece.Room.Module, h.Piece.Index);
            float hurt = h.Place.Memory.DecoDamage.GetValueOrDefault(key) + damage;
            h.Place.Memory.DecoDamage[key] = hurt;
            if (hurt >= h.Piece.Piece.Hp) Detonate(h.Place, h.Piece);
            return false;
        }
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        if (b.Type == BlockType.Core)
        {
            PlaceMemory m = h.Place.Memory;
            float done = m.BlockDamage.GetValueOrDefault(h.Cell) + damage;
            m.BlockDamage[h.Cell] = done;
            if (done >= BlockInfo.HitPoints(b))
            {
                h.Place.Inst.Blocks.Set(h.Cell, Block.Air);
                Removed(h.Place, new List<Int3> { h.Cell });
                CoreDestroyed?.Invoke(h.Place);
            }
            return false;
        }
        return h.Place.SecretAt(h.Cell) is SecretRoom s && Hurt(h.Place, s, damage);
    }

    /// <summary>A faction's core was shot out (no start world has one): the place is nobody's now.</summary>
    public event Action<Place> CoreDestroyed;

    /// <summary>
    /// A hazard went up: its heart (zone frame), its reach and its damage at the heart. The place's own
    /// blocks have taken it already; the game hurts the survivor, the creatures and the structures round it.
    /// </summary>
    public event Action<Vector3, float, float> Exploded;

    /// <summary>How long after one hazard goes up a neighbour in its reach follows: a ripple, not one bang.</summary>
    public const float ChainDelay = 0.35f;

    /// <summary>Hazards set off by a neighbour, and when they go.</summary>
    readonly List<(Place Place, int Module, int Index, double At)> _fuses = new();
    double _now;

    /// <summary>Explosions so far (tests).</summary>
    public int Explosions { get; private set; }

    /// <summary>
    /// A hazard goes up: it's gone from the room, and its blast (<see cref="Blast"/>) hurts every standing place's blocks, doors and
    /// dressing within its reach by how near they are, and sets every other hazard there to follow after <see cref="ChainDelay"/>.
    /// </summary>
    public void Detonate(Place p, DecoHit hit)
    {
        DecoBlast blast = hit.Piece.Blast;
        if (blast == null || !hit.Node.Visible) return;
        Vector3 at = hit.Node.GlobalPosition + Vector3.Up * (hit.Piece.Height * 0.5f);
        p.Memory.DecoDamage.Remove((hit.Room.Module, hit.Index));
        p.TakePiece(hit);
        Explosions++;
        Blast(at, blast.Radius, blast.Damage);
        Exploded?.Invoke(at, blast.Radius, blast.Damage);
    }

    /// <summary>Lights the fuses whose time has come (each frame).</summary>
    void BurnFuses(double delta)
    {
        _now += delta;
        for (int i = _fuses.Count - 1; i >= 0; i--)
        {
            var (q, module, index, when) = _fuses[i];
            if (when > _now) continue;
            _fuses.RemoveAt(i);
            if (!Standing.Contains(q)) continue;
            DecoHit other = q.Pieces().FirstOrDefault(h => h.Room.Module == module && h.Index == index);
            if (other != null) Detonate(q, other);
        }
    }

    bool Hurt(Place p, SecretRoom s, float damage)
    {
        float done = p.Memory.WallDamage.GetValueOrDefault(s) + damage;
        p.Memory.WallDamage[s] = done;
        if (done < BlockInfo.HitPoints(new Block(BlockType.Breakable, PoiGenerator.Gen.Material.Concrete, default, Role.Secret))) return false;
        p.Break(s);
        WallsBroken++;
        WallBroken?.Invoke(p, s);
        return true;
    }

    /// <summary>Every town with its people about: each place standing with people, and any with one of theirs out on a run with the place down.</summary>
    public readonly HashSet<Town> Towns = new();

    /// <summary>The town whose area a world point is in, if any place standing has people.</summary>
    public Town TownAt(Vector3 world) => Towns.FirstOrDefault(t => t.Covers(world));

    /// <summary>The town whose ground a world point is on, out to a reach beyond its edge (the people met on its roads are its own), if any place standing has people.</summary>
    public Town TownHolding(Vector3 world, float reach) => Towns.Where(t => t.Beyond(world) < reach).OrderBy(t => t.Beyond(world)).FirstOrDefault();

    /// <summary>Someone of any town about you could rob now (their hands up to you, close by), in town or out on the land.</summary>
    public Town.Person Robbable(Vector3 world) => Towns.Select(t => t.Robbable(world)).FirstOrDefault(p => p != null);

    /// <summary>The townsperson nearest along a ray, in any standing place (with <paramref name="dead"/>, the body).</summary>
    public Town.Person PersonAlong(Vector3 eye, Vector3 dir, float reach, out float distance, bool dead = false)
    {
        distance = reach;
        Town.Person best = null;
        foreach (Town t in Towns)
        {
            Town.Person q = t.Along(eye, dir, distance, out float d, dead);
            if (q == null) continue;
            best = q;
            distance = d;
        }
        return best;
    }
}
