using Anela.Heblo.Domain.Features.MarketingAds;

namespace Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;

/// <summary>Result of resolving who may approve from where. Class, not record: it may cross the API boundary in C4.</summary>
public sealed class ApprovalChannelResolution
{
    public bool Allowed { get; init; }
    public ApprovalChannel Channel { get; init; }
    public string? DenyReason { get; init; }

    public static ApprovalChannelResolution Allow(ApprovalChannel channel) => new() { Allowed = true, Channel = channel };
    public static ApprovalChannelResolution Deny(string reason) => new() { Allowed = false, DenyReason = reason };
}
