using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.ProductPricing;

namespace Anela.Heblo.Application.Features.ProductPricing.Services;

/// <summary>
/// Builds the price divergence report: a pure read-and-compare of Shoptet and Flexi.
/// This type deliberately depends on nothing but read methods —
/// <see cref="IEshopPriceListClient.GetPricesWithVatAsync"/>, <see cref="IProductPriceErpClient.GetAllAsync"/>
/// and <see cref="ICatalogRepository.GetAllAsync"/> —
/// so no write path (<c>SetPriceWithVatAsync</c>, <c>IErpPriceWriter</c>) is even reachable from here.
/// </summary>
public class PriceDivergenceReportService : IPriceDivergenceReportService
{
    /// <summary>Prices agree when they match to 2 decimals, away-from-zero rounded — the same
    /// convention used for every money value in this codebase.</summary>
    private const int PriceDecimals = 2;

    /// <summary>Assumption A3: only sellable types carry a retail price.</summary>
    private static readonly ProductType[] PricedProductTypes =
    {
        ProductType.Product, ProductType.Goods, ProductType.Set,
    };

    private readonly ICatalogRepository _catalogRepository;
    private readonly IEshopPriceListClient _eshopClient;
    private readonly IProductPriceErpClient _erpClient;

    public PriceDivergenceReportService(
        ICatalogRepository catalogRepository,
        IEshopPriceListClient eshopClient,
        IProductPriceErpClient erpClient)
    {
        _catalogRepository = catalogRepository;
        _eshopClient = eshopClient;
        _erpClient = erpClient;
    }

    public async Task<PriceDivergenceReportResult> BuildReportAsync(CancellationToken ct)
    {
        var inScopeProducts = (await _catalogRepository.GetAllAsync(ct))
            .Where(p => PricedProductTypes.Contains(p.Type))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var shoptetPrices = new Dictionary<string, decimal>(
            await _eshopClient.GetPricesWithVatAsync(ct), StringComparer.OrdinalIgnoreCase);

        var erpPrices = (await _erpClient.GetAllAsync(forceReload: false, ct))
            .Where(p => !string.IsNullOrWhiteSpace(p.ProductCode))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var rows = inScopeProducts
            .Select(product => BuildRow(product, shoptetPrices, erpPrices))
            .ToList();

        return new PriceDivergenceReportResult
        {
            Rows = rows,
            Summary = BuildSummary(rows),
        };
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
    /// 4. <see cref="PriceDivergenceKind.FlexiDiffers"/> — both known, prices disagree.
    /// 5. <see cref="PriceDivergenceKind.InAgreement"/> — both known, prices match to 2 decimals.
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

        return PricesAgree(shoptetPriceWithVat.Value, erp.PriceWithVat)
            ? PriceDivergenceKind.InAgreement
            : PriceDivergenceKind.FlexiDiffers;
    }

    private static bool PricesAgree(decimal a, decimal b) =>
        Math.Round(a, PriceDecimals, MidpointRounding.AwayFromZero) ==
        Math.Round(b, PriceDecimals, MidpointRounding.AwayFromZero);

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
        };
}
