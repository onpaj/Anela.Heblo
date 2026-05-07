using Anela.Heblo.Application.Features.PackingMaterials.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;

public class ProcessDailyConsumptionHandler : IRequestHandler<ProcessDailyConsumptionRequest, ProcessDailyConsumptionResponse>
{
    private readonly IConsumptionCalculationService _consumptionService;
    private readonly ILogger<ProcessDailyConsumptionHandler> _logger;

    public ProcessDailyConsumptionHandler(
        IConsumptionCalculationService consumptionService,
        ILogger<ProcessDailyConsumptionHandler> logger)
    {
        _consumptionService = consumptionService;
        _logger = logger;
    }

    public async Task<ProcessDailyConsumptionResponse> Handle(
        ProcessDailyConsumptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing daily consumption for {Date}", request.ProcessingDate);

            var result = await _consumptionService.ProcessDailyConsumptionAsync(request.ProcessingDate, cancellationToken);

            if (!result.WasRun)
            {
                return new ProcessDailyConsumptionResponse
                {
                    Success = false,
                    ProcessedDate = request.ProcessingDate,
                    MaterialsProcessed = 0,
                    Message = $"Daily consumption for {request.ProcessingDate} was already processed"
                };
            }

            _logger.LogInformation("Successfully processed daily consumption for {Date}", request.ProcessingDate);

            var message = result.MaterialsProcessed > 0
                ? $"Daily consumption successfully processed for {request.ProcessingDate}. {result.MaterialsProcessed} materials updated."
                : $"No invoices found for {request.ProcessingDate} — no materials were updated.";

            return new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = result.MaterialsProcessed,
                Message = message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing daily consumption for {Date}", request.ProcessingDate);

            return new ProcessDailyConsumptionResponse
            {
                Success = false,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = 0,
                Message = "An unexpected error occurred while processing daily consumption."
            };
        }
    }
}
