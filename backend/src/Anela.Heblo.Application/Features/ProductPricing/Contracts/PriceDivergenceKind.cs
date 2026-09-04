namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

/// <summary>
/// How one product's Shoptet and Flexi retail prices relate to each other. See
/// <c>PriceDivergenceReportService.ClassifyRow</c> for the precedence used when a row
/// matches more than one condition (e.g. divergent AND unknown price type).
/// </summary>
public enum PriceDivergenceKind
{
    InAgreement = 0,
    FlexiDiffers = 1,
    MissingInShoptet = 2,
    MissingInFlexi = 3,
    FlexiPriceTypeUnknown = 4,
}
