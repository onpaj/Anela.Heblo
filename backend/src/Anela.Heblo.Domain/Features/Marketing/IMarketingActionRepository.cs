using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public interface IMarketingActionRepository : IRepository<MarketingAction, int>
    {
        Task<PagedResult<MarketingAction>> GetPagedAsync(
            MarketingActionQueryCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<List<MarketingAction>> GetForCalendarAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);

        Task<List<MarketingAction>> GetByOutlookEventIdsAsync(IReadOnlyCollection<string> outlookEventIds, CancellationToken cancellationToken = default);
    }
}
