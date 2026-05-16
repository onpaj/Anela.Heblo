using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.CreateEans;

public class CreateEansResponse : BaseResponse
{
    public List<EanDto> Eans { get; set; } = new();

    public CreateEansResponse() : base() { }

    public CreateEansResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
