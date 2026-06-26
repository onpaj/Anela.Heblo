using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateMaterialContainers;

public class CreateMaterialContainersResponse : BaseResponse
{
    public List<MaterialContainerDto> Containers { get; set; } = new();

    public CreateMaterialContainersResponse() : base() { }

    public CreateMaterialContainersResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
