using GdTracker.Cloud;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;
using GdTracker.Sharing;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data;

/// <summary>
/// Обмен прогрессом. Один и тот же пакет используется и для файлов обмена
/// (<see cref="IProgressSharingService"/>), и для облачной синхронизации
/// (<see cref="ILocalProgressStore"/>): сборка и слияние живут здесь в единственном
/// экземпляре, поэтому файл и облако не могут разойтись в поведении.
/// </summary>
public class ProgressSharingService : IProgressSharingService, ILocalProgressStore
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ProgressSharingService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task ExportAsync(string filePath, CancellationToken ct = default)
    {
        var package = await CreatePackageAsync(ct);
        var json = ProgressPackageSerializer.Serialize(package);
        await File.WriteAllTextAsync(filePath, json, ct);
    }

    public async Task<ImportSummary> ImportAsync(string filePath, CancellationToken ct = default)
    {
        var json = await File.ReadAllTextAsync(filePath, ct);
        var package = ProgressPackageSerializer.Deserialize(json);
        return await MergePackageAsync(package, ct);
    }

    /// <inheritdoc />
    public async Task<ProgressPackage> CreatePackageAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var levels = await db.Levels.AsNoTracking()
            .Include(l => l.ProgressRecords)
            .ToListAsync(ct);

        return new ProgressPackage
        {
            ExportedAt = DateTime.UtcNow,
            Levels = levels.Select(l => new PackageLevel
            {
                GdLevelId = l.GdLevelId,
                Name = l.Name,
                Source = l.Source,
                Stars = l.Stars,
                Records = l.ProgressRecords.Select(r => new PackageRecord
                {
                    Type = r.Type,
                    StartPercent = r.StartPercent,
                    ReachedPercent = r.ReachedPercent,
                    Mode = r.Mode,
                    Attempts = r.Attempts,
                    Date = r.Date,
                    Note = r.Note,
                    Source = r.Source,
                }).ToList(),
            }).ToList(),
        };
    }

    /// <inheritdoc />
    public async Task<ImportSummary> MergePackageAsync(ProgressPackage package, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var existing = await db.Levels.Include(l => l.ProgressRecords).ToListAsync(ct);
        var byGd = existing
            .Where(l => l.GdLevelId.HasValue)
            .ToDictionary(l => l.GdLevelId!.Value);

        var added = 0;
        var updated = 0;
        var recordsAdded = 0;
        var now = DateTime.UtcNow;

        foreach (var pl in package.Levels)
        {
            Level? level = null;
            if (pl.GdLevelId is { } gid)
                byGd.TryGetValue(gid, out level);
            level ??= existing.FirstOrDefault(l =>
                l.GdLevelId == null && string.Equals(l.Name, pl.Name, StringComparison.OrdinalIgnoreCase));

            if (level is null)
            {
                level = new Level
                {
                    GdLevelId = pl.GdLevelId,
                    Name = pl.Name,
                    Source = pl.Source,
                    Stars = pl.Stars,
                    CreatedAt = now,
                };
                db.Levels.Add(level);
                existing.Add(level);
                if (pl.GdLevelId is { } g)
                    byGd[g] = level;
                added++;
            }
            else
            {
                updated++;
                if (pl.Stars is not null)
                    level.Stars = pl.Stars;
            }

            var seen = level.ProgressRecords.Select(Key).ToHashSet();
            foreach (var pr in pl.Records)
            {
                if (!seen.Add(KeyOf(pr.Type, pr.StartPercent, pr.ReachedPercent, pr.Mode, pr.Source, pr.Date)))
                    continue;

                level.ProgressRecords.Add(new ProgressRecord
                {
                    Type = pr.Type,
                    StartPercent = pr.StartPercent,
                    ReachedPercent = pr.ReachedPercent,
                    Mode = pr.Mode,
                    Attempts = pr.Attempts,
                    Date = pr.Date,
                    Note = pr.Note,
                    Source = pr.Source,
                });
                recordsAdded++;
            }

            level.UpdatedAt = now;
            LevelAggregator.Apply(level);
        }

        await db.SaveChangesAsync(ct);
        return new ImportSummary(added, updated, recordsAdded);
    }

    private static string Key(ProgressRecord r)
        => KeyOf(r.Type, r.StartPercent, r.ReachedPercent, r.Mode, r.Source, r.Date);

    private static string KeyOf(RunType type, int start, int reached, ProgressMode mode, ProgressSource source, DateTime date)
        => $"{type}|{start}|{reached}|{mode}|{source}|{date.Ticks}";
}
