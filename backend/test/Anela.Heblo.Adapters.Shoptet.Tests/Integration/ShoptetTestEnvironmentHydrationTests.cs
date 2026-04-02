using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetTestEnvironmentHydrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IEshopOrderClient _client;
    private readonly ITestOutputHelper _output;

    // Shipping IDs match ShoptetPlaywrightExpeditionListSource constants.
    // The GUIDs for these IDs must be set in appsettings / user secrets
    // under Shoptet:ShippingGuidMap:21 and Shoptet:ShippingGuidMap:6.
    // Custom status IDs are store-specific — configure under Shoptet:StatusId:EXP and Shoptet:StatusId:PACK.
    private readonly IReadOnlyList<OrderDefinition> _seedCatalog;

    private record OrderDefinition(string ExternalCode, int ShippingId, int TargetState);

    private static readonly string[] CustomerNames =
    [
        "Jana Nováková", "Petr Svoboda", "Lucie Dvořáčková", "Tomáš Procházka",
        "Martina Horáčková", "Ondřej Blažek", "Eva Kopecká", "Jakub Krejčí",
        "Zuzana Pokorná", "Martin Vlček", "Tereza Marková", "Michal Hájek",
        "Lenka Pospíšilová", "David Fiala", "Alena Veselá", "Radek Mašek",
        "Petra Novotná", "Lukáš Beneš", "Simona Růžičková", "Pavel Malý",
        "Kristýna Šimková", "Josef Kratochvíl", "Barbora Sedláčková", "Filip Čermák",
        "Monika Poláková", "Vojtěch Liška",
    ];

    public ShoptetTestEnvironmentHydrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _output = output;

        var expStatusId = _configuration.GetValue<int?>("Shoptet:StatusId:EXP")
            ?? throw new InvalidOperationException(
                "Missing Shoptet:StatusId:EXP in configuration. "
                    + "Add it to user secrets — use GET /api/eshop?include=orderStatuses to discover valid IDs.");
        var packStatusId = _configuration.GetValue<int?>("Shoptet:StatusId:PACK")
            ?? throw new InvalidOperationException(
                "Missing Shoptet:StatusId:PACK in configuration. "
                    + "Add it to user secrets — use GET /api/eshop?include=orderStatuses to discover valid IDs.");

        _seedCatalog = BuildSeedCatalog(expStatusId, packStatusId);
    }

    // ── Guard tests ───────────────────────────────────────────────────────────

    [Fact]
    public void AssertTestEnvironment_WhenFlagFalse_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "false",
                    ["Shoptet:BaseUrl"] = "https://api.test-store.com",
                })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IsTestEnvironment*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenUrlContainsAnela_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "true",
                    ["Shoptet:BaseUrl"] = "https://api.anela.myshoptet.com",
                })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*anela*");
    }

    [Fact]
    public void AssertTestEnvironment_WhenValidTestConfig_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "true",
                    ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
                })
            .Build();

        var act = () => AssertTestEnvironment(config);

        act.Should().NotThrow();
    }

    // ── Hydration ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HydrateTestEnvironment()
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_HYDRATE") != "1")
            return;

        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]!;

        // Pre-fetch all existing TEST- orders in one paginated call to avoid
        // per-order API lookups (externalCode filter is not supported by the API).
        var existingOrders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
        var existingByExternalCode = existingOrders
            .Where(o => o.ExternalCode != null)
            .ToDictionary(o => o.ExternalCode!, StringComparer.Ordinal);

        _output.WriteLine($"Found {existingOrders.Count} existing TEST- orders.");

        int created = 0,
            reset = 0,
            skipped = 0;

        foreach (var (definition, idx) in _seedCatalog.Select((d, i) => (d, i)))
        {
            var shippingGuid =
                _configuration[$"Shoptet:ShippingGuidMap:{definition.ShippingId}"]
                ?? throw new InvalidOperationException(
                    $"Missing ShippingGuidMap entry for shippingId={definition.ShippingId}. "
                        + "Add it to user secrets under Shoptet:ShippingGuidMap:{id}.");

            existingByExternalCode.TryGetValue(definition.ExternalCode, out var existing);

            if (existing is null)
            {
                var shippingName = definition.ShippingId == 21 ? "Zásilkovna (do ruky)" : "PPL (do ruky)";
                var request = new CreateEshopOrderRequest
                {
                    Email = "test-seed@heblo.test",
                    Phone = "+420725191660",
                    ExternalCode = definition.ExternalCode,
                    ShippingGuid = shippingGuid,
                    PaymentMethodGuid = paymentGuid,
                    CurrencyCode = "CZK",
                    BillingAddress = new EshopOrderAddress
                    {
                        FullName = CustomerNames[idx % CustomerNames.Length],
                        Street = "Testovaci 1",
                        City = "Praha",
                        Zip = "10000",
                    },
                    Items = new List<EshopOrderItem>
                    {
                        // OCH001030 is a real product variant code required by the Shoptet API.
                        // suppressProductChecking is not supported via REST.
                        new EshopOrderItem
                        {
                            ItemType = "product",
                            Code = "OCH001030",
                            Name = "Test product",
                            VatRate = "21",
                            ItemPriceWithVat = "1.00",
                            Amount = "1",
                        },
                        new EshopOrderItem
                        {
                            ItemType = "billing",
                            Name = "Platba prevod",
                            VatRate = "0",
                            ItemPriceWithVat = "0.00",
                            Amount = "1",
                        },
                        new EshopOrderItem
                        {
                            ItemType = "shipping",
                            Name = shippingName,
                            VatRate = "0",
                            ItemPriceWithVat = "0.00",
                            Amount = "1",
                        },
                    },
                };

                var code = await _client.CreateOrderAsync(request, ct);

                // Newly created orders may land in a default status — reset to target
                await _client.UpdateStatusAsync(code, definition.TargetState, ct);

                _output.WriteLine($"CREATED  {definition.ExternalCode} → status {definition.TargetState}");
                created++;
            }
            else if (existing.StatusId != definition.TargetState)
            {
                await _client.UpdateStatusAsync(existing.Code, definition.TargetState, ct);
                _output.WriteLine(
                    $"RESET    {definition.ExternalCode}: {existing.StatusId} → {definition.TargetState}");
                reset++;
            }
            else
            {
                _output.WriteLine(
                    $"OK       {definition.ExternalCode} already in state {definition.TargetState}");
                skipped++;
            }
        }

        _output.WriteLine($"\nDone — created={created} reset={reset} skipped={skipped}");
        (created + reset + skipped).Should().Be(_seedCatalog.Count);
    }

    // ── Purge ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeTestOrders()
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_HYDRATE") != "1")
            return;

        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var orders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);

        foreach (var order in orders)
        {
            await _client.DeleteOrderAsync(order.Code, ct);
            _output.WriteLine($"DELETED  {order.Code} ({order.ExternalCode})");
        }

        _output.WriteLine($"\nDeleted {orders.Count} test orders.");
    }

    // ── Guard helper ──────────────────────────────────────────────────────────

    private static void AssertTestEnvironment(IConfiguration config)
    {
        var isTest = config.GetValue<bool>("Shoptet:IsTestEnvironment");
        if (!isTest)
            throw new InvalidOperationException(
                "Hydration must not run against live environment. "
                    + "Set Shoptet:IsTestEnvironment=true in test appsettings.json");

        var baseUrl = config["Shoptet:BaseUrl"] ?? string.Empty;
        if (baseUrl.Contains("anela", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Hydration refused: base URL contains 'anela' — this looks like the production store.");
    }

    // ── Seed catalog ──────────────────────────────────────────────────────────

    private static IReadOnlyList<OrderDefinition> BuildSeedCatalog(int expStatusId, int packStatusId)
    {
        var catalog = new List<OrderDefinition>();

        // Shipping 21 — ZASILKOVNA_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-ZAK-21-INIT-{i:D2}", 21, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-EXP-{i:D2}", 21, expStatusId));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-PACK-{i:D2}", 21, packStatusId));

        // Shipping 6 — PPL_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-PPL-6-INIT-{i:D2}", 6, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-EXP-{i:D2}", 6, expStatusId));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-PACK-{i:D2}", 6, packStatusId));

        return catalog;
    }
}
