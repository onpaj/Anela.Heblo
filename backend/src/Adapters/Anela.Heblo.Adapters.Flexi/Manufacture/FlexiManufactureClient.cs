using Anela.Heblo.Adapters.Flexi.Manufacture.Internal;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Logging;
using Rem.FlexiBeeSDK.Client;
using Rem.FlexiBeeSDK.Client.Clients.Accounting.Ledger;
using Rem.FlexiBeeSDK.Client.Clients.Products.BoM;
using Rem.FlexiBeeSDK.Client.Clients.Products.StockMovement;
using Rem.FlexiBeeSDK.Model;
using System.Net;

namespace Anela.Heblo.Adapters.Flexi.Manufacture;

internal class FlexiManufactureClient : IManufactureClient
{
    private readonly IBoMClient _bomClient;
    private readonly IProductSetsClient _productSetsClient;
    private readonly ILogger<FlexiManufactureClient> _logger;
    private readonly IFlexiManufactureTemplateService _templateService;
    private readonly IFefoConsumptionAllocator _fefoAllocator;
    private readonly IFlexiIngredientRequirementAggregator _requirementAggregator;
    private readonly IFlexiIngredientStockValidator _stockValidator;
    private readonly IFlexiLotLoader _lotLoader;
    private readonly IFlexiManufactureDocumentService _documentService;
    private readonly IStockItemsMovementClient _stockMovementClient;
    private readonly TimeProvider _timeProvider;

    public FlexiManufactureClient(
        IBoMClient bomClient,
        IProductSetsClient productSetsClient,
        ILogger<FlexiManufactureClient> logger,
        IFlexiManufactureTemplateService templateService,
        IFefoConsumptionAllocator fefoAllocator,
        IFlexiIngredientRequirementAggregator requirementAggregator,
        IFlexiIngredientStockValidator stockValidator,
        IFlexiLotLoader lotLoader,
        IFlexiManufactureDocumentService documentService,
        IStockItemsMovementClient stockMovementClient,
        TimeProvider timeProvider)
    {
        _bomClient = bomClient;
        _productSetsClient = productSetsClient;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
        _fefoAllocator = fefoAllocator ?? throw new ArgumentNullException(nameof(fefoAllocator));
        _requirementAggregator = requirementAggregator ?? throw new ArgumentNullException(nameof(requirementAggregator));
        _stockValidator = stockValidator ?? throw new ArgumentNullException(nameof(stockValidator));
        _lotLoader = lotLoader ?? throw new ArgumentNullException(nameof(lotLoader));
        _documentService = documentService ?? throw new ArgumentNullException(nameof(documentService));
        _stockMovementClient = stockMovementClient ?? throw new ArgumentNullException(nameof(stockMovementClient));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
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
        var ingredientRequirements = await _requirementAggregator.AggregateAsync(request.Items, cancellationToken);
        if (request.ValidateIngredientStock)
        {
            await _stockValidator.ValidateAsync(ingredientRequirements, cancellationToken);
        }
        var ingredientLots = await _lotLoader.LoadAvailableLotsAsync(ingredientRequirements, cancellationToken);
        var consumptionItems = _fefoAllocator.Allocate(ingredientRequirements, ingredientLots, "");
        var consumptionResult = await _documentService.SubmitConsumptionAsync(request, consumptionItems, cancellationToken);
        var semiProductReceiptDocCode = await _documentService.SubmitProductionAsync(request, consumptionResult.TotalCost, cancellationToken);

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
        var requirementsByProduct = new List<(string ProductCode, Dictionary<string, IngredientRequirement> Requirements)>();

        foreach (var item in request.Items.Where(i => i.Amount > 0))
        {
            // Get ingredient requirements for this specific product
            var ingredientRequirements = await _requirementAggregator.AggregateAsync(
                new List<SubmitManufactureClientItem> { item }, cancellationToken);

            ApplyResidueDistribution(request, item.ProductCode, ingredientRequirements);
            requirementsByProduct.Add((item.ProductCode, ingredientRequirements));
        }

        // Validate the order as a whole: products sharing a material can each fit on stock while
        // their sum does not, and Flexi would then silently issue 0 of that line.
        var totalRequirements = SumRequirements(requirementsByProduct.Select(p => p.Requirements));
        if (request.ValidateIngredientStock)
        {
            await _stockValidator.ValidateAsync(totalRequirements, cancellationToken);
        }

        // Lots are loaded once and drawn down product by product, so two products sharing a
        // material can never allocate the same lot quantity twice.
        var remainingLots = await _lotLoader.LoadAvailableLotsAsync(totalRequirements, cancellationToken);
        var allConsumptionItems = new List<ConsumptionItem>();
        var productCosts = new Dictionary<string, double>();

        foreach (var (productCode, ingredientRequirements) in requirementsByProduct)
        {
            var consumptionItems = _fefoAllocator.Allocate(ingredientRequirements, remainingLots, productCode);
            remainingLots = DeductAllocatedLots(remainingLots, consumptionItems);

            allConsumptionItems.AddRange(consumptionItems);
            productCosts[productCode] = 0; // Will be calculated during consumption
        }

        // Phase 2: Create ONE consume document (per warehouse) with all consumption lines
        var consumptionCodes = await _documentService.SubmitConsolidatedConsumptionAsync(request, allConsumptionItems, productCosts, cancellationToken);

        // Phase 3: Create ONE produce document with all products
        var productReceiptDocCode = await _documentService.SubmitConsolidatedProductionAsync(request, productCosts, cancellationToken);

        // Phase 4: Create discard document for direct semiproduct output (if any)
        string? directOutputDocCode = null;
        if (request.DirectSemiProductOutputAmount > 0
            && !string.IsNullOrEmpty(request.DirectSemiProductOutputCode))
        {
            directOutputDocCode = await _documentService.SubmitDirectSemiProductOutputAsync(
                request, cancellationToken);
        }

        return new SubmitManufactureClientResponse
        {
            ManufactureId = request.ManufactureOrderCode,
            SemiProductIssueForProductDocCode = consumptionCodes.SemiProductIssueCode,
            MaterialIssueForProductDocCode = consumptionCodes.MaterialIssueCode,
            ProductReceiptDocCode = productReceiptDocCode,
            DirectSemiProductOutputDocCode = directOutputDocCode,
        };
    }

