using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GdTracker.Core.Models;
using GdTracker.Core.Services;

namespace GdTracker.ViewModels;

/// <summary>Вкладка «CPS»: замер скорости кликов с живым показом и итогом.</summary>
public partial class CpsViewModel : ViewModelBase
{
    private readonly IGlobalInputMonitor _monitor;
    private readonly Func<long> _nowMs;
    private readonly List<long> _presses = new();
    private long _startMs;

    public CpsViewModel(IGlobalInputMonitor monitor)
        : this(monitor, () => Environment.TickCount64) { }

    // Второй параметр не резолвится контейнером DI, поэтому в приложении выбирается
    // одноаргументный конструктор с часами по умолчанию; тесты используют этот.
    public CpsViewModel(IGlobalInputMonitor monitor, Func<long> nowMs)
    {
        _monitor = monitor;
        _nowMs = nowMs;
        _monitor.Pressed += OnPressed;
        _monitor.StopRequested += OnStopRequested;
    }

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private int _currentCps;
    [ObservableProperty] private int _maxCps;
    [ObservableProperty] private int _totalClicks;
    [ObservableProperty] private string _elapsed = "00:00";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private CpsResult? _result;

    public bool HasResult => Result is not null;

    [RelayCommand]
    private void Start()
    {
        _presses.Clear();
        Result = null;
        CurrentCps = 0;
        MaxCps = 0;
        TotalClicks = 0;
        Elapsed = "00:00";
        _startMs = _nowMs();
        IsRunning = true;
        _monitor.Start();
    }

    [RelayCommand]
    private void Stop()
    {
        if (!IsRunning) return;
        _monitor.Stop();
        IsRunning = false;

        long duration = _nowMs() - _startMs;
        var result = CpsCalculator.Compute(_presses, duration);
        MaxCps = result.MaxCps;
        TotalClicks = result.TotalClicks;
        CurrentCps = 0;
        Elapsed = Format(duration);
        Result = result;
    }

    /// <summary>Пересчёт живых метрик (зовётся таймером страницы во время замера).</summary>
    public void RefreshLive()
    {
        if (!IsRunning) return;
        long now = _nowMs() - _startMs;
        CurrentCps = CpsCalculator.CurrentCps(_presses, now);
        MaxCps = CpsCalculator.Compute(_presses, now).MaxCps;
        TotalClicks = _presses.Count;
        Elapsed = Format(now);
    }

    private void OnPressed()
    {
        if (IsRunning)
            _presses.Add(_nowMs() - _startMs);
    }

    private void OnStopRequested()
    {
        if (IsRunning)
            Stop();
    }

    private static string Format(long ms)
    {
        var ts = TimeSpan.FromMilliseconds(ms);
        return $"{(int)ts.TotalMinutes:00}:{ts.Seconds:00}";
    }
}
