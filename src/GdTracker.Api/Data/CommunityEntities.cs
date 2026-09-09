using GdTracker.Sharing.Cloud;

namespace GdTracker.Api.Data;

// Права аккаунта (UserRole) описаны в общих контрактах GdTracker.Sharing.Cloud:
// роль видит не только сервер, но и настольный клиент рядом с ником, и страница
// демонлиста, которая рисует ею значок у имени автора.

/// <summary>Позиция экстрим-демона в списке.</summary>
public sealed class DemonListEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Место в списке, начиная с единицы. Места идут подряд без пропусков.</summary>
    public int Position { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Автор уровня.</summary>
    public string Publisher { get; set; } = string.Empty;

    /// <summary>Верификатор — тот, кто прошёл уровень первым.</summary>
    public string Verifier { get; set; } = string.Empty;

    /// <summary>Внутриигровой идентификатор уровня, если известен.</summary>
    public long? LevelId { get; set; }

    public string? Video { get; set; }

    public string? Thumbnail { get; set; }

    /// <summary>Минимальный процент прохождения, с которого рекорд попадает в список.</summary>
    public int Requirement { get; set; } = 100;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}

/// <summary>
/// Мнение участника о том, на каком месте должен стоять демон. Двигать список
/// оно не двигает — это ровно то, что участник думает о текущей расстановке.
/// </summary>
public sealed class PlacementOpinion
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid DemonId { get; set; }

    public Guid AuthorId { get; set; }

    /// <summary>Место, которое автор считает справедливым. Может быть не указано.</summary>
    public int? SuggestedPosition { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }
}

/// <summary>
/// Заявка на попадание в топ пройденных уровней. Участник прикладывает видео
/// с кликами, модератор его смотрит и либо ставит рекорд в топ, либо отклоняет.
/// </summary>
public sealed class RecordSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>Демон из списка, если заявка на него; иначе рекорд по произвольному уровню.</summary>
    public Guid? DemonId { get; set; }

    /// <summary>Название уровня — копия на момент подачи, чтобы заявка читалась и без ссылки на список.</summary>
    public string LevelName { get; set; } = string.Empty;

    /// <summary>Достигнутый процент: 100 — полное прохождение.</summary>
    public int Progress { get; set; }

    /// <summary>Ссылка на видео прохождения. Обязательна.</summary>
    public string VideoUrl { get; set; } = string.Empty;

    /// <summary>Подтверждение автора, что на видео виден индикатор кликов.</summary>
    public bool HasClicks { get; set; }

    /// <summary>Комментарий автора к заявке.</summary>
    public string? Comment { get; set; }

    public RecordStatus Status { get; set; } = RecordStatus.Pending;

    /// <summary>Место в топе пройденных. Проставляется модератором при одобрении.</summary>
    public int? Placement { get; set; }

    public DateTime SubmittedAtUtc { get; set; }

    public DateTime? ReviewedAtUtc { get; set; }

    public Guid? ReviewedById { get; set; }

    /// <summary>Пояснение модератора — прежде всего к отказу.</summary>
    public string? ReviewNote { get; set; }
}
