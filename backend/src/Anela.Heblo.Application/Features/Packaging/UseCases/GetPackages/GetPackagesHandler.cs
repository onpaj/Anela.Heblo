using Anela.Heblo.Domain.Features.Logistics;
using Anela.Heblo.Domain.Features.Packaging;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesHandler : IRequestHandler<GetPackagesRequest, GetPackagesResponse>
{
    private readonly IPackageRepository _repo;
    private readonly IShippingMethodCatalog _shippingCatalog;
    private readonly ICurrentUserService _currentUserService;

    public GetPackagesHandler(
        IPackageRepository repo,
        IShippingMethodCatalog shippingCatalog,
        ICurrentUserService currentUserService)
    {
        _repo = repo;
        _shippingCatalog = shippingCatalog;
        _currentUserService = currentUserService;
    }

    public async Task<GetPackagesResponse> Handle(GetPackagesRequest request, CancellationToken cancellationToken)
    {
        var shippingProviderCodes = request.Carrier.HasValue
            ? _shippingCatalog.GetShippingCodesForCarrier(request.Carrier.Value)
            : null;

        var shippingProviderNames = request.Carrier.HasValue
            ? _shippingCatalog.GetShippingNamesForCarrier(request.Carrier.Value)
            : null;

        var (items, total) = await _repo.GetPaginatedAsync(
            request.OrderCode,
            request.CustomerName,
            request.PackageNumber,
            shippingProviderCodes,
            shippingProviderNames,
            request.FromDate,
            request.ToDate,
            request.PageNumber,
            request.PageSize,
            request.SortBy,
            request.SortDescending,
            cancellationToken);

        // Who packed each package would let anyone rebuild the per-packer counts, so it is gated too.
        var canViewPackerBreakdown = PackerStatisticsAccess.CanViewBreakdown(_currentUserService);

        return new GetPackagesResponse
        {
            CanViewPackerBreakdown = canViewPackerBreakdown,
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
                PackedBy = canViewPackerBreakdown ? p.PackedBy : null,
                PackedByUserId = canViewPackerBreakdown ? p.PackedByUserId : null,
            }).ToList(),
            TotalCount = total,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
