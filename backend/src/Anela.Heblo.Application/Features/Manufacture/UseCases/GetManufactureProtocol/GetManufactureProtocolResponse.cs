using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;

public class GetManufactureProtocolResponse : BaseResponse
{
    public byte[] PdfBytes { get; set; } = Array.Empty<byte>();
    public string FileName { get; set; } = null!;
}
