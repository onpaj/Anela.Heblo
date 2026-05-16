using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateLot;

public class CreateLotRequest : IRequest<CreateLotResponse>
{
    public string MaterialCode { get; set; } = null!;
    public string LotCode { get; set; } = null!;
    public DateOnly? Expiration { get; set; }
    public DateOnly ReceivedDate { get; set; }
    public string? Notes { get; set; }
}
