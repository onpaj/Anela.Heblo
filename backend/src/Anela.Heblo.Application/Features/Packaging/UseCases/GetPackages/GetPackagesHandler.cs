using Anela.Heblo.Domain.Features.Packaging;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesHandler : IRequestHandler<GetPackagesRequest, GetPackagesResponse>
{
    private readonly IPackageRepository _repo;

    public GetPackagesHandler(IPackageRepository repo) => _repo = repo;

    public async Task<GetPackagesResponse> Handle(GetPackagesRequest request, CancellationToken cancellationToken)
    {
        var (items, total) = await _repo.GetPaginatedAsync(
            request.OrderCode,
            request.CustomerName,
            request.PackageNumber,
            request.ShippingProviderCode,
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
                ShippingProviderName = p.ShippingProviderName,
                PackedAt = p.PackedAt,
                PackedBy = p.PackedBy,
                PackedByUserId = p.PackedByUserId,
            }).ToList(),
            TotalCount = total,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
        };
    }
}
