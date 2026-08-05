using System.Diagnostics.Metrics;
using Anela.Heblo.Persistence.Analytics;
using Anela.Heblo.Persistence.Infrastructure.Resilience;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Polly.Timeout;

namespace Anela.Heblo.Adapters.Flexi.Tests.Analytics;

public class AnalyticsPersistenceModuleTests
{
    [Fact]
    public async Task AnalyticsResiliencePipeline_IsIsolatedFromApplicationDbContextsSharedPipeline()
    {
        // Regression for the /automerge REJECT on PR #3779: Database:Resilience:TotalTimeBudget was
        // cut 10s->3s for request-serving paths, but AnalyticsPersistenceModule used to resolve the
        // same unkeyed, config-bound IDbResiliencePipelineProvider singleton that ApplicationDbContext
        // uses. AnalyticsDbContext backs LedgerSyncService.UpsertBatchAsync, which SaveChangesAsync's
        // up to 500 rows in one call and can legitimately run past a 3s-per-attempt ceiling. The
        // analytics pipeline must resolve its own keyed provider, unaffected by that tight budget.
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(new TestMeterFactory());
        services.AddSingleton<DbResilienceMetrics>();

        // Simulates PersistenceModule's config-bound pipeline for ApplicationDbContext, tuned to a
        // request-path budget far too tight for a batch upsert.
        services.AddSingleton<IDbResiliencePipelineProvider>(sp =>
            new DbResiliencePipelineProvider(
                Options.Create(new DbResilienceOptions { TotalTimeBudget = TimeSpan.FromMilliseconds(300) }),
                sp.GetRequiredService<DbResilienceMetrics>(),
                NullLogger<DbResiliencePipelineProvider>.Instance));

        services.AddAnalyticsPersistenceServices(
            "Host=localhost;Database=test;Username=test;Password=test",
            maxPoolSize: 5);

        await using var provider = services.BuildServiceProvider();

        var analyticsPipeline = provider.GetRequiredKeyedService<IDbResiliencePipelineProvider>("analytics");
        var requestPathPipeline = provider.GetRequiredService<IDbResiliencePipelineProvider>();

        analyticsPipeline.Should().NotBeSameAs(requestPathPipeline);

        // An operation that legitimately runs longer than the request path's 300ms budget must
        // still succeed on the analytics pipeline, which keeps DbResilienceOptions' un-tuned
        // (10s) default rather than inheriting the request path's tighter setting.
        var result = await analyticsPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });

        result.Should().Be(42);

        // Sanity check that the request-path pipeline really does enforce its own tight budget,
        // i.e. the two pipelines are not accidentally sharing configuration some other way.
        var act = async () => await requestPathPipeline.Pipeline.ExecuteAsync(async ct =>
        {
            await Task.Delay(TimeSpan.FromMilliseconds(600), ct);
            return 42;
        });

        await act.Should().ThrowAsync<TimeoutRejectedException>();
    }

    private sealed class TestMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options) => new(options.Name, options.Version);
        public void Dispose() { }
    }
}
