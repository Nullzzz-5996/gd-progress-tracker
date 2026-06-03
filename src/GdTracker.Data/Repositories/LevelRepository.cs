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

    public async Task<Level?> GetByIdAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.Levels.AsNoTracking()
            .Include(l => l.ProgressRecords)
            .FirstOrDefaultAsync(l => l.Id == id, ct);
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
}
