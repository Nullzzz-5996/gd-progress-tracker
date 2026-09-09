using System.Net.Mail;
using System.Security.Claims;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Api.Endpoints;

/// <summary>Общие проверки и ответы для эндпоинтов API.</summary>
internal static class EndpointHelpers
{
    /// <summary>Максимальная длина пароля: PBKDF2 не ограничивает её, но и принимать мегабайт незачем.</summary>
    public const int MaxPasswordLength = 128;

    /// <summary>Минимальная длина пароля.</summary>
    public const int MinPasswordLength = 8;

    /// <summary>Приводит почту к каноническому виду для сравнения и хранения.</summary>
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>
    /// Каноническая форма того, что ввели в поле входа. Ник и почта сводятся к
    /// нижнему регистру одинаково (<see cref="Data.Usernames.Normalize"/> делает
    /// то же самое), поэтому одного значения хватает, чтобы сравнить его и с
    /// колонкой почты, и с колонкой нормализованного ника.
    /// </summary>
    public static string NormalizeLogin(string login) => login.Trim().ToLowerInvariant();

    /// <summary>
    /// Проверяет пару «почта — пароль» на пригодность к регистрации.
    /// Возвращает описание ошибки либо null, если всё в порядке.
    /// </summary>
    public static ApiError? ValidateCredentials(string? email, string? password)
    {
        if (string.IsNullOrWhiteSpace(email) || !MailAddress.TryCreate(email.Trim(), out _))
            return new ApiError("invalid_email", "Укажи корректный адрес почты.");

        if (string.IsNullOrEmpty(password) || password.Length < MinPasswordLength)
            return new ApiError("weak_password", $"Пароль должен быть не короче {MinPasswordLength} символов.");

        if (password.Length > MaxPasswordLength)
            return new ApiError("weak_password", $"Пароль длиннее {MaxPasswordLength} символов не принимается.");

        return null;
    }

    /// <summary>
    /// Ответ забаненному аккаунту: 403 с причиной. Один и тот же и на входе,
    /// и на синхронизации, и в демонлисте — человек должен понимать, что
    /// происходит, а не гадать над «доступ запрещён».
    /// </summary>
    public static IResult BannedResult(Data.UserAccount user) => Results.Json(
        new ApiError(
            BanCodes.Account,
            string.IsNullOrWhiteSpace(user.BanReason)
                ? "Аккаунт заблокирован владельцем."
                : $"Аккаунт заблокирован владельцем: {user.BanReason}"),
        statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Идентификатор пользователя из токена, либо null, если токена нет или он без «sub».</summary>
    public static Guid? UserId(ClaimsPrincipal principal)
    {
        var raw = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                  ?? principal.FindFirstValue("sub");

        return Guid.TryParse(raw, out var id) ? id : null;
    }
}
