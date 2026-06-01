using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;

public class ListConversationsResponse : BaseResponse
{
    public List<ConversationDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }

    public ListConversationsResponse() { }
    public ListConversationsResponse(ErrorCodes errorCode) : base(errorCode) { }
}
