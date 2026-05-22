using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetConversation;

public class GetConversationResponse : BaseResponse
{
    public ConversationDto? Conversation { get; set; }
    public List<MessageDto> Messages { get; set; } = new();
    public Dictionary<string, string> AgentNames { get; set; } = new();

    public GetConversationResponse() { }
    public GetConversationResponse(ErrorCodes errorCode) : base(errorCode) { }
}
