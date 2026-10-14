using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals.Persistence;

[Collection("PostgresIntegration")]
[Trait("Category", "Integration")]
public class MarketingAdsProposalsMigrationIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 7, 8, 0, 0, DateTimeKind.Utc);
    private readonly PostgresSharedContainerFixture _fixture;
    private string _connectionString = null!;

    public MarketingAdsProposalsMigrationIntegrationTests(PostgresSharedContainerFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _connectionString = await _fixture.CreateDatabaseAsync("marketing_ads_c3");
        await using var context = NewContext();
        var migrations = context.Database.GetMigrations().ToList();
        var ours = migrations.Single(m => m.EndsWith("_AddMarketingAdsProposals", StringComparison.Ordinal));
        var previous = migrations[migrations.IndexOf(ours) - 1];
        var script = context.GetService<IMigrator>().GenerateScript(previous, ours);

        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
                "MigrationId" character varying(150) PRIMARY KEY,
                "ProductVersion" character varying(32) NOT NULL);
            """);
        await ExecuteAsync(script);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private ApplicationDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_connectionString).Options);

    private async Task ExecuteAsync(string sql)
    {
        await using var conn = new NpgsqlConnection(_connectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static AdProposal NewProposal() => AdProposal.Create(
        new AdProposalDraft("GoogleAds", "acc-1", "PauseAd", "agent-1", AdActorKind.Agent, null,
            """[{"type":"PauseAd"}]""", 1, "reason"),
        Now, TimeSpan.FromHours(72));

    private async Task<int> InsertProposalWithAuditAsync()
    {
        await using var ctx = NewContext();
        var proposal = NewProposal();
        ctx.AdProposals.Add(proposal);
        ctx.AdAuditEvents.Add(new AdAuditEvent
        {
            Proposal = proposal,
            Version = 1,
            EventType = AdAuditEventType.Submitted,
            ActorPrincipal = "agent-1",
            ActorKind = AdActorKind.Agent,
            Channel = ApprovalChannel.Mcp,
            PayloadJson = "{}",
            OccurredAt = Now,
        });
        await ctx.SaveChangesAsync();
        return proposal.Id;
    }

    [Fact]
    public async Task Seeds_ProposeOnlyEverywhere_DefaultLimits_AndExecutionDisabled()
    {
        await using var ctx = NewContext();

        var autonomy = await ctx.AdAutonomySettings.ToListAsync();
        autonomy.Should().HaveCount(6).And.OnlyContain(a => a.Mode == AdAutonomyMode.ProposeOnly);

        var limits = await ctx.AdLimitSettings.ToListAsync();
        limits.Should().HaveCount(27);
        limits.Single(l => l.Platform == "GoogleAds" && l.ActionType == "PauseAd" && l.Key == "MaxActionsPerProposal").Value.Should().Be(10);
        limits.Single(l => l.Platform == "Sklik" && l.ActionType == "AddNegativeKeyword" && l.Key == "McpMaxActionsPerProposal").Value.Should().Be(20);
        limits.Single(l => l.Platform == "MetaAds" && l.ActionType == "AddNegativeKeyword" && l.Key == "Enabled").Value.Should().Be(0);

        var killSwitch = await ctx.AdKillSwitches.SingleAsync();
        killSwitch.Scope.Should().Be(AdKillSwitch.GlobalScope);
        killSwitch.ExecutionEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData("""UPDATE public."AdAuditEvents" SET "ActorPrincipal" = 'forged'""")]
    [InlineData("""DELETE FROM public."AdAuditEvents" """)]
    [InlineData("""TRUNCATE public."AdAuditEvents" """)]
    [InlineData("""UPDATE public."AdProposalVersions" SET "Reasoning" = 'rewritten'""")]
    [InlineData("""DELETE FROM public."AdProposalVersions" """)]
    public async Task AppendOnlyTables_RejectMutation(string sql)
    {
        await InsertProposalWithAuditAsync();

        var act = () => ExecuteAsync(sql);

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "P0001" && e.MessageText.Contains("append-only"));
    }

    [Fact]
    public async Task Proposal_RoundTripsThroughASecondContext_WithUtcTimestamps()
    {
        var id = await InsertProposalWithAuditAsync();

        await using var verify = NewContext();
        var loaded = await verify.AdProposals.Include(p => p.Versions).SingleAsync(p => p.Id == id);
        loaded.Status.Should().Be(AdProposalStatus.Pending);
        loaded.CreatedAt.Should().Be(Now);
        loaded.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        loaded.Versions.Should().ContainSingle(v => v.Version == 1 && v.ActionsJson.Contains("PauseAd"));
        (await verify.AdAuditEvents.SingleAsync()).ProposalId.Should().Be(id);
    }

    [Fact]
    public async Task Proposal_StaleRevision_ThrowsConcurrencyException()
    {
        var id = await InsertProposalWithAuditAsync();
        await using var first = NewContext();
        await using var second = NewContext();
        var a = await first.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);
        var b = await second.AdProposals.Include(p => p.Versions).Include(p => p.Approvals).SingleAsync(p => p.Id == id);

        a.RecordApproval("chief", ApprovalChannel.Web, Now, isFinal: true);
        await first.SaveChangesAsync();
        b.Reject("specialist", ApprovalChannel.Web, "no", Now);
        var act = () => second.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
