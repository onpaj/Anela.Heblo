using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.CloseConversation;

public class CloseConversationResponse : BaseResponse
{
    public CloseConversationResponse() { }
    public CloseConversationResponse(ErrorCodes errorCode) : base(errorCode) { }
}
