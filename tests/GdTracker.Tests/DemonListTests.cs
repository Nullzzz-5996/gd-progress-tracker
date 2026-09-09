using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;
using GdTracker.ViewModels;

namespace GdTracker.Tests;

/// <summary>
/// Управляемая заглушка демонлиста: хранит «серверную» расстановку, мнения и
/// заявки, считает вызовы и умеет притвориться недоступным сервером.
/// </summary>
internal sealed class FakeCommunityClient : ICommunityClient
{
    public List<DemonView> Demons { get; } = new();

    public Dictionary<string, List<OpinionView>> Opinions { get; } = new();

    public List<RecordView> TopRecords { get; } = new();

    public List<RecordView> MyRecords { get; } = new();

    public List<RecordView> PendingRecords { get; } = new();

    public List<ManagedUserView> Users { get; } = new();

    public CommunityProfile Profile { get; set; } = new(
        "user-1", "gd@example.com", "gd-player", UserRole.Member,
        CanModerate: false, CanBan: false, ListBanned: false, ListBanReason: null);

    /// <summary>Сервер не отвечает: все запросы падают сетевой ошибкой.</summary>
    public bool ServerDown { get; set; }

    public int DemonsReadCount { get; private set; }
    public int MoveCount { get; private set; }
    public int AddDemonCount { get; private set; }
    public int DeleteDemonCount { get; private set; }
    public int PostOpinionCount { get; private set; }
    public int SubmitRecordCount { get; private set; }
    public int ReviewCount { get; private set; }

    public string? LastReviewedRecordId { get; private set; }
    public ReviewRecordRequest? LastReview { get; private set; }
    public SubmitRecordRequest? LastSubmission { get; private set; }
    public int? LastMovePosition { get; private set; }

    public Task<IReadOnlyList<DemonView>> GetDemonsAsync(string baseUrl, CancellationToken ct = default)
    {
        Fail();
        DemonsReadCount++;
        return Task.FromResult<IReadOnlyList<DemonView>>(Demons.OrderBy(d => d.Position).ToList());
    }

    public Task<IReadOnlyList<OpinionView>> GetOpinionsAsync(
        string baseUrl, string demonId, CancellationToken ct = default)
    {
        Fail();
        var list = Opinions.TryGetValue(demonId, out var stored) ? stored : new List<OpinionView>();
        return Task.FromResult<IReadOnlyList<OpinionView>>(list.ToList());
    }

    public Task<OpinionView> PostOpinionAsync(
        string baseUrl, string accessToken, string demonId, OpinionRequest request, CancellationToken ct = default)
    {
        Fail();
        PostOpinionCount++;

        var opinion = new OpinionView(
            Guid.NewGuid().ToString(),
            Profile.UserId,
            Profile.Username,
            Profile.Role,
            request.SuggestedPosition,
            request.Text,
            DateTime.UtcNow);

        if (!Opinions.TryGetValue(demonId, out var list))
            Opinions[demonId] = list = new List<OpinionView>();

        list.Add(opinion);
        return Task.FromResult(opinion);
    }

    public Task DeleteOpinionAsync(
        string baseUrl, string accessToken, string opinionId, CancellationToken ct = default)
    {
        Fail();
        foreach (var list in Opinions.Values)
            list.RemoveAll(o => o.Id == opinionId);

        return Task.CompletedTask;
    }

    public Task<DemonView> MoveDemonAsync(
        string baseUrl, string accessToken, string demonId, int position, CancellationToken ct = default)
    {
        Fail();
        MoveCount++;
        LastMovePosition = position;

        var demon = Demons.First(d => d.Id == demonId);
        Demons.Remove(demon);
        Demons.Insert(Math.Min(position - 1, Demons.Count), demon);
        Renumber();

        return Task.FromResult(Demons.First(d => d.Id == demonId));
    }

    public Task<DemonView> AddDemonAsync(
        string baseUrl, string accessToken, AddDemonRequest request, CancellationToken ct = default)
    {
        Fail();
        AddDemonCount++;

        var demon = new DemonView(
            Guid.NewGuid().ToString(),
            request.Position,
            request.Name,
            request.Publisher ?? string.Empty,
            request.Verifier ?? string.Empty,
            request.LevelId,
            request.Video,
            request.Thumbnail,
            request.Requirement ?? 100,
            OpinionCount: 0);

        Demons.Insert(Math.Min(request.Position - 1, Demons.Count), demon);
        Renumber();

        return Task.FromResult(demon);
    }

