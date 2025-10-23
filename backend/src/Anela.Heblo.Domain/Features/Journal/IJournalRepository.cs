using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Journal
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            JournalQueryCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            JournalSearchCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicator>> GetJournalIndicatorsAsync(
            IEnumerable<string> productCodes,
            CancellationToken cancellationToken = default);
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int PageNumber { get; set; }
        public int PageSize { get; set; }
    }
}