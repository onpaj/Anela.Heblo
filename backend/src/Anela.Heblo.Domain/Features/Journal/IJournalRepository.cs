using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            JournalQueryCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            JournalSearchCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicatorSnapshot>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }
}