using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    /// <summary>
    /// Sanity ceiling, not a business rule: the priciest thing this shop sells is orders of
    /// magnitude below it, so nothing legitimate is ever refused, while a mistyped extra digit
    /// or a stray script cannot push an absurd retail price into the live shop. The frontend's
    /// large-change confirmation is a separate, softer guard — it asks the operator about a
    /// plausible-but-big change, and it cannot guard a caller that is not the UI.
    /// </summary>
    private const decimal MaxPriceWithVat = 1_000_000m;

    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty().MaximumLength(50);

        // Validated against the two-decimal wire representation, not the raw decimal: both
        // adapters serialize with ToString("F2"), so anything below 0.01 (e.g. 0.004) would
        // round down to "0.00" on the wire. Shoptet treats a literal 0 as a genuine free price
        // (not "clear the price") as of 2026-09-14, so that would make the product free on the
        // live shop.
        RuleFor(r => r.PriceWithVat).GreaterThanOrEqualTo(0.01m).LessThanOrEqualTo(MaxPriceWithVat);
    }
}
