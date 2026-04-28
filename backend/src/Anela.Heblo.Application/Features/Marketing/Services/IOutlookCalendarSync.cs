using Anela.Heblo.Domain.Features.Marketing;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public interface IOutlookCalendarSync
    {
        Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct);
        Task UpdateEventAsync(MarketingAction action, CancellationToken ct);
        Task DeleteEventAsync(string outlookEventId, CancellationToken ct);
        Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct);
    }
}
