namespace GdTracker.Cloud;

/// <summary>Разновидности сбоев при обращении к облаку.</summary>
public enum CloudErrorKind
{
    /// <summary>Сервер недоступен: нет сети, неверный адрес, таймаут.</summary>
    Network,

    /// <summary>Неверные данные входа или истёкший токен.</summary>
    Unauthorized,

    /// <summary>Данные в облаке изменились с другого устройства.</summary>
    Conflict,

    /// <summary>Запрос отклонён проверками (короткий пароль, занятая почта и подобное).</summary>
    Validation,

    /// <summary>Ошибка на стороне сервера или неожиданный ответ.</summary>
    Server,
}

/// <summary>
/// Ошибка обращения к облаку с уже готовым для показа пользователю сообщением.
/// Приложение работает и без облака, поэтому такие ошибки нигде не роняют работу
/// вкладок — они лишь отображаются на странице аккаунта.
/// </summary>
public sealed class CloudException : Exception
{
    public CloudException(CloudErrorKind kind, string message, Exception? inner = null)
        : base(message, inner)
        => Kind = kind;

    public CloudErrorKind Kind { get; }
}
