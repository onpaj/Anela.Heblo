using MediatR;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ReprintMaterialContainerLabel;

public class ReprintMaterialContainerLabelRequest : IRequest<ReprintMaterialContainerLabelResponse>
{
    public string Code { get; set; } = string.Empty;

    /// <summary>Set by the client to confirm the operator has changed and calibrated the media when switching type.</summary>
    public bool MediaChangeConfirmed { get; set; }
}
