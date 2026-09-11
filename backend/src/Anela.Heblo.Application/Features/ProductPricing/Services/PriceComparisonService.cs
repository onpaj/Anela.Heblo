using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Services;

/// <summary>
/// Builds the price divergence report: a pure read-and-compare of Shoptet and Flexi.
/// This type deliberately depends on nothing but read methods —
/// <see cref="IEshopPriceListClient.GetPricesWithVatAsync"/>,
/// <see cref="IEshopPriceListClient.GetPriceWithVatAsync"/>, <see cref="IProductPriceErpClient.GetAllAsync"/>
/// and <see cref="ICatalogRepository.GetAllAsync"/> —
/// so no write path (<c>SetPriceWithVatAsync</c>, <c>IErpPriceWriter</c>) is even reachable from here.
/// </summary>
public class PriceComparisonService : IPriceComparisonService
{
    /// <summary>Prices agree when they match to 2 decimals, away-from-zero rounded — the same
    /// convention used for every money value in this codebase.</summary>
    private const int PriceDecimals = 2;

    /// <summary>
    /// Flexi stores <c>cenaZakl</c> excluding VAT and reconstructs the with-VAT price as
    /// <c>cena * (100 + vat) / 100</c> on read, which does not round-trip exactly
    /// (190.00 -> cenaZakl 157.02 -> 189.99). Without this tolerance a large share of the
    /// catalogue would report as divergent by one haléř and the dashboard tile would be
    /// permanently orange. Shoptet stores the with-VAT price directly and gets no tolerance.
    /// </summary>
    private const decimal FlexiRoundTripTolerance = 0.01m;

    /// <summary>
    /// Shoptet pages its price list at 100 items, so the whole list costs one request per 100
    /// products while a selection costs one request per product. Up to this many, reading the
    /// selection one product at a time is the cheaper and noticeably faster call — beyond it,
    /// it stops being, and a sync with no filter applied would turn into one HTTP request per
    /// product in the catalogue. A cost heuristic only: both routes produce identical rows.
    /// </summary>
    private const int MaxProductsReadIndividually = 25;

    /// <summary>Assumption A3: only sellable types carry a retail price.</summary>
    private static readonly ProductType[] PricedProductTypes =
    {
        ProductType.Product, ProductType.Goods, ProductType.Set,
    };

    private readonly ICatalogRepository _catalogRepository;
    private readonly IEshopPriceListClient _eshopClient;
    private readonly IProductPriceErpClient _erpClient;

    public PriceComparisonService(
        ICatalogRepository catalogRepository,
        IEshopPriceListClient eshopClient,
        IProductPriceErpClient erpClient)
    {
        _catalogRepository = catalogRepository;
        _eshopClient = eshopClient;
        _erpClient = erpClient;
    }

    public Task<PriceComparisonResult> BuildReportAsync(CancellationToken ct) =>
        BuildAsync(productCodes: null, forceReload: false, ct);

    public Task<PriceComparisonResult> BuildScopedReportAsync(
        IReadOnlyCollection<string> productCodes, CancellationToken ct) =>
        BuildAsync(productCodes, forceReload: true, ct);

    private async Task<PriceComparisonResult> BuildAsync(
        IReadOnlyCollection<string>? productCodes, bool forceReload, CancellationToken ct)
    {
        var pricedProducts = (await _catalogRepository.GetAllAsync(ct))
            .Where(p => PricedProductTypes.Contains(p.Type))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var inScopeProducts = productCodes is null
            ? pricedProducts
            : RestrictToRequested(pricedProducts, productCodes);

        var shoptetPrices = await ReadShoptetPricesAsync(
            inScopeProducts, isScoped: productCodes is not null, ct);

        var erpPrices = (await _erpClient.GetAllAsync(forceReload, ct))
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = inScopeProducts
            .Select(product => BuildRow(product, shoptetPrices, erpPrices))
            .ToList();

        return new PriceComparisonResult
        {
            Rows = rows,
            Summary = BuildSummary(rows),
        };
    }

    private static List<CatalogAggregate> RestrictToRequested(
        IEnumerable<CatalogAggregate> pricedProducts, IReadOnlyCollection<string> productCodes)
    {
        var requested = new HashSet<string>(productCodes, StringComparer.OrdinalIgnoreCase);
        return pricedProducts.Where(p => requested.Contains(p.ProductCode)).ToList();
    }

