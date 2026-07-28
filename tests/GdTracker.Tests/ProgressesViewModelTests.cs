using FluentAssertions;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>Тесты ProgressesViewModel на SQLite in-memory через реальные репозитории.</summary>
public partial class ProgressesViewModelTests
{
    private static ProgressesViewModel BuildVm(
        InMemorySqlite factory, out LevelRepository levels, out LevelProgressRowRepository rows,
        int? targetLevelId = null)
    {
        levels = new LevelRepository(factory);
        rows = new LevelProgressRowRepository(factory);
        var ctx = new ProgressNavigationContext { TargetLevelId = targetLevelId };
        return new ProgressesViewModel(levels, rows, ctx);
    }

    [Fact]
    public async Task LoadAsync_selects_target_level_from_context_and_clears_it()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        await levels.AddAsync(new Level { Name = "A" });
        var b = await levels.AddAsync(new Level { Name = "B" });

        var ctx = new ProgressNavigationContext { TargetLevelId = b.Id };
        var vm = new ProgressesViewModel(levels, new LevelProgressRowRepository(factory), ctx);
        await vm.LoadAsync();

        vm.SelectedLevel!.Id.Should().Be(b.Id);
        ctx.TargetLevelId.Should().BeNull("контекст одноразовый");
    }

    [Fact]
    public async Task LoadAsync_without_target_selects_first_level()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out _);
        await levels.AddAsync(new Level { Name = "A" });
        await levels.AddAsync(new Level { Name = "B" });

        await vm.LoadAsync();

        vm.SelectedLevel.Should().NotBeNull();
        vm.Levels.Should().HaveCount(2);
    }

    [Fact]
    public async Task AddRow_persists_and_is_capped_at_100()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out var rows);
        var level = await levels.AddAsync(new Level { Name = "A" });
        await vm.LoadAsync();

        for (int i = 0; i < 105; i++)
            await vm.AddRowCommand.ExecuteAsync(null);

        vm.Rows.Should().HaveCount(100);
        vm.AddRowCommand.CanExecute(null).Should().BeFalse();
        (await rows.GetByLevelAsync(level.Id)).Should().HaveCount(100);
    }

    [Fact]
    public async Task FromZeroDisplay_uses_best_normal_percent()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out _);
        await levels.AddAsync(new Level { Name = "A", BestNormalPercent = 97 });

        await vm.LoadAsync();

        vm.FromZeroDisplay.Should().Be("0-97");
    }

    [Fact]
    public async Task SaveRow_persists_edited_columns()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out var rows);
        var level = await levels.AddAsync(new Level { Name = "A" });
        await vm.LoadAsync();
        await vm.AddRowCommand.ExecuteAsync(null);

        var row = vm.Rows.Single();
        row.PracticeAttempts = "23";
        row.SegmentRange = "24-35";
        await vm.SaveRowAsync(row);

        var reloaded = (await rows.GetByLevelAsync(level.Id)).Single();
        reloaded.PracticeAttempts.Should().Be("23");
        reloaded.SegmentRange.Should().Be("24-35");
    }

    [Fact]
    public async Task DeleteRow_removes_and_renumbers_position()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out var rows);
        var level = await levels.AddAsync(new Level { Name = "A" });
        await vm.LoadAsync();
        await vm.AddRowCommand.ExecuteAsync(null);
        await vm.AddRowCommand.ExecuteAsync(null);
        await vm.AddRowCommand.ExecuteAsync(null);

        var second = vm.Rows[1];
        await vm.DeleteRowCommand.ExecuteAsync(second);

        vm.Rows.Should().HaveCount(2);
        var persisted = await rows.GetByLevelAsync(level.Id);
        persisted.Select(r => r.Position).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task LoadRows_reloads_for_current_selected_level()
    {
        using var factory = new InMemorySqlite();
        var vm = BuildVm(factory, out var levels, out var rows);
        var a = await levels.AddAsync(new Level { Name = "A" });
        var b = await levels.AddAsync(new Level { Name = "B" });
        await rows.AddAsync(new LevelProgressRow { LevelId = a.Id, Position = 1, PracticeAttempts = "1" });
        await rows.AddAsync(new LevelProgressRow { LevelId = a.Id, Position = 2, PracticeAttempts = "2" });
        await rows.AddAsync(new LevelProgressRow { LevelId = b.Id, Position = 1, PracticeAttempts = "9" });

        await vm.LoadAsync();

        vm.SelectedLevel = vm.Levels.First(l => l.Id == a.Id);
        await vm.LoadRowsAsync();
        vm.Rows.Should().HaveCount(2);

        vm.SelectedLevel = vm.Levels.First(l => l.Id == b.Id);
        await vm.LoadRowsAsync();
        vm.Rows.Should().HaveCount(1);
    }
}

