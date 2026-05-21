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

        var otherConversations = conversation.ContactId is not null
            ? await _repository.ListConversationsForContactAsync(conversation.ContactId, conversation.Id, cancellationToken)
            : [];

        return new GetConversationResponse
        {
            Conversation = MapConversationDto(conversation, otherConversations),
            Messages = conversation.Messages.Select(MapMessageDto).ToList(),
        };
    }

    private static ConversationDto MapConversationDto(SmartsuppConversation c, List<SmartsuppConversation> otherConversations) =>
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
            Rating = c.Rating,
            RatingText = c.RatingText,
            CloseType = c.CloseType,
            ClosedByAgentId = c.ClosedByAgentId,
            AssignedAgentIds = ParseStringList(c.AssignedAgentIdsJson),
            Channel = c.Channel,
            IsServed = c.IsServed,
            FinishedAt = c.FinishedAt,
            Domain = c.Domain,
            Referer = c.Referer,
            LocationCountry = c.LocationCountry,
            LocationCity = c.LocationCity,
            LocationCode = c.LocationCode,
            Tags = ParseStringList(c.TagsJson),
            ContactPhone = c.Contact?.Phone,
            ContactNote = c.Contact?.Note,
            ContactTags = ParseStringList(c.Contact?.TagsJson),
            ContactProperties = ParseStringDictionary(c.Contact?.PropertiesJson),
            LocationIp = c.LocationIp,
            Variables = ParseStringDictionary(c.VariablesJson),
            OtherConversations = otherConversations.Select(MapConversationSummaryDto).ToList(),
        };

    private static ConversationSummaryDto MapConversationSummaryDto(SmartsuppConversation c) =>
        new()
        {
            Id = c.Id,
            Status = c.Status.ToString(),
            LastMessageAt = c.LastMessageAt,
            LastMessagePreview = c.LastMessagePreview,
            IsUnread = c.IsUnread,
        };

    private static MessageDto MapMessageDto(SmartsuppMessage m) =>
        new()
        {
            Id = m.Id,
            AuthorType = m.AuthorType.ToString(),
            AuthorName = m.AuthorName,
            Content = m.Content,
            CreatedAt = m.CreatedAt,
            AgentId = m.AgentId,
            SubType = m.SubType,
            DeliveryStatus = m.DeliveryStatus,
            DeliveredAt = m.DeliveredAt,
            ResponseTime = m.ResponseTime,
            IsFirstReply = m.IsFirstReply,
            PageUrl = m.PageUrl,
        };

    private static List<string> ParseStringList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<string>();
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        }
        catch (System.Text.Json.JsonException)
        {
            return new List<string>();
        }
    }

    private static Dictionary<string, string> ParseStringDictionary(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.EnumerateObject()
                .ToDictionary(
                    p => p.Name,
                    p => p.Value.ValueKind == System.Text.Json.JsonValueKind.String
                        ? p.Value.GetString() ?? string.Empty
                        : p.Value.GetRawText());
        }
        catch (System.Text.Json.JsonException)
        {
            return new Dictionary<string, string>();
        }
    }
}
