using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesHandler : IRequestHandler<GetPackagesRequest, GetPackagesResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IShippingMethodCatalog _shippingCatalog;

    public GetPackagesHandler(IPackageRepository repo, IShippingMethodCatalog shippingCatalog)
    {
        _repo = repo;
        _shippingCatalog = shippingCatalog;
    }

    public async Task<GetPackagesResponse> Handle(GetPackagesRequest request, CancellationToken cancellationToken)
    {
        var shippingProviderCodes = request.Carrier.HasValue
            ? _shippingCatalog.GetShippingCodesForCarrier(request.Carrier.Value)
            : null;

        var (items, total) = await _repo.GetPaginatedAsync(
            request.OrderCode,
            request.CustomerName,
            request.PackageNumber,
            shippingProviderCodes,
            request.FromDate,
            request.ToDate,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        return new GetPackagesResponse
        {
            Items = items.Select(p => new PackageDto
            {
                Id = p.Id,
                OrderCode = p.OrderCode,
                CustomerName = p.CustomerName,
                PackageNumber = p.PackageNumber,
                TrackingNumber = p.TrackingNumber,
                ShippingProviderCode = p.ShippingProviderCode,
                ShippingProviderName = _shippingCatalog.ResolveCarrier(p.ShippingProviderCode)?.GetDisplayName()
                    ?? p.ShippingProviderName,
                PackedAt = p.PackedAt,
                PackedBy = p.PackedBy,
            }).ToList(),
            TotalCount = total,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
