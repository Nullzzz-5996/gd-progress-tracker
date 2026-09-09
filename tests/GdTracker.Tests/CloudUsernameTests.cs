using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using GdTracker.Api.Endpoints;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GdTracker.Tests;

/// <summary>
/// Сервер с заранее назначенным администратором: права выдаются настройкой,
/// а не регистрацией, поэтому иначе роль выше участника в тестах не получить.
/// </summary>
public sealed class CommunityApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Почта, которой настройки сервера дают права администратора.</summary>
    public const string AdminEmail = "boss@example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"gdtracker-roles-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", $"Data Source={_databasePath}");
        builder.UseSetting("Auth:SigningKey", "тестовый-ключ-подписи-минимум-32-байта-длиной");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "1000");
        builder.UseSetting("Community:Administrators:0", AdminEmail);
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
            // Файл ещё держит пул соединений SQLite — прогон это ронять не должно.
        }
    }
}

/// <summary>
/// Ники и роли: аккаунт заводится под уникальным именем, роль ездит вместе с ним
/// во всех ответах, а страница демонлиста получает и имя, и роль автора.
/// </summary>
public class CloudUsernameTests : IClassFixture<CommunityApiFactory>
{
    private const string BaseUrl = "http://localhost";
    private const string Password = "пароль-подлиннее";

    private readonly CommunityApiFactory _factory;

    public CloudUsernameTests(CommunityApiFactory factory) => _factory = factory;

    private CloudClient CreateClient() => new(_factory.CreateClient());

    private static string UniqueEmail() => $"gd-{Guid.NewGuid():N}@example.com";

    private static string UniqueName() => $"gd{Guid.NewGuid():N}"[..12];

    [Fact]
    public async Task Registration_keeps_the_chosen_username()
    {
        var client = CreateClient();
        var username = UniqueName();

        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);
        var info = await client.GetAccountAsync(BaseUrl, auth.AccessToken);

