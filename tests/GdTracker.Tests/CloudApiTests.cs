using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Core;
using GdTracker.Sharing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GdTracker.Tests;

/// <summary>
/// Сервер синхронизации, поднятый в памяти на время тестов: своя временная база
/// SQLite, заданный ключ подписи (иначе сервер сгенерировал бы файл ключа) и
/// поднятый лимит частоты запросов, чтобы он не срабатывал на серии проверок.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(), $"gdtracker-api-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Db", $"Data Source={_databasePath}");
        builder.UseSetting("Auth:SigningKey", "тестовый-ключ-подписи-минимум-32-байта-длиной");
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "1000");
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
            // Файл ещё держит пул соединений SQLite — временный файл переживёт тесты,
            // и это не повод ронять прогон.
        }
    }
}

/// <summary>
/// Сквозные тесты облака: настоящий HTTP-клиент приложения против настоящего сервера.
/// Заодно проверяют, что клиент и сервер одинаково понимают JSON пакета прогресса.
/// </summary>
public class CloudApiTests : IClassFixture<ApiFactory>
{
    private const string BaseUrl = "http://localhost";

    private readonly ApiFactory _factory;

    public CloudApiTests(ApiFactory factory) => _factory = factory;

    private CloudClient CreateClient() => new(_factory.CreateClient());

    private static string UniqueEmail() => $"gd-{Guid.NewGuid():N}@example.com";

    [Fact]
    public async Task Register_returns_token_that_identifies_the_account()
    {
        var client = CreateClient();
        var email = UniqueEmail();

        var auth = await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");
        var info = await client.GetAccountAsync(BaseUrl, auth.AccessToken);

        auth.AccessToken.Should().NotBeNullOrEmpty();
        auth.ExpiresAtUtc.Should().BeAfter(DateTime.UtcNow);
        info.Email.Should().Be(email);
        info.SnapshotRevision.Should().Be(0);
    }

    [Fact]
    public async Task Register_normalizes_email_case()
    {
        var client = CreateClient();
        var email = UniqueEmail().ToUpperInvariant();

        await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");
        var auth = await client.LoginAsync(BaseUrl, email.ToLowerInvariant(), "пароль-подлиннее");

        auth.Email.Should().Be(email.ToLowerInvariant());
    }

    [Fact]
    public async Task Register_rejects_duplicate_email()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");

