# Статистика аккаунта из сейва GD — план реализации

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Вкладка «Статистика» показывает счётчики аккаунта GD из сейв-файла независимо от того, есть ли уровни в БД трекера.

**Architecture:** Новый парсер `AccountStatsParser` вычитывает блок `GS_value` из уже декодированного plist (существующий `SaveFileCodec` не трогаем). Прочитанное складывается снимками в новую таблицу `AccountStatsSnapshots`; страница рисует последний снимок мгновенно, а сейв перечитывает в фоне и только если файл изменился. Путь к сейву переезжает в сохраняемые настройки.

**Tech Stack:** .NET 10 (`net10.0` для библиотек, `net10.0-windows` для WPF), EF Core 10 + SQLite, CommunityToolkit.Mvvm 8.4.2, WPF-UI 4.3.0, xUnit 2.9.3 + FluentAssertions 7.2.0.

## Global Constraints

- Решение в формате `.slnx`: команды вида `dotnet test GdTracker.slnx`, **не** `.sln`.
- FluentAssertions запинен на 7.2.0 (8.0+ коммерческая) — не обновлять.
- Nullable enabled, file-scoped namespaces, XML-документация `///` **на русском**.
- MVVM: `[ObservableProperty]` на полях с подчёркиванием, `[RelayCommand]` на `private async Task XxxAsync()`.
- Тесты: рукописные фейки, без Moq/NSubstitute. БД в тестах — реальный SQLite через `TestSupport.InMemorySqlite`, никогда `UseInMemoryDatabase`.
- Фикстуры парсера — синтетический plist строковой константой, никаких реальных `.dat` файлов в тестах.
- Сейв-файл открывается только на чтение и только с `FileShare.ReadWrite` — игра может держать его открытым. Приложение никогда не пишет в сейв.
- Подписи в UI строго: ключ 8 — «Секретные монеты» (не «Монеты»), ключ 22 — «Сферы (всего)» (не «Сферы»). Обе ошибки незаметны при тестировании.
- Все строки интерфейса на русском.

---

### Task 1: Парсер блока `GS_value`

**Files:**
- Create: `src/GdTracker.Core/Models/AccountStats.cs`
- Create: `src/GdTracker.GameSync/AccountStatsParser.cs`
- Test: `tests/GdTracker.Tests/AccountStatsParserTests.cs`

**Interfaces:**
- Consumes: ничего (первая задача).
- Produces: `GdTracker.Core.Models.AccountStats` (record с `long`-свойствами `Stars`, `Moons`, `Demons`, `OnlineLevelsCompleted`, `OfficialLevelsCompleted`, `SecretCoins`, `Attempts`, `Jumps`, `TotalOrbs` и `IReadOnlyDictionary<string, long> RawValues`); `GdTracker.GameSync.AccountStatsParser.Parse(string plistXml) → AccountStats?`.

- [ ] **Step 1: Написать падающий тест**

Создать `tests/GdTracker.Tests/AccountStatsParserTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class AccountStatsParserTests
{
    // Структура реального CCGameManager.dat: числовые ключи вперемешку с unique_<id>_<coin>.
    private const string Sample =
        "<?xml version=\"1.0\"?><plist version=\"1.0\" gjver=\"2.0\"><dict>" +
        "<k>GLM_01</k><d><k>1</k><d><k>k1</k><i>1</i></d></d>" +
        "<k>GS_value</k><d>" +
            "<k>1</k><i>258487</i>" +      // прыжки
            "<k>2</k><i>43329</i>" +       // попытки
            "<k>3</k><i>27</i>" +          // официальные уровни
            "<k>4</k><i>322</i>" +         // онлайн-уровни
            "<k>5</k><i>17</i>" +          // демоны
            "<k>6</k><i>886</i>" +         // звёзды
            "<k>8</k><i>84</i>" +          // секретные монеты
            "<k>12</k><i>82</i>" +         // пользовательские монеты (не путать с 8)
            "<k>14</k><i>10682</i>" +      // текущий баланс сфер (не путать с 22)
            "<k>22</k><i>49359</i>" +      // сферы за всё время
            "<k>28</k><i>84</i>" +         // луны
            "<k>99</k><i>7</i>" +          // неизвестный ключ — должен попасть только в RawValues
            "<k>unique_128_1</k><i>1</i>" +
            "<k>unique_128_2</k><i>1</i>" +
        "</d>" +
        "</dict></plist>";

    [Fact]
    public void Reads_all_nine_displayed_metrics()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats.Should().NotBeNull();
        stats!.Jumps.Should().Be(258487);
        stats.Attempts.Should().Be(43329);
        stats.OfficialLevelsCompleted.Should().Be(27);
        stats.OnlineLevelsCompleted.Should().Be(322);
        stats.Demons.Should().Be(17);
        stats.Stars.Should().Be(886);
        stats.SecretCoins.Should().Be(84);
        stats.TotalOrbs.Should().Be(49359);
        stats.Moons.Should().Be(84);
    }

    [Fact]
    public void Secret_coins_and_total_orbs_are_not_confused_with_their_neighbours()
    {
        var stats = AccountStatsParser.Parse(Sample);

        // Ключ 8 — секретные монеты (84), ключ 12 — пользовательские (82).
        stats!.SecretCoins.Should().Be(84);
        // Ключ 22 — сферы за всё время (49359), ключ 14 — текущий баланс (10682).
        stats.TotalOrbs.Should().Be(49359);
    }

    [Fact]
    public void Ignores_non_numeric_unique_coin_keys()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats!.RawValues.Should().NotContainKey("unique_128_1");
        stats.RawValues.Keys.Should().OnlyContain(k => k.All(char.IsAsciiDigit));
    }

    [Fact]
    public void Keeps_unknown_numeric_keys_in_raw_values()
    {
        var stats = AccountStatsParser.Parse(Sample);

        stats!.RawValues["99"].Should().Be(7);
        stats.RawValues["12"].Should().Be(82);
    }

    [Fact]
    public void Missing_keys_default_to_zero()
    {
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GS_value</k><d><k>6</k><i>5</i></d></dict></plist>";

        var stats = AccountStatsParser.Parse(xml);

        stats!.Stars.Should().Be(5);
        stats.Moons.Should().Be(0);      // ключ 28 отсутствует в сейвах до 2.2
        stats.Demons.Should().Be(0);
    }

    [Fact]
    public void Returns_null_when_gs_value_block_is_absent()
    {
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>GLM_01</k><d><k>1</k><d><k>k1</k><i>1</i></d></d></dict></plist>";

        AccountStatsParser.Parse(xml).Should().BeNull();
    }
}
```

- [ ] **Step 2: Запустить тест и убедиться, что он падает**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsParserTests"`
Expected: FAIL — ошибка компиляции, `AccountStatsParser` не существует.

- [ ] **Step 3: Создать модель**

Создать `src/GdTracker.Core/Models/AccountStats.cs`:

