using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data.Repositories;

/// <inheritdoc />
public class ProgressRepository : IProgressRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public ProgressRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<ProgressRecord> AddAsync(ProgressRecord record, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.ProgressRecords.Add(record);
        await db.SaveChangesAsync(ct);

        await RecomputeLevelAsync(db, record.LevelId, ct);
        return record;
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var record = await db.ProgressRecords.FindAsync(new object?[] { id }, ct);
        if (record is null)
            return;

        var levelId = record.LevelId;
        db.ProgressRecords.Remove(record);
        await db.SaveChangesAsync(ct);

        await RecomputeLevelAsync(db, levelId, ct);
    }

    /// <summary>Перечитывает уровень со всеми записями и применяет агрегаты.</summary>
    private static async Task RecomputeLevelAsync(AppDbContext db, int levelId, CancellationToken ct)
    {
        var level = await db.Levels
            .Include(l => l.ProgressRecords)
            .FirstOrDefaultAsync(l => l.Id == levelId, ct);
        if (level is null)
            return;

        LevelAggregator.Apply(level);
        level.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
