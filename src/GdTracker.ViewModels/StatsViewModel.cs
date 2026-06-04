using CommunityToolkit.Mvvm.ComponentModel;
using GdTracker.Core.Abstractions;
using GdTracker.Core.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;

namespace GdTracker.ViewModels;

/// <summary>View-модель страницы статистики: сводка + графики (LiveCharts2).</summary>
public partial class StatsViewModel : ViewModelBase
{
    private readonly ILevelRepository _levels;

    public StatsViewModel(ILevelRepository levels) => _levels = levels;

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

    public async Task LoadAsync()
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
}
