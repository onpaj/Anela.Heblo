using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.ReprintMaterialContainerLabel;

public class ReprintMaterialContainerLabelResponse : BaseResponse
{
    public MaterialContainerDto? Container { get; set; }

    /// <summary>True when the print was blocked because the media type changed; the client must confirm and resend.</summary>
    public bool RequiresMediaChangeConfirmation { get; set; }

    public ReprintMaterialContainerLabelResponse() : base() { }

    public ReprintMaterialContainerLabelResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
