using Emplyo.Domain.Enums;
using Emplyo.Domain.ValueObjects;
using Xunit;

namespace Emplyo.UnitTests;

public class WorkingDayCalculatorTests
{
    [Fact]
    public void Excludes_weekends()
    {
        // Mon 2026-06-15 .. Sun 2026-06-21 => Mon-Fri = 5 working days.
        var days = WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 21));
        Assert.Equal(5m, days);
    }

    [Fact]
    public void Excludes_holidays()
    {
        var holidays = new[] { new DateOnly(2026, 6, 17) };
        var days = WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), holidays);
        Assert.Equal(4m, days);
    }

    [Fact]
    public void Returns_zero_when_end_before_start()
    {
        var days = WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 10));
        Assert.Equal(0m, days);
    }

    [Fact]
    public void Ranges_overlap_detection_is_inclusive()
    {
        Assert.True(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20)));
        Assert.False(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20)));
    }
}

public class EmploymentStatusPolicyTests
{
    [Theory]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.OnLeave, true)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.OnLeave, false)]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.OnLeave, false)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.OnLeave, false)]
    public void Validates_transitions(EmploymentStatus from, EmploymentStatus to, bool expected)
    {
        Assert.Equal(expected, EmploymentStatusPolicy.CanTransition(from, to));
    }
}