```csharp
namespace GdTracker.Core.Models;

/// <summary>
/// Счётчики аккаунта Geometry Dash из блока GS_value сейв-файла.
/// Значения накопительные: они не уменьшаются, когда уровень исчезает из локальных записей,
/// поэтому агрегацией таблицы уровней их получить нельзя.
/// </summary>
public sealed record AccountStats
{
    /// <summary>Звёзды (ключ 6).</summary>
    public long Stars { get; init; }

    /// <summary>Луны — валюта платформерных уровней 2.2 (ключ 28).</summary>
    public long Moons { get; init; }

    /// <summary>Пройдено демонов (ключ 5).</summary>
    public long Demons { get; init; }

    /// <summary>Пройдено онлайн-уровней (ключ 4).</summary>
    public long OnlineLevelsCompleted { get; init; }

    /// <summary>Пройдено официальных уровней (ключ 3).</summary>
    public long OfficialLevelsCompleted { get; init; }

    /// <summary>Секретные монеты (ключ 8). Не путать с пользовательскими монетами (ключ 12).</summary>
    public long SecretCoins { get; init; }

    /// <summary>Попыток за всё время (ключ 2).</summary>
    public long Attempts { get; init; }

    /// <summary>Прыжков за всё время (ключ 1).</summary>
    public long Jumps { get; init; }

    /// <summary>Сфер собрано за всё время (ключ 22). Не путать с текущим балансом (ключ 14).</summary>
    public long TotalOrbs { get; init; }

    /// <summary>
    /// Все числовые ключи GS_value как есть — архив на будущее.
    /// Историю по метрикам вне девятки иначе не восстановить задним числом.
    /// </summary>
    public IReadOnlyDictionary<string, long> RawValues { get; init; }
        = new Dictionary<string, long>();
}
```

- [ ] **Step 4: Создать парсер**

Создать `src/GdTracker.GameSync/AccountStatsParser.cs`:

```csharp
using System.Xml.Linq;
using GdTracker.Core.Models;

namespace GdTracker.GameSync;

/// <summary>
/// Извлекает счётчики аккаунта из блока GS_value декодированного plist сейва GD.
/// Расшифровка ключей — реверс-инжиниринг сообщества (GD Docs и линейка gd.py/GDColon),
/// проверенная на реальном сейве: ключ 3 сходится точно с числом официальных уровней на 100%.
/// Показываются только ключи, по которым источники не расходятся.
/// </summary>
public static class AccountStatsParser
{
    private const string GsValueKey = "<k>GS_value</k>";

    // Номера ключей внутри GS_value.
    private const string KeyJumps = "1";
    private const string KeyAttempts = "2";
    private const string KeyOfficialLevels = "3";
    private const string KeyOnlineLevels = "4";
    private const string KeyDemons = "5";
    private const string KeyStars = "6";
    private const string KeySecretCoins = "8";   // 12 — пользовательские монеты, это другое
    private const string KeyTotalOrbs = "22";    // 14 — текущий баланс, это другое
    private const string KeyMoons = "28";

    /// <summary>
    /// Разбирает GS_value. Возвращает null, если блока в файле нет
    /// (иначе нельзя отличить «статистики нет» от «всё по нулям»).
    /// </summary>
    public static AccountStats? Parse(string plistXml)
    {
        var fragment = ExtractGsValueFragment(plistXml);
        if (fragment is null)
            return null;

        var values = ReadNumericEntries(XDocument.Parse(fragment).Root!);

        return new AccountStats
        {
            Jumps = Get(values, KeyJumps),
            Attempts = Get(values, KeyAttempts),
            OfficialLevelsCompleted = Get(values, KeyOfficialLevels),
            OnlineLevelsCompleted = Get(values, KeyOnlineLevels),
            Demons = Get(values, KeyDemons),
            Stars = Get(values, KeyStars),
            SecretCoins = Get(values, KeySecretCoins),
            TotalOrbs = Get(values, KeyTotalOrbs),
            Moons = Get(values, KeyMoons),
            RawValues = values,
        };
    }

    /// <summary>
    /// Вырезает словарь-значение ключа GS_value как самостоятельный XML-фрагмент.
    /// Разбирать документ целиком нельзя по цене: сейв распаковывается в ~56 млн символов,
    /// а нужный блок — около 3 КБ в самом его конце.
    /// </summary>
    private static string? ExtractGsValueFragment(string xml)
    {
        var keyIdx = xml.IndexOf(GsValueKey, StringComparison.Ordinal);
        if (keyIdx < 0)
            return null;

        var start = xml.IndexOf('<', keyIdx + GsValueKey.Length);
        if (start < 0)
            return null;

        var depth = 0;
        var i = start;
        while (i < xml.Length)
        {
            var lt = xml.IndexOf('<', i);
            if (lt < 0)
                break;

            var gt = xml.IndexOf('>', lt);
            if (gt < 0)
                break;

            // Сканируется только блок GS_value (~3 КБ), не весь документ:
            // поиск по 56 млн символов уже позади, в IndexOf выше.
            var tag = xml.Substring(lt + 1, gt - lt - 1).Trim();
            var selfClosing = tag.EndsWith('/');
            var closing = tag.StartsWith('/');
            var name = tag.Trim('/', ' ');

            if (!selfClosing && name is "d" or "dict")
            {
                if (closing)
                {
                    depth--;
                    if (depth == 0)
                        return xml.Substring(start, gt - start + 1);
                }
                else
                {
                    depth++;
                }
            }

            i = gt + 1;
        }

        return null;
    }

    /// <summary>
    /// Собирает пары «числовой ключ → значение».
    /// В GS_value рядом со статистикой лежат ~77 записей unique_&lt;levelID&gt;_&lt;coinIdx&gt;
    /// (флаги собранных монет официальных уровней) — они отбрасываются.
    /// </summary>
    private static Dictionary<string, long> ReadNumericEntries(XElement dict)
    {
        var values = new Dictionary<string, long>();
        string? key = null;

        foreach (var el in dict.Elements())
        {
            if (el.Name.LocalName == "k")
            {
                key = el.Value;
                continue;
            }

            if (key is not null && IsNumericKey(key) && long.TryParse(el.Value, out var v))
                values[key] = v;

            key = null;
        }

        return values;
    }

    private static bool IsNumericKey(string key)
        => key.Length > 0 && key.All(char.IsAsciiDigit);

    private static long Get(Dictionary<string, long> values, string key)
        => values.TryGetValue(key, out var v) ? v : 0;
}
```

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsParserTests"`
Expected: PASS, 6 тестов.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.Core/Models/AccountStats.cs src/GdTracker.GameSync/AccountStatsParser.cs tests/GdTracker.Tests/AccountStatsParserTests.cs
git commit -m "Парсер счётчиков аккаунта из блока GS_value"
```

---

### Task 2: Чтение статистики из файла сейва

**Files:**
- Modify: `src/GdTracker.Core/Abstractions/ISaveFileReader.cs`
- Modify: `src/GdTracker.GameSync/SaveFileReader.cs`

**Interfaces:**
- Consumes: `AccountStatsParser.Parse(string) → AccountStats?` из Task 1.
- Produces: `ISaveFileReader.ReadAccountStats(string saveFilePath) → AccountStats?` и `ISaveFileReader.GetLastWriteTimeUtc(string saveFilePath) → DateTime?`.

Тестов на этом шаге нет: `SaveFileReader` — тонкая обёртка над файловой системой, вся логика уже покрыта в Task 1. Это соответствует текущему состоянию проекта, где `SaveFileReader` и `SaveFileLocator` тестами не покрыты.

- [ ] **Step 1: Расширить интерфейс**

В `src/GdTracker.Core/Abstractions/ISaveFileReader.cs` добавить два члена внутрь интерфейса, после `ReadLevels`:

```csharp
    /// <summary>
    /// Декодирует сейв и возвращает счётчики аккаунта.
    /// Возвращает null, если блока GS_value в файле нет.
    /// </summary>
    AccountStats? ReadAccountStats(string saveFilePath);

    /// <summary>Время последней записи сейв-файла (UTC), либо null, если файла нет.</summary>
    DateTime? GetLastWriteTimeUtc(string saveFilePath);
