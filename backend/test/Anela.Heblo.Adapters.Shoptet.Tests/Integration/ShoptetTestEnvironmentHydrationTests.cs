using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Features.ShoptetOrders;
using Anela.Heblo.Domain.Features.Catalog.Stock;
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
    private readonly IEshopStockClient _stockClient;
    private readonly ITestOutputHelper _output;

    // Shipping IDs match ShoptetPlaywrightExpeditionListSource constants.
    // The GUIDs for these IDs must be set in appsettings / user secrets
    // under Shoptet:ShippingGuidMap:21 and Shoptet:ShippingGuidMap:6.
    // Custom status IDs are store-specific — configure under Shoptet:StatusId:EXP and Shoptet:StatusId:PACK.
    private readonly IReadOnlyList<OrderDefinition> _seedCatalog;

    private record OrderDefinition(string ExternalCode, int ShippingId, int TargetState, int ProductCount = 1);

    private static readonly string[] CustomerNames =
    [
        "Jana Nováková",
        "Petr Svoboda",
        "Lucie Dvořáčková",
        "Tomáš Procházka",
        "Martina Horáčková",
        "Ondřej Blažek",
        "Eva Kopecká",
        "Jakub Krejčí",
        "Zuzana Pokorná",
        "Martin Vlček",
        "Tereza Marková",
        "Michal Hájek",
        "Lenka Pospíšilová",
        "David Fiala",
        "Alena Veselá",
        "Radek Mašek",
        "Petra Novotná",
        "Lukáš Beneš",
        "Simona Růžičková",
        "Pavel Malý",
        "Kristýna Šimková",
        "Josef Kratochvíl",
        "Barbora Sedláčková",
        "Filip Čermák",
        "Monika Poláková",
        "Vojtěch Liška",
    ];

    public ShoptetTestEnvironmentHydrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<IEshopOrderClient>();
        _stockClient = fixture.ServiceProvider.GetRequiredService<IEshopStockClient>();
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
    public void Guard_WhenFlagFalse_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "false",
                    ["Shoptet:BaseUrl"] = "https://api.test-store.com",
                    ["Shoptet:ApiToken"] = "780175-test-token",
                })
            .Build();

        var act = () => ShoptetTestGuard.Assert(config);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*IsTestEnvironment*");
    }

    [Fact]
    public void Guard_WhenUrlContainsAnela_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "true",
                    ["Shoptet:BaseUrl"] = "https://api.anela.myshoptet.com",
                    ["Shoptet:ApiToken"] = "780175-test-token",
                })
            .Build();

        var act = () => ShoptetTestGuard.Assert(config);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*anela*");
    }

    [Fact]
    public void Guard_WhenApiTokenWrongPrefix_Throws()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "true",
                    ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
                    ["Shoptet:ApiToken"] = "999999-production-token",
                })
            .Build();

        var act = () => ShoptetTestGuard.Assert(config);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*780175*");
    }

    [Fact]
    public void Guard_WhenValidTestConfig_DoesNotThrow()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Shoptet:IsTestEnvironment"] = "true",
                    ["Shoptet:BaseUrl"] = "https://api.myshoptet.com",
                    ["Shoptet:ApiToken"] = "780175-test-token",
                })
            .Build();

        var act = () => ShoptetTestGuard.Assert(config);

        act.Should().NotThrow();
    }

    // ── Hydration ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HydrateTestEnvironment()
    {
        if (Environment.GetEnvironmentVariable("SHOPTET_HYDRATE") != "1")
            return;

        ShoptetTestGuard.Assert(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]!;

        // Pre-fetch all existing TEST- orders in one paginated call to avoid
        // per-order API lookups (externalCode filter is not supported by the API).
        var existingOrders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);
        var existingByExternalCode = existingOrders
            .Where(o => o.ExternalCode != null)
            .ToDictionary(o => o.ExternalCode!, StringComparer.Ordinal);

        _output.WriteLine($"Found {existingOrders.Count} existing TEST- orders.");

        // Pre-fetch all variant codes from the stock CSV export — one call, no N+1.
        // The stock client reads the same CSV that Shoptet generates for the store.
        var stockItems = await _stockClient.ListAsync(ct);
        var variantCodes = stockItems
            .Where(s => s.Stock > 0)
            .Select(s => s.Code)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .ToList();

        var maxProducts = _seedCatalog.Max(d => d.ProductCount);
        if (variantCodes.Count < maxProducts)
            throw new InvalidOperationException(
                $"Not enough distinct variant codes in the store ({variantCodes.Count}) "
                    + $"to seed an order with {maxProducts} products. "
                    + "Add more products to the test store catalog.");

        _output.WriteLine($"Loaded {variantCodes.Count} product variant codes from stock CSV.");

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
                // Seed from ExternalCode so the same order always gets the same amounts/prices/products.
                var rng = new Random(definition.ExternalCode.GetHashCode());
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
                        FullName = $"TEST-{CustomerNames[idx % CustomerNames.Length]}",
                        Street = "Testovaci 1",
                        City = "Praha",
                        Zip = "10000",
                    },
                    Items = BuildOrderItems(definition.ProductCount, shippingName, rng, variantCodes),
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

        ShoptetTestGuard.Assert(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var orders = await _client.ListByExternalCodePrefixAsync("TEST-", "test-seed@heblo.test", ct);

        // Some states block deletion with 409 — try resetting to -2 first, skip if still blocked.
        const int deletableStateId = -2;
        int deleted = 0, skipped = 0;

        foreach (var order in orders)
        {
            try
            {
                if (order.StatusId != deletableStateId)
                    await _client.UpdateStatusAsync(order.Code, deletableStateId, ct);

                await _client.DeleteOrderAsync(order.Code, ct);
                _output.WriteLine($"DELETED  {order.Code} ({order.ExternalCode})");
                deleted++;
            }
            catch (HttpRequestException ex)
            {
                _output.WriteLine($"SKIPPED  {order.Code} ({order.ExternalCode}) — {ex.Message}");
                skipped++;
            }
        }

        _output.WriteLine($"\nDeleted {deleted}, skipped {skipped} test orders.");
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

        // Shipping 21 — multi-product orders
        catalog.Add(new("TEST-ZAK-21-INIT-M2-01", 21, -2, ProductCount: 2));
        catalog.Add(new("TEST-ZAK-21-INIT-M2-02", 21, -2, ProductCount: 2));
        catalog.Add(new("TEST-ZAK-21-INIT-M3-01", 21, -2, ProductCount: 3));
        catalog.Add(new("TEST-ZAK-21-INIT-M4-01", 21, -2, ProductCount: 4));
        catalog.Add(new("TEST-ZAK-21-INIT-M10-01", 21, -2, ProductCount: 10));

        // Shipping 6 — PPL_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-PPL-6-INIT-{i:D2}", 6, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-EXP-{i:D2}", 6, expStatusId));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-PACK-{i:D2}", 6, packStatusId));

        // Shipping 6 — multi-product orders
        catalog.Add(new("TEST-PPL-6-INIT-M2-01", 6, -2, ProductCount: 2));
        catalog.Add(new("TEST-PPL-6-INIT-M2-02", 6, -2, ProductCount: 2));
        catalog.Add(new("TEST-PPL-6-INIT-M3-01", 6, -2, ProductCount: 3));
        catalog.Add(new("TEST-PPL-6-INIT-M4-01", 6, -2, ProductCount: 4));
        catalog.Add(new("TEST-PPL-6-INIT-M10-01", 6, -2, ProductCount: 10));

        return catalog;
    }

    // ── Item builder ──────────────────────────────────────────────────────────

    private static readonly string[] SetCodes = ["SA015030", "SA015005", "SA014005"];

    // Amount distribution: 90 % → 1, 6 % → 2, 3 % → 3, 1 % → 5
    private static int PickAmount(Random rng)
    {
        var roll = rng.Next(100);
        return roll switch
        {
            < 90 => 1,
            < 96 => 2,
            < 99 => 3,
            _ => 5,
        };
    }

    private static List<EshopOrderItem> BuildOrderItems(
        int productCount,
        string shippingName,
        Random rng,
        List<string> variantCodes)
    {
        // Sample without replacement so the same variant code never appears twice in one order
        // (Shoptet rejects orders with duplicate product codes).
        var pool = variantCodes.ToList();
        var items = Enumerable
            .Range(1, productCount)
            .Select(n =>
            {
                var idx = rng.Next(pool.Count);
                var code = pool[idx];
                pool.RemoveAt(idx);

                var amount = PickAmount(rng);
                // Price per piece: random integer in [200, 1100], formatted with 2 dp
                var price = rng.Next(200, 1101);
                return new EshopOrderItem
                {
                    ItemType = "product",
                    Code = code,
                    Name = productCount == 1 ? "Test product" : $"Test product {n}",
                    VatRate = "21",
                    ItemPriceWithVat = $"{price}.00",
                    Amount = amount.ToString(),
                };
            })
            .ToList<EshopOrderItem>();

        // 15% chance to include a product set
        if (rng.Next(100) < 15)
        {
            var setCode = SetCodes[rng.Next(SetCodes.Length)];
            items.Add(new EshopOrderItem
            {
                ItemType = "product-set",
                Code = setCode,
                Name = $"Test set {setCode}",
                VatRate = "21",
                ItemPriceWithVat = $"{rng.Next(200, 1101)}.00",
                Amount = "1",
            });
        }

        items.Add(new EshopOrderItem
        {
            ItemType = "billing",
            Name = "Platba prevod",
            VatRate = "0",
            ItemPriceWithVat = "0.00",
            Amount = "1",
        });
        items.Add(new EshopOrderItem
        {
            ItemType = "shipping",
            Name = shippingName,
            VatRate = "0",
            ItemPriceWithVat = "0.00",
            Amount = "1",
        });

        return items;
    }
}
