using GdTracker.Core.Abstractions;
using GdTracker.Sharing;

namespace GdTracker.Cloud;

/// <summary>
/// Локальный прогресс глазами синхронизации. Реализуется слоем данных
/// (<c>GdTracker.Data.ProgressSharingService</c>) — тем же кодом, что собирает и
/// сливает пакеты при обмене файлами, поэтому облако и файл обмена ведут себя одинаково.
/// </summary>
public interface ILocalProgressStore
{
    /// <summary>Собирает пакет со всем локальным прогрессом.</summary>
    Task<ProgressPackage> CreatePackageAsync(CancellationToken ct = default);

    /// <summary>Вливает пакет в локальную базу без дублирования уровней и записей.</summary>
    Task<ImportSummary> MergePackageAsync(ProgressPackage package, CancellationToken ct = default);
}

/// <summary>Что сделала синхронизация.</summary>
/// <param name="LevelsAdded">Сколько уровней добавлено локально из облака.</param>
/// <param name="LevelsUpdated">Сколько уровней обновлено данными из облака.</param>
/// <param name="RecordsAdded">Сколько записей прогресса добавлено локально.</param>
/// <param name="Revision">Ревизия снимка в облаке после операции.</param>
/// <param name="SyncedAtUtc">Момент завершения.</param>
/// <param name="Uploaded">Отправлялись ли локальные данные в облако.</param>
public readonly record struct SyncOutcome(
    int LevelsAdded,
    int LevelsUpdated,
    int RecordsAdded,
    long Revision,
    DateTime SyncedAtUtc,
    bool Uploaded);

/// <summary>Синхронизация локального прогресса с облаком.</summary>
public interface ICloudSyncService
{
    /// <summary>Забирает облачные данные, сливает с локальными и отправляет результат обратно.</summary>
    Task<SyncOutcome> SyncAsync(CancellationToken ct = default);

    /// <summary>Забирает облачные данные и вливает их локально, ничего не отправляя.</summary>
    Task<SyncOutcome> PullAsync(CancellationToken ct = default);

    /// <summary>Отправляет локальные данные в облако, заменяя облачный снимок.</summary>
    Task<SyncOutcome> PushAsync(CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class CloudSyncService : ICloudSyncService
{
    private readonly ICloudClient _client;
    private readonly ICloudAccountService _account;
    private readonly ILocalProgressStore _local;
    private readonly ICloudSettingsStore _settings;

    public CloudSyncService(
        ICloudClient client,
        ICloudAccountService account,
        ILocalProgressStore local,
        ICloudSettingsStore settings)
    {
        _client = client;
        _account = account;
        _local = local;
        _settings = settings;
    }

    /// <summary>
    /// Сколько раз повторять цикл «забрать — слить — отправить» при конфликте ревизий.
    /// Конфликт означает, что между чтением и записью кто-то успел синхронизироваться
    /// с другого устройства; одного повтора хватает почти всегда, двух — с запасом.
    /// </summary>
    private const int ConflictRetries = 2;

    public async Task<SyncOutcome> SyncAsync(CancellationToken ct = default)
    {
        var token = _account.RequireAccessToken();

        for (var attempt = 0; ; attempt++)
        {
            var (summary, baseRevision) = await PullInternalAsync(token, ct);

            // Пакет собирается уже после слияния, поэтому в облако уходит объединённая
            // картина: и то, что пришло оттуда, и то, что было только здесь.
            var package = await _local.CreatePackageAsync(ct);

            try
            {
                var pushed = await Guarded(() => _client.PushSnapshotAsync(
                    _account.ServerUrl, token, baseRevision, package, ct));

                return Complete(summary, pushed.Revision, uploaded: true);
            }
            catch (CloudException e) when (e.Kind == CloudErrorKind.Conflict && attempt < ConflictRetries)
            {
                // Кто-то записал снимок между нашим чтением и записью — повторяем
                // цикл с новой ревизией; слияние идемпотентно, дублей не будет.
            }
        }
    }

    public async Task<SyncOutcome> PullAsync(CancellationToken ct = default)
    {
        var token = _account.RequireAccessToken();
        var (summary, revision) = await PullInternalAsync(token, ct);
        return Complete(summary, revision, uploaded: false);
    }

    public async Task<SyncOutcome> PushAsync(CancellationToken ct = default)
    {
        var token = _account.RequireAccessToken();

        // Узнаём текущую ревизию, не выкачивая сам снимок: отправка заменяет
        // облачные данные локальными, разбирать их содержимое незачем.
        var info = await Guarded(() => _account.GetAccountInfoAsync(ct));
        var package = await _local.CreatePackageAsync(ct);

        var pushed = await Guarded(() => _client.PushSnapshotAsync(
            _account.ServerUrl, token, info.SnapshotRevision, package, ct));

        return Complete(new ImportSummary(0, 0, 0), pushed.Revision, uploaded: true);
    }

    /// <summary>Забирает снимок и вливает его локально; возвращает итог слияния и ревизию облака.</summary>
    private async Task<(ImportSummary Summary, long Revision)> PullInternalAsync(string token, CancellationToken ct)
    {
        var remote = await Guarded(() => _client.GetSnapshotAsync(_account.ServerUrl, token, ct));

        // Снимка нет — в облаке пусто, сливать нечего, базовая ревизия нулевая.
        if (remote is null)
            return (new ImportSummary(0, 0, 0), 0);

        var summary = await _local.MergePackageAsync(remote.Package, ct);
        return (summary, remote.Revision);
    }

    private SyncOutcome Complete(ImportSummary summary, long revision, bool uploaded)
    {
        var now = DateTime.UtcNow;
        _settings.LastRevision = revision;
        _settings.LastSyncAtUtc = now;

        return new SyncOutcome(
            summary.LevelsAdded, summary.LevelsUpdated, summary.RecordsAdded, revision, now, uploaded);
    }

    /// <summary>
    /// Выполняет облачный вызов, превращая отказ авторизации в выход из аккаунта:
    /// держаться за отозванный токен смысла нет, а пользователю нужно понятное
    /// «войди снова», а не повторяющаяся ошибка на каждой кнопке.
    /// </summary>
    private async Task<T> Guarded<T>(Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (CloudException e) when (e.Kind == CloudErrorKind.Unauthorized)
        {
            _account.HandleUnauthorized();
            throw new CloudException(CloudErrorKind.Unauthorized, "Сессия истекла — войди в аккаунт заново.", e);
        }
    }
}
