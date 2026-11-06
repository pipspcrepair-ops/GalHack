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
/// Something outside a place's own blocks that the survival tool can salvage: the POI and
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
            BlockType.Ramp or BlockType.Stair => 0.5f,
            BlockType.Rib => 0.4f,
            BlockType.Pillar => 0.25f,
            BlockType.Lattice => 0.3f,
            BlockType.Pane => 0.17f,
            BlockType.Machine or BlockType.Tank or BlockType.Shelf or BlockType.Locker or BlockType.Crate => 0.6f,
            _ => 0.3f,
        };
        return per.ToDictionary(kv => kv.Key, kv => kv.Value * share);
    }

    /// <summary>
    /// One dig pulse of the survival tool on a place: a hidden room's wall breaks (60 points, one
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
            return (null, "You're taking their town apart: they won't stand for it");
        }
        if (h.Place.SecretAt(h.Cell) is SecretRoom secret)
            return (null, Hurt(h.Place, secret, damage) ? "The panel gives way: a room behind it" : "The panel cracks");
        if (h.Piece != null) return WorkPiece(h, damage);
        if (h.Door != null) return (null, "");
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        Dictionary<string, double> yield = Yield(b);
        if (yield == null) return (null, b.IsAir ? "" : "The tool can't cut that");
        float hp = BlockInfo.HitPoints(b);
        float done = m.BlockDamage.GetValueOrDefault(h.Cell) + damage;
        if (done < hp)
        {
            m.BlockDamage[h.Cell] = done;
            return (null, "");
        }
        m.BlockDamage.Remove(h.Cell);
        h.Place.Inst.Blocks.Set(h.Cell, Block.Air);
        h.Place.Remesh(new[] { h.Cell });
        Salvaged++;
        return (Pay(m, yield.Select(kv => (kv.Key, kv.Value))), $"Salvaged: {b.Material.ToString().ToLowerInvariant()} {b.Type.ToString().ToLowerInvariant()}");
    }

    (Bill, string) WorkPiece(Hit h, float damage)
    {
        DecoPiece piece = h.Piece.Piece;
        PlaceMemory m = h.Place.Memory;
        var key = (h.Piece.Room.Module, h.Piece.Index);
        float hp = piece.Hp > 0 ? piece.Hp : 120f;
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
        if (s.Empty) return (null, $"{s.Name}: nothing left to take");
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
        return (share, s.Empty ? $"{s.Name}: stripped bare" : $"Salvaged from the {s.Name.ToLowerInvariant()}");
    }

    /// <summary>A bolt struck a place: a hidden room's cracked wall gives (a few shots open it), and a faction's core can be shot out. True if a wall broke.</summary>
    public bool Shot(Hit h, float damage)
    {
        Block b = h.Place.Inst.Blocks.Get(h.Cell);
        if (b.Type == BlockType.Core)
        {
            PlaceMemory m = h.Place.Memory;
            float done = m.BlockDamage.GetValueOrDefault(h.Cell) + damage;
            m.BlockDamage[h.Cell] = done;
            if (done >= BlockInfo.HitPoints(b))
            {
                h.Place.Inst.Blocks.Set(h.Cell, Block.Air);
                h.Place.Remesh(new[] { h.Cell });
                CoreDestroyed?.Invoke(h.Place);
            }
            return false;
        }
        return h.Place.SecretAt(h.Cell) is SecretRoom s && Hurt(h.Place, s, damage);
    }

    /// <summary>A faction's core was shot out (no start world has one): the place is nobody's now.</summary>
    public event Action<Place> CoreDestroyed;

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

    /// <summary>The town whose area a world point is in, if any place standing has people.</summary>
    public Town TownAt(Vector3 world) => Standing.Select(p => p.Memory.Town).FirstOrDefault(t => t != null && t.Covers(world));

    /// <summary>The townsperson nearest along a ray, in any standing place.</summary>
    public Town.Person PersonAlong(Vector3 eye, Vector3 dir, float reach, out float distance)
    {
        distance = reach;
        Town.Person best = null;
        foreach (Place p in Standing)
        {
            if (p.Memory.Town is not Town t) continue;
            Town.Person q = t.Along(eye, dir, distance, out float d);
            if (q == null) continue;
            best = q;
            distance = d;
        }
        return best;
    }
}
