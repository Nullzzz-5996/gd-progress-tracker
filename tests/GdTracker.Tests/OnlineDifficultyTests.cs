using FluentAssertions;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class OnlineDifficultyTests
{
    [Fact]
    public void Auto_wins()
        => OnlineDifficulty.Resolve(auto: true, demon: true, demonDiff: 6, difficulty: 50).Should().Be("Auto");

    [Theory]
    [InlineData(3, "Easy Demon")]
    [InlineData(4, "Medium Demon")]
    [InlineData(5, "Insane Demon")]
    [InlineData(6, "Extreme Demon")]
    [InlineData(0, "Hard Demon")]
    public void Demon_subtypes(int demonDiff, string expected)
        => OnlineDifficulty.Resolve(auto: false, demon: true, demonDiff: demonDiff, difficulty: 0).Should().Be(expected);

    [Theory]
    [InlineData(10, "Easy")]
    [InlineData(20, "Normal")]
    [InlineData(30, "Hard")]
    [InlineData(40, "Harder")]
    [InlineData(50, "Insane")]
    [InlineData(0, "Unrated")]
    public void Difficulty_face(int diff, string expected)
        => OnlineDifficulty.Resolve(auto: false, demon: false, demonDiff: 0, difficulty: diff).Should().Be(expected);
}
