using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.SetProductPrice;

public class SetProductPriceHandler : IRequestHandler<SetProductPriceRequest, SetProductPriceResponse>
{
    private static readonly PriceSyncTarget[] AllTargets = { PriceSyncTarget.Shoptet, PriceSyncTarget.Flexi };

    private readonly IProductPriceRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SetProductPriceHandler(IProductPriceRepository repository, ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SetProductPriceResponse> Handle(
        SetProductPriceRequest request, CancellationToken cancellationToken)
    {
        var price = await _repository.GetAsync(request.ProductCode, cancellationToken);
        if (price is null)
        {
            return new SetProductPriceResponse(
                ErrorCodes.ProductPriceNotFound,
                new Dictionary<string, string> { ["ProductCode"] = request.ProductCode });
        }

        price.PriceWithVat = request.PriceWithVat;
        price.ModifiedAt = DateTime.UtcNow;
        price.ModifiedBy = _currentUserService.GetCurrentUser().Email;
        await _repository.UpsertAsync(price, cancellationToken);

        // The push itself is the job's work — Flexi's p95 is ~6.7s and must not block a save.
        foreach (var target in AllTargets)
        {
            var state = await _repository.GetSyncStateAsync(request.ProductCode, target, cancellationToken)
                ?? new ProductPriceSyncState { ProductCode = request.ProductCode, Target = target };

            state.Status = PriceSyncStatus.Pending;
            await _repository.UpsertSyncStateAsync(state, cancellationToken);
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new SetProductPriceResponse { PriceWithVat = request.PriceWithVat };
    }
}
