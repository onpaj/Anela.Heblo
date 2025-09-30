using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Model.IssuedOrders;

namespace Anela.Heblo.Adapters.Flexi.IssuedOrders;

public class FlexiManufactureClient : IManufactureClient
{
    private const string DocumentTypeSemiProduct = "VYR-POLOTOVAR";
    private const string DocumentTypeProduct = "VYR-PRODUKT";
    private const string WarehouseDocumentTypeSemiProduct = "VYROBA-POLOTOVAR";
    private const string WarehouseDocumentTypeProduct = "VYROBA-PRODUKT";
    private const string WarehouseCodeSemiProduct = "POLOTOVARY";
    private const string WarehouseCodeProduct = "ZBOZI";
    private readonly IIssuedOrdersClient _ordersClient;
    private readonly ILogger<FlexiManufactureClient> _logger;

    public FlexiManufactureClient(
        IIssuedOrdersClient ordersClient,
        ILogger<FlexiManufactureClient> logger)
    {
        _ordersClient = ordersClient ?? throw new ArgumentNullException(nameof(ordersClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {

        _logger.LogDebug("Starting manufacture order submission. ManufactureOrderId: {ManufactureOrderId}, Type: {ManufactureType}, ItemsCount: {ItemsCount}",
            request.ManufactureOrderCode, request.ManufactureType, request.Items.Count);

        try
        {
            // Map request to FlexiBee DTO
            var createOrder = new CreateIssuedOrderFlexiDto()
            {
                DepartmentCode = "C",
                OrderInternalNumber = request.ManufactureInternalNumber,
                DocumentType = request.ManufactureType == ManufactureType.SemiProduct ? DocumentTypeSemiProduct : DocumentTypeProduct,
                WarehouseDocumentType = request.ManufactureType == ManufactureType.SemiProduct ? WarehouseDocumentTypeSemiProduct : WarehouseDocumentTypeProduct,
                DateCreated = request.Date,
                DateVat = request.Date,
                CreatedBy = request.CreatedBy,
                User = request.CreatedBy,
                Note = request.ManufactureOrderCode,
                Description = request.ManufactureOrderCode,
                Items = request.Items.Where(w => w.Amount > 0)
                    .Select(s => MapToFlexiItem(s, request)).ToList()
            };

            _logger.LogDebug("Mapped request to CreateIssuedOrderFlexiDto. DocumentType: {DocumentType}, ItemsCount: {ItemsCount}",
                createOrder.DocumentType, createOrder.Items?.Count ?? 0);

            // Create the issued order
            var result = await _ordersClient.SaveAsync(createOrder, cancellationToken);
            if (!result.IsSuccess)
            {
                _logger.LogError("Failed to create manufacture order {ManufactureOrderId}: {Error}", request.ManufactureOrderCode, result.GetErrorMessage());
                throw new InvalidOperationException($"Failed to create issued order: {result.GetErrorMessage()}");
            }

            var firstResult = result.Result.Results.First();
            if (firstResult?.Id == null)
            {
                _logger.LogError("SaveAsync returned result with null Id for manufacture order {ManufactureOrderId}", request.ManufactureOrderCode);
                throw new InvalidOperationException("Failed to create issued order - no ID returned");
            }

            if (!int.TryParse(firstResult.Id.ToString(), out var orderId))
            {
                _logger.LogError("Failed to parse order ID '{OrderId}' for manufacture order {ManufactureOrderId}",
                    firstResult.Id, request.ManufactureOrderCode);
                throw new InvalidOperationException($"Failed to parse order ID: {firstResult.Id}");
            }

            _logger.LogDebug("Successfully created issued order with ID {OrderId} for manufacture order {ManufactureOrderId}",
                orderId, request.ManufactureOrderCode);

            var savedOrder = await _ordersClient.GetAsync(orderId, cancellationToken);

            // Finalize the order
            var finalizeOrder = new FinalizeIssuedOrderFlexiDto(orderId)
            {
                FinalizeStockMovement = new IssuedOrderStockMovementFlexiDto()
                {
                    Items = savedOrder.Items.Select(s => new FinalizeIssuedOrderItemFlexiDto()
                    {
                        Id = s.Id,
                        Amount = s.Amount,
                        LotNumber = request.LotNumber,
                        ExpirationDate = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    }).ToList(),
                    WarehouseDocumentType = createOrder.WarehouseDocumentType,
                    WarehouseCode = request.ManufactureType == ManufactureType.SemiProduct ? WarehouseCodeSemiProduct : WarehouseCodeProduct,
                }
            };

            _logger.LogDebug("Finalizing issued order {OrderId}", orderId);


            var finalizeResult = await _ordersClient.FinalizeAsync(finalizeOrder, cancellationToken);

            if (!finalizeResult.IsSuccess)
            {
                var shortenedMessage = finalizeResult.GetErrorMessage()?.Split("Could not execute JDBC batch update")
                    .First();
                _logger.LogError("FinalizeAsync failed for order {OrderId}: {ErrorMessage}", orderId, shortenedMessage);
                throw new InvalidOperationException($"Failed to finalize issued order {orderId}: {shortenedMessage}");
            }

            _logger.LogDebug("Successfully finalized issued order {OrderId}", orderId);

            return savedOrder.Code;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit manufacture order {ManufactureOrderId}: {ErrorMessage}",
                request.ManufactureOrderCode, ex.Message);
            throw;
        }
    }

    private string FormatOrderName(SubmitManufactureClientRequest request)
    {
        if (request.ManufactureType == ManufactureType.SemiProduct)
        {
            return $"{request.Items.First().ProductCode} - {request.Items.First().ProductName.Split(" - ").First()}";
        }

        return $"{request.Items.First().ProductCode.Left(6)} - {request.Items.First().ProductName.Split(" - ").First()}";
    }

    private static IssuedOrderItemFlexiDto MapToFlexiItem(SubmitManufactureClientItem item,
        SubmitManufactureClientRequest request)
    {
        if (item.Amount <= 0)
        {
            throw new ArgumentException("Item quantity must be greater than zero", nameof(item));
        }

        return new IssuedOrderItemFlexiDto
        {
            ProductCode = item.ProductCode,
            Name = item.ProductName,
            Amount = (double)item.Amount,
            LotNumber = request.LotNumber,
            ExpirationDate = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            WarehouseCode = request.ManufactureType == ManufactureType.SemiProduct ? WarehouseCodeSemiProduct : WarehouseCodeProduct,
        };
    }
}
