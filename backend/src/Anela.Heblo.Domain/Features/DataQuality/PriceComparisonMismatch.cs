namespace Anela.Heblo.Domain.Features.DataQuality;

public enum PriceComparisonMismatch
{
    Unknown = 0,
    PriceDiffers = 1,
    MissingInFlexi = 2,
    FlexiPriceTypeUnknown = 3,

    /// <summary>
    /// Not a mismatch — Shoptet is the retail source of truth, so a product it has never
    /// priced has no comparison to fail. Recorded as an informational drift row purely so
    /// the count cannot be invisible: with it hidden, a truncated or empty Shoptet read
    /// classifies every row this way, produces zero mismatches, and renders a green
    /// "vše OK" tile over a comparison that compared nothing.
    /// </summary>
    MissingInShoptet = 4,

    /// <summary>
    /// Flexi's VAT band was not one the adapter recognises, so its with-VAT price was derived
    /// from the read path's 21% assumption. A mismatch like FlexiPriceTypeUnknown: the numbers
    /// may happen to agree, but nothing here knows whether they really do.
    /// </summary>
    FlexiVatRateUnknown = 5
}
