using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public class SendMessageResponse : BaseResponse
{
    public string MessageId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    public SendMessageResponse() { }
    public SendMessageResponse(ErrorCodes errorCode) : base(errorCode) { }
}
