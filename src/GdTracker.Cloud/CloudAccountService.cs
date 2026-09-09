using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <summary>
/// Состояние аккаунта в приложении: вход, выход, восстановление сессии между запусками.
/// Аккаунт необязателен — пока входа нет, приложение работает целиком локально,
/// и ни один экран не требует авторизации.
/// </summary>
public interface ICloudAccountService
{
    /// <summary>Есть ли действующая сессия.</summary>
    bool IsSignedIn { get; }

    /// <summary>Почта вошедшего пользователя, либо null.</summary>
    string? Email { get; }

    /// <summary>Идентификатор вошедшего пользователя, либо null.</summary>
    string? UserId { get; }

    /// <summary>Ник вошедшего пользователя, либо null.</summary>
    string? Username { get; }

    /// <summary>Роль вошедшего пользователя, либо null, если входа нет.</summary>
    UserRole? Role { get; }

    /// <summary>Адрес сервера синхронизации (сохраняется между запусками).</summary>
    string ServerUrl { get; set; }

    /// <summary>Срабатывает при входе, выходе и смене адреса сервера.</summary>
    event EventHandler? StateChanged;

    /// <summary>
    /// Восстанавливает сессию из сохранённого токена. Просроченный токен молча
    /// отбрасывается: приложение продолжает работать без аккаунта.
    /// </summary>
    void Restore();

    Task RegisterAsync(string email, string password, string? username = null, CancellationToken ct = default);

    /// <param name="login">Ник или почта — сервер принимает и то, и другое.</param>
    Task SignInAsync(string login, string password, CancellationToken ct = default);

    /// <summary>
    /// Меняет ник. Ник публичный и уникальный, поэтому занятое имя сервер отклонит —
    /// сессия при этом остаётся прежней.
    /// </summary>
    Task ChangeUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>Выходит из аккаунта. Локальные данные при этом не трогаются.</summary>
    void SignOut();

    /// <summary>
    /// Отвечает ли сервер по текущему адресу. Вход не требуется: это проверка
    /// «есть ли вообще куда регистрироваться».
    /// </summary>
    Task<bool> CheckServerAsync(CancellationToken ct = default);

    /// <summary>Сведения об аккаунте с сервера (в том числе ревизия облачного снимка).</summary>
    Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default);

    /// <summary>Удаляет облачную копию прогресса, оставляя аккаунт и локальные данные.</summary>
    Task DeleteCloudSnapshotAsync(CancellationToken ct = default);

    /// <summary>Удаляет аккаунт и его данные в облаке; локальная база остаётся нетронутой.</summary>
    Task DeleteAccountAsync(string password, CancellationToken ct = default);

    /// <summary>
    /// Токен для запроса. Бросает <see cref="CloudException"/> с
    /// <see cref="CloudErrorKind.Unauthorized"/>, если входа нет.
    /// </summary>
    string RequireAccessToken();

    /// <summary>
    /// Сбрасывает сессию после отказа сервера (401): токен отозван или истёк,
    /// и держать его дальше бессмысленно.
    /// </summary>
    void HandleUnauthorized();
}

/// <inheritdoc />
public sealed class CloudAccountService : ICloudAccountService
{
    private readonly ICloudClient _client;
    private readonly ITokenStore _tokens;
    private readonly ICloudSettingsStore _settings;

    private StoredToken? _session;

    public CloudAccountService(ICloudClient client, ITokenStore tokens, ICloudSettingsStore settings)
    {
        _client = client;
        _tokens = tokens;
        _settings = settings;
    }

    public bool IsSignedIn => _session is { IsExpired: false };

    public string? Email => _session?.Email;

    public string? UserId => _session?.UserId;

    public string? Username => _session?.Username;

    public UserRole? Role => _session?.Role;

    public string ServerUrl
    {
        get => _settings.ServerUrl;
        set
        {
            if (string.Equals(_settings.ServerUrl, value, StringComparison.OrdinalIgnoreCase))
                return;

            _settings.ServerUrl = value;
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public event EventHandler? StateChanged;

    public void Restore()
    {
        var stored = _tokens.Load();
        if (stored is null)
            return;

        if (stored.IsExpired)
        {
            _tokens.Clear();
            return;
        }

        _session = stored;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task RegisterAsync(
        string email, string password, string? username = null, CancellationToken ct = default)
    {
        var response = await _client.RegisterAsync(ServerUrl, email.Trim(), password, username?.Trim(), ct);
        Apply(response);
    }

    public async Task ChangeUsernameAsync(string username, CancellationToken ct = default)
    {
        var info = await _client.ChangeUsernameAsync(ServerUrl, RequireAccessToken(), username.Trim(), ct);

        // Токен остаётся прежним — в нём ника нет, сервер выдаёт его отдельно.
        // Обновляем только сохранённую карточку сессии, чтобы страница показывала
        // новый ник и после перезапуска приложения.
        _session = _session! with { Username = info.Username, Role = info.Role };
        _tokens.Save(_session);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SignInAsync(string login, string password, CancellationToken ct = default)
    {
        var response = await _client.LoginAsync(ServerUrl, login.Trim(), password, ct);
        Apply(response);
    }

    public void SignOut()
    {
        _session = null;
        _tokens.Clear();
        // Ревизию тоже забываем: следующая синхронизация (возможно, другого
        // аккаунта) должна начинать с чтения облака, а не с чужого номера.
        _settings.LastRevision = 0;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<bool> CheckServerAsync(CancellationToken ct = default)
        => _client.CheckHealthAsync(ServerUrl, ct);

    public async Task<AccountInfo> GetAccountInfoAsync(CancellationToken ct = default)
    {
        var info = await _client.GetAccountAsync(ServerUrl, RequireAccessToken(), ct);

        // Ник и роль могли поменяться на сервере (например, выдали права модератора):
        // сохранённая карточка сессии подтягивается за ответом, иначе приложение
        // показывало бы прежнюю роль до следующего входа.
        if (_session is not null && (_session.Username != info.Username || _session.Role != info.Role))
        {
            _session = _session with { Username = info.Username, Role = info.Role };
            _tokens.Save(_session);
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        return info;
    }

    public Task DeleteCloudSnapshotAsync(CancellationToken ct = default)
        => _client.DeleteSnapshotAsync(ServerUrl, RequireAccessToken(), ct);

    public async Task DeleteAccountAsync(string password, CancellationToken ct = default)
    {
        await _client.DeleteAccountAsync(ServerUrl, RequireAccessToken(), password, ct);
        SignOut();
    }

    public string RequireAccessToken()
    {
        if (_session is null || _session.IsExpired)
            throw new CloudException(CloudErrorKind.Unauthorized, "Нужен вход в аккаунт.");

        return _session.AccessToken;
    }

    public void HandleUnauthorized()
    {
        if (_session is null)
            return;

        SignOut();
    }

    private void Apply(AuthResponse response)
    {
        _session = new StoredToken(
            response.UserId,
            response.Email,
            response.Username,
            response.Role,
            response.AccessToken,
            response.ExpiresAtUtc);
        _tokens.Save(_session);
        _settings.LastEmail = response.Email;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
