using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class GdSearchResponseParserTests
{
    // Уровни через '|', поля key:value; затем '#' и секция создателей (userID:userName:accountID).
    private const string Levels =
        "1:10565740:2:Bloodbath:5:2:6:4993756:9:50:10:26663480:14:1383866:17:1:43:6:18:10:25:0" +
        "|" +
        "1:123:2:EasyLevel:6:777:9:10:10:50:14:5:17:0:18:2:25:0";

    private const string Creators = "4993756:Riot:9050610|777:Bob:1";

    private static readonly string Response = $"{Levels}#{Creators}#songs#1:0:9#hash";

    [Fact]
    public void Parses_two_levels()
        => GdSearchResponseParser.Parse(Response).Should().HaveCount(2);

    [Fact]
    public void Maps_creator_difficulty_and_stats_for_demon()
    {
        var bloodbath = GdSearchResponseParser.Parse(Response).Single(l => l.Id == 10565740);
        bloodbath.Name.Should().Be("Bloodbath");
        bloodbath.Creator.Should().Be("Riot");
        bloodbath.Difficulty.Should().Be("Extreme Demon");
        bloodbath.Stars.Should().Be(10);
        bloodbath.Downloads.Should().Be(26663480);
        bloodbath.Likes.Should().Be(1383866);
    }

    [Fact]
    public void Maps_non_demon_face_and_creator()
    {
        var easy = GdSearchResponseParser.Parse(Response).Single(l => l.Id == 123);
        easy.Creator.Should().Be("Bob");
        easy.Difficulty.Should().Be("Easy");
        easy.Stars.Should().Be(2);
    }

    [Fact]
    public void Empty_or_error_response_yields_no_results()
    {
        GdSearchResponseParser.Parse("").Should().BeEmpty();
        GdSearchResponseParser.Parse("-1").Should().BeEmpty();
    }
}
