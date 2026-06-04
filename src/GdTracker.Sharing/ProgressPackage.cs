using GdTracker.Core;

namespace GdTracker.Sharing;

/// <summary>Переносимый пакет прогресса для обмена файлами.</summary>
public sealed class ProgressPackage
{
    public int Version { get; set; } = 1;
    public DateTime ExportedAt { get; set; }
    public List<PackageLevel> Levels { get; set; } = new();
}

public sealed class PackageLevel
{
    public long? GdLevelId { get; set; }
    public string Name { get; set; } = string.Empty;
    public LevelSource Source { get; set; }
    public int? Stars { get; set; }
    public List<PackageRecord> Records { get; set; } = new();
}

public sealed class PackageRecord
{
    public RunType Type { get; set; }
    public int StartPercent { get; set; }
    public int ReachedPercent { get; set; }
    public ProgressMode Mode { get; set; }
    public int? Attempts { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
    public ProgressSource Source { get; set; }
}
