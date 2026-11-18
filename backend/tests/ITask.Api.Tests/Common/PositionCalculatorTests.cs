using ITask.Api.Common.Ordering;

namespace ITask.Api.Tests.Common;

public sealed class PositionCalculatorTests
{
    [Fact]
    public void Append_starts_at_one_step_and_adds_a_step()
    {
        Assert.Equal(1024, PositionCalculator.Append(null));
        Assert.Equal(3072, PositionCalculator.Append(2048));
    }

    [Fact]
    public void Between_takes_the_midpoint()
    {
        Assert.Equal(1536, PositionCalculator.Between(1024, 2048));
    }

    [Fact]
    public void Between_with_no_left_neighbour_halves_towards_zero()
    {
        Assert.Equal(512, PositionCalculator.Between(null, 1024));
    }

    [Fact]
    public void Between_with_no_right_neighbour_appends()
    {
        Assert.Equal(3072, PositionCalculator.Between(2048, null));
        Assert.Equal(1024, PositionCalculator.Between(null, null));
    }

    [Theory]
    [InlineData(1.0, 1.0000001, true)]
    [InlineData(1.0, 1.00001, false)]
    [InlineData(null, 0.0000005, true)]
    [InlineData(1024.0, null, false)]
    public void NeedsRenumber_when_the_gap_is_under_the_threshold(double? before, double? after, bool expected)
    {
        Assert.Equal(expected, PositionCalculator.NeedsRenumber(before, after));
    }

    [Fact]
    public void Renumber_spaces_items_one_step_apart()
    {
        Assert.Equal([1024, 2048, 3072], PositionCalculator.Renumber(3));
        Assert.Empty(PositionCalculator.Renumber(0));
    }
}
