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
        /// Non-deleted actions linked to an Outlook event that overlap [fromUtc, toUtc].
        /// Overlap (not start-only) semantics deliberately mirror Graph's calendarView,
        /// which also returns events overlapping the queried window - otherwise a long
        /// event starting before fromUtc could never be reconciled when deleted upstream.
        /// An action without an EndDate counts as a point event at StartDate, so old
        /// open-ended actions are not dragged into the orphan candidate set.
        /// Used to find actions whose event no longer exists in Outlook.
        /// </summary>
        /// <summary>
        /// Drops the given actions from the change tracker. Called after a failed batch save
        /// so still-tracked Added/Modified entities cannot poison a later SaveChangesAsync
        /// in the same scope.
        /// </summary>
        void DetachRange(IEnumerable<MarketingAction> actions);

        Task<List<MarketingAction>> GetSyncedInWindowAsync(
            DateTime fromUtc,
            DateTime toUtc,
            CancellationToken cancellationToken = default);
    }
}
