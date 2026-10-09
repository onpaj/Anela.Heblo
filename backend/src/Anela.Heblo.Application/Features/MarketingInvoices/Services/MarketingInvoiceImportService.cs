using Anela.Heblo.Application.Features.MarketingInvoices.Contracts;
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

        var allIds = transactions.Select(t => t.TransactionId).ToList();
        var alreadyImported = await _repository.GetExistingTransactionIdsAsync(source.Platform, allIds, ct);

        var result = new MarketingImportResult();
        var stagedCount = 0;

        // Within-run duplicate guard — kept unconditionally, do not remove. `alreadyImported`
        // is a snapshot fetched once, before this loop starts, so it cannot see a TransactionId
        // that appears twice within this same `transactions` batch (neither copy is in the
        // database yet). ImportedMarketingTransactionConfiguration declares a unique index on
        // (Platform, TransactionId), and the SaveChangesAsync call below rethrows on failure —
        // so without this guard, two in-batch duplicates would both pass the alreadyImported
        // check, both get AddAsync'd, and fail the entire run's save instead of cleanly
        // skipping one row.
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

                if (alreadyImported.Contains(transaction.TransactionId))
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
                throw;
            }
        }

        _logger.LogInformation(
            "Marketing invoice import complete for {Platform}: Imported={Imported}, Skipped={Skipped}, Failed={Failed}",
            source.Platform, result.Imported, result.Skipped, result.Failed);

        return result;
    }
}
