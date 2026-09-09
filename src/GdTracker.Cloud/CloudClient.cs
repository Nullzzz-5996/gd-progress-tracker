using System.Net;
using System.Net.Http.Json;
using GdTracker.Sharing;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <inheritdoc />
public sealed class CloudClient : ICloudClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <summary>Боевой конструктор: собственный HttpClient с разумным таймаутом.</summary>
    public CloudClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    /// <summary>Конструктор с готовым HttpClient (используется тестами и DI).</summary>
    public CloudClient(HttpClient http, bool ownsHttpClient = false)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
    }

    public Task<AuthResponse> RegisterAsync(
        string baseUrl, string email, string password, string? username = null, CancellationToken ct = default)
        => PostAsync<RegisterRequest, AuthResponse>(
            baseUrl, "api/auth/register", new RegisterRequest(email, password, username), ct);

    public Task<AuthResponse> LoginAsync(string baseUrl, string login, string password, CancellationToken ct = default)
        => PostAsync<LoginRequest, AuthResponse>(baseUrl, "api/auth/login", new LoginRequest(login, password), ct);

    public async Task<AccountInfo> GetAccountAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CloudHttp.Endpoint(baseUrl, "api/account/me"));
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<AccountInfo>(response, ct);
    }

    public async Task<AccountInfo> ChangeUsernameAsync(
        string baseUrl, string accessToken, string username, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, CloudHttp.Endpoint(baseUrl, "api/account/username"))
        {
            Content = JsonContent.Create(new ChangeUsernameRequest(username), options: CloudJson.Options),
        };
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<AccountInfo>(response, ct);
    }

    public async Task<SnapshotResponse?> GetSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CloudHttp.Endpoint(baseUrl, "api/sync/snapshot"));
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);

        // 204 — аккаунт есть, но прогресс в облако ещё ни разу не загружали.
        if (response.StatusCode == HttpStatusCode.NoContent)
            return null;

        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<SnapshotResponse>(response, ct);
    }

    public async Task<PushSnapshotResponse> PushSnapshotAsync(
        string baseUrl, string accessToken, long baseRevision, ProgressPackage package, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, CloudHttp.Endpoint(baseUrl, "api/sync/snapshot"))
        {
            Content = JsonContent.Create(new PushSnapshotRequest(baseRevision, package), options: CloudJson.Options),
        };
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<PushSnapshotResponse>(response, ct);
    }

    public async Task DeleteSnapshotAsync(string baseUrl, string accessToken, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, CloudHttp.Endpoint(baseUrl, "api/sync/snapshot"));
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
    }

    public async Task DeleteAccountAsync(string baseUrl, string accessToken, string password, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, CloudHttp.Endpoint(baseUrl, "api/account"))
        {
            // Почта в теле не используется сервером (пользователь определяется по
            // токену), но контракт подтверждения общий с входом.
            Content = JsonContent.Create(new LoginRequest(string.Empty, password), options: CloudJson.Options),
        };
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
    }

    public async Task<bool> CheckHealthAsync(string baseUrl, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CloudHttp.Endpoint(baseUrl, "health"));
        using var response = await CloudHttp.SendAsync(_http, request, ct);
        return response.IsSuccessStatusCode;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string baseUrl, string path, TRequest payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CloudHttp.Endpoint(baseUrl, path))
        {
            Content = JsonContent.Create(payload, options: CloudJson.Options),
        };

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<TResponse>(response, ct);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
