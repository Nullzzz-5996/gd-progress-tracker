using FluentAssertions;
using GdTracker.Core;
using GdTracker.GameSync;

namespace GdTracker.Tests;

public class SaveFileParserTests
{
    // Репрезентативный sample по реальной структуре CCGameManager.dat.
    private const string Sample =
        "<?xml version=\"1.0\"?><plist version=\"1.0\" gjver=\"2.0\"><dict>" +
        "<k>GLM_01</k><d>" +
            "<k>1</k><d><k>kCEK</k><i>4</i><k>k1</k><i>1</i><k>k18</k><i>30</i>" +
                "<k>k89</k><t /><k>k19</k><i>100</i><k>k20</k><i>100</i><k>k26</k><i>1</i>" +
                "<k>k2</k><s>Stereo Madness</s></d>" +
            "<k>2</k><d><k>k1</k><i>2</i><k>k18</k><i>9</i><k>k19</k><i>45</i>" +
                "<k>k20</k><i>60</i><k>k2</k><s>Back On Track</s></d>" +
        "</d>" +
        "<k>GLM_03</k><d>" +
            "<k>13519</k><d><k>k1</k><i>13519</i><k>k18</k><i>158</i><k>k19</k><i>72</i>" +
                "<k>k20</k><i>88</i><k>k26</k><i>10</i><k>k2</k><s>The Nightmare</s></d>" +
        "</d>" +
        "</dict></plist>";

    [Fact]
    public void Parses_official_and_online_levels()
    {
        var levels = SaveFileParser.Parse(Sample);
        levels.Should().HaveCount(3);
    }

    [Fact]
    public void Reads_official_level_fields()
    {
        var levels = SaveFileParser.Parse(Sample);
        var sm = levels.Single(l => l.GdLevelId == 1);

        sm.Name.Should().Be("Stereo Madness");
        sm.Source.Should().Be(LevelSource.Official);
        sm.BestNormalPercent.Should().Be(100);
        sm.BestPracticePercent.Should().Be(100);
        sm.Attempts.Should().Be(30);
        sm.Stars.Should().Be(1);
    }

    [Fact]
    public void Reads_online_level_as_online_source()
    {
        var levels = SaveFileParser.Parse(Sample);
        var nightmare = levels.Single(l => l.GdLevelId == 13519);

        nightmare.Name.Should().Be("The Nightmare");
        nightmare.Source.Should().Be(LevelSource.Online);
        nightmare.BestNormalPercent.Should().Be(72);
        nightmare.BestPracticePercent.Should().Be(88);
        nightmare.Attempts.Should().Be(158);
        nightmare.Stars.Should().Be(10);
    }

    [Fact]
    public void Missing_optional_keys_default_to_zero_or_null()
    {
        var levels = SaveFileParser.Parse(Sample);
        var bot = levels.Single(l => l.GdLevelId == 2);

        bot.Stars.Should().BeNull();        // k26 отсутствует
        bot.BestNormalPercent.Should().Be(45);
    }

    [Fact]
    public void Empty_or_missing_glm_sections_return_empty()
    {
        var xml = "<?xml version=\"1.0\"?><plist version=\"1.0\"><dict>" +
                  "<k>valueKeeper</k><d><k>x</k><s>1</s></d></dict></plist>";
        SaveFileParser.Parse(xml).Should().BeEmpty();
    }
}
