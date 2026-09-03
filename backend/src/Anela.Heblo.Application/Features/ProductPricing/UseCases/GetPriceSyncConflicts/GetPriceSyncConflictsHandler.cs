using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetPriceSyncConflicts;

public class GetPriceSyncConflictsHandler
    : IRequestHandler<GetPriceSyncConflictsRequest, GetPriceSyncConflictsResponse>
{
    private readonly IProductPriceRepository _repository;

    public GetPriceSyncConflictsHandler(IProductPriceRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPriceSyncConflictsResponse> Handle(
        GetPriceSyncConflictsRequest request, CancellationToken cancellationToken)
    {
        var conflicts = await _repository.GetConflictsAsync(cancellationToken);
        var prices = (await _repository.GetAllAsync(cancellationToken))
            .ToDictionary(p => p.ProductCode, StringComparer.OrdinalIgnoreCase);

        return new GetPriceSyncConflictsResponse
        {
            Conflicts = conflicts.Select(state =>
            {
                prices.TryGetValue(state.ProductCode, out var price);

                return new PriceSyncConflictDto
                {
                    ProductCode = state.ProductCode,
                    Target = state.Target,
                    HebloPriceWithVat = price?.PriceWithVat ?? 0m,
                    RemotePriceWithVat = state.RemoteValueAtConflict,
                    ConflictDetectedAt = state.ConflictDetectedAt,
                };
            }).ToList(),
        };
    }
}
