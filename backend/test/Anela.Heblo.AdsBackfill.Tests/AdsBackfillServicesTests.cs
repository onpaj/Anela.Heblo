using Anela.Heblo.AdsBackfill;
using Anela.Heblo.Application.Features.MarketingAds.Contracts;
using Anela.Heblo.Application.Features.MarketingAds.Proposals.Execution;
using Anela.Heblo.Application.Features.MarketingAds.Sync;
using Anela.Heblo.Domain.Features.MarketingAds;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.AdsBackfill.Tests;

/// <summary>
/// Builds the tool's real container. Nothing here opens a connection: resolving only constructs the
/// DbContexts and data sources, so a syntactically valid connection string is enough.
/// </summary>
public class AdsBackfillServicesTests
{
    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddAdsBackfill(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AdsDatabase:ConnectionString"] = "Host=localhost;Database=heblo_test;Username=heblo;Password=not-a-secret",
                ["GoogleAds:CustomerId"] = "1234567890",
                ["GoogleAds:OAuth2ClientId"] = "client-id.apps.googleusercontent.com",
                ["GoogleAds:OAuth2ClientSecret"] = "client-secret-value",
                ["GoogleAds:OAuth2RefreshToken"] = "refresh-token-value",
            }).Build())
            .BuildServiceProvider();

    [Fact]
    public void AdsBackfillService_ResolvesFromTheToolsContainer()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<AdsBackfillService>();

        service.Should().NotBeNull();
    }

    [Fact]
    public void ExecutionLookup_IsTheRealOne_SoHebloWritesAreClassifiedDuringABackfill()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAdExecutionLookup>().Should().BeOfType<AdExecutionLookup>();
    }

    [Fact]
    public void HebloDbContext_BuildsItsNpgsqlModel_WithoutConnecting()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        // Building the model exercises the provider's type mappings (pgvector columns included).
        var model = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Model;

        model.FindEntityType(typeof(AdProposalActionExecution)).Should().NotBeNull();
    }
}
