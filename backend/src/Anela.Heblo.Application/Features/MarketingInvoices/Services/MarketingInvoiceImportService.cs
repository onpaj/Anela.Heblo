using Anela.Heblo.Domain.Features.MarketingInvoices;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.MarketingInvoices.Services;

public class MarketingInvoiceImportService : IMarketingInvoiceImportService
{
    private readonly IImportedMarketingTransactionRepository _repository;
    private readonly ILogger<MarketingInvoiceImportService> _logger;

    public MarketingInvoiceImportService(
        IImportedMarketingTransactionRepository repository,
        ILogger<MarketingInvoiceImportService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<MarketingImportResult> ImportAsync(
        IMarketingTransactionSource source,
        DateTime from,
        DateTime to,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Starting marketing invoice import for platform {Platform} from {From:yyyy-MM-dd} to {To:yyyy-MM-dd}",
            source.Platform, from, to);

        var transactions = await source.GetTransactionsAsync(from, to, ct);

        var result = new MarketingImportResult();
        var stagedCount = 0;
        var stagedIds = new HashSet<string>();

        foreach (var transaction in transactions)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(transaction.Currency))
                {
                    _logger.LogWarning(
                        "Marketing transaction {TransactionId} for {Platform} has empty Currency — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Failed++;
                    continue;
                }

                if (stagedIds.Contains(transaction.TransactionId))
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already staged in this run — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Skipped++;
                    continue;
                }

                var exists = await _repository.ExistsAsync(source.Platform, transaction.TransactionId, ct);
                if (exists)
                {
                    _logger.LogDebug(
                        "Transaction {TransactionId} for {Platform} already imported — skipping",
                        transaction.TransactionId, source.Platform);
                    result.Skipped++;
                    continue;
                }

                var entity = new ImportedMarketingTransaction
                {
                    TransactionId = transaction.TransactionId,
                    Platform = source.Platform,
                    Amount = transaction.Amount,
                    Currency = transaction.Currency,
                    TransactionDate = transaction.TransactionDate,
                    ImportedAt = DateTime.UtcNow,
                    IsSynced = false,
                    Description = transaction.Description,
                    RawData = transaction.RawData,
                };

                await _repository.AddAsync(entity, ct);
                stagedIds.Add(transaction.TransactionId);
                stagedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to import transaction {TransactionId} for {Platform}",
                    transaction.TransactionId, source.Platform);
                result.Failed++;
            }
        }

        if (stagedCount > 0)
        {
            try
            {
                await _repository.SaveChangesAsync(ct);
                result.Imported = stagedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist {Count} marketing transactions for {Platform}",
                    stagedCount, source.Platform);
                result.Failed += stagedCount;
                // result.Imported intentionally stays 0 — nothing was committed.
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
