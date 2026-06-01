using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsRequest : IRequest<PrintMaterialContainerLabelsResponse>
{
    public int Count { get; set; }
}
