using Hangfire;
using Anela.Heblo.Xcc.Telemetry;
using MediatR;
using Anela.Heblo.Application.Features.Purchase.Requests;

namespace Anela.Heblo.API.Services;

public class HangfireBackgroundJobService
{
    private readonly ILogger<HangfireBackgroundJobService> _logger;
    private readonly ITelemetryService _telemetryService;
    private readonly IMediator _mediator;

    public HangfireBackgroundJobService(
        ILogger<HangfireBackgroundJobService> logger,
        ITelemetryService telemetryService,
        IMediator mediator)
    {
        _logger = logger;
        _telemetryService = telemetryService;
        _mediator = mediator;
    }

    /// <summary>
    /// Daily purchase price recalculation job (runs at 2:00 AM UTC)
    /// </summary>
    [Queue("Heblo")]
    public async Task RecalculatePurchasePricesAsync()
    {
        try
        {
            _logger.LogInformation("Starting daily purchase price recalculation job at {Timestamp}", DateTime.UtcNow);

            var request = new RecalculatePurchasePriceRequest
            {
                RecalculateAll = true
            };

            var response = await _mediator.Send(request);

            _logger.LogInformation("Purchase price recalculation completed - Success: {SuccessCount}, Failed: {FailedCount}, Total: {TotalCount}",
                response.SuccessCount, response.FailedCount, response.TotalCount);

            _telemetryService.TrackBusinessEvent("PurchasePriceRecalculation", new Dictionary<string, string>
            {
                ["Status"] = "Success",
                ["SuccessCount"] = response.SuccessCount.ToString(),
                ["FailedCount"] = response.FailedCount.ToString(),
                ["TotalCount"] = response.TotalCount.ToString(),
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Purchase price recalculation failed at {Timestamp}", DateTime.UtcNow);
            _telemetryService.TrackException(ex, new Dictionary<string, string>
            {
                ["Job"] = "PurchasePriceRecalculation",
                ["Timestamp"] = DateTime.UtcNow.ToString("O")
            });
            throw;
        }
    }
}