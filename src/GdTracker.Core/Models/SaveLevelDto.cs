namespace GdTracker.Core.Models;

/// <summary>Данные одного уровня, прочитанные из сейв-файла GD (CCGameManager.dat).</summary>
public sealed record SaveLevelDto
{
    /// <summary>ID уровня в GD (k1).</summary>
    public required long GdLevelId { get; init; }

    /// <summary>Название уровня (k2), если присутствует в сейве.</summary>
    public string? Name { get; init; }

    /// <summary>Официальный или онлайн уровень.</summary>
    public LevelSource Source { get; init; }

    /// <summary>Лучший процент в обычном режиме (k19).</summary>
    public int BestNormalPercent { get; init; }

    /// <summary>Лучший процент в режиме практики (k20).</summary>
    public int BestPracticePercent { get; init; }

    /// <summary>Количество попыток по уровню (k18).</summary>
    public int Attempts { get; init; }

    /// <summary>Звёзды за уровень (k26), если присутствуют.</summary>
    public int? Stars { get; init; }

    /// <summary>Имя создателя (k5), если присутствует.</summary>
    public string? Creator { get; init; }

    /// <summary>Вычисленная метка сложности.</summary>
    public string? Difficulty { get; init; }
}

