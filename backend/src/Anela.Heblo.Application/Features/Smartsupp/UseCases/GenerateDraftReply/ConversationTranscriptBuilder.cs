using System.Text;
using Anela.Heblo.Domain.Features.Smartsupp;

namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.GenerateDraftReply;

/// <summary>
/// Builds a role-labelled, time-ordered text transcript from Smartsupp messages
/// and derives a retrieval query fallback from the customer's recent messages.
/// </summary>
public static class ConversationTranscriptBuilder
{
    private const int FallbackContactMessageCount = 3;

    public static string Build(IEnumerable<SmartsuppMessage> messages)
    {
        var builder = new StringBuilder();

        foreach (var message in messages.OrderBy(m => m.CreatedAt))
        {
            if (string.IsNullOrWhiteSpace(message.Content) || IsSystemEvent(message))
                continue;

            var label = LabelFor(message.AuthorType);
            if (label is null)
                continue;

            builder.AppendLine($"{label}: {message.Content.Trim()}");
        }

        return builder.ToString().TrimEnd();
    }

    public static string? LastContactMessages(IEnumerable<SmartsuppMessage> messages)
    {
        var recent = messages
            .Where(m => m.AuthorType == SmartsuppMessageAuthorType.Visitor
                        && !IsSystemEvent(m)
                        && !string.IsNullOrWhiteSpace(m.Content))
            .OrderBy(m => m.CreatedAt)
            .TakeLast(FallbackContactMessageCount)
            .Select(m => m.Content!.Trim())
            .ToList();

        return recent.Count == 0 ? null : string.Join("\n", recent);
    }

    // SmartSupp emits page-visit events as AuthorType Visitor / SubType "system";
    // they are not real customer messages and must not feed the retrieval query.
    private static bool IsSystemEvent(SmartsuppMessage message) =>
        message.AuthorType == SmartsuppMessageAuthorType.System
        || string.Equals(message.SubType, "system", StringComparison.OrdinalIgnoreCase);

    private static string? LabelFor(SmartsuppMessageAuthorType type) => type switch
    {
        SmartsuppMessageAuthorType.Visitor => "Zákazník",
        SmartsuppMessageAuthorType.Agent => "Agent",
        SmartsuppMessageAuthorType.Bot => "Bot",
        _ => null,
    };
}
