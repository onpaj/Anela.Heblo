using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;

public class PrintLotLabelsRequest : IRequest<PrintLotLabelsResponse>
{
    public string LotNumber { get; set; } = string.Empty;
    public string Expiration { get; set; } = string.Empty;
    public int Count { get; set; }
}
