using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <inheritdoc />
public sealed class SaveFileReader : ISaveFileReader
{
    public string? DefaultSaveFilePath => SaveFileLocator.DefaultGameManagerPath();

    public IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath)
    {
        var bytes = ReadAllBytesShared(saveFilePath);
        var xml = SaveFileCodec.Decode(bytes);
        return SaveFileParser.Parse(xml);
    }

    public AccountStats? ReadAccountStats(string saveFilePath)
    {
        var bytes = ReadAllBytesShared(saveFilePath);
        var xml = SaveFileCodec.Decode(bytes);
        return AccountStatsParser.Parse(xml);
    }

    public DateTime? GetLastWriteTimeUtc(string saveFilePath)
        => File.Exists(saveFilePath) ? File.GetLastWriteTimeUtc(saveFilePath) : null;

    /// <summary>
    /// Читает файл с <see cref="FileShare.ReadWrite"/>, чтобы прочитать даже когда
    /// Geometry Dash держит сейв открытым. Приложение только читает, никогда не пишет сейв.
    /// </summary>
    private static byte[] ReadAllBytesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var ms = new MemoryStream();
        fs.CopyTo(ms);
        return ms.ToArray();
    }
}
