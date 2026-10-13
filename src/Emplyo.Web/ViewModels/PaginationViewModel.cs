namespace Emplyo.Web.ViewModels;

public class PaginationViewModel
{
    public int CurrentPage { get; init; }
    public int TotalPages { get; init; }
    public int TotalCount { get; init; }
    public int PageSize { get; init; }
    public bool HasPrevious { get; init; }
    public bool HasNext { get; init; }
    public string Action { get; init; } = "Index";

    public static PaginationViewModel From<T>(Emplyo.Application.Common.Models.PagedResult<T> result, string action = "Index")
        => new()
        {
            CurrentPage = result.Page,
            TotalPages = result.TotalPages,
            TotalCount = result.TotalCount,
            PageSize = result.PageSize,
            HasPrevious = result.HasPrevious,
            HasNext = result.HasNext,
            Action = action
        };
}
