using System.Text.Json;
using System.Text.Json.Serialization;
using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;

/// <summary>Builds audit rows; payloads are camelCase JSON with enums by name.</summary>
public static class AdAuditEvents
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Pass a *tracked* proposal: EF fills ProposalId from the navigation, also for a proposal inserted in the same SaveChanges.</summary>
    public static AdAuditEvent ForProposal(AdProposal proposal, AdAuditEventType type, string actor, AdActorKind actorKind,
        ApprovalChannel channel, object payload, DateTime at) => new()
        {
            Proposal = proposal,
            Version = proposal.CurrentVersion,
            EventType = type,
            ActorPrincipal = actor,
            ActorKind = actorKind,
            Channel = channel,
            PayloadJson = JsonSerializer.Serialize(payload, Options),
            OccurredAt = at,
        };

    public static AdAuditEvent ForSetting(AdAuditEventType type, string actor, ApprovalChannel channel, object payload, DateTime at) => new()
    {
        EventType = type,
        ActorPrincipal = actor,
        ActorKind = AdActorKind.Human,
        Channel = channel,
        PayloadJson = JsonSerializer.Serialize(payload, Options),
        OccurredAt = at,
    };

    /// <summary>
    /// Channel recorded for Submitted/Revised until C4 resolves it from the token: agents talk to Heblo
    /// only over MCP; a person is recorded as Web.
    /// </summary>
    public static ApprovalChannel SubmissionChannel(AdActorKind kind) =>
        kind == AdActorKind.Agent ? ApprovalChannel.Mcp : ApprovalChannel.Web;
}
