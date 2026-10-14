using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Execution;

public class AdExecutionLookupTests
{
    private static AdProposalActionExecution Row(int index, AdActionExecutionState state, DateTime executedAt,
        string platform = "GoogleAds", string account = "acc-1") => new()
        {
            Version = 1,
            ActionIndex = index,
            Platform = platform,
            AccountExternalId = account,
            ActionType = "AddNegativeKeyword",
            TargetLevel = "Campaign",
            TargetExternalId = $"cmp-{index}",
            State = state,
            ExecutedAt = executedAt,
            PlatformResourceId = $"res-{index}",
            PayloadJson = """{"text":"zdarma","matchType":"Exact"}""",
        };

    private static AdProposal NewProposal(ProposalTestHarness h, int actionCount, string platform = "GoogleAds", string account = "acc-1")
    {
        var proposal = AdProposal.Create(new AdProposalDraft(platform, account, "AddNegativeKeyword", "a", AdActorKind.Agent, null, "[]", actionCount, "r"),
            h.Now, TimeSpan.FromHours(72));
        proposal.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: true);
        proposal.StartExecution(h.Now);
        return proposal;
    }

    private static async Task<IReadOnlyList<AdExecutedAction>> Query(ProposalTestHarness h, DateTimeOffset from, DateTimeOffset to) =>
        await new AdExecutionLookup(h.Proposals).GetExecutedActionsAsync(AdPlatform.GoogleAds, "acc-1", from, to, CancellationToken.None);

    private static async Task Save(ProposalTestHarness h, AdProposal proposal)
    {
        h.Proposals.Add(proposal);
        await h.Proposals.SaveChangesAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ReturnsOnlySucceededExecutionsInsideTheWindow()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = NewProposal(h, 3);
        proposal.RecordExecution(Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(10)));
        proposal.RecordExecution(Row(1, AdActionExecutionState.Failed, t.AddMinutes(10)));
        proposal.RecordExecution(Row(2, AdActionExecutionState.Succeeded, t.AddDays(-2)));
        await Save(h, proposal);

        var result = await Query(h, new DateTimeOffset(t, TimeSpan.Zero), new DateTimeOffset(t.AddHours(1), TimeSpan.Zero));

        var executed = result.Should().ContainSingle().Subject;
        executed.Type.Should().Be(AdActionType.AddNegativeKeyword);
        executed.Platform.Should().Be(AdPlatform.GoogleAds);
        executed.AccountExternalId.Should().Be("acc-1");
        executed.TargetLevel.Should().Be(AdEntityLevel.Campaign);
        executed.TargetExternalId.Should().Be("cmp-0");
        executed.PlatformResourceId.Should().Be("res-0");
        executed.IsRevert.Should().BeFalse();
        executed.ExecutedAt.Should().Be(new DateTimeOffset(t.AddMinutes(10), TimeSpan.Zero));
        executed.ExecutedAt.Offset.Should().Be(TimeSpan.Zero);
        executed.Payload.Should().Contain(new KeyValuePair<string, string>("text", "zdarma"));
        long.Parse(executed.ExecutionId).Should().BePositive();
    }

    [Fact]
    public async Task WindowBoundsAreInclusiveAndJustOutsideIsExcluded()
    {
        using var h = new ProposalTestHarness();
        var from = h.Now;
        var to = from.AddHours(1);
        var proposal = NewProposal(h, 4);
        proposal.RecordExecution(Row(0, AdActionExecutionState.Succeeded, from));
        proposal.RecordExecution(Row(1, AdActionExecutionState.Succeeded, to));
        proposal.RecordExecution(Row(2, AdActionExecutionState.Succeeded, from.AddTicks(-1)));
        proposal.RecordExecution(Row(3, AdActionExecutionState.Succeeded, to.AddTicks(1)));
        await Save(h, proposal);

        var result = await Query(h, new DateTimeOffset(from, TimeSpan.Zero), new DateTimeOffset(to, TimeSpan.Zero));

        result.Select(r => r.TargetExternalId).Should().BeEquivalentTo("cmp-0", "cmp-1");
    }

    [Fact]
    public async Task NonUtcWindowBoundsAreNormalisedToUtc()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;   // UTC
        var proposal = NewProposal(h, 2);
        proposal.RecordExecution(Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(30)));
        proposal.RecordExecution(Row(1, AdActionExecutionState.Succeeded, t.AddMinutes(-30)));
        await Save(h, proposal);
        var cest = TimeSpan.FromHours(2);
        var local = DateTime.SpecifyKind(t.AddHours(2), DateTimeKind.Unspecified);

        // t..t+1h expressed at +02:00
        var result = await Query(h, new DateTimeOffset(local, cest), new DateTimeOffset(local.AddHours(1), cest));

        result.Should().ContainSingle().Which.TargetExternalId.Should().Be("cmp-0");
    }

    [Fact]
    public async Task RevertInsideTheWindowIsReturnedAsARevertRow()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = NewProposal(h, 1);
        var execution = Row(0, AdActionExecutionState.Succeeded, t.AddDays(-2));   // executed outside the window
        proposal.RecordExecution(execution);
        execution.MarkReverted(t.AddMinutes(5), "{}");                               // reverted inside it
        await Save(h, proposal);

        var result = await Query(h, new DateTimeOffset(t, TimeSpan.Zero), new DateTimeOffset(t.AddHours(1), TimeSpan.Zero));

        var revert = result.Should().ContainSingle().Subject;
        revert.IsRevert.Should().BeTrue();
        revert.ExecutedAt.Should().Be(new DateTimeOffset(t.AddMinutes(5), TimeSpan.Zero));
    }

    [Fact]
    public async Task ExecutionAndRevertBothInsideTheWindowAreBothReturned()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = NewProposal(h, 1);
        var execution = Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(1));
        proposal.RecordExecution(execution);
        execution.MarkReverted(t.AddMinutes(5), "{}");
        await Save(h, proposal);

        var result = await Query(h, new DateTimeOffset(t, TimeSpan.Zero), new DateTimeOffset(t.AddHours(1), TimeSpan.Zero));

        result.Select(r => r.IsRevert).Should().BeEquivalentTo(new[] { false, true });
    }

    [Fact]
    public async Task FailedRevertIsNotReturned()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var proposal = NewProposal(h, 1);
        var execution = Row(0, AdActionExecutionState.Succeeded, t.AddDays(-2));
        proposal.RecordExecution(execution);
        execution.MarkRevertFailed(t.AddMinutes(5), "boom");
        await Save(h, proposal);

        var result = await Query(h, new DateTimeOffset(t, TimeSpan.Zero), new DateTimeOffset(t.AddHours(1), TimeSpan.Zero));

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task OtherAccountsAreIgnored()
    {
        using var h = new ProposalTestHarness();
        var t = h.Now;
        var otherAccount = NewProposal(h, 1, account: "acc-2");
        otherAccount.RecordExecution(Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(10), account: "acc-2"));
        var otherPlatform = NewProposal(h, 1, platform: "Sklik");
        otherPlatform.RecordExecution(Row(0, AdActionExecutionState.Succeeded, t.AddMinutes(10), platform: "Sklik"));
        await Save(h, otherAccount);
        await Save(h, otherPlatform);

        var result = await Query(h, new DateTimeOffset(t, TimeSpan.Zero), new DateTimeOffset(t.AddHours(1), TimeSpan.Zero));

        result.Should().BeEmpty();
    }
}
