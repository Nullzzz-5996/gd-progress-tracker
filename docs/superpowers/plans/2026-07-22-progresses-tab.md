# Вкладка «Прогрессы» — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить вкладку «Прогрессы» с таблицей ручного учёта (4 столбца, до 100 строк на уровень) и кнопку перехода на неё с вкладки «Уровни».

**Architecture:** Новая EF-сущность `LevelProgressRow` (своя таблица, каскад от `Level`) + репозиторий по образцу существующих. Страница `ProgressesPage` со своей `ProgressesViewModel` (выбор уровня + `DataGrid`). Переход с вкладки «Уровни» реализован в code-behind `DashboardPage` через уже используемый `INavigationService` (как в `MainWindow`) + singleton `IProgressNavigationContext`, передающий id уровня. View-модели `LevelsViewModel`/`LevelDetailViewModel` не меняются — это исключает правку ~9 мест их конструирования в тестах.

**Tech Stack:** .NET 10, WPF + WPF-UI, MVVM (CommunityToolkit.Mvvm), EF Core 10 + SQLite, xUnit + FluentAssertions.

## Global Constraints

- Целевой фреймворк приложения: `net10.0-windows10.0.19041.0`; прочие проекты `net10.0`. Сборка x64 (`Directory.Build.props`).
- Решение — `GdTracker.slnx`. Сборка: `dotnet build GdTracker.slnx`. Тесты: `dotnet test GdTracker.slnx`.
- БД: EF Core SQLite, паттерн `IDbContextFactory<AppDbContext>` + короткоживущие контексты. Миграции применяются на старте (`db.Database.Migrate()` в `App.OnStartup`). В тестах схема создаётся `EnsureCreated()` через `InMemorySqlite` — миграция для тестов не нужна.
- Столбец 4 «с нуля» не хранится; отображается как `0-{Level.BestNormalPercent}` (дефис, как в примере пользователя «0-97»).
- Максимум 100 строк на уровень. Столбцы 1–3 — свободный текст (trim, пустое → `null`), без строгой валидации.
- Экспорт/импорт и импорт из сейва этих строк не касаются (вне объёма).
- Разметку/внешний вид юнит-тесты не покрывают (тестовый проект не ссылается на `GdTracker.App`).
- Русскоязычные подписи UI и XML-doc комментарии — как в остальном коде.

---

### Task 1: Модель, репозиторий и его тесты

**Files:**
- Create: `src/GdTracker.Core/Models/LevelProgressRow.cs`
- Modify: `src/GdTracker.Core/Models/Level.cs` (добавить навигационную коллекцию)
- Create: `src/GdTracker.Core/Abstractions/ILevelProgressRowRepository.cs`
- Modify: `src/GdTracker.Data/AppDbContext.cs` (DbSet + конфигурация + каскад)
- Create: `src/GdTracker.Data/Repositories/LevelProgressRowRepository.cs`
- Test: `tests/GdTracker.Tests/LevelProgressRowRepositoryTests.cs`

**Interfaces:**
- Produces:
  - `class LevelProgressRow { int Id; int LevelId; Level? Level; int Position; string? PracticeAttempts; string? SegmentRange; string? ToHundredRange; DateTime CreatedAt; }`
  - `interface ILevelProgressRowRepository` с методами `Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default)`, `Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default)`, `Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default)`, `Task DeleteAsync(int id, CancellationToken ct = default)`.
  - `class LevelProgressRowRepository : ILevelProgressRowRepository`.
- Consumes: `LevelRepository`, `InMemorySqlite` (в тестах, из `TestSupport.cs`).

- [ ] **Step 1: Написать падающий тест репозитория**

