using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.IssuedOrders;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.IssuedOrders;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

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
}

public class FlexiManufactureClient : IManufactureClient
{
    private const string DocumentTypeSemiProduct = "VYR-POLOTOVAR";
    private const string DocumentTypeProduct = "VYR-PRODUKT";
    private const string WarehouseDocumentTypeSemiProduct = "VYROBA-POLOTOVAR";
    private const string WarehouseDocumentTypeProduct = "VYROBA-PRODUKT";
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

    public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ManufactureType == ErpManufactureType.Product)
        {
            // For products, create separate consumption and production movements for each product
            await SubmitManufacturePerProductAsync(request, cancellationToken);
        }
        else
        {
            // For semi-products, use aggregated approach (existing behavior)
            await SubmitManufactureAggregatedAsync(request, cancellationToken);
        }

        return request.ManufactureOrderCode;
    }

    private async Task SubmitManufactureAggregatedAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken)
    {
        var ingredientRequirements = await AggregateIngredientRequirementsAsync(request, cancellationToken);
        if (request.ValidateIngredientStock)
        {
            await ValidateIngredientStockAsync(ingredientRequirements, cancellationToken);
        }
        var ingredientLots = await LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
        var consumptionItems = AllocateConsumptionItemsUsingFefo(ingredientRequirements, ingredientLots);
        var totalConsumptionCost = await SubmitConsumptionMovementsAsync(request, consumptionItems, cancellationToken);
        await SubmitProductionMovementAsync(request, totalConsumptionCost, cancellationToken);
    }

    private async Task SubmitManufacturePerProductAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken)
    {
        foreach (var item in request.Items)
        {
            // Skip items with zero or negative amount
            if (item.Amount <= 0)
            {
                continue;
            }

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
                ValidateIngredientStock = request.ValidateIngredientStock
            };

            // Process this product independently
            var ingredientRequirements = await AggregateIngredientRequirementsAsync(singleProductRequest, cancellationToken);
            if (request.ValidateIngredientStock)
            {
                await ValidateIngredientStockAsync(ingredientRequirements, cancellationToken);
            }
            var ingredientLots = await LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
            var consumptionItems = AllocateConsumptionItemsUsingFefo(ingredientRequirements, ingredientLots);
            var productConsumptionCost = await SubmitConsumptionMovementsAsync(singleProductRequest, consumptionItems, cancellationToken);
            await SubmitProductionMovementAsync(singleProductRequest, productConsumptionCost, cancellationToken);
        }
    }

    private async Task<Dictionary<string, IngredientRequirement>> AggregateIngredientRequirementsAsync(
        SubmitManufactureClientRequest request,
        CancellationToken cancellationToken)
    {
        var ingredientRequirements = new Dictionary<string, IngredientRequirement>();

        foreach (var item in request.Items)
        {
            var template = await GetManufactureTemplateAsync(item.ProductCode, cancellationToken);
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
        Dictionary<string, List<CatalogLot>> ingredientLots)
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
                    Amount = requirement.RequiredAmount
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
                    Amount = amountFromThisLot
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

    private async Task<double> SubmitConsumptionMovementsAsync(
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

        foreach (var warehouseGroup in consumptionByWarehouse)
        {
            var warehouseId = warehouseGroup.Key;
            var stockItems = await _stockClient.StockToDateAsync(request.Date, warehouseId, cancellationToken);
            var stockMovementItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>();

            foreach (var consumptionItem in warehouseGroup)
            {
                var stockItem = stockItems.FirstOrDefault(s => s.ProductCode == consumptionItem.ProductCode);
                var unitPrice = stockItem != null ? (double)stockItem.Price : 0;
                totalConsumptionCost += unitPrice * consumptionItem.Amount;

                stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = consumptionItem.ProductCode,
                    ProductName = consumptionItem.ProductName,
                    Amount = consumptionItem.Amount,
                    AmountIssued = consumptionItem.Amount,
                    LotNumber = consumptionItem.LotNumber,
                    Expiration = consumptionItem.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UnitPrice = unitPrice
                });
            }

            var documentType = warehouseId switch
            {
                _ when warehouseId == FlexiStockClient.SemiProductsWarehouseId => WarehouseDocumentTypeSemiProduct,
                _ when warehouseId == FlexiStockClient.ProductsWarehouseId => WarehouseDocumentTypeProduct,
                _ => WarehouseDocumentTypeSemiProduct
            };

            // For products, include product code and name in note (format: "ProductCode - ProductName")
            var note = request.ManufactureType == ErpManufactureType.Product && request.Items.Count == 1
                ? $"{request.Items[0].ProductCode} - {request.Items[0].ProductName}"
                : request.ManufactureInternalNumber;

            var consumptionRequest = new StockItemsMovementUpsertRequestFlexiDto
            {
                CreatedBy = request.CreatedBy,
                AccountingDate = request.Date,
                IssueDate = request.Date,
                StockItems = stockMovementItems,
                Description = request.ManufactureOrderCode,
                DocumentTypeCode = documentType,
                StockMovementDirection = StockMovementDirection.Out,
                Note = note,
                WarehouseId = warehouseId.ToString()
            };

            var consumptionResult = await _stockMovementClient.SaveAsync(consumptionRequest, cancellationToken);

            if (!consumptionResult.IsSuccess)
            {
                throw new InvalidOperationException(
                    $"Failed to create consumption stock movement for warehouse {warehouseId}: {consumptionResult.GetErrorMessage()}"
                );
            }
        }

        return Math.Round(totalConsumptionCost, 4);
    }

    private async Task SubmitProductionMovementAsync(
        SubmitManufactureClientRequest request,
        double totalConsumptionCost,
        CancellationToken cancellationToken)
    {
        var productWarehouseId = request.ManufactureType == ErpManufactureType.SemiProduct
            ? FlexiStockClient.SemiProductsWarehouseId
            : FlexiStockClient.ProductsWarehouseId;

        var productDocumentType = request.ManufactureType == ErpManufactureType.SemiProduct
            ? WarehouseDocumentTypeSemiProduct
            : WarehouseDocumentTypeProduct;

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

        // For products, include product code and name in note (format: "ProductCode - ProductName")
        var note = request.ManufactureType == ErpManufactureType.Product && request.Items.Count == 1
            ? $"{request.Items[0].ProductCode} - {request.Items[0].ProductName}"
            : request.ManufactureInternalNumber;

        var productionRequest = new StockItemsMovementUpsertRequestFlexiDto
        {
            CreatedBy = request.CreatedBy,
            AccountingDate = request.Date,
            IssueDate = request.Date,
            StockItems = productMovementItems,
            Description = request.ManufactureOrderCode,
            DocumentTypeCode = productDocumentType,
            StockMovementDirection = StockMovementDirection.In,
            Note = note,
            WarehouseId = productWarehouseId.ToString()
        };

        var productionResult = await _stockMovementClient.SaveAsync(productionRequest, cancellationToken);

        if (!productionResult.IsSuccess)
        {
            throw new InvalidOperationException(
                $"Failed to create production stock movement: {productionResult.GetErrorMessage()}"
            );
        }
    }

    public async Task<DiscardResidualSemiProductResponse> DiscardResidualSemiProductAsync(DiscardResidualSemiProductRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting residual semi-product discard process. ManufactureOrderCode: {ManufactureOrderCode}, SemiProductCode: {SemiProductCode}, MaxAutoDiscardQuantity: {MaxAutoDiscardQuantity}",
            request.ManufactureOrderCode, request.ProductCode, request.MaxAutoDiscardQuantity);

        try
        {
            // Step 1: Read current stock quantity for the semi-product
            var stockItems = await _stockClient.StockToDateAsync(request.CompletionDate, FlexiStockClient.SemiProductsWarehouseId, cancellationToken);
            var semiProductStock = stockItems.FirstOrDefault(s => s.ProductCode == request.ProductCode);

            if (semiProductStock == null)
            {
                _logger.LogDebug("No stock found for semi-product {SemiProductCode} in warehouse {WarehouseId}",
                    request.ProductCode, FlexiStockClient.SemiProductsWarehouseId);

                return new DiscardResidualSemiProductResponse
                {
                    Success = true,
                    QuantityFound = 0,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = false,
                    Details = "Nebylo nalezeno žádné zbytkové množství - žádná akce není potřeba"
                };
            }

            var currentQuantity = (double)semiProductStock.Stock;

            _logger.LogDebug("Found {CurrentQuantity} units of semi-product {SemiProductCode} in stock",
                currentQuantity, request.ProductCode);


            if (currentQuantity > request.MaxAutoDiscardQuantity)
            {
                _logger.LogWarning("Residual quantity {CurrentQuantity} exceeds auto-discard limit {MaxAutoDiscardQuantity} for product {SemiProductCode}",
                    currentQuantity, request.MaxAutoDiscardQuantity, request.ProductCode);

                return new DiscardResidualSemiProductResponse
                {
                    Success = false,
                    QuantityFound = currentQuantity,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = true,
                    Details = $"Množství {currentQuantity} překračuje limit pro automatické vyřazení {request.MaxAutoDiscardQuantity} - vyžaduje ruční schválení"
                };
            }

            // Step 2: Load all available lots for the semi-product
            _logger.LogDebug("Loading all available lots for semi-product {SemiProductCode}", request.ProductCode);
            var lots = await _lotsClient.GetAsync(request.ProductCode, cancellationToken: cancellationToken);
            var availableLots = lots.Where(l => l.Amount > 0).ToList();

            if (availableLots.Count == 0)
            {
                _logger.LogWarning("No available lots found for semi-product {SemiProductCode}, but stock shows {CurrentQuantity} units",
                    request.ProductCode, currentQuantity);

                return new DiscardResidualSemiProductResponse
                {
                    Success = false,
                    QuantityFound = currentQuantity,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = true,
                    ErrorMessage = "Nebyly nalezeny žádné šarže pro vyřazení",
                    Details = "Skladový stav existuje, ale nejsou dostupné šarže - vyžaduje ruční schválení"
                };
            }

            _logger.LogDebug("Found {LotCount} available lots for semi-product {SemiProductCode}", availableLots.Count, request.ProductCode);

            // Step 3: Create stock movement items for each lot
            var stockMovementItems = new List<StockItemsMovementUpsertRequestItemFlexiDto>();
            double totalAmountToDiscard = 0;

            foreach (var lot in availableLots)
            {
                var amountToDiscard = (double)lot.Amount;
                totalAmountToDiscard += amountToDiscard;

                stockMovementItems.Add(new StockItemsMovementUpsertRequestItemFlexiDto
                {
                    ProductCode = request.ProductCode,
                    ProductName = request.ProductName,
                    Amount = amountToDiscard,
                    AmountIssued = amountToDiscard,
                    LotNumber = lot.Lot,
                    Expiration = lot.Expiration?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                    UnitPrice = (double)semiProductStock.Price,
                });

                _logger.LogDebug("Added lot {LotNumber} with amount {Amount} and expiration {Expiration} to discard",
                    lot.Lot, amountToDiscard, lot.Expiration);
            }

            _logger.LogDebug("Creating stock movement to discard {TotalAmount} units across {LotCount} lots of {SemiProductCode}",
                totalAmountToDiscard, availableLots.Count, request.ProductCode);

            // Step 4: Create stock movement for discard (stock reduction)
            string? movementReference;
            try
            {
                var stockMovementRequest = new StockItemsMovementUpsertRequestFlexiDto()
                {
                    CreatedBy = request.CompletedBy,
                    AccountingDate = _timeProvider.GetLocalNow().DateTime,
                    IssueDate = _timeProvider.GetLocalNow().DateTime,
                    StockItems = stockMovementItems,
                    Description = request.ManufactureOrderCode,
                    DocumentTypeCode = WarehouseDocumentTypeSemiProduct,
                    StockMovementDirection = StockMovementDirection.Out,
                    Note = request.ManufactureOrderCode,
                    WarehouseId = FlexiStockClient.SemiProductsWarehouseId.ToString(),
                };

                var stockMovementResult = await _stockMovementClient.SaveAsync(stockMovementRequest, cancellationToken);
                if (!stockMovementResult.IsSuccess)
                {
                    return new DiscardResidualSemiProductResponse
                    {
                        Success = false,
                        QuantityFound = currentQuantity,
                        QuantityDiscarded = 0,
                        RequiresManualApproval = true,
                        ErrorMessage = $"Selhalo vytvoření skladového pohybu: {stockMovementResult.GetErrorMessage()}",
                        Details = "Vytvoření skladového pohybu selhalo - vyžaduje ruční schválení"
                    };
                }
                var newDocumentIdString = stockMovementResult?.Result?.Results?.FirstOrDefault()?.Id;
                var newDocumentId = Int32.Parse(newDocumentIdString);
                var document = await _stockMovementClient.GetAsync(newDocumentId);
                movementReference = document.FirstOrDefault()?.Document.DocumentCode;

                _logger.LogDebug("Successfully created stock movement {MovementReference} for discard", movementReference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create stock movement for discard, but continuing with discard process");
                return new DiscardResidualSemiProductResponse
                {
                    Success = false,
                    QuantityFound = currentQuantity,
                    QuantityDiscarded = 0,
                    RequiresManualApproval = true,
                    ErrorMessage = $"Selhalo vytvoření skladového pohybu: {ex.Message}",
                    Details = "Vytvoření skladového pohybu selhalo - vyžaduje ruční schválení"
                };
            }

            _logger.LogInformation("Successfully discarded {TotalAmount} units across {LotCount} lots of semi-product {SemiProductCode} for manufacture order {ManufactureOrderCode}",
                totalAmountToDiscard, availableLots.Count, request.ProductCode, request.ManufactureOrderCode);

            return new DiscardResidualSemiProductResponse
            {
                Success = true,
                QuantityFound = currentQuantity,
                QuantityDiscarded = totalAmountToDiscard,
                RequiresManualApproval = false,
                StockMovementReference = movementReference,
                Details = $"Úspěšně automaticky vyřazeno {totalAmountToDiscard} kusů z {availableLots.Count} šarží"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discard residual semi-product for manufacture order {ManufactureOrderCode}: {ErrorMessage}",
                request.ManufactureOrderCode, ex.Message);

            return new DiscardResidualSemiProductResponse
            {
                Success = false,
                QuantityFound = 0,
                QuantityDiscarded = 0,
                RequiresManualApproval = false,
                ErrorMessage = $"Selhalo vyřazení zbytkového polotovaru: {ex.Message}"
            };
        }
    }

    public async Task<ManufactureTemplate> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        var bom = await _bomClient.GetAsync(id, cancellationToken);

        var header = bom.SingleOrDefault(s => s.Level == 1) ?? throw new ApplicationException(message: $"No BoM header for product {id} found");
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
}
