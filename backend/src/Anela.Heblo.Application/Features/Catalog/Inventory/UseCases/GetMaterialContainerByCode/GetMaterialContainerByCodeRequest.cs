using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetMaterialContainerByCode;

public class GetMaterialContainerByCodeRequest : IRequest<GetMaterialContainerByCodeResponse>
{
    public string Code { get; set; } = null!;
}
