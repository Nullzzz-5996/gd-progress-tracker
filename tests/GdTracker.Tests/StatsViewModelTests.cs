using System.Threading;
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

    /// <summary>Если задан, ReadAccountStats блокируется на нём после увеличения ReadCount —
    /// позволяет тесту гарантированно свести во времени два параллельных вызова команды.</summary>
    public ManualResetEventSlim? ReadGate { get; set; }

    public IReadOnlyList<SaveLevelDto> ReadLevels(string saveFilePath) => [];

    public AccountStats? ReadAccountStats(string saveFilePath)
    {
        ReadCount++;
        ReadGate?.Wait();
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

    [Fact]
    public async Task Second_refresh_click_is_blocked_while_first_is_still_reading()
    {
        // ВАЖНО: AsyncRelayCommand.ExecuteAsync/Execute всегда запускают делегат заново,
        // какими бы ни были AsyncRelayCommandOptions, — это подтверждено исходниками
        // CommunityToolkit.Mvvm 8.4.2 (AsyncRelayCommand.cs, ExecuteAsync ничего не проверяет
        // перед вызовом execute()). AllowConcurrentExecutions = false влияет только на
        // CanExecute(): пока команда выполняется, CanExecute возвращает false, и именно на
        // это опирается настоящая кнопка «Обновить» в приложении — её ICommand-биндинг
        // перед вызовом Execute всегда проверяет CanExecute и сам отключает кнопку по
        // CanExecuteChanged. Поэтому здесь мы эмулируем ровно то, что делает реальная
        // кнопка: проверяем CanExecute и не вызываем Execute повторно, если он вернул false.
        using var factory = new InMemorySqlite();
        var reader = new FakeSaveReader(Stats());
        using var gate = new ManualResetEventSlim(initialState: false);
        reader.ReadGate = gate;
        var vm = Build(factory, reader);
        var command = vm.RefreshAccountCommand;

        command.CanExecute(null).Should().BeTrue();

        // Первый клик «Обновить» уходит в чтение сейва и зависает на шлюзе...
        var first = command.ExecuteAsync(null);
        SpinWait.SpinUntil(() => reader.ReadCount > 0, TimeSpan.FromSeconds(5))
            .Should().BeTrue("первое чтение должно было начаться");

        // ...и пока оно не завершилось, повторный клик должен быть заблокирован на уровне
        // команды: кнопка в реальном приложении в этот момент уже отключена.
        command.CanExecute(null).Should().BeFalse("во время чтения повторный клик должен игнорироваться");
        reader.ReadCount.Should().Be(1);

        gate.Set();
        await first;

        // После завершения — второго чтения не случилось, а команда снова доступна.
        reader.ReadCount.Should().Be(1);
        command.CanExecute(null).Should().BeTrue();
    }
}
