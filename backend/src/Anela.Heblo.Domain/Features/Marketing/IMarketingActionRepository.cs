using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Domain.Features.Journal;
using Anela.Heblo.Xcc.Persistance;

namespace Anela.Heblo.Domain.Features.Marketing
{
    public interface IMarketingActionRepository : IRepository<MarketingAction, int>
    {
        Task DeleteSoftAsync(int id, string userId, string username, CancellationToken cancellationToken = default);

        Task<PagedResult<MarketingAction>> GetPagedAsync(
            MarketingActionQueryCriteria criteria,
            CancellationToken cancellationToken = default);

        Task<List<MarketingAction>> GetForCalendarAsync(
            DateTime from,
            DateTime to,
            CancellationToken cancellationToken = default);
    }
}
