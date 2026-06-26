using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesResponse : BaseResponse
{
    public List<PackageDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }

    public GetPackagesResponse() { }
    public GetPackagesResponse(ErrorCodes errorCode) : base(errorCode) { }
}

public class PackageDto
{
    public int Id { get; set; }
    public string OrderCode { get; set; } = null!;
    public string CustomerName { get; set; } = null!;
    public string PackageNumber { get; set; } = null!;
    public string? TrackingNumber { get; set; }
    public string ShippingProviderCode { get; set; } = null!;
    public string? ShippingProviderName { get; set; }
    public DateTimeOffset PackedAt { get; set; }
    public string? PackedBy { get; set; }
    public Guid? PackedByUserId { get; set; }
}
