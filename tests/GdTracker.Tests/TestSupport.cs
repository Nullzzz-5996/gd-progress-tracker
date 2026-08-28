using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Data;
using GdTracker.ViewModels;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Tests;

/// <summary>Заглушка файловых диалогов для тестов (всегда «отмена»).</summary>
internal sealed class NullFileDialog : IFileDialogService
{
    public string? PickSaveFile(string suggestedName, string filter) => null;
    public string? PickOpenFile(string filter) => null;
}

/// <summary>Ручной фейк диалога подтверждения: настраиваемый ответ и счётчик/содержимое обращений.</summary>
internal sealed class FakeConfirmation : IConfirmationService
{
    /// <summary>Ответ, который вернёт <see cref="Confirm"/> (по умолчанию — «да»).</summary>
    public bool Result { get; set; } = true;

    public int CallCount { get; private set; }
    public string? LastTitle { get; private set; }
    public string? LastMessage { get; private set; }

    public bool Confirm(string title, string message)
    {
        CallCount++;
        LastTitle = title;
        LastMessage = message;
        return Result;
    }
}

/// <summary>Обёртка над репозиторием уровней, считающая обращения к методам удаления (для тестов).</summary>
internal sealed class DeleteCountingLevelRepository : ILevelRepository
{
    private readonly ILevelRepository _inner;

    public DeleteCountingLevelRepository(ILevelRepository inner) => _inner = inner;

    public int DeleteAsyncCallCount { get; private set; }
    public int DeleteManyAsyncCallCount { get; private set; }

    public Task<IReadOnlyList<Level>> GetAllAsync(CancellationToken ct = default) => _inner.GetAllAsync(ct);

    public Task<IReadOnlyList<Level>> GetTrackedAsync(CancellationToken ct = default) => _inner.GetTrackedAsync(ct);

    public Task<Level?> FindUntrackedByNameAsync(string name, CancellationToken ct = default)
        => _inner.FindUntrackedByNameAsync(name, ct);

    public Task<Level?> GetByIdAsync(int id, CancellationToken ct = default) => _inner.GetByIdAsync(id, ct);

    public Task<Level?> GetByGdLevelIdAsync(long gdLevelId, CancellationToken ct = default)
        => _inner.GetByGdLevelIdAsync(gdLevelId, ct);

    public Task<Level> AddAsync(Level level, CancellationToken ct = default) => _inner.AddAsync(level, ct);

    public Task UpdateAsync(Level level, CancellationToken ct = default) => _inner.UpdateAsync(level, ct);

    public Task DeleteAsync(int id, CancellationToken ct = default)
    {
        DeleteAsyncCallCount++;
        return _inner.DeleteAsync(id, ct);
    }

    public Task DeleteManyAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default)
    {
        DeleteManyAsyncCallCount++;
        return _inner.DeleteManyAsync(ids, ct);
    }
}

/// <summary>Фабрика контекстов над общим открытым SQLite in-memory соединением (для тестов).</summary>
internal sealed class InMemorySqlite : IDbContextFactory<AppDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AppDbContext> _options;

    public InMemorySqlite()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options;

        using var ctx = new AppDbContext(_options);
        ctx.Database.EnsureCreated();
    }

    public AppDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
