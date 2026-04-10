using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

internal sealed class FlexiManufactureClient : IManufactureClient
{
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILogger<FlexiManufactureClient> _logger;
    private readonly IFlexiManufactureTemplateService _templateService;
    private readonly IFefoConsumptionAllocator _fefoAllocator;
    private readonly IFlexiIngredientRequirementAggregator _requirementAggregator;
    private readonly IFlexiIngredientStockValidator _stockValidator;
    private readonly IFlexiLotLoader _lotLoader;
    private readonly IFlexiManufactureDocumentService _movementService;

    public FlexiManufactureClient(
        IBoMClient bomClient,
        IProductSetsClient productSetsClient,
        ILogger<FlexiManufactureClient> logger,
        IFlexiManufactureTemplateService templateService,
        IFefoConsumptionAllocator fefoAllocator,
        IFlexiIngredientRequirementAggregator requirementAggregator,
        IFlexiIngredientStockValidator stockValidator,
        IFlexiLotLoader lotLoader,
        IFlexiManufactureDocumentService movementService)
    {
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _fefoAllocator = fefoAllocator ?? throw new ArgumentNullException(nameof(fefoAllocator));
        _requirementAggregator = requirementAggregator ?? throw new ArgumentNullException(nameof(requirementAggregator));
        _stockValidator = stockValidator ?? throw new ArgumentNullException(nameof(stockValidator));
        _lotLoader = lotLoader ?? throw new ArgumentNullException(nameof(lotLoader));
        _movementService = movementService ?? throw new ArgumentNullException(nameof(movementService));
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

            var ingredientLots = await _lotLoader.LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
            var consumptionItems = _fefoAllocator.Allocate(ingredientRequirements, ingredientLots, item.ProductCode);

            allConsumptionItems.AddRange(consumptionItems);
            productCosts[item.ProductCode] = 0; // Will be calculated during consumption
        }

        // Phase 2: Create ONE consume document (per warehouse) with all consumption lines
        await _movementService.SubmitConsolidatedConsumptionAsync(request, allConsumptionItems, productCosts, cancellationToken);

        // Phase 3: Create ONE produce document with all products
        await _movementService.SubmitConsolidatedProductionAsync(request, productCosts, cancellationToken);
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


}
