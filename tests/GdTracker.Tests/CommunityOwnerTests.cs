using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GdTracker.Api.Endpoints;
using GdTracker.Sharing.Cloud;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GdTracker.Tests;

/// <summary>
/// Сервер с заранее назначенным владельцем: роль выдаётся только настройкой,
/// поэтому без неё проверять было бы нечего.
/// </summary>
public sealed class OwnerApiFactory : WebApplicationFactory<Program>
{
    public const string OwnerEmail = "owner@example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"gdtracker-owner-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", $"Data Source={_databasePath}");
        builder.UseSetting("Auth:SigningKey", "тестовый-ключ-подписи-минимум-32-байта-длиной");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "1000");
        builder.UseSetting("Community:Owners:0", OwnerEmail);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        try
        {
            if (File.Exists(_databasePath))
                File.Delete(_databasePath);
        }
        catch (IOException)
        {
            // Файл ещё держит пул соединений SQLite — это не повод ронять прогон.
        }
    }
}

/// <summary>
/// Права владельца: он правит списки наравне с модератором и, сверх того, банит —
/// целиком либо только в демонлисте. Проверяется и обратная сторона: чего нельзя
/// ни ему самому (забанить себя, выдать роль владельца), ни остальным (банить вообще).
/// </summary>
public class CommunityOwnerTests : IClassFixture<OwnerApiFactory>
{
    private const string Password = "пароль-подлиннее";

    private readonly OwnerApiFactory _factory;

