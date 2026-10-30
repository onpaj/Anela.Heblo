using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetTopProduct;

public class SetTopProductResponse : BaseResponse
{
    public string ProductCode { get; set; } = string.Empty;

    public bool IsTopProduct { get; set; }

    public SetTopProductResponse() : base() { }

    public SetTopProductResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