    // When ResidueDistribution is set, override the semiproduct ingredient amount with the
    // distribution-adjusted consumption so that all products together consume exactly
    // ActualSemiProductQuantity grams (not the BoM-theoretical amount).
    private static void ApplyResidueDistribution(
        SubmitManufactureClientRequest request,
        string productCode,
        Dictionary<string, IngredientRequirement> ingredientRequirements)
    {
        var distributionEntry = request.ResidueDistribution?.Products
            .FirstOrDefault(p => p.ProductCode == productCode);
        if (distributionEntry == null)
        {
            return;
        }

        var semiProductKey = ingredientRequirements
            .FirstOrDefault(kv => kv.Value.ProductType == ProductType.SemiProduct).Key;
        if (semiProductKey == null)
        {
            return;
        }

        var existing = ingredientRequirements[semiProductKey];
        ingredientRequirements[semiProductKey] = new IngredientRequirement
        {
            ProductCode = existing.ProductCode,
            ProductName = existing.ProductName,
            ProductType = existing.ProductType,
            RequiredAmount = distributionEntry.AdjustedConsumption,
            HasLots = existing.HasLots
        };
    }

    private static Dictionary<string, List<CatalogLot>> DeductAllocatedLots(
        Dictionary<string, List<CatalogLot>> lots,
        IReadOnlyCollection<ConsumptionItem> allocated)
    {
        return lots.ToDictionary(
            kv => kv.Key,
            kv => kv.Value
                .Select(lot => new CatalogLot
                {
                    Id = lot.Id,
                    ProductCode = lot.ProductCode,
                    Lot = lot.Lot,
                    Expiration = lot.Expiration,
                    Amount = lot.Amount - allocated
                        .Where(c => c.ProductCode == kv.Key && c.LotNumber == lot.Lot && c.Expiration == lot.Expiration)
                        .Sum(c => c.Amount),
                })
                .Where(lot => lot.Amount > 0)
                .ToList());
    }

    private static Dictionary<string, IngredientRequirement> SumRequirements(
        IEnumerable<Dictionary<string, IngredientRequirement>> requirementSets)
    {
        return requirementSets
            .SelectMany(set => set.Values)
            .GroupBy(r => r.ProductCode)
            .ToDictionary(
                g => g.Key,
                g => new IngredientRequirement
                {
                    ProductCode = g.Key,
                    ProductName = g.First().ProductName,
                    ProductType = g.First().ProductType,
                    RequiredAmount = g.Sum(r => r.RequiredAmount),
                    HasLots = g.First().HasLots
                });
    }

    public async Task UpdateBoMIngredientAmountAsync(string productCode, string ingredientCode, double newAmount, CancellationToken cancellationToken = default)
    {
        try
        {
            await _bomClient.UpdateIngredientAmountAsync(productCode, ingredientCode, newAmount, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while updating BoM ingredient amount — " +
                "endpoint may be disabled or unsupported on this instance. ProductCode: {ProductCode}, IngredientCode: {IngredientCode}",
                productCode, ingredientCode);
            throw;
        }
    }

    public async Task<ManufactureTemplate?> GetManufactureTemplateAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _templateService.GetManufactureTemplateAsync(id, cancellationToken);
    }

    [Obsolete("Use SetBomItemsOrderAndPhaseAsync. Kept for compatibility.")]
    public async Task SetBomItemsOrderAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order)> items,
        CancellationToken cancellationToken = default)
    {
        await _bomClient.SetItemsOrderAsync(items, cancellationToken);
        _templateService.InvalidateTemplate(productCode);
    }

    public async Task SetBomItemsOrderAndPhaseAsync(
        string productCode,
        IEnumerable<(int BoMItemId, int Order, string? PhaseLabel)> items,
        CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            await _bomClient.UpdateBoMItemAsync(
                item.BoMItemId,
                order: item.Order,
                nameC: item.PhaseLabel ?? string.Empty,
                cancellationToken: cancellationToken);
        }
        _templateService.InvalidateTemplate(productCode);
    }

    public async Task<List<ManufactureTemplate>> FindByIngredientAsync(string ingredientCode, CancellationToken cancellationToken)
    {
        IEnumerable<BoMItemFlexiDto> templates;
        try
        {
            templates = await _bomClient.GetByIngredientAsync(ingredientCode, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotImplemented)
        {
            _logger.LogError(ex,
                "FlexiBee kusovnik returned 501 NotImplemented while fetching BoM by ingredient — " +
                "endpoint may be disabled or unsupported on this instance. IngredientCode: {IngredientCode}",
                ingredientCode);
            throw;
        }

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
}
