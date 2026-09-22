using Anela.Heblo.Adapters.Flexi.Analytics;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Analytics;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

/// <summary>
/// The analytics stack is registered only when AnalyticsDatabase:ConnectionString is non-empty.
/// That switch is why the whole thing sat dormant from May to September 2026: the key was "" in
/// every environment and nobody noticed the job was simply absent from Hangfire.
/// </summary>
public class FlexiAnalyticsRegistrationTests
{
    private static IConfiguration Configuration(string? analyticsConnectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["FlexiBeeSettings:Server"] = "https://example.flexibee.eu",
                ["FlexiBeeSettings:Login"] = "test",
                ["FlexiBeeSettings:Password"] = "test",
                ["FlexiBeeSettings:Company"] = "test",
                ["AnalyticsDatabase:ConnectionString"] = analyticsConnectionString,
            })
            .Build();

    [Fact]
    public void AddFlexiAdapter_WithAnAnalyticsConnectionString_RegistersTheNightlySyncJob()
    {
        var services = new ServiceCollection();

        services.AddFlexiAdapter(Configuration("Host=localhost;Database=Heblo_V3;Username=u;Password=p"));

        services.Should().ContainSingle(d =>
            d.ServiceType == typeof(IRecurringJob) && d.ImplementationType == typeof(FlexiAnalyticsSyncJob));
        services.Should().Contain(d => d.ServiceType == typeof(IFlexiAnalyticsSyncService));
        services.Should().Contain(d => d.ServiceType == typeof(AnalyticsDbContext));
    }

    [Fact]
    public void AddFlexiAdapter_WithoutAnAnalyticsConnectionString_LeavesTheStackInert()
    {
        var services = new ServiceCollection();

        services.AddFlexiAdapter(Configuration(""));

        services.Should().NotContain(d => d.ServiceType == typeof(IRecurringJob));
        services.Should().NotContain(d => d.ServiceType == typeof(AnalyticsDbContext));
    }

    [Fact]
    public void JobMetadata_MatchesTheNameHangfireRegistersUnder()
    {
        // RecurringJobDiscoveryService keys both RecurringJobConfigurations and Hangfire's
        // recurring-job:* set on Metadata.JobName, so this string is the contract.
        var job = new FlexiAnalyticsSyncJob(
            Moq.Mock.Of<IFlexiAnalyticsSyncService>(),
            Microsoft.Extensions.Options.Options.Create(new FlexiAnalyticsSyncOptions()),
            Moq.Mock.Of<Microsoft.Extensions.Logging.ILogger<FlexiAnalyticsSyncJob>>());

        job.Metadata.JobName.Should().Be("flexi-analytics-sync");
        job.Metadata.CronExpression.Should().Be("0 3 * * *");
        job.Metadata.TimeZoneId.Should().Be("Europe/Prague");
        job.Metadata.DefaultIsEnabled.Should().BeTrue();
    }

    [Fact]
    public void AddFlexiAdapter_ExposesTheLedgerSyncAsBothAnEntitySyncAndTheBackfillEntryPoint()
    {
        // Both resolve to the same LedgerSyncService registration, so the nightly job and the
        // one-off historical load cannot drift apart.
        var services = new ServiceCollection();

        services.AddFlexiAdapter(Configuration("Host=localhost;Database=Heblo_V3;Username=u;Password=p"));

        services.Should().Contain(d => d.ServiceType == typeof(ILedgerBackfillService));
        services.Where(d => d.ServiceType == typeof(IEntitySyncService)).Should().HaveCount(4);
    }
}
