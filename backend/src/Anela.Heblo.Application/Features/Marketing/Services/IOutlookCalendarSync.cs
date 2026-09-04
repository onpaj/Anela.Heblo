using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public interface IOutlookCalendarSync
    {
        Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct);
        Task UpdateEventAsync(MarketingAction action, CancellationToken ct);
        Task DeleteEventAsync(string outlookEventId, CancellationToken ct);
        Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);

        /// <summary>
        /// Fetches a single event by id. Returns <c>null</c> when Graph reports 404
        /// (the event was deleted); throws <see cref="OutlookCalendarSyncException"/> on other failures.
        /// </summary>
        Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct);
    }
}
