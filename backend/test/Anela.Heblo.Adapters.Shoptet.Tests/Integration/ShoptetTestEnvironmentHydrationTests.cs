using Anela.Heblo.Adapters.ShoptetApi.Orders;
using Anela.Heblo.Adapters.ShoptetApi.Orders.Model;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
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
    private readonly ShoptetOrderClient _client;
    private readonly ITestOutputHelper _output;

    // Shipping IDs match ShoptetPlaywrightExpeditionListSource constants.
    // The GUIDs for these IDs must be set in appsettings / user secrets
    // under Shoptet:ShippingGuidMap:21 and Shoptet:ShippingGuidMap:6.
    private static readonly IReadOnlyList<OrderDefinition> SeedCatalog = BuildSeedCatalog();

    private record OrderDefinition(string ExternalCode, int ShippingId, int TargetState);

    public ShoptetTestEnvironmentHydrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _client = fixture.ServiceProvider.GetRequiredService<ShoptetOrderClient>();
        _output = output;
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
        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var paymentGuid = _configuration["Shoptet:PaymentMethodGuid"]!;

        int created = 0,
            reset = 0,
            skipped = 0;

        foreach (var definition in SeedCatalog)
        {
            var shippingGuid =
                _configuration[$"Shoptet:ShippingGuidMap:{definition.ShippingId}"]
                ?? throw new InvalidOperationException(
                    $"Missing ShippingGuidMap entry for shippingId={definition.ShippingId}. "
                        + "Add it to user secrets under Shoptet:ShippingGuidMap:{id}.");

            var existing = await _client.FindByExternalCodeAsync(definition.ExternalCode, ct);

            if (existing is null)
            {
                var request = new CreateOrderRequest
                {
                    Email = "test-seed@heblo.test",
                    ExternalCode = definition.ExternalCode,
                    ShippingGuid = shippingGuid,
                    PaymentMethodGuid = paymentGuid,
                    Currency = new OrderCurrency { Code = "CZK" },
                    BillingAddress = new OrderAddress
                    {
                        FullName = "Test Heblo",
                        Street = "Testovací 1",
                        City = "Praha",
                        Zip = "10000",
                    },
                    Items = new List<OrderItem>
                    {
                        new OrderItem
                        {
                            ItemType = "product",
                            Code = "TEST-ITEM",
                            Name = "Test product",
                            VatRate = 21,
                            ItemPriceWithVat = 1.00m,
                            Quantity = 1,
                        },
                    },
                };

                var code = await _client.CreateOrderAsync(request, ct);

                // Newly created orders may land in a default status — reset to target
                await _client.UpdateStatusAsync(code, definition.TargetState, ct);

                _output.WriteLine($"CREATED  {definition.ExternalCode} → status {definition.TargetState}");
                created++;
            }
            else if (existing.Status.Id != definition.TargetState)
            {
                await _client.UpdateStatusAsync(existing.Code, definition.TargetState, ct);
                _output.WriteLine(
                    $"RESET    {definition.ExternalCode}: {existing.Status.Id} → {definition.TargetState}");
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
        (created + reset + skipped).Should().Be(SeedCatalog.Count);
    }

    // ── Purge ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeTestOrders()
    {
        AssertTestEnvironment(_configuration);

        var ct = new CancellationTokenSource(TimeSpan.FromMinutes(5)).Token;
        var orders = await _client.ListByExternalCodePrefixAsync("TEST-", ct);

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

    private static IReadOnlyList<OrderDefinition> BuildSeedCatalog()
    {
        var catalog = new List<OrderDefinition>();

        // Shipping 21 — ZASILKOVNA_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-ZAK-21-INIT-{i:D2}", 21, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-EXP-{i:D2}", 21, 55));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-ZAK-21-PACK-{i:D2}", 21, 26));

        // Shipping 6 — PPL_DO_RUKY
        for (int i = 1; i <= 9; i++)
            catalog.Add(new($"TEST-PPL-6-INIT-{i:D2}", 6, -2));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-EXP-{i:D2}", 6, 55));
        for (int i = 1; i <= 2; i++)
            catalog.Add(new($"TEST-PPL-6-PACK-{i:D2}", 6, 26));

        return catalog;
    }
}
