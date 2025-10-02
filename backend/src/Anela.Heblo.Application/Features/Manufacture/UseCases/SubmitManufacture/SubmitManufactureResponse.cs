using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureResponse : BaseResponse
{
    public string? ManufactureId { get; set; }

    public SubmitManufactureResponse() : base() { }

    public SubmitManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }

    public SubmitManufactureResponse(Exception exception) : base(exception)
    {
    }
}