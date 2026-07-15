using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsResponse : BaseResponse
{
    public List<MaterialContainerDto> Containers { get; set; } = new();

    /// <summary>True when the print was blocked because the media type changed; the client must confirm and resend.</summary>
    public bool RequiresMediaChangeConfirmation { get; set; }

    public PrintMaterialContainerLabelsResponse() : base() { }

    public PrintMaterialContainerLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
