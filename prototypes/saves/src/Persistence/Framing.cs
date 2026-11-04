using System;
using System.Security.Cryptography;
using System.Text;

namespace GalHack.Persistence;

/// <summary>
/// The framing every save file uses: one short text line naming the kind of file, its format
/// version and the SHA-256 of the body, then the body (JSON) and a final newline.
///
///   GALHACK-RECORD 1 sha256=9f86d081884c7d65...
///   {"id":"crate-a","type":"container",...}
///
/// So any file can be checked on its own, without the manifest: that is what a salvage tool
/// needs, and a missing final newline gives away a write that was cut short.
/// </summary>
public static class Framing
{
    const int MaxHeaderLength = 200;

    public static byte[] Wrap(string magic, int format, ReadOnlySpan<byte> body)
    {
        string header = $"{magic} {format} sha256={Hash(body)}\n";
        int headerLength = Encoding.ASCII.GetByteCount(header);
        var bytes = new byte[headerLength + body.Length + 1];
        Encoding.ASCII.GetBytes(header, bytes);
        body.CopyTo(bytes.AsSpan(headerLength));
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    /// <summary>
    /// Checks the header and the checksum and returns the body. Throws a FormatException whose
    /// message says what is wrong in words a player could read out to a developer.
    /// </summary>
    public static ReadOnlyMemory<byte> Unwrap(byte[] bytes, string magic, out int format)
    {
        int newline = Array.IndexOf(bytes, (byte)'\n', 0, Math.Min(bytes.Length, MaxHeaderLength));
        if (newline < 0) throw new FormatException(bytes.Length == 0 ? "the file is empty" : "the header line is missing or cut short");

        string[] parts = Encoding.ASCII.GetString(bytes, 0, newline).Split(' ');
        if (parts.Length != 3 || parts[0] != magic) throw new FormatException($"this is not a {magic} file");
        if (!int.TryParse(parts[1], out format) || format < 1) throw new FormatException($"bad format number '{parts[1]}'");
        if (!parts[2].StartsWith("sha256=", StringComparison.Ordinal)) throw new FormatException("the header has no checksum");

        if (bytes.Length < newline + 2 || bytes[^1] != (byte)'\n') throw new FormatException("the file is cut short (no final newline)");
        var body = new ReadOnlyMemory<byte>(bytes, newline + 1, bytes.Length - newline - 2);
        if (Hash(body.Span) != parts[2]["sha256=".Length..]) throw new FormatException("checksum mismatch: the file is damaged");
        return body;
    }

    public static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
