using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Models;
using GdTracker.Core.Services;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace GdTracker.ViewModels;

/// <summary>
/// View-модель страницы статистики. Две независимые секции: счётчики аккаунта из сейва
/// (работают даже при пустой БД) и сводка по уровням трекера (работает без сейва).
/// </summary>
public partial class StatsViewModel : ViewModelBase, IDisposable
{
    private readonly ILevelRepository _levels;
    private readonly IAccountStatsRepository _accountStats;
    private readonly ISaveFileReader _saveReader;
    private readonly ISettingsService _settings;
    private readonly IFileDialogService _fileDialog;
    private readonly IChartPalette _palette;

    private DateTime? _loadedSaveFileWrittenAt;

    /// <summary>
    /// Последние загруженные снимки динамики — нужны, чтобы перестроить серии графика
    /// с новыми цветами палитры (см. <see cref="OnPaletteChanged"/>), не перечитывая
    /// историю из БД заново.
    /// </summary>
    private IReadOnlyList<AccountStatsSnapshot>? _trendHistory;

    /// <summary>
    /// Сериализует само чтение сейва: обработчик Loaded страницы и команда «Обновить» —
    /// разные пути вызова, поэтому AllowConcurrentExecutions на команде их друг от друга
    /// не защищает. Без этого лока два почти одновременных чтения удваивают пиковую
    /// память (~1,2 ГБ) и рискуют дважды вставить снимок в архив на пустой таблице.
    /// </summary>
    private readonly SemaphoreSlim _accountLoadLock = new(1, 1);

    public StatsViewModel(
        ILevelRepository levels,
        IAccountStatsRepository accountStats,
        ISaveFileReader saveReader,
        ISettingsService settings,
        IFileDialogService fileDialog,
        IChartPalette palette)
    {
        _levels = levels;
        _accountStats = accountStats;
        _saveReader = saveReader;
        _settings = settings;
        _fileDialog = fileDialog;
        _palette = palette;
        _palette.Changed += OnPaletteChanged;
    }

    /// <summary>
    /// Вью-модели транзиентные и создаются заново при каждом заходе на вкладку статистики,
    /// а палитра — общий на всё приложение singleton. Без отписки здесь каждый визит на
    /// вкладку добавлял бы ещё одного мёртвого подписчика на <see cref="IChartPalette.Changed"/>
    /// (утечка памяти + лишние перестроения графика от давно закрытых страниц).
    /// </summary>
    public void Dispose() => _palette.Changed -= OnPaletteChanged;

