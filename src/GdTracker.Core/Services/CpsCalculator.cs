using GdTracker.Core.Models;

namespace GdTracker.Core.Services;

/// <summary>
/// Чистые вычисления метрик CPS из меток времени нажатий (мс от старта, по возрастанию).
/// </summary>
public static class CpsCalculator
{
    private const long OneSecond = 1000;
    private const long TenSeconds = 10_000;

    /// <summary>Итог: пиковый CPS (окно 1 с) и лучший устойчивый (окно 10 с; null при &lt; 10 с).</summary>
    public static CpsResult Compute(IReadOnlyList<long> pressTimesMs, long durationMs)
    {
        int max1s = MaxInWindow(pressTimesMs, OneSecond);
        double? best10 = durationMs >= TenSeconds ? MaxInWindow(pressTimesMs, TenSeconds) / 10.0 : null;
        return new CpsResult(max1s, best10, pressTimesMs.Count, durationMs);
    }

    /// <summary>Текущий CPS: число нажатий в окне (nowMs − 1000, nowMs]. Метки по возрастанию.</summary>
    public static int CurrentCps(IReadOnlyList<long> pressTimesMs, long nowMs)
    {
        long from = nowMs - OneSecond;
        int count = 0;
        for (int i = pressTimesMs.Count - 1; i >= 0 && pressTimesMs[i] > from; i--)
            count++;
        return count;
    }

    /// <summary>Наибольшее число нажатий в любом окне размера window (мс), оканчивающемся на нажатии.</summary>
    private static int MaxInWindow(IReadOnlyList<long> t, long window)
    {
        int max = 0, start = 0;
        for (int end = 0; end < t.Count; end++)
        {
            while (t[end] - t[start] >= window) start++;
            int count = end - start + 1;
            if (count > max) max = count;
        }
        return max;
    }
}
