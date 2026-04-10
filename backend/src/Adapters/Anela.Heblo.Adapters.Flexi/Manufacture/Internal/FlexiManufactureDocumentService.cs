using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture.Internal;

internal sealed class FlexiManufactureDocumentService : IFlexiManufactureDocumentService
{
    private const string WarehouseDocumentType_OutboundMaterial = "V-VYDEJ-MATERIAL";
    private const string WarehouseDocumentType_InboundSemiProduct = "V-PRIJEM-POLOTOVAR";
    private const string WarehouseDocumentType_OutboundSemiProduct = "V-VYDEJ-POLOTOVAR";
    private const string WarehouseDocumentType_InboundProduct = "V-PRIJEM-VYROBEK";

    private readonly IErpStockClient _stockClient;
    private readonly IStockItemsMovementClient _stockMovementClient;

    public FlexiManufactureDocumentService(
        IErpStockClient stockClient,
        IStockItemsMovementClient stockMovementClient)
    {
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
    }

    public async Task SubmitConsolidatedConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken)
    {
        var consumptionByWarehouse = consumptionItems
            .GroupBy(item => FlexiWarehouseResolver.ForProductType(item.ProductType))
            .ToList();

        foreach (var warehouseGroup in consumptionByWarehouse)
        {
            var warehouseId = warehouseGroup.Key;
            var stockItems = await _stockClient.StockToDateAsync(request.Date, warehouseId, cancellationToken);
            var stockMovementItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>();

            foreach (var consumptionItem in warehouseGroup)
            {
                var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
                var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

                // Track cost per manufactured product
                productCosts[consumptionItem.SourceProductCode] += unitPrice * consumptionItem.Amount;

                stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = consumptionItem.ProductCode,
                    ProductName = consumptionItem.ProductName,
                    Amount = consumptionItem.Amount,
                    AmountIssued = consumptionItem.Amount,
                    LotNumber = consumptionItem.LotNumber,
                    Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UnitPrice = unitPrice,
                });
            }

            var documentType = GetConsumptionDocumentType(warehouseId);
            var note = CreateDescription(request);

            var consumptionRequest = new StockItemsMovementUpsertRequestFlexiDto
            {
                CreatedBy = request.CreatedBy,
                AccountingDate = request.Date,
                IssueDate = request.Date,
                StockItems = stockMovementItems,
                Description = note,
                DocumentTypeCode = documentType,
                StockMovementDirection = StockMovementDirection.Out,
                Note = request.ManufactureOrderCode,
                WarehouseId = warehouseId.ToString()
            };

            var consumptionResult = await _stockMovementClient.SaveAsync(consumptionRequest, cancellationToken);

            if (consumptionResult != null && !consumptionResult.IsSuccess)
            {
                throw new FlexiManufactureException(
                    FlexiManufactureOperationKind.ConsumptionMovement,
                    $"Failed to create consumption stock movement for warehouse {warehouseId}",
                    warehouseId: warehouseId,
                    rawFlexiError: consumptionResult.GetErrorMessage());
            }
        }
    }

    public async Task SubmitConsolidatedProductionAsync(
        SubmitManufactureClientRequest request,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken)
    {
        var productMovementItems = request.Items
            .Where(i => i.Amount > 0)
            .Select(item =>
            {
                var productCost = productCosts.GetValueOrDefault(item.ProductCode, 0);
                var unitPrice = item.Amount > 0 ? productCost / (double)item.Amount : 0;

                return new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = item.ProductCode,
                    ProductName = item.ProductName,
                    Amount = (double)item.Amount,
                    AmountIssued = (double)item.Amount,
                    LotNumber = request.LotNumber,
                    Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UnitPrice = unitPrice
                };
            }).ToList();

        // Don't create production movement if there are no products
        if (productMovementItems.Count == 0)
        {
            return;
        }

        var documentType = GetProductionDocumentType(request.ManufactureType);
        var note = CreateDescription(request);
        var productionRequest = new StockItemsMovementUpsertRequestFlexiDto
        {
            CreatedBy = request.CreatedBy,
            AccountingDate = request.Date,
            IssueDate = request.Date,
            StockItems = productMovementItems,
            Description = note,
            DocumentTypeCode = documentType,
            StockMovementDirection = StockMovementDirection.In,
            Note = request.ManufactureOrderCode,
            WarehouseId = FlexiStockClient.ProductsWarehouseId.ToString()
        };

        var productionResult = await _stockMovementClient.SaveAsync(productionRequest, cancellationToken);

        if (productionResult != null && !productionResult.IsSuccess)
        {
            throw new FlexiManufactureException(
                FlexiManufactureOperationKind.ProductionMovement,
                "Failed to create production stock movement",
                rawFlexiError: productionResult.GetErrorMessage());
        }
    }

    private static string GetProductionDocumentType(ErpManufactureType manufactureType)
    {
        var documentType = manufactureType switch
        {
            ErpManufactureType.SemiProduct => WarehouseDocumentType_InboundSemiProduct,
            ErpManufactureType.Product => WarehouseDocumentType_InboundProduct,
            _ => throw new InvalidOperationException("Unknown warehouse for consumption movement for manufacture type " + manufactureType)
        };
        return documentType;
    }

    private static string GetConsumptionDocumentType(int warehouseId)
    {
        var documentType = warehouseId switch
        {
            FlexiStockClient.SemiProductsWarehouseId => WarehouseDocumentType_OutboundSemiProduct,
            FlexiStockClient.MaterialWarehouseId => WarehouseDocumentType_OutboundMaterial,
            _ => throw new InvalidOperationException("Unknown warehouse for consumption movement for warehouseId " + warehouseId)
        };
        return documentType;
    }

    private static string CreateDescription(SubmitManufactureClientRequest request)
    {
        return request.ManufactureType == ErpManufactureType.Product && request.Items.Count == 1
            ? $"{request.Items[0].ProductCode} - {request.Items[0].ProductName}"
            : request.ManufactureInternalNumber;
    }
}