        auth.Username.Should().Be(username);
        auth.Role.Should().Be(UserRole.Member);
        info.Username.Should().Be(username);
        info.Role.Should().Be(UserRole.Member);
    }

    [Fact]
    public async Task Registration_without_username_derives_one_from_the_email()
    {
        var client = CreateClient();

        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), Password);

        // Ник выводится из части адреса до собачки — адрес целиком в публичный список не уходит.
        auth.Username.Should().NotBeNullOrWhiteSpace();
        auth.Username.Should().NotContain("@");
    }

    [Fact]
    public async Task Username_is_taken_regardless_of_letter_case()
    {
        var client = CreateClient();
        var username = UniqueName();
        await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        var act = () => client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username.ToUpperInvariant());

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Fact]
    public async Task Login_takes_the_username_instead_of_the_email()
    {
        var client = CreateClient();
        var username = UniqueName();
        await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        var auth = await client.LoginAsync(BaseUrl, username, Password);

        auth.Username.Should().Be(username);
    }

    [Fact]
    public async Task Login_by_username_ignores_letter_case()
    {
        var client = CreateClient();
        var username = UniqueName();
        await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        var auth = await client.LoginAsync(BaseUrl, username.ToUpperInvariant(), Password);

        auth.Username.Should().Be(username);
    }

    [Fact]
    public async Task Login_by_email_still_works()
    {
        // Выпущенные версии приложения шлют в этом поле почту — вход по нику
        // не должен был отобрать у них возможность войти.
        var client = CreateClient();
        var email = UniqueEmail();
        await client.RegisterAsync(BaseUrl, email, Password, UniqueName());

        var auth = await client.LoginAsync(BaseUrl, email, Password);

        auth.Email.Should().Be(email);
    }

    [Fact]
    public async Task Login_with_unknown_username_is_rejected()
    {
        var client = CreateClient();

        var act = () => client.LoginAsync(BaseUrl, UniqueName(), Password);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Login_by_username_with_wrong_password_is_rejected()
    {
        var client = CreateClient();
        var username = UniqueName();
        await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        var act = () => client.LoginAsync(BaseUrl, username, "неверный-пароль");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Theory]
    [InlineData("ab")]                       // короче трёх символов
    [InlineData("имя с пробелом")]           // пробелы не годятся
    [InlineData("ник!")]                     // знаки препинания не годятся
    [InlineData("---")]                      // ни одной буквы и цифры
    [InlineData("этот-ник-заведомо-длиннее-двадцати")]
    public async Task Bad_username_is_rejected(string username)
    {
        var client = CreateClient();

        var act = () => client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Fact]
    public async Task Cyrillic_username_is_allowed()
    {
        var client = CreateClient();
        var username = "Игрок" + Random.Shared.Next(1000, 9999);

        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, username);

        auth.Username.Should().Be(username);
    }

    [Fact]
    public async Task Username_can_be_changed()
    {
        var client = CreateClient();
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, UniqueName());
        var updated = UniqueName();

        var info = await client.ChangeUsernameAsync(BaseUrl, auth.AccessToken, updated);

        info.Username.Should().Be(updated);
        (await client.GetAccountAsync(BaseUrl, auth.AccessToken)).Username.Should().Be(updated);
    }

    [Fact]
    public async Task Username_cannot_be_changed_to_a_taken_one()
    {
        var client = CreateClient();
        var taken = UniqueName();
        await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, taken);
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), Password, UniqueName());

        var act = () => client.ChangeUsernameAsync(BaseUrl, auth.AccessToken, taken);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Fact]
    public async Task Configured_administrator_gets_the_role_with_the_token()
    {
        // Аккаунт этой почты вписан в настройки сервера как администратор: роль
        // появляется сразу при регистрации, а не после перезапуска сервера.
        var auth = await SignInAdminAsync();

        auth.Role.Should().Be(UserRole.Administrator);
        (await CreateClient().GetAccountAsync(BaseUrl, auth.AccessToken)).Role.Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task Opinion_carries_the_author_username_and_role()
    {
        var admin = await SignInAdminAsync();
        var demonId = await AddDemonAsync(admin);

        var http = _factory.CreateClient();
        var memberName = UniqueName();
        var member = await new CloudClient(http).RegisterAsync(BaseUrl, UniqueEmail(), Password, memberName);

        var posted = await PostAsync<OpinionRequest, OpinionView>(
            member.AccessToken, $"/api/community/demons/{demonId}/opinions",
            new OpinionRequest("Стоит выше, чем заслуживает.", null));

        posted.AuthorName.Should().Be(memberName);
        posted.AuthorRole.Should().Be(UserRole.Member);

        // Тот же ник с ролью приходит и в публичном чтении — из него страница
        // рисует значок рядом с именем.
        var listed = await GetAsync<List<OpinionView>>(null, $"/api/community/demons/{demonId}/opinions");
        listed.Should().ContainSingle(o => o.AuthorName == memberName && o.AuthorRole == UserRole.Member);
    }

    [Fact]
    public async Task Moderator_role_is_visible_next_to_the_name_in_opinions()
    {
        var admin = await SignInAdminAsync();
        var demonId = await AddDemonAsync(admin);

        var posted = await PostAsync<OpinionRequest, OpinionView>(
            admin.AccessToken, $"/api/community/demons/{demonId}/opinions",
            new OpinionRequest("Место выверено, оставляю как есть.", null));

        posted.AuthorRole.Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task Community_profile_reports_username_and_role()
    {
        var admin = await SignInAdminAsync();

        var profile = await GetAsync<CommunityProfile>(admin.AccessToken, "/api/community/me");

        profile.Username.Should().NotBeNullOrWhiteSpace();
        profile.Role.Should().Be(UserRole.Administrator);
        profile.CanModerate.Should().BeTrue();
    }

    /// <summary>Вход администратором: аккаунт заводится один раз на всю фикстуру.</summary>
    private async Task<AuthResponse> SignInAdminAsync()
    {
        var client = CreateClient();
        try
        {
            return await client.RegisterAsync(BaseUrl, CommunityApiFactory.AdminEmail, Password, UniqueName());
        }
        catch (CloudException)
        {
            // Другой тест уже завёл этот аккаунт — тогда просто входим.
            return await client.LoginAsync(BaseUrl, CommunityApiFactory.AdminEmail, Password);
        }
    }

    private async Task<string> AddDemonAsync(AuthResponse admin)
    {
        var demon = await PostAsync<AddDemonRequest, DemonView>(
            admin.AccessToken,
            "/api/community/demons",
            new AddDemonRequest(1, $"Тестовый демон {Guid.NewGuid():N}", "автор", "верификатор", null, null, null, 100));

        return demon.Id;
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(string token, string path, TRequest body)
    {
        var http = _factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.PostAsJsonAsync(path, body, CloudJson.Options);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<TResponse>(CloudJson.Options))!;
    }

    private async Task<TResponse> GetAsync<TResponse>(string? token, string path)
    {
        var http = _factory.CreateClient();
        if (token is not null)
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await http.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());

        return (await response.Content.ReadFromJsonAsync<TResponse>(CloudJson.Options))!;
    }
}
