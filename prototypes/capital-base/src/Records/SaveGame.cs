using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CapitalBase.World;
using GalHack.Persistence;
using GalHack.Physics;
using GalHack.Structures;
using EditLog = TerrainDigging.Terrain.EditLog;
using LoadResult = GalHack.Persistence.LoadResult;
using N = System.Numerics;
using Structure = GalHack.Structures.Structure;

namespace CapitalBase.Records;

public sealed class Stack
{
    public string Item { get; set; }
    public int Count { get; set; }
}

/// <summary>A capital ship, base or vessel: its blocks as building's blueprint text, where it is and what it holds.</summary>
public sealed class CraftData
{
    public int Order { get; set; }
    public string Blueprint { get; set; }

    /// <summary>flying, landed, anchored (a base), keyed (capital mode, still in the ground) or docked.</summary>
    public string State { get; set; }
    public double[] Position { get; set; }
    public double[] Rotation { get; set; }
    public double[] Velocity { get; set; }

    /// <summary>Docked: the pose in the host's frame (the host is the record's ShipId).</summary>
    public double[] Local { get; set; }
    public double[] LocalRotation { get; set; }
    public List<Stack> Cargo { get; set; } = new();
    public bool AutoTransmit { get; set; }
}

/// <summary>One planet's ground: terrain-digging's canonical edit log, against the generator it was dug on.</summary>
public sealed class TerrainData
{
    public string Generator { get; set; }
    public int Cells { get; set; }
    public string Edits { get; set; }
}

/// <summary>The player's side: which structure is Home, the game mode, and the materials they carry.</summary>
public sealed class ColonyData
{
    public string Home { get; set; }
    public bool Creative { get; set; } = true;
    public List<Stack> Inventory { get; set; } = new();
}

/// <summary>
/// Saving and loading the whole prototype through prototypes/saves' persistence core: every craft,
/// the planet's ground and the player's side are records in its <see cref="RecordTable"/>, written
/// by its shadow-paged <see cref="SaveStore"/>. Its ledger is the world's: everything the world
/// declares on purpose (blocks built, items dug) is declared to the table too, so a commit that
/// would lose or invent a single block or item is refused and the last good save kept.
/// </summary>
public sealed class SaveGame
{
    public const string CraftType = "craft", TerrainType = "terrain", ColonyType = "colony";
    public const string TerrainId = "ground", ColonyId = "colony";
    const string Owner = "player";

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public SaveStore Store { get; }
    public RecordTable Table { get; private set; } = new();
    public LoadResult LastLoad { get; private set; }
    public CommitResult LastCommit { get; private set; }

    CapitalWorld _world;

    public SaveGame(string folder)
    {
        Store = new SaveStore(folder, Schemas());
        if (!Store.Exists) Store.Create();
        else
        {
            LoadResult r = Store.Load();
            if (!r.Ok) throw new InvalidOperationException(r.Summary());
            Table = r.Table;
        }
    }

    public static RecordSchemas Schemas() => new RecordSchemas()
        .Register(CraftType, 1, p => CountCraft(Read<CraftData>(p)))
        .Register(TerrainType, 1, _ => (0, 0))
        .Register(ColonyType, 1, p => (Read<ColonyData>(p).Inventory.Sum(s => s.Count), 0));

    static (int, int) CountCraft(CraftData c) => (c.Cargo.Sum(s => s.Count), Blueprint.Parse(c.Blueprint).Blocks.Count);

