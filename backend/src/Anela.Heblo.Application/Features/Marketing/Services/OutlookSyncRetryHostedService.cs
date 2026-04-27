using Anela.Heblo.Domain.Features.Marketing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Marketing.Services
{
    public class OutlookSyncRetryHostedService : BackgroundService
    {
        private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
        private const int BatchSize = 50;

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<OutlookSyncRetryHostedService> _logger;

        public OutlookSyncRetryHostedService(
            IServiceScopeFactory scopeFactory,
            ILogger<OutlookSyncRetryHostedService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(RetryInterval, stoppingToken);
                await ProcessFailedSyncsAsync(stoppingToken);
            }
        }

        internal async Task ProcessFailedSyncsAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IMarketingActionRepository>();
            var outlookSync = scope.ServiceProvider.GetRequiredService<IOutlookCalendarSync>();

            var failed = await repository.GetFailedOutlookSyncAsync(BatchSize, ct);
            if (failed.Count == 0)
            {
                return;
            }

            _logger.LogInformation("Retrying Outlook sync for {Count} failed actions", failed.Count);

            foreach (var action in failed)
            {
                await RetryActionAsync(action, repository, outlookSync, ct);
            }
        }

        private async Task RetryActionAsync(
            MarketingAction action,
            IMarketingActionRepository repository,
            IOutlookCalendarSync outlookSync,
            CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            try
            {
                if (action.IsDeleted)
                {
                    // Retry a failed Outlook event delete
                    if (!string.IsNullOrEmpty(action.OutlookEventId))
                    {
                        await outlookSync.DeleteEventAsync(action.OutlookEventId, ct);
                    }

                    action.ClearOutlookLink();
                }
                else if (string.IsNullOrEmpty(action.OutlookEventId))
                {
                    // Retry a failed create
                    var eventId = await outlookSync.CreateEventAsync(action, ct);
                    action.MarkOutlookSynced(eventId, now);
                }
                else
                {
                    // Retry a failed update
                    await outlookSync.UpdateEventAsync(action, ct);
                    action.MarkOutlookSynced(action.OutlookEventId, now);
                }

                await repository.UpdateAsync(action, ct);
                await repository.SaveChangesAsync(ct);
                _logger.LogInformation("Successfully retried Outlook sync for MarketingAction {ActionId}", action.Id);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Retry of Outlook sync for MarketingAction {ActionId} failed again; will retry in next cycle",
                    action.Id);
                // Leave status as Failed — next cycle will pick it up again
            }
        }
    }
}
