using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            string? searchText,
            DateTime? dateFrom,
            DateTime? dateTo,
            string? productCodePrefix,
            IReadOnlyCollection<int>? tagIds,
            string? createdByUserId,
            int pageNumber,
            int pageSize,
            string sortBy,
            string sortDirection,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }
}
