using Anela.Heblo.Domain.Features.Logistics;
using MediatR;

namespace Anela.Heblo.Application.Features.Packaging.UseCases.GetPackages;

public class GetPackagesRequest : IRequest<GetPackagesResponse>
{
    public string? OrderCode { get; set; }
    public string? CustomerName { get; set; }
    public string? PackageNumber { get; set; }
    public Carriers? Carrier { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public string SortBy { get; set; } = "PackedAt";
    public bool SortDescending { get; set; } = true;
}
