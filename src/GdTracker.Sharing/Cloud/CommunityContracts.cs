namespace GdTracker.Sharing.Cloud;

/// <summary>
/// Контракты демонлиста и топа пройденных уровней. Лежат рядом с контрактами
/// облака и по той же причине: их одинаково читают сервер (GdTracker.Api),
/// страница сайта и настольное приложение — вкладка «Демонлист» ходит в те же
/// эндпоинты, что и страница.
/// </summary>
/// <param name="Id">Идентификатор записи в списке, а не идентификатор уровня в игре.</param>
public sealed record DemonView(
    string Id,
    int Position,
    string Name,
    string Publisher,
    string Verifier,
    long? LevelId,
    string? Video,
    string? Thumbnail,
    int Requirement,
    int OpinionCount);

/// <summary>Мнение участника о месте демона.</summary>
/// <param name="AuthorName">Ник автора.</param>
/// <param name="AuthorRole">Роль автора: страница рисует её значком рядом с ником.</param>
public sealed record OpinionView(
    string Id,
    string AuthorId,
    string AuthorName,
    UserRole AuthorRole,
    int? SuggestedPosition,
    string Text,
    DateTime CreatedAtUtc);

/// <summary>Новое мнение о размещении.</summary>
public sealed record OpinionRequest(string Text, int? SuggestedPosition);

/// <summary>Перестановка демона на другое место.</summary>
public sealed record MoveDemonRequest(int Position);

/// <summary>Добавление демона в список на указанное место.</summary>
public sealed record AddDemonRequest(
    int Position,
    string Name,
    string? Publisher,
    string? Verifier,
    long? LevelId,
    string? Video,
    string? Thumbnail,
    int? Requirement);

/// <summary>Состояние заявки на рекорд.</summary>
public enum RecordStatus
{
    /// <summary>Подана и ждёт решения модератора.</summary>
    Pending = 0,

    /// <summary>Одобрена: стоит в топе пройденных.</summary>
    Approved = 1,

    /// <summary>Отклонена модератором.</summary>
    Rejected = 2,
}

/// <summary>Заявка на попадание в топ пройденных уровней.</summary>
public sealed record SubmitRecordRequest(
    string? DemonId,
    string? LevelName,
    int Progress,
    string VideoUrl,
    bool HasClicks,
    string? Comment);

/// <summary>Заявка так, как её видят автор и модератор.</summary>
public sealed record RecordView(
    string Id,
    string PlayerId,
    string PlayerName,
    UserRole PlayerRole,
    string? DemonId,
    int? DemonPosition,
    string LevelName,
    int Progress,
    string VideoUrl,
    bool HasClicks,
    string? Comment,
    RecordStatus Status,
    int? Placement,
    DateTime SubmittedAtUtc,
    DateTime? ReviewedAtUtc,
    string? ReviewNote);

/// <summary>Решение модератора по заявке.</summary>
/// <param name="Placement">Место в топе при одобрении. Не указано — рекорд встаёт в конец.</param>
public sealed record ReviewRecordRequest(bool Approve, int? Placement, string? Note);

/// <summary>Кто вошёл и что ему можно.</summary>
/// <param name="CanModerate">Правит топ-200 и рассматривает заявки: модератор и выше.</param>
/// <param name="CanBan">Банит аккаунты и раздаёт баны в демонлисте: только владелец.</param>
/// <param name="ListBanned">Забанен в демонлисте: читать список можно, участвовать — нет.</param>
public sealed record CommunityProfile(
    string UserId,
    string Email,
    string Username,
    UserRole Role,
    bool CanModerate,
    bool CanBan,
    bool ListBanned,
    string? ListBanReason);

/// <summary>Аккаунт так, как его видит владелец в списке участников.</summary>
public sealed record ManagedUserView(
    string UserId,
    string Email,
    string Username,
    UserRole Role,
    bool Banned,
    string? BanReason,
    DateTime? BannedAtUtc,
    bool ListBanned,
    string? ListBanReason,
    DateTime? ListBannedAtUtc,
    DateTime CreatedAtUtc);

/// <summary>Бан или его снятие. <paramref name="Banned"/> = false снимает бан.</summary>
public sealed record BanRequest(bool Banned, string? Reason);

/// <summary>Смена ника со страницы демонлиста. То же, что PUT /api/account/username.</summary>
public sealed record UpdateProfileRequest(string Username);

/// <summary>Выдача или снятие прав. Доступно только администратору.</summary>
public sealed record SetRoleRequest(string Email, UserRole Role);
