using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public class MarketingInvoiceImportService
{
    private readonly IMarketingTransactionSource _source;
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _logger;

    public MarketingInvoiceImportService(
        IMarketingTransactionSource source,
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> logger)
    {
        _source = source;
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketingImportResult> ImportAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        if (from > to)
        {
            throw new ArgumentException($"'from' ({from:yyyy-MM-dd}) must be before or equal to 'to' ({to:yyyy-MM-dd}).", nameof(from));
        }

        _logger.LogInformation(
            "Starting marketing invoice import for platform {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            _source.Platform, from, to);

        var transactions = await _source.GetTransactionsAsync(from, to, ct);

        var result = new MarketingImportResult();
        var toImport = new List<ImportedMarketingTransaction>();

        foreach (var transaction in transactions)
        {
            try
            {
                var exists = await _repository.ExistsAsync(_source.Platform, transaction.TransactionId, ct);
                if (exists)
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already imported — skipping",
                        transaction.TransactionId, _source.Platform);
                    result.Skipped++;
                    continue;
                }

                toImport.Add(new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = _source.Platform,
                    Amount = transaction.Amount,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    IsSynced = false,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to check transaction {TransactionId} for {Platform}",
                    transaction.TransactionId, _source.Platform);
                result.Failed++;
            }
        }

        if (toImport.Count > 0)
        {
            try
            {
                await _repository.AddRangeAsync(toImport, ct);
                await _repository.SaveChangesAsync(ct);
                result.Imported = toImport.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to batch import {Count} transactions for {Platform}",
                    toImport.Count, _source.Platform);
                result.Failed += toImport.Count;
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            _source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
