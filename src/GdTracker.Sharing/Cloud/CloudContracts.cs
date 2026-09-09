using System.Text.Json;
using System.Text.Json.Serialization;

namespace GdTracker.Sharing.Cloud;

/// <summary>
/// Контракты облачного API: одни и те же типы использует сервер (GdTracker.Api)
/// и клиент (GdTracker.Cloud). Лежат в GdTracker.Sharing, потому что это
/// единственная сборка без внешних зависимостей, доступная обеим сторонам,
/// и потому что переносимый пакет прогресса (<see cref="ProgressPackage"/>) —
/// тело запросов синхронизации — тоже живёт здесь.
/// </summary>
public static class CloudJson
{
    /// <summary>
    /// Настройки JSON, общие для клиента и сервера. Совпадают с настройками
    /// файлового обмена (<see cref="ProgressPackageSerializer"/>): camelCase и
    /// перечисления строками, иначе пакет, отправленный в облако, десериализовался
    /// бы иначе, чем тот же пакет из файла.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };
}

/// <summary>
/// Права аккаунта в сообществе. Живут в общих контрактах, потому что роль видна
/// всем сторонам: сервер её хранит, настольное приложение показывает рядом с
/// ником, страница демонлиста рисует ею значок у имени автора.
/// </summary>
public enum UserRole
{
    /// <summary>Обычный участник: пишет мнения и подаёт заявки на рекорды.</summary>
    Member = 0,

    /// <summary>Модератор: двигает демонов в списке и рассматривает заявки.</summary>
    Moderator = 1,

    /// <summary>Администратор: то же, что модератор, плюс выдача прав другим.</summary>
    Administrator = 2,

    /// <summary>
    /// Владелец: старшая роль. Правит топ-200 и топ пройденных уровней наравне
    /// с модератором, а сверх того банит аккаунты — целиком или только в
    /// демонлисте. Роль не выдаётся через API: её задаёт владелец сервера
    /// настройкой <c>Community:Owners</c>.
    /// </summary>
    Owner = 3,
}

/// <summary>Причины, по которым сервер отказывает забаненному аккаунту.</summary>
public static class BanCodes
{
    /// <summary>Аккаунт забанен целиком: ни входа, ни синхронизации.</summary>
    public const string Account = "account_banned";

    /// <summary>Аккаунт забанен в демонлисте: вход есть, участия в списке нет.</summary>
    public const string DemonList = "demonlist_banned";
}

/// <summary>
/// Регистрация нового аккаунта. Ник необязателен в контракте, но не в интерфейсе:
/// клиент его спрашивает, а пустое значение сервер выводит из почты, чтобы аккаунт,
/// созданный старым клиентом, всё равно получил имя.
/// </summary>
public sealed record RegisterRequest(string Email, string Password, string? Username = null);

/// <summary>Вход в существующий аккаунт.</summary>
/// <param name="Login">
/// Ник или почта — сервер принимает и то, и другое. Ник человек помнит про себя
/// сам, поэтому в интерфейсе спрашивают именно его; почта остаётся обязательной
/// только при регистрации. Перепутать их нельзя: в нике не бывает «@».
///
/// На проводе поле по-прежнему зовётся «email»: так его шлют уже выпущенные
/// клиенты, и переименование оставило бы их без входа.
/// </param>
public sealed record LoginRequest(
    [property: JsonPropertyName("email")] string Login,
    string Password);

/// <summary>Смена ника.</summary>
public sealed record ChangeUsernameRequest(string Username);

/// <summary>Выданный сервером токен доступа.</summary>
public sealed record AuthResponse(
    string UserId,
    string Email,
    string Username,
    UserRole Role,
    string AccessToken,
    DateTime ExpiresAtUtc);

/// <summary>Сведения о текущем аккаунте и его снимке прогресса.</summary>
public sealed record AccountInfo(
    string UserId,
    string Email,
    string Username,
    UserRole Role,
    DateTime CreatedAtUtc,
    long SnapshotRevision,
    DateTime? SnapshotUpdatedAtUtc);

/// <summary>Снимок прогресса, хранящийся в облаке.</summary>
public sealed record SnapshotResponse(long Revision, DateTime UpdatedAtUtc, ProgressPackage Package);

/// <summary>
/// Загрузка снимка в облако. <paramref name="BaseRevision"/> — ревизия, на которой
/// основан пакет: сервер отклонит запись (409), если в облаке уже другая ревизия,
/// то есть кто-то синхронизировался с другого устройства.
/// </summary>
public sealed record PushSnapshotRequest(long BaseRevision, ProgressPackage Package);

/// <summary>Результат записи снимка.</summary>
public sealed record PushSnapshotResponse(long Revision, DateTime UpdatedAtUtc);

/// <summary>Ошибка API в машиночитаемом виде.</summary>
public sealed record ApiError(string Code, string Message);
