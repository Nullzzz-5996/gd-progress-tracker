using FluentAssertions;
using GdTracker.Core;
using GdTracker.Core.Services;

namespace GdTracker.Tests;

public class ProgressValidatorTests
{
    [Fact]
    public void FromZero_with_valid_reached_is_valid()
    {
        ProgressValidator.Validate(RunType.FromZero, 0, 45).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Segment_from_30_to_60_is_valid()
    {
        ProgressValidator.Validate(RunType.Segment, 30, 60).IsValid.Should().BeTrue();
    }

    [Fact]
    public void FromZero_reaching_100_is_valid()
    {
        ProgressValidator.Validate(RunType.FromZero, 0, 100).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Reached_equal_to_start_is_invalid()
    {
        ProgressValidator.Validate(RunType.Segment, 50, 50).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reached_below_start_is_invalid()
    {
        ProgressValidator.Validate(RunType.Segment, 60, 50).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Reached_above_100_is_invalid()
    {
        ProgressValidator.Validate(RunType.FromZero, 0, 101).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Negative_start_is_invalid()
    {
        ProgressValidator.Validate(RunType.Segment, -5, 50).IsValid.Should().BeFalse();
    }

    [Fact]
    public void FromZero_with_nonzero_start_is_invalid()
    {
        ProgressValidator.Validate(RunType.FromZero, 10, 50).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Segment_with_zero_start_is_invalid()
    {
        ProgressValidator.Validate(RunType.Segment, 0, 50).IsValid.Should().BeFalse();
    }
}
