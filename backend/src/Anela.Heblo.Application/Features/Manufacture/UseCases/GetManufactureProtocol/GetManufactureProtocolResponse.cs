using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = string.Empty;

    public GetManufactureProtocolResponse() : base()
    {
    }

    public GetManufactureProtocolResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }
}
