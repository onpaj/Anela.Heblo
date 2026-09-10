namespace Anela.Heblo.Domain.Features.DataQuality;

public enum PriceComparisonMismatch
{
    Unknown = 0,
    PriceDiffers = 1,
    MissingInFlexi = 2,
    FlexiPriceTypeUnknown = 3
}
