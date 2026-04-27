using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// No-op implementation of IOutlookCalendarSync used when mock authentication is active
    /// or BypassJwtValidation is set. Logs a warning and returns empty/default results so
    /// the application starts cleanly without Azure AD token acquisition.
    /// </summary>
    public class NoOpOutlookCalendarSync : IOutlookCalendarSync
    {
        private readonly ILogger<NoOpOutlookCalendarSync> _logger;

        public NoOpOutlookCalendarSync(ILogger<NoOpOutlookCalendarSync> logger)
        {
            _logger = logger;
        }

        public Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth or PushEnabled=false) — skipping CreateEvent for action {ActionId}", action.Id);
            return Task.FromResult(string.Empty);
        }

        public Task UpdateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth or PushEnabled=false) — skipping UpdateEvent for action {ActionId}", action.Id);
            return Task.CompletedTask;
        }

        public Task DeleteEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth or PushEnabled=false) — skipping DeleteEvent for outlookEventId {OutlookEventId}", outlookEventId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth or PushEnabled=false) — returning empty list for ListEvents");
            return Task.FromResult<IReadOnlyList<OutlookEventDto>>(Array.Empty<OutlookEventDto>());
        }
    }
}
