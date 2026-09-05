using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostsList;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class GetMarketingCostsListHandlerTests
{
    private readonly Mock<IImportedMarketingTransactionRepository> _repositoryMock;
    private readonly GetMarketingCostsListHandler _handler;

    public GetMarketingCostsListHandlerTests()
    {
        _repositoryMock = new Mock<IImportedMarketingTransactionRepository>();
        _handler = new GetMarketingCostsListHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsPagedResults()
    {
        var items = new List<ImportedMarketingTransaction>
        {
            new()
            {
                Id = 1,
                TransactionId = "tx_001",
                Platform = "GoogleAds",
                Amount = 1250.00m,
                Currency = "CZK",
                TransactionDate = new DateTime(2026, 4, 15),
                ImportedAt = new DateTime(2026, 4, 16),
                IsSynced = true,
            }
        };

        _repositoryMock.Setup(r => r.GetPagedAsync(
            null, null, null, null, null, true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, 1));

        var request = new GetMarketingCostsListRequest { PageNumber = 1, PageSize = 20 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(response.Items);
        Assert.Equal("tx_001", response.Items[0].TransactionId);
        Assert.Equal(1, response.TotalCount);
        Assert.Equal(1, response.TotalPages);
    }

    [Fact]
    public async Task Handle_PassesFiltersToRepository()
    {
        _repositoryMock.Setup(r => r.GetPagedAsync(
            "MetaAds", It.IsAny<DateTime?>(), It.IsAny<DateTime?>(), true, "amount", false, 2, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<ImportedMarketingTransaction>(), 0));

        var request = new GetMarketingCostsListRequest
        {
            Platform = "MetaAds",
            IsSynced = true,
            SortBy = "amount",
            SortDescending = false,
            PageNumber = 2,
            PageSize = 10,
        };

        await _handler.Handle(request, CancellationToken.None);

        _repositoryMock.Verify(r => r.GetPagedAsync(
            "MetaAds", null, null, true, "amount", false, 2, 10, It.IsAny<CancellationToken>()), Times.Once);
    }
}
