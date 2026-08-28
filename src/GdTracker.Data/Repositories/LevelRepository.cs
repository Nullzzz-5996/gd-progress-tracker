using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data.Repositories;

/// <inheritdoc />
public class LevelRepository : ILevelRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public LevelRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<Level>> GetAllAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Level>> GetTrackedAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .Where(l => l.IsTracked)
            .OrderBy(l => l.Name)
            .ToListAsync(ct);
    }

    public async Task<Level?> FindUntrackedByNameAsync(string name, CancellationToken ct = default)
    {
        // lower() в SQLite работает по ASCII — этого достаточно для названий уровней GD.
        var normalized = name.Trim().ToLower();
        if (normalized.Length == 0)
            return null;

        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .Where(l => !l.IsTracked && l.Name.ToLower() == normalized)
            .OrderByDescending(l => l.TotalAttempts)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<Level?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .Include(l => l.ProgressRecords)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
    }

    public async Task<Level?> GetByGdLevelIdAsync(long gdLevelId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .FirstOrDefaultAsync(l => l.GdLevelId == gdLevelId, ct);
    }

    public async Task<Level> AddAsync(Level level, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        level.CreatedAt = level.UpdatedAt = DateTime.UtcNow;
        db.Levels.Add(level);
        await db.SaveChangesAsync(ct);
        return level;
    }

    public async Task UpdateAsync(Level level, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        level.UpdatedAt = DateTime.UtcNow;
        db.Levels.Update(level);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var level = await db.Levels.FindAsync(new object?[] { id }, ct);
        if (level is null)
            return;

        db.Levels.Remove(level);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
            return;

        await using var db = await _factory.CreateDbContextAsync(ct);
        // ExecuteDelete + каскад на уровне БД удалит связанные записи прогресса/видео.
        await db.Levels.Where(l => ids.Contains(l.Id)).ExecuteDeleteAsync(ct);
    }
}
