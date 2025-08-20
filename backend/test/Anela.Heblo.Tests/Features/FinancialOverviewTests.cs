using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.API;
using Anela.Heblo.API.Infrastructure.Authentication;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Anela.Heblo.Tests.Features;

public class FinancialOverviewTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly ILedgerService _mockLedgerService;

    public FinancialOverviewTests(WebApplicationFactory<Program> factory)
    {
        _mockLedgerService = Substitute.For<ILedgerService>();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureAppConfiguration((context, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    {"UseMockAuth", "true"}
                });
            });
            builder.ConfigureServices(services =>
            {
                var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILedgerService));
                if (descriptor != null)
                {
                    services.Remove(descriptor);
                }
                services.AddSingleton(_mockLedgerService);
            });
        });

        _client = _factory.CreateClient();

        SetupDefaultMockData();
    }

    private void SetupDefaultMockData()
    {
        var testData = new List<LedgerItem>
        {
            new LedgerItem
            {
                Date = DateTime.UtcNow.AddMonths(-1),
                DocumentNumber = "TEST001",
                DebitAccountNumber = "601",
                Amount = 10000m
            },
            new LedgerItem
            {
                Date = DateTime.UtcNow.AddMonths(-1),
                DocumentNumber = "TEST002",
                DebitAccountNumber = "502",
                Amount = 5000m
            },
            new LedgerItem
            {
                Date = DateTime.UtcNow,
                DocumentNumber = "TEST003",
                DebitAccountNumber = "604",
                Amount = 15000m
            },
            new LedgerItem
            {
                Date = DateTime.UtcNow,
                DocumentNumber = "TEST004",
                DebitAccountNumber = "518",
                Amount = 7000m
            }
        };

        _mockLedgerService.GetLedgerItems(
            Arg.Any<DateTime>(),
            Arg.Any<DateTime>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<IEnumerable<string>>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(testData);
    }

    [Fact]
    public async Task GetFinancialOverview_WithValidPermission_ReturnsSuccessAndData()
    {
        var response = await _client.GetAsync("/api/financialoverview");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetFinancialOverviewResponse>();
        content.Should().NotBeNull();
        content!.Data.Should().NotBeNull();
        content.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task GetFinancialOverview_WithCustomMonths_ReturnsData()
    {
        var response = await _client.GetAsync("/api/financialoverview?months=12");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetFinancialOverviewResponse>();
        content.Should().NotBeNull();
        content!.Data.Should().NotBeNull();
    }

    // TODO: Implement permission test when MockAuthenticationHandler supports dynamic claims
    // For now, the handler always includes the FinancialOverview.View permission

    [Fact]
    public async Task GetFinancialOverview_ReturnsCorrectDataStructure()
    {
        var response = await _client.GetAsync("/api/financialoverview?months=3");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetFinancialOverviewResponse>();
        content.Should().NotBeNull();

        if (content!.Data.Any())
        {
            var firstMonth = content.Data.First();
            firstMonth.Year.Should().BeGreaterThan(0);
            firstMonth.Month.Should().BeInRange(1, 12);
            firstMonth.MonthYearDisplay.Should().NotBeNullOrEmpty();
            firstMonth.FinancialBalance.Should().Be(firstMonth.Income - firstMonth.Expenses);
        }

        content.Summary.TotalBalance.Should().Be(content.Summary.TotalIncome - content.Summary.TotalExpenses);
    }

    [Fact]
    public async Task GetFinancialOverview_WithIncludeStockDataFalse_ReturnsOnlyFinancialData()
    {
        var response = await _client.GetAsync("/api/financialoverview?months=6&includeStockData=false");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetFinancialOverviewResponse>();
        content.Should().NotBeNull();
        content!.Data.Should().NotBeNull();

        // Verify stock data is not included
        if (content.Data.Any())
        {
            var monthData = content.Data.First();
            monthData.StockChanges.Should().BeNull();
            monthData.TotalStockValueChange.Should().BeNull();
            monthData.TotalBalance.Should().BeNull();
        }

        content.Summary.StockSummary.Should().BeNull();
    }

    [Fact]
    public async Task GetFinancialOverview_WithIncludeStockDataTrue_ReturnsFinancialAndStockData()
    {
        var response = await _client.GetAsync("/api/financialoverview?months=6&includeStockData=true");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var content = await response.Content.ReadFromJsonAsync<GetFinancialOverviewResponse>();
        content.Should().NotBeNull();
        content!.Data.Should().NotBeNull();

        // Verify stock data is included (even if empty with placeholder service)
        if (content.Data.Any())
        {
            var monthData = content.Data.First();
            // With placeholder service, stock changes will be null but properties should exist
            monthData.TotalStockValueChange.Should().NotBeNull();
            monthData.TotalBalance.Should().NotBeNull();
        }

        content.Summary.StockSummary.Should().NotBeNull();
    }
}