Create `tests/GdTracker.Tests/LevelProgressRowRepositoryTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

/// <summary>Интеграционные тесты репозитория строк таблицы «Прогрессы» на SQLite in-memory.</summary>
public class LevelProgressRowRepositoryTests
{
    [Fact]
    public async Task Add_and_get_by_level_returns_rows_ordered_by_position()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);

        var level = await levels.AddAsync(new Level { Name = "Bloodbath" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 2, PracticeAttempts = "23" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1, SegmentRange = "24-35" });

        var loaded = await rows.GetByLevelAsync(level.Id);

        loaded.Should().HaveCount(2);
        loaded.Select(r => r.Position).Should().ContainInOrder(1, 2);
    }

    [Fact]
    public async Task Update_persists_edited_columns()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        var row = await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        row.PracticeAttempts = "42";
        row.ToHundredRange = "67-100";
        await rows.UpdateAsync(row);

        var loaded = await rows.GetByLevelAsync(level.Id);
        loaded.Single().PracticeAttempts.Should().Be("42");
        loaded.Single().ToHundredRange.Should().Be("67-100");
    }

    [Fact]
    public async Task Delete_removes_row()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        var row = await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        await rows.DeleteAsync(row.Id);

        (await rows.GetByLevelAsync(level.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_level_cascades_to_progress_rows()
    {
        using var factory = new InMemorySqlite();
        var levels = new LevelRepository(factory);
        var rows = new LevelProgressRowRepository(factory);
        var level = await levels.AddAsync(new Level { Name = "Test" });
        await rows.AddAsync(new LevelProgressRow { LevelId = level.Id, Position = 1 });

        await levels.DeleteManyAsync(new[] { level.Id });

        (await rows.GetByLevelAsync(level.Id)).Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Запустить тест — убедиться, что не компилируется/падает**

Run: `dotnet test GdTracker.slnx --filter FullyQualifiedName~LevelProgressRowRepositoryTests`
Expected: ошибка компиляции (нет `LevelProgressRow`, `LevelProgressRowRepository`).

- [ ] **Step 3: Создать сущность**

Create `src/GdTracker.Core/Models/LevelProgressRow.cs`:

```csharp
namespace GdTracker.Core.Models;

/// <summary>
/// Одна строка личной таблицы прогресса на вкладке «Прогрессы».
/// Столбцы 1–3 заполняются вручную; столбец «с нуля» вычисляется в UI из
/// <see cref="Level.BestNormalPercent"/> и здесь не хранится.
/// </summary>
public class LevelProgressRow
{
    /// <summary>Внутренний первичный ключ.</summary>
    public int Id { get; set; }

    /// <summary>Уровень, к которому относится строка.</summary>
    public int LevelId { get; set; }

    /// <summary>Навигационное свойство к уровню.</summary>
    public Level? Level { get; set; }

    /// <summary>Позиция строки в таблице уровня (1…100).</summary>
    public int Position { get; set; }

    /// <summary>Столбец 1 «практика — за сколько попыток» (например «23»).</summary>
    public string? PracticeAttempts { get; set; }

    /// <summary>Столбец 2 «с парта до парта» (например «24-35»).</summary>
    public string? SegmentRange { get; set; }

    /// <summary>Столбец 3 «до ста» (например «67-100»).</summary>
    public string? ToHundredRange { get; set; }

    /// <summary>Дата создания строки.</summary>
    public DateTime CreatedAt { get; set; }
}
```

- [ ] **Step 4: Добавить навигационную коллекцию в `Level`**

Modify `src/GdTracker.Core/Models/Level.cs` — после свойства `ProgressRecords` (в конце класса):

```csharp
    /// <summary>Записи прогресса по этому уровню.</summary>
    public List<ProgressRecord> ProgressRecords { get; set; } = new();

    /// <summary>Строки личной таблицы прогресса (вкладка «Прогрессы»).</summary>
    public List<LevelProgressRow> ProgressRows { get; set; } = new();
```

- [ ] **Step 5: Создать интерфейс репозитория**

Create `src/GdTracker.Core/Abstractions/ILevelProgressRowRepository.cs`:

```csharp
using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище строк личной таблицы прогресса (вкладка «Прогрессы»).</summary>
public interface ILevelProgressRowRepository
{
    Task<IReadOnlyList<LevelProgressRow>> GetByLevelAsync(int levelId, CancellationToken ct = default);
    Task<LevelProgressRow> AddAsync(LevelProgressRow row, CancellationToken ct = default);
    Task UpdateAsync(LevelProgressRow row, CancellationToken ct = default);
    Task DeleteAsync(int id, CancellationToken ct = default);
}
```

- [ ] **Step 6: Настроить `AppDbContext`**

Modify `src/GdTracker.Data/AppDbContext.cs`:

Добавить DbSet после `AccountStatsSnapshots`:

```csharp
    public DbSet<AccountStatsSnapshot> AccountStatsSnapshots => Set<AccountStatsSnapshot>();
    public DbSet<LevelProgressRow> LevelProgressRows => Set<LevelProgressRow>();
```

Внутри `modelBuilder.Entity<Level>(e => { ... })`, сразу после блока `e.HasMany(x => x.ProgressRecords)...`:

```csharp
            e.HasMany(x => x.ProgressRows)
                .WithOne(x => x.Level!)
                .HasForeignKey(x => x.LevelId)
                .OnDelete(DeleteBehavior.Cascade);
