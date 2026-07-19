# График динамики звёзд и демонов — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** На вкладке «Статистика» показать одну диаграмму с двумя осями: звёзды слева, демоны справа, по накопленным снимкам.

**Architecture:** Репозиторий отдаёт историю снимков, вью-модель превращает её в две серии LiveCharts2 с двумя осями Y, страница рисует одну `CartesianChart`. Снимки уже копятся — новых источников данных не появляется.

**Tech Stack:** .NET 10, EF Core 10 + SQLite, CommunityToolkit.Mvvm 8.4.2, LiveChartsCore.SkiaSharpView 2.0.4, WPF-UI 4.3.0.

## Global Constraints

- Решение `.slnx`: `dotnet build GdTracker.slnx`, `dotnet test GdTracker.slnx`, никогда не `.sln`.
- Nullable enabled, file-scoped namespaces, XML-документация `///` **на русском**, строки интерфейса на русском.
- CommunityToolkit.Mvvm: `[ObservableProperty]` на полях с подчёркиванием.
- FluentAssertions 7.2.0 (8.x платная), xUnit, без mock-библиотек.
- В тестах реальный SQLite через `TestSupport.InMemorySqlite`, никогда `UseInMemoryDatabase`.
- LiveCharts2 используется как в остальных графиках страницы: серии присваиваются целыми массивами, метки категорий задаются через `Axis.Labels` позиционно к `Values`, без кастомных кистей и подсказок.
- Цвета: звёзды `#FFD54F`, демоны `#EF5350` — те же, что у соответствующих карточек, связь кривой с карточкой намеренная.
- Сейчас 118 тестов зелёных.

---

### Task A: История снимков и серии во вью-модели

**Files:**
- Modify: `src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs`
- Modify: `src/GdTracker.Data/Repositories/AccountStatsRepository.cs`
- Modify: `src/GdTracker.ViewModels/StatsViewModel.cs`
- Test: `tests/GdTracker.Tests/AccountStatsRepositoryTests.cs`
- Test: `tests/GdTracker.Tests/StatsViewModelTests.cs`

**Interfaces:**
- Consumes: `AccountStatsSnapshot` (поля `CapturedAt`, `Stars`, `Demons`), `InMemorySqlite`, `FakeSaveReader`, `FakeSettings` из существующих тестов.
- Produces: `IAccountStatsRepository.GetHistoryAsync(CancellationToken ct = default) → Task<IReadOnlyList<AccountStatsSnapshot>>`; свойства `StatsViewModel.TrendSeries` (`ISeries[]`), `TrendXAxes` (`Axis[]`), `TrendYAxes` (`Axis[]`).

- [ ] **Step 1: Написать падающие тесты репозитория**

Добавить в `tests/GdTracker.Tests/AccountStatsRepositoryTests.cs`:

```csharp
    [Fact]
    public async Task History_is_empty_when_no_snapshots()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        (await repo.GetHistoryAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task History_is_ordered_from_oldest_to_newest()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(886), null);
        await repo.AddIfChangedAsync(Sample(890), null);
        await repo.AddIfChangedAsync(Sample(895), null);

        var history = await repo.GetHistoryAsync();

        history.Should().HaveCount(3);
        history.Select(h => h.Stars).Should().ContainInOrder(886L, 890L, 895L);
    }
```

- [ ] **Step 2: Запустить и убедиться, что падают**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsRepositoryTests"`
Expected: FAIL — метода `GetHistoryAsync` не существует.

- [ ] **Step 3: Добавить метод в интерфейс**

В `src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs` внутрь интерфейса:

```csharp
    /// <summary>
    /// Все снимки от старых к новым — для графика динамики.
    /// Ограничения по количеству нет: строка пишется только при изменении метрик, их мало.
    /// </summary>
    Task<IReadOnlyList<AccountStatsSnapshot>> GetHistoryAsync(CancellationToken ct = default);
```

- [ ] **Step 4: Реализовать в репозитории**

В `src/GdTracker.Data/Repositories/AccountStatsRepository.cs` добавить метод:

