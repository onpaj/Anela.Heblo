using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.GetProductUsage;

public class GetProductUsageRequest : IRequest<GetProductUsageResponse>
{
    public string ProductCode { get; set; } = null!;
}