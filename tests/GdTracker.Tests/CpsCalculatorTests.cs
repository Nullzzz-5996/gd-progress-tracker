using FluentAssertions;
using GdTracker.Core.Services;

namespace GdTracker.Tests;

/// <summary>Юнит-тесты чистого калькулятора метрик CPS.</summary>
public class CpsCalculatorTests
{
    // Равномерно: нажатие каждые 100 мс в течение 12 с (120 нажатий) → 10/с.
    private static long[] Uniform() => Enumerable.Range(0, 120).Select(i => (long)i * 100).ToArray();

    [Fact]
    public void Uniform_rate_gives_expected_max_and_best10()
    {
        var r = CpsCalculator.Compute(Uniform(), 12000);
        r.TotalClicks.Should().Be(120);
        r.MaxCps.Should().Be(10);
        r.Best10sCps.Should().Be(10.0);
    }

    [Fact]
    public void Burst_has_high_max_but_low_best10()
    {
        // 20 нажатий за 200 мс, заход 12 с.
        var t = Enumerable.Range(0, 20).Select(i => (long)i * 10).ToArray();
        var r = CpsCalculator.Compute(t, 12000);
        r.MaxCps.Should().Be(20);
        r.Best10sCps.Should().Be(2.0); // 20 нажатий / 10 с
    }

    [Fact]
    public void Run_shorter_than_10s_has_null_best10()
    {
        var r = CpsCalculator.Compute(new long[] { 0, 500, 1000, 1500, 2000 }, 5000);
        r.Best10sCps.Should().BeNull();
        r.MaxCps.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Empty_input_is_zero()
    {
        var r = CpsCalculator.Compute(Array.Empty<long>(), 15000);
        r.MaxCps.Should().Be(0);
        r.Best10sCps.Should().Be(0.0); // ≥10 с, но нажатий нет
        r.TotalClicks.Should().Be(0);
    }

    [Fact]
    public void CurrentCps_counts_presses_in_last_second()
    {
        var t = Enumerable.Range(0, 10).Select(i => (long)i * 100).ToArray(); // 0..900
        CpsCalculator.CurrentCps(t, 900).Should().Be(10);   // окно (-100, 900]
        CpsCalculator.CurrentCps(t, 1500).Should().Be(4);   // окно (500, 1500]: 600,700,800,900
        CpsCalculator.CurrentCps(t, 5000).Should().Be(0);
    }
}