    public Task DeleteDemonAsync(string baseUrl, string accessToken, string demonId, CancellationToken ct = default)
    {
        Fail();
        DeleteDemonCount++;
        Demons.RemoveAll(d => d.Id == demonId);
        Renumber();
        return Task.CompletedTask;
    }

    public Task<CommunityProfile> GetProfileAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
    {
        Fail();
        return Task.FromResult(Profile);
    }

    public Task<IReadOnlyList<RecordView>> GetTopRecordsAsync(string baseUrl, CancellationToken ct = default)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<RecordView>>(TopRecords.ToList());
    }

    public Task<IReadOnlyList<RecordView>> GetMyRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<RecordView>>(MyRecords.ToList());
    }

    public Task<IReadOnlyList<RecordView>> GetPendingRecordsAsync(
        string baseUrl, string accessToken, CancellationToken ct = default)
    {
        Fail();
        return Task.FromResult<IReadOnlyList<RecordView>>(PendingRecords.ToList());
    }

    public Task<RecordView> SubmitRecordAsync(
        string baseUrl, string accessToken, SubmitRecordRequest request, CancellationToken ct = default)
    {
        Fail();
        SubmitRecordCount++;
        LastSubmission = request;

        var record = new RecordView(
            Guid.NewGuid().ToString(),
            Profile.UserId,
            Profile.Username,
            Profile.Role,
            request.DemonId,
            null,
            request.LevelName ?? string.Empty,
            request.Progress,
            request.VideoUrl,
            request.HasClicks,
            request.Comment,
            RecordStatus.Pending,
            null,
            DateTime.UtcNow,
            null,
            null);

        MyRecords.Add(record);
        PendingRecords.Add(record);
        return Task.FromResult(record);
    }

    public Task<RecordView> ReviewRecordAsync(
        string baseUrl, string accessToken, string recordId, ReviewRecordRequest request, CancellationToken ct = default)
    {
        Fail();
        ReviewCount++;
        LastReviewedRecordId = recordId;
        LastReview = request;

        var record = PendingRecords.First(r => r.Id == recordId);
        PendingRecords.Remove(record);

        var reviewed = record with
        {
            Status = request.Approve ? RecordStatus.Approved : RecordStatus.Rejected,
            Placement = request.Approve ? request.Placement ?? TopRecords.Count + 1 : null,
            ReviewNote = request.Note,
        };

        if (request.Approve)
            TopRecords.Add(reviewed);

        return Task.FromResult(reviewed);
    }

    public Task<IReadOnlyList<ManagedUserView>> GetUsersAsync(
        string baseUrl, string accessToken, string? query, CancellationToken ct = default)
    {
        Fail();
        var rows = string.IsNullOrWhiteSpace(query)
            ? Users
            : Users.Where(u => u.Username.Contains(query, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult<IReadOnlyList<ManagedUserView>>(rows.ToList());
    }

    public Task<ManagedUserView> SetBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default)
        => Task.FromResult(Update(userId, user => user with
        {
            Banned = request.Banned,
            BanReason = request.Reason,
        }));

    public Task<ManagedUserView> SetListBanAsync(
        string baseUrl, string accessToken, string userId, BanRequest request, CancellationToken ct = default)
        => Task.FromResult(Update(userId, user => user with
        {
            ListBanned = request.Banned,
            ListBanReason = request.Reason,
        }));

    /// <summary>Кладёт в список демона с заданным местом — чтобы тесты читались.</summary>
    public DemonView AddDemon(int position, string name)
    {
        var demon = new DemonView(
            $"demon-{position}", position, name, "автор", "верификатор",
            LevelId: 100 + position, Video: "https://example.com/video", Thumbnail: null,
            Requirement: 100, OpinionCount: 0);

        Demons.Add(demon);
        return demon;
    }

    private ManagedUserView Update(string userId, Func<ManagedUserView, ManagedUserView> change)
    {
        Fail();
        var index = Users.FindIndex(u => u.UserId == userId);
        Users[index] = change(Users[index]);
        return Users[index];
    }

    private void Renumber()
    {
        for (var i = 0; i < Demons.Count; i++)
            Demons[i] = Demons[i] with { Position = i + 1 };
    }

    private void Fail()
    {
        if (ServerDown)
            throw new CloudException(CloudErrorKind.Network, "Сервер синхронизации недоступен.");
    }
}

