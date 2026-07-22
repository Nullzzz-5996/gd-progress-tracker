using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data.Repositories;

/// <inheritdoc />
public class LevelProgressRowRepository : ILevelProgressRowRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public LevelProgressRowRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.LevelProgressRows.AsNoTracking()
            .Where(r => r.LevelId == levelId)
            .OrderBy(r => r.Position)
            .ToListAsync(ct);
    }

    public async Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        row.CreatedAt = DateTime.UtcNow;
        db.LevelProgressRows.Add(row);
        await db.SaveChangesAsync(ct);
        return row;
    }

    public async Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        db.LevelProgressRows.Update(row);
        await db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        var row = await db.LevelProgressRows.FindAsync(new object?[] { id }, ct);
        if (row is null)
            return;

        db.LevelProgressRows.Remove(row);
        await db.SaveChangesAsync(ct);
    }
}
