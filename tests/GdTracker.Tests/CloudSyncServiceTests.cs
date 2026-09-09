using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Core;
using GdTracker.Core.Abstractions;
using GdTracker.Sharing;

namespace GdTracker.Tests;

/// <summary>Тесты синхронизации: порядок шагов, конфликты ревизий и поведение при сбоях.</summary>
public class CloudSyncServiceTests
{
    [Fact]
    public async Task Sync_merges_cloud_snapshot_before_uploading_local_data()
    {
        var harness = await Harness.SignedIn();
        harness.Client.Snapshot = new Sharing.Cloud.SnapshotResponse(
            3, DateTime.UtcNow, PackageWith("Облачный уровень"));
        harness.Local.MergeResult = new ImportSummary(1, 0, 2);
        harness.Local.Local = PackageWith("Облачный уровень", "Локальный уровень");

        var outcome = await harness.Sync.SyncAsync();

        // Сначала слияние облачного пакета...
        harness.Local.Merged.Should().ContainSingle()
            .Which.Levels.Should().ContainSingle(l => l.Name == "Облачный уровень");
        // ...затем отправка уже объединённой картины поверх той же ревизии.
        harness.Client.PushedBaseRevisions.Should().ContainSingle().Which.Should().Be(3);
        harness.Client.Pushed.Should().ContainSingle()
            .Which.Levels.Should().HaveCount(2);

        outcome.LevelsAdded.Should().Be(1);
        outcome.RecordsAdded.Should().Be(2);
        outcome.Revision.Should().Be(4);
        outcome.Uploaded.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_with_empty_cloud_uploads_local_data_from_revision_zero()
    {
        var harness = await Harness.SignedIn();
        harness.Local.Local = PackageWith("Только локальный");

        var outcome = await harness.Sync.SyncAsync();

        harness.Local.Merged.Should().BeEmpty();
        harness.Client.PushedBaseRevisions.Should().ContainSingle().Which.Should().Be(0);
        outcome.Revision.Should().Be(1);
    }

    [Fact]
    public async Task Sync_retries_after_revision_conflict()
    {
        var harness = await Harness.SignedIn();
        harness.Client.Snapshot = new Sharing.Cloud.SnapshotResponse(1, DateTime.UtcNow, new ProgressPackage());
        // Первая запись натыкается на чужую синхронизацию, вторая проходит.
        harness.Client.ConflictsToRaise = 1;

        var outcome = await harness.Sync.SyncAsync();

        harness.Client.PushCallCount.Should().Be(2);
        harness.Client.GetSnapshotCallCount.Should().Be(2, "перед повтором данные из облака перечитываются");
        outcome.Uploaded.Should().BeTrue();
    }

    [Fact]
    public async Task Sync_gives_up_after_repeated_conflicts()
    {
        var harness = await Harness.SignedIn();
        harness.Client.ConflictsToRaise = 99;

        var act = () => harness.Sync.SyncAsync();

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Conflict);
    }

    [Fact]
    public async Task Pull_merges_without_uploading()
    {
        var harness = await Harness.SignedIn();
        harness.Client.Snapshot = new Sharing.Cloud.SnapshotResponse(
            5, DateTime.UtcNow, PackageWith("Облачный уровень"));
        harness.Local.MergeResult = new ImportSummary(1, 1, 3);

        var outcome = await harness.Sync.PullAsync();

        harness.Client.PushCallCount.Should().Be(0);
        outcome.Uploaded.Should().BeFalse();
        outcome.Revision.Should().Be(5);
        outcome.RecordsAdded.Should().Be(3);
    }

    [Fact]
    public async Task Push_uploads_local_data_without_merging_cloud_data()
    {
        var harness = await Harness.SignedIn();
        harness.Client.Snapshot = new Sharing.Cloud.SnapshotResponse(
            2, DateTime.UtcNow, PackageWith("Облачный уровень"));
        harness.Local.Local = PackageWith("Локальный уровень");

        var outcome = await harness.Sync.PushAsync();

        harness.Local.Merged.Should().BeEmpty("отправка заменяет облачные данные, а не сливает их");
        harness.Client.Pushed.Should().ContainSingle()
            .Which.Levels.Should().ContainSingle(l => l.Name == "Локальный уровень");
        outcome.Revision.Should().Be(3);
    }

    [Fact]
    public async Task Successful_sync_remembers_revision_and_time()
    {
        var harness = await Harness.SignedIn();

        var outcome = await harness.Sync.SyncAsync();

        harness.Settings.LastRevision.Should().Be(outcome.Revision);
        harness.Settings.LastSyncAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Sync_without_sign_in_reports_unauthorized_and_touches_nothing()
    {
        var harness = Harness.SignedOut();

        var act = () => harness.Sync.SyncAsync();

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
        harness.Client.GetSnapshotCallCount.Should().Be(0);
        harness.Settings.LastSyncAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Revoked_token_signs_the_user_out()
    {
        var harness = await Harness.SignedIn();
        harness.Client.FailWith = new CloudException(CloudErrorKind.Unauthorized, "Токен отозван.");

        var act = () => harness.Sync.SyncAsync();

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
        harness.Account.IsSignedIn.Should().BeFalse("держаться за отозванный токен смысла нет");
    }

    [Fact]
    public async Task Network_failure_leaves_last_sync_time_untouched()
    {
        var harness = await Harness.SignedIn();
        harness.Settings.LastSyncAtUtc = null;
        harness.Client.FailWith = new CloudException(CloudErrorKind.Network, "Сервер недоступен.");

        var act = () => harness.Sync.SyncAsync();

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Network);
        harness.Settings.LastSyncAtUtc.Should().BeNull();
        harness.Account.IsSignedIn.Should().BeTrue("недоступная сеть — не повод выкидывать пользователя из аккаунта");
    }

    private static ProgressPackage PackageWith(params string[] levelNames) => new()
    {
        ExportedAt = DateTime.UtcNow,
        Levels = levelNames.Select(name => new PackageLevel
        {
            Name = name,
            Source = LevelSource.Custom,
            Records = new List<PackageRecord>(),
        }).ToList(),
    };

    /// <summary>Связка сервисов синхронизации с заглушками вместо сети и базы.</summary>
    private sealed record Harness(
        CloudSyncService Sync,
        CloudAccountService Account,
        FakeCloudClient Client,
        FakeLocalProgressStore Local,
        InMemoryCloudSettings Settings)
    {
        public static Harness SignedOut()
        {
            var client = new FakeCloudClient();
            var settings = new InMemoryCloudSettings();
            var local = new FakeLocalProgressStore();
            var account = new CloudAccountService(client, new InMemoryTokenStore(), settings);
            return new Harness(new CloudSyncService(client, account, local, settings), account, client, local, settings);
        }

        public static async Task<Harness> SignedIn()
        {
            var harness = SignedOut();
            await harness.Account.SignInAsync("gd@example.com", "пароль-подлиннее");
            return harness;
        }
    }
}