```

Добавить конфигурацию сущности (после блока `modelBuilder.Entity<AccountStatsSnapshot>`):

```csharp
        modelBuilder.Entity<LevelProgressRow>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.PracticeAttempts).HasMaxLength(50);
            e.Property(x => x.SegmentRange).HasMaxLength(50);
            e.Property(x => x.ToHundredRange).HasMaxLength(50);
            e.HasIndex(x => x.LevelId);
        });
```

- [ ] **Step 7: Реализовать репозиторий**

Create `src/GdTracker.Data/Repositories/LevelProgressRowRepository.cs`:

```csharp
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
```

- [ ] **Step 8: Запустить тесты — должны пройти**

Run: `dotnet test GdTracker.slnx --filter FullyQualifiedName~LevelProgressRowRepositoryTests`
Expected: PASS (4 теста).

- [ ] **Step 9: Коммит**

```bash
git add src/GdTracker.Core/Models/LevelProgressRow.cs src/GdTracker.Core/Models/Level.cs src/GdTracker.Core/Abstractions/ILevelProgressRowRepository.cs src/GdTracker.Data/AppDbContext.cs src/GdTracker.Data/Repositories/LevelProgressRowRepository.cs tests/GdTracker.Tests/LevelProgressRowRepositoryTests.cs
git commit -m "feat: LevelProgressRow entity + repository for Progresses tab"
```

---

### Task 2: EF-миграция

**Files:**
- Create: `src/GdTracker.Data/Migrations/<timestamp>_AddLevelProgressRows.cs` (+ `.Designer.cs`) — генерируется инструментом
- Modify: `src/GdTracker.Data/Migrations/AppDbContextModelSnapshot.cs` — обновляется инструментом

**Interfaces:** нет (изменение схемы БД).

- [ ] **Step 1: Убедиться, что установлен инструмент `dotnet-ef`**

Run: `dotnet ef --version`
Expected: печатает версию. Если команда не найдена — `dotnet tool install --global dotnet-ef`.

- [ ] **Step 2: Создать миграцию**

Дизайн-тайм фабрика (`AppDbContextFactory`) лежит в `GdTracker.Data`, поэтому проект указывается и как `-p`, и как `-s` (WPF-приложение собирать не нужно):

Run:
```bash
dotnet ef migrations add AddLevelProgressRows -p src/GdTracker.Data/GdTracker.Data.csproj -s src/GdTracker.Data/GdTracker.Data.csproj
```
Expected: `Done.` и новые файлы в `src/GdTracker.Data/Migrations/`.

- [ ] **Step 3: Проверить содержимое миграции**

Открыть новый `*_AddLevelProgressRows.cs` и убедиться, что `Up()` создаёт таблицу `LevelProgressRows` со столбцами `Id, LevelId, Position, PracticeAttempts, SegmentRange, ToHundredRange, CreatedAt`, внешним ключом на `Levels` с `onDelete: ReferentialAction.Cascade` и индексом по `LevelId`.

- [ ] **Step 4: Собрать решение**

Run: `dotnet build GdTracker.slnx`
Expected: `Сборка успешно завершена.`, 0 ошибок, 0 предупреждений.

- [ ] **Step 5: Прогнать все тесты (регрессия)**

Run: `dotnet test GdTracker.slnx`
Expected: PASS (все тесты, включая новые из Task 1).

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.Data/Migrations/
git commit -m "feat: EF migration AddLevelProgressRows"
```

---

### Task 3: View-модели вкладки «Прогрессы» и контекст навигации

**Files:**
- Create: `src/GdTracker.ViewModels/ProgressNavigationContext.cs` (интерфейс + реализация)
- Create: `src/GdTracker.ViewModels/LevelProgressRowViewModel.cs`
- Create: `src/GdTracker.ViewModels/ProgressesViewModel.cs`
- Test: `tests/GdTracker.Tests/ProgressesViewModelTests.cs`

