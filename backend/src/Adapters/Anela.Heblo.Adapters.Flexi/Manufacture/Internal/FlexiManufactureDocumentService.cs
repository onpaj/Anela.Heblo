using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
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

    public async Task<ConsolidatedConsumptionCodes> SubmitConsolidatedConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        Dictionary<string, double> productCosts,
        CancellationToken cancellationToken)
    {
        string? semiProductIssueCode = null;
        string? materialIssueCode = null;

        var consumptionByWarehouse = consumptionItems
            .GroupBy(item => item.ProductType switch
            {
                ProductType.Material => FlexiStockClient.MaterialWarehouseId,
                ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
                ProductType.Product or ProductType.Goods => FlexiStockClient.ProductsWarehouseId,
                _ => FlexiStockClient.MaterialWarehouseId
            })
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

                // Round to 4 decimal places to eliminate double-precision drift that
                // originates from (decimal -> double) conversions accumulating across
                // FEFO lot allocations. Without this, Flexi rejects movements like
                // 5800.0000000000009 > 5800.0 available. See PR #572 / commit fba995e1.
                var amount = Math.Round(consumptionItem.Amount, 4);

                // Track cost per manufactured product
                productCosts[consumptionItem.SourceProductCode] += unitPrice * amount;

                stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = consumptionItem.ProductCode,
                    ProductName = consumptionItem.ProductName,
                    Amount = amount,
                    AmountIssued = amount,
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

            var docCode = consumptionResult?.Result?.Results?.FirstOrDefault()?.Code;
            if (documentType == WarehouseDocumentType_OutboundSemiProduct)
            {
                semiProductIssueCode = docCode;
            }
            else if (documentType == WarehouseDocumentType_OutboundMaterial)
            {
                materialIssueCode = docCode;
            }
        }

        return new ConsolidatedConsumptionCodes(semiProductIssueCode, materialIssueCode);
    }

    public async Task<string?> SubmitConsolidatedProductionAsync(
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
            return null;
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

        return productionResult?.Result?.Results?.FirstOrDefault()?.Code;
    }

    public async Task<ConsumptionResult> SubmitConsumptionAsync(
        SubmitManufactureClientRequest request,
        List<ConsumptionItem> consumptionItems,
        CancellationToken cancellationToken)
    {
        var consumptionByWarehouse = consumptionItems
            .GroupBy(item => item.ProductType switch
            {
                ProductType.Material => FlexiStockClient.MaterialWarehouseId,
                ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
                ProductType.Product or ProductType.Goods => FlexiStockClient.ProductsWarehouseId,
                _ => FlexiStockClient.MaterialWarehouseId
            })
            .ToList();

        double totalConsumptionCost = 0;
        string? capturedDocCode = null;

        foreach (var warehouseGroup in consumptionByWarehouse)
        {
            var warehouseId = warehouseGroup.Key;
            var stockItems = await _stockClient.StockToDateAsync(request.Date, warehouseId, cancellationToken);
            var stockMovementItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>();

            foreach (var consumptionItem in warehouseGroup)
            {
                var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
                var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

                // Round to 4 decimal places — see SubmitConsolidatedConsumptionAsync above.
                var amount = Math.Round(consumptionItem.Amount, 4);

                totalConsumptionCost += unitPrice * amount;

                stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = consumptionItem.ProductCode,
                    ProductName = consumptionItem.ProductName,
                    Amount = amount,
                    AmountIssued = amount,
                    LotNumber = consumptionItem.LotNumber,
                    Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UnitPrice = unitPrice
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

            capturedDocCode = consumptionResult?.Result?.Results?.FirstOrDefault()?.Code;
        }

        return new ConsumptionResult(Math.Round(totalConsumptionCost, 4), capturedDocCode);
    }

    public async Task<string?> SubmitProductionAsync(
        SubmitManufactureClientRequest request,
        double totalConsumptionCost,
        CancellationToken cancellationToken)
    {
        var productWarehouseId = request.ManufactureType == ErpManufactureType.SemiProduct
            ? FlexiStockClient.SemiProductsWarehouseId
            : FlexiStockClient.ProductsWarehouseId;

        var documentType = GetProductionDocumentType(request.ManufactureType);
        var totalManufacturedAmount = request.Items.Sum(i => (double)i.Amount);
        var manufacturedUnitPrice = totalManufacturedAmount > 0 ? totalConsumptionCost / totalManufacturedAmount : 0;

        var productMovementItems = request.Items.Select(item => new StockItemsMovementUpsertRequestItemFlexiDto
        {
            ProductCode = item.ProductCode,
            ProductName = item.ProductName,
            Amount = (double)item.Amount,
            AmountIssued = (double)item.Amount,
            LotNumber = request.LotNumber,
            Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            UnitPrice = manufacturedUnitPrice
        }).ToList();

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
            WarehouseId = productWarehouseId.ToString()
        };

        var productionResult = await _stockMovementClient.SaveAsync(productionRequest, cancellationToken);

        if (productionResult != null && !productionResult.IsSuccess)
        {
            throw new FlexiManufactureException(
                FlexiManufactureOperationKind.ProductionMovement,
                "Failed to create production stock movement",
                rawFlexiError: productionResult.GetErrorMessage());
        }

        return productionResult?.Result?.Results?.FirstOrDefault()?.Code;
    }

    public async Task<string?> SubmitDirectSemiProductOutputAsync(
        SubmitManufactureClientRequest request,
        CancellationToken cancellationToken)
    {
        var warehouseId = FlexiStockClient.SemiProductsWarehouseId;
        var stockItems = await _stockClient.StockToDateAsync(request.Date, warehouseId, cancellationToken);
        var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == request.DirectSemiProductOutputCode);
        var unitPrice = stockItem != null ? (double)stockItem.Price : 0;

        var amount = Math.Round((double)request.DirectSemiProductOutputAmount, 4);

        var movementItem = new StockItemsMovementUpsertRequestItemFlexiDto
        {
            ProductCode = request.DirectSemiProductOutputCode!,
            ProductName = request.DirectSemiProductOutputName ?? request.DirectSemiProductOutputCode!,
            Amount = amount,
            AmountIssued = amount,
            LotNumber = request.LotNumber,
            Expiration = request.ExpirationDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            UnitPrice = unitPrice,
        };

        var discardRequest = new StockItemsMovementUpsertRequestFlexiDto
        {
            CreatedBy = request.CreatedBy,
            AccountingDate = request.Date,
            IssueDate = request.Date,
            StockItems = new List<StockItemsMovementUpsertRequestItemFlexiDto> { movementItem },
            Description = request.ManufactureInternalNumber,
            DocumentTypeCode = WarehouseDocumentType_OutboundSemiProduct,
            StockMovementDirection = StockMovementDirection.Out,
            Note = request.ManufactureOrderCode,
            WarehouseId = warehouseId.ToString(),
        };

        var result = await _stockMovementClient.SaveAsync(discardRequest, cancellationToken);

        if (result != null && !result.IsSuccess)
        {
            throw new FlexiManufactureException(
                FlexiManufactureOperationKind.ConsumptionMovement,
                "Failed to create discard stock movement for direct semiproduct output",
                warehouseId: warehouseId,
                rawFlexiError: result.GetErrorMessage());
        }

        return result?.Result?.Results?.FirstOrDefault()?.Code;
    }

    private static string GetProductionDocumentType(ErpManufactureType manufactureType)
    {
        return manufactureType switch
        {
            ErpManufactureType.SemiProduct => WarehouseDocumentType_InboundSemiProduct,
            ErpManufactureType.Product => WarehouseDocumentType_InboundProduct,
            _ => throw new InvalidOperationException("Unknown warehouse for consumption movement for manufacture type " + manufactureType)
        };
    }

    private static string GetConsumptionDocumentType(int warehouseId)
    {
        return warehouseId switch
        {
            FlexiStockClient.SemiProductsWarehouseId => WarehouseDocumentType_OutboundSemiProduct,
            FlexiStockClient.MaterialWarehouseId => WarehouseDocumentType_OutboundMaterial,
            _ => throw new InvalidOperationException("Unknown warehouse for consumption movement for warehouseId " + warehouseId)
        };
    }

    private static string CreateDescription(SubmitManufactureClientRequest request)
    {
        return request.ManufactureType == ErpManufactureType.Product && request.Items.Count == 1
            ? $"{request.Items[0].ProductCode} - {request.Items[0].ProductName}"
            : request.ManufactureInternalNumber;
    }
}
