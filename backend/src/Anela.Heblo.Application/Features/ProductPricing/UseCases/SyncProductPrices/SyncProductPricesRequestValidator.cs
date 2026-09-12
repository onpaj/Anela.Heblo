using FluentValidation;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SyncProductPrices;

public class SyncProductPricesRequestValidator : AbstractValidator<SyncProductPricesRequest>
{
    /// <summary>Matches SetProductPriceRequestValidator's ceiling on a single code.</summary>
    private const int MaxProductCodeLength = 50;

    /// <summary>
    /// Deliberately far above the whole priced catalogue, so syncing with no filter applied
    /// still works — this is not a business limit on the selection, only a ceiling on what a
    /// single request may ask the server to deserialize and hash.
    /// </summary>
    private const int MaxProductCodes = 10_000;

    public SyncProductPricesRequestValidator()
    {
        // An empty list would fall through to "every priced product", which is exactly the
        // unscoped read this endpoint exists to avoid — the caller must name its selection.
        RuleFor(r => r.ProductCodes).NotEmpty();

        RuleFor(r => r.ProductCodes.Count)
            .LessThanOrEqualTo(MaxProductCodes)
            .WithName(nameof(SyncProductPricesRequest.ProductCodes));

        RuleForEach(r => r.ProductCodes).NotEmpty().MaximumLength(MaxProductCodeLength);
    }
}
