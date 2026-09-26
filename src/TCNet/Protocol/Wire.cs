using System.Buffers.Binary;
using System.Text;

namespace TCNet;

/// <summary>Little-endian field access and fixed-width text helpers used by every packet.</summary>
public static class Wire
{
    public static ushort U16(ReadOnlySpan<byte> p, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(p[offset..]);
    public static uint U32(ReadOnlySpan<byte> p, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(p[offset..]);
    public static void U16(Span<byte> p, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(p[offset..], value);
    public static void U32(Span<byte> p, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(p[offset..], value);

    /// <summary>Reads a fixed-width, NUL-padded 8-bit text field (ASCII; bytes ≥ 0x80 read as Latin-1).</summary>
    public static string Ascii(ReadOnlySpan<byte> p, int offset, int length)
    {
        var field = p.Slice(offset, length);
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        return Encoding.Latin1.GetString(field).TrimEnd(' ');
    }

    /// <summary>Writes a fixed-width text field, truncated and NUL padded. Non-Latin-1 characters become '?'.</summary>
    public static void Ascii(Span<byte> p, int offset, int length, string? value)
    {
        var field = p.Slice(offset, length);
        field.Clear();
        if (string.IsNullOrEmpty(value)) return;
        int n = Math.Min(value.Length, length);
        for (int i = 0; i < n; i++)
        {
            char c = value[i];
            field[i] = c <= 0xFF ? (byte)c : (byte)'?';
        }
    }

    /// <summary>Reads a UTF-8 field terminated by NUL or the field end.</summary>
    public static string Utf8(ReadOnlySpan<byte> p, int offset, int length)
    {
        var field = p.Slice(offset, length);
        int end = field.IndexOf((byte)0);
        if (end >= 0) field = field[..end];
        return Encoding.UTF8.GetString(field);
    }

    /// <summary>Writes UTF-8, never splitting a multi-byte sequence, NUL padded.</summary>
    public static void Utf8(Span<byte> p, int offset, int length, string? value)
    {
        var field = p.Slice(offset, length);
        field.Clear();
        if (string.IsNullOrEmpty(value)) return;
        var bytes = Encoding.UTF8.GetBytes(value);
        int n = Math.Min(bytes.Length, length);
        // Back off to a sequence boundary.
        while (n > 0 && n < bytes.Length && (bytes[n] & 0xC0) == 0x80) n--;
        bytes.AsSpan(0, n).CopyTo(field);
    }

    /// <summary>Reads a UTF-16LE field terminated by a NUL code unit or the field end.</summary>
    public static string Utf16(ReadOnlySpan<byte> p, int offset, int length)
    {
        var field = p.Slice(offset, length & ~1);
        int units = field.Length / 2;
        for (int i = 0; i < units; i++)
        {
            if (field[2 * i] == 0 && field[2 * i + 1] == 0) { units = i; break; }
        }
        return Encoding.Unicode.GetString(field[..(units * 2)]);
    }

    /// <summary>Writes UTF-16LE, never splitting a surrogate pair, NUL padded.</summary>
    public static void Utf16(Span<byte> p, int offset, int length, string? value)
    {
        var field = p.Slice(offset, length);
        field.Clear();
        if (string.IsNullOrEmpty(value)) return;
        int maxChars = length / 2;
        int n = Math.Min(value.Length, maxChars);
        if (n > 0 && n < value.Length && char.IsHighSurrogate(value[n - 1])) n--;
        Encoding.Unicode.GetBytes(value.AsSpan(0, n), field);
    }

    /// <summary>Formats bytes as a classic 16-per-row hex dump with offsets and ASCII.</summary>
    public static string HexDump(ReadOnlySpan<byte> data, int maxBytes = int.MaxValue)
    {
        var sb = new StringBuilder();
        int len = Math.Min(data.Length, maxBytes);
        for (int row = 0; row < len; row += 16)
        {
            sb.Append(row.ToString("X4")).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                if (row + i < len) sb.Append(data[row + i].ToString("X2")).Append(' ');
                else sb.Append("   ");
                if (i == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int i = 0; i < 16 && row + i < len; i++)
            {
                byte b = data[row + i];
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }
            sb.AppendLine();
        }
        if (len < data.Length) sb.Append("… ").Append(data.Length - len).AppendLine(" more bytes");
        return sb.ToString();
    }

    /// <summary>Parses "0A 1B ff" / "0a1bff" / "0x0A,0x1B" style hex into bytes.</summary>
    public static byte[] ParseHex(string hex)
    {
        var clean = new StringBuilder(hex.Length);
        var s = hex.Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        foreach (char c in s) if (Uri.IsHexDigit(c)) clean.Append(c);
        if (clean.Length % 2 != 0) throw new FormatException("Hex string has an odd number of digits.");
        return Convert.FromHexString(clean.ToString());
    }
}
