using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetLot;

public class GetLotRequest : IRequest<GetLotResponse>
{
    public int Id { get; set; }
}
