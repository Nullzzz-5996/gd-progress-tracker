using GdTracker.Sharing;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <summary>
/// Транспорт облачного API: только HTTP-вызовы, без состояния.
/// Адрес сервера и токен передаются явно — состояние сессии хранит
/// <see cref="ICloudAccountService"/>.
/// </summary>
public interface ICloudClient
{
    /// <param name="username">
    /// Ник, под которым аккаунт будет виден в демонлисте. Пустое значение сервер
    /// выведет из почты сам.
    /// </param>
    Task<AuthResponse> RegisterAsync(
        string baseUrl, string email, string password, string? username = null, CancellationToken ct = default);

    /// <param name="login">Ник или почта — сервер принимает и то, и другое.</param>
    Task<AuthResponse> LoginAsync(string baseUrl, string login, string password, CancellationToken ct = default);

    Task<AccountInfo> GetAccountAsync(string baseUrl, string accessToken, CancellationToken ct = default);

    /// <summary>Меняет ник аккаунта и возвращает обновлённые сведения о нём.</summary>
    Task<AccountInfo> ChangeUsernameAsync(
        string baseUrl, string accessToken, string username, CancellationToken ct = default);

    /// <summary>Снимок прогресса из облака либо null, если пользователь ещё ничего не загружал.</summary>
    Task<SnapshotResponse?> GetSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default);

    /// <summary>
    /// Записывает снимок. Бросает <see cref="CloudException"/> с
    /// <see cref="CloudErrorKind.Conflict"/>, если в облаке уже другая ревизия.
    /// </summary>
    Task<PushSnapshotResponse> PushSnapshotAsync(
        string baseUrl, string accessToken, long baseRevision, ProgressPackage package, CancellationToken ct = default);

    /// <summary>Удаляет снимок прогресса из облака (аккаунт остаётся).</summary>
    Task DeleteSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default);

    /// <summary>
    /// Удаляет аккаунт вместе с облачными данными. Пароль подтверждает намерение:
    /// сервер не примет удаление по одному лишь токену.
    /// </summary>
    Task DeleteAccountAsync(string baseUrl, string accessToken, string password, CancellationToken ct = default);

    /// <summary>Проверяет доступность сервера по указанному адресу.</summary>
    Task<bool> CheckHealthAsync(string baseUrl, CancellationToken ct = default);
}
