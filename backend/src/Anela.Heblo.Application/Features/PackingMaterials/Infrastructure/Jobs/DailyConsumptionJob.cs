using Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.Infrastructure.Jobs;

public class DailyConsumptionJob
{
    private readonly IMediator _mediator;
    private readonly ILogger<DailyConsumptionJob> _logger;

    public DailyConsumptionJob(
        IMediator mediator,
        ILogger<DailyConsumptionJob> logger)
    {
        _mediator = mediator;
        _logger = logger;
    }

    public async Task ProcessDailyConsumption()
    {
        var processingDate = DateOnly.FromDateTime(DateTime.Today.AddDays(-1)); // Process previous day

        _logger.LogInformation("Starting daily consumption job for {Date}", processingDate);

        try
        {
            var request = new ProcessDailyConsumptionRequest
            {
                ProcessingDate = processingDate
            };

            var result = await _mediator.Send(request);

            if (result.Success)
            {
                _logger.LogInformation("Daily consumption job completed successfully for {Date}: {Message}",
                    processingDate, result.Message);
            }
            else
            {
                _logger.LogWarning("Daily consumption job completed with warnings for {Date}: {Message}",
                    processingDate, result.Message);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Daily consumption job failed for {Date}", processingDate);
            throw; // Re-throw to let Hangfire handle retry logic
        }
    }

}