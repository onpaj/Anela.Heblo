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

        /// <summary>
        /// Non-deleted actions linked to an Outlook event whose StartDate lies within
        /// [fromUtc, toUtc]. Used to find actions whose event no longer exists in Outlook.
        /// </summary>
        Task<List<MarketingAction>> GetSyncedInWindowAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);
    }
}
