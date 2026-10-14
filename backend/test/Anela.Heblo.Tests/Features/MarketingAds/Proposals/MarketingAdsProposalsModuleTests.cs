using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Proposals;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Approval;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Autonomy;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.ReadModel;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingAds.Proposals;

/// <summary>
/// Spec 12.1: the proposal layer is wired by AddMarketingAdsModule, the module's composition root.
/// Built with ValidateOnBuild so an unresolvable registration fails here, not at app startup.
/// </summary>
public class MarketingAdsProposalsModuleTests
{
    private static ServiceProvider Build(IDictionary<string, string?>? settings = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        // What the API host always provides.
        services.AddDbContext<ApplicationDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddSingleton(Mock.Of<IBackgroundJobClient>());
        services.AddMarketingAdsModule(configuration);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void AddMarketingAdsModule_WiresTheProposalLayer()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdProposalRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAdGovernanceRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAgentRunRepository>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAdAuditLog>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IAdAutonomyService>().Should().BeOfType<AdAutonomyService>();
        scope.ServiceProvider.GetRequiredService<IAdProposalExecutionEnqueuer>().Should().BeOfType<HangfireAdProposalExecutionEnqueuer>();
    }

    [Fact]
    public void DefaultResolver_DeniesEveryApprovalUntilC4()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var resolution = scope.ServiceProvider.GetRequiredService<IApprovalChannelResolver>().Resolve();

        resolution.Allowed.Should().BeFalse();
        resolution.DenyReason.Should().Be(DenyAllApprovalChannelResolver.Reason);
    }

    [Fact]
    public void WithoutAdsDbContext_ReadModelIsUnavailable()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdsReadModel>().Should().BeOfType<UnavailableAdsReadModel>();
    }

    [Fact]
    public void ProposalOptions_BindFromTheAdsSection()
    {
        using var provider = Build(new Dictionary<string, string?>
        {
            ["Ads:ProposalTtlHours"] = "24",
            ["Ads:StuckApprovedRequeueMinutes"] = "90",
            ["Ads:ExpiryCronExpression"] = "*/5 * * * *",
        });

        var options = provider.GetRequiredService<IOptions<AdProposalOptions>>().Value;

        options.ProposalTtl.Should().Be(TimeSpan.FromHours(24));
        options.StuckApprovedRequeueMinutes.Should().Be(90);
        options.ExpiryCronExpression.Should().Be("*/5 * * * *");
    }

    [Fact]
    public void InvalidProposalOptions_DoNotFailResolution_TheyFailARunViaEnsureValid()
    {
        // D3: a bad setting must fail a run, never app startup or job discovery.
        using var provider = Build(new Dictionary<string, string?> { ["Ads:ProposalTtlHours"] = "0" });

        var options = provider.GetRequiredService<IOptions<AdProposalOptions>>().Value;

        options.Invoking(o => o.EnsureValid()).Should().Throw<InvalidOperationException>();
    }
}
