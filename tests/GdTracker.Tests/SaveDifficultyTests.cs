using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class SaveDifficultyTests
{
    [Fact]
    public void Auto_wins()
        => SaveDifficulty.Resolve(auto: true, demon: true, demonType: 6, stars: 10).Should().Be("Auto");

    [Theory]
    [InlineData(3, "Easy Demon")]
    [InlineData(4, "Medium Demon")]
    [InlineData(1, "Hard Demon")]
    [InlineData(5, "Insane Demon")]
    [InlineData(6, "Extreme Demon")]
    [InlineData(0, "Demon")]
    [InlineData(2, "Demon")]
    public void Demon_subtypes(int demonType, string expected)
        => SaveDifficulty.Resolve(auto: false, demon: true, demonType: demonType, stars: 10).Should().Be(expected);

    [Theory]
    [InlineData(0, "Unrated")]
    [InlineData(1, "Auto")]
    [InlineData(2, "Easy")]
    [InlineData(3, "Normal")]
    [InlineData(4, "Hard")]
    [InlineData(6, "Harder")]
    [InlineData(8, "Insane")]
    [InlineData(10, "Demon")]
    public void Star_based(int stars, string expected)
        => SaveDifficulty.Resolve(auto: false, demon: false, demonType: 0, stars: stars).Should().Be(expected);

    [Fact]
    public void Out_of_range_stars_are_unrated()
        => SaveDifficulty.Resolve(false, false, 0, 200).Should().Be("Unrated");
}
