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
    public void Counts_a_single_working_day_inclusively()
    {
        Assert.Equal(1m, WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 15)));
    }

    [Fact]
    public void Counts_zero_for_a_single_weekend_day()
    {
        Assert.Equal(0m, WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 20)));
    }

    [Fact]
    public void A_holiday_on_a_weekend_does_not_reduce_the_working_day_count_twice()
    {
        var days = WorkingDayCalculator.CountWorkingDays(
            new DateOnly(2026, 6, 15),
            new DateOnly(2026, 6, 21),
            new[] { new DateOnly(2026, 6, 20), new DateOnly(2026, 6, 21) });
        Assert.Equal(5m, days);
    }

    [Fact]
    public void Duplicate_holidays_are_counted_once()
    {
        var holiday = new DateOnly(2026, 6, 17);
        var days = WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), new[] { holiday, holiday });
        Assert.Equal(4m, days);
    }

    [Fact]
    public void Returns_zero_when_every_weekday_is_a_holiday()
    {
        var holidays = Enumerable.Range(15, 5).Select(day => new DateOnly(2026, 6, day));
        Assert.Equal(0m, WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), holidays));
    }

    [Fact]
    public void Null_holiday_collection_is_supported()
    {
        Assert.Equal(5m, WorkingDayCalculator.CountWorkingDays(new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 19), null));
    }

    [Fact]
    public void Ranges_overlap_detection_is_inclusive()
    {
        Assert.True(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20)));
        Assert.False(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 9), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 20)));
    }

    [Fact]
    public void Ranges_overlap_detects_nested_and_reversed_ranges()
    {
        Assert.True(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 12)));
        Assert.True(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 10), new DateOnly(2026, 1, 12), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)));
        Assert.False(WorkingDayCalculator.RangesOverlap(new DateOnly(2026, 1, 20), new DateOnly(2026, 1, 25), new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 10)));
    }
}

public class EmploymentStatusPolicyTests
{
    [Theory]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.Suspended, true)]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Pending, EmploymentStatus.OnLeave, false)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.OnLeave, true)]
    [InlineData(EmploymentStatus.Active, EmploymentStatus.Suspended, true)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Suspended, true)]
    [InlineData(EmploymentStatus.OnLeave, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.Terminated, true)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.Active, true)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.OnLeave, false)]
    [InlineData(EmploymentStatus.Terminated, EmploymentStatus.Suspended, false)]
    [InlineData(EmploymentStatus.Suspended, EmploymentStatus.OnLeave, false)]
    public void Validates_transitions(EmploymentStatus from, EmploymentStatus to, bool expected)
    {
        Assert.Equal(expected, EmploymentStatusPolicy.CanTransition(from, to));
    }

    [Theory]
    [InlineData(EmploymentStatus.Pending)]
    [InlineData(EmploymentStatus.Active)]
    [InlineData(EmploymentStatus.OnLeave)]
    [InlineData(EmploymentStatus.Suspended)]
    [InlineData(EmploymentStatus.Terminated)]
    public void A_status_can_always_transition_to_itself(EmploymentStatus status)
    {
        Assert.True(EmploymentStatusPolicy.CanTransition(status, status));
    }
}
