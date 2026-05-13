using Anela.Heblo.Application.Features.Catalog.Inventory.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.GetEanByCode;

public class GetEanByCodeResponse : BaseResponse
{
    public EanDto Ean { get; set; } = null!;
    public LotDto Lot { get; set; } = null!;

    public GetEanByCodeResponse() : base() { }

    public GetEanByCodeResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
