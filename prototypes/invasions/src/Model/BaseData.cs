using System.Collections.Generic;
using System.Text.Json;
using Invasions.Director;

namespace Invasions.Model;

// The shape of data/base.json.

public sealed class BlockSpec
{
    public string Kind { get; set; } = "wall";
    public double Hp { get; set; } = 500;
    /// <summary>Item units a storage container holds.</summary>
    public int Capacity { get; set; }
}

public sealed class ItemSpec
{
    public string Name { get; set; } = "";
    public double Value { get; set; } = 1;
}

public sealed class BaseData
{
    public string Name { get; set; } = "Base";
    public double CellSize { get; set; } = 2;
    public double WallHeight { get; set; } = 3;
    public int Margin { get; set; } = 12;
    public double ZoneRadius { get; set; } = 300;
    public List<string> Layout { get; set; } = new();
    public Dictionary<string, BlockSpec> Legend { get; set; } = new();
    public List<int[]> Sirens { get; set; } = new();
    public List<int[]> Lights { get; set; } = new();
    public int[] PlayerSpawn { get; set; } = { 0, 0 };
    public Dictionary<string, ItemSpec> Items { get; set; } = new();
    public Dictionary<string, int> StartingInventory { get; set; } = new();

    public static BaseData FromJson(string json) => JsonSerializer.Deserialize<BaseData>(json, Json.Options)!;
}
