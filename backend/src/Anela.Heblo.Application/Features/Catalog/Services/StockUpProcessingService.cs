using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class StockUpProcessingService : IStockUpProcessingService
{
    private readonly IStockUpOperationRepository _repository;
    private readonly IEshopStockDomainService _eshopService;
    private readonly ILogger<StockUpProcessingService> _logger;

    public StockUpProcessingService(
        IStockUpOperationRepository repository,
        IEshopStockDomainService eshopService,
        ILogger<StockUpProcessingService> logger)
    {
        _repository = repository;
        _eshopService = eshopService;
        _logger = logger;
    }

    public async Task CreateOperationAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Creating StockUpOperation for document {DocumentNumber}, product {ProductCode}, amount {Amount}",
            documentNumber, productCode, amount);

        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        await _repository.AddAsync(operation, ct);
        await _repository.SaveChangesAsync(ct);

        _logger.LogDebug(
            "StockUpOperation created with ID {OperationId} in Pending state",
            operation.Id);
    }

    public async Task ProcessPendingOperationsAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting to process pending stock-up operations");

        var pendingOperations = await _repository.GetByStateAsync(StockUpOperationState.Pending, ct);

        if (!pendingOperations.Any())
        {
            _logger.LogDebug("No pending operations to process");
            return;
        }

        _logger.LogInformation("Found {Count} pending operations to process", pendingOperations.Count);

        foreach (var operation in pendingOperations)
        {
            await ProcessOperationAsync(operation, ct);
        }

        _logger.LogInformation("Finished processing pending stock-up operations");
    }

    private async Task ProcessOperationAsync(StockUpOperation operation, CancellationToken ct)
    {
        _logger.LogDebug(
            "Processing operation {OperationId} for document {DocumentNumber}",
            operation.Id, operation.DocumentNumber);

        try
        {
            // === LAYER 3: Pre-submit check in Shoptet history ===
            _logger.LogDebug(
                "Checking if document {DocumentNumber} already exists in Shoptet history",
                operation.DocumentNumber);

            try
            {
                var existsInShoptet = await _eshopService.VerifyStockUpExistsAsync(operation.DocumentNumber);
                if (existsInShoptet)
                {
                    _logger.LogWarning(
                        "Document {DocumentNumber} already exists in Shoptet, marking as completed",
                        operation.DocumentNumber);
                    operation.MarkAsCompleted(DateTime.UtcNow);
                    await _repository.SaveChangesAsync(ct);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Pre-check verification failed for {DocumentNumber}, continuing with submit",
                    operation.DocumentNumber);
                // Continue with submit - verification errors shouldn't block the operation
            }

            // === Submit to Shoptet ===
            operation.MarkAsSubmitted(DateTime.UtcNow);
            await _repository.SaveChangesAsync(ct);
            _logger.LogDebug("Operation {DocumentNumber} marked as Submitted", operation.DocumentNumber);

            var request = new StockUpRequest(operation.ProductCode, operation.Amount, operation.DocumentNumber);
            await _eshopService.StockUpAsync(request);
            _logger.LogInformation(
                "Successfully submitted {DocumentNumber} to Shoptet",
                operation.DocumentNumber);

            // === LAYER 4: Post-verify in Shoptet history ===
            _logger.LogDebug(
                "Verifying {DocumentNumber} in Shoptet history after submission",
                operation.DocumentNumber);

            var verified = await _eshopService.VerifyStockUpExistsAsync(operation.DocumentNumber);
            if (verified)
            {
                operation.MarkAsCompleted(DateTime.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogInformation(
                    "Operation {DocumentNumber} verified and completed successfully",
                    operation.DocumentNumber);
            }
            else
            {
                _logger.LogError(
                    "Verification failed: {DocumentNumber} not found in Shoptet history after submission",
                    operation.DocumentNumber);
                operation.MarkAsFailed(
                    DateTime.UtcNow,
                    "Verification failed: Record not found in Shoptet history");
                await _repository.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to process operation {OperationId} for document {DocumentNumber}",
                operation.Id, operation.DocumentNumber);

            operation.MarkAsFailed(DateTime.UtcNow, $"Processing failed: {ex.Message}");
            await _repository.SaveChangesAsync(ct);
        }
    }
}
