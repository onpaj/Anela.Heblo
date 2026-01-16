using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Services;

public class StockUpOrchestrationService : IStockUpOrchestrationService
{
    private readonly IStockUpOperationRepository _repository;
    private readonly IEshopStockDomainService _eshopService;
    private readonly ILogger<StockUpOrchestrationService> _logger;

    public StockUpOrchestrationService(
        IStockUpOperationRepository repository,
        IEshopStockDomainService eshopService,
        ILogger<StockUpOrchestrationService> logger)
    {
        _repository = repository;
        _eshopService = eshopService;
        _logger = logger;
    }

    public async Task<StockUpOperationResult> ExecuteAsync(
        string documentNumber,
        string productCode,
        int amount,
        StockUpSourceType sourceType,
        int sourceId,
        CancellationToken ct = default)
    {
        _logger.LogInformation("Starting StockUp orchestration for document {DocumentNumber}, product {ProductCode}, amount {Amount}",
            documentNumber, productCode, amount);

        // === LAYER 1: DB UNIQUE constraint protection ===
        // Create StockUpOperation - UNIQUE constraint prevents duplicates
        var operation = new StockUpOperation(documentNumber, productCode, amount, sourceType, sourceId);

        try
        {
            await _repository.AddAsync(operation, ct);
            await _repository.SaveChangesAsync(ct);
            _logger.LogDebug("StockUpOperation created with ID {OperationId}, state: {State}",
                operation.Id, operation.State);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Operation already exists - check its state
            _logger.LogInformation("DocumentNumber {DocumentNumber} already exists, checking existing operation state",
                documentNumber);

            var existing = await _repository.GetByDocumentNumberAsync(documentNumber, ct);
            if (existing == null)
            {
                _logger.LogError("Unique constraint violation but existing operation not found for {DocumentNumber}",
                    documentNumber);
                throw;
            }

            if (existing.State == StockUpOperationState.Completed)
            {
                _logger.LogInformation("Operation {DocumentNumber} already completed at {CompletedAt}",
                    documentNumber, existing.CompletedAt);
                return StockUpOperationResult.AlreadyCompleted(existing);
            }

            if (existing.State == StockUpOperationState.Failed)
            {
                _logger.LogWarning("Operation {DocumentNumber} previously failed: {ErrorMessage}",
                    documentNumber, existing.ErrorMessage);
                return StockUpOperationResult.PreviouslyFailed(existing);
            }

            // In Pending, Submitted, or Verified state - another process is handling it
            _logger.LogWarning("Operation {DocumentNumber} already in progress, state: {State}",
                documentNumber, existing.State);
            return StockUpOperationResult.InProgress(existing);
        }

        // === LAYER 3: Pre-submit check in Shoptet history ===
        _logger.LogDebug("Checking if document {DocumentNumber} already exists in Shoptet history",
            documentNumber);

        try
        {
            var existsInShoptet = await _eshopService.VerifyStockUpExistsAsync(documentNumber);
            if (existsInShoptet)
            {
                _logger.LogWarning("Document {DocumentNumber} already exists in Shoptet, marking as completed",
                    documentNumber);
                operation.MarkAsCompleted(DateTime.UtcNow);
                await _repository.SaveChangesAsync(ct);
                return StockUpOperationResult.AlreadyInShoptet(operation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Pre-check verification failed for {DocumentNumber}, continuing with submit",
                documentNumber);
            // Continue with submit - verification errors shouldn't block the operation
        }

        // === Submit to Shoptet ===
        try
        {
            operation.MarkAsSubmitted(DateTime.UtcNow);
            await _repository.SaveChangesAsync(ct);
            _logger.LogDebug("Operation {DocumentNumber} marked as Submitted", documentNumber);

            var request = new StockUpRequest(productCode, amount, documentNumber);
            await _eshopService.StockUpAsync(request);
            _logger.LogInformation("Successfully submitted {DocumentNumber} to Shoptet", documentNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Submit failed for {DocumentNumber}", documentNumber);
            operation.MarkAsFailed(DateTime.UtcNow, $"Submit failed: {ex.Message}");
            await _repository.SaveChangesAsync(ct);
            return StockUpOperationResult.SubmitFailed(operation, ex);
        }

        // === LAYER 4: Post-verify in Shoptet history ===
        _logger.LogDebug("Verifying {DocumentNumber} in Shoptet history after submission", documentNumber);

        try
        {
            var verified = await _eshopService.VerifyStockUpExistsAsync(documentNumber);
            if (verified)
            {
                operation.MarkAsVerified(DateTime.UtcNow);
                operation.MarkAsCompleted(DateTime.UtcNow);
                await _repository.SaveChangesAsync(ct);
                _logger.LogInformation("Operation {DocumentNumber} verified and completed successfully", documentNumber);
                return StockUpOperationResult.Success(operation);
            }
            else
            {
                _logger.LogError("Verification failed: {DocumentNumber} not found in Shoptet history after submission",
                    documentNumber);
                operation.MarkAsFailed(DateTime.UtcNow,
                    "Verification failed: Record not found in Shoptet history");
                await _repository.SaveChangesAsync(ct);
                return StockUpOperationResult.VerificationFailed(operation);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Verification error for {DocumentNumber}", documentNumber);
            operation.MarkAsFailed(DateTime.UtcNow, $"Verification error: {ex.Message}");
            await _repository.SaveChangesAsync(ct);
            return StockUpOperationResult.VerificationError(operation, ex);
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        // PostgreSQL unique constraint violation error code: 23505
        return ex.InnerException?.Message?.Contains("23505") == true
               || ex.InnerException?.Message?.Contains("duplicate key") == true
               || ex.InnerException?.Message?.Contains("IX_StockUpOperations_DocumentNumber_Unique") == true;
    }
}
