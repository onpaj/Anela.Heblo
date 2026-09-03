using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    /// <summary>
    /// No-op implementation of IOutlookCalendarSync used when mock authentication is active
    /// or BypassJwtValidation is set. Logs a warning and returns empty/default results so
    /// the application starts cleanly without Azure AD token acquisition.
    /// </summary>
    public sealed class NoOpOutlookCalendarSync : IOutlookCalendarSync
    {
        private readonly ILogger<NoOpOutlookCalendarSync> _logger;

        public NoOpOutlookCalendarSync(ILogger<NoOpOutlookCalendarSync> logger)
        {
            _logger = logger;
        }

        public Task<string> CreateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — skipping CreateEvent for action {ActionId}", action.Id);
            return Task.FromResult(string.Empty);
        }

        public Task UpdateEventAsync(MarketingAction action, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — skipping UpdateEvent for action {ActionId}", action.Id);
            return Task.CompletedTask;
        }

        public Task DeleteEventAsync(string outlookEventId, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — skipping DeleteEvent for outlookEventId {OutlookEventId}", outlookEventId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutlookEventDto>> ListEventsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken ct)
        {
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — returning empty list for ListEvents");
            return Task.FromResult<IReadOnlyList<OutlookEventDto>>(Array.Empty<OutlookEventDto>());
        }

        public Task<OutlookEventDto?> GetEventAsync(string outlookEventId, CancellationToken ct)
        {
            // Unlike ListEventsAsync's empty-list no-op, a null here would be read by
            // MarketingCalendarSyncService.ReconcileOrphanAsync as positive confirmation
            // that the Outlook event was deleted, causing it to soft-delete the
            // corresponding marketing action. Throw instead so the caller treats this
            // as a failed confirmation rather than a deletion.
            _logger.LogWarning("Outlook sync disabled (mock auth active (UseMockAuth or BypassJwtValidation)) — refusing to confirm GetEvent {OutlookEventId}", outlookEventId);
            throw new InvalidOperationException(
                "Outlook sync is disabled (mock auth active — UseMockAuth or BypassJwtValidation). " +
                $"Cannot confirm the state of Outlook event {outlookEventId}; refusing to let the marketing " +
                "calendar sync interpret this as \"deleted in Outlook\" and soft-delete the corresponding action.");
        }
    }
}
