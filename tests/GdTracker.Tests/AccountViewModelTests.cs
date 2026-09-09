using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>
/// Тесты страницы «Аккаунт». Главное требование: ни один сбой облака не должен
/// приводить к исключению наружу — приложение обязано оставаться рабочим без аккаунта.
/// </summary>
public class AccountViewModelTests
{
    [Fact]
    public void Fresh_install_shows_sign_in_form()
    {
        var (vm, _, _, _) = CreateViewModel();

        vm.IsSignedIn.Should().BeFalse();
        vm.IsSignedOut.Should().BeTrue();
    }

    [Fact]
    public async Task Sign_in_switches_page_to_signed_in_state()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.IsSignedIn.Should().BeTrue();
        vm.AccountEmail.Should().Be("gd@example.com");
        vm.Error.Should().BeNull();
        vm.Password.Should().BeEmpty("пароль не должен оставаться в памяти вью-модели после входа");
    }

    [Fact]
    public async Task Signed_in_panel_shows_username_with_its_role()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.StoredUsername = "GdPlayer";
        client.StoredRole = UserRole.Moderator;
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.AccountUsername.Should().Be("GdPlayer");
        vm.AccountRoleLabel.Should().Be("модератор");
        vm.IsPrivilegedRole.Should().BeTrue("значок роли модератора подсвечивается");
    }

    [Fact]
    public async Task Ordinary_member_role_is_shown_without_highlight()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.AccountRoleLabel.Should().Be("участник");
        vm.IsPrivilegedRole.Should().BeFalse();
    }

    [Fact]
    public async Task Registration_without_username_asks_for_it_instead_of_calling_server()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.RegisterCommand.ExecuteAsync(null);

        vm.Error.Should().Contain("ник");
        client.StoredEmail.Should().BeNull();
    }

    [Fact]
    public async Task Registration_sends_the_chosen_username()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        vm.Username = "GdPlayer";

        await vm.RegisterCommand.ExecuteAsync(null);

        client.StoredUsername.Should().Be("GdPlayer");
        vm.AccountUsername.Should().Be("GdPlayer");
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Username_can_be_changed_from_the_page()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        vm.Username = "GdPlayer";
        await vm.RegisterCommand.ExecuteAsync(null);

        vm.Username = "ДругойНик";
        await vm.ChangeUsernameCommand.ExecuteAsync(null);

        client.StoredUsername.Should().Be("ДругойНик");
        vm.AccountUsername.Should().Be("ДругойНик");
        vm.Status.Should().Contain("ДругойНик");
    }

    [Fact]
    public async Task Taken_username_is_reported_without_breaking_the_session()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        vm.Username = "GdPlayer";
        await vm.RegisterCommand.ExecuteAsync(null);

        client.FailWith = new CloudException(CloudErrorKind.Validation, "Этот ник уже занят.");
        vm.Username = "Занятый";
        await vm.ChangeUsernameCommand.ExecuteAsync(null);

        vm.Error.Should().Be("Этот ник уже занят.");
        vm.IsSignedIn.Should().BeTrue();
        vm.AccountUsername.Should().Be("GdPlayer");
    }

    [Fact]
    public async Task Sign_in_without_password_asks_for_it_instead_of_calling_server()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.Error.Should().Contain("пароль");
        client.StoredEmail.Should().BeNull();
    }

    [Fact]
    public async Task Server_failure_is_shown_as_message_not_thrown()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.FailWith = new CloudException(CloudErrorKind.Network, "Сервер синхронизации недоступен.");
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.Error.Should().StartWith("Сервер синхронизации недоступен.");
        vm.IsSignedIn.Should().BeFalse();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Unreachable_local_server_is_explained_not_just_reported()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.ServerUrl = "http://localhost:5080";
        client.FailWith = new CloudException(CloudErrorKind.Network, "Сервер синхронизации недоступен.");
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        vm.Username = "GdPlayer";

        await vm.RegisterCommand.ExecuteAsync(null);

        // Одного «недоступен» мало: у себя на компьютере это значит «сервер не запущен».
        vm.Error.Should().Contain("не запущен").And.Contain("run-sync-server");
    }

    [Fact]
    public async Task Unreachable_remote_server_suggests_checking_the_address()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.ServerUrl = "https://sync.example.com";
        client.FailWith = new CloudException(CloudErrorKind.Network, "Сервер синхронизации недоступен.");
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";

        await vm.SignInCommand.ExecuteAsync(null);

        vm.Error.Should().Contain("адрес сервера").And.NotContain("run-sync-server");
    }

    [Fact]
    public async Task Server_check_reports_a_live_server()
    {
        var (vm, _, _, _) = CreateViewModel();

        await vm.CheckServerCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        vm.Status.Should().Contain("отвечает");
    }

    [Fact]
    public async Task Server_check_explains_a_dead_server()
    {
        var (vm, client, _, _) = CreateViewModel();
        vm.ServerUrl = "http://localhost:5080";
        client.FailWith = new CloudException(CloudErrorKind.Network, "Сервер синхронизации недоступен.");

        await vm.CheckServerCommand.ExecuteAsync(null);

        vm.Error.Should().Contain("не запущен");
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_without_sign_in_reports_error_and_does_not_throw()
    {
        var (vm, _, _, _) = CreateViewModel();

        await vm.SyncCommand.ExecuteAsync(null);

        vm.Error.Should().NotBeNullOrEmpty();
        vm.IsBusy.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_reports_what_arrived_from_the_cloud()
    {
        var (vm, client, local, _) = CreateViewModel();
        client.Snapshot = new Sharing.Cloud.SnapshotResponse(4, DateTime.UtcNow, new Sharing.ProgressPackage());
        local.MergeResult = new Core.Abstractions.ImportSummary(2, 1, 5);
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        await vm.SignInCommand.ExecuteAsync(null);

        await vm.SyncCommand.ExecuteAsync(null);

        vm.Error.Should().BeNull();
        vm.Status.Should().Contain("2").And.Contain("5");
        vm.LastSyncText.Should().StartWith("Последняя синхронизация");
    }

    [Fact]
    public async Task Sign_out_keeps_the_app_usable_and_says_so()
    {
        var (vm, _, _, _) = CreateViewModel();
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        await vm.SignInCommand.ExecuteAsync(null);

        vm.SignOutCommand.Execute(null);

        vm.IsSignedIn.Should().BeFalse();
        vm.Status.Should().Contain("Локальный прогресс остался");
    }

    [Fact]
    public void Server_url_change_is_saved_for_the_next_run()
    {
        var (vm, _, _, settings) = CreateViewModel();

        vm.ServerUrl = "https://sync.example.com";

        settings.ServerUrl.Should().Be("https://sync.example.com");
    }

    [Fact]
    public void Constructing_the_page_does_not_rewrite_settings()
    {
        var settings = new InMemoryCloudSettings { ServerUrl = "https://sync.example.com", AutoSyncOnStartup = true };
        var client = new FakeCloudClient();
        var account = new CloudAccountService(client, new InMemoryTokenStore(), settings);
        var sync = new CloudSyncService(client, account, new FakeLocalProgressStore(), settings);

        var vm = new AccountViewModel(account, sync, settings, new FakeConfirmation());

        vm.ServerUrl.Should().Be("https://sync.example.com");
        vm.AutoSyncOnStartup.Should().BeTrue();
        settings.ServerUrl.Should().Be("https://sync.example.com");
    }

    [Fact]
    public async Task Cloud_data_deletion_asks_for_confirmation_first()
    {
        var (vm, client, _, _) = CreateViewModel(out var confirmation);
        confirmation.Result = false;
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        await vm.SignInCommand.ExecuteAsync(null);

        await vm.DeleteCloudDataCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(1);
        client.DeleteSnapshotCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Account_deletion_requires_password_confirmation()
    {
        var (vm, client, _, _) = CreateViewModel(out var confirmation);
        vm.Email = "gd@example.com";
        vm.Password = "пароль-подлиннее";
        await vm.SignInCommand.ExecuteAsync(null);
        // После входа поле пароля очищается — удаление должно об этом сказать.

        await vm.DeleteAccountCommand.ExecuteAsync(null);

        confirmation.CallCount.Should().Be(0);
        client.DeleteAccountCallCount.Should().Be(0);
        vm.Error.Should().Contain("пароль");
    }

    private static (AccountViewModel Vm, FakeCloudClient Client, FakeLocalProgressStore Local, InMemoryCloudSettings Settings)
        CreateViewModel() => CreateViewModel(out _);

    private static (AccountViewModel Vm, FakeCloudClient Client, FakeLocalProgressStore Local, InMemoryCloudSettings Settings)
        CreateViewModel(out FakeConfirmation confirmation)
    {
        var client = new FakeCloudClient();
        var settings = new InMemoryCloudSettings();
        var local = new FakeLocalProgressStore();
        var account = new CloudAccountService(client, new InMemoryTokenStore(), settings);
        var sync = new CloudSyncService(client, account, local, settings);
        confirmation = new FakeConfirmation();

        return (new AccountViewModel(account, sync, settings, confirmation), client, local, settings);
    }
}
