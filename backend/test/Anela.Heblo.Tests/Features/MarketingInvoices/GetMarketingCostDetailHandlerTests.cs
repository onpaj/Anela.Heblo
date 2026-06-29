using Anela.Heblo.Application.Features.MarketingInvoices.UseCases.GetMarketingCostDetail;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.MarketingInvoices;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.MarketingInvoices;

public class GetMarketingCostDetailHandlerTests
{
    private readonly Mock<IImportedMarketingTransactionRepository> _repositoryMock;
    private readonly GetMarketingCostDetailHandler _handler;

    public GetMarketingCostDetailHandlerTests()
    {
        _repositoryMock = new Mock<IImportedMarketingTransactionRepository>();
        _handler = new GetMarketingCostDetailHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsDetailWhenFound()
    {
        var entity = new ImportedMarketingTransaction
        {
            Id = 1,
            TransactionId = "tx_001",
            Platform = "GoogleAds",
            Amount = 1250.00m,
            Currency = "CZK",
            TransactionDate = new DateTime(2026, 4, 15),
            ImportedAt = new DateTime(2026, 4, 16),
            IsSynced = true,
            Description = "Brand campaign spend",
            RawData = "{\"budget\": \"123\"}",
        };

        _repositoryMock.Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(entity);

        var request = new GetMarketingCostDetailRequest { Id = 1 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.True(response.Success);
        Assert.NotNull(response.Item);
        Assert.Equal("tx_001", response.Item.TransactionId);
        Assert.Equal("Brand campaign spend", response.Item.Description);
        Assert.Equal("{\"budget\": \"123\"}", response.Item.RawData);
    }

    [Fact]
    public async Task Handle_ReturnsNotFoundWhenMissing()
    {
        _repositoryMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ImportedMarketingTransaction?)null);

        var request = new GetMarketingCostDetailRequest { Id = 999 };
        var response = await _handler.Handle(request, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.Item);
    }
}