**Interfaces:**
- Consumes: `ILevelRepository`, `ILevelProgressRowRepository`, `LevelProgressRow`, `Level`.
- Produces:
  - `interface IProgressNavigationContext { int? TargetLevelId { get; set; } }` + `sealed class ProgressNavigationContext : IProgressNavigationContext`.
  - `class LevelProgressRowViewModel(LevelProgressRow model)` со свойствами `int Id`, `string? PracticeAttempts`, `string? SegmentRange`, `string? ToHundredRange` и методом `LevelProgressRow ToModel()`.
  - `class ProgressesViewModel(ILevelRepository, ILevelProgressRowRepository, IProgressNavigationContext)` c публичными членами: `ObservableCollection<Level> Levels`, `ObservableCollection<LevelProgressRowViewModel> Rows`, `Level? SelectedLevel`, `string FromZeroDisplay`, `string? Status`, `bool IsLoadingLevels`, `Task LoadAsync()`, `Task LoadRowsAsync()`, `Task SaveRowAsync(LevelProgressRowViewModel row)`, команды `AddRowCommand`, `DeleteRowCommand`.

- [ ] **Step 1: Написать падающие тесты view-модели**

Create `tests/GdTracker.Tests/ProgressesViewModelTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>Тесты ProgressesViewModel на SQLite in-memory через реальные репозитории.</summary>
public class ProgressesViewModelTests
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
```

- [ ] **Step 2: Запустить тесты — не компилируются**

Run: `dotnet test GdTracker.slnx --filter FullyQualifiedName~ProgressesViewModelTests`
Expected: ошибка компиляции (нет `ProgressesViewModel`, `ProgressNavigationContext`, `LevelProgressRowViewModel`).

- [ ] **Step 3: Создать контекст навигации**

Create `src/GdTracker.ViewModels/ProgressNavigationContext.cs`:

```csharp
namespace GdTracker.ViewModels;

/// <summary>
/// Передаёт вкладке «Прогрессы», какой уровень открыть, при переходе с вкладки «Уровни».
/// Singleton: устанавливается перед навигацией, читается вью-моделью страницы при загрузке.
/// </summary>
public interface IProgressNavigationContext
{
    int? TargetLevelId { get; set; }
}

/// <inheritdoc />
public sealed class ProgressNavigationContext : IProgressNavigationContext
{
    public int? TargetLevelId { get; set; }
}
```

- [ ] **Step 4: Создать view-модель строки**

Create `src/GdTracker.ViewModels/LevelProgressRowViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>Строка таблицы «Прогрессы»: редактируемые столбцы 1–3 поверх модели.</summary>
public partial class LevelProgressRowViewModel : ObservableObject
{
    private readonly LevelProgressRow _model;

    public LevelProgressRowViewModel(LevelProgressRow model)
    {
        _model = model;
        _practiceAttempts = model.PracticeAttempts;
        _segmentRange = model.SegmentRange;
        _toHundredRange = model.ToHundredRange;
    }

    /// <summary>Первичный ключ строки в БД.</summary>
    public int Id => _model.Id;

    [ObservableProperty] private string? _practiceAttempts;
    [ObservableProperty] private string? _segmentRange;
    [ObservableProperty] private string? _toHundredRange;

    /// <summary>
    /// Возвращает подлежащую модель с текущими значениями (обрезанными; пустое → null).
    /// Позиция строки задаётся снаружи (<see cref="LevelProgressRow.Position"/>) и здесь не меняется.
    /// </summary>
    public LevelProgressRow ToModel()
    {
        _model.PracticeAttempts = Normalize(PracticeAttempts);
        _model.SegmentRange = Normalize(SegmentRange);
        _model.ToHundredRange = Normalize(ToHundredRange);
        return _model;
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
```

- [ ] **Step 5: Создать view-модель страницы**

