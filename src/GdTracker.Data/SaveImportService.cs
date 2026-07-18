using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data;

/// <inheritdoc />
public class SaveImportService : ISaveImportService
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public SaveImportService(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<SaveImportResult> ImportAsync(
        IReadOnlyList<SaveLevelDto> levels, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var ids = levels.Select(l => l.GdLevelId).ToHashSet();
        var existing = await db.Levels
            .Include(l => l.ProgressRecords)
            .Where(l => l.GdLevelId.HasValue && ids.Contains(l.GdLevelId.Value))
            .ToListAsync(ct);
        var byId = existing.ToDictionary(l => l.GdLevelId!.Value);

        var now = DateTime.UtcNow;
        var added = 0;
        var updated = 0;

        foreach (var dto in levels)
        {
            if (!byId.TryGetValue(dto.GdLevelId, out var level))
            {
                level = new Level { GdLevelId = dto.GdLevelId, CreatedAt = now };
                db.Levels.Add(level);
                byId[dto.GdLevelId] = level;
                added++;
            }
            else
            {
                updated++;
            }

            if (!string.IsNullOrWhiteSpace(dto.Name))
                level.Name = dto.Name!;
            else if (string.IsNullOrEmpty(level.Name))
                level.Name = dto.GdLevelId.ToString();

            level.Source = dto.Source;
            level.Stars = dto.Stars;
            level.Creator = dto.Creator;
            level.Difficulty = dto.Difficulty;
            level.UpdatedAt = now;

            // Удаляем прежние импортные записи (идемпотентность), ручные не трогаем.
            var stale = level.ProgressRecords
                .Where(r => r.Source == ProgressSource.SaveImport)
                .ToList();
            foreach (var r in stale)
            {
                level.ProgressRecords.Remove(r);
                db.ProgressRecords.Remove(r);
            }

            // Синтетические записи из сейва: лучший normal/practice + попытки (k18).
            var attemptsAssigned = false;
            if (dto.BestNormalPercent > 0)
            {
                level.ProgressRecords.Add(NewImport(ProgressMode.Normal, dto.BestNormalPercent, dto.Attempts, now));
                attemptsAssigned = true;
            }
            if (dto.BestPracticePercent > 0)
            {
                level.ProgressRecords.Add(NewImport(
                    ProgressMode.Practice, dto.BestPracticePercent, attemptsAssigned ? null : dto.Attempts, now));
                attemptsAssigned = true;
            }
            if (!attemptsAssigned && dto.Attempts > 0)
            {
                // Уровень с попытками, но без прогресса — сохраняем попытки.
                level.ProgressRecords.Add(NewImport(ProgressMode.Normal, 0, dto.Attempts, now));
            }

            LevelAggregator.Apply(level);
        }

        await db.SaveChangesAsync(ct);
        return new SaveImportResult(added, updated);
    }

    private static ProgressRecord NewImport(ProgressMode mode, int reached, int? attempts, DateTime date) => new()
    {
        Type = RunType.FromZero,
        StartPercent = 0,
        ReachedPercent = reached,
        Mode = mode,
        Attempts = attempts,
        Date = date,
        Source = ProgressSource.SaveImport,
        Note = "Импорт из игры",
    };
}
