using System.IO.Compression;
using System.Text;

namespace GdTracker.GameSync;

/// <summary>
/// Кодек сейв-файлов Geometry Dash (Windows): XOR с ключом 0x0B → URL-safe Base64 → gzip.
/// </summary>
public static class SaveFileCodec
{
    private const byte XorKey = 0x0B;

    /// <summary>Декодирует сырые байты сейва GD в plist XML.</summary>
    public static string Decode(byte[] data)
    {
        // 1. XOR каждого байта с ключом.
        var xored = new byte[data.Length];
        for (var i = 0; i < data.Length; i++)
            xored[i] = (byte)(data[i] ^ XorKey);

        // 2. Это ASCII-текст URL-safe Base64 → декодируем в gzip-байты.
        var base64 = Encoding.ASCII.GetString(xored);
        var compressed = FromUrlSafeBase64(base64);

        // 3. Распаковка gzip → plist XML (UTF-8).
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>Кодирует plist XML обратно в формат сейва GD (обратная операция к <see cref="Decode"/>).</summary>
    public static byte[] Encode(string xml)
    {
        // 1. gzip-сжатие.
        var raw = Encoding.UTF8.GetBytes(xml);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(raw, 0, raw.Length);
        var compressed = output.ToArray();

        // 2. URL-safe Base64.
        var base64 = ToUrlSafeBase64(compressed);

        // 3. XOR ASCII-байтов.
        var bytes = Encoding.ASCII.GetBytes(base64);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] ^= XorKey;
        return bytes;
    }

    private static byte[] FromUrlSafeBase64(string value)
    {
        // GD использует алфавит '-'/'_' вместо '+'/'/' и может опускать padding.
        var sb = new StringBuilder(value.Length + 3);
        foreach (var c in value)
        {
            switch (c)
            {
                case '-': sb.Append('+'); break;
                case '_': sb.Append('/'); break;
                case '\0': break;                 // отбрасываем хвостовые нули
                default:
                    if (!char.IsWhiteSpace(c))
                        sb.Append(c);
                    break;
            }
        }

        switch (sb.Length % 4)
        {
            case 2: sb.Append("=="); break;
            case 3: sb.Append('='); break;
        }

        return Convert.FromBase64String(sb.ToString());
    }

    private static string ToUrlSafeBase64(byte[] data)
        => Convert.ToBase64String(data).Replace('+', '-').Replace('/', '_');
}
