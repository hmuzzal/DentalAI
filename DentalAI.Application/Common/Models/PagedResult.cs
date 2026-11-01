namespace DentalAI.Application.Common.Models;

/// <summary>One page of a list. <c>Page</c> is 1-based; <c>TotalCount</c> counts every match, not just this page.</summary>
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
