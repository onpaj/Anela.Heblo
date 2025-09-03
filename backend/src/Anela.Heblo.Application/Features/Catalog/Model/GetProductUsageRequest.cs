using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetProductUsageRequest : IRequest<GetProductUsageResponse>
{
    public string ProductCode { get; set; } = null!;
}