    /// <summary>
    /// Reads the Shoptet side by whichever route costs fewer requests for this selection
    /// (see <see cref="MaxProductsReadIndividually"/>). Both routes are keyed the same way, so
    /// no caller can tell which one ran. The whole report is never read per code, however few
    /// priced products the catalogue happens to hold — that path exists for a sync, not a load.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>> ReadShoptetPricesAsync(
        IReadOnlyCollection<CatalogAggregate> inScopeProducts, bool isScoped, CancellationToken ct)
    {
        if (!isScoped || inScopeProducts.Count > MaxProductsReadIndividually)
        {
            return new Dictionary<string, decimal>(
                await _eshopClient.GetPricesWithVatAsync(ct), StringComparer.OrdinalIgnoreCase);
        }

        // Sequential on purpose: this route only runs for a handful of products, and the live
        // shop gains nothing from a burst of parallel reads.
        var prices = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var product in inScopeProducts)
        {
            var price = await _eshopClient.GetPriceWithVatAsync(product.ProductCode, ct);
            if (price is not null)
            {
                prices[product.ProductCode] = price.Value;
            }
        }

        return prices;
    }

    private static PriceDivergenceRowDto BuildRow(
        CatalogAggregate product,
        IReadOnlyDictionary<string, decimal> shoptetPrices,
        IReadOnlyDictionary<string, ProductPriceErp> erpPrices)
    {
        shoptetPrices.TryGetValue(product.ProductCode, out var shoptetPrice);
        var hasShoptetPrice = shoptetPrices.ContainsKey(product.ProductCode);

        erpPrices.TryGetValue(product.ProductCode, out var erp);

        var shoptetPriceWithVat = hasShoptetPrice ? shoptetPrice : (decimal?)null;
        var flexiPriceWithVat = erp?.PriceWithVat;

        var (differenceWithVat, differencePercent) = CalculateDifference(shoptetPriceWithVat, flexiPriceWithVat);

        return new PriceDivergenceRowDto
        {
            ProductCode = product.ProductCode,
            ProductName = product.ProductName,
            ShoptetPriceWithVat = shoptetPriceWithVat,
            FlexiPriceWithVat = flexiPriceWithVat,
            FlexiPriceWithoutVat = erp?.PriceWithoutVat,
            FlexiPriceType = erp?.ErpPriceType,
            DifferenceWithVat = differenceWithVat,
            DifferencePercent = differencePercent,
            Kind = ClassifyRow(shoptetPriceWithVat, erp),
        };
    }

    /// <summary>
    /// Precedence, most to least urgent:
    /// 1. <see cref="PriceDivergenceKind.MissingInShoptet"/> — Shoptet is the retail source of
    ///    truth; a product absent from it has no comparison to make at all.
    /// 2. <see cref="PriceDivergenceKind.MissingInFlexi"/> — Shoptet has a price but the ERP
    ///    read has nothing for this product.
    /// 3. <see cref="PriceDivergenceKind.FlexiPriceTypeUnknown"/> — both sides have a price, but
    ///    Flexi's with-VAT figure was derived from an *assumed* price type (see
    ///    FlexiProductPriceErpClient.MapToProductPrices), so any agreement it happens to show
    ///    cannot be trusted. This is checked, and reported, even when the numbers match —
    ///    that is the whole point of calling it out separately from FlexiDiffers/InAgreement.
    /// 4. <see cref="PriceDivergenceKind.FlexiVatRateUnknown"/> — the price type is known but
    ///    Flexi's VAT band is not, so a "bez DPH" item was grossed up by the read path's 21%
    ///    assumption (see ProductPriceFlexiDto.Vat). Same reasoning as case 3: reported even
    ///    when the numbers match, because an assumed rate can agree by coincidence.
    /// 5. <see cref="PriceDivergenceKind.FlexiDiffers"/> — both known, prices disagree.
    /// 6. <see cref="PriceDivergenceKind.InAgreement"/> — both known, prices match to 2 decimals.
    /// </summary>
    private static PriceDivergenceKind ClassifyRow(decimal? shoptetPriceWithVat, ProductPriceErp? erp)
    {
        if (shoptetPriceWithVat is null)
        {
            return PriceDivergenceKind.MissingInShoptet;
        }

        if (erp is null)
        {
            return PriceDivergenceKind.MissingInFlexi;
        }

        if (erp.ErpPriceType is null)
        {
            return PriceDivergenceKind.FlexiPriceTypeUnknown;
        }

        if (erp.VatRate is null)
        {
            return PriceDivergenceKind.FlexiVatRateUnknown;
        }

        return PricesAgree(shoptetPriceWithVat.Value, erp.PriceWithVat)
            ? PriceDivergenceKind.InAgreement
            : PriceDivergenceKind.FlexiDiffers;
    }

    private static bool PricesAgree(decimal shoptetPriceWithVat, decimal flexiPriceWithVat) =>
        Math.Abs(Math.Round(shoptetPriceWithVat, PriceDecimals, MidpointRounding.AwayFromZero)
               - Math.Round(flexiPriceWithVat, PriceDecimals, MidpointRounding.AwayFromZero))
        <= FlexiRoundTripTolerance;

    private static (decimal? DifferenceWithVat, decimal? DifferencePercent) CalculateDifference(
        decimal? shoptetPriceWithVat, decimal? flexiPriceWithVat)
    {
        if (shoptetPriceWithVat is null || flexiPriceWithVat is null)
        {
            return (null, null);
        }

        var difference = Math.Round(
            flexiPriceWithVat.Value - shoptetPriceWithVat.Value, PriceDecimals, MidpointRounding.AwayFromZero);

        // A zero Shoptet price makes "percent relative to Shoptet" undefined rather than a
        // huge or infinite number — reported as null instead of a misleading figure.
        if (shoptetPriceWithVat.Value == 0m)
        {
            return (difference, null);
        }

        var percent = Math.Round(
            difference / shoptetPriceWithVat.Value * 100m, PriceDecimals, MidpointRounding.AwayFromZero);

        return (difference, percent);
    }

    private static PriceDivergenceSummaryDto BuildSummary(IReadOnlyCollection<PriceDivergenceRowDto> rows) =>
        new()
        {
            TotalInScope = rows.Count,
            InAgreementCount = rows.Count(r => r.Kind == PriceDivergenceKind.InAgreement),
            FlexiDiffersCount = rows.Count(r => r.Kind == PriceDivergenceKind.FlexiDiffers),
            MissingInShoptetCount = rows.Count(r => r.Kind == PriceDivergenceKind.MissingInShoptet),
            MissingInFlexiCount = rows.Count(r => r.Kind == PriceDivergenceKind.MissingInFlexi),
            FlexiPriceTypeUnknownCount = rows.Count(r => r.Kind == PriceDivergenceKind.FlexiPriceTypeUnknown),
            FlexiVatRateUnknownCount = rows.Count(r => r.Kind == PriceDivergenceKind.FlexiVatRateUnknown),
        };
}
