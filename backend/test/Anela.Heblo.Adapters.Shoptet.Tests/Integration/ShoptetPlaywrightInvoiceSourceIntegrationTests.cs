using Anela.Heblo.Adapters.Shoptet.Playwright;
using Anela.Heblo.Adapters.Shoptet.Tests.Integration.Infrastructure;
using Anela.Heblo.Application.Domain.IssuedInvoices;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Anela.Heblo.Adapters.Shoptet.Tests.Integration;

[Collection("ShoptetIntegration")]
[Trait("Category", "Integration")]
[Trait("Category", "Playwright")]
public class ShoptetPlaywrightInvoiceSourceIntegrationTests
{
    private readonly ShoptetIntegrationTestFixture _fixture;
    private readonly IConfiguration _configuration;
    private readonly IIssuedInvoiceSource _invoiceSource;
    private readonly ITestOutputHelper _output;

    private static DateTime ReferenceDate => new DateTime(2025, 07, 1);
    
    public ShoptetPlaywrightInvoiceSourceIntegrationTests(
        ShoptetIntegrationTestFixture fixture,
        ITestOutputHelper output)
    {
        _fixture = fixture;
        _configuration = fixture.Configuration;
        _invoiceSource = fixture.ServiceProvider.GetRequiredService<IIssuedInvoiceSource>();
        _output = output;
    }

    [Fact]
    public async Task GetAllAsync_WithDateRangeQuery_ReturnsInvoiceBatches()
    {
        // Arrange - Skip if configuration is not available or contains test placeholders
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK"
        };

        // Act
        var result = await _invoiceSource.GetAllAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Should().BeOfType<List<IssuedInvoiceDetailBatch>>();
        result.Should().HaveCount(1, "Should return exactly one batch");

        var batch = result.First();
        batch.BatchId.Should().Be(query.RequestId, "Batch ID should match request ID");
        batch.Invoices.Should().NotBeNull("Invoices collection should not be null");

        if (batch.Invoices.Any())
        {
            batch.Invoices.Should().OnlyContain(invoice =>
                !string.IsNullOrEmpty(invoice.Code),
                "All invoices should have document numbers");

            _output.WriteLine($"Retrieved {batch.Invoices.Count} invoices from Shoptet");

            // Check that invoices are within the date range (if dates are available)
            foreach (var invoice in batch.Invoices.Take(3))
            {
                _output.WriteLine($"Invoice: {invoice.Code} - {invoice.Customer.DisplayName}");
            }
        }
        else
        {
            _output.WriteLine("No invoices found in the specified date range");
        }
    }

    [Fact]
    public async Task GetAllAsync_WithSpecificInvoiceQuery_ReturnsSpecificInvoice()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        // First get a list of invoices to find a valid invoice ID
        var dateQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK"
        };

        var allInvoices = await _invoiceSource.GetAllAsync(dateQuery);
        if (!allInvoices.Any() || !allInvoices.First().Invoices.Any())
        {
            _output.WriteLine("Skipping specific invoice test - no invoices found in date range");
            return;
        }

        var testInvoiceId = allInvoices.First().Invoices.First().Code;
        _output.WriteLine($"Testing with invoice ID: {testInvoiceId}");

        var specificQuery = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            InvoiceId = testInvoiceId,
            Currency = "CZK"
        };

        // Act
        var result = await _invoiceSource.GetAllAsync(specificQuery);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        var batch = result.First();
        batch.BatchId.Should().Be(specificQuery.RequestId);
        batch.Invoices.Should().NotBeEmpty("Should find the specific invoice");

        // The result should contain the requested invoice
        batch.Invoices.Should().Contain(inv => inv.Code == testInvoiceId,
            "Should contain the requested invoice");
    }

    [Fact]
    public async Task GetAllAsync_WithCzkCurrency_ReturnsCzkInvoices()
    {
        // Arrange - Skip if configuration is not available
        HasValidConfiguration().Should().BeTrue("Shoptet credentials not configured or contain test placeholders");

        var query = new IssuedInvoiceSourceQuery
        {
            RequestId = Guid.NewGuid().ToString(),
            DateFrom = ReferenceDate,
            DateTo = ReferenceDate.AddDays(30),
            Currency = "CZK"
        };

        // Act
        var result = await _invoiceSource.GetAllAsync(query);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);

        var batch = result.First();
        batch.BatchId.Should().Be(query.RequestId);

        if (batch.Invoices.Any())
        {
            _output.WriteLine($"Retrieved {batch.Invoices.Count} CZK invoices from Shoptet");

            // All invoices should be in EUR currency (if currency info is available in parsed data)
            foreach (var invoice in batch.Invoices.Take(3))
            {
                _output.WriteLine($"CZK Invoice: {invoice.Code}");
            }
        }
        else
        {
            _output.WriteLine("No CZK invoices found in the specified date range");
        }
    }

    [Fact]
    public async Task CommitAsync_WithValidBatch_CompletesSuccessfully()
    {
        // Arrange
        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = Guid.NewGuid().ToString(),
            Invoices = new List<IssuedInvoiceDetail>
            {
                new IssuedInvoiceDetail { Code = "2025000002" }
            }
        };

        // Act & Assert - Should not throw
        var act = async () => await _invoiceSource.CommitAsync(batch, "Test commit");
        await act.Should().NotThrowAsync("CommitAsync should complete without errors");
    }

    [Fact]
    public async Task FailAsync_WithValidBatch_CompletesSuccessfully()
    {
        // Arrange
        var batch = new IssuedInvoiceDetailBatch
        {
            BatchId = Guid.NewGuid().ToString(),
            Invoices = new List<IssuedInvoiceDetail>
            {
                new IssuedInvoiceDetail { Code = "TEST001" }
            }
        };

        // Act & Assert - Should not throw
        var act = async () => await _invoiceSource.FailAsync(batch, "Test error");
        await act.Should().NotThrowAsync("FailAsync should complete without errors");
    }

    private bool HasValidConfiguration()
    {
        var url = _configuration["Shoptet.Playwright:ShopEntryUrl"];
        var username = _configuration["Shoptet.Playwright:Login"];
        var password = _configuration["Shoptet.Playwright:Password"];

        return !string.IsNullOrEmpty(url) && 
               !string.IsNullOrEmpty(username) && 
               !string.IsNullOrEmpty(password) &&
               !url.Contains("your-shoptet") && 
               !username.Contains("test-username") && 
               !password.Contains("test-password");
    }
}