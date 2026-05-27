using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;

public class GetEanByCodeRequest : IRequest<GetEanByCodeResponse>
{
    public string Code { get; set; } = null!;
}