Create `src/GdTracker.ViewModels/ProgressesViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;

namespace GdTracker.ViewModels;

/// <summary>Вкладка «Прогрессы»: выбор уровня + таблица из 4 столбцов (до 100 строк на уровень).</summary>
public partial class ProgressesViewModel : ViewModelBase
{
    private const int MaxRows = 100;

    private readonly ILevelRepository _levels;
    private readonly ILevelProgressRowRepository _rows;
    private readonly IProgressNavigationContext _navContext;

    public ProgressesViewModel(
        ILevelRepository levels,
        ILevelProgressRowRepository rows,
        IProgressNavigationContext navContext)
    {
        _levels = levels;
        _rows = rows;
        _navContext = navContext;
    }

    /// <summary>Уровни для переключателя.</summary>
    public ObservableCollection<Level> Levels { get; } = new();

    /// <summary>Строки таблицы выбранного уровня.</summary>
    public ObservableCollection<LevelProgressRowViewModel> Rows { get; } = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FromZeroDisplay))]
    private Level? _selectedLevel;

    [ObservableProperty] private string? _status;

    /// <summary>
    /// Пока true, обработчик выбора уровня в UI не перезагружает строки: стартовый уровень
    /// загружает сам <see cref="LoadAsync"/>, без гонки с событием SelectionChanged у ComboBox.
    /// </summary>
    public bool IsLoadingLevels { get; private set; }

    /// <summary>Столбец 4 «с нуля»: 0–лучший normal % выбранного уровня.</summary>
    public string FromZeroDisplay => SelectedLevel is null ? string.Empty : $"0-{SelectedLevel.BestNormalPercent}";

    private bool CanAddRow => SelectedLevel is not null && Rows.Count < MaxRows;

    /// <summary>Грузит уровни, выбирает целевой (из контекста навигации) или первый, грузит его строки.</summary>
    public async Task LoadAsync()
    {
        Status = null;
        IReadOnlyList<Level> all;
        try
        {
            all = await _levels.GetAllAsync();
        }
        catch (Exception ex)
        {
            Status = $"Не удалось загрузить уровни: {ex.Message}";
            return;
        }

        IsLoadingLevels = true;
        try
        {
            Levels.Clear();
            foreach (var l in all)
                Levels.Add(l);

            var targetId = _navContext.TargetLevelId;
            _navContext.TargetLevelId = null; // одноразово: возврат на вкладку позже не должен перепрыгивать
            SelectedLevel = (targetId is not null ? Levels.FirstOrDefault(l => l.Id == targetId) : null)
                            ?? Levels.FirstOrDefault();
        }
        finally
        {
            IsLoadingLevels = false;
        }

        await LoadRowsAsync();
    }

    /// <summary>Перезагружает строки для текущего <see cref="SelectedLevel"/>.</summary>
    public async Task LoadRowsAsync()
    {
        Rows.Clear();
        if (SelectedLevel is not null)
        {
            var loaded = await _rows.GetByLevelAsync(SelectedLevel.Id);
            foreach (var r in loaded)
                Rows.Add(new LevelProgressRowViewModel(r));
        }

        AddRowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanAddRow))]
    private async Task AddRowAsync()
    {
        if (!CanAddRow)
            return;

        var model = new LevelProgressRow { LevelId = SelectedLevel!.Id, Position = Rows.Count + 1 };
        var saved = await _rows.AddAsync(model);
        Rows.Add(new LevelProgressRowViewModel(saved));
        AddRowCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task DeleteRowAsync(LevelProgressRowViewModel? row)
    {
        if (row is null)
            return;

        await _rows.DeleteAsync(row.Id);
        Rows.Remove(row);

        // Перенумеровываем оставшиеся строки, чтобы позиции шли подряд 1..N.
        for (int i = 0; i < Rows.Count; i++)
        {
            var model = Rows[i].ToModel();
            model.Position = i + 1;
            await _rows.UpdateAsync(model);
        }

        AddRowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Сохраняет отредактированную строку (вызывается из RowEditEnding страницы).</summary>
    public async Task SaveRowAsync(LevelProgressRowViewModel row) => await _rows.UpdateAsync(row.ToModel());
}
```

- [ ] **Step 6: Запустить тесты — должны пройти**

Run: `dotnet test GdTracker.slnx --filter FullyQualifiedName~ProgressesViewModelTests`
Expected: PASS (7 тестов).

- [ ] **Step 7: Коммит**

```bash
git add src/GdTracker.ViewModels/ProgressNavigationContext.cs src/GdTracker.ViewModels/LevelProgressRowViewModel.cs src/GdTracker.ViewModels/ProgressesViewModel.cs tests/GdTracker.Tests/ProgressesViewModelTests.cs
git commit -m "feat: ProgressesViewModel + row VM + navigation context"
```

---

### Task 4: Страница «Прогрессы» и регистрация в навигации

**Files:**
- Create: `src/GdTracker.App/Views/ProgressesPage.xaml`
- Create: `src/GdTracker.App/Views/ProgressesPage.xaml.cs`
- Modify: `src/GdTracker.App/MainWindow.xaml` (пункт навигации)
- Modify: `src/GdTracker.App/App.xaml.cs` (регистрация сервисов, страницы, VM)

**Interfaces:**
- Consumes: `ProgressesViewModel`, `LevelProgressRowViewModel`, `ILevelProgressRowRepository`, `IProgressNavigationContext`, `LevelProgressRowRepository`, `ProgressNavigationContext`.
- Produces: `ProgressesPage` (тип страницы для навигации).

