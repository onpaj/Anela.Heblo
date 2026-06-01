using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Manufacture.UseCases.SubmitManufacture;

public class SubmitManufactureResponse : BaseResponse
{
    public string? ManufactureId { get; set; }
    public string? UserMessage { get; set; }
    public string? MaterialIssueForSemiProductDocCode { get; set; }
    public string? SemiProductReceiptDocCode { get; set; }
    public string? SemiProductIssueForProductDocCode { get; set; }
    public string? MaterialIssueForProductDocCode { get; set; }
    public string? ProductReceiptDocCode { get; set; }
    public string? DirectSemiProductOutputDocCode { get; set; }

    public SubmitManufactureResponse() : base() { }

    public SubmitManufactureResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null) : base(errorCode, parameters) { }

    public SubmitManufactureResponse(Exception exception) : base(exception)
    {
    }
}