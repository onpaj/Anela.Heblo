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
    private readonly IEshopStockDomainService _eshopStockDomainService;
    private readonly IBackgroundRefreshTaskRegistry _backgroundRefreshTaskRegistry;
    private readonly ICurrentUserService _currentUserService;

    public ProcessReceivedBoxesHandler(
        ILogger<ProcessReceivedBoxesHandler> logger,
        ITransportBoxRepository transportBoxRepository,
        IEshopStockDomainService eshopStockDomainService,
        IBackgroundRefreshTaskRegistry backgroundRefreshTaskRegistry,
        ICurrentUserService currentUserService)
    {
        _logger = logger;
        _transportBoxRepository = transportBoxRepository;
        _eshopStockDomainService = eshopStockDomainService;
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
                await StockUpBoxItemsAsync(box, batchId, cancellationToken);

                // Change box state to Stocked (as we're not implementing InSwap)
                box.ToPick(DateTime.UtcNow, userName);

                // Save changes
                await _transportBoxRepository.UpdateAsync(box);
                await _transportBoxRepository.SaveChangesAsync();

                response.SuccessfulBoxesCount++;

                _logger.LogInformation("Successfully processed transport box {BoxId} ({BoxCode}) with {ItemCount} items", 
                    box.Id, box.Code, box.Items.Count);
            }
            catch (Exception ex)
            {
                // Set box to error state and continue with next box
                try
                {
                    box.Error(DateTime.UtcNow, userName, ex.Message);
                    await _transportBoxRepository.UpdateAsync(box);
                    await _transportBoxRepository.SaveChangesAsync();
                    
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
                             "Processed: {ProcessedCount}, Successful: {SuccessfulCount}, Failed: {FailedCount}", 
            batchId, response.ProcessedBoxesCount, response.SuccessfulBoxesCount, response.FailedBoxesCount);

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

    private async Task StockUpBoxItemsAsync(TransportBox box, string batchId, CancellationToken cancellationToken)
    {
        foreach (var item in box.Items)
        {
            _logger.LogDebug("Stocking up item: {ProductCode} - {ProductName}, Amount: {Amount}", 
                item.ProductCode, item.ProductName, item.Amount);

            var stockUpRequest = new StockUpRequest(item.ProductCode, item.Amount, batchId);
            await _eshopStockDomainService.StockUpAsync(stockUpRequest);

            _logger.LogDebug("Successfully stocked up item: {ProductCode}, Amount: {Amount}", 
                item.ProductCode, item.Amount);
        }
    }
}