    public CommunityOwnerTests(OwnerApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Owner_from_configuration_gets_the_role_and_the_right_to_ban()
    {
        var (_, profile) = await SignInAsOwnerAsync();

        profile.Role.Should().Be(UserRole.Owner);
        profile.CanBan.Should().BeTrue();
        // Владелец правит топ-200 и заявки наравне с модератором.
        profile.CanModerate.Should().BeTrue();
    }

    [Fact]
    public async Task Member_cannot_ban_anybody()
    {
        var (member, _) = await RegisterAsync();
        var (_, victim) = await RegisterAsync();

        var response = await member.PutAsJsonAsync(
            $"/api/community/users/{victim.UserId}/ban",
            new BanRequest(true, "просто так"),
            CloudJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_moves_a_demon_and_the_neighbours_close_the_gap()
    {
        var (owner, _) = await SignInAsOwnerAsync();

        var before = await DemonsAsync();
        // Демона с третьего места поднимаем на первое: так проверяется и сам
        // переезд, и сдвиг тех, кто стоял выше.
        var moved = before[2];

        var response = await owner.PutAsJsonAsync(
            $"/api/community/demons/{moved.Id}/position",
            new MoveDemonRequest(1),
            CloudJson.Options);
        response.EnsureSuccessStatusCode();

        var after = await DemonsAsync();

        after[0].Id.Should().Be(moved.Id);
        after[0].Position.Should().Be(1);
        // Прежние первый и второй съехали на место вниз, а не потерялись.
        after[1].Id.Should().Be(before[0].Id);
        after[2].Id.Should().Be(before[1].Id);
        after.Should().HaveCount(before.Count);
        // Места идут подряд, без дыр и повторов — иначе список посыпется.
        after.Select(d => d.Position).Should().Equal(Enumerable.Range(1, after.Count));
    }

    [Fact]
    public async Task Member_cannot_move_a_demon()
    {
        var (member, _) = await RegisterAsync();
        var demons = await DemonsAsync();

        var response = await member.PutAsJsonAsync(
            $"/api/community/demons/{demons[0].Id}/position",
            new MoveDemonRequest(5),
            CloudJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DemonsAsync())[0].Id.Should().Be(demons[0].Id);
    }

    [Fact]
    public async Task Member_cannot_see_the_accounts_list()
    {
        var (member, _) = await RegisterAsync();

        var response = await member.GetAsync("/api/community/users");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Owner_finds_an_account_by_name_and_sees_its_bans()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        var (_, member) = await RegisterAsync();

        await BanAsync(owner, member.UserId, listOnly: true, reason: "спам в мнениях");

        var found = await owner.GetFromJsonAsync<List<ManagedUserView>>(
            "/api/community/users?query=" + Uri.EscapeDataString(member.Username), CloudJson.Options);

        var row = found.Should().ContainSingle().Subject;
        row.Username.Should().Be(member.Username);
        row.ListBanned.Should().BeTrue();
        row.ListBanReason.Should().Be("спам в мнениях");
        // Бан в демонлисте не трогает вход: полного бана у аккаунта нет.
        row.Banned.Should().BeFalse();
    }

    [Fact]
    public async Task Banned_account_loses_the_login_and_the_cloud()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        var (victim, victimProfile) = await RegisterAsync(out var email);

        await BanAsync(owner, victimProfile.UserId, listOnly: false, reason: "накрутка рекордов");

        // Уже выданный токен отклоняется сразу, не дожидаясь конца своего срока.
        var sync = await victim.GetAsync("/api/sync/snapshot");
        sync.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var login = await LoginAsync(email);
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Причина доезжает до забаненного: иначе он не поймёт, что произошло.
        var error = await login.Content.ReadFromJsonAsync<ApiError>(CloudJson.Options);
        error!.Code.Should().Be(BanCodes.Account);
        error.Message.Should().Contain("накрутка рекордов");
    }

    [Fact]
    public async Task Lifting_the_ban_returns_the_account()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        var (_, victimProfile) = await RegisterAsync(out var email);

        await BanAsync(owner, victimProfile.UserId, listOnly: false, reason: "разберёмся");
        await BanAsync(owner, victimProfile.UserId, listOnly: false, banned: false);

        var login = await LoginAsync(email);
        login.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Demonlist_ban_leaves_the_login_but_closes_the_list()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        var (member, memberProfile) = await RegisterAsync(out var email);
        var demonId = await FirstDemonIdAsync();

        await BanAsync(owner, memberProfile.UserId, listOnly: true, reason: "оскорбления в мнениях");

        // Вход и облако на месте — закрыт именно демонлист.
        (await LoginAsync(email)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.GetAsync("/api/sync/snapshot")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var opinion = await member.PostAsJsonAsync(
            $"/api/community/demons/{demonId}/opinions",
            new OpinionRequest("место неверное", null),
            CloudJson.Options);
        opinion.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var record = await member.PostAsJsonAsync(
            "/api/community/records",
            new SubmitRecordRequest(null, "Bloodbath", 100, "https://youtu.be/x", true, null),
            CloudJson.Options);
        record.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var error = await record.Content.ReadFromJsonAsync<ApiError>(CloudJson.Options);
        error!.Code.Should().Be(BanCodes.DemonList);

        // Профиль сообщает о бане, чтобы страница не рисовала неработающие формы.
        var profile = await ProfileAsync(member);
        profile.ListBanned.Should().BeTrue();
        profile.ListBanReason.Should().Be("оскорбления в мнениях");
    }

    [Fact]
    public async Task Demonlist_ban_hides_the_record_from_the_public_top_and_gives_it_back()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        var (member, memberProfile) = await RegisterAsync();

        var levelName = $"Уровень-{Guid.NewGuid():N}"[..20];
        var submitted = await member.PostAsJsonAsync(
            "/api/community/records",
            new SubmitRecordRequest(null, levelName, 100, "https://youtu.be/proof", true, null),
            CloudJson.Options);
        submitted.EnsureSuccessStatusCode();

        var record = (await submitted.Content.ReadFromJsonAsync<RecordView>(CloudJson.Options))!;
        var reviewed = await owner.PostAsJsonAsync(
            $"/api/community/records/{record.Id}/review",
            new ReviewRecordRequest(true, null, null),
            CloudJson.Options);
        reviewed.EnsureSuccessStatusCode();

        (await PublicTopAsync()).Should().Contain(r => r.LevelName == levelName);

        await BanAsync(owner, memberProfile.UserId, listOnly: true, reason: "читы");
        (await PublicTopAsync()).Should().NotContain(r => r.LevelName == levelName);

        // Бан ничего не стирает: снятие возвращает рекорд на прежнее место.
        await BanAsync(owner, memberProfile.UserId, listOnly: true, banned: false);
        (await PublicTopAsync()).Should().Contain(r => r.LevelName == levelName);
    }

    [Fact]
    public async Task Owner_does_not_ban_himself()
    {
        var (owner, ownerProfile) = await SignInAsOwnerAsync();

        var self = await owner.PutAsJsonAsync(
            $"/api/community/users/{ownerProfile.UserId}/ban",
            new BanRequest(true, null),
            CloudJson.Options);

        self.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await LoginAsync(OwnerApiFactory.OwnerEmail)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Owner_role_is_not_handed_out_through_the_api()
    {
        var (owner, _) = await SignInAsOwnerAsync();
        await RegisterAsync(out var email);

        var response = await owner.PutAsJsonAsync(
            "/api/community/roles",
            new SetRoleRequest(email, UserRole.Owner),
            CloudJson.Options);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Роль модератора владелец выдаёт как обычно — запрещена только своя.
        var moderator = await owner.PutAsJsonAsync(
            "/api/community/roles",
            new SetRoleRequest(email, UserRole.Moderator),
            CloudJson.Options);

        moderator.EnsureSuccessStatusCode();
        var updated = (await moderator.Content.ReadFromJsonAsync<CommunityProfile>(CloudJson.Options))!;
        updated.Role.Should().Be(UserRole.Moderator);
        updated.CanBan.Should().BeFalse();
    }

    // ----------------------------------------------------------- вспомогательное

    private async Task<List<RecordView>> PublicTopAsync()
    {
        var client = _factory.CreateClient();
        return (await client.GetFromJsonAsync<List<RecordView>>("/api/community/records", CloudJson.Options))!;
    }

    /// <summary>Демонлист в порядке мест — таким его отдаёт сервер всем подряд.</summary>
    private async Task<List<DemonView>> DemonsAsync()
    {
        var client = _factory.CreateClient();
        var demons = await client.GetFromJsonAsync<List<DemonView>>("/api/community/demons", CloudJson.Options);
        demons.Should().NotBeNullOrEmpty("сервер наполняет пустой демонлист стартовой расстановкой");
        return demons!;
    }

    private async Task<string> FirstDemonIdAsync() => (await DemonsAsync())[0].Id;

    private async Task BanAsync(
        HttpClient owner, string userId, bool listOnly, string? reason = null, bool banned = true)
    {
        var path = listOnly ? "list-ban" : "ban";
        var response = await owner.PutAsJsonAsync(
            $"/api/community/users/{userId}/{path}",
            new BanRequest(banned, reason),
            CloudJson.Options);

        response.EnsureSuccessStatusCode();
    }

    private Task<(HttpClient Client, CommunityProfile Profile)> SignInAsOwnerAsync() =>
        RegisterOrLoginAsync(OwnerApiFactory.OwnerEmail);

    private Task<(HttpClient Client, CommunityProfile Profile)> RegisterAsync() =>
        RegisterAsync(out _);

    private Task<(HttpClient Client, CommunityProfile Profile)> RegisterAsync(out string email)
    {
        email = $"gd-{Guid.NewGuid():N}@example.com";
        return RegisterOrLoginAsync(email);
    }

    /// <summary>
    /// Заводит аккаунт и возвращает клиент с его токеном. Владелец заводится
    /// один раз на всю фикстуру, поэтому повторная регистрация переходит во вход.
    /// </summary>
    private async Task<(HttpClient Client, CommunityProfile Profile)> RegisterOrLoginAsync(string email)
    {
        var client = _factory.CreateClient();
        var username = "u" + Guid.NewGuid().ToString("N")[..12];

        var response = await client.PostAsJsonAsync(
            "/api/auth/register", new RegisterRequest(email, Password, username), CloudJson.Options);

        if (response.StatusCode == HttpStatusCode.Conflict)
            response = await LoginAsync(email);

        response.EnsureSuccessStatusCode();

        var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>(CloudJson.Options))!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        return (client, await ProfileAsync(client));
    }

    private Task<HttpResponseMessage> LoginAsync(string email) =>
        _factory.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, Password), CloudJson.Options);

    private static async Task<CommunityProfile> ProfileAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<CommunityProfile>("/api/community/me", CloudJson.Options))!;
}
