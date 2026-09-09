using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;
using GdTracker.Core.Abstractions;
using GdTracker.Sharing;

namespace GdTracker.Tests;

/// <summary>Хранилище токена в памяти: тесты не должны трогать DPAPI и файлы пользователя.</summary>
internal sealed class InMemoryTokenStore : ITokenStore
{
    private StoredToken? _token;

    public int SaveCallCount { get; private set; }
    public int ClearCallCount { get; private set; }

    public StoredToken? Load() => _token;

    public void Save(StoredToken token)
    {
        SaveCallCount++;
        _token = token;
    }

    public void Clear()
    {
        ClearCallCount++;
        _token = null;
    }

    /// <summary>Подкладывает токен так, как если бы он остался от прошлого запуска.</summary>
    public void Preload(StoredToken token) => _token = token;
}

/// <summary>Настройки синхронизации в памяти.</summary>
internal sealed class InMemoryCloudSettings : ICloudSettingsStore
{
    public string ServerUrl { get; set; } = "http://localhost:5080";
    public string? LastEmail { get; set; }
    public bool AutoSyncOnStartup { get; set; }
    public DateTime? LastSyncAtUtc { get; set; }
    public long LastRevision { get; set; }
}

/// <summary>
/// Локальный прогресс в памяти: пакет, который «лежит на компьютере», и журнал
/// слияний — этого хватает, чтобы проверить порядок шагов синхронизации.
/// </summary>
internal sealed class FakeLocalProgressStore : ILocalProgressStore
{
    public ProgressPackage Local { get; set; } = new();

    public List<ProgressPackage> Merged { get; } = new();

    public ImportSummary MergeResult { get; set; } = new(0, 0, 0);

    public Task<ProgressPackage> CreatePackageAsync(CancellationToken ct = default)
        => Task.FromResult(Local);

    public Task<ImportSummary> MergePackageAsync(ProgressPackage package, CancellationToken ct = default)
    {
        Merged.Add(package);
        return Task.FromResult(MergeResult);
    }
}

/// <summary>
/// Управляемая заглушка облака: хранит «серверный» снимок, считает вызовы и умеет
/// по команде отвечать ошибкой нужного вида.
/// </summary>
internal sealed class FakeCloudClient : ICloudClient
{
    public string? StoredEmail { get; private set; }

    /// <summary>Ник, который «хранит сервер»: задаётся регистрацией и сменой ника.</summary>
    public string StoredUsername { get; set; } = "gd-player";

    /// <summary>Роль, которую заглушка отдаёт в ответах.</summary>
    public UserRole StoredRole { get; set; } = UserRole.Member;

    public string Token { get; set; } = "test-token";
    public DateTime TokenExpiry { get; set; } = DateTime.UtcNow.AddDays(30);

    public SnapshotResponse? Snapshot { get; set; }

    public int GetSnapshotCallCount { get; private set; }
    public int PushCallCount { get; private set; }
    public int DeleteSnapshotCallCount { get; private set; }
    public int DeleteAccountCallCount { get; private set; }
    public List<ProgressPackage> Pushed { get; } = new();
    public List<long> PushedBaseRevisions { get; } = new();

    /// <summary>Сколько ближайших записей отклонить конфликтом ревизий.</summary>
    public int ConflictsToRaise { get; set; }

    /// <summary>Ошибка, которой отвечают все вызовы (проверка обработки сбоев).</summary>
    public CloudException? FailWith { get; set; }

    public Task<AuthResponse> RegisterAsync(
        string baseUrl, string email, string password, string? username = null, CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(username))
            StoredUsername = username.Trim();

        return Authenticate(email);
    }

    public Task<AccountInfo> ChangeUsernameAsync(
        string baseUrl, string accessToken, string username, CancellationToken ct = default)
    {
        if (FailWith is not null)
            throw FailWith;

        StoredUsername = username.Trim();
        return GetAccountAsync(baseUrl, accessToken, ct);
    }

    public Task<AuthResponse> LoginAsync(string baseUrl, string login, string password, CancellationToken ct = default)
        => Authenticate(login);

    public Task<AccountInfo> GetAccountAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        if (FailWith is not null)
            throw FailWith;

        return Task.FromResult(new AccountInfo(
            "user-1",
            StoredEmail ?? "user@example.com",
            StoredUsername,
            StoredRole,
            DateTime.UtcNow.AddDays(-1),
            Snapshot?.Revision ?? 0,
            Snapshot?.UpdatedAtUtc));
    }

    public Task<SnapshotResponse?> GetSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        GetSnapshotCallCount++;
        if (FailWith is not null)
            throw FailWith;

        return Task.FromResult(Snapshot);
    }

    public Task<PushSnapshotResponse> PushSnapshotAsync(
        string baseUrl, string accessToken, long baseRevision, ProgressPackage package, CancellationToken ct = default)
    {
        PushCallCount++;
        if (FailWith is not null)
            throw FailWith;

        if (ConflictsToRaise > 0)
        {
            ConflictsToRaise--;
            // Имитируем чужую запись: ревизия в облаке ушла вперёд.
            Snapshot = new SnapshotResponse((Snapshot?.Revision ?? 0) + 1, DateTime.UtcNow, new ProgressPackage());
            throw new CloudException(CloudErrorKind.Conflict, "Конфликт ревизий.");
        }

        PushedBaseRevisions.Add(baseRevision);
        Pushed.Add(package);

        var revision = baseRevision + 1;
        Snapshot = new SnapshotResponse(revision, DateTime.UtcNow, package);
        return Task.FromResult(new PushSnapshotResponse(revision, Snapshot.UpdatedAtUtc));
    }

    public Task DeleteSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        DeleteSnapshotCallCount++;
        if (FailWith is not null)
            throw FailWith;

        Snapshot = null;
        return Task.CompletedTask;
    }

    public Task DeleteAccountAsync(string baseUrl, string accessToken, string password, CancellationToken ct = default)
    {
        DeleteAccountCallCount++;
        if (FailWith is not null)
            throw FailWith;

        Snapshot = null;
        return Task.CompletedTask;
    }

    public Task<bool> CheckHealthAsync(string baseUrl, CancellationToken ct = default)
        => Task.FromResult(FailWith is null);

    private Task<AuthResponse> Authenticate(string email)
    {
        if (FailWith is not null)
            throw FailWith;

        StoredEmail = email;
        return Task.FromResult(new AuthResponse("user-1", email, StoredUsername, StoredRole, Token, TokenExpiry));
    }
}
