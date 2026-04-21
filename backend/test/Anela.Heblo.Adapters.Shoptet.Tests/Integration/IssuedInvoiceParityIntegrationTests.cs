using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
[Trait("Category", "Parity")]
public class IssuedInvoiceParityIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly ShoptetPlaywrightInvoiceSource _playwright;
    private readonly ShoptetApiInvoiceSource _rest;
    private readonly ITestOutputHelper _output;

    private static DateTime ReferenceDate => new DateTime(2026, 04, 15);

    public IssuedInvoiceParityIntegrationTests(ShoptetIntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _playwright = fixture.ServiceProvider.GetRequiredService<ShoptetPlaywrightInvoiceSource>();
        _rest = fixture.ServiceProvider.GetRequiredService<ShoptetApiInvoiceSource>();
        _output = output;
    }

    [Fact]
    public async Task CzkDateRange_BothAdaptersReturnSameInvoices()
    {
        EnsureBothConfigured();
        await AssertParityAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK",
        });
    }

    [Fact]
    public async Task EurDateRange_BothAdaptersReturnSameInvoices()
    {
        EnsureBothConfigured();
        await AssertParityAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "EUR",
        });
    }

    [Fact]
    public async Task SingleCzkInvoiceLookup_BothAdaptersReturnSameInvoice()
    {
        EnsureBothConfigured();

        var list = await _rest.GetAllAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK",
        });
        if (!list.Single().Invoices.Any()) { _output.WriteLine("No CZK invoices — skipping"); return; }

        await AssertParityAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            InvoiceId = list.Single().Invoices.First().Code,
            Currency = "CZK",
        });
    }

    [Fact]
    public async Task SingleEurInvoiceLookup_BothAdaptersReturnSameInvoice()
    {
        EnsureBothConfigured();

        var list = await _rest.GetAllAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "EUR",
        });
        if (!list.Single().Invoices.Any()) { _output.WriteLine("No EUR invoices — skipping"); return; }

        await AssertParityAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            InvoiceId = list.Single().Invoices.First().Code,
            Currency = "EUR",
        });
    }

    [Fact]
    public async Task EmptyWindow_BothAdaptersReturnNoInvoices()
    {
        EnsureBothConfigured();
        await AssertParityAsync(new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = new DateTime(2020, 1, 1),
            DateTo = new DateTime(2020, 1, 1),
            Currency = "CZK",
        });
    }

    private async Task AssertParityAsync(IssuedInvoiceSourceQuery query)
    {
        var pwQuery = new IssuedInvoiceSourceQuery { RequestId = "pw-" + Guid.NewGuid(), InvoiceId = query.InvoiceId, DateFrom = query.DateFrom, DateTo = query.DateTo, Currency = query.Currency };
        var apiQuery = new IssuedInvoiceSourceQuery { RequestId = "api-" + Guid.NewGuid(), InvoiceId = query.InvoiceId, DateFrom = query.DateFrom, DateTo = query.DateTo, Currency = query.Currency };

        var pwResult = await _playwright.GetAllAsync(pwQuery);
        var apiResult = await _rest.GetAllAsync(apiQuery);

        pwResult.Should().HaveCount(1);
        apiResult.Should().HaveCount(1);

        var pwInvoices = pwResult.Single().Invoices.OrderBy(i => i.Code).ToList();
        var apiInvoices = apiResult.Single().Invoices.OrderBy(i => i.Code).ToList();

        _output.WriteLine($"Playwright: {pwInvoices.Count} invoices, REST: {apiInvoices.Count} invoices");

        var pwCodes = pwInvoices.Select(i => i.Code).ToHashSet();
        var apiCodes = apiInvoices.Select(i => i.Code).ToHashSet();
        pwCodes.Except(apiCodes).Should().BeEmpty(
            $"Codes in Playwright but missing from REST: {string.Join(", ", pwCodes.Except(apiCodes))}");
        apiCodes.Except(pwCodes).Should().BeEmpty(
            $"Codes in REST but missing from Playwright: {string.Join(", ", apiCodes.Except(pwCodes))}");

        for (var i = 0; i < pwInvoices.Count; i++)
        {
            var pw = pwInvoices[i];
            var api = apiInvoices[i];
            _output.WriteLine($"Comparing {pw.Code}");

            api.Should().BeEquivalentTo(pw, opts => opts
                .Excluding(x => x.ChangeTime)
                .Excluding(x => x.OrderCode)
                .Excluding(x => x.ConstSymbol)
                .Excluding(x => x.SpecSymbol)
                .Excluding(x => x.AddressesEqual)
                // Item Code: Pohoda XML export synthesises codes like "SHIPPING21"/"BILLING5" for
                // virtual shipping/billing line items. The REST API returns null for these items.
                // Real product item codes are present in both adapters — this exclusion is intentional.
                .Excluding(info => info.Path.Contains("Items[") && info.Path.EndsWith(".Code"))
                .Using<decimal>(ctx => ctx.Subject.Should().BeApproximately(ctx.Expectation, 0.02m)).WhenTypeIs<decimal>()
                .Using<DateTime>(ctx => ctx.Subject.Date.Should().Be(ctx.Expectation.Date)).WhenTypeIs<DateTime>(),
                $"Invoice {pw.Code} field mismatch");

            api.BillingMethod.Should().Be(pw.BillingMethod,
                $"BillingMethod mismatch on {pw.Code} — update BillingMethodMapper");
            api.ShippingMethod.Should().Be(pw.ShippingMethod,
                $"ShippingMethod mismatch on {pw.Code} — update Shoptet:InvoiceShippingGuidMap");
        }
    }

    private void EnsureBothConfigured()
    {
        var hasPlaywright = !string.IsNullOrEmpty(_fixture.Configuration["ShoptetPlaywright:ShopEntryUrl"])
            && !(_fixture.Configuration["ShoptetPlaywright:ShopEntryUrl"] ?? "").Contains("your-shoptet");
        var hasApi = !string.IsNullOrEmpty(_fixture.Configuration["Shoptet:ApiToken"])
            && _fixture.Configuration["Shoptet:ApiToken"] != "xxxxxxxx";

        (hasPlaywright && hasApi).Should().BeTrue(
            "Both ShoptetPlaywright:* and Shoptet:ApiToken credentials required for parity tests");
    }
}
