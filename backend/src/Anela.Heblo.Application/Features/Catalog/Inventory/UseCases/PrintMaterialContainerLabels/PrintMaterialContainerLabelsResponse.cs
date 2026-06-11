using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintMaterialContainerLabels;

public class PrintMaterialContainerLabelsResponse : BaseResponse
{
    public List<MaterialContainerDto> Containers { get; set; } = new();

    public PrintMaterialContainerLabelsResponse() : base() { }

    public PrintMaterialContainerLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
