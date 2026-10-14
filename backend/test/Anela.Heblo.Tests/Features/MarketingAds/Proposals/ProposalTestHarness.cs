using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Actions;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Domain.Features.Users;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

/// <summary>
/// Real repositories on an InMemory ApplicationDbContext (HasData seeds via EnsureCreated), the real
/// evaluator, context builder and autonomy service; mocks for identity, channel resolver, enqueuer and
/// the ads read model. <see cref="NewContext"/> opens a second context on the same store for verification.
/// </summary>
internal sealed class ProposalTestHarness : IDisposable
{
    public static readonly DateTimeOffset Start = new(2026, 10, 7, 8, 0, 0, TimeSpan.Zero);

    private readonly DbContextOptions<ApplicationDbContext> _options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;

    public ProposalTestHarness()
    {
        Context = new ApplicationDbContext(_options);
        Context.Database.EnsureCreated();
        Proposals = new AdProposalRepository(Context);
        Governance = new AdGovernanceRepository(Context);
        AgentRuns = new AgentRunRepository(Context);
        Audit = new AdAuditLog(Context);
        ContextBuilder = new AdLimitsContextBuilder(Governance, Proposals, ReadModel.Object, Time);
        Autonomy = new AdAutonomyService(Governance, ContextBuilder, Evaluator, Audit, Time);

        ActAs("chief");
        CurrentUser.Setup(c => c.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)).Returns(true);
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(ApprovalChannel.Web));
        Enqueuer.Setup(e => e.EnqueueExecution(It.IsAny<int>())).Returns("job-1");
        ReadModel.Setup(r => r.IsAccountManagedAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ReadModel.Setup(r => r.GetAdGroupOccupancyAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((AdPlatform _, string _, IReadOnlyCollection<string> ids, CancellationToken _) =>
                ids.ToDictionary(id => id, id => new AdGroupOccupancy($"grp-{id}", 3)));
        ReadModel.Setup(r => r.GetEntityNamesAsync(It.IsAny<AdPlatform>(), It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, string>());
    }

    public FakeTimeProvider Time { get; } = new(Start);
    public ApplicationDbContext Context { get; }
    public AdProposalRepository Proposals { get; }
    public AdGovernanceRepository Governance { get; }
    public AgentRunRepository AgentRuns { get; }
    public AdAuditLog Audit { get; }
    public AdLimitsEvaluator Evaluator { get; } = new();
    public AdLimitsContextBuilder ContextBuilder { get; }
    public AdAutonomyService Autonomy { get; }
    public Mock<ICurrentUserService> CurrentUser { get; } = new();
    public Mock<IApprovalChannelResolver> ChannelResolver { get; } = new();
    public Mock<IAdProposalExecutionEnqueuer> Enqueuer { get; } = new();
    public Mock<IAdsReadModel> ReadModel { get; } = new();
    public IOptions<AdProposalOptions> Options { get; } = Microsoft.Extensions.Options.Options.Create(new AdProposalOptions());
    public DateTime Now => Time.GetUtcNow().UtcDateTime;

    public ApplicationDbContext NewContext() => new(_options);

    public void ActAs(string principal) =>
        CurrentUser.Setup(c => c.GetCurrentUser()).Returns(new CurrentUser(principal, principal, $"{principal}@anela.cz", true));

    public void UseChannel(ApprovalChannel channel) =>
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Allow(channel));

    public void DenyChannel() =>
        ChannelResolver.Setup(r => r.Resolve()).Returns(ApprovalChannelResolution.Deny(DenyAllApprovalChannelResolver.Reason));

    public static AdAction PauseAction(string adId, string account = "acc-1", AdPlatform platform = AdPlatform.GoogleAds) =>
        new(AdActionType.PauseAd, platform, account, AdEntityLevel.Ad, adId, AdActionValues.Enabled, AdActionValues.Paused,
            new Dictionary<string, string>());

    /// <summary>Saves a Pending PauseAd proposal (version 1) and returns its id.</summary>
    public async Task<int> SeedPendingAsync(int actionCount = 1, string creator = "agent-1", AdPlatform platform = AdPlatform.GoogleAds)
    {
        var actions = Enumerable.Range(1, actionCount).Select(i => PauseAction($"ad-{i}", platform: platform)).ToList();
        var proposal = AdProposal.Create(new AdProposalDraft(platform.ToString(), "acc-1", nameof(AdActionType.PauseAd), creator,
            AdActorKind.Agent, null, AdActionJson.Serialize(actions), actions.Count, "reason"), Now, Options.Value.ProposalTtl);
        Proposals.Add(proposal);
        await Proposals.SaveChangesAsync(CancellationToken.None);
        Context.ChangeTracker.Clear();
        return proposal.Id;
    }

    /// <summary>Saves an Approved proposal ready for the execution job.</summary>
    public async Task<int> SeedApprovedAsync(int actionCount = 1, AdPlatform platform = AdPlatform.GoogleAds)
    {
        var id = await SeedPendingAsync(actionCount, platform: platform);
        var proposal = await Proposals.GetForUpdateAsync(id, CancellationToken.None);
        proposal!.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        await Proposals.SaveChangesAsync(CancellationToken.None);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task SetKillSwitchAsync(string scope, bool enabled)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdKillSwitches.FirstOrDefaultAsync(s => s.Scope == scope);
        if (row is null)
        {
            ctx.AdKillSwitches.Add(new AdKillSwitch { Scope = scope, ExecutionEnabled = enabled, UpdatedAt = Now, UpdatedBy = "test" });
        }
        else
        {
            row.ExecutionEnabled = enabled;
        }
        await ctx.SaveChangesAsync();
    }

    public async Task SetAutonomyAsync(string platform, string actionType, AdAutonomyMode mode)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdAutonomySettings.SingleAsync(s => s.Platform == platform && s.ActionType == actionType);
        row.Mode = mode;
        await ctx.SaveChangesAsync();
    }

    public async Task SetLimitAsync(string platform, string actionType, string key, int value)
    {
        await using var ctx = NewContext();
        var row = await ctx.AdLimitSettings.SingleAsync(l => l.Platform == platform && l.ActionType == actionType && l.Key == key);
        row.Value = value;
        await ctx.SaveChangesAsync();
    }

    public void Dispose() => Context.Dispose();
}