```

- [ ] **Step 2: Реализовать в SaveFileReader**

В `src/GdTracker.GameSync/SaveFileReader.cs` добавить после метода `ReadLevels`:

```csharp
    public AccountStats? ReadAccountStats(string saveFilePath)
    {
        var bytes = ReadAllBytesShared(saveFilePath);
        var xml = SaveFileCodec.Decode(bytes);
        return AccountStatsParser.Parse(xml);
    }

    public DateTime? GetLastWriteTimeUtc(string saveFilePath)
        => File.Exists(saveFilePath) ? File.GetLastWriteTimeUtc(saveFilePath) : null;
```

- [ ] **Step 3: Собрать решение**

Run: `dotnet build GdTracker.slnx`
Expected: `Сборка успешно завершена`, 0 ошибок. Предупреждения NU1701/NU1903 присутствуют и до этих изменений — это не регрессия.

- [ ] **Step 4: Прогнать весь набор тестов**

Run: `dotnet test GdTracker.slnx`
Expected: все тесты зелёные (44 существующих + 6 новых из Task 1 = 50).

- [ ] **Step 5: Коммит**

```bash
git add src/GdTracker.Core/Abstractions/ISaveFileReader.cs src/GdTracker.GameSync/SaveFileReader.cs
git commit -m "Чтение счётчиков аккаунта и времени записи сейва"
```

---

### Task 3: Сущность снимка и миграция

**Files:**
- Create: `src/GdTracker.Core/Models/AccountStatsSnapshot.cs`
- Modify: `src/GdTracker.Data/AppDbContext.cs`
- Create: `src/GdTracker.Data/Migrations/<timestamp>_AddAccountStatsSnapshot.cs` (генерируется)

**Interfaces:**
- Consumes: ничего.
- Produces: сущность `AccountStatsSnapshot` с полями `Id` (int), `CapturedAt` (DateTime), `SaveFileWrittenAt` (DateTime?), девятью `long`-метриками с теми же именами, что в `AccountStats`, и `RawValuesJson` (string); `AppDbContext.AccountStatsSnapshots`.

- [ ] **Step 1: Создать сущность**

Создать `src/GdTracker.Core/Models/AccountStatsSnapshot.cs`:

```csharp
namespace GdTracker.Core.Models;

/// <summary>
/// Снимок счётчиков аккаунта на момент чтения сейва.
/// Копится, чтобы позже построить динамику: сам сейв истории не хранит.
/// </summary>
public class AccountStatsSnapshot
{
    public int Id { get; set; }

    /// <summary>Когда снимок сделан (UTC).</summary>
    public DateTime CapturedAt { get; set; }

    /// <summary>Время последней записи сейв-файла на момент снимка (UTC).</summary>
    public DateTime? SaveFileWrittenAt { get; set; }

    public long Stars { get; set; }
    public long Moons { get; set; }
    public long Demons { get; set; }
    public long OnlineLevelsCompleted { get; set; }
    public long OfficialLevelsCompleted { get; set; }
    public long SecretCoins { get; set; }
    public long Attempts { get; set; }
    public long Jumps { get; set; }
    public long TotalOrbs { get; set; }

    /// <summary>
    /// Все числовые ключи GS_value в виде JSON — архив на будущее.
    /// Единственное, что нельзя добавить задним числом: без него история
    /// по метрикам вне девятки начнётся только с момента будущей доработки.
    /// </summary>
    public string RawValuesJson { get; set; } = "{}";
}
```

- [ ] **Step 2: Зарегистрировать в контексте**

В `src/GdTracker.Data/AppDbContext.cs` добавить после строки с `VideoClips`:

```csharp
    public DbSet<AccountStatsSnapshot> AccountStatsSnapshots => Set<AccountStatsSnapshot>();
```

И внутрь `OnModelCreating`, после блока `modelBuilder.Entity<VideoClip>(...)`:

```csharp
        modelBuilder.Entity<AccountStatsSnapshot>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.RawValuesJson).IsRequired();
            e.HasIndex(x => x.CapturedAt);
        });
```

- [ ] **Step 3: Создать миграцию**

```bash
dotnet tool restore
dotnet dotnet-ef migrations add AddAccountStatsSnapshot --project src/GdTracker.Data --startup-project src/GdTracker.App
```

Expected: созданы `<timestamp>_AddAccountStatsSnapshot.cs`, `.Designer.cs` и обновлён `AppDbContextModelSnapshot.cs`.

- [ ] **Step 4: Проверить содержимое миграции**

Открыть созданный `<timestamp>_AddAccountStatsSnapshot.cs` и убедиться, что `Up()` содержит **только** `CreateTable("AccountStatsSnapshots", ...)` и `CreateIndex`. Если там оказались изменения других таблиц — значит в рабочей копии есть незакоммиченные правки модели; в этом случае остановиться и сообщить, не коммитить.

- [ ] **Step 5: Проверить, что миграция применяется**

Run: `dotnet build GdTracker.slnx`
Expected: 0 ошибок.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.Core/Models/AccountStatsSnapshot.cs src/GdTracker.Data/AppDbContext.cs src/GdTracker.Data/Migrations/
git commit -m "Сущность снимка статистики аккаунта и миграция"
```

---

### Task 4: Репозиторий снимков

**Files:**
- Create: `src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs`
- Create: `src/GdTracker.Data/Repositories/AccountStatsRepository.cs`
- Test: `tests/GdTracker.Tests/AccountStatsRepositoryTests.cs`

**Interfaces:**
- Consumes: `AccountStats` (Task 1), `AccountStatsSnapshot` (Task 3).
- Produces: `IAccountStatsRepository.GetLatestAsync(CancellationToken) → Task<AccountStatsSnapshot?>` и `AddIfChangedAsync(AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken) → Task<AccountStatsSnapshot>`.

- [ ] **Step 1: Написать падающие тесты**

