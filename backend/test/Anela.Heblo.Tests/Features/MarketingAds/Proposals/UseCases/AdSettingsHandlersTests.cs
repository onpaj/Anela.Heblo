using System.Text.Json;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Limits;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.GetAdSettings;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.SetAdKillSwitch;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdAutonomySetting;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.UseCases.UpdateAdLimitSetting;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Authorization;
using Anela.Heblo.Domain.Features.MarketingAds;
using FluentAssertions;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.MarketingAds;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.UseCases;

public class AdSettingsHandlersTests
{
    private static UpdateAdAutonomySettingHandler Autonomy(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);
    private static UpdateAdLimitSettingHandler Limit(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);
    private static SetAdKillSwitchHandler KillSwitch(ProposalTestHarness h) => new(h.Governance, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);

    [Fact]
    public async Task Get_ReturnsSeedsAndExecutionDisabled()
    {
        using var h = new ProposalTestHarness();

        var response = await new GetAdSettingsHandler(h.Governance, h.Proposals).Handle(new GetAdSettingsRequest(), CancellationToken.None);

        response.Autonomy.Should().HaveCount(6).And.OnlyContain(a => a.Mode == AdAutonomyMode.ProposeOnly);
        response.Limits.Should().HaveCount(27);
        response.IsExecutionEnabledGlobally.Should().BeFalse();
    }

    [Fact]
    public async Task Get_ComputesApprovalWithoutEditRate()
    {
        using var h = new ProposalTestHarness();
        await h.SeedApprovedAsync();
        var revised = await h.SeedPendingAsync(creator: "agent-1");
        await using (var ctx = h.NewContext())
        {
            var p = await ctx.AdProposals.Include(x => x.Versions).Include(x => x.Approvals).SingleAsync(x => x.Id == revised);
            p.Revise("[]", 1, "v2", "agent-1", h.Now, TimeSpan.FromHours(72));
            p.RecordApproval("chief", ApprovalChannel.Web, h.Now, isFinal: true);
            await ctx.SaveChangesAsync();
        }

        var response = await new GetAdSettingsHandler(h.Governance, h.Proposals).Handle(new GetAdSettingsRequest(), CancellationToken.None);

        var rate = response.ApprovalRates.Single(r => r.Platform == AdPlatform.GoogleAds && r.ActionType == AdActionType.PauseAd);
        rate.Decided.Should().Be(2);
        rate.ApprovedWithoutEdit.Should().Be(1);
        rate.Rate.Should().Be(0.5m);
    }

    [Fact]
    public async Task UpdateAutonomy_PersistsAndAuditsOldAndNew()
    {
        using var h = new ProposalTestHarness();

        var response = await Autonomy(h).Handle(new UpdateAdAutonomySettingRequest
        {
            Platform = AdPlatform.Sklik,
            ActionType = AdActionType.AddNegativeKeyword,
            Mode = AdAutonomyMode.AutoWithinLimits,
        }, CancellationToken.None);

        response.Success.Should().BeTrue();
        await using var verify = h.NewContext();
        var row = await verify.AdAutonomySettings.SingleAsync(s => s.Platform == "Sklik" && s.ActionType == "AddNegativeKeyword");
        row.Mode.Should().Be(AdAutonomyMode.AutoWithinLimits);
        row.UpdatedBy.Should().Be("chief");
        var audit = await verify.AdAuditEvents.SingleAsync();
        audit.EventType.Should().Be(AdAuditEventType.SettingChanged);
        audit.Channel.Should().Be(ApprovalChannel.Web);
        using var payload = JsonDocument.Parse(audit.PayloadJson);
        payload.RootElement.GetProperty("old").GetString().Should().Be("ProposeOnly");
        payload.RootElement.GetProperty("new").GetString().Should().Be("AutoWithinLimits");
    }

    [Fact]
    public async Task UpdateAutonomy_TwoChanges_AuditChainsOldToNew()
    {
        using var h = new ProposalTestHarness();
        var request = new UpdateAdAutonomySettingRequest { Platform = AdPlatform.Sklik, ActionType = AdActionType.PauseAd, Mode = AdAutonomyMode.AutoWithinLimits };
        await Autonomy(h).Handle(request, CancellationToken.None);
        request.Mode = AdAutonomyMode.Auto;
        await Autonomy(h).Handle(request, CancellationToken.None);

        await using var verify = h.NewContext();
        var events = await verify.AdAuditEvents.OrderBy(e => e.Id).ToListAsync();
        events.Select(e => (JsonDocument.Parse(e.PayloadJson).RootElement.GetProperty("old").GetString(),
                JsonDocument.Parse(e.PayloadJson).RootElement.GetProperty("new").GetString()))
            .Should().Equal(("ProposeOnly", "AutoWithinLimits"), ("AutoWithinLimits", "Auto"));
    }

