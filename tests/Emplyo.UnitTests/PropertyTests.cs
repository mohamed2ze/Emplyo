using Emplyo.Application.Common.Models;
using Emplyo.Domain.ValueObjects;
using Xunit;

namespace Emplyo.UnitTests;

public class WorkingDayCalculatorPropertyTests
{
    [Fact]
    public void Random_ranges_satisfy_counting_invariants()
    {
        var random = new Random(20260615);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var start = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 365 * 6));
            var end = start.AddDays(random.Next(1, 60));
            var holidays = Enumerable.Range(0, random.Next(0, 5))
                .Select(_ => start.AddDays(random.Next(0, 61)))
                .ToList();

            var count = WorkingDayCalculator.CountWorkingDays(start, end, holidays);
            var weekdayCount = CountWeekdays(start, end);

            Assert.InRange(count, 0m, weekdayCount);
            Assert.True(count <= weekdayCount);
            Assert.True(WorkingDayCalculator.CountWorkingDays(end, start, holidays) == 0m);
            Assert.True(WorkingDayCalculator.CountWorkingDays(start, end, null) >= count);
        }
    }

    [Fact]
    public void Adding_a_weekday_holiday_reduces_the_count_by_exactly_one()
    {
        var random = new Random(42);
        for (var iteration = 0; iteration < 200; iteration++)
        {
            var start = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 365 * 6));
            var end = start.AddDays(random.Next(6, 60));
            var weekday = start;
            while (weekday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                weekday = weekday.AddDays(1);
            }

            var without = WorkingDayCalculator.CountWorkingDays(start, end);
            var with = WorkingDayCalculator.CountWorkingDays(start, end, new[] { weekday });

            Assert.Equal(without - 1m, with);
        }
    }

    [Fact]
    public void Duplicate_holiday_inputs_do_not_change_the_result()
    {
        var random = new Random(7);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var start = new DateOnly(2024, 1, 1).AddDays(random.Next(0, 365 * 6));
            var end = start.AddDays(random.Next(0, 40));
            var holiday = start.AddDays(random.Next(0, 41));

            var single = WorkingDayCalculator.CountWorkingDays(start, end, new[] { holiday });
            var duplicates = WorkingDayCalculator.CountWorkingDays(start, end, new[] { holiday, holiday, holiday });

            Assert.Equal(single, duplicates);
        }
    }

    private static decimal CountWeekdays(DateOnly start, DateOnly end)
    {
        decimal count = 0;
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)) count++;
        }
        return count;
    }
}

public class PagingPropertyTests
{
    [Fact]
    public void Normalized_paging_always_satisfies_the_contract()
    {
        var random = new Random(99);
        for (var iteration = 0; iteration < 1000; iteration++)
        {
            var page = random.Next(-10_000, 10_000);
            var pageSize = random.Next(-10_000, 10_000);

            var (normalizedPage, normalizedPageSize) = Paging.Normalize(page, pageSize);

            Assert.InRange(normalizedPage, 1, int.MaxValue);
            Assert.InRange(normalizedPageSize, 1, Paging.MaxPageSize);
        }
    }

    [Fact]
    public void Valid_paging_values_are_not_modified()
    {
        var random = new Random(100);
        for (var iteration = 0; iteration < 500; iteration++)
        {
            var page = random.Next(1, int.MaxValue);
            var pageSize = random.Next(1, Paging.MaxPageSize + 1);

            Assert.Equal((page, pageSize), Paging.Normalize(page, pageSize));
        }
    }
}
