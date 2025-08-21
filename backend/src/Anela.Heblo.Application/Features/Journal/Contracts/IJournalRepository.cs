using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Application.Features.Journal.Contracts
{
    public interface IJournalRepository : IRepository<JournalEntry, int>
    {
        Task DeleteSoftAsync(int id, string userId, CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> GetEntriesAsync(
            GetJournalEntriesRequest request,
            CancellationToken cancellationToken = default);

        Task<PagedResult<JournalEntry>> SearchEntriesAsync(
            SearchJournalEntriesRequest request,
            CancellationToken cancellationToken = default);

        Task<List<JournalEntry>> GetEntriesByProductAsync(
            string productCode,
            CancellationToken cancellationToken = default);

        Task<Dictionary<string, JournalIndicatorDto>> GetJournalIndicatorsAsync(
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