using Anela.Heblo.Adapters.GoogleAnalytics.Sync;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.Ga4;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Adapters.GoogleAnalytics.Tests;

public class AdapterRegistrationTests
{
    private static IServiceCollection Register(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddGoogleAnalyticsAdapter(configuration, new StubEnvironment());
        return services;
    }

    private static Dictionary<string, string?> Configured() => new()
    {
        ["GoogleAnalytics:PropertyId"] = "392098710",
        ["GoogleAnalytics:CredentialsJson"] = "{\"type\":\"service_account\"}",
        ["ConnectionStrings:Testing"] = "Host=localhost;Database=heblo;Username=postgres",
    };

    [Fact]
    public void registers_nothing_at_all_when_the_property_id_is_missing()
    {
        var settings = Configured();
        settings["GoogleAnalytics:PropertyId"] = "";

        Register(settings).Should().BeEmpty("an unconfigured environment must stay completely inert");
    }

    [Fact]
    public void registers_nothing_at_all_when_the_credentials_are_missing()
    {
        var settings = Configured();
        settings["GoogleAnalytics:CredentialsJson"] = "";

        Register(settings).Should().BeEmpty();
    }

    [Fact]
    public void registers_nothing_when_there_is_no_database_to_write_to()
    {
        var settings = Configured();
        settings["ConnectionStrings:Testing"] = "InMemory";

        Register(settings).Should().BeEmpty("ga4_agg needs a real Postgres schema");
    }

    [Fact]
    public void registers_the_job_under_both_the_interface_and_its_concrete_type()
    {
        // Discovery needs the interface. The concrete binding is not required for Hangfire to
        // activate the job, but it mirrors what AddRecurringJobs() does for Application-assembly
        // jobs, so the container owns the instance's lifetime. Pinned so it is not dropped as
        // "redundant".
        var services = Register(Configured());

        services.Should().Contain(d => d.ServiceType == typeof(IRecurringJob) && d.ImplementationType == typeof(Ga4SyncJob));
        services.Should().Contain(d => d.ServiceType == typeof(Ga4SyncJob));
    }

    [Fact]
    public void registers_every_entity_sync_service_exactly_once()
    {
        var services = Register(Configured());

        var implementations = services
            .Where(d => d.ServiceType == typeof(IGa4EntitySyncService))
            .Select(d => d.ImplementationType)
            .ToList();

        implementations.Should().BeEquivalentTo(new[]
        {
            typeof(TrafficMonthlySyncService),
            typeof(TrafficTotalSyncService),
            typeof(TrafficSyncService),
            typeof(ConversionsSyncService),
            typeof(LandingPageSyncService),
            typeof(PageSyncService),
        });
    }

    [Fact]
    public void points_the_ga4_dbcontext_at_the_main_heblo_connection_string()
    {
        // ga4_agg lives inside Heblo_V3 (ADR-007), not in a database of its own, so there is no
        // separate connection string to configure or forget.
        Register(Configured()).Should().Contain(d => d.ServiceType == typeof(Ga4DbContext));
    }

    private sealed class StubEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
