using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Tests;

/// <summary>
/// Тесты состояния аккаунта: приложение обязано подниматься и работать без входа,
/// а просроченный или отозванный токен не должен превращаться в постоянную ошибку.
/// </summary>
public class CloudAccountServiceTests
{
    [Fact]
    public void Fresh_install_is_not_signed_in()
    {
        var (account, _, _) = CreateAccount();

        account.IsSignedIn.Should().BeFalse();
        account.Email.Should().BeNull();
    }

    [Fact]
    public void Restore_brings_back_saved_session()
    {
        var (account, _, tokens) = CreateAccount();
        tokens.Preload(new StoredToken("user-1", "gd@example.com", "gd-player", UserRole.Member, "token", DateTime.UtcNow.AddDays(10)));

        account.Restore();

        account.IsSignedIn.Should().BeTrue();
        account.Email.Should().Be("gd@example.com");
        account.RequireAccessToken().Should().Be("token");
    }

    [Fact]
    public void Restore_discards_expired_token_and_stays_signed_out()
    {
        var (account, _, tokens) = CreateAccount();
        tokens.Preload(new StoredToken("user-1", "gd@example.com", "gd-player", UserRole.Member, "token", DateTime.UtcNow.AddMinutes(-1)));

        account.Restore();

        account.IsSignedIn.Should().BeFalse();
        tokens.ClearCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Sign_in_saves_token_and_email()
    {
        var (account, settings, tokens) = CreateAccount();

        await account.SignInAsync("GD@Example.com", "пароль-подлиннее");

        account.IsSignedIn.Should().BeTrue();
        tokens.SaveCallCount.Should().Be(1);
        settings.LastEmail.Should().Be("GD@Example.com");
    }

    [Fact]
    public async Task Sign_in_remembers_username_and_role()
    {
        var (account, _, _) = CreateAccount(out var client);
        client.StoredUsername = "GdPlayer";
        client.StoredRole = UserRole.Moderator;

        await account.SignInAsync("gd@example.com", "пароль-подлиннее");

        account.Username.Should().Be("GdPlayer");
        account.Role.Should().Be(UserRole.Moderator);
    }

    [Fact]
    public async Task Changed_username_replaces_the_one_in_the_saved_session()
    {
        var (account, _, tokens) = CreateAccount(out _);
        await account.SignInAsync("gd@example.com", "пароль-подлиннее");

        await account.ChangeUsernameAsync("НовыйНик");

        account.Username.Should().Be("НовыйНик");
        // Ник переживает перезапуск: он лежит в том же файле, что и токен.
        tokens.Load()!.Username.Should().Be("НовыйНик");
    }

    [Fact]
    public async Task Account_info_picks_up_a_role_granted_on_the_server()
    {
        var (account, _, _) = CreateAccount(out var client);
        await account.SignInAsync("gd@example.com", "пароль-подлиннее");

        // Права выдали уже после входа — в токене их нет, но сервер о них знает.
        client.StoredRole = UserRole.Administrator;
        await account.GetAccountInfoAsync();

        account.Role.Should().Be(UserRole.Administrator);
    }

    [Fact]
    public async Task Sign_out_clears_token_and_remembered_revision()
    {
        var (account, settings, tokens) = CreateAccount();
        await account.SignInAsync("gd@example.com", "пароль-подлиннее");
        settings.LastRevision = 7;

        account.SignOut();

        account.IsSignedIn.Should().BeFalse();
        tokens.ClearCallCount.Should().Be(1);
        // Ревизия чужого аккаунта не должна пережить выход: иначе следующая
        // синхронизация другим аккаунтом ушла бы с неверной базовой ревизией.
        settings.LastRevision.Should().Be(0);
    }

    [Fact]
    public void Require_access_token_without_sign_in_reports_unauthorized()
    {
        var (account, _, _) = CreateAccount();

        var act = () => account.RequireAccessToken();

        act.Should().Throw<CloudException>().Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task State_changed_fires_on_sign_in_and_sign_out()
    {
        var (account, _, _) = CreateAccount();
        var changes = 0;
        account.StateChanged += (_, _) => changes++;

        await account.SignInAsync("gd@example.com", "пароль-подлиннее");
        account.SignOut();

        changes.Should().Be(2);
    }

    [Fact]
    public async Task Delete_account_signs_out_locally()
    {
        var (account, _, tokens) = CreateAccount();
        await account.SignInAsync("gd@example.com", "пароль-подлиннее");

        await account.DeleteAccountAsync("пароль-подлиннее");

        account.IsSignedIn.Should().BeFalse();
        tokens.ClearCallCount.Should().Be(1);
    }

    private static (CloudAccountService Account, InMemoryCloudSettings Settings, InMemoryTokenStore Tokens) CreateAccount()
        => CreateAccount(out _);

    private static (CloudAccountService Account, InMemoryCloudSettings Settings, InMemoryTokenStore Tokens) CreateAccount(
        out FakeCloudClient client)
    {
        var settings = new InMemoryCloudSettings();
        var tokens = new InMemoryTokenStore();
        client = new FakeCloudClient();
        var account = new CloudAccountService(client, tokens, settings);
        return (account, settings, tokens);
    }
}
