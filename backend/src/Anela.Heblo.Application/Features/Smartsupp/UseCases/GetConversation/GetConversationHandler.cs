using Anela.Heblo.Application.Features.Smartsupp.Contracts;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Smartsupp;
using MediatR;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GetConversation;

public class GetConversationHandler : IRequestHandler<GetConversationRequest, GetConversationResponse>
{
    private readonly ISmartsuppRepository _repository;

    public GetConversationHandler(ISmartsuppRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetConversationResponse> Handle(GetConversationRequest request, CancellationToken cancellationToken)
    {
        var conversation = await _repository.GetConversationAsync(request.Id, cancellationToken);

        if (conversation is null)
            return new GetConversationResponse(ErrorCodes.SmartsuppConversationNotFound);

        return new GetConversationResponse
        {
            Conversation = MapConversationDto(conversation),
            Messages = conversation.Messages.Select(MapMessageDto).ToList(),
        };
    }

    private static ConversationDto MapConversationDto(SmartsuppConversation c) =>
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

    private static MessageDto MapMessageDto(SmartsuppMessage m) =>
        new()
        {
            Id = m.Id,
            AuthorType = m.AuthorType.ToString(),
            AuthorName = m.AuthorName,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
        };
}
