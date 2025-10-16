using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Logistics.UseCases.ReceiveTransportBox;

public class ReceiveTransportBoxResponse : BaseResponse
{
    public int BoxId { get; set; }
    public string? BoxCode { get; set; }

    public ReceiveTransportBoxResponse() : base()
    {
    }

    public ReceiveTransportBoxResponse(ErrorCodes errorCode, Dictionary<string, string>? parameters = null)
        : base(errorCode, parameters)
    {
    }

    public ReceiveTransportBoxResponse(Exception ex) : base(ex)
    {
    }
}