```csharp
    public async Task<IReadOnlyList<AccountStatsSnapshot>> GetHistoryAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountStatsSnapshots.AsNoTracking()
            .OrderBy(s => s.CapturedAt)
            .ThenBy(s => s.Id)
            .ToListAsync(ct);
    }
```

- [ ] **Step 5: Запустить тесты репозитория**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsRepositoryTests"`
Expected: PASS.

- [ ] **Step 6: Написать падающие тесты вью-модели**

Добавить в `tests/GdTracker.Tests/StatsViewModelTests.cs`:

```csharp
    [Fact]
    public async Task Trend_has_two_series_scaled_to_separate_axes()
    {
        using var factory = new InMemorySqlite();
        var vm = Build(factory, new FakeSaveReader(Stats()));

        await vm.LoadAsync();

        vm.TrendSeries.Should().HaveCount(2);
        vm.TrendYAxes.Should().HaveCount(2);
        // Демоны меньше звёзд в десятки раз — на общей оси они выродились бы в прямую.
        vm.TrendSeries[1].ScalesYAt.Should().Be(1);
    }

    [Fact]
    public async Task Trend_plots_every_snapshot_with_matching_labels()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);
        await repo.AddIfChangedAsync(Stats(886), null);
        await repo.AddIfChangedAsync(Stats(890), null);
        var vm = Build(factory, new FakeSaveReader(Stats(890)));

        await vm.LoadAsync();

        var stars = (LineSeries<double>)vm.TrendSeries[0];
        stars.Values!.Should().HaveCount(2);
        vm.TrendXAxes[0].Labels.Should().HaveCount(2);
    }

    [Fact]
    public async Task Trend_with_single_snapshot_has_one_point()
    {
        using var factory = new InMemorySqlite();
        var vm = Build(factory, new FakeSaveReader(Stats()));

        await vm.LoadAsync();

        var stars = (LineSeries<double>)vm.TrendSeries[0];
        stars.Values!.Should().HaveCount(1);
    }

    [Fact]
    public async Task Trend_is_empty_without_snapshots_and_does_not_throw()
    {
        using var factory = new InMemorySqlite();
        // Сейв недоступен — снимков не появится вовсе.
        var reader = new FakeSaveReader(Stats()) { DefaultSaveFilePath = null };
        var vm = Build(factory, reader);

        await vm.LoadAsync();

        var stars = (LineSeries<double>)vm.TrendSeries[0];
        stars.Values!.Should().BeEmpty();
    }
```

Файлу понадобятся `using GdTracker.Data.Repositories;` и `using LiveChartsCore.SkiaSharpView;` — добавь, если их ещё нет.

- [ ] **Step 7: Запустить и убедиться, что падают**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: FAIL — свойств `TrendSeries`/`TrendXAxes`/`TrendYAxes` не существует.

- [ ] **Step 8: Добавить свойства и построение серий**

В `src/GdTracker.ViewModels/StatsViewModel.cs` рядом с остальными свойствами секции «Аккаунт»:

```csharp
    [ObservableProperty] private ISeries[] _trendSeries = [];
    [ObservableProperty] private Axis[] _trendXAxes = [];
    [ObservableProperty] private Axis[] _trendYAxes = [];
```

Добавить приватный метод:

```csharp
    /// <summary>
    /// Строит график динамики по накопленным снимкам. Звёзды и демоны различаются
    /// в десятки раз, поэтому демоны идут по отдельной правой оси (ScalesYAt = 1),
    /// иначе их кривая выродилась бы в прямую по нулю.
    /// </summary>
    private async Task LoadTrendAsync()
    {
        var history = await _accountStats.GetHistoryAsync();

        TrendSeries =
        [
            new LineSeries<double>
            {
                Name = "Звёзды",
                Values = history.Select(h => (double)h.Stars).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(0xFF, 0xD5, 0x4F), 2),
                GeometryStroke = new SolidColorPaint(new SKColor(0xFF, 0xD5, 0x4F), 2),
                Fill = null,
            },
            new LineSeries<double>
            {
                Name = "Демоны",
                Values = history.Select(h => (double)h.Demons).ToArray(),
                Stroke = new SolidColorPaint(new SKColor(0xEF, 0x53, 0x50), 2),
                GeometryStroke = new SolidColorPaint(new SKColor(0xEF, 0x53, 0x50), 2),
                Fill = null,
                ScalesYAt = 1,
            },
        ];

        TrendXAxes = [new Axis { Labels = history.Select(h => h.CapturedAt.ToLocalTime().ToString("dd.MM")).ToArray() }];
        TrendYAxes =
        [
            new Axis { Name = "Звёзды" },
            new Axis { Name = "Демоны", Position = AxisPosition.End },
        ];
    }
```

