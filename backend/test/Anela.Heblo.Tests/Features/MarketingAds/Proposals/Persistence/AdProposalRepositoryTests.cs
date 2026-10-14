using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

public class AdProposalRepositoryTests : IDisposable
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private readonly DbContextOptions<ApplicationDbContext> _options =
        new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
    private readonly ApplicationDbContext _context;

    public AdProposalRepositoryTests()
    {
        _context = new ApplicationDbContext(_options);
        _context.Database.EnsureCreated(); // applies HasData seeds on InMemory
    }

    public void Dispose() => _context.Dispose();

    private static AdProposal Proposal(string creator = "agent-1", string platform = "GoogleAds") => AdProposal.Create(
        new AdProposalDraft(platform, "acc-1", "PauseAd", creator, AdActorKind.Agent, null, "[]", 1, "r"),
        Now, TimeSpan.FromHours(72));

    private static AdProposalActionExecution Execution(AdActionExecutionState state, DateTime at, int actionIndex) => new()
    {
        Version = 1, ActionIndex = actionIndex, Platform = "GoogleAds", AccountExternalId = "acc-1", ActionType = "PauseAd",
        TargetLevel = "Ad", TargetExternalId = $"ad-{actionIndex}", State = state, ExecutedAt = at,
    };

    [Fact]
    public async Task Add_ThenGetForUpdate_ReturnsChildrenFromASecondContext()
    {
        var repo = new AdProposalRepository(_context);
        var proposal = Proposal();
        repo.Add(proposal);
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var loaded = await new AdProposalRepository(verify).GetForUpdateAsync(proposal.Id, CancellationToken.None);
        loaded!.Versions.Should().ContainSingle();
        loaded.GetCurrentVersion().Reasoning.Should().Be("r");
    }

    [Fact]
    public async Task ListAsync_FiltersByCreatorAndPages()
    {
        var repo = new AdProposalRepository(_context);
        repo.Add(Proposal("agent-1"));
        repo.Add(Proposal("agent-2"));
        repo.Add(Proposal("agent-1"));
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var (items, total) = await new AdProposalRepository(verify)
            .ListAsync(new AdProposalQuery { CreatedByPrincipal = "agent-1", PageSize = 1 }, CancellationToken.None);

        total.Should().Be(2);
        items.Should().ContainSingle().Which.CreatedByPrincipal.Should().Be("agent-1");
    }

    [Fact]
    public async Task CountSucceededActionsSince_CountsOnlySucceededInsideTheWindow()
    {
        var repo = new AdProposalRepository(_context);
        var proposal = Proposal();
        proposal.RecordExecution(Execution(AdActionExecutionState.Succeeded, Now.AddDays(-1), 0));
        proposal.RecordExecution(Execution(AdActionExecutionState.Failed, Now.AddDays(-1), 1));
        proposal.RecordExecution(Execution(AdActionExecutionState.Succeeded, Now.AddDays(-8), 2));
        repo.Add(proposal);
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var count = await new AdProposalRepository(verify)
            .CountSucceededActionsSinceAsync("GoogleAds", "acc-1", "PauseAd", Now.AddDays(-7), CancellationToken.None);

        count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingExpiredIds_ReturnsOnlyPendingPastExpiry()
    {
        var repo = new AdProposalRepository(_context);
        var expired = Proposal();
        var approved = Proposal();
        approved.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        repo.Add(expired);
        repo.Add(approved);
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var ids = await new AdProposalRepository(verify).GetPendingExpiredIdsAsync(Now.AddHours(73), CancellationToken.None);

        ids.Should().Equal(expired.Id);
    }

    [Fact]
    public async Task GetLimitSettings_IsNotTracked_SoAuditBeforeValuesCannotAlias()
    {
        var governance = new AdGovernanceRepository(_context);

        var limits = await governance.GetLimitSettingsAsync("GoogleAds", "PauseAd", CancellationToken.None);

        limits.Should().HaveCount(5);
        _context.ChangeTracker.Entries<AdLimitSetting>().Should().BeEmpty();
    }

    [Fact]
    public async Task AuditLog_ListAsync_FiltersByProposalNewestFirst()
    {
        var repo = new AdProposalRepository(_context);
        var audit = new AdAuditLog(_context);
        var proposal = Proposal();
        repo.Add(proposal);
        audit.Append(new AdAuditEvent { Proposal = proposal, Version = 1, EventType = AdAuditEventType.Submitted, ActorPrincipal = "a", ActorKind = AdActorKind.Agent, Channel = ApprovalChannel.Mcp, OccurredAt = Now });
        audit.Append(new AdAuditEvent { Proposal = proposal, Version = 1, EventType = AdAuditEventType.Approved, ActorPrincipal = "c", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = Now.AddMinutes(1) });
        audit.Append(new AdAuditEvent { EventType = AdAuditEventType.KillSwitchChanged, ActorPrincipal = "admin", ActorKind = AdActorKind.Human, Channel = ApprovalChannel.Web, OccurredAt = Now });
        await repo.SaveChangesAsync(CancellationToken.None);

        await using var verify = new ApplicationDbContext(_options);
        var (items, total) = await new AdAuditLog(verify).ListAsync(new AdAuditQuery { ProposalId = proposal.Id }, CancellationToken.None);

        total.Should().Be(2);
        items.Select(e => e.EventType).Should().Equal(AdAuditEventType.Approved, AdAuditEventType.Submitted);
    }

    [Fact]
    public async Task TrySaveChanges_Succeeds_ReturnsTrue()
    {
        var repo = new AdProposalRepository(_context);
        repo.Add(Proposal());

        var saved = await repo.TrySaveChangesAsync(CancellationToken.None);

        saved.Should().BeTrue();
        await using var verify = new ApplicationDbContext(_options);
        (await verify.AdProposals.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task TrySaveChanges_OnConcurrentChange_ReturnsFalseAndDiscardsTrackedChanges()
    {
        var repo = new AdProposalRepository(_context);
        var proposal = Proposal();
        repo.Add(proposal);
        await repo.SaveChangesAsync(CancellationToken.None);
        await using (var other = new ApplicationDbContext(_options))
        {
            var racing = await other.AdProposals.SingleAsync(p => p.Id == proposal.Id);
            racing.Touch(Now.AddMinutes(1));
            await other.SaveChangesAsync();
        }
        proposal.Touch(Now.AddMinutes(2));

        var saved = await repo.TrySaveChangesAsync(CancellationToken.None);

        saved.Should().BeFalse();
        _context.ChangeTracker.Entries().Should().BeEmpty("a failed save must not poison the shared context");
        await using var verify = new ApplicationDbContext(_options);
        (await verify.AdProposals.SingleAsync()).UpdatedAt.Should().Be(Now.AddMinutes(1));
    }
}
