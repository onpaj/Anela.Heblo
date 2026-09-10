using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceRequestValidator : AbstractValidator<SetProductPriceRequest>
{
    public SetProductPriceRequestValidator()
    {
        RuleFor(r => r.ProductCode).NotEmpty().MaximumLength(50);

        // Validated against the two-decimal wire representation, not the raw decimal: both
        // adapters serialize with ToString("F2"), so anything below 0.01 (e.g. 0.004) would
        // round down to "0.00" on the wire. Shoptet treats a literal 0 as a genuine free price
        // (not "clear the price") as of 2026-09-14, so that would make the product free on the
        // live shop. An upper bound (guarding a mistyped extra digit) is deferred to Task 7 as
        // a frontend confirmation step — no server-side ceiling here.
        RuleFor(r => r.PriceWithVat).GreaterThanOrEqualTo(0.01m);
    }
}
