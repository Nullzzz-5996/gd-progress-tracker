using System.Text.Json;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Cloud;

/// <summary>
/// Список демонов и когда он получен. <see cref="CachedAtUtc"/> заполнено только
/// у списка, поднятого из локальной копии: значит, сервер не ответил и вкладка
/// показывает последнюю известную расстановку, а не текущую.
/// </summary>
public sealed record DemonListSnapshot(IReadOnlyList<DemonView> Demons, DateTime? CachedAtUtc)
{
    public bool FromCache => CachedAtUtc is not null;
}

/// <summary>
/// Локальная копия списка демонов. Нужна ровно для одного: показать вкладку,
/// когда сервера нет под рукой. Ничего, кроме последнего успешно прочитанного
/// списка, здесь не хранится — мнения и заявки без сервера бессмысленны.
/// </summary>
public interface IDemonListCache
{
    DemonListSnapshot? Load();

    void Save(IReadOnlyList<DemonView> demons);
}

/// <summary>Копия списка в JSON-файле рядом с настройками облака.</summary>
public sealed class DemonListCache : IDemonListCache
{
    private static readonly JsonSerializerOptions FileJson = new(CloudJson.Options) { WriteIndented = false };

    private readonly string _filePath;

    public DemonListCache() : this(Path.Combine(CloudPaths.AppDataDir, "demonlist.json"))
    {
    }

    public DemonListCache(string filePath) => _filePath = filePath;

    public DemonListSnapshot? Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return null;

            var file = JsonSerializer.Deserialize<CachedList>(File.ReadAllText(_filePath), FileJson);
            if (file is null || file.Demons.Count == 0)
                return null;

            return new DemonListSnapshot(file.Demons, file.SavedAtUtc);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            // Битая копия — то же самое, что её отсутствие: вкладка скажет, что
            // сервер недоступен, и на этом всё.
            return null;
        }
    }

    public void Save(IReadOnlyList<DemonView> demons)
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var payload = new CachedList(DateTime.UtcNow, demons.ToList());
            File.WriteAllText(_filePath, JsonSerializer.Serialize(payload, FileJson));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Не сохранили копию — не беда: список только что прочитан с сервера.
        }
    }

    private sealed record CachedList(DateTime SavedAtUtc, List<DemonView> Demons);
}

/// <summary>
/// Демонлист в приложении. Берёт на себя то, чего не знает транспорт: адрес
/// сервера, токен вошедшего пользователя, сброс сессии по 401 и локальную копию
/// списка на случай недоступного сервера.
///
/// Данные общие с сайтом: и вкладка, и страница демонлиста читают один и тот же
/// сервер, поэтому перестановка, сделанная модератором в приложении, тут же видна
/// на сайте, и наоборот.
/// </summary>
public interface ICommunityService
{
    /// <summary>
    /// Список демонов. Если сервер недоступен, возвращает последнюю сохранённую
    /// копию (<see cref="DemonListSnapshot.FromCache"/>), а не бросает исключение;
    /// когда копии нет, сетевая ошибка всплывает как обычно.
    /// </summary>
    Task<DemonListSnapshot> GetDemonsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<OpinionView>> GetOpinionsAsync(string demonId, CancellationToken ct = default);

    Task<OpinionView> PostOpinionAsync(
        string demonId, string text, int? suggestedPosition, CancellationToken ct = default);

    Task DeleteOpinionAsync(string opinionId, CancellationToken ct = default);

    Task<DemonView> MoveDemonAsync(string demonId, int position, CancellationToken ct = default);

    Task<DemonView> AddDemonAsync(AddDemonRequest request, CancellationToken ct = default);

    Task DeleteDemonAsync(string demonId, CancellationToken ct = default);

    /// <summary>Права и бан вошедшего пользователя в демонлисте.</summary>
    Task<CommunityProfile> GetProfileAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RecordView>> GetTopRecordsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RecordView>> GetMyRecordsAsync(CancellationToken ct = default);

    Task<IReadOnlyList<RecordView>> GetPendingRecordsAsync(CancellationToken ct = default);

    Task<RecordView> SubmitRecordAsync(SubmitRecordRequest request, CancellationToken ct = default);

    Task<RecordView> ReviewRecordAsync(string recordId, ReviewRecordRequest request, CancellationToken ct = default);

    Task<IReadOnlyList<ManagedUserView>> GetUsersAsync(string? query, CancellationToken ct = default);

    Task<ManagedUserView> SetBanAsync(string userId, bool banned, string? reason, CancellationToken ct = default);