- [ ] **Step 1: Создать разметку страницы**

Create `src/GdTracker.App/Views/ProgressesPage.xaml`:

```xml
<Page x:Class="GdTracker.App.Views.ProgressesPage"
      xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
      xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
      Title="ProgressesPage">
    <Grid Margin="24">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
            <RowDefinition Height="Auto" />
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" Text="Прогрессы" FontSize="28" FontWeight="SemiBold" Margin="0,0,0,12" />

        <!-- Выбор уровня -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,12">
            <TextBlock Text="Уровень:" VerticalAlignment="Center" Margin="0,0,8,0" />
            <ComboBox Width="320"
                      ItemsSource="{Binding Levels}"
                      DisplayMemberPath="Name"
                      SelectedItem="{Binding SelectedLevel, Mode=TwoWay}"
                      SelectionChanged="OnLevelChanged" />
            <TextBlock Margin="16,0,0,0" VerticalAlignment="Center"
                       Foreground="{DynamicResource WarningBrush}" Text="{Binding Status}" />
        </StackPanel>

        <!-- Таблица прогресса: столбцы 1–3 редактируемые, 4-й (с нуля) — read-only -->
        <DataGrid Grid.Row="2"
                  x:Name="RowsGrid"
                  ItemsSource="{Binding Rows}"
                  AutoGenerateColumns="False"
                  CanUserAddRows="False"
                  HeadersVisibility="Column"
                  GridLinesVisibility="All"
                  RowEditEnding="OnRowEditEnding">
            <DataGrid.Columns>
                <DataGridTextColumn Header="Практика (за сколько попыток)" Width="*"
                                    Binding="{Binding PracticeAttempts, UpdateSourceTrigger=PropertyChanged}" />
                <DataGridTextColumn Header="С парта до парта" Width="*"
                                    Binding="{Binding SegmentRange, UpdateSourceTrigger=PropertyChanged}" />
                <DataGridTextColumn Header="До ста" Width="*"
                                    Binding="{Binding ToHundredRange, UpdateSourceTrigger=PropertyChanged}" />
                <DataGridTemplateColumn Header="С нуля" Width="*" IsReadOnly="True">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <TextBlock VerticalAlignment="Center" Margin="6,0,0,0"
                                       Text="{Binding DataContext.FromZeroDisplay, RelativeSource={RelativeSource AncestorType=DataGrid}}" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
                <DataGridTemplateColumn Width="Auto" IsReadOnly="True">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate>
                            <Button Content="✕"
                                    Command="{Binding DataContext.DeleteRowCommand, RelativeSource={RelativeSource AncestorType=DataGrid}}"
                                    CommandParameter="{Binding}" />
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>
            </DataGrid.Columns>
        </DataGrid>

        <Button Grid.Row="3" Margin="0,12,0,0" HorizontalAlignment="Left"
                Content="Добавить строку" Command="{Binding AddRowCommand}" />
    </Grid>
</Page>
```

- [ ] **Step 2: Создать code-behind страницы**

Create `src/GdTracker.App/Views/ProgressesPage.xaml.cs`:

```csharp
using System.Windows.Controls;
using GdTracker.ViewModels;

namespace GdTracker.App.Views;

public partial class ProgressesPage : Page
{
    private readonly ProgressesViewModel _viewModel;

    public ProgressesPage(ProgressesViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    // Смена уровня пользователем: перезагрузить строки. Программную установку уровня из
    // LoadAsync пропускаем — там строки грузятся сами (IsLoadingLevels), без гонки.
    private async void OnLevelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_viewModel.IsLoadingLevels)
            return;
        await _viewModel.LoadRowsAsync();
    }

    // Значения ячеек уже в строке-VM (UpdateSourceTrigger=PropertyChanged), поэтому по
    // завершении редактирования строки достаточно сохранить её.
    private async void OnRowEditEnding(object sender, DataGridRowEditEndingEventArgs e)
    {
        if (e.Row.Item is LevelProgressRowViewModel row)
            await _viewModel.SaveRowAsync(row);
    }
}
```

- [ ] **Step 3: Добавить пункт навигации**

Modify `src/GdTracker.App/MainWindow.xaml` — внутри `<ui:NavigationView.MenuItems>`, сразу после пункта «Уровни»:

```xml
                <ui:NavigationViewItem Content="Уровни"
                                       TargetPageType="{x:Type views:DashboardPage}" />
                <ui:NavigationViewItem Content="Прогрессы"
                                       TargetPageType="{x:Type views:ProgressesPage}" />
```

