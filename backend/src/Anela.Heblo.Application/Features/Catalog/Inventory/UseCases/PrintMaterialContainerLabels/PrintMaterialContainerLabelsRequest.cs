using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsRequest : IRequest<PrintMaterialContainerLabelsResponse>
{
    public int Count { get; set; }

    /// <summary>Set by the client to confirm the operator has changed and calibrated the media when switching type.</summary>
    public bool MediaChangeConfirmed { get; set; }
}
