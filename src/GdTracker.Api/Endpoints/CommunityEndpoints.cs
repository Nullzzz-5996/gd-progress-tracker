using System.Linq.Expressions;
using System.Security.Claims;
using GdTracker.Api.Data;
using GdTracker.Sharing.Cloud;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Endpoints;

/// <summary>
/// Демонлист и топ пройденных уровней.
///
/// Чтение открыто всем — список должен читаться и без аккаунта. Мнение о
/// размещении и заявку на рекорд пишет любой вошедший участник. Двигать демонов
/// и одобрять заявки может только модератор, банить — только владелец: права
/// проверяются по строке аккаунта в базе, а не по роли из токена, иначе снятые
/// права продолжали бы действовать до конца срока жизни уже выданного токена.
/// По той же причине там же проверяются и баны.
/// </summary>
public static class CommunityEndpoints
{
    /// <summary>Сколько мнений об одном демоне может оставить один аккаунт.</summary>
    private const int MaxOpinionsPerDemon = 1;

    public static void MapCommunityEndpoints(this WebApplication app)
    {
        var pub = app.MapGroup("/api/community");
        var auth = app.MapGroup("/api/community").RequireAuthorization();

        MapProfile(auth);
        MapUsers(auth);
        MapDemons(pub, auth);
        MapOpinions(pub, auth);
        MapRecords(pub, auth);
    }

    // ------------------------------------------------------------------ профиль

    private static void MapProfile(RouteGroupBuilder auth)
    {
        auth.MapGet("/me", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            return user is null ? failure! : Results.Ok(Profile(user));
        });

