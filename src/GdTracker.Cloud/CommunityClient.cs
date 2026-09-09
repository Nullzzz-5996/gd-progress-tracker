using System.Net.Http.Json;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <summary>
/// Транспорт демонлиста: те же эндпоинты <c>/api/community</c>, в которые ходит
/// страница сайта, поэтому список, мнения и заявки в приложении и на сайте —
/// одни и те же данные, а не две независимые копии.
///
/// Как и <see cref="ICloudClient"/>, состояния не держит: адрес сервера и токен
/// приходят снаружи, сессию ведёт <see cref="ICloudAccountService"/>.
/// </summary>
public interface ICommunityClient
{
    /// <summary>Топ-200 демонов. Чтение открыто всем: список виден и без входа.</summary>
    Task<IReadOnlyList<DemonView>> GetDemonsAsync(string baseUrl, CancellationToken ct = default);

    /// <summary>Мнения о размещении демона. Тоже открыты на чтение.</summary>
    Task<IReadOnlyList<OpinionView>> GetOpinionsAsync(string baseUrl, string demonId, CancellationToken ct = default);

    Task<OpinionView> PostOpinionAsync(
        string baseUrl, string accessToken, string demonId, OpinionRequest request, CancellationToken ct = default);

    Task DeleteOpinionAsync(string baseUrl, string accessToken, string opinionId, CancellationToken ct = default);

    /// <summary>Перестановка демона. Доступна модератору и выше.</summary>
    Task<DemonView> MoveDemonAsync(
        string baseUrl, string accessToken, string demonId, int position, CancellationToken ct = default);

    Task<DemonView> AddDemonAsync(
        string baseUrl, string accessToken, AddDemonRequest request, CancellationToken ct = default);

    Task DeleteDemonAsync(string baseUrl, string accessToken, string demonId, CancellationToken ct = default);

    /// <summary>Кто вошёл и что ему можно: права и бан в демонлисте.</summary>
    Task<CommunityProfile> GetProfileAsync(string baseUrl, string accessToken, CancellationToken ct = default);

    /// <summary>Публичный топ пройденных уровней — только одобренные заявки.</summary>
    Task<IReadOnlyList<RecordView>> GetTopRecordsAsync(string baseUrl, CancellationToken ct = default);

