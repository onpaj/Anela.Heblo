using Anela.Heblo.Application.Features.MarketingAds;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Infrastructure.Jobs;
using Anela.Heblo.Application.Features.MarketingAds.Reporting;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Ads;
using Anela.Heblo.Tests.Features.MarketingAds.TestSupport;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace Anela.Heblo.Tests.Features.MarketingAds;

/// <summary>
/// AddRecurringJobs() registers every IRecurringJob in the Application assembly, and job discovery
/// resolves them all at once — one unresolvable dependency would unschedule every job in Heblo.
/// </summary>
public class MarketingAdsSyncWiringTests
{
    private static ServiceProvider Build(Action<IServiceCollection>? extra = null)
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Mock.Of<IRecurringJobStatusChecker>());
        extra?.Invoke(services);
        services.AddMarketingAdsModule(configuration);
        // what AddRecurringJobs() does for Application-assembly jobs
        services.AddScoped<IRecurringJob, AdsDailySyncJob>();
        services.AddScoped<IRecurringJob, AdsChangeSyncJob>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    [Fact]
    public void Without_ads_database_jobs_resolve_and_sync_is_disabled()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetServices<IRecurringJob>().Should().HaveCount(2);
        scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().Should().BeOfType<DisabledAdsSyncService>();
        scope.ServiceProvider.GetRequiredService<IAdsChangeSyncService>().Should().BeOfType<DisabledAdsSyncService>();
        scope.ServiceProvider.GetRequiredService<IAdsReadRepository>().Should().BeOfType<UnavailableAdsReadRepository>();
    }

    [Fact]
    public void With_ads_database_the_real_services_resolve()
    {
        var name = AdsTestDb.NewName();
        using var provider = Build(s => s.AddDbContext<AdsDbContext>(o => o.UseInMemoryDatabase(name)));
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdsDailySyncService>().Should().BeOfType<AdsDailySyncService>();
        scope.ServiceProvider.GetRequiredService<IAdsChangeSyncService>().Should().BeOfType<AdsChangeSyncService>();
        scope.ServiceProvider.GetRequiredService<IAdExecutionLookup>().Should().BeOfType<NoOpAdExecutionLookup>();
        scope.ServiceProvider.GetRequiredService<IAdsReadRepository>().Should().BeOfType<AdsReadRepository>();
    }

    [Fact]
    public async Task Disabled_service_reports_skipped()
    {
        var report = await new DisabledAdsSyncService().RunAsync(default);

        report.Skipped.Should().BeTrue();
        report.SkipReason.Should().Contain("AdsDatabase");
    }
}