- [ ] **Step 4: Зарегистрировать сервисы, страницу и VM в DI**

Modify `src/GdTracker.App/App.xaml.cs`:

В блоке «Репозитории» после `services.AddSingleton<IProgressRepository, ProgressRepository>();`:

```csharp
                services.AddSingleton<IProgressRepository, ProgressRepository>();
                services.AddSingleton<ILevelProgressRowRepository, LevelProgressRowRepository>();
```

Рядом с регистрацией навигации (после `services.AddSingleton<INavigationService, NavigationService>();`) добавить контекст навигации прогрессов:

```csharp
                services.AddSingleton<INavigationService, NavigationService>();
                // Контекст перехода на вкладку «Прогрессы»: какой уровень открыть.
                services.AddSingleton<IProgressNavigationContext, ProgressNavigationContext>();
```

В блоке «Страницы и их view-модели», после регистрации `DashboardPage`/`LevelsViewModel`:

```csharp
                services.AddTransient<DashboardPage>();
                services.AddTransient<LevelsViewModel>();
                services.AddTransient<ProgressesPage>();
                services.AddTransient<ProgressesViewModel>();
```

- [ ] **Step 5: Собрать решение**

Run: `dotnet build GdTracker.slnx`
Expected: `Сборка успешно завершена.`, 0 ошибок, 0 предупреждений.

- [ ] **Step 6: Прогнать все тесты (регрессия)**

Run: `dotnet test GdTracker.slnx`
Expected: PASS (все тесты).

- [ ] **Step 7: Коммит**

```bash
git add src/GdTracker.App/Views/ProgressesPage.xaml src/GdTracker.App/Views/ProgressesPage.xaml.cs src/GdTracker.App/MainWindow.xaml src/GdTracker.App/App.xaml.cs
git commit -m "feat: Progresses page + navigation item + DI registration"
```

---

### Task 5: Кнопка «Добавить прогресс» на вкладке «Уровни»

**Files:**
- Modify: `src/GdTracker.App/Views/DashboardPage.xaml` (новая кнопка в шапке деталей + переименование старой кнопки)
- Modify: `src/GdTracker.App/Views/DashboardPage.xaml.cs` (навигация из code-behind)

**Interfaces:**
- Consumes: `INavigationService` (Wpf.Ui), `IProgressNavigationContext`, `ProgressesPage`, `LevelDetailViewModel`.

- [ ] **Step 1: Добавить кнопку в шапку деталей уровня**

Modify `src/GdTracker.App/Views/DashboardPage.xaml` — внутри `DataTemplate` для `LevelDetailViewModel`, в `StackPanel` шапки (`Grid.Row="0"`), после внутреннего блока с процентами/попытками (перед закрывающим `</StackPanel>` этой шапки):

Найти конец шапки:
```xml
                                <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                    <TextBlock Text="{Binding Level.BestNormalPercent, StringFormat='Normal: {0}%'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.BestPracticePercent, StringFormat='Practice: {0}%'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.TotalAttempts, StringFormat='Попытки: {0}'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.IsCompleted, StringFormat='Пройден: {0}'}" />
                                </StackPanel>
                            </StackPanel>
```
и вставить кнопку перед закрывающим `</StackPanel>` шапки:
```xml
                                <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                    <TextBlock Text="{Binding Level.BestNormalPercent, StringFormat='Normal: {0}%'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.BestPracticePercent, StringFormat='Practice: {0}%'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.TotalAttempts, StringFormat='Попытки: {0}'}" Margin="0,0,16,0" />
                                    <TextBlock Text="{Binding Level.IsCompleted, StringFormat='Пройден: {0}'}" />
                                </StackPanel>
                                <Button Content="Добавить прогресс" Margin="0,8,0,0" HorizontalAlignment="Left"
                                        Click="OnOpenProgressesClick" />
                            </StackPanel>
```

- [ ] **Step 2: Переименовать старую кнопку формы, чтобы не было двух «Добавить прогресс»**

Modify `src/GdTracker.App/Views/DashboardPage.xaml` — в нижнем блоке формы ручного добавления записи заменить текст кнопки:

Было:
```xml
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <Button Content="Добавить прогресс" Command="{Binding AddProgressCommand}" />
```
Стало:
```xml
                                    <StackPanel Orientation="Horizontal" Margin="0,4,0,0">
                                        <Button Content="Добавить запись" Command="{Binding AddProgressCommand}" />
```

