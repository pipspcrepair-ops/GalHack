using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SurvivalVitals.Model;

/// <summary>
/// A point in world metres (x east, y up, z south, like Godot). The model keeps its own small
/// vector so it never touches the engine, and runs the same in tests as in the game.
/// </summary>
[JsonConverter(typeof(PosJson))]
public readonly record struct Pos(double X, double Y, double Z)
{
    public static Pos Flat(double x, double z) => new(x, 0, z);

    public double DistanceTo(Pos o)
    {
        double dx = X - o.X, dy = Y - o.Y, dz = Z - o.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Distance across the ground, ignoring height: zones and territories are columns.</summary>
    public double FlatDistanceTo(Pos o)
    {
        double dx = X - o.X, dz = Z - o.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    public override string ToString() => $"({X:F1}, {Y:F1}, {Z:F1})";
}

/// <summary>Reads [x, z] as a point on the ground and [x, y, z] as a point in space.</summary>
public sealed class PosJson : JsonConverter<Pos>
{
    public override Pos Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        double[] v = JsonSerializer.Deserialize<double[]>(ref reader, options);
        return v.Length switch
        {
            2 => Pos.Flat(v[0], v[1]),
            3 => new Pos(v[0], v[1], v[2]),
            _ => throw new JsonException($"A position needs 2 or 3 numbers, not {v.Length}"),
        };
    }

    public override void Write(Utf8JsonWriter writer, Pos value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new[] { value.X, value.Y, value.Z }, options);
}

/// <summary>
/// Stateless seeded randomness: the same seed and index always give the same number, so
/// weather and fresh-start sites never depend on how often anything was asked.
/// </summary>
public static class SeededHash
{
    /// <summary>A number in [0, 1) for this seed, index and purpose.</summary>
    public static double Unit(int seed, long index, int salt)
    {
        ulong x = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL ^ (ulong)index * 0xBF58476D1CE4E5B9UL ^ (ulong)salt * 0x94D049BB133111EBUL;
        x ^= x >> 30; x *= 0xBF58476D1CE4E5B9UL;
        x ^= x >> 27; x *= 0x94D049BB133111EBUL;
        x ^= x >> 31;
        return (x >> 11) * (1.0 / (1UL << 53));
    }
}
