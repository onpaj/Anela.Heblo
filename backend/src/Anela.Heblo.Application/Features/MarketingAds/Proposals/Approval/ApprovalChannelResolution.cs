using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Result of resolving who may approve from where. Class, not record: it may cross the API boundary in C4.</summary>
public sealed class ApprovalChannelResolution
{
    public const string NotAHumanChannel = "Decisions are accepted only from the web UI or MCP.";

    public bool Allowed { get; init; }
    public ApprovalChannel Channel { get; init; }
    public string? DenyReason { get; init; }

    public static ApprovalChannelResolution Allow(ApprovalChannel channel) => new() { Allowed = true, Channel = channel };
    public static ApprovalChannelResolution Deny(string reason) => new() { Allowed = false, DenyReason = reason };

    /// <summary>A human decision may come only from Web or Mcp; System (or an unset channel) is never a human approver.</summary>
    public bool IsHumanChannelAllowed => Allowed && Channel is ApprovalChannel.Web or ApprovalChannel.Mcp;

    /// <summary>Why a human decision is refused: the resolver's reason, or <see cref="NotAHumanChannel"/>.</summary>
    public string HumanChannelDenyReason => Allowed ? NotAHumanChannel : DenyReason ?? NotAHumanChannel;
}