(Заголовок секции `<TextBlock Text="Добавить прогресс" FontWeight="SemiBold" .../>` выше по разметке заменить на `Добавить запись` тем же образом — чтобы подпись секции совпадала с кнопкой.)

- [ ] **Step 3: Внедрить навигацию в code-behind**

Modify `src/GdTracker.App/Views/DashboardPage.xaml.cs` — заменить целиком на:

```csharp
using System.Windows;
using System.Windows.Controls;
using GdTracker.ViewModels;
using Wpf.Ui;

namespace GdTracker.App.Views;

public partial class DashboardPage : Page
{
    private readonly LevelsViewModel _viewModel;
    private readonly INavigationService _navigation;
    private readonly IProgressNavigationContext _progressContext;

    public DashboardPage(
        LevelsViewModel viewModel,
        INavigationService navigation,
        IProgressNavigationContext progressContext)
    {
        _viewModel = viewModel;
        _navigation = navigation;
        _progressContext = progressContext;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.LoadAsync();
    }

    // Синхронизация выделения мышью (Ctrl/Shift) с IsSelected строк для группового удаления.
    private void OnLevelsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (var item in e.RemovedItems)
            if (item is LevelRowViewModel row)
                row.IsSelected = false;

        foreach (var item in e.AddedItems)
            if (item is LevelRowViewModel row)
                row.IsSelected = true;
    }

    // Кнопка «Добавить прогресс» в шапке деталей: запоминаем уровень и переходим на вкладку
    // «Прогрессы». DataContext кнопки — LevelDetailViewModel выбранного уровня.
    private void OnOpenProgressesClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LevelDetailViewModel { Level: { } level } })
        {
            _progressContext.TargetLevelId = level.Id;
            _navigation.Navigate(typeof(ProgressesPage));
        }
    }
}
```

- [ ] **Step 4: Собрать решение**

Run: `dotnet build GdTracker.slnx`
Expected: `Сборка успешно завершена.`, 0 ошибок, 0 предупреждений.

- [ ] **Step 5: Прогнать все тесты (регрессия)**

Run: `dotnet test GdTracker.slnx`
Expected: PASS (все тесты; view-модели «Уровней» не менялись, конструкторы прежние).

- [ ] **Step 6: Ручная проверка в приложении**

Запустить приложение (F5 в VS Code или `dotnet run --project src/GdTracker.App`). Проверить:
1. Появилась вкладка «Прогрессы».
2. На вкладке «Уровни» при выборе уровня в шапке деталей есть кнопка «Добавить прогресс», а кнопка формы ниже теперь называется «Добавить запись».
3. Нажатие «Добавить прогресс» открывает вкладку «Прогрессы» с этим уровнем в выпадающем списке.
4. «Добавить строку» добавляет строку; столбцы 1–3 редактируются и сохраняются (правку видно после смены уровня и обратно); столбец «С нуля» показывает `0-{Normal%}`; «✕» удаляет строку; после 100 строк «Добавить строку» неактивна.

- [ ] **Step 7: Коммит**

```bash
git add src/GdTracker.App/Views/DashboardPage.xaml src/GdTracker.App/Views/DashboardPage.xaml.cs
git commit -m "feat: 'Add progress' button navigates to Progresses tab"
```

---

## Примечания по реализации

- **Почему навигация в code-behind, а не во view-модели.** `LevelsViewModel` конструируется ~9 раз в тестах; добавление зависимости навигации потребовало бы правки всех этих мест. Навигация — забота слоя View (как в `MainWindow`), поэтому кнопка и переход живут в `DashboardPage.xaml.cs`. Это осознанное отклонение от исходной спецификации, где упоминался `IAppNavigator`; поведение при этом идентично.
- **Гонка загрузки строк.** Единственный путь загрузки строк — `LoadRowsAsync` (из `LoadAsync` для стартового уровня и из `OnLevelChanged` для пользовательских переключений). Флаг `IsLoadingLevels` гасит событие ComboBox во время программной установки уровня в `LoadAsync`, исключая параллельную загрузку и порчу `ObservableCollection`.
- **Сохранение правок.** Ячейки пишут в строку-VM по каждому нажатию (`UpdateSourceTrigger=PropertyChanged`), а `RowEditEnding` сохраняет строку в БД. Логика сохранения (`SaveRowAsync`) покрыта юнит-тестом; тонкая связка через событие DataGrid проверяется вручную (Task 5, Step 6).