Создать `tests/GdTracker.Tests/AccountStatsRepositoryTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;

namespace GdTracker.Tests;

public class AccountStatsRepositoryTests
{
    private static AccountStats Sample(long stars = 886) => new()
    {
        Stars = stars,
        Moons = 84,
        Demons = 17,
        OnlineLevelsCompleted = 322,
        OfficialLevelsCompleted = 27,
        SecretCoins = 84,
        Attempts = 43329,
        Jumps = 258487,
        TotalOrbs = 49359,
        RawValues = new Dictionary<string, long> { ["6"] = stars, ["99"] = 7 },
    };

    [Fact]
    public async Task Returns_null_when_no_snapshots_yet()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        (await repo.GetLatestAsync()).Should().BeNull();
    }

    [Fact]
    public async Task Stores_first_snapshot()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var saved = await repo.AddIfChangedAsync(Sample(), new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc));

        saved.Stars.Should().Be(886);
        saved.Jumps.Should().Be(258487);
        saved.SaveFileWrittenAt.Should().Be(new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc));
        (await repo.GetLatestAsync())!.Stars.Should().Be(886);
    }

    [Fact]
    public async Task Does_not_duplicate_unchanged_stats()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(), null);
        await repo.AddIfChangedAsync(Sample(), null);

        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(1);
    }

    [Fact]
    public async Task Stores_new_row_when_a_value_changed()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(886), null);
        var second = await repo.AddIfChangedAsync(Sample(890), null);

        second.Stars.Should().Be(890);
        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(2);
    }

    [Fact]
    public async Task Returns_existing_snapshot_when_nothing_changed()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var first = await repo.AddIfChangedAsync(Sample(), null);
        var second = await repo.AddIfChangedAsync(Sample(), null);

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task Latest_is_the_most_recent_by_captured_at()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(886), null);
        await repo.AddIfChangedAsync(Sample(900), null);

        (await repo.GetLatestAsync())!.Stars.Should().Be(900);
    }

    [Fact]
    public async Task Archives_raw_values_as_json()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        var saved = await repo.AddIfChangedAsync(Sample(), null);

        saved.RawValuesJson.Should().Contain("\"99\":7");
    }

    [Fact]
    public async Task Save_file_time_alone_does_not_create_a_new_row()
    {
        using var factory = new InMemorySqlite();
        var repo = new AccountStatsRepository(factory);

        await repo.AddIfChangedAsync(Sample(), new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc));
        await repo.AddIfChangedAsync(Sample(), new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc));

        await using var db = factory.CreateDbContext();
        db.AccountStatsSnapshots.Count().Should().Be(1);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsRepositoryTests"`
Expected: FAIL — ошибка компиляции, `AccountStatsRepository` не существует.

- [ ] **Step 3: Создать интерфейс**

Создать `src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs`:

```csharp
using GdTracker.Core.Models;

namespace GdTracker.Core.Abstractions;

/// <summary>Хранилище снимков статистики аккаунта.</summary>
public interface IAccountStatsRepository
{
    /// <summary>Последний снимок по времени съёмки, либо null, если снимков ещё нет.</summary>
    Task<AccountStatsSnapshot?> GetLatestAsync(CancellationToken ct = default);

    /// <summary>
    /// Сохраняет снимок, если хоть одна метрика отличается от последнего.
    /// В любом случае возвращает актуальный снимок — новый или существующий.
    /// </summary>
    Task<AccountStatsSnapshot> AddIfChangedAsync(
        AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken ct = default);
}
```

- [ ] **Step 4: Реализовать репозиторий**

Создать `src/GdTracker.Data/Repositories/AccountStatsRepository.cs`:

```csharp
using System.Text.Json;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Data.Repositories;

/// <inheritdoc />
public class AccountStatsRepository : IAccountStatsRepository
{
    private readonly IDbContextFactory<AppDbContext> _factory;

    public AccountStatsRepository(IDbContextFactory<AppDbContext> factory) => _factory = factory;

    public async Task<AccountStatsSnapshot?> GetLatestAsync(CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);
        return await db.AccountStatsSnapshots.AsNoTracking()
            .OrderByDescending(s => s.CapturedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AccountStatsSnapshot> AddIfChangedAsync(
        AccountStats stats, DateTime? saveFileWrittenAt, CancellationToken ct = default)
    {
        await using var db = await _factory.CreateDbContextAsync(ct);

        var latest = await db.AccountStatsSnapshots
            .OrderByDescending(s => s.CapturedAt)
            .ThenByDescending(s => s.Id)
            .FirstOrDefaultAsync(ct);

        if (latest is not null && !HasChanges(latest, stats))
        {
            // Время записи файла само по себе новую строку не создаёт: игра переписывает
            // сейв при каждом выходе, даже если ни один счётчик не изменился.
            if (saveFileWrittenAt is not null && latest.SaveFileWrittenAt != saveFileWrittenAt)
            {
                latest.SaveFileWrittenAt = saveFileWrittenAt;
                await db.SaveChangesAsync(ct);
            }

            return latest;
        }

        var snapshot = new AccountStatsSnapshot
        {
            CapturedAt = DateTime.UtcNow,
            SaveFileWrittenAt = saveFileWrittenAt,
            Stars = stats.Stars,
            Moons = stats.Moons,
            Demons = stats.Demons,
            OnlineLevelsCompleted = stats.OnlineLevelsCompleted,
            OfficialLevelsCompleted = stats.OfficialLevelsCompleted,
            SecretCoins = stats.SecretCoins,
            Attempts = stats.Attempts,
            Jumps = stats.Jumps,
            TotalOrbs = stats.TotalOrbs,
            RawValuesJson = JsonSerializer.Serialize(stats.RawValues),
        };

        db.AccountStatsSnapshots.Add(snapshot);
        await db.SaveChangesAsync(ct);
        return snapshot;
    }

    /// <summary>
    /// Сравнение только по девяти метрикам, не по RawValuesJson: посторонний ключ,
    /// меняющийся при каждом запуске игры, иначе плодил бы строки на пустом месте.
    /// </summary>
    private static bool HasChanges(AccountStatsSnapshot latest, AccountStats stats)
        => latest.Stars != stats.Stars
           || latest.Moons != stats.Moons
           || latest.Demons != stats.Demons
           || latest.OnlineLevelsCompleted != stats.OnlineLevelsCompleted
           || latest.OfficialLevelsCompleted != stats.OfficialLevelsCompleted
           || latest.SecretCoins != stats.SecretCoins
           || latest.Attempts != stats.Attempts
           || latest.Jumps != stats.Jumps
           || latest.TotalOrbs != stats.TotalOrbs;
}
```

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~AccountStatsRepositoryTests"`
Expected: PASS, 8 тестов.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.Core/Abstractions/IAccountStatsRepository.cs src/GdTracker.Data/Repositories/AccountStatsRepository.cs tests/GdTracker.Tests/AccountStatsRepositoryTests.cs
git commit -m "Репозиторий снимков статистики аккаунта"
```

---

### Task 5: Сохраняемые настройки (путь к сейву)

**Files:**
- Create: `src/GdTracker.Core/Abstractions/ISettingsService.cs`
- Create: `src/GdTracker.Data/SettingsService.cs`
- Test: `tests/GdTracker.Tests/SettingsServiceTests.cs`

**Interfaces:**
- Consumes: ничего.
- Produces: `ISettingsService` со свойством `string? SaveFilePath { get; }` и методом `void SetSaveFilePath(string? path)`; класс `SettingsService` с конструкторами `SettingsService()` (боевой, пишет в `%LOCALAPPDATA%\GdTracker\settings.json`) и `SettingsService(string filePath)` (для тестов).

- [ ] **Step 1: Написать падающие тесты**

Создать `tests/GdTracker.Tests/SettingsServiceTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.Data;

