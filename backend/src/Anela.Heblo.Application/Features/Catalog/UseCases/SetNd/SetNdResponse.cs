using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.UseCases.SetNd;

public class SetNdResponse : BaseResponse
{
    public string ProductCode { get; set; } = string.Empty;

    public bool IsNd { get; set; }

    public SetNdResponse() : base() { }

    public SetNdResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}
