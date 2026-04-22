using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Adapters.ShoptetApi.IssuedInvoices;
using Anela.Heblo.Domain.Features.Invoices;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
public class ShoptetApiInvoiceSourceIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly ShoptetApiInvoiceSource _source;
    private readonly ITestOutputHelper _output;

    private static DateTime ReferenceDate => new DateTime(2025, 07, 1);

    public ShoptetApiInvoiceSourceIntegrationTests(ShoptetIntegrationTestFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _source = fixture.ServiceProvider.GetRequiredService<ShoptetApiInvoiceSource>();
        _output = output;
    }

    [Fact]
    public async Task GetAllAsync_CzkDateRange_ReturnsSingleBatchWithExpectedShape()
    {
        HasValidApiConfiguration().Should().BeTrue("Shoptet API token not configured");

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK",
        };

        var result = await _source.GetAllAsync(query);

        result.Should().HaveCount(1, "REST adapter always returns exactly one batch");
        var batch = result.Single();
        batch.BatchId.Should().Be(query.RequestId);
        batch.Invoices.Should().NotBeNull();

        if (batch.Invoices.Any())
        {
            batch.Invoices.Should().OnlyContain(i => !string.IsNullOrEmpty(i.Code));
            batch.Invoices.Should().OnlyContain(i => i.Price.CurrencyCode == "CZK");
            _output.WriteLine($"Fetched {batch.Invoices.Count} CZK invoices");
            foreach (var inv in batch.Invoices.Take(3))
                _output.WriteLine($"  {inv.Code} — {inv.Customer.DisplayName} — {inv.Price.WithVat} {inv.Price.CurrencyCode}");
        }
    }

   

    [Fact]
    public async Task GetAllAsync_SpecificInvoiceQuery_ReturnsThatInvoice()
    {
        HasValidApiConfiguration().Should().BeTrue("Shoptet API token not configured");

        var listQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK",
        };
        var list = await _source.GetAllAsync(listQuery);
        if (!list.Single().Invoices.Any())
        {
            _output.WriteLine("No invoices found — cannot run specific lookup test");
            return;
        }

        var targetCode = list.Single().Invoices.First().Code;
        var singleQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            InvoiceId = targetCode,
            Currency = "CZK",
        };

        var result = (await _source.GetAllAsync(singleQuery)).Single().Invoices;
        result.Should().ContainSingle(i => i.Code == targetCode);
    }

    [Fact]
    public async Task GetAllAsync_MultiplePagesOfInvoices_FetchesAllPages()
    {
        HasValidApiConfiguration().Should().BeTrue("Shoptet API token not configured");

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = new DateTime(2024, 1, 1),
            DateTo = new DateTime(2025, 12, 31),
            Currency = "CZK",
        };

        var invoices = (await _source.GetAllAsync(query)).Single().Invoices;
        _output.WriteLine($"Total CZK invoices in 2024-2025: {invoices.Count}");

        if (invoices.Count >= 50)
        {
            invoices.Select(i => i.Code).Should().OnlyHaveUniqueItems("pagination must not duplicate invoices");
        }
    }

    [Fact]
    public async Task CommitAsync_NoOp_DoesNotThrow()
    {
        var batch = new IssuedInvoiceDetailBatch { BatchId = "test" };
        await _source.Invoking(s => s.CommitAsync(batch)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task FailAsync_NoOp_DoesNotThrow()
    {
        var batch = new IssuedInvoiceDetailBatch { BatchId = "test" };
        await _source.Invoking(s => s.FailAsync(batch)).Should().NotThrowAsync();
    }

    private bool HasValidApiConfiguration()
    {
        var token = _fixture.Configuration["Shoptet:ApiToken"];
        return !string.IsNullOrEmpty(token) && token != "xxxxxxxx";
    }
}
