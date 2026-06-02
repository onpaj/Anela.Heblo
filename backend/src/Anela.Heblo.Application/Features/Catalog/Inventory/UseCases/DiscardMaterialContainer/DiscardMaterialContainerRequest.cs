using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DiscardMaterialContainer;

public class DiscardMaterialContainerRequest : IRequest<DiscardMaterialContainerResponse>
{
    public int Id { get; set; }
}