Понадобятся `using LiveChartsCore.SkiaSharpView.Painting;`, `using SkiaSharp;` и `using LiveChartsCore.Measure;` — добавь к существующим.

- [ ] **Step 9: Вызвать построение на обеих ветках загрузки**

В `LoadAccountAsync` вызвать `await LoadTrendAsync();` так, чтобы график строился и когда сейв прочитан, и когда чтение пропущено по неизменившемуся времени файла. Иначе после появления нового снимка график остался бы вчерашним.

Конкретно: добавить вызов после успешного `Apply(snapshot, readAt)` и перед обоими ранними возвратами по ветке пропуска чтения. Проще всего — вынести в конец метода общим путём, но следи, чтобы вызов не попал на ветки, где путь к сейву не задан или файл не найден: там история тоже нужна (снимки могли остаться от прошлых запусков), так что вызывать её надо и там. Единственное место, где вызов не нужен — блок `catch`, потому что там уже показывается ошибка.

- [ ] **Step 10: Запустить тесты**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: PASS.

- [ ] **Step 11: Полный прогон**

Run: `dotnet test GdTracker.slnx`
Expected: 118 существующих + 6 новых = 124, все зелёные.

- [ ] **Step 12: Коммит**

```bash
git add src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs src/GdTracker.Data/Repositories/AccountStatsRepository.cs src/GdTracker.ViewModels/StatsViewModel.cs tests/GdTracker.Tests/AccountStatsRepositoryTests.cs tests/GdTracker.Tests/StatsViewModelTests.cs
git commit -m "История снимков и серии графика динамики"
```

---

### Task B: График на странице

**Files:**
- Modify: `src/GdTracker.App/Views/StatsPage.xaml`

**Interfaces:**
- Consumes: `TrendSeries`, `TrendXAxes`, `TrendYAxes` из Task A.
- Produces: видимая диаграмма на странице.

- [ ] **Step 1: Добавить диаграмму в разметку**

В `src/GdTracker.App/Views/StatsPage.xaml` вставить сразу после блока с девятью карточками аккаунта и перед подзаголовком «Прогресс в трекере»:

```xml
            <!-- Динамика: звёзды по левой оси, демоны по правой (различаются в десятки раз) -->
            <TextBlock Text="Динамика звёзд и демонов" FontSize="18" FontWeight="SemiBold" Margin="0,16,0,8" />
            <lvc:CartesianChart Series="{Binding TrendSeries}"
                                XAxes="{Binding TrendXAxes}"
                                YAxes="{Binding TrendYAxes}"
                                Height="260"
                                LegendPosition="Top" />
```

- [ ] **Step 2: Собрать**

Run: `dotnet build GdTracker.slnx`
Expected: 0 ошибок. Перед сборкой убедись, что не запущено ни одного процесса `GdTracker.App`, иначе DLL заблокированы.

- [ ] **Step 3: Прогнать тесты**

Run: `dotnet test GdTracker.slnx`
Expected: 124 зелёных, без изменений.

- [ ] **Step 4: Коммит**

```bash
git add src/GdTracker.App/Views/StatsPage.xaml
git commit -m "График динамики звёзд и демонов на странице статистики"
```

---

## Замечание по проверке

Увидеть расхождение двух кривых на одной точке невозможно, а писать выдуманную историю в
рабочую базу пользователя нельзя — это подделка его данных. Визуальную проверку выполняет
контролирующий агент на копии базы во временном каталоге, засеянной синтетическими замерами.
Исполнителям задач запускать приложение не требуется.
