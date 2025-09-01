using System.Net;
using System.Net.Http.Json;
using Anela.Heblo.Application.Features.FinancialOverview.Model;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Tests.Common;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features;

public class FinancialOverviewTests : IClassFixture<FinancialOverviewTestFactory>
{
    private readonly FinancialOverviewTestFactory _factory;
    private readonly HttpClient _client;
    private readonly Mock<ILedgerService> _mockLedgerService;

    public FinancialOverviewTests(FinancialOverviewTestFactory factory)
    {
        _factory = factory;
        _mockLedgerService = factory.MockLedgerService;
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

        _mockLedgerService
            .Setup(x => x.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(testData);
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

public class FinancialOverviewTestFactory : HebloWebApplicationFactory
{
    public Mock<ILedgerService> MockLedgerService { get; }

    public FinancialOverviewTestFactory()
    {
        MockLedgerService = new Mock<ILedgerService>();
    }

    protected override void ConfigureTestServices(IServiceCollection services)
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ILedgerService));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
        services.AddSingleton(MockLedgerService.Object);
    }
}