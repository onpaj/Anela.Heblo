using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Model;

public class GetMaterialsForPurchaseRequest : IRequest<GetMaterialsForPurchaseResponse>
{
    public string? SearchTerm { get; set; }
    public int Limit { get; set; } = 50;
}