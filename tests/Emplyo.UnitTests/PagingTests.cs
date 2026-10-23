using Emplyo.Application.Common.Models;
using Xunit;

namespace Emplyo.UnitTests;

public class PagingTests
{
    [Theory]
    [InlineData(0, 0, 1, Paging.DefaultPageSize)]
    [InlineData(-5, -10, 1, Paging.DefaultPageSize)]
    [InlineData(2, 25, 2, 25)]
    [InlineData(int.MaxValue, Paging.MaxPageSize + 1, int.MaxValue, Paging.MaxPageSize)]
    public void Normalize_applies_page_boundaries(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var (normalizedPage, normalizedPageSize) = Paging.Normalize(page, pageSize);

        Assert.Equal(expectedPage, normalizedPage);
        Assert.Equal(expectedPageSize, normalizedPageSize);
    }

    [Fact]
    public void Paged_result_computes_total_pages_and_navigation_flags()
    {
        var result = PagedResult<int>.Create(new[] { 1, 2, 3 }, page: 2, pageSize: 3, totalCount: 8);

        Assert.Equal(3, result.TotalPages);
        Assert.True(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    [Fact]
    public void Paged_result_reports_no_navigation_on_a_single_page()
    {
        var result = PagedResult<int>.Create(new[] { 1, 2 }, page: 1, pageSize: 10, totalCount: 2);

        Assert.Equal(1, result.TotalPages);
        Assert.False(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    [Fact]
    public void Paged_result_reports_zero_pages_for_an_empty_result()
    {
        var result = PagedResult<int>.Create(Array.Empty<int>(), page: 1, pageSize: 15, totalCount: 0);

        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    [Fact]
    public void Paged_result_guards_against_a_zero_page_size()
    {
        var result = PagedResult<int>.Create(new[] { 1 }, page: 1, pageSize: 0, totalCount: 1);

        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNext);
    }

    [Fact]
    public void Paged_result_rounds_a_partial_last_page_up()
    {
        var result = PagedResult<int>.Create(new[] { 1 }, page: 4, pageSize: 3, totalCount: 10);

        Assert.Equal(4, result.TotalPages);
        Assert.True(result.HasPrevious);
        Assert.False(result.HasNext);
    }
}
