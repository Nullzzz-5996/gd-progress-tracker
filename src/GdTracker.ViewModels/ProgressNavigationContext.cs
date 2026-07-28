namespace GdTracker.ViewModels;

/// <summary>
/// Передаёт вкладке «Прогрессы», какой уровень открыть, при переходе с вкладки «Уровни».
/// Singleton: устанавливается перед навигацией, читается вью-моделью страницы при загрузке.
/// </summary>
public interface IProgressNavigationContext
{
    int? TargetLevelId { get; set; }
}

/// <inheritdoc />
public sealed class ProgressNavigationContext : IProgressNavigationContext
{
    public int? TargetLevelId { get; set; }
}
