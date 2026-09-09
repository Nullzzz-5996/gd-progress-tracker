using FluentAssertions;
using GdTracker.Cloud;
using GdTracker.Sharing.Cloud;

namespace GdTracker.Tests;

/// <summary>
/// Сквозные тесты демонлиста: транспорт вкладки приложения против настоящего
/// сервера. Страница сайта ходит в те же адреса, поэтому проверка заодно
/// подтверждает, что вкладка и сайт работают с одними и теми же данными.
/// </summary>
public class CommunityClientApiTests : IClassFixture<OwnerApiFactory>
{
    private const string BaseUrl = "http://localhost";
    private const string Password = "пароль-подлиннее";

    private readonly OwnerApiFactory _factory;

    public CommunityClientApiTests(OwnerApiFactory factory) => _factory = factory;

    private CommunityClient CreateClient() => new(_factory.CreateClient());

    private CloudClient CreateAuthClient() => new(_factory.CreateClient());

    [Fact]
    public async Task Demon_list_is_read_without_signing_in()
    {
        var demons = await CreateClient().GetDemonsAsync(BaseUrl);

        demons.Should().NotBeEmpty("сервер наполняет пустой список стартовой расстановкой");
        demons.Select(d => d.Position).Should().BeInAscendingOrder();
        demons.First().Position.Should().Be(1);
    }

    [Fact]
    public async Task Member_opinion_comes_back_in_the_list()
    {
        var client = CreateClient();
        var token = await RegisterAsync();
        var demon = (await client.GetDemonsAsync(BaseUrl)).First();

        await client.PostOpinionAsync(
            BaseUrl, token, demon.Id, new OpinionRequest("Стоит выше, чем заслуживает.", demon.Position + 5));

        var opinions = await client.GetOpinionsAsync(BaseUrl, demon.Id);
        opinions.Should().ContainSingle(o => o.Text == "Стоит выше, чем заслуживает.");
        opinions.Single(o => o.SuggestedPosition == demon.Position + 5).Should().NotBeNull();
    }

    [Fact]
    public async Task Ordinary_member_cannot_move_a_demon()
    {
        var client = CreateClient();
        var token = await RegisterAsync();
        var demon = (await client.GetDemonsAsync(BaseUrl)).First();

        var act = () => client.MoveDemonAsync(BaseUrl, token, demon.Id, 2);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    [Fact]
    public async Task Owner_moves_a_demon_and_the_list_follows()
    {
        var client = CreateClient();
        var token = await SignInAsOwnerAsync();
        var demons = await client.GetDemonsAsync(BaseUrl);
        var second = demons[1];

        await client.MoveDemonAsync(BaseUrl, token, second.Id, 1);

        var moved = await client.GetDemonsAsync(BaseUrl);
        moved[0].Id.Should().Be(second.Id);
        moved.Select(d => d.Position).Should().Equal(Enumerable.Range(1, moved.Count));
    }

    [Fact]
    public async Task Approved_record_appears_in_the_public_top()
    {
        var client = CreateClient();
        var memberToken = await RegisterAsync();
        var demon = (await client.GetDemonsAsync(BaseUrl)).First();

        var submitted = await client.SubmitRecordAsync(BaseUrl, memberToken, new SubmitRecordRequest(
            demon.Id, demon.Name, 100, "https://example.com/run", HasClicks: true, "Клики видно."));
        submitted.Status.Should().Be(RecordStatus.Pending);

        var ownerToken = await SignInAsOwnerAsync();
        var pending = await client.GetPendingRecordsAsync(BaseUrl, ownerToken);
        pending.Should().Contain(r => r.Id == submitted.Id);

        await client.ReviewRecordAsync(
            BaseUrl, ownerToken, submitted.Id, new ReviewRecordRequest(Approve: true, Placement: 1, "Засчитано."));

        var top = await client.GetTopRecordsAsync(BaseUrl);
        top.Should().Contain(r => r.Id == submitted.Id && r.Status == RecordStatus.Approved);
    }

    [Fact]
    public async Task Record_without_clicks_is_rejected_by_the_server()
    {
        var client = CreateClient();
        var token = await RegisterAsync();

        var act = () => client.SubmitRecordAsync(BaseUrl, token, new SubmitRecordRequest(
            DemonId: null, "Свой уровень", 100, "https://example.com/run", HasClicks: false, Comment: null));

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Validation);
    }

    [Fact]
    public async Task Profile_tells_the_tab_what_the_account_may_do()
    {
        var client = CreateClient();

        var member = await client.GetProfileAsync(BaseUrl, await RegisterAsync());
        var owner = await client.GetProfileAsync(BaseUrl, await SignInAsOwnerAsync());

        member.CanModerate.Should().BeFalse();
        member.CanBan.Should().BeFalse();
        owner.CanModerate.Should().BeTrue();
        owner.CanBan.Should().BeTrue();
    }

    [Fact]
    public async Task Members_list_is_closed_to_everyone_but_the_owner()
    {
        var client = CreateClient();
        var token = await RegisterAsync();

        var act = () => client.GetUsersAsync(BaseUrl, token, query: null);

        (await act.Should().ThrowAsync<CloudException>()).Which.Kind.Should().Be(CloudErrorKind.Unauthorized);
    }

    /// <summary>Свежий участник: у каждого теста свой, чтобы они не мешали друг другу.</summary>
    private async Task<string> RegisterAsync()
    {
        var response = await CreateAuthClient().RegisterAsync(
            BaseUrl, $"gd-{Guid.NewGuid():N}@example.com", Password, $"gd-{Guid.NewGuid():N}"[..16]);

        return response.AccessToken;
    }

    /// <summary>
    /// Владелец задан настройкой фабрики, поэтому его аккаунт заводится один раз,
    /// а дальше в него входят: повторная регистрация тем же адресом уже не пройдёт.
    /// </summary>
    private async Task<string> SignInAsOwnerAsync()
    {
        var client = CreateAuthClient();

        try
        {
            var registered = await client.RegisterAsync(BaseUrl, OwnerApiFactory.OwnerEmail, Password, "owner");
            return registered.AccessToken;
        }
        catch (CloudException e) when (e.Kind == CloudErrorKind.Validation)
        {
            var signedIn = await client.LoginAsync(BaseUrl, OwnerApiFactory.OwnerEmail, Password);
            return signedIn.AccessToken;
        }
    }
}
