using FluentAssertions;
using GdTracker.Core;
using GdTracker.Sharing;

namespace GdTracker.Tests;

public class ProgressPackageSerializerTests
{
    private static ProgressPackage Sample() => new()
    {
        Version = 1,
        ExportedAt = new DateTime(2026, 6, 4, 10, 0, 0, DateTimeKind.Utc),
        Levels =
        {
            new PackageLevel
            {
                GdLevelId = 10565740, Name = "Bloodbath", Source = LevelSource.Online, Stars = 10,
                Records =
                {
                    new PackageRecord
                    {
                        Type = RunType.FromZero, StartPercent = 0, ReachedPercent = 6,
                        Mode = ProgressMode.Normal, Attempts = 2180,
                        Date = new DateTime(2026, 6, 1, 9, 0, 0, DateTimeKind.Utc),
                        Note = "best", Source = ProgressSource.Manual,
                    },
                },
            },
        },
    };

    [Fact]
    public void Roundtrip_preserves_package()
    {
        var json = ProgressPackageSerializer.Serialize(Sample());
        var back = ProgressPackageSerializer.Deserialize(json);

        back.Levels.Should().ContainSingle();
        var level = back.Levels[0];
        level.Name.Should().Be("Bloodbath");
        level.GdLevelId.Should().Be(10565740);
        level.Source.Should().Be(LevelSource.Online);
        level.Stars.Should().Be(10);

        var rec = level.Records.Should().ContainSingle().Subject;
        rec.Type.Should().Be(RunType.FromZero);
        rec.ReachedPercent.Should().Be(6);
        rec.Mode.Should().Be(ProgressMode.Normal);
        rec.Attempts.Should().Be(2180);
        rec.Source.Should().Be(ProgressSource.Manual);
    }

    [Fact]
    public void Enums_serialized_as_readable_strings()
    {
        var json = ProgressPackageSerializer.Serialize(Sample());
        json.Should().Contain("Online");
        json.Should().Contain("Normal");
        json.Should().Contain("FromZero");
    }
}
