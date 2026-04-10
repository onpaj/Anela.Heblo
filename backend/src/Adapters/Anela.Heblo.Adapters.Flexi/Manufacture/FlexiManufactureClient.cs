using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.IssuedOrders;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

internal sealed record ConsumptionResult(double TotalCost, string? DocCode);

internal sealed record ConsolidatedConsumptionCodes(string? SemiProductIssueCode, string? MaterialIssueCode);

internal sealed class IngredientRequirement
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required ProductType ProductType { get; init; }
    public required double RequiredAmount { get; init; }
    public required bool HasLots { get; init; }
}

internal sealed class ConsumptionItem
{
    public required string ProductCode { get; init; }
    public required string ProductName { get; init; }
    public required ProductType ProductType { get; init; }
    public string? LotNumber { get; init; }
    public DateOnly? Expiration { get; init; }
    public required double Amount { get; init; }
    public required string SourceProductCode { get; init; }
}

public class FlexiManufactureClient : IManufactureClient
{
    private const string WarehouseDocumentType_OutboundMaterial = "V-VYDEJ-MATERIAL";
    private const string WarehouseDocumentType_InboundSemiProduct = "V-PRIJEM-POLOTOVAR";
    private const string WarehouseDocumentType_OutboundSemiProduct = "V-VYDEJ-POLOTOVAR";
    private const string WarehouseDocumentType_InboundProduct = "V-PRIJEM-VYROBEK";


    private const string WarehouseCodeSemiProduct = "POLOTOVARY";
    private const string WarehouseCodeProduct = "ZBOZI";

    private readonly IIssuedOrdersClient _ordersClient;
    private readonly IErpStockClient _stockClient;
    private readonly IStockItemsMovementClient _stockMovementClient;
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILotsClient _lotsClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<FlexiManufactureClient> _logger;

