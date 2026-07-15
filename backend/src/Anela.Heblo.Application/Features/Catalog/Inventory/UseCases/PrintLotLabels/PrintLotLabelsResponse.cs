using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Catalog.Inventory.UseCases.PrintLotLabels;

public class PrintLotLabelsResponse : BaseResponse
{
    public string LotNumber { get; set; } = string.Empty;
    public string Expiration { get; set; } = string.Empty;
    public int Count { get; set; }

    /// <summary>True when the print was blocked because the media type changed; the client must confirm and resend.</summary>
    public bool RequiresMediaChangeConfirmation { get; set; }

    public PrintLotLabelsResponse() : base() { }

    public PrintLotLabelsResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters) { }
}