namespace GdTracker.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "gdtracker-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    [Fact]
    public void Missing_file_yields_null_path_and_does_not_throw()
    {
        var settings = new SettingsService(SettingsPath);

        settings.SaveFilePath.Should().BeNull();
    }

    [Fact]
    public void Path_survives_a_new_instance()
    {
        new SettingsService(SettingsPath).SetSaveFilePath(@"C:\games\CCGameManager.dat");

        new SettingsService(SettingsPath).SaveFilePath.Should().Be(@"C:\games\CCGameManager.dat");
    }

    [Fact]
    public void Path_can_be_cleared()
    {
        var settings = new SettingsService(SettingsPath);
        settings.SetSaveFilePath(@"C:\games\CCGameManager.dat");

        settings.SetSaveFilePath(null);

        new SettingsService(SettingsPath).SaveFilePath.Should().BeNull();
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults_instead_of_throwing()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(SettingsPath, "{ это не json");

        var settings = new SettingsService(SettingsPath);

        settings.SaveFilePath.Should().BeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~SettingsServiceTests"`
Expected: FAIL — ошибка компиляции, `SettingsService` не существует.

- [ ] **Step 3: Создать интерфейс**

Создать `src/GdTracker.Core/Abstractions/ISettingsService.cs`:

```csharp
namespace GdTracker.Core.Abstractions;

/// <summary>Настройки приложения, сохраняемые между запусками.</summary>
public interface ISettingsService
{
    /// <summary>
    /// Заданный пользователем путь к сейв-файлу GD, либо null, если не задан.
    /// Значение по умолчанию здесь не подставляется: автоопределение — забота
    /// <see cref="ISaveFileReader.DefaultSaveFilePath"/>, чтобы слой данных не знал про GameSync.
    /// </summary>
    string? SaveFilePath { get; }

    /// <summary>Задаёт (или очищает при null) путь к сейв-файлу и сразу сохраняет настройки.</summary>
    void SetSaveFilePath(string? path);
}
```

- [ ] **Step 4: Реализовать сервис**

Создать `src/GdTracker.Data/SettingsService.cs`:

```csharp
using System.Text.Json;
using GdTracker.Core.Abstractions;

namespace GdTracker.Data;

/// <inheritdoc />
public sealed class SettingsService : ISettingsService
{
    private readonly string _filePath;
    private AppSettings _settings;

    /// <summary>Боевой конструктор: settings.json рядом с БД приложения.</summary>
    public SettingsService() : this(Path.Combine(AppPaths.AppDataDir, "settings.json"))
    {
    }

    /// <summary>Конструктор с явным путём (используется тестами).</summary>
    public SettingsService(string filePath)
    {
        _filePath = filePath;
        _settings = Load(filePath);
    }

    public string? SaveFilePath => _settings.SaveFilePath;

    public void SetSaveFilePath(string? path)
    {
        _settings = _settings with { SaveFilePath = string.IsNullOrWhiteSpace(path) ? null : path };
        Save();
    }

    private static AppSettings Load(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return new AppSettings();

            return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(filePath)) ?? new AppSettings();
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Битые или недоступные настройки не должны мешать запуску приложения.
            return new AppSettings();
        }
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(_settings, JsonOptions));
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Содержимое settings.json.</summary>
    private sealed record AppSettings
    {
        public string? SaveFilePath { get; init; }
    }
}
```

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~SettingsServiceTests"`
Expected: PASS, 4 теста.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.Core/Abstractions/ISettingsService.cs src/GdTracker.Data/SettingsService.cs tests/GdTracker.Tests/SettingsServiceTests.cs
git commit -m "Сохраняемые настройки приложения"
```

---

### Task 6: Секция аккаунта в StatsViewModel

**Files:**
- Modify: `src/GdTracker.ViewModels/StatsViewModel.cs`
- Test: `tests/GdTracker.Tests/StatsViewModelTests.cs`

**Interfaces:**
- Consumes: `IAccountStatsRepository` (Task 4), `ISettingsService` (Task 5), `ISaveFileReader.ReadAccountStats` / `GetLastWriteTimeUtc` (Task 2), существующие `ILevelRepository` и `IFileDialogService`.
- Produces: конструктор `StatsViewModel(ILevelRepository, IAccountStatsRepository, ISaveFileReader, ISettingsService, IFileDialogService)`; свойства `AccountStars`, `AccountMoons`, `AccountDemons`, `AccountOnlineLevels`, `AccountOfficialLevels`, `AccountSecretCoins`, `AccountAttempts`, `AccountJumps`, `AccountTotalOrbs` (все `long`), `HasAccountStats` (bool), `AccountStatus` (string?), `AccountUpdatedAt` (string?), `IsAccountBusy` (bool); команды `RefreshAccountCommand`, `PickSaveFileCommand`.

- [ ] **Step 1: Написать падающие тесты**

Создать `tests/GdTracker.Tests/StatsViewModelTests.cs`:

```csharp
using FluentAssertions;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Data.Repositories;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>Фейковый ридер сейва: считает обращения и отдаёт заданную статистику.</summary>
internal sealed class FakeSaveReader : ISaveFileReader
{
    private readonly AccountStats? _stats;

    public FakeSaveReader(AccountStats? stats, DateTime? writtenAt = null)
    {
        _stats = stats;
        WrittenAt = writtenAt ?? new DateTime(2026, 7, 18, 10, 0, 0, DateTimeKind.Utc);
    }

    public DateTime? WrittenAt { get; set; }
    public int ReadCount { get; private set; }
    public bool ThrowOnRead { get; set; }
    public string? DefaultSaveFilePath { get; set; } = @"C:\fake\CCGameManager.dat";

    public IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath) => [];

    public AccountStats? ReadAccountStats(string saveFilePath)
    {
        ReadCount++;
        if (ThrowOnRead)
            throw new IOException("файл занят");
        return _stats;
    }

    public DateTime? GetLastWriteTimeUtc(string saveFilePath) => WrittenAt;
}

/// <summary>Настройки в памяти.</summary>
internal sealed class FakeSettings : ISettingsService
{
    public string? SaveFilePath { get; private set; }
    public void SetSaveFilePath(string? path) => SaveFilePath = path;
}

public class StatsViewModelTests
{
    private static AccountStats Stats(long stars = 886) => new()
    {
        Stars = stars,
        Moons = 84,
        Demons = 17,
        OnlineLevelsCompleted = 322,
        OfficialLevelsCompleted = 27,
        SecretCoins = 84,
        Attempts = 43329,
        Jumps = 258487,
        TotalOrbs = 49359,
    };

    private static StatsViewModel Build(
        InMemorySqlite factory, ISaveFileReader reader, ISettingsService? settings = null)
        => new(
            new LevelRepository(factory),
            new AccountStatsRepository(factory),
            reader,
            settings ?? new FakeSettings(),
            new NullFileDialog());

    [Fact]
    public async Task Shows_account_stats_when_levels_table_is_empty()
    {
        using var factory = new InMemorySqlite();
        var vm = Build(factory, new FakeSaveReader(Stats()));

        await vm.LoadAsync();

        // Главное требование: секция аккаунта наполнена, хотя уровней в БД нет.
        vm.TotalLevels.Should().Be(0);
        vm.HasAccountStats.Should().BeTrue();
        vm.AccountStars.Should().Be(886);
        vm.AccountMoons.Should().Be(84);
        vm.AccountDemons.Should().Be(17);
        vm.AccountOnlineLevels.Should().Be(322);
        vm.AccountOfficialLevels.Should().Be(27);
        vm.AccountSecretCoins.Should().Be(84);
        vm.AccountAttempts.Should().Be(43329);
        vm.AccountJumps.Should().Be(258487);
        vm.AccountTotalOrbs.Should().Be(49359);
    }

