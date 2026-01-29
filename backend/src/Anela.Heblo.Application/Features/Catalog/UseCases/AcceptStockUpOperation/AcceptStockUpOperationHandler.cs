using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.AcceptStockUpOperation;

public class AcceptStockUpOperationHandler : IRequestHandler<AcceptStockUpOperationRequest, AcceptStockUpOperationResponse>
{
    private readonly IStockUpOperationRepository _repository;
    private readonly ILogger<AcceptStockUpOperationHandler> _logger;

    public AcceptStockUpOperationHandler(
        IStockUpOperationRepository repository,
        ILogger<AcceptStockUpOperationHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<AcceptStockUpOperationResponse> Handle(
        AcceptStockUpOperationRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing accept request for stock-up operation {OperationId}",
            request.OperationId);

        // Fetch the operation
        var operation = await _repository.GetByIdAsync(request.OperationId, cancellationToken);

        if (operation is null)
        {
            _logger.LogWarning(
                "Stock-up operation {OperationId} not found",
                request.OperationId);

            return new AcceptStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = $"Stock-up operation with ID {request.OperationId} not found"
            };
        }

        // Try to accept the failure
        try
        {
            operation.AcceptFailure(DateTime.UtcNow);

            await _repository.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Successfully accepted failed stock-up operation {OperationId} (Document: {DocumentNumber})",
                request.OperationId,
                operation.DocumentNumber);

            return new AcceptStockUpOperationResponse
            {
                Success = true,
                Status = StockUpResultStatus.Success
            };
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(
                ex,
                "Cannot accept stock-up operation {OperationId} - invalid state: {State}",
                request.OperationId,
                operation.State);

            return new AcceptStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unexpected error while accepting stock-up operation {OperationId}",
                request.OperationId);

            return new AcceptStockUpOperationResponse
            {
                Success = false,
                Status = StockUpResultStatus.Failed,
                ErrorMessage = $"An unexpected error occurred: {ex.Message}"
            };
        }
    }
}
