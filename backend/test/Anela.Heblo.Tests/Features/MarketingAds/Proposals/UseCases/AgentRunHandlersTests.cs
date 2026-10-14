using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.FinishAgentRun;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.ListAgentRuns;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.StartAgentRun;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class AgentRunHandlersTests
{
    private static StartAgentRunHandler Start(ProposalTestHarness h) => new(h.AgentRuns, h.CurrentUser.Object, h.Time);
    private static FinishAgentRunHandler Finish(ProposalTestHarness h) => new(h.AgentRuns, h.Proposals, h.CurrentUser.Object, h.Time);

    [Fact]
    public async Task StartThenFinish_RecordsTheRunAndCountsItsProposals()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search", DataScopesRead = new() { "ads.search_terms" } }, CancellationToken.None);
        await using (var ctx = h.NewContext())
        {
            var p = AdProposal.Create(new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, started.AgentRunId, "[]", 1, "r"), h.Now, TimeSpan.FromHours(72));
            ctx.AdProposals.Add(p);
            await ctx.SaveChangesAsync();
        }
        h.Time.Advance(TimeSpan.FromMinutes(3));

        var finished = await Finish(h).Handle(new FinishAgentRunRequest
        {
            AgentRunId = started.AgentRunId,
            Status = AgentRunStatus.Succeeded,
            Summary = "1 proposal",
            DataScopesRead = new() { "ads.search_terms", "ads.daily_facts" },
            InputTokens = 12000,
            OutputTokens = 800,
            CostCzk = 1.25m,
        }, CancellationToken.None);

        finished.Success.Should().BeTrue();
        await using var verify = h.NewContext();
        var run = await verify.AgentRuns.SingleAsync(r => r.Id == started.AgentRunId);
        run.Status.Should().Be(AgentRunStatus.Succeeded);
        run.ProposalsCreated.Should().Be(1);
        run.FinishedAt.Should().Be(h.Now);
        run.CostCzk.Should().Be(1.25m);
        run.InputTokens.Should().Be(12000);
        run.Summary.Should().Be("1 proposal");
        finished.Run.DataScopesRead.Should().Equal("ads.search_terms", "ads.daily_facts");
    }

    [Fact]
    public async Task Start_PersistsRunningRunForThePrincipal()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");

        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "  paid-search  ", DataScopesRead = new() { "a" } }, CancellationToken.None);

        await using var verify = h.NewContext();
        var run = await verify.AgentRuns.SingleAsync(r => r.Id == started.AgentRunId);
        run.AgentName.Should().Be("paid-search");
        run.Principal.Should().Be("agent-1");
        run.Status.Should().Be(AgentRunStatus.Running);
        run.StartedAt.Should().Be(h.Now);
    }

    [Fact]
    public async Task Finish_ByAnotherPrincipal_LooksLikeNotFound()
    {
        using var h = new ProposalTestHarness();
        h.ActAs("agent-1");
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);
        h.ActAs("agent-2");

        var response = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Failed }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
        await using var verify = h.NewContext();
        (await verify.AgentRuns.SingleAsync()).Status.Should().Be(AgentRunStatus.Running);
    }

    [Fact]
    public async Task Finish_Unknown_IsNotFound()
    {
        using var h = new ProposalTestHarness();

        var response = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = Guid.NewGuid(), Status = AgentRunStatus.Failed }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsAgentRunNotFound);
    }

    [Fact]
    public async Task Finish_Twice_IsInvalid()
    {
        using var h = new ProposalTestHarness();
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);
        await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Succeeded }, CancellationToken.None);

        var again = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Failed }, CancellationToken.None);

        again.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
        await using var verify = h.NewContext();
        (await verify.AgentRuns.SingleAsync()).Status.Should().Be(AgentRunStatus.Succeeded);
    }

    [Fact]
    public async Task Finish_AsRunning_IsInvalid()
    {
        using var h = new ProposalTestHarness();
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);

        var response = await Finish(h).Handle(new FinishAgentRunRequest { AgentRunId = started.AgentRunId, Status = AgentRunStatus.Running }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
    }

    [Fact]
    public async Task Finish_WithOutOfRangeValues_IsInvalidAndLeavesTheRunRunning()
    {
        using var h = new ProposalTestHarness();
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);

        var requests = new[]
        {
            new FinishAgentRunRequest { Summary = new string('x', FinishAgentRunRequest.MaxSummaryLength + 1) },
            new FinishAgentRunRequest { InputTokens = -1 },
            new FinishAgentRunRequest { OutputTokens = -1 },
            new FinishAgentRunRequest { CostCzk = -0.01m },
            new FinishAgentRunRequest { DataScopesRead = Enumerable.Repeat("s", StartAgentRunRequest.MaxScopes + 1).ToList() },
        };
        foreach (var request in requests)
        {
            request.AgentRunId = started.AgentRunId;
            request.Status = AgentRunStatus.Succeeded;
            var response = await Finish(h).Handle(request, CancellationToken.None);
            response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
        }

        await using var verify = h.NewContext();
        (await verify.AgentRuns.SingleAsync()).Status.Should().Be(AgentRunStatus.Running);
    }

    [Fact]
    public async Task NulInNameSummaryOrScopes_IsInvalid()
    {
        using var h = new ProposalTestHarness();
        var nulName = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid\0search" }, CancellationToken.None);
        var nulScope = await Start(h).Handle(new StartAgentRunRequest { AgentName = "a", DataScopesRead = new() { "ads\0" } }, CancellationToken.None);
        var started = await Start(h).Handle(new StartAgentRunRequest { AgentName = "paid-search" }, CancellationToken.None);

        var nulSummary = await Finish(h).Handle(new FinishAgentRunRequest
        {
            AgentRunId = started.AgentRunId,
            Status = AgentRunStatus.Succeeded,
            Summary = "done\0",
        }, CancellationToken.None);
        var nulFinishScope = await Finish(h).Handle(new FinishAgentRunRequest
        {
            AgentRunId = started.AgentRunId,
            Status = AgentRunStatus.Succeeded,
            DataScopesRead = new() { "\0" },
        }, CancellationToken.None);

        new[] { nulName.ErrorCode, nulScope.ErrorCode, nulSummary.ErrorCode, nulFinishScope.ErrorCode }
            .Should().OnlyContain(c => c == ErrorCodes.MarketingAdsInvalidAgentRun);
        await using var verify = h.NewContext();
        (await verify.AgentRuns.SingleAsync()).Status.Should().Be(AgentRunStatus.Running);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Start_WithoutName_IsInvalid(string name)
    {
        using var h = new ProposalTestHarness();

        var response = await Start(h).Handle(new StartAgentRunRequest { AgentName = name }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidAgentRun);
    }

    [Fact]
    public async Task Start_WithTooLongNameOrBadScopes_IsInvalid()
    {
        using var h = new ProposalTestHarness();

        var longName = await Start(h).Handle(new StartAgentRunRequest { AgentName = new string('n', StartAgentRunRequest.MaxAgentNameLength + 1) }, CancellationToken.None);
        var longScope = await Start(h).Handle(new StartAgentRunRequest { AgentName = "a", DataScopesRead = new() { new string('s', StartAgentRunRequest.MaxScopeLength + 1) } }, CancellationToken.None);
        var controlChar = await Start(h).Handle(new StartAgentRunRequest { AgentName = "a", DataScopesRead = new() { "bad\nscope" } }, CancellationToken.None);

        new[] { longName.ErrorCode, longScope.ErrorCode, controlChar.ErrorCode }.Should().OnlyContain(c => c == ErrorCodes.MarketingAdsInvalidAgentRun);
        await using var verify = h.NewContext();
        (await verify.AgentRuns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task List_ReturnsNewestFirst()
    {
        using var h = new ProposalTestHarness();
        await Start(h).Handle(new StartAgentRunRequest { AgentName = "first" }, CancellationToken.None);
        h.Time.Advance(TimeSpan.FromMinutes(1));
        await Start(h).Handle(new StartAgentRunRequest { AgentName = "second" }, CancellationToken.None);

        var response = await new ListAgentRunsHandler(h.AgentRuns).Handle(new ListAgentRunsRequest(), CancellationToken.None);

        response.Items.Select(r => r.AgentName).Should().Equal("second", "first");
    }

    [Fact]
    public async Task List_SecondPage_ReturnsOlderRuns()
    {
        using var h = new ProposalTestHarness();
        foreach (var name in new[] { "first", "second", "third" })
        {
            await Start(h).Handle(new StartAgentRunRequest { AgentName = name }, CancellationToken.None);
            h.Time.Advance(TimeSpan.FromMinutes(1));
        }

        var response = await new ListAgentRunsHandler(h.AgentRuns).Handle(new ListAgentRunsRequest { Page = 2, PageSize = 2 }, CancellationToken.None);

        response.TotalCount.Should().Be(3);
        response.Items.Select(r => r.AgentName).Should().Equal("first");
    }
}
