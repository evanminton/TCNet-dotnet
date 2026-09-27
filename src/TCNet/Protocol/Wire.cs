using System.Buffers;
using System.Buffers.Binary;
using System.Globalization;
using System.Text;

namespace TCNet;

/// <summary>Little-endian and fixed-width text field helpers, plus hex dump / parse.</summary>
public static class Wire
{
    public static ushort U16(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadUInt16LittleEndian(p[o..]);
    public static uint U32(ReadOnlySpan<byte> p, int o) => BinaryPrimitives.ReadUInt32LittleEndian(p[o..]);
    public static void PutU16(Span<byte> p, int o, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(p[o..], v);
    public static void PutU32(Span<byte> p, int o, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(p[o..], v);

    private static ReadOnlySpan<byte> UntilNul(ReadOnlySpan<byte> f)
    {
        int n = f.IndexOf((byte)0);
        return n >= 0 ? f[..n] : f;
    }

    /// <summary>8-bit text (ASCII; bytes ≥ 0x80 read as Latin-1), NUL/space trimmed.</summary>
    public static string Ascii(ReadOnlySpan<byte> p, int o, int len) =>
        Encoding.Latin1.GetString(UntilNul(p.Slice(o, len))).TrimEnd(' ');

    /// <summary>Writes 8-bit text truncated and NUL padded; characters above U+00FF become '?'.</summary>
    public static void PutAscii(Span<byte> p, int o, int len, string? s)
    {
        var f = p.Slice(o, len);
        f.Clear();
        if (s is null) return;
        for (int i = 0; i < Math.Min(len, s.Length); i++) f[i] = s[i] <= 0xFF ? (byte)s[i] : (byte)'?';
    }

    public static string Utf8(ReadOnlySpan<byte> p, int o, int len) => Encoding.UTF8.GetString(UntilNul(p.Slice(o, len)));

    /// <summary>Writes UTF-8 without splitting a sequence, NUL padded.</summary>
    public static void PutUtf8(Span<byte> p, int o, int len, string? s)
    {
        var f = p.Slice(o, len);
        f.Clear();
        if (string.IsNullOrEmpty(s)) return;
        var b = Encoding.UTF8.GetBytes(s);
        int n = Math.Min(b.Length, len);
        while (n > 0 && n < b.Length && (b[n] & 0xC0) == 0x80) n--;
        b.AsSpan(0, n).CopyTo(f);
    }

    public static string Utf16(ReadOnlySpan<byte> p, int o, int len)
    {
        var f = p.Slice(o, len & ~1);
        int units = f.Length / 2;
        for (int i = 0; i < units; i++)
            if (f[2 * i] == 0 && f[2 * i + 1] == 0) { units = i; break; }
        return Encoding.Unicode.GetString(f[..(units * 2)]);
    }

    /// <summary>Writes UTF-16LE without splitting a surrogate pair, NUL padded.</summary>
    public static void PutUtf16(Span<byte> p, int o, int len, string? s)
    {
        var f = p.Slice(o, len);
        f.Clear();
        if (string.IsNullOrEmpty(s)) return;
        int n = Math.Min(s.Length, len / 2);
        if (n > 0 && n < s.Length && char.IsHighSurrogate(s[n - 1])) n--;
        Encoding.Unicode.GetBytes(s.AsSpan(0, n), f);
    }

    /// <summary>16 bytes per row: offset, hex, ASCII.</summary>
    public static string HexDump(ReadOnlySpan<byte> data, int max = int.MaxValue)
    {
        var sb = new StringBuilder();
        int len = Math.Min(max, data.Length);
        for (int row = 0; row < len; row += 16)
        {
            sb.Append(row.ToString("X4", CultureInfo.InvariantCulture)).Append("  ");
            for (int i = 0; i < 16; i++)
            {
                sb.Append(row + i < len ? data[row + i].ToString("X2", CultureInfo.InvariantCulture) + " " : "   ");
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

    private static readonly char[] HexSeparators = [' ', '\t', '\r', '\n', ',', '-', ':'];
    private static readonly SearchValues<char> HexDigits = SearchValues.Create("0123456789abcdefABCDEF");

    /// <summary>
    /// Accepts "54 43 4E", "54434e", "0x54,0x43", "54-43:4E" and similar: tokens split on whitespace, ',', '-' or ':',
    /// each an even number of hex digits with an optional leading 0x. Anything else throws <see cref="FormatException"/>.
    /// </summary>
    public static byte[] ParseHex(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var token in text.Split(HexSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            var digits = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? token.AsSpan(2) : token.AsSpan();
            if (digits.IsEmpty || digits.ContainsAnyExcept(HexDigits))
                throw new FormatException($"\"{token}\" is not hex.");
            if (digits.Length % 2 != 0) throw new FormatException($"\"{token}\" has an odd number of hex digits.");
            sb.Append(digits);
        }
        return Convert.FromHexString(sb.ToString());
    }

    internal static string Hex(ReadOnlySpan<byte> b, int max = 32) =>
        Convert.ToHexString(b[..Math.Min(max, b.Length)]) + (b.Length > max ? "…" : "");
}
