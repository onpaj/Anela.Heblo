using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.DeleteLot;

public class DeleteLotRequest : IRequest<DeleteLotResponse>
{
    public int Id { get; set; }
}
