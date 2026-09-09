using GdTracker.Sharing.Cloud;
using GdTracker.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace GdTracker.Api.Security;

/// <summary>
/// Раздача прав из конфигурации. Права модератора и администратора нельзя
/// получить регистрацией — их выдаёт владелец сервера, перечислив почтовые
/// адреса в настройках:
///
/// <code>
/// Community:Owners:0         = owner@example.com
/// Community:Administrators:0 = admin@example.com
/// Community:Moderators:0     = mod@example.com
/// </code>
///
/// Роль владельца выдаётся только отсюда: через <c>PUT /api/community/roles</c>
/// её не назначить даже владельцу — иначе право банить расходилось бы по
/// сообществу само собой.
///
/// Список применяется при старте сервера и ещё раз при регистрации: аккаунт из
/// списка может появиться и после того, как сервер уже поднялся. Дальше права
/// выдаются через <c>PUT /api/community/roles</c>, и настройка их не отбирает —
/// она только поднимает роль, но никогда не понижает.
/// </summary>
public sealed class RoleBootstrap
{
    private readonly IReadOnlyDictionary<string, UserRole> _byEmail;

    public RoleBootstrap(IConfiguration configuration)
    {
        var map = new Dictionary<string, UserRole>(StringComparer.OrdinalIgnoreCase);

        foreach (var email in Read(configuration, "Community:Moderators"))
            map[email] = UserRole.Moderator;

        // Списки идут по возрастанию прав: адрес, попавший в несколько,
        // получает самую высокую роль из них.
        foreach (var email in Read(configuration, "Community:Administrators"))
            map[email] = UserRole.Administrator;

        foreach (var email in Read(configuration, "Community:Owners"))
            map[email] = UserRole.Owner;

        _byEmail = map;
    }

    /// <summary>Роль из настроек для адреса почты, либо null, если адреса там нет.</summary>
    public UserRole? RoleFor(string email) =>
        _byEmail.TryGetValue(email, out var role) ? role : null;

    /// <summary>
    /// Поднимает роль аккаунта до указанной в настройках. Возвращает true,
    /// если роль изменилась (запись ещё нужно сохранить).
    /// </summary>
    public bool Apply(UserAccount user)
    {
        if (RoleFor(user.Email) is not { } role || user.Role >= role)
            return false;

        user.Role = role;
        return true;
    }

    /// <summary>Применяет настройки ко всем уже существующим аккаунтам.</summary>
    public async Task<int> ApplyToExistingAsync(ApiDbContext db, CancellationToken ct = default)
    {
        if (_byEmail.Count == 0)
            return 0;

        var emails = _byEmail.Keys.ToList();
        var users = await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync(ct);

        var changed = users.Count(Apply);
        if (changed > 0)
            await db.SaveChangesAsync(ct);

        return changed;
    }

    private static IEnumerable<string> Read(IConfiguration configuration, string key) =>
        configuration.GetSection(key)
            .GetChildren()
            .Select(child => child.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().ToLowerInvariant());
}
