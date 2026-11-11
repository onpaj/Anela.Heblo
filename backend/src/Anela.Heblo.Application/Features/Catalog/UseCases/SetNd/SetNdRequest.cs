using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetNd;

public class SetNdRequest : IRequest<SetNdResponse>
{
    public string ProductCode { get; set; } = string.Empty;

    public bool IsNd { get; set; }
}
