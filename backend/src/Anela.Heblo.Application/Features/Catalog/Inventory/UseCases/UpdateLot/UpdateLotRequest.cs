using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.UpdateLot;

public class UpdateLotRequest : IRequest<UpdateLotResponse>
{
    public int Id { get; set; }
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
}
