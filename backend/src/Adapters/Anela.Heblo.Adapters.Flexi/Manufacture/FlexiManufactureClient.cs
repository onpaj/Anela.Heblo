using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Adapters.Flexi.Stock;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using Rem.FlexiBeeSDK.Model.Products.StockMovement;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

internal sealed class FlexiManufactureClient : IManufactureClient
{
    private const string WarehouseDocumentType_OutboundMaterial = "V-VYDEJ-MATERIAL";
    private const string WarehouseDocumentType_InboundSemiProduct = "V-PRIJEM-POLOTOVAR";
    private const string WarehouseDocumentType_OutboundSemiProduct = "V-VYDEJ-POLOTOVAR";
    private const string WarehouseDocumentType_InboundProduct = "V-PRIJEM-VYROBEK";


    private readonly IErpStockClient _stockClient;
    private readonly IStockItemsMovementClient _stockMovementClient;
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILotsClient _lotsClient;
    private readonly ILogger<FlexiManufactureClient> _logger;
    private readonly IFlexiManufactureTemplateService _templateService;
    private readonly IFefoConsumptionAllocator _fefoAllocator;
    private readonly IFlexiIngredientRequirementAggregator _requirementAggregator;
    private readonly IFlexiIngredientStockValidator _stockValidator;

    public FlexiManufactureClient(
        IErpStockClient stockClient,
        IStockItemsMovementClient stockMovementClient,
        IBoMClient bomClient,
        IProductSetsClient productSetsClient,
        ILotsClient lotsClient,
        ILogger<FlexiManufactureClient> logger,
        IFlexiManufactureTemplateService templateService,
        IFefoConsumptionAllocator fefoAllocator,
        IFlexiIngredientRequirementAggregator requirementAggregator,
        IFlexiIngredientStockValidator stockValidator)
    {
        _stockClient = stockClient ?? throw new ArgumentNullException(nameof(stockClient));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
        _lotsClient = lotsClient ?? throw new ArgumentNullException(nameof(lotsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _fefoAllocator = fefoAllocator ?? throw new ArgumentNullException(nameof(fefoAllocator));
        _requirementAggregator = requirementAggregator ?? throw new ArgumentNullException(nameof(requirementAggregator));
        _stockValidator = stockValidator ?? throw new ArgumentNullException(nameof(stockValidator));
    }

    public async Task<string> SubmitManufactureAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken = default)
    {
        await SubmitManufacturePerProductAsync(request, cancellationToken);
        return request.ManufactureOrderCode;
    }

    private async Task SubmitManufacturePerProductAsync(SubmitManufactureClientRequest request, CancellationToken cancellationToken)
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
            var ingredientRequirements = await _requirementAggregator.AggregateAsync(singleProductRequest.Items, cancellationToken);

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
                await _stockValidator.ValidateAsync(ingredientRequirements, cancellationToken);
            }

            var ingredientLots = await LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
            var consumptionItems = _fefoAllocator.Allocate(ingredientRequirements, ingredientLots, item.ProductCode);

            allConsumptionItems.AddRange(consumptionItems);
            productCosts[item.ProductCode] = 0; // Will be calculated during consumption
        }

        // Phase 2: Create ONE consume document (per warehouse) with all consumption lines
        await SubmitConsolidatedConsumptionMovementsAsync(request, allConsumptionItems, productCosts, cancellationToken);

        // Phase 3: Create ONE produce document with all products
        await SubmitConsolidatedProductionMovementAsync(request, productCosts, cancellationToken);
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

    private async Task SubmitConsolidatedConsumptionMovementsAsync(
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

    private async Task SubmitConsolidatedProductionMovementAsync(
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

    public async Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default)
    {
        await _bomClient.UpdateIngredientAmountAsync(productCode, ingredientCode, newAmount, cancellationToken);
    }

    public Task<ManufactureTemplate?> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
        => _templateService.GetManufactureTemplateAsync(id, cancellationToken);

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
