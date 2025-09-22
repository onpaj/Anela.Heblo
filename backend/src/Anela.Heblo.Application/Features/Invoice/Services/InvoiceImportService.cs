using Anela.Heblo.Application.Features.Invoice.UseCases.ImportInvoices;
using Anela.Heblo.Domain.Features.Invoice;
using Anela.Heblo.Domain.Features.Invoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Invoice.Services;

public class InvoiceImportService : IInvoiceImportService
{
    private readonly IIssuedInvoiceSource _source;
    private readonly IIssuedInvoiceClient _client;
    private readonly IIssuedInvoiceRepository _repository;
    private readonly ILogger<InvoiceImportService> _logger;

    public InvoiceImportService(
        IIssuedInvoiceSource source,
        IIssuedInvoiceClient client,
        IIssuedInvoiceRepository repository,
        ILogger<InvoiceImportService> logger)
    {
        _source = source;
        _client = client;
        _repository = repository;
        _logger = logger;
    }

    public async Task<BatchImportResult> ImportBatchAsync(BatchCriteria? criteria, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting batch import with criteria: {@Criteria}", criteria);
        
        // 1. Load batch from source based on criteria
        var query = CreateInvoiceRequest(criteria);
        var batches = await _source.GetAllAsync(query);
        
        var allAttempts = new List<ImportAttempt>();
        
        // 2. Process each batch
        foreach (var batch in batches)
        {
            _logger.LogInformation("Processing batch {BatchId} with {Count} invoices", batch.BatchId, batch.Invoices.Count);
            
            bool batchHasError = false;
            var batchAttempts = new List<ImportAttempt>();
            
            // 3. Process each invoice in the batch
            foreach (var invoiceDetail in batch.Invoices)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;
                    
                var attempt = await ImportSingleInvoiceAsync(invoiceDetail, cancellationToken);
                batchAttempts.Add(attempt);
                
                // Record every attempt (success or failure) to repository
                await _repository.RecordImportAttemptAsync(attempt);
                
                if (!attempt.IsSuccess)
                {
                    batchHasError = true;
                }
            }
            
            allAttempts.AddRange(batchAttempts);
            
            // 4. Commit or fail the batch based on results
            try
            {
                if (batchHasError)
                {
                    await _source.FailAsync(batch, "One or more invoices failed to import");
                    _logger.LogWarning("Batch {BatchId} marked as failed", batch.BatchId);
                }
                else
                {
                    await _source.CommitAsync(batch, "All invoices imported successfully");
                    _logger.LogInformation("Batch {BatchId} committed successfully", batch.BatchId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to commit/fail batch {BatchId}", batch.BatchId);
            }
        }
        
        var result = new BatchImportResult
        {
            ProcessedCount = allAttempts.Count,
            SuccessCount = allAttempts.Count(a => a.IsSuccess),
            FailedCount = allAttempts.Count(a => !a.IsSuccess),
            Attempts = allAttempts
        };
        
        _logger.LogInformation("Batch import completed. Processed: {Processed}, Success: {Success}, Failed: {Failed}", 
            result.ProcessedCount, result.SuccessCount, result.FailedCount);
        
        return result;
    }

    private async Task<ImportAttempt> ImportSingleInvoiceAsync(IssuedInvoiceDetail invoiceDetail, CancellationToken cancellationToken)
    {
        var attempt = ImportAttempt.Create(invoiceDetail.Id);
        attempt.InvoiceNumber = invoiceDetail.Code;
        attempt.Amount = invoiceDetail.Price?.WithVat;
        attempt.InvoiceDate = invoiceDetail.IssueDate;
        attempt.Currency = invoiceDetail.Price?.CurrencyCode;

        try
        {
            _logger.LogDebug("Processing invoice {InvoiceNumber} (ID: {InvoiceId})", invoiceDetail.Code, invoiceDetail.Id);
            
            // Check if already successfully imported (idempotency)
            if (await _repository.IsSuccessfullyImportedAsync(invoiceDetail.Id))
            {
                var history = await _repository.GetImportHistoryAsync(invoiceDetail.Id);
                var successfulImport = history.First(h => h.IsSuccess);
                
                attempt.MarkAsSuccessful(successfulImport.ImportId!);
                _logger.LogInformation("Invoice {InvoiceNumber} already imported with ID {ImportId}", 
                    invoiceDetail.Code, successfulImport.ImportId);
                return attempt;
            }

            // Convert to FlexiBee format (similar to existing logic)
            var flexiInvoice = MapToFlexiDto(invoiceDetail);
            
            // Import to client (idempotent operation)
            var importResult = await _client.SaveAsync(flexiInvoice, cancellationToken);
            
            if (importResult.IsSuccess)
            {
                attempt.MarkAsSuccessful(importResult.ImportId!);
                _logger.LogInformation("Successfully imported invoice {InvoiceNumber}: {Amount} {Currency}", 
                    invoiceDetail.Code, invoiceDetail.Price?.WithVat, invoiceDetail.Price?.CurrencyCode);
            }
            else
            {
                attempt.MarkAsFailed(importResult.ErrorMessage ?? "Import failed");
                _logger.LogWarning("Failed to import invoice {InvoiceNumber}: {Error}", 
                    invoiceDetail.Code, importResult.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            attempt.MarkAsFailed(ex.Message);
            _logger.LogError(ex, "Exception while importing invoice {InvoiceNumber}", invoiceDetail.Code);
        }

        return attempt;
    }

    private IssuedInvoiceRequest CreateInvoiceRequest(BatchCriteria? criteria)
    {
        var request = new IssuedInvoiceRequest();
        
        if (criteria?.FromDate.HasValue == true)
            request.DateFrom = criteria.FromDate.Value;
            
        if (criteria?.ToDate.HasValue == true)
            request.DateTo = criteria.ToDate.Value;
            
        if (criteria?.InvoiceNumbers?.Any() == true)
            request.InvoiceIds = criteria.InvoiceNumbers;
            
        if (!string.IsNullOrEmpty(criteria?.Currency))
            request.Currency = criteria.Currency;
            
        // Set reasonable defaults if no criteria provided
        if (!request.DateFrom.HasValue && !request.DateTo.HasValue && request.InvoiceIds?.Any() != true)
        {
            request.DateFrom = DateTime.Today.AddDays(-7); // Last 7 days
            request.DateTo = DateTime.Today;
        }
        
        return request;
    }

    private static object MapToFlexiDto(IssuedInvoiceDetail invoiceDetail)
    {
        // TODO: Implement mapping to FlexiBee DTO format
        // This should use the same mapping logic as in the original service
        // For now, return the original object - this needs to be implemented based on existing transformations
        return invoiceDetail;
    }
}