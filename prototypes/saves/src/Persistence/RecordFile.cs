using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;

namespace GalHack.Persistence;

/// <summary>
/// Reads and writes one record file. A record file is never changed once written: the next
/// version of the record goes to a new file named for the commit's generation, and the old file
/// stays until a manifest no longer points at it.
///
///   records/crate-a.g42.rec
/// </summary>
public static class RecordFile
{
    public const string Magic = "GALHACK-RECORD";
    public const int Format = 1;
    public const string Extension = ".rec";

    public static string FileName(string id, long generation) => $"{id}.g{generation}{Extension}";

    /// <summary>Recognises our own record file names. Anything else in the folder is left alone.</summary>
    public static bool TryParseFileName(string name, out string id, out long generation)
    {
        id = null;
        generation = -1;
        string[] parts = name.Split('.');
        if (parts.Length != 3 || "." + parts[2] != Extension || parts[1].Length < 2 || parts[1][0] != 'g') return false;
        if (!SaveRecord.IsValidId(parts[0]) || !long.TryParse(parts[1].AsSpan(1), out generation)) return false;
        id = parts[0];
        return true;
    }

    public static byte[] Encode(SaveRecord r)
    {
        var body = new ArrayBufferWriter<byte>(256 + r.Payload.Length);
        using (var w = new Utf8JsonWriter(body))
        {
            // A fixed field order, so the same record always makes the same bytes.
            w.WriteStartObject();
            w.WriteString("id", r.Id);
            w.WriteString("type", r.Type);
            w.WriteString("zone", r.ZoneId);
            WriteNullable(w, "ship", r.ShipId);
            WriteNullable(w, "owner", r.Owner);
            w.WriteBoolean("touched", r.Touched);
            w.WriteNumber("version", r.Version);
            w.WriteNumber("items", r.Items);
            w.WriteNumber("blocks", r.Blocks);
            w.WritePropertyName("payload");
            w.WriteRawValue(r.Payload); // validated as JSON, but stored byte for byte
            w.WriteEndObject();
        }
        return Framing.Wrap(Magic, Format, body.WrittenSpan);
    }

    /// <summary>Checks and reads a record file. Throws a FormatException saying what is wrong.</summary>
    public static SaveRecord Decode(byte[] bytes)
    {
        ReadOnlyMemory<byte> body = Framing.Unwrap(bytes, Magic, out int format);
        if (format > Format) throw new FormatException($"record format {format} was written by a newer build (this one reads up to {Format})");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            JsonElement payload = root.GetProperty("payload");
            if (payload.ValueKind != JsonValueKind.Object) throw new FormatException("the payload is not a JSON object");
            return new SaveRecord
            {
                Id = root.GetProperty("id").GetString(),
                Type = root.GetProperty("type").GetString(),
                ZoneId = root.GetProperty("zone").GetString(),
                ShipId = ReadNullable(root, "ship"),
                Owner = ReadNullable(root, "owner"),
                Touched = root.GetProperty("touched").GetBoolean(),
                Version = root.GetProperty("version").GetInt32(),
                Items = root.GetProperty("items").GetInt32(),
                Blocks = root.GetProperty("blocks").GetInt32(),
                Payload = payload.GetRawText(),
            };
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("the record is malformed: " + e.Message);
        }
    }

    static void WriteNullable(Utf8JsonWriter w, string name, string value)
    {
        if (value == null) w.WriteNull(name);
        else w.WriteString(name, value);
    }

    static string ReadNullable(JsonElement root, string name)
    {
        JsonElement e = root.GetProperty(name);
        return e.ValueKind == JsonValueKind.Null ? null : e.GetString();
    }
}
