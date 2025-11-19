using Anela.Heblo.Application.Features.Invoices.UseCases.GetIssuedInvoicesList;
using Anela.Heblo.Application.Features.PackingMaterials.Services;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.PackingMaterials.UseCases.ProcessDailyConsumption;

public class ProcessDailyConsumptionHandler : IRequestHandler<ProcessDailyConsumptionRequest, ProcessDailyConsumptionResponse>
{
    private readonly IConsumptionCalculationService _consumptionService;
    private readonly IMediator _mediator;
    private readonly ILogger<ProcessDailyConsumptionHandler> _logger;

    public ProcessDailyConsumptionHandler(
        IConsumptionCalculationService consumptionService,
        IMediator mediator,
        ILogger<ProcessDailyConsumptionHandler> logger)
    {
        _consumptionService = consumptionService;
        _mediator = mediator;
        _logger = logger;
    }

    public async Task<ProcessDailyConsumptionResponse> Handle(
        ProcessDailyConsumptionRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing daily consumption for {Date}", request.ProcessingDate);

            // Get invoices for the processing date via existing GetIssuedInvoicesListHandler
            var targetDate = request.ProcessingDate.ToDateTime(TimeOnly.MinValue);
            var invoicesRequest = new GetIssuedInvoicesListRequest
            {
                InvoiceDateFrom = targetDate,
                InvoiceDateTo = targetDate,
                PageNumber = 1,
                PageSize = 0 // 0 means return all invoices without pagination
            };

            var invoicesResult = await _mediator.Send(invoicesRequest, cancellationToken);

            if (!invoicesResult.Success)
            {
                _logger.LogError("Failed to get issued invoices for {Date}: {ErrorMessage}",
                    request.ProcessingDate, invoicesResult.Params?.GetValueOrDefault("ErrorMessage") ?? "Unknown error");
                
                return new ProcessDailyConsumptionResponse
                {
                    Success = false,
                    ProcessedDate = request.ProcessingDate,
                    MaterialsProcessed = 0,
                    Message = $"Failed to get issued invoices for {request.ProcessingDate}: {invoicesResult.Params?.GetValueOrDefault("ErrorMessage") ?? "Unknown error"}"
                };
            }

            // Calculate aggregated statistics from the invoices
            var orderCount = invoicesResult.Items.Count; // Each invoice represents one order
            var productCount = invoicesResult.Items.Sum(invoice => invoice.ItemsCount); // Sum of all products

            _logger.LogInformation("Retrieved {InvoiceCount} invoices with {OrderCount} orders and {ProductCount} products for {Date}",
                invoicesResult.Items.Count, orderCount, productCount, request.ProcessingDate);

            var processed = await _consumptionService.ProcessDailyConsumptionAsync(
                request.ProcessingDate,
                orderCount,
                productCount,
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