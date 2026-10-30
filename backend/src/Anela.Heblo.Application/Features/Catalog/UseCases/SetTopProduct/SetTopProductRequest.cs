using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetTopProduct;

public class SetTopProductRequest : IRequest<SetTopProductResponse>
{
    public string ProductCode { get; set; } = string.Empty;

    public bool IsTopProduct { get; set; }
}
