using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.Services;

public class ConfirmSemiProductManufactureResponse : BaseResponse
{
    public string? Message { get; set; }

    public ConfirmSemiProductManufactureResponse() : base() { }

    public ConfirmSemiProductManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }
}