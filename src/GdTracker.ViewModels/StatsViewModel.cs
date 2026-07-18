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