/// <summary>Локальная копия списка в памяти: тесты не должны трогать файлы пользователя.</summary>
internal sealed class InMemoryDemonListCache : IDemonListCache
{
    private DemonListSnapshot? _saved;

    public int SaveCallCount { get; private set; }

    public DemonListSnapshot? Load() => _saved;

    public void Save(IReadOnlyList<DemonView> demons)
    {
        SaveCallCount++;
        _saved = new DemonListSnapshot(demons.ToList(), DateTime.UtcNow);
    }

    /// <summary>Подкладывает копию так, как если бы она осталась от прошлого запуска.</summary>
    public void Preload(IReadOnlyList<DemonView> demons)
        => _saved = new DemonListSnapshot(demons.ToList(), DateTime.UtcNow.AddHours(-2));
}

/// <summary>
/// Тесты вкладки «Демонлист». Главные требования те же, что у остальной облачной
/// части: список читается без аккаунта, недоступный сервер не роняет вкладку,
/// а кнопки, на которые у пользователя нет прав, ему не показываются.
/// </summary>
public class DemonListTests
{
    [Fact]
    public async Task List_is_read_without_signing_in()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.AddDemon(2, "Acheron");

        await vm.LoadAsync();

        vm.Demons.Should().HaveCount(2);
        vm.IsSignedIn.Should().BeFalse();
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Demons_are_grouped_into_the_same_tiers_as_on_the_site()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.AddDemon(1, "Основной");
        client.AddDemon(80, "Расширенный");
        client.AddDemon(160, "Легаси");

        await vm.LoadAsync();

