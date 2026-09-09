using System.Security.Claims;
using System.Text.Json;
using GdTracker.Api.Data;
using GdTracker.Sharing;
using GdTracker.Sharing.Cloud;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Endpoints;

/// <summary>
/// Хранение снимка прогресса. Сервер держит один снимок на аккаунт и следит только
/// за ревизией: слияние данных — забота клиента, который умеет объединять уровни
/// и записи без дублей (та же логика, что при импорте файла обмена).
/// </summary>
public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this WebApplication app)
    {
        var sync = app.MapGroup("/api/sync").RequireAuthorization();

        sync.MapGet("/snapshot", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            if (await BanGuardAsync(db, userId, ct) is { } banned)
                return banned;

            var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (snapshot is null)
                return Results.NoContent();

            ProgressPackage package;
            try
            {
                package = JsonSerializer.Deserialize<ProgressPackage>(snapshot.PayloadJson, CloudJson.Options)
                          ?? new ProgressPackage();
            }
            catch (JsonException)
            {
                // Снимок в базе испорчен: честнее сказать об этом, чем отдать пустой
                // пакет, который клиент воспримет как «в облаке ничего нет».
                return Results.Json(
                    new ApiError("corrupted_snapshot", "Снимок в облаке повреждён — загрузи прогресс заново."),
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            return Results.Ok(new SnapshotResponse(snapshot.Revision, snapshot.UpdatedAtUtc, package));
        });

        sync.MapPut("/snapshot", async (
            PushSnapshotRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            if (await BanGuardAsync(db, userId, ct) is { } banned)
                return banned;

            if (request.Package is null)
                return Results.BadRequest(new ApiError("empty_package", "Пакет прогресса отсутствует."));

            var snapshot = await db.Snapshots.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            var currentRevision = snapshot?.Revision ?? 0;

            // Оптимистическая блокировка: клиент присылает ревизию, на которой
            // основан пакет. Если в облаке уже другая — данные с другого устройства
            // пропали бы при записи, поэтому просим клиента сначала забрать их.
            if (request.BaseRevision != currentRevision)
                return Results.Conflict(new ApiError(
                    "revision_conflict",
                    "В облаке появились изменения с другого устройства — сначала синхронизируй их."));

            var payload = JsonSerializer.Serialize(request.Package, CloudJson.Options);
            var now = DateTime.UtcNow;

            if (snapshot is null)
            {
                snapshot = new ProgressSnapshot { UserId = userId };
                db.Snapshots.Add(snapshot);
            }

            snapshot.Revision = currentRevision + 1;
            snapshot.UpdatedAtUtc = now;
            snapshot.PayloadJson = payload;

            await db.SaveChangesAsync(ct);
            return Results.Ok(new PushSnapshotResponse(snapshot.Revision, snapshot.UpdatedAtUtc));
        });

        // Удаление данных из облака без удаления самого аккаунта.
        sync.MapDelete("/snapshot", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            if (await BanGuardAsync(db, userId, ct) is { } banned)
                return banned;

            var snapshot = await db.Snapshots.FirstOrDefaultAsync(s => s.UserId == userId, ct);
            if (snapshot is not null)
            {
                db.Snapshots.Remove(snapshot);
                await db.SaveChangesAsync(ct);
            }

            return Results.NoContent();
        });
    }

    /// <summary>
    /// Забаненному облако закрыто целиком: ни забрать снимок, ни записать.
    /// Возвращает null, если бана нет и запрос можно выполнять.
    /// </summary>
    private static async Task<IResult?> BanGuardAsync(ApiDbContext db, Guid userId, CancellationToken ct)
    {
        var banned = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.IsBanned, ct);

        return banned is null ? null : EndpointHelpers.BannedResult(banned);
    }
}