    [Fact]
    public async Task Skips_reading_when_save_file_has_not_changed()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats());
        var vm = Build(factory, reader);

        await vm.LoadAsync();
        await vm.LoadAsync();

        // Второй заход на вкладку не должен стоить секунды и всплеска памяти.
        reader.ReadCount.Should().Be(1);
    }

    [Fact]
    public async Task Reads_again_when_save_file_changed()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats());
        var vm = Build(factory, reader);

        await vm.LoadAsync();
        reader.WrittenAt = new DateTime(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);
        await vm.LoadAsync();

        reader.ReadCount.Should().Be(2);
    }

    [Fact]
    public async Task Refresh_command_reads_unconditionally()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats());
        var vm = Build(factory, reader);

        await vm.LoadAsync();
        await vm.RefreshAccountCommand.ExecuteAsync(null);

        reader.ReadCount.Should().Be(2);
    }

    [Fact]
    public async Task Keeps_last_snapshot_and_reports_error_when_read_fails()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats());
        var vm = Build(factory, reader);
        await vm.LoadAsync();

        reader.ThrowOnRead = true;
        reader.WrittenAt = new DateTime(2026, 7, 18, 13, 0, 0, DateTimeKind.Utc);
        await vm.LoadAsync();

        vm.AccountStars.Should().Be(886);            // снимок остался на экране
        vm.AccountStatus.Should().NotBeNullOrEmpty(); // и рядом объяснение
    }

    [Fact]
    public async Task Reports_missing_save_file_without_throwing()
    {
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats()) { DefaultSaveFilePath = null };
        var vm = Build(factory, reader);

        await vm.LoadAsync();

        vm.HasAccountStats.Should().BeFalse();
        vm.AccountStatus.Should().NotBeNullOrEmpty();
        reader.ReadCount.Should().Be(0);
    }

    [Fact]
    public async Task Reports_save_without_gs_value_block()
    {
        using var factory = new InMemorySqlite();
        var vm = Build(factory, new FakeSaveReader(stats: null));

        await vm.LoadAsync();

        vm.HasAccountStats.Should().BeFalse();
        vm.AccountStatus.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Configured_path_wins_over_autodetected_one()
    {
        using var factory = new InMemorySqlite();
        var settings = new FakeSettings();
        settings.SetSaveFilePath(@"D:\custom\CCGameManager.dat");
        var reader = new FakeSaveReader(Stats());
        var vm = Build(factory, reader, settings);

        await vm.LoadAsync();

        vm.HasAccountStats.Should().BeTrue();
        reader.ReadCount.Should().Be(1);
    }
}
```

- [ ] **Step 2: Запустить тесты и убедиться, что они падают**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: FAIL — ошибка компиляции, у `StatsViewModel` нет такого конструктора.

- [ ] **Step 3: Переписать StatsViewModel**

Заменить `src/GdTracker.ViewModels/StatsViewModel.cs` целиком:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace GdTracker.ViewModels;

/// <summary>
/// View-модель страницы статистики. Две независимые секции: счётчики аккаунта из сейва
/// (работают даже при пустой БД) и сводка по уровням трекера (работает без сейва).
/// </summary>
public partial class StatsViewModel : ViewModelBase
{
    private readonly ILevelRepository _levels;
    private readonly IAccountStatsRepository _accountStats;
    private readonly ISaveFileReader _saveReader;
    private readonly ISettingsService _settings;
    private readonly IFileDialogService _fileDialog;

    private DateTime? _loadedSaveFileWrittenAt;

    public StatsViewModel(
        ILevelRepository levels,
        IAccountStatsRepository accountStats,
        ISaveFileReader saveReader,
        ISettingsService settings,
        IFileDialogService fileDialog)
    {
        _levels = levels;
        _accountStats = accountStats;
        _saveReader = saveReader;
        _settings = settings;
        _fileDialog = fileDialog;
    }

    // --- Секция «Аккаунт» ---

    [ObservableProperty] private long _accountStars;
    [ObservableProperty] private long _accountMoons;
    [ObservableProperty] private long _accountDemons;
    [ObservableProperty] private long _accountOnlineLevels;
    [ObservableProperty] private long _accountOfficialLevels;
    [ObservableProperty] private long _accountSecretCoins;
    [ObservableProperty] private long _accountAttempts;
    [ObservableProperty] private long _accountJumps;
    [ObservableProperty] private long _accountTotalOrbs;

    [ObservableProperty] private bool _hasAccountStats;
    [ObservableProperty] private string? _accountStatus;
    [ObservableProperty] private string? _accountUpdatedAt;
    [ObservableProperty] private bool _isAccountBusy;

    /// <summary>Ошибка загрузки нижней секции. Отдельно от AccountStatus: иначе успешное
    /// чтение сейва затирало бы сообщение о сбое в трекерной секции.</summary>
    [ObservableProperty] private string? _trackerStatus;

    // --- Секция «Прогресс в трекере» ---

    [ObservableProperty] private int _totalLevels;
    [ObservableProperty] private int _completed;
    [ObservableProperty] private int _inProgress;
    [ObservableProperty] private int _untouched;
    [ObservableProperty] private int _totalAttempts;
    [ObservableProperty] private string _officialProgress = "0 / 0";

    [ObservableProperty] private ISeries[] _completionSeries = [];
    [ObservableProperty] private ISeries[] _bucketSeries = [];
    [ObservableProperty] private Axis[] _bucketXAxes = [];
    [ObservableProperty] private ISeries[] _topAttemptsSeries = [];
    [ObservableProperty] private Axis[] _topXAxes = [];

    /// <summary>
    /// Загружает обе секции. Исключения не выпускаются наружу: метод вызывается из
    /// обработчика Loaded страницы, что эквивалентно async void — необработанное
    /// исключение уронило бы приложение.
    /// </summary>
    public async Task LoadAsync()
    {
        try
        {
            await LoadTrackerAsync();
        }
        catch (Exception ex)
        {
            TrackerStatus = $"Не удалось загрузить статистику трекера: {ex.Message}";
        }

        await LoadAccountAsync(force: false);
    }

    private async Task LoadTrackerAsync()
    {
        var levels = await _levels.GetAllAsync();
        var stats = StatisticsCalculator.Compute(levels, topCount: 10);

        TotalLevels = stats.TotalLevels;
        Completed = stats.Completed;
        InProgress = stats.InProgress;
        Untouched = stats.Untouched;
        TotalAttempts = stats.TotalAttempts;
        OfficialProgress = $"{stats.OfficialCompleted} / {stats.OfficialTotal}";

        CompletionSeries =
        [
            new PieSeries<double> { Name = "Пройдено", Values = [stats.Completed] },
            new PieSeries<double> { Name = "В процессе", Values = [stats.InProgress] },
            new PieSeries<double> { Name = "Не начато", Values = [stats.Untouched] },
        ];

        BucketSeries =
        [
            new ColumnSeries<double>
            {
                Name = "Уровней",
                Values = stats.NormalPercentBuckets.Select(b => (double)b.Count).ToArray(),
            },
        ];
        BucketXAxes = [new Axis { Labels = stats.NormalPercentBuckets.Select(b => b.Label).ToArray() }];

        TopAttemptsSeries =
        [
            new ColumnSeries<double>
            {
                Name = "Попытки",
                Values = stats.TopByAttempts.Select(t => (double)t.Attempts).ToArray(),
            },
        ];
        TopXAxes =
        [
            new Axis
            {
                Labels = stats.TopByAttempts.Select(t => t.Name).ToArray(),
                LabelsRotation = 30,
            },
        ];
    }

    /// <summary>Перечитывает сейв безусловно, игнорируя проверку времени записи.</summary>
    [RelayCommand]
    private async Task RefreshAccountAsync() => await LoadAccountAsync(force: true);

    /// <summary>Выбор сейв-файла вручную; путь сохраняется между запусками.</summary>
    [RelayCommand]
    private async Task PickSaveFileAsync()
    {
        var picked = _fileDialog.PickOpenFile("Сейв Geometry Dash (*.dat)|*.dat|Все файлы (*.*)|*.*");
        if (picked is null)
            return;

        _settings.SetSaveFilePath(picked);
        await LoadAccountAsync(force: true);
    }

    private async Task LoadAccountAsync(bool force)
    {
        try
        {
            var latest = await _accountStats.GetLatestAsync();
            if (latest is not null)
                Apply(latest);

            var path = _settings.SaveFilePath ?? _saveReader.DefaultSaveFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AccountStatus = "Сейв-файл Geometry Dash не найден. Укажите файл CCGameManager.dat вручную.";
                return;
            }

            var writtenAt = _saveReader.GetLastWriteTimeUtc(path);
            if (writtenAt is null)
            {
                AccountStatus = $"Файл не найден: {path}";
                return;
            }

            // Страницы транзиентные, и Loaded срабатывает при каждом возврате на вкладку.
            // Без этой проверки переключение туда-обратно каждый раз стоило бы ~1,2 с
            // и всплеска памяти в сотни мегабайт на неизменившемся файле.
            if (!force && _loadedSaveFileWrittenAt == writtenAt)
                return;

            IsAccountBusy = true;
            try
            {
                var stats = await Task.Run(() => _saveReader.ReadAccountStats(path));
                if (stats is null)
                {
                    AccountStatus = "В сейв-файле нет блока статистики (GS_value).";
                    return;
                }

                var snapshot = await _accountStats.AddIfChangedAsync(stats, writtenAt);
                _loadedSaveFileWrittenAt = writtenAt;
                Apply(snapshot);
                AccountStatus = null;
            }
            finally
            {
                IsAccountBusy = false;
            }
        }
        catch (Exception ex)
        {
            // Последний снимок остаётся на экране: он всё ещё полезнее пустоты.
            AccountStatus = $"Не удалось прочитать сейв: {ex.Message}";
        }
    }

    private void Apply(AccountStatsSnapshot snapshot)
    {
        AccountStars = snapshot.Stars;
        AccountMoons = snapshot.Moons;
        AccountDemons = snapshot.Demons;
        AccountOnlineLevels = snapshot.OnlineLevelsCompleted;
        AccountOfficialLevels = snapshot.OfficialLevelsCompleted;
        AccountSecretCoins = snapshot.SecretCoins;
        AccountAttempts = snapshot.Attempts;
        AccountJumps = snapshot.Jumps;
        AccountTotalOrbs = snapshot.TotalOrbs;
        AccountUpdatedAt = $"данные на {snapshot.CapturedAt.ToLocalTime():dd.MM.yyyy HH:mm}";
        HasAccountStats = true;
    }
}
```

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test GdTracker.slnx --filter "FullyQualifiedName~StatsViewModelTests"`
Expected: PASS, 8 тестов.

- [ ] **Step 5: Прогнать весь набор**

Run: `dotnet test GdTracker.slnx`
Expected: все зелёные. Если падает `LevelsWorkflowTests` — значит там конструируется `StatsViewModel`; поправить вызов под новую сигнатуру.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.ViewModels/StatsViewModel.cs tests/GdTracker.Tests/StatsViewModelTests.cs
git commit -m "Секция статистики аккаунта в StatsViewModel"
```

