using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.ListConversations;

public class ListConversationsHandler : IRequestHandler<ListConversationsRequest, ListConversationsResponse>
{
    private readonly ISmartsuppRepository _repository;

    public ListConversationsHandler(ISmartsuppRepository repository)
    {
        _repository = repository;
    }

    public async Task<ListConversationsResponse> Handle(ListConversationsRequest request, CancellationToken cancellationToken)
    {
        var status = Enum.TryParse<SmartsuppConversationStatus>(request.Status, out var parsed)
            ? parsed
            : SmartsuppConversationStatus.Open;

        var (items, total) = await _repository.ListConversationsAsync(status, request.Page, request.PageSize, cancellationToken);

        return new ListConversationsResponse
        {
            Items = items.Select(MapDto).ToList(),
            Total = total,
            Page = request.Page,
            PageSize = request.PageSize,
        };
    }

    private static ConversationDto MapDto(SmartsuppConversation c) =>
        new()
        {
            Id = c.Id,
            Subject = c.Subject,
            ContactName = c.ContactName,
            ContactEmail = c.ContactEmail,
            ContactAvatarUrl = c.ContactAvatarUrl,
            Status = c.Status.ToString(),
            IsUnread = c.IsUnread,
            LastMessageAt = c.LastMessageAt,
            LastMessagePreview = c.LastMessagePreview,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
        };
}
