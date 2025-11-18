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
            _logger.LogInformation("Processing daily consumption for {Date} with {OrderCount} orders and {ProductCount} products",
                request.ProcessingDate, request.OrderCount, request.ProductCount);

            var processed = await _consumptionService.ProcessDailyConsumptionAsync(
                request.ProcessingDate,
                request.OrderCount,
                request.ProductCount,
                cancellationToken);

            if (!processed)
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

            return new ProcessDailyConsumptionResponse
            {
                Success = true,
                ProcessedDate = request.ProcessingDate,
                MaterialsProcessed = 0, // TODO: Return actual count if needed
                Message = $"Daily consumption successfully processed for {request.ProcessingDate}"
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
                Message = $"Error processing daily consumption: {ex.Message}"
            };
        }
    }
}