---

### Task 7: Секция аккаунта на странице и регистрация в DI

**Files:**
- Modify: `src/GdTracker.App/Views/StatsPage.xaml`
- Modify: `src/GdTracker.App/App.xaml.cs:34-40`

**Interfaces:**
- Consumes: свойства и команды `StatsViewModel` из Task 6, `AccountStatsRepository` (Task 4), `SettingsService` (Task 5).
- Produces: рабочий экран.

- [ ] **Step 1: Зарегистрировать сервисы в DI**

В `src/GdTracker.App/App.xaml.cs` в блоке репозиториев, после строки `services.AddSingleton<IProgressRepository, ProgressRepository>();`, добавить:

```csharp
                services.AddSingleton<IAccountStatsRepository, AccountStatsRepository>();
                services.AddSingleton<ISettingsService, SettingsService>();
```

- [ ] **Step 2: Добавить секцию аккаунта в разметку**

В `src/GdTracker.App/Views/StatsPage.xaml` вставить между заголовком «Статистика» (строка 18) и комментарием `<!-- Сводные карточки -->`:

```xml
            <!-- Аккаунт: данные из сейв-файла игры, не зависят от таблицы уровней -->
            <TextBlock Text="Аккаунт" FontSize="18" FontWeight="SemiBold" Margin="0,0,0,8" />

            <StackPanel Orientation="Horizontal" Margin="0,0,0,8">
                <Button Content="Обновить" Command="{Binding RefreshAccountCommand}" Margin="0,0,8,0" />
                <Button Content="Указать файл" Command="{Binding PickSaveFileCommand}" Margin="0,0,8,0" />
                <TextBlock Text="{Binding AccountUpdatedAt}" Opacity="0.6" VerticalAlignment="Center" />
            </StackPanel>

            <TextBlock Text="{Binding AccountStatus}" Foreground="#FFC107" TextWrapping="Wrap"
                       Margin="0,0,0,8">
                <TextBlock.Style>
                    <Style TargetType="TextBlock">
                        <Setter Property="Visibility" Value="Visible" />
                        <Style.Triggers>
                            <DataTrigger Binding="{Binding AccountStatus}" Value="{x:Null}">
                                <Setter Property="Visibility" Value="Collapsed" />
                            </DataTrigger>
                        </Style.Triggers>
                    </Style>
                </TextBlock.Style>
            </TextBlock>

            <WrapPanel>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountStars}" FontSize="26" FontWeight="Bold" Foreground="#FFD54F" />
                        <TextBlock Text="Звёзды" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountMoons}" FontSize="26" FontWeight="Bold" Foreground="#B39DDB" />
                        <TextBlock Text="Луны" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountDemons}" FontSize="26" FontWeight="Bold" Foreground="#EF5350" />
                        <TextBlock Text="Демоны" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountOnlineLevels}" FontSize="26" FontWeight="Bold" />
                        <TextBlock Text="Онлайн-уровни" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountOfficialLevels}" FontSize="26" FontWeight="Bold" />
                        <TextBlock Text="Официальные уровни" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountSecretCoins}" FontSize="26" FontWeight="Bold" Foreground="#FFB300" />
                        <TextBlock Text="Секретные монеты" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountAttempts}" FontSize="26" FontWeight="Bold" />
                        <TextBlock Text="Попытки" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountJumps}" FontSize="26" FontWeight="Bold" />
                        <TextBlock Text="Прыжки" Opacity="0.7" />
                    </StackPanel>
                </Border>
                <Border Style="{StaticResource StatCard}">
                    <StackPanel>
                        <TextBlock Text="{Binding AccountTotalOrbs}" FontSize="26" FontWeight="Bold" Foreground="#4DD0E1" />
                        <TextBlock Text="Сферы (всего)" Opacity="0.7" />
                    </StackPanel>
                </Border>
            </WrapPanel>

            <!-- Прогресс в трекере: данные из БД приложения -->
            <TextBlock Text="Прогресс в трекере" FontSize="18" FontWeight="SemiBold" Margin="0,16,0,8" />
```