    Task<IReadOnlyList<RecordView>> GetMyRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default);

    /// <summary>Очередь заявок на рассмотрение. Доступна модератору и выше.</summary>
    Task<IReadOnlyList<RecordView>> GetPendingRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default);

    Task<RecordView> SubmitRecordAsync(
        string baseUrl, string accessToken, SubmitRecordRequest request, CancellationToken ct = default);

    Task<RecordView> ReviewRecordAsync(
        string baseUrl, string accessToken, string recordId, ReviewRecordRequest request, CancellationToken ct = default);

    /// <summary>Список аккаунтов. Доступен только владельцу.</summary>
    Task<IReadOnlyList<ManagedUserView>> GetUsersAsync(
        string baseUrl, string accessToken, string? query, CancellationToken ct = default);

    /// <summary>Полный бан аккаунта: ни входа, ни синхронизации.</summary>
    Task<ManagedUserView> SetBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default);

    /// <summary>Бан в демонлисте: вход остаётся, участие в списке — нет.</summary>
    Task<ManagedUserView> SetListBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CommunityClient : ICommunityClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;

    /// <summary>Боевой конструктор: собственный HttpClient с разумным таймаутом.</summary>
    public CommunityClient() : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsHttpClient: true)
    {
    }

    /// <summary>Конструктор с готовым HttpClient (используется тестами и DI).</summary>
    public CommunityClient(HttpClient http, bool ownsHttpClient = false)
    {
        _http = http;
        _ownsHttpClient = ownsHttpClient;
    }

    public Task<IReadOnlyList<DemonView>> GetDemonsAsync(string baseUrl, CancellationToken ct = default)
        => GetListAsync<DemonView>(baseUrl, accessToken: null, "api/community/demons", ct);

    public Task<IReadOnlyList<OpinionView>> GetOpinionsAsync(
        string baseUrl, string demonId, CancellationToken ct = default)
        => GetListAsync<OpinionView>(baseUrl, accessToken: null, $"api/community/demons/{demonId}/opinions", ct);

    public Task<OpinionView> PostOpinionAsync(
        string baseUrl, string accessToken, string demonId, OpinionRequest request, CancellationToken ct = default)
        => SendAsync<OpinionRequest, OpinionView>(
            HttpMethod.Post, baseUrl, accessToken, $"api/community/demons/{demonId}/opinions", request, ct);

    public Task DeleteOpinionAsync(
        string baseUrl, string accessToken, string opinionId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, baseUrl, accessToken, $"api/community/opinions/{opinionId}", ct);

    public Task<DemonView> MoveDemonAsync(
        string baseUrl, string accessToken, string demonId, int position, CancellationToken ct = default)
        => SendAsync<MoveDemonRequest, DemonView>(
            HttpMethod.Put, baseUrl, accessToken, $"api/community/demons/{demonId}/position",
            new MoveDemonRequest(position), ct);

    public Task<DemonView> AddDemonAsync(
        string baseUrl, string accessToken, AddDemonRequest request, CancellationToken ct = default)
        => SendAsync<AddDemonRequest, DemonView>(
            HttpMethod.Post, baseUrl, accessToken, "api/community/demons", request, ct);

    public Task DeleteDemonAsync(string baseUrl, string accessToken, string demonId, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, baseUrl, accessToken, $"api/community/demons/{demonId}", ct);

    public Task<CommunityProfile> GetProfileAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
        => GetAsync<CommunityProfile>(baseUrl, accessToken, "api/community/me", ct);

    public Task<IReadOnlyList<RecordView>> GetTopRecordsAsync(string baseUrl, CancellationToken ct = default)
        => GetListAsync<RecordView>(baseUrl, accessToken: null, "api/community/records", ct);

    public Task<IReadOnlyList<RecordView>> GetMyRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
        => GetListAsync<RecordView>(baseUrl, accessToken, "api/community/records/mine", ct);

    public Task<IReadOnlyList<RecordView>> GetPendingRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
        => GetListAsync<RecordView>(baseUrl, accessToken, "api/community/records/pending", ct);

    public Task<RecordView> SubmitRecordAsync(
        string baseUrl, string accessToken, SubmitRecordRequest request, CancellationToken ct = default)
        => SendAsync<SubmitRecordRequest, RecordView>(
            HttpMethod.Post, baseUrl, accessToken, "api/community/records", request, ct);

    public Task<RecordView> ReviewRecordAsync(
        string baseUrl, string accessToken, string recordId, ReviewRecordRequest request, CancellationToken ct = default)
        => SendAsync<ReviewRecordRequest, RecordView>(
            HttpMethod.Post, baseUrl, accessToken, $"api/community/records/{recordId}/review", request, ct);

    public Task<IReadOnlyList<ManagedUserView>> GetUsersAsync(
        string baseUrl, string accessToken, string? query, CancellationToken ct = default)
    {
        var path = string.IsNullOrWhiteSpace(query)
            ? "api/community/users"
            : $"api/community/users?query={Uri.EscapeDataString(query.Trim())}";

        return GetListAsync<ManagedUserView>(baseUrl, accessToken, path, ct);
    }

    public Task<ManagedUserView> SetBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default)
        => SendAsync<BanRequest, ManagedUserView>(
            HttpMethod.Put, baseUrl, accessToken, $"api/community/users/{userId}/ban", request, ct);

    public Task<ManagedUserView> SetListBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default)
        => SendAsync<BanRequest, ManagedUserView>(
            HttpMethod.Put, baseUrl, accessToken, $"api/community/users/{userId}/list-ban", request, ct);

    private async Task<IReadOnlyList<T>> GetListAsync<T>(
        string baseUrl, string? accessToken, string path, CancellationToken ct)
        => await GetAsync<List<T>>(baseUrl, accessToken, path, ct);

    private async Task<T> GetAsync<T>(string baseUrl, string? accessToken, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, CloudHttp.Endpoint(baseUrl, path));

        // Часть списков читается и без входа: адрес демонлиста открыт всем,
        // а токен добавляется только там, где сервер спрашивает, кто пришёл.
        if (accessToken is not null)
            CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<T>(response, ct);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(
        HttpMethod method, string baseUrl, string accessToken, string path, TRequest payload, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, CloudHttp.Endpoint(baseUrl, path))
        {
            Content = JsonContent.Create(payload, options: CloudJson.Options),
        };
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
        return await CloudHttp.ReadAsync<TResponse>(response, ct);
    }

    /// <summary>Запрос без тела ответа: удаление отвечает 204.</summary>
    private async Task SendAsync(
        HttpMethod method, string baseUrl, string accessToken, string path, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, CloudHttp.Endpoint(baseUrl, path));
        CloudHttp.Authorize(request, accessToken);

        using var response = await CloudHttp.SendAsync(_http, request, ct);
        await CloudHttp.EnsureSuccessAsync(response, ct);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
            _http.Dispose();
    }
}
