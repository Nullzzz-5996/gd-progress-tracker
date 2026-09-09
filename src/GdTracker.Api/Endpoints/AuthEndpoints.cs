using System.Security.Claims;
using GdTracker.Api.Data;
using GdTracker.Api.Security;
using GdTracker.Sharing.Cloud;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Endpoints;

/// <summary>Регистрация, вход и сведения об аккаунте.</summary>
public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth").RequireRateLimiting("auth");

        auth.MapPost("/register", async (
            RegisterRequest request,
            ApiDbContext db,
            ITokenIssuer tokens,
            RoleBootstrap roles,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.ValidateCredentials(request.Email, request.Password) is { } error)
                return Results.BadRequest(error);

            var email = EndpointHelpers.NormalizeEmail(request.Email);
            if (await db.Users.AnyAsync(u => u.Email == email, ct))
                return Results.Conflict(new ApiError("email_taken", "Аккаунт с такой почтой уже есть."));

            // Ник прислали — проверяем его; не прислали (старый клиент) — выводим из почты,
            // чтобы аккаунт всё равно был подписан именем, а не пустотой.
            string username;
            if (!string.IsNullOrWhiteSpace(request.Username))
            {
                if (Usernames.Validate(request.Username) is { } nameError)
                    return Results.BadRequest(nameError);

                username = request.Username.Trim();
                if (await Usernames.IsTakenAsync(db, Usernames.Normalize(username), null, ct))
                    return Results.Conflict(new ApiError("username_taken", "Этот ник уже занят."));
            }
            else
            {
                username = await Usernames.DeriveAsync(db, email, ct);
            }

            var user = new UserAccount
            {
                Email = email,
                PasswordHash = PasswordHasher.Hash(request.Password),
                CreatedAtUtc = DateTime.UtcNow,
                Username = username,
                UsernameNormalized = Usernames.Normalize(username),
            };

            // Адрес мог быть заранее вписан в список модераторов сервера:
            // тогда права появляются сразу, а не после перезапуска.
            roles.Apply(user);

            db.Users.Add(user);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Уникальные индексы по почте и по нику: две одновременные регистрации
                // доходят до этой точки обе, но записаться должна только первая.
                return Results.Conflict(new ApiError(
                    "account_taken", "Такая почта или такой ник уже заняты."));
            }

            return Results.Ok(Authenticated(user, tokens));
        });

        auth.MapPost("/login", async (
            LoginRequest request,
            ApiDbContext db,
            ITokenIssuer tokens,
            CancellationToken ct) =>
        {
            // Длину пароля проверяем до обращения к базе и до хеширования: PBKDF2 на
            // мегабайтном «пароле» — это подаренная злоумышленнику нагрузка на сервер.
            if ((request.Password?.Length ?? 0) > EndpointHelpers.MaxPasswordLength)
                return Results.Json(
                    new ApiError("invalid_credentials", "Неверный ник, почта или пароль."),
                    statusCode: StatusCodes.Status401Unauthorized);

            // Входят по нику или по почте: ник помнят про себя, почта нужна была
            // при регистрации. Нормализация у них общая, поэтому одно значение
            // сравнивается с обеими колонками, а совпасть они не могут — «@» в
            // нике запрещён правилами имён.
            var login = EndpointHelpers.NormalizeLogin(request.Login ?? string.Empty);
            var user = await db.Users.FirstOrDefaultAsync(
                u => u.Email == login || u.UsernameNormalized == login, ct);

            // Один и тот же ответ на «нет такого аккаунта» и «неверный пароль»:
            // иначе API превращается в проверялку существующих ников и адресов почты.
            if (user is null || !PasswordHasher.Verify(request.Password ?? string.Empty, user.PasswordHash))
                return Results.Json(
                    new ApiError("invalid_credentials", "Неверный ник, почта или пароль."),
                    statusCode: StatusCodes.Status401Unauthorized);

            // Про бан сообщаем только тому, кто прошёл проверку пароля: иначе API
            // рассказывал бы посторонним, какие аккаунты заблокированы.
            if (user.IsBanned)
                return EndpointHelpers.BannedResult(user);

            return Results.Ok(Authenticated(user, tokens));
        });

        var account = app.MapGroup("/api/account").RequireAuthorization();

        account.MapGet("/me", async (ClaimsPrincipal principal, ApiDbContext db, CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.Unauthorized();

            // Токен забаненного ещё жив, но работать он не должен.
            if (user.IsBanned)
                return EndpointHelpers.BannedResult(user);

            var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
            return Results.Ok(Info(user, snapshot));
        });

        // Смена ника. Ник публичный, поэтому занятость проверяется так же строго,
        // как при регистрации: два одинаковых имени в топе неразличимы.
        account.MapPut("/username", async (
            ChangeUsernameRequest request,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.Unauthorized();

            if (user.IsBanned)
                return EndpointHelpers.BannedResult(user);

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
                return Results.Conflict(new ApiError("username_taken", "Этот ник уже занят."));
            }

            var snapshot = await db.Snapshots.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, ct);
            return Results.Ok(Info(user, snapshot));
        });

        // Удаление аккаунта вместе с облачным снимком. Пароль запрашивается ещё раз:
        // одного украденного токена не должно хватать, чтобы стереть чужие данные.
        account.MapDelete("", async (
            [FromBody] LoginRequest confirmation,
            ClaimsPrincipal principal,
            ApiDbContext db,
            CancellationToken ct) =>
        {
            if (EndpointHelpers.UserId(principal) is not { } userId)
                return Results.Unauthorized();

            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (user is null)
                return Results.Unauthorized();

            if (user.IsBanned)
                return EndpointHelpers.BannedResult(user);

            if (!PasswordHasher.Verify(confirmation.Password ?? string.Empty, user.PasswordHash))
                return Results.Json(
                    new ApiError("invalid_credentials", "Неверный пароль."),
                    statusCode: StatusCodes.Status401Unauthorized);

            db.Users.Remove(user);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    /// <summary>Ответ на вход и регистрацию: токен вместе с ником и ролью.</summary>
    private static AuthResponse Authenticated(UserAccount user, ITokenIssuer tokens)
    {
        var (token, expires) = tokens.Issue(user);
        return new AuthResponse(user.Id.ToString(), user.Email, user.Username, user.Role, token, expires);
    }

    private static AccountInfo Info(UserAccount user, ProgressSnapshot? snapshot) => new(
        user.Id.ToString(),
        user.Email,
        user.Username,
        user.Role,
        user.CreatedAtUtc,
        snapshot?.Revision ?? 0,
        snapshot?.UpdatedAtUtc);
}
