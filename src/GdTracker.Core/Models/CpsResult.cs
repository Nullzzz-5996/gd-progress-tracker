namespace GdTracker.Core.Models;

/// <summary>Итог замера CPS.</summary>
/// <param name="MaxCps">Пиковый CPS: наибольшее число нажатий за окно 1 с.</param>
/// <param name="Best10sCps">Лучший устойчивый CPS за окно 10 с (нажатий/10); null при заходе &lt; 10 с.</param>
/// <param name="TotalClicks">Всего засчитанных нажатий.</param>
/// <param name="DurationMs">Длительность захода, мс.</param>
public record CpsResult(int MaxCps, double? Best10sCps, int TotalClicks, long DurationMs);
