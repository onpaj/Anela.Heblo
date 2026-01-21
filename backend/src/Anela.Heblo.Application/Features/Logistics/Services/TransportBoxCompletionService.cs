using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.Services;

public class TransportBoxCompletionService : ITransportBoxCompletionService
{
    private readonly ILogger<TransportBoxCompletionService> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOperationRepository _stockUpOperationRepository;

    public TransportBoxCompletionService(
        ILogger<TransportBoxCompletionService> logger,
        ITransportBoxRepository transportBoxRepository,
        IStockUpOperationRepository stockUpOperationRepository)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockUpOperationRepository = stockUpOperationRepository;
    }

    public async Task CompleteReceivedBoxesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting CompleteReceivedBoxes background task");

        var receivedBoxes = await _transportBoxRepository.GetReceivedBoxesAsync(cancellationToken);

        _logger.LogInformation("Found {Count} transport boxes in Received state", receivedBoxes.Count);

        if (receivedBoxes.Count == 0)
        {
            _logger.LogDebug("No boxes to process");
            return;
        }

        int completedCount = 0;
        int errorCount = 0;
        int skippedCount = 0;

        foreach (var box in receivedBoxes)
        {
            try
            {
                var result = await ProcessBoxAsync(box, cancellationToken);

                switch (result)
                {
                    case BoxProcessingResult.Completed:
                        completedCount++;
                        break;
                    case BoxProcessingResult.Failed:
                        errorCount++;
                        break;
                    case BoxProcessingResult.Skipped:
                        skippedCount++;
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error processing box {BoxId} ({BoxCode})",
                    box.Id, box.Code);
                errorCount++;
            }
        }

        _logger.LogInformation(
            "CompleteReceivedBoxes finished. Completed: {Completed}, Failed: {Failed}, Skipped: {Skipped}",
            completedCount, errorCount, skippedCount);
    }

    private async Task<BoxProcessingResult> ProcessBoxAsync(
        TransportBox box,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Processing box {BoxId} ({BoxCode})", box.Id, box.Code);

        // Get all stock-up operations for this box
        var operations = await _stockUpOperationRepository.GetBySourceAsync(
            StockUpSourceType.TransportBox,
            box.Id,
            cancellationToken);

        if (operations.Count == 0)
        {
            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has no StockUpOperations, marking as Error",
                box.Id, box.Code);

            box.Error(DateTime.UtcNow, "System",
                "No stock-up operations found for this box");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        // Check operation states
        var allCompleted = operations.All(op => op.State == StockUpOperationState.Completed);
        var anyFailed = operations.Any(op => op.State == StockUpOperationState.Failed);
        var pendingOrSubmitted = operations.Any(op =>
            op.State == StockUpOperationState.Pending ||
            op.State == StockUpOperationState.Submitted);

        if (allCompleted)
        {
            _logger.LogInformation(
                "All {Count} stock-up operations for box {BoxId} ({BoxCode}) completed, marking as Stocked",
                operations.Count, box.Id, box.Code);

            box.ToPick(DateTime.UtcNow, "System");
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Completed;
        }

        if (anyFailed)
        {
            var failedOps = operations
                .Where(op => op.State == StockUpOperationState.Failed)
                .ToList();

            var errorMessage = $"{failedOps.Count} stock-up operation(s) failed. " +
                             $"Document numbers: {string.Join(", ", failedOps.Select(op => op.DocumentNumber))}";

            _logger.LogWarning(
                "Box {BoxId} ({BoxCode}) has {FailedCount} failed stock-up operations, marking as Error",
                box.Id, box.Code, failedOps.Count);

            box.Error(DateTime.UtcNow, "System", errorMessage);
            await _transportBoxRepository.UpdateAsync(box, cancellationToken);
            await _transportBoxRepository.SaveChangesAsync(cancellationToken);

            return BoxProcessingResult.Failed;
        }

        if (pendingOrSubmitted)
        {
            _logger.LogDebug(
                "Box {BoxId} ({BoxCode}) still has {Count} operations in progress, skipping",
                box.Id, box.Code,
                operations.Count(op =>
                    op.State == StockUpOperationState.Pending ||
                    op.State == StockUpOperationState.Submitted));

            return BoxProcessingResult.Skipped;
        }

        // Should not reach here
        _logger.LogWarning("Box {BoxId} ({BoxCode}) in unexpected state, skipping",
            box.Id, box.Code);
        return BoxProcessingResult.Skipped;
    }

    private enum BoxProcessingResult
    {
        Completed,
        Failed,
        Skipped
    }
}
