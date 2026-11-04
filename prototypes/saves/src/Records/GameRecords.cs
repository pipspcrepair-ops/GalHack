using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using GalHack.Persistence;

namespace GalHack.Records;

/// <summary>
/// Turns typed payloads into records and back, and tells the persistence core about every type:
/// its current version, how to count its items and blocks, and how to migrate older versions.
/// </summary>
public static class GameRecords
{
    public static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The payload version this build writes for each type.</summary>
    public static readonly IReadOnlyDictionary<string, int> CurrentVersions = new Dictionary<string, int>
    {
        [RecordTypes.Player] = 1,
        [RecordTypes.Ship] = 1,
        [RecordTypes.Structure] = 1,
        [RecordTypes.Container] = 2, // v1 -> v2: one name per item became stacks, see ContainerV1ToV2
        [RecordTypes.Drop] = 1,
        [RecordTypes.Terrain] = 1,
    };

    public static RecordSchemas Schemas() =>
        new RecordSchemas()
            .Register(RecordTypes.Player, 1, p => Count(Read<PlayerData>(p)))
            .Register(RecordTypes.Ship, 1, p => Count(Read<ShipData>(p)))
            .Register(RecordTypes.Structure, 1, p => Count(Read<StructureData>(p)))
            .Register(RecordTypes.Container, 2, p => Count(Read<ContainerData>(p)))
            .Migration(RecordTypes.Container, 1, ContainerV1ToV2)
            .Register(RecordTypes.Drop, 1, p => Count(Read<DropData>(p)))
            .Register(RecordTypes.Terrain, 1, p => Count(Read<TerrainData>(p)));

    public static T Read<T>(string payload) => JsonSerializer.Deserialize<T>(payload, Json);
    public static T Read<T>(SaveRecord record) => Read<T>(record.Payload);
    public static string Write(object data) => JsonSerializer.Serialize(data, data.GetType(), Json);

    /// <summary>Items and blocks in a payload. The record's audit counts always come from here.</summary>
    public static (int Items, int Blocks) Count(object data) => data switch
    {
        PlayerData p => (Stacks.Total(p.Inventory), 0),
        ContainerData c => (Stacks.Total(c.Stacks), 0),
        DropData d => (Stacks.Total(d.Stacks), 0),
        StructureData s => (0, s.Blocks.Count),
        _ => (0, 0),
    };

    public static SaveRecord Make(string id, string type, string zoneId, string shipId, string owner, object data, bool touched = false)
    {
        var (items, blocks) = Count(data);
        return new SaveRecord
        {
            Id = id,
            Type = type,
            ZoneId = zoneId,
            ShipId = shipId,
            Owner = owner,
            Touched = touched,
            Version = CurrentVersions[type],
            Items = items,
            Blocks = blocks,
            Payload = Write(data),
        };
    }

    /// <summary>The same record with new data. The payload and its counts always change together.</summary>
    public static SaveRecord With(SaveRecord record, object data)
    {
        var (items, blocks) = Count(data);
        return record with { Payload = Write(data), Items = items, Blocks = blocks };
    }

    /// <summary>
    /// Container v1 kept one name per item: {"x":1,"y":0,"z":2,"items":["ore","ore","ice"]}.
    /// v2 keeps stacks, a label and a facing: {"label":"crate","position":[1,0,2],"yaw":0,
    /// "stacks":[{"item":"ore","count":2},{"item":"ice","count":1}]}. Stacks keep the order
    /// each item was first seen in, and every item is kept.
    /// </summary>
    public static JsonObject ContainerV1ToV2(JsonObject v1)
    {
        var stacks = new List<ItemStack>();
        foreach (JsonNode item in v1["items"].AsArray())
            Stacks.Add(stacks, item.GetValue<string>(), 1);
        var v2 = new ContainerData
        {
            Label = "crate",
            Position = new[] { v1["x"].GetValue<double>(), v1["y"].GetValue<double>(), v1["z"].GetValue<double>() },
            Yaw = 0,
            Stacks = stacks,
        };
        return JsonNode.Parse(Write(v2)).AsObject();
    }
}
