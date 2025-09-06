using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureOutput;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class GetManufactureOutputHandlerTests
{
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILogger<GetManufactureOutputHandler>> _loggerMock;
    private readonly GetManufactureOutputHandler _handler;

    public GetManufactureOutputHandlerTests()
    {
        _manufactureHistoryClientMock = new Mock<IManufactureHistoryClient>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _loggerMock = new Mock<ILogger<GetManufactureOutputHandler>>();

        _handler = new GetManufactureOutputHandler(
            _manufactureHistoryClientMock.Object,
            _catalogRepositoryMock.Object,
            _loggerMock.Object);
    }


    [Fact]
    public async Task Handle_ValidRequestWithEmptyHistory_ReturnsSuccessfulResponseWithEmptyData()
    {
        // Arrange
        var request = new GetManufactureOutputRequest
        {
            MonthsBack = 12
        };

        var catalogItems = CreateTestCatalogItems();
        var emptyHistory = new List<ManufactureHistoryRecord>();

        _manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyHistory);

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Months.Should().NotBeNull();
        response.Months.Should().HaveCountGreaterThan(0); // Should contain empty months
        response.Months.Should().AllSatisfy(month => month.TotalOutput.Should().Be(0));
    }

    [Fact]
    public async Task Handle_ValidRequestWithHistory_ReturnsSuccessfulResponseWithData()
    {
        // Arrange
        var request = new GetManufactureOutputRequest
        {
            MonthsBack = 3
        };

        var catalogItems = CreateTestCatalogItems();
        var history = CreateTestManufactureHistory();

        _manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(history);

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Months.Should().NotBeNull();
        response.Months.Should().HaveCountGreaterOrEqualTo(3);

        // Should have at least one month with data
        response.Months.Should().Contain(month => month.TotalOutput > 0);
    }

    [Fact]
    public async Task Handle_NullHistoryFromClient_ReturnsSuccessfulResponseWithEmptyData()
    {
        // Arrange
        var request = new GetManufactureOutputRequest
        {
            MonthsBack = 6
        };

        var catalogItems = CreateTestCatalogItems();

        _manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((List<ManufactureHistoryRecord>)null!);

        _catalogRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItems);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Should().NotBeNull();
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Months.Should().NotBeNull();
        response.Months.Should().HaveCountGreaterThan(0);
        response.Months.Should().AllSatisfy(month => month.TotalOutput.Should().Be(0));
    }

    private List<CatalogAggregate> CreateTestCatalogItems()
    {
        var product1 = new CatalogAggregate
        {
            ProductCode = "PRD001",
            ProductName = "Product 1",
            Type = ProductType.Product
        };
        
        // Set manufacture difficulty through the configuration
        var difficulty1Settings = new List<ManufactureDifficultySetting>
        {
            new ManufactureDifficultySetting { DifficultyValue = 3, ValidFrom = DateTime.MinValue }
        };
        product1.ManufactureDifficultySettings.Assign(difficulty1Settings, DateTime.Now);

        var product2 = new CatalogAggregate
        {
            ProductCode = "PRD002",
            ProductName = "Product 2",
            Type = ProductType.Product
        };
        
        // Set manufacture difficulty through the configuration
        var difficulty2Settings = new List<ManufactureDifficultySetting>
        {
            new ManufactureDifficultySetting { DifficultyValue = 1, ValidFrom = DateTime.MinValue }
        };
        product2.ManufactureDifficultySettings.Assign(difficulty2Settings, DateTime.Now);

        return new List<CatalogAggregate> { product1, product2 };
    }

    private List<ManufactureHistoryRecord> CreateTestManufactureHistory()
    {
        var currentDate = DateTime.Now;
        return new List<ManufactureHistoryRecord>
        {
            new ManufactureHistoryRecord
            {
                ProductCode = "PRD001",
                Date = currentDate.AddDays(-15),
                Amount = 100,
                PricePerPiece = 25.50m,
                PriceTotal = 2550.00m,
                DocumentNumber = "DOC001"
            },
            new ManufactureHistoryRecord
            {
                ProductCode = "PRD002",
                Date = currentDate.AddDays(-10),
                Amount = 50,
                PricePerPiece = 45.00m,
                PriceTotal = 2250.00m,
                DocumentNumber = "DOC002"
            }
        };
    }
}