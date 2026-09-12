namespace Anela.Heblo.Application.Features.ProductPricing.Contracts;

/// <summary>
/// How one product's Shoptet and Flexi retail prices relate to each other. See
/// <c>PriceComparisonService.ClassifyRow</c> for the precedence used when a row
/// matches more than one condition (e.g. divergent AND unknown price type).
/// </summary>
public enum PriceDivergenceKind
{
    InAgreement = 0,
    FlexiDiffers = 1,
    MissingInShoptet = 2,
    MissingInFlexi = 3,
    FlexiPriceTypeUnknown = 4,

    /// <summary>
    /// Both sides have a price and Flexi's price type is known, but Flexi's VAT band was not
    /// one the adapter recognises, so the with-VAT figure rests on the read path's 21%
    /// assumption. Distinct from <see cref="FlexiPriceTypeUnknown"/> because the unknown is a
    /// different one — the rate, not the semantics — and an operator fixing it looks at a
    /// different field in Flexi.
    /// </summary>
    FlexiVatRateUnknown = 5,
}