    Task<ManagedUserView> SetListBanAsync(string userId, bool banned, string? reason, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CommunityService : ICommunityService
{
    private readonly ICommunityClient _client;
    private readonly ICloudAccountService _account;
    private readonly IDemonListCache _cache;

    public CommunityService(ICommunityClient client, ICloudAccountService account, IDemonListCache cache)
    {
        _client = client;
        _account = account;
        _cache = cache;
    }

    private string ServerUrl => _account.ServerUrl;

    public async Task<DemonListSnapshot> GetDemonsAsync(CancellationToken ct = default)
    {
        try
        {
            var demons = await _client.GetDemonsAsync(ServerUrl, ct);
            _cache.Save(demons);
            return new DemonListSnapshot(demons, CachedAtUtc: null);
        }
        catch (CloudException e) when (e.Kind is CloudErrorKind.Network or CloudErrorKind.Server)
        {
            // Сервер лежит или его нет в сети — показываем сохранённую расстановку,
            // как это делает страница сайта со стартовым снимком. Копии нет —
            // скрывать сетевую ошибку нечем, и она всплывает как обычно.
            var cached = _cache.Load();
            if (cached is null)
                throw;

            return cached;
        }
    }

    public Task<IReadOnlyList<OpinionView>> GetOpinionsAsync(string demonId, CancellationToken ct = default)
        => _client.GetOpinionsAsync(ServerUrl, demonId, ct);

    public Task<OpinionView> PostOpinionAsync(
        string demonId, string text, int? suggestedPosition, CancellationToken ct = default)
        => Guarded(token => _client.PostOpinionAsync(
            ServerUrl, token, demonId, new OpinionRequest(text, suggestedPosition), ct));

    public Task DeleteOpinionAsync(string opinionId, CancellationToken ct = default)
        => Guarded(token => _client.DeleteOpinionAsync(ServerUrl, token, opinionId, ct));

    public Task<DemonView> MoveDemonAsync(string demonId, int position, CancellationToken ct = default)
        => Guarded(token => _client.MoveDemonAsync(ServerUrl, token, demonId, position, ct));

    public Task<DemonView> AddDemonAsync(AddDemonRequest request, CancellationToken ct = default)
        => Guarded(token => _client.AddDemonAsync(ServerUrl, token, request, ct));

    public Task DeleteDemonAsync(string demonId, CancellationToken ct = default)
        => Guarded(token => _client.DeleteDemonAsync(ServerUrl, token, demonId, ct));

    public Task<CommunityProfile> GetProfileAsync(CancellationToken ct = default)
        => Guarded(token => _client.GetProfileAsync(ServerUrl, token, ct));

    public Task<IReadOnlyList<RecordView>> GetTopRecordsAsync(CancellationToken ct = default)
        => _client.GetTopRecordsAsync(ServerUrl, ct);

    public Task<IReadOnlyList<RecordView>> GetMyRecordsAsync(CancellationToken ct = default)
        => Guarded(token => _client.GetMyRecordsAsync(ServerUrl, token, ct));

    public Task<IReadOnlyList<RecordView>> GetPendingRecordsAsync(CancellationToken ct = default)
        => Guarded(token => _client.GetPendingRecordsAsync(ServerUrl, token, ct));

    public Task<RecordView> SubmitRecordAsync(SubmitRecordRequest request, CancellationToken ct = default)
        => Guarded(token => _client.SubmitRecordAsync(ServerUrl, token, request, ct));

    public Task<RecordView> ReviewRecordAsync(
        string recordId, ReviewRecordRequest request, CancellationToken ct = default)
        => Guarded(token => _client.ReviewRecordAsync(ServerUrl, token, recordId, request, ct));

    public Task<IReadOnlyList<ManagedUserView>> GetUsersAsync(string? query, CancellationToken ct = default)
        => Guarded(token => _client.GetUsersAsync(ServerUrl, token, query, ct));

    public Task<ManagedUserView> SetBanAsync(
        string userId, bool banned, string? reason, CancellationToken ct = default)
        => Guarded(token => _client.SetBanAsync(ServerUrl, token, userId, new BanRequest(banned, reason), ct));

    public Task<ManagedUserView> SetListBanAsync(
        string userId, bool banned, string? reason, CancellationToken ct = default)
        => Guarded(token => _client.SetListBanAsync(ServerUrl, token, userId, new BanRequest(banned, reason), ct));

    /// <summary>
    /// Запрос от имени вошедшего пользователя. Токен берётся до запроса: если
    /// входа нет, отказ приходит сразу и с внятным текстом, а не после похода
    /// в сеть. То же, что в синхронизации прогресса: отказ сервера по токену
    /// сбрасывает сессию, чтобы пользователь увидел «войди снова», а не
    /// повторяющуюся ошибку на каждой кнопке вкладки.
    /// </summary>
    private async Task<T> Guarded<T>(Func<string, Task<T>> call)
    {
        var token = _account.RequireAccessToken();

        try
        {
            return await call(token);
        }
        catch (CloudException e) when (e.Kind == CloudErrorKind.Unauthorized)
        {
            _account.HandleUnauthorized();
            throw new CloudException(CloudErrorKind.Unauthorized, "Сессия истекла — войди в аккаунт заново.", e);
        }
    }

    private async Task Guarded(Func<string, Task> call)
    {
        var token = _account.RequireAccessToken();

        try
        {
            await call(token);
        }
        catch (CloudException e) when (e.Kind == CloudErrorKind.Unauthorized)
        {
            _account.HandleUnauthorized();
            throw new CloudException(CloudErrorKind.Unauthorized, "Сессия истекла — войди в аккаунт заново.", e);
        }
    }
}