        auth.MapPut("/me", async (
            UpdateProfileRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            if (user is null) return failure!;

            if (Usernames.Validate(request.Username) is { } error)
                return Results.BadRequest(error);

            var username = request.Username.Trim();
            var normalized = Usernames.Normalize(username);

            if (await Usernames.IsTakenAsync(db, normalized, user.Id, ct))
                return Results.Conflict(new ApiError("username_taken", "Этот ник уже занят."));

            user.Username = username;
            user.UsernameNormalized = normalized;

            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Ник заняли между проверкой и записью — об этом сообщает уникальный индекс.
                return Results.Conflict(new ApiError("username_taken", "Этот ник уже занят."));
            }

            return Results.Ok(Profile(user));
        });

        // Выдача прав. Первый администратор появляется не отсюда, а из настройки
        // Community:Administrators — иначе назначать права было бы некому.
        auth.MapPut("/roles", async (
            SetRoleRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveUserAsync(principal, db, ct);
            if (actor is null) return failure!;
            if (actor.Role < UserRole.Administrator)
                return Forbidden("Выдавать права может только администратор или владелец.");

            // Роль владельца через API не выдаётся вовсе: право банить приходит
            // только из настройки Community:Owners. Остальные роли — строго ниже
            // своей, иначе администратор назначил бы себе равного, а тот — его
            // самого разжаловал.
            if (request.Role >= UserRole.Owner)
                return Forbidden("Роль владельца задаётся настройкой сервера, а не через API.");

            if (request.Role >= actor.Role)
                return Forbidden("Нельзя выдать роль наравне со своей или выше.");

            var email = EndpointHelpers.NormalizeEmail(request.Email ?? string.Empty);
            var target = await db.Users.FirstOrDefaultAsync(u => u.Email == email, ct);
            if (target is null)
                return Results.NotFound(new ApiError("user_not_found", "Аккаунта с такой почтой нет."));

            // Иначе единственный администратор мог бы случайно разжаловать сам себя
            // и оставить сообщество без того, кто выдаёт права.
            if (target.Id == actor.Id)
                return Results.BadRequest(new ApiError("self_role", "Свою роль менять нельзя."));

            if (target.Role >= actor.Role)
                return Forbidden("Этот аккаунт не ниже тебя по правам.");

            target.Role = request.Role;
            await db.SaveChangesAsync(ct);
            return Results.Ok(Profile(target));
        });
    }

    // ------------------------------------------------------- аккаунты и баны

    /// <summary>
    /// Хозяйство владельца: список аккаунтов и два вида бана. Полный бан
    /// закрывает вход и синхронизацию, бан в демонлисте оставляет и то и другое,
    /// но убирает участника из списка — ни мнений, ни заявок, а прежние записи
    /// пропадают из публичного топа. Оба снимаются тем же запросом с
    /// <c>banned = false</c>, поэтому бан обратим и ничего не стирает.
    /// </summary>
    private static void MapUsers(RouteGroupBuilder auth)
    {
        auth.MapGet("/users", async (
            string? query,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveOwnerAsync(principal, db, ct);
            if (actor is null) return failure!;

            var users = db.Users.AsNoTracking();

            // Поиск по нику и почте: список аккаунтов растёт, а забанить нужно
            // одного конкретного человека.
            if (!string.IsNullOrWhiteSpace(query))
            {
                var needle = query.Trim().ToLowerInvariant();
                users = users.Where(u =>
                    u.UsernameNormalized.Contains(needle) || u.Email.Contains(needle));
            }

            var rows = await users
                .OrderByDescending(u => u.Role)
                .ThenBy(u => u.UsernameNormalized)
                .Take(200)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(ToView));
        });

        auth.MapPut("/users/{id:guid}/ban", async (
            Guid id,
            BanRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
            await ApplyBanAsync(id, request, principal, db, listOnly: false, ct));

        auth.MapPut("/users/{id:guid}/list-ban", async (
            Guid id,
            BanRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
            await ApplyBanAsync(id, request, principal, db, listOnly: true, ct));
    }

    private static async Task<IResult> ApplyBanAsync(
        Guid id,
        BanRequest request,
        ClaimsPrincipal principal,
        ApiDbContext db,
        bool listOnly,
        CancellationToken ct)
    {
        var (actor, failure) = await ResolveOwnerAsync(principal, db, ct);
        if (actor is null) return failure!;

        var target = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (target is null)
            return Results.NotFound(new ApiError("user_not_found", "Такого аккаунта нет."));

        // Сам себя владелец не банит: иначе сервер остался бы без того, кто снимет бан.
        if (target.Id == actor.Id)
            return Results.BadRequest(new ApiError("self_ban", "Себя банить нельзя."));

        // Владельца не банит и другой владелец: спор двух владельцев решается
        // настройкой сервера, а не тем, кто успел нажать кнопку первым.
        if (target.Role >= actor.Role)
            return Forbidden("Этот аккаунт не ниже тебя по правам.");

        var reason = Shorten(request.Reason, 500);
        var now = DateTime.UtcNow;

        if (listOnly)
        {
            target.IsListBanned = request.Banned;
            target.ListBanReason = request.Banned ? reason : null;
            target.ListBannedAtUtc = request.Banned ? now : null;
        }
        else
        {
            target.IsBanned = request.Banned;
            target.BanReason = request.Banned ? reason : null;
            target.BannedAtUtc = request.Banned ? now : null;
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToView(target));
    }

    // ---------------------------------------------------------------- демонлист

    private static void MapDemons(RouteGroupBuilder pub, RouteGroupBuilder auth)
    {
        pub.MapGet("/demons", async (ApiDbContext db, CancellationToken ct) =>
        {
            var demons = await db.Demons.AsNoTracking().OrderBy(d => d.Position).ToListAsync(ct);

            // Счётчик мнений одним запросом: по запросу на демона получилось бы
            // двести обращений к базе на каждое открытие страницы.
            var counts = await db.Opinions.AsNoTracking()
                .GroupBy(o => o.DemonId)
                .Select(g => new { DemonId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.DemonId, x => x.Count, ct);

            return Results.Ok(demons.Select(d => ToView(d, counts.GetValueOrDefault(d.Id))));
        });

        auth.MapPut("/demons/{id:guid}/position", async (
            Guid id,
            MoveDemonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveModeratorAsync(principal, db, ct);
            if (actor is null) return failure!;

            var demon = await db.Demons.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (demon is null)
                return Results.NotFound(new ApiError("demon_not_found", "Такого демона в списке нет."));

            var total = await db.Demons.CountAsync(ct);
            var target = Math.Clamp(request.Position, 1, total);
            if (target == demon.Position)
                return Results.Ok(ToView(demon, await CountOpinionsAsync(db, demon.Id, ct)));

            // Демона вынимают с его места и вставляют на новое: всё, что лежит
            // между старым и новым местом, сдвигается на позицию навстречу.
            if (target < demon.Position)
            {
                var shifted = await db.Demons
                    .Where(d => d.Position >= target && d.Position < demon.Position)
                    .ToListAsync(ct);
                foreach (var other in shifted) other.Position++;
            }
            else
            {
                var shifted = await db.Demons
                    .Where(d => d.Position > demon.Position && d.Position <= target)
                    .ToListAsync(ct);
                foreach (var other in shifted) other.Position--;
            }

            demon.Position = target;
            demon.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToView(demon, await CountOpinionsAsync(db, demon.Id, ct)));
        });

        auth.MapPost("/demons", async (
            AddDemonRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveModeratorAsync(principal, db, ct);
            if (actor is null) return failure!;

            var name = (request.Name ?? string.Empty).Trim();
            if (name.Length is < 1 or > 200)
                return Results.BadRequest(new ApiError("invalid_name", "Укажи название уровня."));

            var total = await db.Demons.CountAsync(ct);
            // Новый демон может встать и в самый конец, поэтому верхняя граница — total + 1.
            var target = Math.Clamp(request.Position, 1, total + 1);

            var below = await db.Demons.Where(d => d.Position >= target).ToListAsync(ct);
            foreach (var other in below) other.Position++;

            var now = DateTime.UtcNow;
            var demon = new DemonListEntry
            {
                Position = target,
                Name = name,
                Publisher = (request.Publisher ?? string.Empty).Trim(),
                Verifier = (request.Verifier ?? string.Empty).Trim(),
                LevelId = request.LevelId,
                Video = Shorten(request.Video, 500),
                Thumbnail = Shorten(request.Thumbnail, 500),
                Requirement = Math.Clamp(request.Requirement ?? 100, 1, 100),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            };

            db.Demons.Add(demon);
            await db.SaveChangesAsync(ct);
            return Results.Ok(ToView(demon, 0));
        });

        auth.MapDelete("/demons/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveModeratorAsync(principal, db, ct);
            if (actor is null) return failure!;

            var demon = await db.Demons.FirstOrDefaultAsync(d => d.Id == id, ct);
            if (demon is null)
                return Results.NotFound(new ApiError("demon_not_found", "Такого демона в списке нет."));

            var below = await db.Demons.Where(d => d.Position > demon.Position).ToListAsync(ct);
            foreach (var other in below) other.Position--;

            db.Demons.Remove(demon);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    // ------------------------------------------------------------------- мнения

    private static void MapOpinions(RouteGroupBuilder pub, RouteGroupBuilder auth)
    {
        pub.MapGet("/demons/{id:guid}/opinions", async (Guid id, ApiDbContext db, CancellationToken ct) =>
        {
            // Забаненный в демонлисте из списка исчезает вместе со сказанным:
            // строки не удаляются, поэтому снятие бана возвращает их на место.
            var rows = await db.Opinions.AsNoTracking()
                .Where(o => o.DemonId == id)
                .Join(db.Users.AsNoTracking(), o => o.AuthorId, u => u.Id, (o, u) => new { Opinion = o, User = u })
                .Where(x => !x.User.IsBanned && !x.User.IsListBanned)
                .OrderByDescending(x => x.Opinion.CreatedAtUtc)
                .ToListAsync(ct);

            return Results.Ok(rows.Select(x => new OpinionView(
                x.Opinion.Id.ToString(),
                x.User.Id.ToString(),
                NameOf(x.User),
                x.User.Role,
                x.Opinion.SuggestedPosition,
                x.Opinion.Text,
                x.Opinion.CreatedAtUtc)));
        });

        auth.MapPost("/demons/{id:guid}/opinions", async (
            Guid id,
            OpinionRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            if (user is null) return failure!;
            if (user.IsListBanned) return ListBanned(user);

            if (!await db.Demons.AnyAsync(d => d.Id == id, ct))
                return Results.NotFound(new ApiError("demon_not_found", "Такого демона в списке нет."));

            var text = (request.Text ?? string.Empty).Trim();
            if (text.Length is < 3 or > 1000)
                return Results.BadRequest(new ApiError("invalid_text", "Мнение должно быть от 3 до 1000 символов."));

            int? suggested = null;
            if (request.SuggestedPosition is { } raw)
            {
                var total = await db.Demons.CountAsync(ct);
                if (raw < 1 || raw > total)
                    return Results.BadRequest(new ApiError("invalid_position", $"Место должно быть от 1 до {total}."));
                suggested = raw;
            }

            // Одно мнение на демона от аккаунта: это обсуждение места, а не лента комментариев.
            var already = await db.Opinions.CountAsync(o => o.DemonId == id && o.AuthorId == user.Id, ct);
            if (already >= MaxOpinionsPerDemon)
                return Results.Conflict(new ApiError(
                    "opinion_exists",
                    "Ты уже высказался об этом демоне — удали прежнее мнение, чтобы написать новое."));

            var opinion = new PlacementOpinion
            {
                DemonId = id,
                AuthorId = user.Id,
                SuggestedPosition = suggested,
                Text = text,
                CreatedAtUtc = DateTime.UtcNow,
            };

            db.Opinions.Add(opinion);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new OpinionView(
                opinion.Id.ToString(),
                user.Id.ToString(),
                NameOf(user),
                user.Role,
                opinion.SuggestedPosition,
                opinion.Text,
                opinion.CreatedAtUtc));
        });

        auth.MapDelete("/opinions/{id:guid}", async (
            Guid id,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            if (user is null) return failure!;

            var opinion = await db.Opinions.FirstOrDefaultAsync(o => o.Id == id, ct);
            if (opinion is null)
                return Results.NotFound(new ApiError("opinion_not_found", "Такого мнения нет."));

            // Своё мнение убирает автор, чужое — модератор.
            if (opinion.AuthorId != user.Id && !CanModerate(user))
                return Forbidden("Удалить можно только своё мнение.");

            db.Opinions.Remove(opinion);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    // --------------------------------------------------- топ пройденных уровней

    private static void MapRecords(RouteGroupBuilder pub, RouteGroupBuilder auth)
    {
        // Публичный топ — только одобренные заявки, в порядке проставленных мест.
        pub.MapGet("/records", async (ApiDbContext db, CancellationToken ct) =>
        {
            var rows = await LoadRecordsAsync(db, r => r.Status == RecordStatus.Approved, ct, hideBanned: true);
            return Results.Ok(rows
                .OrderBy(r => r.Placement ?? int.MaxValue)
                .ThenBy(r => r.SubmittedAtUtc));
        });

        auth.MapGet("/records/mine", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            if (user is null) return failure!;

            var rows = await LoadRecordsAsync(db, r => r.UserId == user.Id, ct);
            return Results.Ok(rows.OrderByDescending(r => r.SubmittedAtUtc));
        });

        auth.MapGet("/records/pending", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveModeratorAsync(principal, db, ct);
            if (actor is null) return failure!;

            var rows = await LoadRecordsAsync(db, r => r.Status == RecordStatus.Pending, ct);
            return Results.Ok(rows.OrderBy(r => r.SubmittedAtUtc));
        });

        auth.MapPost("/records", async (
            SubmitRecordRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (user, failure) = await ResolveUserAsync(principal, db, ct);
            if (user is null) return failure!;
            if (user.IsListBanned) return ListBanned(user);

            // Видео — единственное доказательство: без него заявку не по чему смотреть.
            var video = (request.VideoUrl ?? string.Empty).Trim();
            if (!IsHttpUrl(video))
                return Results.BadRequest(new ApiError("invalid_video", "Нужна ссылка на видео (http или https)."));

            if (!request.HasClicks)
                return Results.BadRequest(new ApiError(
                    "clicks_required",
                    "Заявка принимается только с видео, на котором слышны или видны клики."));

            DemonListEntry? demon = null;
            if (!string.IsNullOrWhiteSpace(request.DemonId))
            {
                if (!Guid.TryParse(request.DemonId, out var demonId))
                    return Results.BadRequest(new ApiError("invalid_demon", "Неверный идентификатор демона."));

                demon = await db.Demons.AsNoTracking().FirstOrDefaultAsync(d => d.Id == demonId, ct);
                if (demon is null)
                    return Results.NotFound(new ApiError("demon_not_found", "Такого демона в списке нет."));
            }

            var levelName = (request.LevelName ?? demon?.Name ?? string.Empty).Trim();
            if (levelName.Length is < 1 or > 200)
                return Results.BadRequest(new ApiError("invalid_level", "Укажи название уровня."));

            if (request.Progress is < 1 or > 100)
                return Results.BadRequest(new ApiError("invalid_progress", "Процент должен быть от 1 до 100."));

            if (demon is not null && request.Progress < demon.Requirement)
                return Results.BadRequest(new ApiError(
                    "below_requirement",
                    $"Для этого демона засчитываются рекорды от {demon.Requirement}%."));

            var duplicate = await db.Records.AnyAsync(
                r => r.UserId == user.Id && r.Status == RecordStatus.Pending && r.LevelName == levelName,
                ct);
            if (duplicate)
                return Results.Conflict(new ApiError(
                    "already_pending", "Заявка по этому уровню уже ждёт рассмотрения."));

            var record = new RecordSubmission
            {
                UserId = user.Id,
                DemonId = demon?.Id,
                LevelName = levelName,
                Progress = request.Progress,
                VideoUrl = video,
                HasClicks = true,
                Comment = Shorten(request.Comment, 1000),
                Status = RecordStatus.Pending,
                SubmittedAtUtc = DateTime.UtcNow,
            };

            db.Records.Add(record);
            await db.SaveChangesAsync(ct);

            return Results.Ok(ToView(record, user, demon?.Position));
        });

        auth.MapPost("/records/{id:guid}/review", async (
            Guid id,
            ReviewRecordRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            var (actor, failure) = await ResolveModeratorAsync(principal, db, ct);
            if (actor is null) return failure!;

            var record = await db.Records.FirstOrDefaultAsync(r => r.Id == id, ct);
            if (record is null)
                return Results.NotFound(new ApiError("record_not_found", "Заявки с таким номером нет."));

            record.ReviewedAtUtc = DateTime.UtcNow;
            record.ReviewedById = actor.Id;
            record.ReviewNote = Shorten(request.Note, 1000);

            if (!request.Approve)
            {
                record.Status = RecordStatus.Rejected;
                // Отклонённая заявка места в топе не занимает: если она там уже стояла,
                // место освобождается, а идущие ниже подтягиваются вверх.
                if (record.Placement is { } freed)
                {
                    record.Placement = null;
                    await CloseTopGapAsync(db, freed, ct);
                }
            }
            else
            {
                var approvedCount = await db.Records
                    .CountAsync(r => r.Status == RecordStatus.Approved && r.Id != record.Id, ct);

                // Место не указано — рекорд встаёт в конец топа.
                var target = Math.Clamp(request.Placement ?? (approvedCount + 1), 1, approvedCount + 1);

                // Заявку могут пересматривать повторно: если она уже стояла в топе,
                // сначала закрываем её прежнее место, иначе в топе останется дыра.
                if (record.Placement is { } previous)
                {
                    record.Placement = null;
                    await CloseTopGapAsync(db, previous, ct);
                }

                var below = await db.Records
                    .Where(r => r.Status == RecordStatus.Approved
                                && r.Id != record.Id
                                && r.Placement != null
                                && r.Placement >= target)
                    .ToListAsync(ct);
                foreach (var other in below) other.Placement++;

                record.Status = RecordStatus.Approved;
                record.Placement = target;
            }

            await db.SaveChangesAsync(ct);

            var player = await db.Users.AsNoTracking().FirstAsync(u => u.Id == record.UserId, ct);
            var position = record.DemonId is { } demonId
                ? await db.Demons.AsNoTracking()
                    .Where(d => d.Id == demonId)
                    .Select(d => (int?)d.Position)
                    .FirstOrDefaultAsync(ct)
                : null;

            return Results.Ok(ToView(record, player, position));
        });
    }

    // ----------------------------------------------------------- вспомогательное

    /// <summary>Сдвигает топ вверх, закрывая освободившееся место.</summary>
    private static async Task CloseTopGapAsync(ApiDbContext db, int freed, CancellationToken ct)
    {
        var below = await db.Records
            .Where(r => r.Status == RecordStatus.Approved && r.Placement != null && r.Placement > freed)
            .ToListAsync(ct);
        foreach (var other in below) other.Placement--;
    }

    /// <param name="hideBanned">
    /// Убрать записи забаненных. Публичный топ их не показывает; модератору в
    /// очереди и автору в его же заявках они видны — иначе заявка забаненного
    /// зависла бы непросмотренной навсегда.
    /// </param>
    private static async Task<List<RecordView>> LoadRecordsAsync(
        ApiDbContext db,
        Expression<Func<RecordSubmission, bool>> filter,
        CancellationToken ct,
        bool hideBanned = false)
    {
        var joined = db.Records.AsNoTracking()
            .Where(filter)
            .Join(db.Users.AsNoTracking(), r => r.UserId, u => u.Id, (r, u) => new { Record = r, User = u });

        if (hideBanned)
            joined = joined.Where(x => !x.User.IsBanned && !x.User.IsListBanned);

        var rows = await joined.ToListAsync(ct);

        var demonIds = rows.Select(x => x.Record.DemonId).OfType<Guid>().Distinct().ToList();
        var positions = demonIds.Count == 0
            ? new Dictionary<Guid, int>()
            : await db.Demons.AsNoTracking()
                .Where(d => demonIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Position, ct);

        return rows
            .Select(x => ToView(
                x.Record,
                x.User,
                x.Record.DemonId is { } id && positions.TryGetValue(id, out var p) ? p : null))
            .ToList();
    }

    private static Task<int> CountOpinionsAsync(ApiDbContext db, Guid demonId, CancellationToken ct) =>
        db.Opinions.CountAsync(o => o.DemonId == demonId, ct);

    private static DemonView ToView(DemonListEntry d, int opinionCount) => new(
        d.Id.ToString(),
        d.Position,
        d.Name,
        d.Publisher,
        d.Verifier,
        d.LevelId,
        d.Video,
        d.Thumbnail,
        d.Requirement,
        opinionCount);

    private static RecordView ToView(RecordSubmission r, UserAccount player, int? demonPosition) => new(
        r.Id.ToString(),
        player.Id.ToString(),
        NameOf(player),
        player.Role,
        r.DemonId?.ToString(),
        demonPosition,
        r.LevelName,
        r.Progress,
        r.VideoUrl,
        r.HasClicks,
        r.Comment,
        r.Status,
        r.Placement,
        r.SubmittedAtUtc,
        r.ReviewedAtUtc,
        r.ReviewNote);

    private static CommunityProfile Profile(UserAccount user) => new(
        user.Id.ToString(),
        user.Email,
        NameOf(user),
        user.Role,
        CanModerate(user),
        CanBan(user),
        user.IsListBanned,
        user.ListBanReason);

    private static ManagedUserView ToView(UserAccount user) => new(
        user.Id.ToString(),
        user.Email,
        NameOf(user),
        user.Role,
        user.IsBanned,
        user.BanReason,
        user.BannedAtUtc,
        user.IsListBanned,
        user.ListBanReason,
        user.ListBannedAtUtc,
        user.CreatedAtUtc);

    /// <summary>
    /// Ник для показа. Обычно он заполнен всегда — при регистрации и при
    /// обновлении схемы. Запасной вариант остаётся на случай строки, дописанной
    /// в базу руками: показывать в публичном списке пустоту или адрес почты нельзя.
    /// </summary>
    public static string NameOf(UserAccount user)
    {
        if (!string.IsNullOrWhiteSpace(user.Username))
            return user.Username;

        var at = user.Email.IndexOf('@');
        return at > 0 ? user.Email[..at] : user.Email;
    }

    /// <summary>Правит топ-200 и топ пройденных: модератор, администратор и владелец.</summary>
    private static bool CanModerate(UserAccount user) => user.Role >= UserRole.Moderator;

    /// <summary>Банит аккаунты и раздаёт баны в демонлисте: только владелец.</summary>
    private static bool CanBan(UserAccount user) => user.Role >= UserRole.Owner;

    /// <summary>Аккаунт из токена. Второй элемент заполнен, только если аккаунта нет.</summary>
    private static async Task<(UserAccount? User, IResult? Failure)> ResolveUserAsync(
        ClaimsPrincipal principal,
        ApiDbContext db,
        CancellationToken ct)
    {
        if (EndpointHelpers.UserId(principal) is not { } userId)
            return (null, Results.Unauthorized());

        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
        // Токен ещё живой, а аккаунт уже удалён: это не «доступ запрещён», а «входа нет».
        if (user is null)
            return (null, Results.Unauthorized());

        // Забаненному токен не отзывают — его отклоняет каждый запрос, пока бан
        // не снимут. Иначе выданный до бана токен работал бы ещё месяц.
        return user.IsBanned ? (null, EndpointHelpers.BannedResult(user)) : (user, null);
    }

    private static async Task<(UserAccount? User, IResult? Failure)> ResolveModeratorAsync(
        ClaimsPrincipal principal,
        ApiDbContext db,
        CancellationToken ct)
    {
        var (user, failure) = await ResolveUserAsync(principal, db, ct);
        if (user is null)
            return (null, failure);

        return CanModerate(user)
            ? (user, null)
            : (null, Forbidden("Это может делать только аккаунт с правами модератора."));
    }

    /// <summary>Владелец из токена: всё, что касается банов, проходит через него.</summary>
    private static async Task<(UserAccount? User, IResult? Failure)> ResolveOwnerAsync(
        ClaimsPrincipal principal,
        ApiDbContext db,
        CancellationToken ct)
    {
        var (user, failure) = await ResolveUserAsync(principal, db, ct);
        if (user is null)
            return (null, failure);

        return CanBan(user)
            ? (user, null)
            : (null, Forbidden("Банить может только владелец."));
    }

    private static IResult Forbidden(string message) =>
        Results.Json(new ApiError("forbidden", message), statusCode: StatusCodes.Status403Forbidden);

    /// <summary>Отказ участнику, забаненному в демонлисте.</summary>
    private static IResult ListBanned(UserAccount user) => Results.Json(
        new ApiError(
            BanCodes.DemonList,
            string.IsNullOrWhiteSpace(user.ListBanReason)
                ? "Ты забанен в демонлисте: читать список можно, участвовать — нет."
                : $"Ты забанен в демонлисте: {user.ListBanReason}"),
        statusCode: StatusCodes.Status403Forbidden);

    private static bool IsHttpUrl(string value) =>
        value.Length <= 500
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string? Shorten(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= max ? trimmed : trimmed[..max];
    }
}