    /// <summary>Перестраивает график динамики с новыми цветами палитры без обращения к БД —
    /// история снимков уже загружена, меняются только цвета серий и осей.</summary>
    private void OnPaletteChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(ChartLegendPaint));

        if (_trendHistory is not null)
            BuildTrendSeries(_trendHistory);
    }

    /// <summary>
    /// Кисть подписей легенды графиков. Легенда рисуется Skia отдельно от осей и по
    /// умолчанию чёрная, поэтому в тёмной и неоновой темах сливалась с фоном; здесь она
    /// берёт цвет подписей осей из палитры темы.
    /// </summary>
    public SolidColorPaint ChartLegendPaint => new(_palette.AxisLabelColor);

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

    [ObservableProperty] private ISeries[] _trendSeries = [];
    [ObservableProperty] private Axis[] _trendXAxes = [];
    [ObservableProperty] private Axis[] _trendYAxes = [];

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
        BucketXAxes =
        [
            new Axis
            {
                Labels = stats.NormalPercentBuckets.Select(b => b.Label).ToArray(),
                LabelsPaint = new SolidColorPaint(_palette.AxisLabelColor),
                SeparatorsPaint = new SolidColorPaint(_palette.AxisLineColor),
            },
        ];

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
                LabelsPaint = new SolidColorPaint(_palette.AxisLabelColor),
                SeparatorsPaint = new SolidColorPaint(_palette.AxisLineColor),
            },
        ];
    }

    /// <summary>
    /// Загружает накопленные снимки динамики и строит по ним график. Снимки кешируются
    /// в <see cref="_trendHistory"/>, чтобы смена палитры (см. <see cref="OnPaletteChanged"/>)
    /// могла перестроить те же серии заново, не читая историю из БД повторно.
    /// </summary>
    private async Task LoadTrendAsync()
    {
        _trendHistory = await _accountStats.GetHistoryAsync();
        BuildTrendSeries(_trendHistory);
    }

    /// <summary>
    /// Строит график динамики по накопленным снимкам. Звёзды и демоны различаются
    /// в десятки раз, поэтому демоны идут по отдельной правой оси (ScalesYAt = 1),
    /// иначе их кривая выродилась бы в прямую по нулю. Цвета серий и осей берутся
    /// из <see cref="_palette"/>, а не зашиты, — чтобы график следовал теме оформления.
    /// </summary>
    private void BuildTrendSeries(IReadOnlyList<AccountStatsSnapshot> history)
    {
        TrendSeries =
        [
            new LineSeries<double>
            {
                Name = "Звёзды",
                Values = history.Select(h => (double)h.Stars).ToArray(),
                Stroke = new SolidColorPaint(_palette.StarsLineColor, 2),
                GeometryStroke = new SolidColorPaint(_palette.StarsLineColor, 2),
                Fill = null,
            },
            new LineSeries<double>
            {
                Name = "Демоны",
                Values = history.Select(h => (double)h.Demons).ToArray(),
                Stroke = new SolidColorPaint(_palette.DemonsLineColor, 2),
                GeometryStroke = new SolidColorPaint(_palette.DemonsLineColor, 2),
                Fill = null,
                ScalesYAt = 1,
            },
        ];

        TrendXAxes =
        [
            new Axis
            {
                Labels = history.Select(h => h.CapturedAt.ToLocalTime().ToString("dd.MM")).ToArray(),
                LabelsPaint = new SolidColorPaint(_palette.AxisLabelColor),
                SeparatorsPaint = new SolidColorPaint(_palette.AxisLineColor),
            },
        ];
        TrendYAxes =
        [
            new Axis
            {
                Name = "Звёзды",
                NamePaint = new SolidColorPaint(_palette.AxisLabelColor),
                LabelsPaint = new SolidColorPaint(_palette.AxisLabelColor),
                SeparatorsPaint = new SolidColorPaint(_palette.AxisLineColor),
            },
            new Axis
            {
                Name = "Демоны",
                Position = AxisPosition.End,
                NamePaint = new SolidColorPaint(_palette.AxisLabelColor),
                LabelsPaint = new SolidColorPaint(_palette.AxisLabelColor),
                SeparatorsPaint = new SolidColorPaint(_palette.AxisLineColor),
            },
        ];
    }

    /// <summary>Перечитывает сейв безусловно, игнорируя проверку времени записи.</summary>
    /// <remarks>
    /// Чтение сейва — операция на секунду и сотни мегабайт памяти, поэтому повторный клик
    /// по «Обновить» во время выполнения не должен запускать второе параллельное чтение.
    /// </remarks>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task RefreshAccountAsync() => await LoadAccountAsync(force: true);

    /// <summary>Выбор сейв-файла вручную; путь сохраняется между запусками.</summary>
    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task PickSaveFileAsync()
    {
        try
        {
            var picked = _fileDialog.PickOpenFile("Сейв Geometry Dash (*.dat)|*.dat|Все файлы (*.*)|*.*");
            if (picked is null)
                return;

            _settings.SetSaveFilePath(picked);
            await LoadAccountAsync(force: true);
        }
        catch (Exception ex)
        {
            AccountStatus = $"Не удалось выбрать сейв-файл: {ex.Message}";
        }
    }

    private async Task LoadAccountAsync(bool force)
    {
        try
        {
            var latest = await _accountStats.GetLatestAsync();
            if (latest is not null)
                Apply(latest);

            // Вью-модели транзиентные и создаются заново при каждом заходе на вкладку,
            // поэтому маркер прочитанного времени записи файла нужно засеять из последнего
            // снимка БД, а не полагаться на поле экземпляра (оно всегда null на новой вью-модели).
            // "??=" не перетирает значение, как только оно выставлено настоящим чтением ниже.
            _loadedSaveFileWrittenAt ??= latest?.SaveFileWrittenAt;

            var path = _settings.SaveFilePath ?? _saveReader.DefaultSaveFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                AccountStatus = "Сейв-файл Geometry Dash не найден. Укажите файл CCGameManager.dat вручную.";
                // Путь не задан, но снимки от прошлых запусков (или уже прочитанные командой
                // "Выбрать файл" ранее) могли остаться в БД — график должен их показать.
                await LoadTrendAsync();
                return;
            }

            var writtenAt = _saveReader.GetLastWriteTimeUtc(path);
            if (writtenAt is null)
            {
                AccountStatus = $"Файл не найден: {path}";
                // Сейв сейчас недоступен, но накопленная история снимков остаётся полезной.
                await LoadTrendAsync();
                return;
            }

            // Страницы транзиентные, и Loaded срабатывает при каждом возврате на вкладку.
            // Без этой проверки переключение туда-обратно каждый раз стоило бы ~1,2 с
            // и всплеска памяти в сотни мегабайт на неизменившемся файле.
            if (!force && _loadedSaveFileWrittenAt == writtenAt)
            {
                // Успешный пропуск чтения — тоже успешная ветка: висящее предупреждение
                // с прошлой (неудачной) попытки не должно оставаться поверх корректных данных.
                AccountStatus = null;
                // Новый снимок мог появиться с прошлого построения графика этой вкладки
                // (например, его добавил параллельный "Обновить"), поэтому график всё
                // равно перестраивается из БД, даже когда само чтение сейва пропущено.
                await LoadTrendAsync();
                return;
            }

            await _accountLoadLock.WaitAsync();
            try
            {
                // Повторная проверка после входа в критическую секцию: пока мы ждали лок,
                // конкурентное чтение (например, Loaded и «Обновить» почти одновременно)
                // уже могло прочитать сейв и обновить _loadedSaveFileWrittenAt этим значением.
                if (!force && _loadedSaveFileWrittenAt == writtenAt)
                {
                    AccountStatus = null;
                    await LoadTrendAsync();
                    return;
                }

                IsAccountBusy = true;
                try
                {
                    var stats = await Task.Run(() => _saveReader.ReadAccountStats(path));
                    if (stats is null)
                    {
                        // Parse возвращает null и когда блока GS_value нет вовсе, и когда он
                        // есть, но повреждён/оборван — различить это на уровне вью-модели
                        // нельзя без изменения контракта ISaveFileReader, поэтому сообщение
                        // честно описывает оба случая.
                        AccountStatus = "Не удалось получить статистику из сейва: блок GS_value отсутствует или повреждён.";
                        await LoadTrendAsync();
                        return;
                    }

                    var readAt = DateTime.UtcNow;
                    var snapshot = await _accountStats.AddIfChangedAsync(stats, writtenAt);
                    _loadedSaveFileWrittenAt = writtenAt;
                    Apply(snapshot, readAt);
                    AccountStatus = null;
                    await LoadTrendAsync();
                }
                finally
                {
                    IsAccountBusy = false;
                }
            }
            finally
            {
                _accountLoadLock.Release();
            }
        }
        catch (Exception ex)
        {
            // Последний снимок остаётся на экране: он всё ещё полезнее пустоты.
            AccountStatus = $"Не удалось прочитать сейв: {ex.Message}";
        }
    }

    /// <param name="snapshot">Снимок, которым наполняются карточки.</param>
    /// <param name="readAt">
    /// Момент последнего успешного чтения сейва, если оно произошло в этом вызове.
    /// Нужен отдельно от <see cref="AccountStatsSnapshot.CapturedAt"/>: при дедупликации
    /// репозиторий возвращает существующую строку с датой её первого создания, и подпись
    /// «данные на …» иначе показывала бы дату месячной давности сразу после успешного
    /// «Обновить», из-за чего кнопка выглядела бы сломанной.
    /// </param>
    private void Apply(AccountStatsSnapshot snapshot, DateTime? readAt = null)
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

        var asOf = readAt ?? snapshot.CapturedAt;
        AccountUpdatedAt = $"данные на {asOf.ToLocalTime():dd.MM.yyyy HH:mm}";
        HasAccountStats = true;
    }
}
