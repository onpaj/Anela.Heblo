using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Xcc.Services;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ProcessReceivedBoxes;

public class ProcessReceivedBoxesHandler : IRequestHandler<ProcessReceivedBoxesRequest, ProcessReceivedBoxesResponse>
{
    private readonly ILogger<ProcessReceivedBoxesHandler> _logger;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockUpOrchestrationService _stockUpOrchestrationService;
    private readonly IBackgroundRefreshTaskRegistry _backgroundRefreshTaskRegistry;
    private readonly ICurrentUserService _currentUserService;

    public ProcessReceivedBoxesHandler(
        ILogger<ProcessReceivedBoxesHandler> logger,
        ITransportBoxRepository transportBoxRepository,
        IStockUpOrchestrationService stockUpOrchestrationService,
        IBackgroundRefreshTaskRegistry backgroundRefreshTaskRegistry,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _stockUpOrchestrationService = stockUpOrchestrationService;
        _backgroundRefreshTaskRegistry = backgroundRefreshTaskRegistry;
        _currentUserService = currentUserService;
    }

    public async Task<ProcessReceivedBoxesResponse> Handle(ProcessReceivedBoxesRequest request, CancellationToken cancellationToken)
    {
        var batchId = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        var userName = _currentUserService.GetCurrentUser()?.Name ?? "System";

        _logger.LogInformation("Starting automatic processing of received transport boxes - Batch ID: {BatchId}", batchId);

        // Get all boxes in Received state
        var receivedBoxes = await _transportBoxRepository.GetReceivedBoxesAsync(cancellationToken);

        _logger.LogInformation("Found {Count} transport boxes in Received state to process", receivedBoxes.Count);

        var response = new ProcessReceivedBoxesResponse
        {
            ProcessedBoxesCount = receivedBoxes.Count,
            BatchId = batchId
        };

        if (receivedBoxes.Count == 0)
        {
            _logger.LogInformation("No transport boxes in Received state found - processing completed");
            return response;
        }

        // Process each box individually
        foreach (var box in receivedBoxes)
        {
            try
            {
                _logger.LogDebug("Processing transport box {BoxId} ({BoxCode}) with {ItemCount} items",
                    box.Id, box.Code, box.Items.Count);

                // Stock up all items from the box
                // This CREATES StockUpOperation entities in Pending state
                // Box state will be changed by CompleteReceivedBoxesJob after all operations complete
                await StockUpBoxItemsAsync(box, cancellationToken);

                // ⚠️ DO NOT change box state here!
                // Box stays in "Received" until CompleteReceivedBoxesJob verifies all operations completed

                // Save changes (box remains in Received state)
                await _transportBoxRepository.UpdateAsync(box, cancellationToken);
                await _transportBoxRepository.SaveChangesAsync(cancellationToken);

                response.OperationsCreatedCount++;

                _logger.LogInformation("Successfully created stock-up operations for transport box {BoxId} ({BoxCode}) with {ItemCount} items",
                    box.Id, box.Code, box.Items.Count);
            }
            catch (Exception ex)
            {
                // Set box to error state and continue with next box
                try
                {
                    box.Error(DateTime.UtcNow, userName, ex.Message);
                    await _transportBoxRepository.UpdateAsync(box, cancellationToken);
                    await _transportBoxRepository.SaveChangesAsync(cancellationToken);

                    response.FailedBoxesCount++;
                    response.FailedBoxCodes.Add(box.Code ?? box.Id.ToString());

                    _logger.LogError(ex, "Failed to process transport box {BoxId} ({BoxCode}). Box set to Error state",
                        box.Id, box.Code);
                }
                catch (Exception saveEx)
                {
                    _logger.LogError(saveEx, "Failed to save transport box {BoxId} ({BoxCode}) to Error state after processing failure",
                        box.Id, box.Code);

                    response.FailedBoxesCount++;
                    response.FailedBoxCodes.Add(box.Code ?? box.Id.ToString());
                }
            }
        }

        _logger.LogInformation("Completed processing of received transport boxes - Batch ID: {BatchId}. " +
                             "Processed: {ProcessedCount}, Operations Created: {OperationsCreated}, Failed: {FailedCount}",
            batchId, response.ProcessedBoxesCount, response.OperationsCreatedCount, response.FailedBoxesCount);

        try
        {
            await _backgroundRefreshTaskRegistry.ForceRefreshAsync(nameof(ICatalogRepository.RefreshEshopStockData), CancellationToken.None);
            await _backgroundRefreshTaskRegistry.ForceRefreshAsync(nameof(ICatalogRepository.RefreshTransportData), CancellationToken.None);

            _logger.LogDebug("Successfully invalidated catalog data after transport box processing - Batch ID: {BatchId}", batchId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate catalog data after transport box processing - Batch ID: {BatchId}", batchId);
        }

        return response;
    }

    private async Task StockUpBoxItemsAsync(TransportBox box, CancellationToken cancellationToken)
    {
        foreach (var item in box.Items)
        {
            // New DocumentNumber format: BOX-{boxId:000000}-{productCode}
            var documentNumber = $"BOX-{box.Id:000000}-{item.ProductCode}";

            _logger.LogDebug("Stocking up item: {DocumentNumber} - {ProductCode}, Amount: {Amount}",
                documentNumber, item.ProductCode, item.Amount);

            var result = await _stockUpOrchestrationService.ExecuteAsync(
                documentNumber,
                item.ProductCode,
                (int)item.Amount,
                StockUpSourceType.TransportBox,
                box.Id,
                cancellationToken);

            if (!result.IsSuccess)
            {
                _logger.LogWarning("Stock up operation {DocumentNumber} result: {Status} - {Message}",
                    documentNumber, result.Status, result.Message);

                // If failed and not "already completed", throw exception
                if (result.Status == StockUpResultStatus.Failed)
                    throw new InvalidOperationException(result.Message);
            }

            _logger.LogDebug("Successfully processed stock up: {DocumentNumber}, Status: {Status}",
                documentNumber, result.Status);
        }
    }
}