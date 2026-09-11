using Anela.Heblo.Application.Features.ProductPricing.Contracts;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.ProductPricing;
using MediatR;

namespace Anela.Heblo.Application.Features.ProductPricing.UseCases.GetProductPrices;

public class GetProductPricesHandler : IRequestHandler<GetProductPricesRequest, GetProductPricesResponse>
{
    private readonly IProductPriceRepository _repository;
    private readonly ICatalogRepository _catalogRepository;

    public GetProductPricesHandler(
        IProductPriceRepository repository,
        ICatalogRepository catalogRepository)
    {
        _repository = repository;
        _catalogRepository = catalogRepository;
    }

    public async Task<GetProductPricesResponse> Handle(
        GetProductPricesRequest request, CancellationToken cancellationToken)
    {
        var prices = await _repository.GetAllAsync(cancellationToken);
        var productNames = (await _catalogRepository.GetAllAsync(cancellationToken))
            .GroupBy(p => p.ProductCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().ProductName, StringComparer.OrdinalIgnoreCase);
        var shoptetStates = (await _repository.GetSyncStatesAsync(PriceSyncTarget.Shoptet, cancellationToken))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);
        var flexiStates = (await _repository.GetSyncStatesAsync(PriceSyncTarget.Flexi, cancellationToken))
            .ToDictionary(s => s.ProductCode, StringComparer.OrdinalIgnoreCase);

        return new GetProductPricesResponse
        {
            Prices = prices.Select(price =>
            {
                shoptetStates.TryGetValue(price.ProductCode, out var shoptet);
                flexiStates.TryGetValue(price.ProductCode, out var flexi);

                productNames.TryGetValue(price.ProductCode, out var productName);

                return new ProductPriceDto
                {
                    ProductCode = price.ProductCode,
                    ProductName = productName ?? string.Empty,
                    PriceWithVat = price.PriceWithVat,
                    PriceWithoutVat = price.PriceWithoutVat,
                    VatRate = price.VatRate,
                    ModifiedAt = price.ModifiedAt,
                    ModifiedBy = price.ModifiedBy,
                    ShoptetStatus = shoptet?.Status ?? PriceSyncStatus.Pending,
                    ShoptetRemoteValue = shoptet?.RemoteValueAtConflict,
                    FlexiStatus = flexi?.Status ?? PriceSyncStatus.Pending,
                    FlexiRemoteValue = flexi?.RemoteValueAtConflict,
                };
            }).ToList(),
        };
    }
}
