using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Contracts;

public class GetCatalogDetailRequest : IRequest<GetCatalogDetailResponse>
{
    public string ProductCode { get; set; } = string.Empty;
    public int MonthsBack { get; set; } = 13;
}