    static T Read<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Json);

    /// <summary>The world's ledger feeds the table's: what it declares, the next commit expects.</summary>
    public void Attach(CapitalWorld world)
    {
        if (_world != null) _world.Declared -= OnDeclared;
        _world = world;
        world.Declared += OnDeclared;
    }

    void OnDeclared(int items, int blocks) => Table.Declare(items, blocks);

    string Zone => $"surface/{_world.Planet.Spec.Archetype.ToString().ToLowerInvariant()}-{_world.Planet.Spec.Seed}";

    // ---- Save ---------------------------------------------------------------------------------

    /// <summary>
    /// Writes what changed. Throws <see cref="SaveRefusedException"/>, writing nothing, if the
    /// world's items and blocks don't add up to the last save plus what was declared.
    /// </summary>
    public CommitResult Save()
    {
        Capture();
        CommitPlan plan = Table.PrepareCommit();
        LastCommit = Store.Commit(plan);
        Table.CompleteCommit(plan);
        return LastCommit;
    }

    void Capture()
    {
        CapitalWorld w = _world;
        int order = 0;
        foreach (Craft c in w.Crafts)
        {
            CraftData data = Describe(c, order++);
            string host = c.Pin is { Docked: true } pin ? w.CraftOf(pin.Host)?.Id : null;
            Put(new SaveRecord
            {
                Id = c.Id, Type = CraftType, ZoneId = Zone, ShipId = host, Owner = Owner, Touched = true,
                Items = c.Cargo.Total, Blocks = c.Structure.Count, Payload = JsonSerializer.Serialize(data, Json),
            });
        }

        EditLog edits = w.Ground.Export();
        var terrain = new TerrainData { Generator = w.Planet.Source.GeneratorId, Cells = edits.CellCount, Edits = Convert.ToBase64String(edits.Serialize()) };
        Put(new SaveRecord { Id = TerrainId, Type = TerrainType, ZoneId = Zone, Touched = true, Payload = JsonSerializer.Serialize(terrain, Json) });

        var colony = new ColonyData
        {
            Home = w.Home.HomeId,
            Creative = w.Inventory.Creative,
            Inventory = w.Inventory.Items.Where(kv => kv.Value > 0).Select(kv => new Stack { Item = kv.Key, Count = kv.Value }).ToList(),
        };
        Put(new SaveRecord
        {
            Id = ColonyId, Type = ColonyType, ZoneId = Zone, Owner = Owner, Touched = true,
            Items = colony.Inventory.Sum(s => s.Count), Payload = JsonSerializer.Serialize(colony, Json),
        });
    }

    /// <summary>Puts a record only if it changed, so a save writes only what moved.</summary>
    void Put(SaveRecord record)
    {
        if (Table.TryGet(record.Id, out SaveRecord old) && old == record) return;
        Table.Put(record);
    }

    static CraftData Describe(Craft c, int order)
    {
        Body b = c.Body;
        var data = new CraftData
        {
            Order = order,
            Blueprint = Blueprint.Save(c.Structure),
            State = c.State switch
            {
                CraftState.Anchored => "anchored",
                CraftState.Keyed => "keyed",
                CraftState.Docked => "docked",
                CraftState.Landed => "landed",
                _ => "flying",
            },
            Position = V(b.Position),
            Rotation = Q(b.Rotation),
            Velocity = V(b.Velocity),
            Cargo = c.Cargo.Stacks.Select(kv => new Stack { Item = kv.Key, Count = kv.Value }).ToList(),
            AutoTransmit = c.AutoTransmit,
        };
        if (c.Pin is { Docked: true } pin)
        {
            data.Local = V(pin.Position);
            data.LocalRotation = Q(pin.Rotation);
        }
        return data;
    }

    // ---- Load ---------------------------------------------------------------------------------

    /// <summary>
    /// Loads the last commit, audited, and rebuilds the world from it: the ground from the
    /// generator plus its edit log, every craft from its blueprint, in its state and place, with
    /// its cargo. If the save fails its audit, nothing in the world changes.
    /// </summary>
    public LoadResult Load()
    {
        LoadResult r = Store.Load();
        LastLoad = r;
        if (!r.Ok) return r;
        Table = r.Table;
        Restore(r.Table);
        return r;
    }

    void Restore(RecordTable table)
    {
        CapitalWorld w = _world;
        w.Clear();

        if (table.TryGet(TerrainId, out SaveRecord ground))
        {
            TerrainData t = Read<TerrainData>(ground.Payload);
            if (t.Generator != w.Planet.Source.GeneratorId)
                throw new InvalidOperationException($"the save's ground was dug on '{t.Generator}', not this planet '{w.Planet.Source.GeneratorId}'");
            w.Ground.Restore(EditLog.Deserialize(Convert.FromBase64String(t.Edits)));
        }

        var crafts = table.All.Where(r => r.Type == CraftType)
            .Select(r => (record: r, data: Read<CraftData>(r.Payload)))
            .OrderBy(x => x.data.Order).ThenBy(x => x.record.Id, StringComparer.Ordinal).ToList();
        foreach (var (record, data) in crafts)
        {
            Structure s = Blueprint.Load(data.Blueprint, w.Catalogue, out List<string> errors);
            if (s == null) throw new InvalidOperationException($"{record.Id}: {string.Join("; ", errors)}");
            s.DirtyChunks.Clear();
            Craft c = w.Adopt(record.Id, s, Vec(data.Position), Quat(data.Rotation));
            foreach (Stack st in data.Cargo) c.Cargo.Add(st.Item, st.Count);
            c.CargoChanged();
            c.AutoTransmit = data.AutoTransmit;
            switch (data.State)
            {
                case "flying": c.PlaceFlying(Vec(data.Position), Quat(data.Rotation), Vec(data.Velocity)); break;
                case "landed": c.PlaceLanded(Vec(data.Position), Quat(data.Rotation)); break;
                case "anchored":
                case "keyed": w.RestoreAnchor(c, Vec(data.Position), Quat(data.Rotation)); break;
            }
        }
        foreach (var (record, data) in crafts.Where(x => x.data.State == "docked"))
            w.RestoreDock(w.CraftById(record.Id), w.CraftById(record.ShipId), Vec(data.Local), Quat(data.LocalRotation));

        if (table.TryGet(ColonyId, out SaveRecord colony))
        {
            ColonyData d = Read<ColonyData>(colony.Payload);
            if (d.Home != null) w.Home.Flag(d.Home);
            w.Inventory.Creative = d.Creative;
            foreach (Stack st in d.Inventory) w.Inventory.Items[st.Item] = st.Count;
        }
        w.ResetLedger();
    }

    static double[] V(N.Vector3 v) => new double[] { v.X, v.Y, v.Z };
    static double[] Q(N.Quaternion q) => new double[] { q.X, q.Y, q.Z, q.W };
    static N.Vector3 Vec(double[] a) => new((float)a[0], (float)a[1], (float)a[2]);
    static N.Quaternion Quat(double[] a) => new((float)a[0], (float)a[1], (float)a[2], (float)a[3]);
}
