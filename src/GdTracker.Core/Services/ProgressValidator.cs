namespace GdTracker.Core.Services;

/// <summary>Результат валидации.</summary>
public readonly record struct ValidationResult(bool IsValid, string? Error)
{
    public static ValidationResult Ok() => new(true, null);
    public static ValidationResult Fail(string error) => new(false, error);
}

/// <summary>
/// Валидация записи прогресса: проценты в допустимом диапазоне и согласованы с типом записи.
/// </summary>
public static class ProgressValidator
{
    public const int MinPercent = 0;
    public const int MaxPercent = 100;

    /// <summary>
    /// Проверяет корректность процентов прогресса.
    /// Правила: 0 ≤ start &lt; reached ≤ 100; для <see cref="RunType.FromZero"/> start = 0;
    /// для <see cref="RunType.Segment"/> start ≥ 1 (X ≠ 0).
    /// </summary>
    public static ValidationResult Validate(RunType type, int startPercent, int reachedPercent)
    {
        if (startPercent < MinPercent)
            return ValidationResult.Fail($"Стартовый процент не может быть меньше {MinPercent}.");

        if (reachedPercent > MaxPercent)
            return ValidationResult.Fail($"Достигнутый процент не может превышать {MaxPercent}.");

        if (reachedPercent <= startPercent)
            return ValidationResult.Fail("Достигнутый процент должен быть больше стартового.");

        if (type == RunType.FromZero && startPercent != 0)
            return ValidationResult.Fail("Для прогресса «с нуля» стартовый процент должен быть 0.");

        if (type == RunType.Segment && startPercent == 0)
            return ValidationResult.Fail("Для сегмента стартовый процент должен быть больше 0.");

        return ValidationResult.Ok();
    }
}