        var act = () => client.RegisterAsync(BaseUrl, email, "другой-пароль");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Theory]
    [InlineData("не-почта", "пароль-подлиннее")]
    [InlineData("gd@example.com", "корот")]
    public async Task Register_rejects_bad_credentials(string email, string password)
    {
        var client = CreateClient();

        var act = () => client.RegisterAsync(BaseUrl, email, password);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Fact]
    public async Task Login_with_wrong_password_is_rejected()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");

        var act = () => client.LoginAsync(BaseUrl, email, "неверный-пароль");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Login_of_unknown_account_is_rejected_the_same_way()
    {
        var client = CreateClient();

        var act = () => client.LoginAsync(BaseUrl, UniqueEmail(), "пароль-подлиннее");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Login_with_absurdly_long_password_is_rejected_without_hashing()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");

        var act = () => client.LoginAsync(BaseUrl, email, new string('п', 100_000));

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Sync_requires_authentication()
    {
        var client = CreateClient();

        var act = () => client.GetSnapshotAsync(BaseUrl, "поддельный-токен");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Empty_cloud_returns_no_snapshot()
    {
        var client = CreateClient();
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), "пароль-подлиннее");

        var snapshot = await client.GetSnapshotAsync(BaseUrl, auth.AccessToken);

        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task Pushed_package_comes_back_unchanged()
    {
        var client = CreateClient();
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), "пароль-подлиннее");
        var package = SamplePackage();

        var pushed = await client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, package);
        var snapshot = await client.GetSnapshotAsync(BaseUrl, auth.AccessToken);

        pushed.Revision.Should().Be(1);
        snapshot.Should().NotBeNull();
        snapshot!.Revision.Should().Be(1);

        var level = snapshot.Package.Levels.Should().ContainSingle().Subject;
        level.Name.Should().Be("Stereo Madness");
        level.GdLevelId.Should().Be(1);
        level.Source.Should().Be(LevelSource.Official);
        level.Stars.Should().Be(1);

        // Перечисления и даты обязаны пережить путь «клиент → сервер → клиент»:
        // из-за расхождения настроек JSON пакет мог бы приехать обратно испорченным.
        var record = level.Records.Should().ContainSingle().Subject;
        record.Type.Should().Be(RunType.FromZero);
        record.Mode.Should().Be(ProgressMode.Normal);
        record.Source.Should().Be(ProgressSource.Manual);
        record.ReachedPercent.Should().Be(87);
        record.Date.Should().BeCloseTo(package.Levels[0].Records[0].Date, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Push_with_stale_revision_is_rejected_as_conflict()
    {
        var client = CreateClient();
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), "пароль-подлиннее");
        await client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, SamplePackage());

        // Клиент, не знающий о чужой записи, всё ещё считает актуальной нулевую ревизию.
        var act = () => client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, SamplePackage());

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Conflict);
    }

    [Fact]
    public async Task Account_info_reports_current_revision()
    {
        var client = CreateClient();
        var auth = await client.RegisterAsync(BaseUrl, UniqueEmail(), "пароль-подлиннее");
        await client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, SamplePackage());

        var info = await client.GetAccountAsync(BaseUrl, auth.AccessToken);

        info.SnapshotRevision.Should().Be(1);
        info.SnapshotUpdatedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Snapshot_can_be_deleted_without_deleting_the_account()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var auth = await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");
        await client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, SamplePackage());

        await client.DeleteSnapshotAsync(BaseUrl, auth.AccessToken);

        (await client.GetSnapshotAsync(BaseUrl, auth.AccessToken)).Should().BeNull();
        // Аккаунт на месте: вход по-прежнему проходит.
        (await client.LoginAsync(BaseUrl, email, "пароль-подлиннее")).Email.Should().Be(email);
    }

    [Fact]
    public async Task Account_deletion_requires_the_password()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var auth = await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");

        var act = () => client.DeleteAccountAsync(BaseUrl, auth.AccessToken, "неверный-пароль");

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
        (await client.LoginAsync(BaseUrl, email, "пароль-подлиннее")).Email.Should().Be(email);
    }

    [Fact]
    public async Task Deleted_account_can_no_longer_sign_in()
    {
        var client = CreateClient();
        var email = UniqueEmail();
        var auth = await client.RegisterAsync(BaseUrl, email, "пароль-подлиннее");
        await client.PushSnapshotAsync(BaseUrl, auth.AccessToken, baseRevision: 0, SamplePackage());

        await client.DeleteAccountAsync(BaseUrl, auth.AccessToken, "пароль-подлиннее");

        var act = () => client.LoginAsync(BaseUrl, email, "пароль-подлиннее");
        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Health_endpoint_answers()
    {
        var client = CreateClient();

        (await client.CheckHealthAsync(BaseUrl)).Should().BeTrue();
    }

    private static ProgressPackage SamplePackage() => new()
    {
        ExportedAt = DateTime.UtcNow,
        Levels =
        [
            new PackageLevel
            {
                GdLevelId = 1,
                Name = "Stereo Madness",
                Source = LevelSource.Official,
                Stars = 1,
                Records =
                [
                    new PackageRecord
                    {
                        Type = RunType.FromZero,
                        StartPercent = 0,
                        ReachedPercent = 87,
                        Mode = ProgressMode.Normal,
                        Attempts = 42,
                        Date = new DateTime(2026, 5, 1, 12, 30, 0, DateTimeKind.Utc),
                        Note = "почти",
                        Source = ProgressSource.Manual,
                    },
                ],
            },
        ],
    };
}
