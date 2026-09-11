using System;

namespace Anela.Heblo.Application.Features.Journal.Pagination
{
    /// <summary>
    /// Computes the pagination metadata (TotalPages, HasNextPage, HasPreviousPage) shared by
    /// GetJournalEntriesResponse and SearchJournalEntriesResponse. Extracted so the formula
    /// exists in exactly one place for both Journal list use cases.
    /// </summary>
    internal static class JournalPaginationCalculator
    {
        public static (int TotalPages, bool HasNextPage, bool HasPreviousPage) Calculate(
            int totalCount, int pageNumber, int pageSize)
        {
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);
            return (
                TotalPages: totalPages,
                HasNextPage: pageNumber * pageSize < totalCount,
                HasPreviousPage: pageNumber > 1);
        }
    }
}
