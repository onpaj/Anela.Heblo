using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;

public class ListConversationsRequest : IRequest<ListConversationsResponse>
{
    public string Status { get; set; } = "Open";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
