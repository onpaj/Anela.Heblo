using Anela.Heblo.Application.Features.MarketingAds.Proposals.Audit;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

public class AdAuditEventsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(AdActorKind.Agent, ApprovalChannel.Mcp)]
    [InlineData(AdActorKind.Human, ApprovalChannel.Web)]
    public void SubmissionChannel_AgentsAreMcp_PeopleAreWeb(AdActorKind kind, ApprovalChannel expected) =>
        AdAuditEvents.SubmissionChannel(kind).Should().Be(expected);

    [Fact]
    public void ForProposal_LinksTheProposalAtItsCurrentVersion_AndWritesCamelCaseEnumNames()
    {
        var proposal = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null, "[]", 1, "r"),
            Now, TimeSpan.FromHours(72));

        var e = AdAuditEvents.ForProposal(proposal, AdAuditEventType.Submitted, "agent-1", AdActorKind.Agent, ApprovalChannel.Mcp,
            new { ActionCount = 1, Channel = ApprovalChannel.Mcp }, Now);

        e.Proposal.Should().BeSameAs(proposal);
        e.Version.Should().Be(1);
        e.EventType.Should().Be(AdAuditEventType.Submitted);
        e.OccurredAt.Should().Be(Now);
        e.PayloadJson.Should().Be("{\"actionCount\":1,\"channel\":\"Mcp\"}");
    }

    [Fact]
    public void ForSetting_HasNoProposalAndIsHuman()
    {
        var e = AdAuditEvents.ForSetting(AdAuditEventType.SettingChanged, "chief", ApprovalChannel.Web, new { Key = "Enabled" }, Now);

        e.Proposal.Should().BeNull();
        e.ProposalId.Should().BeNull();
        e.Version.Should().BeNull();
        e.ActorKind.Should().Be(AdActorKind.Human);
        e.PayloadJson.Should().Be("{\"key\":\"Enabled\"}");
    }
}