        vm.Demons.Select(d => d.TierTitle).Should()
            .Equal("Основной список", "Расширенный список", "Легаси");
    }

    [Fact]
    public async Task Guest_gets_no_moderation_tools()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");

        await vm.LoadAsync();

        vm.CanModerate.Should().BeFalse();
        vm.CanBan.Should().BeFalse();
        vm.CanParticipate.Should().BeFalse("без входа мнение оставить нельзя");
    }

    [Fact]
    public async Task Unreachable_server_shows_the_saved_list_read_only()
    {
        var (vm, client, cache, _) = CreateViewModel();
        cache.Preload(new[] { Demon(1, "Tidal Wave") });
        client.ServerDown = true;

        await vm.LoadAsync();

        vm.Demons.Should().HaveCount(1);
        vm.IsOffline.Should().BeTrue();
        vm.OfflineNotice.Should().NotBeNull();
        vm.CanParticipate.Should().BeFalse("без сервера менять нечего");
        vm.Error.Should().BeNull("сохранённая копия — это не ошибка, а запасной вариант");
    }

    [Fact]
    public async Task Unreachable_server_without_a_saved_list_reports_the_failure()
    {
        var (vm, client, _, _) = CreateViewModel();
        client.ServerDown = true;

        await vm.LoadAsync();

        vm.Demons.Should().BeEmpty();
        vm.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Successful_read_refreshes_the_local_copy()
    {
        var (vm, client, cache, _) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");

        await vm.LoadAsync();

        cache.SaveCallCount.Should().Be(1);
        cache.Load()!.Demons.Should().HaveCount(1);
    }

    [Fact]
    public async Task Opening_a_demon_loads_its_opinions_and_closes_the_previous_one()
    {
        var (vm, client, _, _) = CreateViewModel();
        var first = client.AddDemon(1, "Tidal Wave");
        client.AddDemon(2, "Acheron");
        client.Opinions[first.Id] = new List<OpinionView>
        {
            new("op-1", "user-2", "somebody", UserRole.Member, 3, "Слишком высоко.", DateTime.UtcNow),
        };

        await vm.LoadAsync();
        await vm.ToggleDemonCommand.ExecuteAsync(vm.Demons[0]);
        await vm.ToggleDemonCommand.ExecuteAsync(vm.Demons[1]);

        vm.Demons[0].IsExpanded.Should().BeFalse("раскрытым держится только один демон");
        vm.Demons[1].IsExpanded.Should().BeTrue();
        vm.Demons[0].Opinions.Should().ContainSingle();
        vm.Demons[1].HasNoOpinions.Should().BeTrue();
    }

    [Fact]
    public async Task Signed_in_member_can_participate_but_not_moderate()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        await SignInAsync(account);

        await vm.LoadAsync();

        vm.CanParticipate.Should().BeTrue();
        vm.CanModerate.Should().BeFalse();
        vm.CanBan.Should().BeFalse();
    }

    [Fact]
    public async Task Too_short_opinion_never_reaches_the_server()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.Demons[0].NewOpinionText = "ок";
        await vm.PostOpinionCommand.ExecuteAsync(vm.Demons[0]);

        client.PostOpinionCount.Should().Be(0);
        vm.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task Posted_opinion_appears_under_the_demon()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.Demons[0].NewOpinionText = "Место занижено, уровень сложнее соседей.";
        vm.Demons[0].NewOpinionPosition = "1";
        await vm.PostOpinionCommand.ExecuteAsync(vm.Demons[0]);

        client.PostOpinionCount.Should().Be(1);
        vm.Demons[0].Opinions.Should().ContainSingle();
        vm.Demons[0].OpinionCount.Should().Be(1);
        vm.Demons[0].NewOpinionText.Should().BeEmpty("после отправки форма очищается");
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Own_opinion_can_be_removed_by_its_author()
    {
        var (vm, client, _, account) = CreateViewModel();
        var demon = client.AddDemon(1, "Tidal Wave");
        client.Opinions[demon.Id] = new List<OpinionView>
        {
            new("op-mine", "user-1", "gd-player", UserRole.Member, null, "Моё мнение.", DateTime.UtcNow),
            new("op-other", "user-2", "somebody", UserRole.Member, null, "Чужое мнение.", DateTime.UtcNow),
        };
        await SignInAsync(account);
        await vm.LoadAsync();
        await vm.ToggleDemonCommand.ExecuteAsync(vm.Demons[0]);

        vm.Demons[0].Opinions.Single(o => o.Id == "op-mine").CanRemove.Should().BeTrue();
        vm.Demons[0].Opinions.Single(o => o.Id == "op-other").CanRemove.Should().BeFalse();
    }

    [Fact]
    public async Task Moderator_moves_a_demon_and_the_list_is_reread()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.AddDemon(2, "Acheron");
        client.Profile = Moderator(client.Profile);
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.Demons[1].MovePosition = "1";
        await vm.MoveDemonCommand.ExecuteAsync(vm.Demons[1]);

        client.MoveCount.Should().Be(1);
        client.LastMovePosition.Should().Be(1);
        vm.Demons[0].Name.Should().Be("Acheron");
        vm.Error.Should().BeNull();
    }

    [Fact]
    public async Task Arrows_move_a_demon_one_place_and_stop_at_the_edges()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.AddDemon(2, "Acheron");
        client.Profile = Moderator(client.Profile);
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.Demons[0].CanMoveUp.Should().BeFalse("первое место — верхний край списка");
        vm.Demons[1].CanMoveDown.Should().BeFalse("последнее место — нижний край");

        await vm.MoveDemonUpCommand.ExecuteAsync(vm.Demons[1]);

        client.LastMovePosition.Should().Be(1);
        vm.Demons[0].Name.Should().Be("Acheron");
    }

    [Fact]
    public async Task Demon_is_removed_only_after_confirmation()
    {
        var (vm, client, _, account) = CreateViewModel(out var confirmation);
        client.AddDemon(1, "Tidal Wave");
        client.Profile = Moderator(client.Profile);
        confirmation.Result = false;
        await SignInAsync(account);
        await vm.LoadAsync();

        await vm.DeleteDemonCommand.ExecuteAsync(vm.Demons[0]);

        confirmation.CallCount.Should().Be(1);
        client.DeleteDemonCount.Should().Be(0);
    }

    [Fact]
    public async Task Record_without_visible_clicks_is_not_submitted()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.SelectedRecordDemon = vm.Demons[0];
        vm.RecordVideoUrl = "https://example.com/run";
        vm.RecordHasClicks = false;
        await vm.SubmitRecordCommand.ExecuteAsync(null);

        client.SubmitRecordCount.Should().Be(0);
        vm.Error.Should().Contain("клик");
    }

    [Fact]
    public async Task Submitted_record_goes_to_the_queue_with_the_chosen_demon()
    {
        var (vm, client, _, account) = CreateViewModel();
        var demon = client.AddDemon(1, "Tidal Wave");
        client.Profile = Moderator(client.Profile);
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.SelectedRecordDemon = vm.Demons[0];
        vm.RecordProgress = "100";
        vm.RecordVideoUrl = "https://example.com/run";
        vm.RecordHasClicks = true;
        await vm.SubmitRecordCommand.ExecuteAsync(null);

        client.SubmitRecordCount.Should().Be(1);
        client.LastSubmission!.DemonId.Should().Be(demon.Id);
        client.LastSubmission.LevelName.Should().Be("Tidal Wave");
        vm.PendingRecords.Should().ContainSingle();
        vm.MyRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task Approved_record_moves_from_the_queue_to_the_top()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.Profile = Moderator(client.Profile);
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.SelectedRecordDemon = vm.Demons[0];
        vm.RecordVideoUrl = "https://example.com/run";
        vm.RecordHasClicks = true;
        await vm.SubmitRecordCommand.ExecuteAsync(null);

        vm.PendingRecords[0].ReviewPlacement = "1";
        await vm.ApproveRecordCommand.ExecuteAsync(vm.PendingRecords[0]);

        client.ReviewCount.Should().Be(1);
        client.LastReview!.Approve.Should().BeTrue();
        client.LastReview.Placement.Should().Be(1);
        vm.PendingRecords.Should().BeEmpty();
        vm.TopRecords.Should().ContainSingle();
    }

    [Fact]
    public async Task Owner_sees_the_members_list_and_can_lift_a_ban()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.Profile = client.Profile with { CanModerate = true, CanBan = true, Role = UserRole.Owner };
        client.Users.Add(new ManagedUserView(
            "user-2", "other@example.com", "somebody", UserRole.Member,
            Banned: false, BanReason: null, BannedAtUtc: null,
            ListBanned: true, ListBanReason: "спам", ListBannedAtUtc: DateTime.UtcNow,
            CreatedAtUtc: DateTime.UtcNow));
        await SignInAsync(account);
        await vm.LoadAsync();

        vm.CanBan.Should().BeTrue();
        vm.Users.Should().ContainSingle();
        vm.Users[0].ListBanButtonText.Should().Be("Вернуть в список");

        await vm.ToggleListBanCommand.ExecuteAsync(vm.Users[0]);

        vm.Users[0].ListBanned.Should().BeFalse();
        vm.Users[0].ListBanButtonText.Should().Be("Бан в списке");
    }

    [Fact]
    public async Task Member_banned_in_the_list_reads_it_but_cannot_participate()
    {
        var (vm, client, _, account) = CreateViewModel();
        client.AddDemon(1, "Tidal Wave");
        client.Profile = client.Profile with { ListBanned = true, ListBanReason = "спам" };
        await SignInAsync(account);

        await vm.LoadAsync();

        vm.Demons.Should().ContainSingle();
        vm.IsListBanned.Should().BeTrue();
        vm.CanParticipate.Should().BeFalse();
        vm.ListBanNotice.Should().Contain("спам");
    }

    private static DemonView Demon(int position, string name) => new(
        $"demon-{position}", position, name, "автор", "верификатор",
        LevelId: null, Video: null, Thumbnail: null, Requirement: 100, OpinionCount: 0);

    private static CommunityProfile Moderator(CommunityProfile profile)
        => profile with { Role = UserRole.Moderator, CanModerate = true };

    private static Task SignInAsync(CloudAccountService account)
        => account.SignInAsync("gd@example.com", "пароль-подлиннее");

    private static (DemonListViewModel Vm, FakeCommunityClient Client, InMemoryDemonListCache Cache, CloudAccountService Account)
        CreateViewModel() => CreateViewModel(out _);

    private static (DemonListViewModel Vm, FakeCommunityClient Client, InMemoryDemonListCache Cache, CloudAccountService Account)
        CreateViewModel(out FakeConfirmation confirmation)
    {
        var settings = new InMemoryCloudSettings();
        var account = new CloudAccountService(new FakeCloudClient(), new InMemoryTokenStore(), settings);
        var client = new FakeCommunityClient();
        var cache = new InMemoryDemonListCache();
        var community = new CommunityService(client, account, cache);
        confirmation = new FakeConfirmation();

        return (new DemonListViewModel(community, account, confirmation), client, cache, account);
    }
}