    [Fact]
    public async Task UpdateAutonomy_UndefinedMode_IsRejected()
    {
        using var h = new ProposalTestHarness();

        var response = await Autonomy(h).Handle(new UpdateAdAutonomySettingRequest
        {
            Platform = AdPlatform.Sklik,
            ActionType = AdActionType.PauseAd,
            Mode = (AdAutonomyMode)99,
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidSetting);
    }

    [Theory]
    [InlineData("NoSuchKey", 1)]
    [InlineData(AdLimitKeys.MaxActionsPerProposal, -1)]
    [InlineData(AdLimitKeys.Enabled, 2)]
    public async Task UpdateLimit_InvalidInput_IsRejected(string key, int value)
    {
        using var h = new ProposalTestHarness();

        var response = await Limit(h).Handle(new UpdateAdLimitSettingRequest
        {
            Platform = AdPlatform.GoogleAds,
            ActionType = AdActionType.PauseAd,
            Key = key,
            Value = value,
        }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsInvalidSetting);
    }

    [Fact]
    public async Task UpdateLimit_ChangesValueAndAudits()
    {
        using var h = new ProposalTestHarness();

        await Limit(h).Handle(new UpdateAdLimitSettingRequest
        {
            Platform = AdPlatform.GoogleAds,
            ActionType = AdActionType.PauseAd,
            Key = AdLimitKeys.McpMaxActionsPerProposal,
            Value = 5,
        }, CancellationToken.None);

        await using var verify = h.NewContext();
        (await verify.AdLimitSettings.SingleAsync(l => l.Platform == "GoogleAds" && l.ActionType == "PauseAd" && l.Key == AdLimitKeys.McpMaxActionsPerProposal)).Value.Should().Be(5);
        using var payload = JsonDocument.Parse((await verify.AdAuditEvents.SingleAsync()).PayloadJson);
        payload.RootElement.GetProperty("old").GetInt32().Should().Be(3);
        payload.RootElement.GetProperty("new").GetInt32().Should().Be(5);
    }

    [Fact]
    public async Task KillSwitch_GlobalOn_AndPlatformOverrideCreated()
    {
        using var h = new ProposalTestHarness();

        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { Platform = AdPlatform.MetaAds, ExecutionEnabled = false }, CancellationToken.None);

        await using var verify = h.NewContext();
        var switches = await verify.AdKillSwitches.ToListAsync();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "GoogleAds").Should().BeTrue();
        AdKillSwitchPolicy.IsExecutionEnabled(switches, "MetaAds").Should().BeFalse();
        (await verify.AdAuditEvents.CountAsync(e => e.EventType == AdAuditEventType.KillSwitchChanged)).Should().Be(2);
    }

    [Fact]
    public async Task KillSwitch_AuditsOldAndNew_ForTheExistingGlobalRow()
    {
        using var h = new ProposalTestHarness();

        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);

        await using var verify = h.NewContext();
        using var payload = JsonDocument.Parse((await verify.AdAuditEvents.SingleAsync()).PayloadJson);
        payload.RootElement.GetProperty("old").GetBoolean().Should().BeFalse();
        payload.RootElement.GetProperty("new").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task KillSwitch_TurningOff_IsAllowedToAnAdminEvenWhenTheChannelIsDenied()
    {
        using var h = new ProposalTestHarness();
        await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        h.Context.ChangeTracker.Clear();
        h.DenyChannel();

        var response = await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = false }, CancellationToken.None);

        response.ErrorCode.Should().BeNull();
        await using var verify = h.NewContext();
        (await verify.AdKillSwitches.SingleAsync()).ExecutionEnabled.Should().BeFalse();
        (await verify.AdAuditEvents.CountAsync(e => e.EventType == AdAuditEventType.KillSwitchChanged)).Should().Be(2);
    }

    [Fact]
    public async Task KillSwitch_TurningOff_WithoutAdminRole_IsForbidden()
    {
        using var h = new ProposalTestHarness();
        h.DenyChannel();
        h.CurrentUser.Setup(c => c.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)).Returns(false);

        var response = await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = false }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        await using var verify = h.NewContext();
        (await verify.AdAuditEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Mutations_FromMcpOrDeniedChannel_AreRejected()
    {
        using var h = new ProposalTestHarness();
        h.UseChannel(ApprovalChannel.Mcp);
        var viaMcp = await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        h.DenyChannel();
        var denied = await Autonomy(h).Handle(new UpdateAdAutonomySettingRequest { Platform = AdPlatform.GoogleAds, ActionType = AdActionType.PauseAd, Mode = AdAutonomyMode.Auto }, CancellationToken.None);

        viaMcp.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        denied.ErrorCode.Should().Be(ErrorCodes.MarketingAdsApprovalChannelDenied);
        await using var verify = h.NewContext();
        (await verify.AdKillSwitches.SingleAsync()).ExecutionEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task Mutations_WithoutAdminRole_AreForbidden()
    {
        using var h = new ProposalTestHarness();
        h.CurrentUser.Setup(c => c.IsInRole(AccessRoles.MarketingAdApprovalsAdmin)).Returns(false);

        var killSwitch = await KillSwitch(h).Handle(new SetAdKillSwitchRequest { ExecutionEnabled = true }, CancellationToken.None);
        var limit = await Limit(h).Handle(new UpdateAdLimitSettingRequest
        {
            Platform = AdPlatform.GoogleAds,
            ActionType = AdActionType.PauseAd,
            Key = AdLimitKeys.MaxActionsPerProposal,
            Value = 1,
        }, CancellationToken.None);

        killSwitch.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        limit.ErrorCode.Should().Be(ErrorCodes.InsufficientPermissions);
        await using var verify = h.NewContext();
        (await verify.AdAuditEvents.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_LosingARaceOnAMissingRow_ReturnsConcurrentChange()
    {
        using var h = new ProposalTestHarness();
        // A second admin creates the missing platform override between this handler's read and its save.
        var governance = new Mock<IAdGovernanceRepository>();
        governance.Setup(g => g.FindKillSwitchAsync("MetaAds", It.IsAny<CancellationToken>())).ReturnsAsync((AdKillSwitch?)null);
        governance.Setup(g => g.TrySaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var handler = new SetAdKillSwitchHandler(governance.Object, h.Audit, h.ChannelResolver.Object, h.CurrentUser.Object, h.Time);

        var response = await handler.Handle(new SetAdKillSwitchRequest { Platform = AdPlatform.MetaAds, ExecutionEnabled = false }, CancellationToken.None);

        response.ErrorCode.Should().Be(ErrorCodes.MarketingAdsConcurrentChange);
    }

    [Fact]
    public async Task Repository_UniqueViolation_IsDiscardedAndReportedAsNotSaved()
    {
        // InMemory reports a duplicate key as ArgumentException; Postgres raises DbUpdateException, so inject that.
        var (context, governance) = CreateRepositoryThrowing(new DbUpdateException("duplicate key",
            new PostgresException("duplicate key value violates unique constraint", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)));
        await using var _ = context;

        var saved = await governance.TrySaveChangesAsync(CancellationToken.None);

        saved.Should().BeFalse();
        context.ChangeTracker.Entries().Should().BeEmpty("a failed save must not poison the shared context");
    }

    [Fact]
    public async Task Repository_OtherFailedUpdate_IsDiscardedAndRethrown()
    {
        var (context, governance) = CreateRepositoryThrowing(new DbUpdateException("value too long for type character varying(64)"));
        await using var _ = context;

        var act = () => governance.TrySaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<DbUpdateException>("only a lost race may be reported as a concurrent change");
        context.ChangeTracker.Entries().Should().BeEmpty("a failed save must not poison the shared context");
    }

    private static (ApplicationDbContext Context, AdGovernanceRepository Governance) CreateRepositoryThrowing(DbUpdateException exception)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(new ThrowingSaveInterceptor(exception))
            .Options;
        var context = new ApplicationDbContext(options);
        var governance = new AdGovernanceRepository(context);
        governance.AddKillSwitch(new AdKillSwitch { Scope = "MetaAds", ExecutionEnabled = false, UpdatedAt = DateTime.UtcNow, UpdatedBy = "chief" });
        new AdAuditLog(context).Append(new AdAuditEvent { EventType = AdAuditEventType.KillSwitchChanged, ActorPrincipal = "chief", PayloadJson = "{}", OccurredAt = DateTime.UtcNow });
        return (context, governance);
    }

    private sealed class ThrowingSaveInterceptor : SaveChangesInterceptor
    {
        private readonly DbUpdateException _exception;

        public ThrowingSaveInterceptor(DbUpdateException exception) => _exception = exception;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            throw _exception;
    }
}
