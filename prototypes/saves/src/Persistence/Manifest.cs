using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text.Json;

namespace GalHack.Persistence;

/// <summary>One line of the manifest: which file holds a record, and what that file must hash to.</summary>
public sealed record ManifestEntry(string Id, string File, long Bytes, string Sha256);

/// <summary>
/// The list of every record file in one committed generation, with each file's size and
/// SHA-256, plus the audit totals. Replacing manifest.json is the commit point: whatever it
/// lists is the save, and anything it doesn't list is not.
/// </summary>
public sealed class Manifest
{
    public const string Magic = "GALHACK-MANIFEST";
    public const int Format = 1;

    public int FormatVersion { get; set; } = Format;
    public long Generation { get; set; }
    public DateTime SavedAtUtc { get; set; }
    public AuditCounts Audit { get; set; } = new();

    /// <summary>Sorted by id, so the same save always makes the same bytes.</summary>
    public List<ManifestEntry> Records { get; set; } = new();

    public byte[] Encode()
    {
        var body = new ArrayBufferWriter<byte>(256 + Records.Count * 140);
        using (var w = new Utf8JsonWriter(body))
        {
            w.WriteStartObject();
            w.WriteNumber("generation", Generation);
            w.WriteString("savedAtUtc", DateTime.SpecifyKind(SavedAtUtc, DateTimeKind.Utc));
            w.WriteStartObject("audit");
            w.WriteNumber("records", Audit.Records);
            w.WriteNumber("items", Audit.Items);
            w.WriteNumber("blocks", Audit.Blocks);
            w.WriteStartObject("owned");
            foreach (var (type, count) in Audit.Owned) w.WriteNumber(type, count);
            w.WriteEndObject();
            w.WriteEndObject();
            w.WriteStartArray("records");
            foreach (ManifestEntry e in Records)
            {
                w.WriteStartObject();
                w.WriteString("id", e.Id);
                w.WriteString("file", e.File);
                w.WriteNumber("bytes", e.Bytes);
                w.WriteString("sha256", e.Sha256);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Framing.Wrap(Magic, Format, body.WrittenSpan);
    }

    /// <summary>Checks and reads a manifest. Throws a FormatException saying what is wrong.</summary>
    public static Manifest Decode(byte[] bytes)
    {
        ReadOnlyMemory<byte> body = Framing.Unwrap(bytes, Magic, out int format);
        if (format > Format) throw new FormatException($"manifest format {format} was written by a newer build (this one reads up to {Format})");
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            JsonElement root = doc.RootElement;
            JsonElement audit = root.GetProperty("audit");
            var manifest = new Manifest
            {
                FormatVersion = format,
                Generation = root.GetProperty("generation").GetInt64(),
                SavedAtUtc = root.GetProperty("savedAtUtc").GetDateTime().ToUniversalTime(),
                Audit = new AuditCounts
                {
                    Records = audit.GetProperty("records").GetInt32(),
                    Items = audit.GetProperty("items").GetInt32(),
                    Blocks = audit.GetProperty("blocks").GetInt32(),
                },
            };
            foreach (JsonProperty owned in audit.GetProperty("owned").EnumerateObject())
                manifest.Audit.Owned[owned.Name] = owned.Value.GetInt32();
            foreach (JsonElement e in root.GetProperty("records").EnumerateArray())
            {
                manifest.Records.Add(new ManifestEntry(
                    e.GetProperty("id").GetString(),
                    e.GetProperty("file").GetString(),
                    e.GetProperty("bytes").GetInt64(),
                    e.GetProperty("sha256").GetString()));
            }
            return manifest;
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new FormatException("the manifest is malformed: " + e.Message);
        }
    }
}