/// <summary>
/// Регрессия: LoadRowsAsync/SaveRowAsync/DeleteRowAsync вызываются из обработчиков страницы
/// (Loaded, SelectionChanged, RowEditEnding), которые эквивалентны async void — необработанное
/// исключение репозитория (например, БД заблокирована или повреждена) уронило бы приложение
/// целиком. Зеркалит LevelsViewModelLoadErrorTests из LevelsWorkflowTests.cs. Часть той же
/// partial-версии ProgressesViewModelTests, чтобы регрессия оставалась под тем же тестовым
/// фильтром, что и остальные тесты этого ViewModel.
/// </summary>
public partial class ProgressesViewModelTests
{
    /// <summary>Репозиторий строк, всегда бросающий исключение из GetByLevelAsync (имитация сбоя БД).</summary>
    private sealed class ThrowingRowRepository : ILevelProgressRowRepository
    {
        public Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default)
            => throw new InvalidOperationException("база данных недоступна");

        public Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(int id, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Обёртка, бросающая исключение только из UpdateAsync — остальные операции реально
    /// выполняются на переданном репозитории (имитация сбоя, всплывающего только при сохранении
    /// уже успешно загруженной/добавленной строки).</summary>
    private sealed class UpdateThrowingRowRepository : ILevelProgressRowRepository
    {
        private readonly ILevelProgressRowRepository _inner;

        public UpdateThrowingRowRepository(ILevelProgressRowRepository inner) => _inner = inner;

        public Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default)
            => _inner.GetByLevelAsync(levelId, ct);

        public Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default)
            => _inner.AddAsync(row, ct);

        public Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default)
            => throw new InvalidOperationException("база данных недоступна");

        public Task DeleteAsync(int id, CancellationToken ct = default) => _inner.DeleteAsync(id, ct);
    }

    [Fact]
    public async Task LoadAsync_does_not_throw_when_row_repository_fails_and_reports_status_instead()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        await levels.AddAsync(new Level { Name = "A" });
        var ctx = new ProgressNavigationContext();
        var vm = new ProgressesViewModel(levels, new ThrowingRowRepository(), ctx);

        Func<Task> act = async () => await vm.LoadAsync();

        await act.Should().NotThrowAsync("иначе необработанное исключение из обработчика Loaded уронит приложение");
        vm.Status.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SaveRowAsync_does_not_throw_when_repository_update_fails_and_reports_status_instead()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        await levels.AddAsync(new Level { Name = "A" });
        var realRows = new LevelProgressRowRepository(factory);
        var ctx = new ProgressNavigationContext();
        var vm = new ProgressesViewModel(levels, new UpdateThrowingRowRepository(realRows), ctx);
        await vm.LoadAsync();
        await vm.AddRowCommand.ExecuteAsync(null);
        var row = vm.Rows.Single();

        Func<Task> act = async () => await vm.SaveRowAsync(row);

        await act.Should().NotThrowAsync("иначе необработанное исключение из RowEditEnding уронит приложение");
        vm.Status.Should().NotBeNullOrEmpty();
    }
}