    public FlexiManufactureClient(
        IIssuedOrdersClient ordersClient,
        IErpStockClient stockClient,
        IStockItemsMovementClient stockMovementClient,
        IBoMClient bomClient,
        IProductSetsClient productSetsClient,
        ILotsClient lotsClient,
        TimeProvider timeProvider,
        ILogger<FlexiManufactureClient> logger)
    {
        _ordersClient = ordersClient ?? throw new ArgumentNullException(nameof(ordersClient));
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _timeProvider = timeProvider;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<SubmitManufactureClientResponse> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ManufactureType == ErpManufactureType.Product)
        {
            // For products, create separate consumption and production movements for each product
            return await SubmitManufacturePerProductAsync(request, cancellationToken);
        }
        else
        {
            // For semi-products, use aggregated approach (existing behavior)
            return await SubmitManufactureAggregatedAsync(request, cancellationToken);
        }
    }

    private async Task<SubmitManufactureClientResponse> SubmitManufactureAggregatedAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken)
    {
        var ingredientRequirements = await AggregateIngredientRequirementsAsync(request, cancellationToken);
        if (request.ValidateIngredientStock)
        {
            await ValidateIngredientStockAsync(ingredientRequirements, cancellationToken);
        }
        var ingredientLots = await LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
        var consumptionItems = AllocateConsumptionItemsUsingFefo(ingredientRequirements, ingredientLots);
        var consumptionResult = await SubmitConsumptionMovementsAsync(request, consumptionItems, cancellationToken);
        var semiProductReceiptDocCode = await SubmitProductionMovementAsync(request, consumptionResult.TotalCost, cancellationToken);

        return new SubmitManufactureClientResponse
        {
            ManufactureId = request.ManufactureOrderCode,
            MaterialIssueForSemiProductDocCode = consumptionResult.DocCode,
            SemiProductReceiptDocCode = semiProductReceiptDocCode,
        };
    }

    private async Task<SubmitManufactureClientResponse> SubmitManufacturePerProductAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken)
    {
        // Phase 1: Collect all ingredient requirements with product attribution
        var allConsumptionItems = new List<ConsumptionItem>();
        var productCosts = new Dictionary<string, double>();

        foreach (var item in request.Items.Where(i => i.Amount > 0))
        {
            // Create a single-item request for this specific product
            var singleProductRequest = new SubmitManufactureClientRequest
            {
                ManufactureOrderCode = request.ManufactureOrderCode,
                ManufactureInternalNumber = request.ManufactureInternalNumber,
                Date = request.Date,
                CreatedBy = request.CreatedBy,
                Items = new List<SubmitManufactureClientItem> { item },
                ManufactureType = request.ManufactureType,
                LotNumber = request.LotNumber,
                ExpirationDate = request.ExpirationDate,
                ValidateIngredientStock = request.ValidateIngredientStock,
                ResidueDistribution = request.ResidueDistribution
            };

            // Get ingredient requirements for this product
            var ingredientRequirements = await AggregateIngredientRequirementsAsync(singleProductRequest, cancellationToken);

            // When ResidueDistribution is set, override the semiproduct ingredient amount with the
            // distribution-adjusted consumption so that all products together consume exactly
            // ActualSemiProductQuantity grams (not the BoM-theoretical amount).
            if (request.ResidueDistribution != null)
            {
                var distributionEntry = request.ResidueDistribution.Products
                    .FirstOrDefault(p => p.ProductCode == item.ProductCode);

                if (distributionEntry != null)
                {
                    var semiProductKey = ingredientRequirements
                        .FirstOrDefault(kv => kv.Value.ProductType == ProductType.SemiProduct).Key;

                    if (semiProductKey != null)
                    {
                        var existing = ingredientRequirements[semiProductKey];
                        ingredientRequirements[semiProductKey] = new IngredientRequirement
                        {
                            ProductCode = existing.ProductCode,
                            ProductName = existing.ProductName,
                            ProductType = existing.ProductType,
                            RequiredAmount = (double)distributionEntry.AdjustedConsumption,
                            HasLots = existing.HasLots
                        };
                    }
                }
            }

            if (request.ValidateIngredientStock)
            {
                await ValidateIngredientStockAsync(ingredientRequirements, cancellationToken);
            }

            var ingredientLots = await LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
            var consumptionItems = AllocateConsumptionItemsUsingFefo(ingredientRequirements, ingredientLots, item.ProductCode);

            allConsumptionItems.AddRange(consumptionItems);
            productCosts[item.ProductCode] = 0; // Will be calculated during consumption
        }

        // Phase 2: Create ONE consume document (per warehouse) with all consumption lines
        var consumptionCodes = await SubmitConsolidatedConsumptionMovementsAsync(request, allConsumptionItems, productCosts, cancellationToken);

        // Phase 3: Create ONE produce document with all products
        var productReceiptDocCode = await SubmitConsolidatedProductionMovementAsync(request, productCosts, cancellationToken);

        return new SubmitManufactureClientResponse
        {
            ManufactureId = request.ManufactureOrderCode,
            SemiProductIssueForProductDocCode = consumptionCodes.SemiProductIssueCode,
            MaterialIssueForProductDocCode = consumptionCodes.MaterialIssueCode,
            ProductReceiptDocCode = productReceiptDocCode,
        };
    }

    private async Task<Dictionary<string, IngredientRequirement>> AggregateIngredientRequirementsAsync(
        SubmitManufactureClientRequest request,
        CancellationToken cancellationToken)
    {
        var ingredientRequirements = new Dictionary<string, IngredientRequirement>();

        foreach (var item in request.Items)
        {
            var template = await GetManufactureTemplateAsync(item.ProductCode, cancellationToken)
                ?? throw new ApplicationException($"No BoM header for product {item.ProductCode} found");
            var scaleFactor = (double)item.Amount / template.Amount;

            foreach (var ingredient in template.Ingredients.Where(w => w.ProductType != ProductType.UNDEFINED))
            {
                var scaledAmount = ingredient.Amount * scaleFactor;

                if (ingredientRequirements.TryGetValue(ingredient.ProductCode, out var existing))
                {
                    ingredientRequirements[ingredient.ProductCode] = new IngredientRequirement
                    {
                        ProductCode = ingredient.ProductCode,
                        ProductName = existing.ProductName,
                        ProductType = existing.ProductType,
                        RequiredAmount = existing.RequiredAmount + scaledAmount,
                        HasLots = existing.HasLots
                    };
                }
                else
                {
                    ingredientRequirements[ingredient.ProductCode] = new IngredientRequirement
                    {
                        ProductCode = ingredient.ProductCode,
                        ProductName = ingredient.ProductName,
                        ProductType = ingredient.ProductType,
                        RequiredAmount = scaledAmount,
                        HasLots = ingredient.HasLots
                    };
                }
            }
        }

        return ingredientRequirements;
    }

    private async Task ValidateIngredientStockAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken)
    {
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var insufficientIngredients = new List<string>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            int warehouseId = requirement.ProductType switch
            {
                ProductType.Material => FlexiStockClient.MaterialWarehouseId,
                ProductType.SemiProduct => FlexiStockClient.SemiProductsWarehouseId,
                ProductType.Product or ProductType.Goods => FlexiStockClient.ProductsWarehouseId,
                _ => FlexiStockClient.MaterialWarehouseId
            };

            var stockItems = await _stockClient.StockToDateAsync(stockDate, warehouseId, cancellationToken);
            var ingredientStock = stockItems.FirstOrDefault(s => s.ProductCode == ingredientCode);
            var availableStock = ingredientStock != null ? (double)ingredientStock.Stock : 0;

            if (availableStock < requirement.RequiredAmount)
            {
                insufficientIngredients.Add(
                    $"{requirement.ProductName} ({ingredientCode}): Required {requirement.RequiredAmount:F2}, Available {availableStock:F2}"
                );
            }
        }

        if (insufficientIngredients.Any())
        {
            throw new InvalidOperationException(
                $"Insufficient stock for the following ingredients:\n{string.Join("\n", insufficientIngredients)}"
            );
        }
    }

    private async Task<Dictionary<string, List<CatalogLot>>> LoadAvailableLotsAsync(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        CancellationToken cancellationToken)
    {
        var ingredientLots = new Dictionary<string, List<CatalogLot>>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            if (requirement.HasLots)
            {
                var lots = await _lotsClient.GetAsync(ingredientCode, cancellationToken: cancellationToken);
                var availableLots = lots.Where(l => l.Amount > 0).ToList();
                ingredientLots[ingredientCode] = availableLots;
            }
            else
            {
                // For items without lots, create empty list
                ingredientLots[ingredientCode] = new List<CatalogLot>();
            }
        }

        return ingredientLots;
    }

    private List<ConsumptionItem> AllocateConsumptionItemsUsingFefo(
        Dictionary<string, IngredientRequirement> ingredientRequirements,
        Dictionary<string, List<CatalogLot>> ingredientLots,
        string sourceProductCode = "")
    {
        var consumptionItems = new List<ConsumptionItem>();

        foreach (var (ingredientCode, requirement) in ingredientRequirements)
        {
            if (!requirement.HasLots)
            {
                // For items without lots, create a single consumption item with full amount
                consumptionItems.Add(new ConsumptionItem
                {
                    ProductCode = ingredientCode,
                    ProductName = requirement.ProductName,
                    ProductType = requirement.ProductType,
                    LotNumber = null,
                    Expiration = null,
                    Amount = requirement.RequiredAmount,
                    SourceProductCode = sourceProductCode
                });
                continue;
            }

            // For items with lots, use FEFO allocation
            var lots = ingredientLots[ingredientCode];
            var sortedLots = lots
                .OrderBy(l => l.Expiration ?? DateOnly.MaxValue)
                .ThenBy(s => s.Id)
                .ToList();
            double remainingToAllocate = requirement.RequiredAmount;

            foreach (var lot in sortedLots)
            {
                if (remainingToAllocate <= 0)
                {
                    break;
                }

                var amountFromThisLot = Math.Min(remainingToAllocate, (double)lot.Amount);

                consumptionItems.Add(new ConsumptionItem
                {
                    ProductCode = ingredientCode,
                    ProductName = requirement.ProductName,
                    ProductType = requirement.ProductType,
                    LotNumber = lot.Lot,
                    Expiration = lot.Expiration,
                    Amount = amountFromThisLot,
                    SourceProductCode = sourceProductCode
                });

                remainingToAllocate -= amountFromThisLot;
            }

            if (remainingToAllocate > 0.001)
            {
                throw new InvalidOperationException(
                    $"Could not allocate sufficient lots for ingredient '{requirement.ProductName}' ({ingredientCode}). " +
                    $"Required: {requirement.RequiredAmount:F2}, Allocated: {requirement.RequiredAmount - remainingToAllocate:F2}, " +
                    $"Missing: {remainingToAllocate:F2}"
                );
            }
        }

        return consumptionItems;
    }

    private async Task<ConsumptionResult> SubmitConsumptionMovementsAsync(
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

            // For products, include product code and name in note (format: "ProductCode - ProductName")
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
                throw new InvalidOperationException(
                    $"Failed to create consumption stock movement for warehouse {warehouseId}: {consumptionResult.GetErrorMessage()}"
                );
            }

            capturedDocCode = consumptionResult?.Result?.Results?.FirstOrDefault()?.Code;
        }

        return new ConsumptionResult(Math.Round(totalConsumptionCost, 4), capturedDocCode);
    }




    private async Task<string?> SubmitProductionMovementAsync(
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
            throw new InvalidOperationException(
                $"Failed to create production stock movement: {productionResult.GetErrorMessage()}"
            );
        }

        return productionResult?.Result?.Results?.FirstOrDefault()?.Code;
    }



    private async Task<ConsolidatedConsumptionCodes> SubmitConsolidatedConsumptionMovementsAsync(
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

                // Track cost per manufactured product
                productCosts[consumptionItem.SourceProductCode] += unitPrice * consumptionItem.Amount;

                var amount = Math.Round(consumptionItem.Amount, 4);
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
                throw new InvalidOperationException(
                    $"Failed to create consumption stock movement for warehouse {warehouseId}: {consumptionResult.GetErrorMessage()}"
                );
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

    private async Task<string?> SubmitConsolidatedProductionMovementAsync(
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
            throw new InvalidOperationException(
                $"Failed to create production stock movement: {productionResult.GetErrorMessage()}"
            );
        }

        return productionResult?.Result?.Results?.FirstOrDefault()?.Code;
    }

    public async Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default)
    {
        await _bomClient.UpdateIngredientAmountAsync(productCode, ingredientCode, newAmount, cancellationToken);
    }

    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        var bom = await _bomClient.GetAsync(id, cancellationToken);

        var header = bom.SingleOrDefault(s => s.Level == 1);
        if (header == null)
            return null;

        var ingredients = bom.Where(w => w.Level != 1);

        // Get stock data to determine HasLots for each ingredient
        var stockDate = _timeProvider.GetLocalNow().DateTime;
        var allStockData = new List<ErpStock>();

        // Load stock from all warehouses to get HasLots information
        var warehouseIds = new[]
        {
            FlexiStockClient.MaterialWarehouseId,
            FlexiStockClient.SemiProductsWarehouseId,
            FlexiStockClient.ProductsWarehouseId
        };

        foreach (var warehouseId in warehouseIds)
        {
            var stockItems = await _stockClient.StockToDateAsync(stockDate, warehouseId, cancellationToken);
            allStockData.AddRange(stockItems);
        }

        var template = new ManufactureTemplate()
        {
            TemplateId = header.Id,
            ProductCode = header.IngredientCode.RemoveCodePrefix(),
            ProductName = header.IngredientFullName,
            Amount = header.Amount,
            OriginalAmount = header.Amount,
            Ingredients = ingredients.Select(s =>
            {
                var productCode = s.IngredientCode.RemoveCodePrefix();
                var stockItem = allStockData.FirstOrDefault(stock => stock.ProductCode == productCode);

                return new Ingredient()
                {
                    TemplateId = s.Id,
                    ProductCode = productCode,
                    ProductName = s.IngredientFullName,
                    Amount = s.Amount,
                    ProductType = ResolveProductType(s),
                    HasLots = stockItem?.HasLots ?? false,
                    HasExpiration = false // This information is not available from BoM, set to false as default
                };
            }).ToList(),
        };

        if (ingredients.Any(a => a.Ingredient.Any(b => b.ProductTypeId == (int)ProductType.SemiProduct)))
            template.ManufactureType = ManufactureType.MultiPhase;
        else
            template.ManufactureType = ManufactureType.SinglePhase;

        return template;
    }

    private ProductType ResolveProductType(BoMItemFlexiDto boMItemFlexiDto)
    {
        try
        {
            var productTypeId = boMItemFlexiDto.Ingredient?.FirstOrDefault()?.ProductTypeId;

            // Return UNDEFINED if no ProductTypeId is available
            if (!productTypeId.HasValue)
            {
                return ProductType.UNDEFINED;
            }

            // Check if the value is a valid ProductType enum value
            if (Enum.IsDefined(typeof(ProductType), productTypeId.Value))
            {
                return (ProductType)productTypeId.Value;
            }

            // Return UNDEFINED for unknown enum values
            return ProductType.UNDEFINED;
        }
        catch
        {
            // Return UNDEFINED for any exceptions
            return ProductType.UNDEFINED;
        }
    }

    public async Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken)
    {
        var templates = await _bomClient.GetByIngredientAsync(ingredientCode, cancellationToken);

        return templates
                .Select(s => new ManufactureTemplate()
                {
                    ProductCode = s.ParentCode!.RemoveCodePrefix(),
                    ProductName = s.ParentFullName!,
                    Amount = s.Amount,
                    TemplateId = s.Id,
                    BatchSize = s.Parent?.Amount ?? 0,
                })
        .Where(w => w.ProductCode != ingredientCode)
        .ToList();
    }

    public async Task<List<ProductPart>> GetSetPartsAsync(string setProductCode, CancellationToken cancellationToken = default)
    {
        var setParts = await _productSetsClient.GetAsync(setProductCode, cancellationToken: cancellationToken);

        return setParts
            .Select(s => new ProductPart()
            {
                ProductCode = s.Product.Code,
                ProductName = s.Product.Name,
                Amount = s.Quantity,
            })
            .ToList();
    }

    public async Task<List<ManufactureErpDocumentItem>> GetErpDocumentItemsAsync(string documentCode, int? documentTypeId = null, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().DateTime;
        var dateFrom = now.AddYears(-5);
        var dateTo = now.AddDays(1);

        var items = await _stockMovementClient.GetAsync(
            dateFrom,
            dateTo,
            documentCode: documentCode,
            documentTypeId: documentTypeId,
            cancellationToken: cancellationToken);

        return items.Select(item => new ManufactureErpDocumentItem
        {
            ProductCode = item.ProductCode,
            ProductName = item.Name,
            Amount = item.Amount,
            LotNumber = string.IsNullOrEmpty(item.Batch) ? null : item.Batch,
            ExpirationDate = ParseExpiration(item.Expiration),
        }).ToList();
    }

    private static DateOnly? ParseExpiration(string? expiration)
    {
        if (string.IsNullOrEmpty(expiration))
            return null;

        if (DateOnly.TryParse(expiration, out var date))
            return date;

        if (DateTime.TryParse(expiration, out var dateTime))
            return DateOnly.FromDateTime(dateTime);

        return null;
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
            WarehouseCode = request.ManufactureType == ErpManufactureType.SemiProduct ? WarehouseCodeSemiProduct : WarehouseCodeProduct,
        };
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