- [ ] **Step 3: Собрать и запустить приложение**

```bash
dotnet build GdTracker.slnx
dotnet run --project src/GdTracker.App
```

Expected: приложение запускается, вкладка «Статистика» показывает секцию «Аккаунт» с девятью карточками. Учесть, что данные появляются не мгновенно: страница сперва рисует снимок из БД (при первом запуске его нет), затем фоном читает сейв — на файле в 55 МБ это около секунды.

- [ ] **Step 4: Проверить сверкой с реальными данными**

Сверить показанные числа с блоком `GS_value` реального сейва. На машине разработки ожидаются: звёзды 886, луны 84, демоны 17, онлайн-уровни 322, официальные 27, секретные монеты 84, попытки 43329, прыжки 258487, сферы 49359.

Обязательные проверки на подмену соседними ключами:
- секретные монеты должны быть **84**, а не 82 (82 — пользовательские монеты, ключ 12);
- сферы должны быть **49359**, а не 10682 (10682 — текущий баланс, ключ 14).

- [ ] **Step 5: Проверить главный сценарий — пустая БД**

Переименовать `%LOCALAPPDATA%\GdTracker\gdtracker.db` во что-нибудь другое, запустить приложение заново и открыть «Статистика».
Expected: секция «Аккаунт» заполнена, секция «Прогресс в трекере» в нулях, приложение не падает. После проверки вернуть файл БД на место.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.App/Views/StatsPage.xaml src/GdTracker.App/App.xaml.cs
git commit -m "Секция статистики аккаунта на странице статистики"
```

---

### Task 8: Путь к сейву — общий и сохраняемый

**Files:**
- Modify: `src/GdTracker.ViewModels/LevelsViewModel.cs:26-41`
- Modify: `src/GdTracker.ViewModels/SettingsViewModel.cs`
- Modify: `src/GdTracker.App/Views/SettingsPage.xaml`
- Test: `tests/GdTracker.Tests/LevelsWorkflowTests.cs` (правка конструктора)

**Interfaces:**
- Consumes: `ISettingsService` (Task 5).
- Produces: путь к сейву, переживающий перезапуск, общий для вкладок «Уровни» и «Статистика».

- [ ] **Step 1: Перевести LevelsViewModel на настройки**

В `src/GdTracker.ViewModels/LevelsViewModel.cs` добавить поле после `_fileDialog`:

```csharp
    private readonly ISettingsService _settings;
```

Добавить параметр `ISettingsService settings` в конструктор (последним) и заменить строку инициализации пути:

```csharp
        _settings = settings;
        _saveFilePath = settings.SaveFilePath ?? saveReader.DefaultSaveFilePath ?? string.Empty;
```

Затем сделать так, чтобы изменение пути сохранялось. Добавить в класс:

```csharp
    /// <summary>Сохраняет изменённый пользователем путь, чтобы он пережил перезапуск.</summary>
    partial void OnSaveFilePathChanged(string value)
        => _settings.SetSaveFilePath(string.IsNullOrWhiteSpace(value) ? null : value);
```

- [ ] **Step 2: Зарегистрировать зависимость и починить тесты**

В `tests/GdTracker.Tests/LevelsWorkflowTests.cs` найти конструирование `LevelsViewModel` и добавить последним аргументом `new FakeSettings()` (тип уже создан в Task 6).

Run: `dotnet test GdTracker.slnx`
Expected: все зелёные.

- [ ] **Step 3: Показать путь на странице настроек**

Заменить `src/GdTracker.ViewModels/SettingsViewModel.cs`:

```csharp
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Abstractions;

namespace GdTracker.ViewModels;

/// <summary>View-модель страницы настроек: версия приложения и путь к сейв-файлу GD.</summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly ISaveFileReader _saveReader;

    public SettingsViewModel(ISettingsService settings, ISaveFileReader saveReader)
    {
        _settings = settings;
        _saveReader = saveReader;
        _saveFilePath = settings.SaveFilePath ?? saveReader.DefaultSaveFilePath ?? string.Empty;
    }

    [ObservableProperty]
    private string _appVersion =
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";

    [ObservableProperty] private string _saveFilePath;

    partial void OnSaveFilePathChanged(string value)
        => _settings.SetSaveFilePath(string.IsNullOrWhiteSpace(value) ? null : value);
}
```

В `src/GdTracker.App/Views/SettingsPage.xaml` заменить строку-заглушку про Фазу 2 на:

```xml
            <TextBlock Margin="0,16,0,4" Text="Путь к сейв-файлу Geometry Dash" />
            <TextBox Text="{Binding SaveFilePath, UpdateSourceTrigger=LostFocus}" />
            <TextBlock Margin="0,4,0,0" Opacity="0.6" TextWrapping="Wrap"
                       Text="Используется вкладками «Уровни» и «Статистика». Сохраняется между запусками." />
```

- [ ] **Step 4: Проверить в приложении**

```bash
dotnet run --project src/GdTracker.App
```

Открыть «Настройки», убедиться, что путь показан. Изменить его на заведомо неверный, перезапустить приложение — путь должен сохраниться, а вкладка «Статистика» показать сообщение с кнопкой «Указать файл». Вернуть верный путь через кнопку и убедиться, что цифры вернулись.

- [ ] **Step 5: Прогнать весь набор тестов**

Run: `dotnet test GdTracker.slnx`
Expected: все зелёные.

- [ ] **Step 6: Коммит**

```bash
git add src/GdTracker.ViewModels/LevelsViewModel.cs src/GdTracker.ViewModels/SettingsViewModel.cs src/GdTracker.App/Views/SettingsPage.xaml tests/GdTracker.Tests/LevelsWorkflowTests.cs
git commit -m "Путь к сейву — общая сохраняемая настройка"
```

---

## Замечания по исполнению

**Незакоммиченная работа в репозитории.** На момент написания плана в рабочей копии лежит незавершённая фича онлайн-поиска уровней (30 изменённых файлов под `src/` и `tests/`). Коммиты по шагам плана должны добавлять **только** перечисленные в них файлы — никаких `git add -A` и `git commit -a`.

**Отклонение от спеки.** Спека предполагала новый метод в `IFileDialogService` для выбора `.dat`. Он не нужен: существующий `PickOpenFile(string filter)` подходит, достаточно передать фильтр. План использует существующий метод.

**Замеры для контекста** (реальный сейв 55,8 МБ): чтение с диска 43 мс, декод 714 мс, полный разбор уровней 389 мс, вырезание и разбор `GS_value` 8 мс, пик памяти 604 МБ. Полный разбор уровней в этой фиче не выполняется.
