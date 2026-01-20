using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.RetryStockUpOperation;

public class RetryStockUpOperationHandler : IRequestHandler<RetryStockUpOperationRequest, RetryStockUpOperationResponse>
{
    private readonly IStockUpOperationRepository _repository;
    private readonly IStockUpOrchestrationService _orchestrationService;
    private readonly ILogger<RetryStockUpOperationHandler> _logger;

    public RetryStockUpOperationHandler(
        IStockUpOperationRepository repository,
        IStockUpOrchestrationService orchestrationService,
        ILogger<RetryStockUpOperationHandler> logger)
    {
        _repository = repository;
        _orchestrationService = orchestrationService;
        _logger = logger;
    }

    public async Task<RetryStockUpOperationResponse> Handle(RetryStockUpOperationRequest request, CancellationToken cancellationToken)
    {
        var operation = await _repository.GetByIdAsync(request.OperationId, cancellationToken);

        if (operation == null)
        {
            return new RetryStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = $"Operation with ID {request.OperationId} not found"
            };
        }

        // Check if operation can be retried
        if (operation.State == StockUpOperationState.Completed)
        {
            return new RetryStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.AlreadyCompleted,
                ErrorMessage = $"Operation {operation.DocumentNumber} is already completed and cannot be retried"
            };
        }

        _logger.LogInformation("Retrying operation {OperationId} - {DocumentNumber}, current state: {State}",
            operation.Id, operation.DocumentNumber, operation.State);

        // Reset the operation to Pending state for retry
        // Use ForceReset for stuck operations (Submitted/Verified), normal Reset for Failed
        if (operation.State == StockUpOperationState.Failed)
        {
            operation.Reset();
        }
        else
        {
            _logger.LogWarning("Force resetting stuck operation {OperationId} from {State} state",
                operation.Id, operation.State);
            operation.ForceReset();
        }

        await _repository.SaveChangesAsync(cancellationToken);

        // Retry the operation using orchestration service
        var result = await _orchestrationService.ExecuteAsync(
            operation.DocumentNumber,
            operation.ProductCode,
            operation.Amount,
            operation.SourceType,
            operation.SourceId,
            cancellationToken);

        return new RetryStockUpOperationResponse
        {
            Success = result.IsSuccess,
            Status = result.Status,
            ErrorMessage = result.IsSuccess ? null : result.Message
        };
    }
}
