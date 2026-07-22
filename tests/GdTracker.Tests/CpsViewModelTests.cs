using FluentAssertions;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>Тесты CpsViewModel с фейковым монитором и управляемыми часами.</summary>
public class CpsViewModelTests
{
    private sealed class FakeMonitor : IGlobalInputMonitor
    {
        public event Action? Pressed;
        public event Action? StopRequested;
        public int StartCount { get; private set; }
        public int StopCount { get; private set; }
        public void Start() => StartCount++;
        public void Stop() => StopCount++;
        public void RaisePressed() => Pressed?.Invoke();
        public void RaiseStop() => StopRequested?.Invoke();
    }

    [Fact]
    public void Start_sets_running_and_starts_monitor()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);

        vm.StartCommand.Execute(null);

        vm.IsRunning.Should().BeTrue();
        mon.StartCount.Should().Be(1);
        vm.Result.Should().BeNull();
        vm.HasResult.Should().BeFalse();
    }

    [Fact]
    public void Presses_counted_and_current_cps_reflects_last_second()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);
        vm.StartCommand.Execute(null);

        for (int i = 0; i < 5; i++) { now = i * 100; mon.RaisePressed(); } // 0..400
        now = 500; vm.RefreshLive();
        vm.CurrentCps.Should().Be(5);
        vm.TotalClicks.Should().Be(5);

        now = 2000; vm.RefreshLive();
        vm.CurrentCps.Should().Be(0);
    }

    [Fact]
    public void Stop_computes_result_and_stops_monitor()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);
        vm.StartCommand.Execute(null);

        for (int i = 0; i < 120; i++) { now = i * 100; mon.RaisePressed(); } // 10/с, 12 с
        now = 12000;
        vm.StopCommand.Execute(null);

        vm.IsRunning.Should().BeFalse();
        mon.StopCount.Should().Be(1);
        vm.HasResult.Should().BeTrue();
        vm.Result!.TotalClicks.Should().Be(120);
        vm.Result.MaxCps.Should().Be(10);
        vm.Result.Best10sCps.Should().Be(10.0);
    }

    [Fact]
    public void Escape_stop_request_stops_when_running()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);
        vm.StartCommand.Execute(null);

        now = 5000;
        mon.RaiseStop();

        vm.IsRunning.Should().BeFalse();
        vm.HasResult.Should().BeTrue();
        mon.StopCount.Should().Be(1);
    }

    [Fact]
    public void Restart_clears_previous_result()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);
        vm.StartCommand.Execute(null);
        now = 3000; mon.RaisePressed();
        vm.StopCommand.Execute(null);
        vm.HasResult.Should().BeTrue();

        vm.StartCommand.Execute(null);

        vm.HasResult.Should().BeFalse();
        vm.Result.Should().BeNull();
        vm.TotalClicks.Should().Be(0);
        vm.CurrentCps.Should().Be(0);
    }

    [Fact]
    public void Presses_before_start_are_ignored()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);

        mon.RaisePressed(); // до старта — игнор
        vm.StartCommand.Execute(null);
        now = 100; mon.RaisePressed();
        now = 200; vm.RefreshLive();

        vm.TotalClicks.Should().Be(1);
    }

    [Fact]
    public void Dispose_stops_monitor_when_running()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);
        vm.StartCommand.Execute(null);
        now = 100; mon.RaisePressed();

        vm.Dispose();

        mon.StopCount.Should().Be(1);
        vm.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Dispose_when_idle_does_not_stop_monitor()
    {
        var mon = new FakeMonitor();
        long now = 0;
        var vm = new CpsViewModel(mon, () => now);

        var act = () => vm.Dispose();

        act.Should().NotThrow();
        mon.StopCount.Should().Be(0);
    }
}
