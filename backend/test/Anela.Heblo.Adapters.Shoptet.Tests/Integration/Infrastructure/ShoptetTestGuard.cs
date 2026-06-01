using Microsoft.Extensions.Configuration;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;

/// <summary>
/// Shared guard that prevents integration tests from running against the production Anela store.
/// Call <see cref="Assert"/> at the start of every test that mutates or reads live data.
/// </summary>
public static class ShoptetTestGuard
{
    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> when the supplied configuration looks like
    /// a production environment. Three checks are enforced:
    /// <list type="bullet">
    ///   <item><see cref="!:Shoptet:IsTestEnvironment"/> must be <c>true</c>.</item>
    ///   <item><see cref="!:Shoptet:BaseUrl"/> must not contain "anela".</item>
    ///   <item><see cref="!:Shoptet:ApiToken"/> must start with "780175" (test-store prefix).</item>
    /// </list>
    /// </summary>
    public static void Assert(IConfiguration config)
    {
        var isTest = config.GetValue<bool>("Shoptet:IsTestEnvironment");
        if (!isTest)
            throw new InvalidOperationException(
                "Integration test must not run against live environment. "
                    + "Set Shoptet:IsTestEnvironment=true in test appsettings.json");

        var baseUrl = config["Shoptet:BaseUrl"] ?? string.Empty;
        if (baseUrl.Contains("anela", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Integration test refused: base URL contains 'anela' — this looks like the production store.");

        var apiToken = config["Shoptet:ApiToken"] ?? string.Empty;
        if (!apiToken.StartsWith("780175", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Integration test refused: API token does not start with '780175' — this does not look like the test store token.");
    }
}
