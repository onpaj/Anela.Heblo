using Anela.Heblo.Application.Features.Manufacture.DashboardTiles;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;
using Moq;

namespace Anela.Heblo.Tests.Features.Manufacture.DashboardTiles;

public class ManualActionRequiredTileTests
{
    private readonly Mock<IManufactureOrderRepository> _mockRepository;
    private readonly Mock<TimeProvider> _mockTimeProvider;
    private readonly ManualActionRequiredTile _tile;
    private readonly DateTime _fixedDateTime = new(2023, 10, 15, 14, 30, 0, DateTimeKind.Utc);

    public ManualActionRequiredTileTests()
    {
        _mockRepository = new Mock<IManufactureOrderRepository>();
        _mockTimeProvider = new Mock<TimeProvider>();
        _tile = new ManualActionRequiredTile(_mockRepository.Object, _mockTimeProvider.Object);

        _mockTimeProvider.Setup(x => x.GetUtcNow())
            .Returns(new DateTimeOffset(_fixedDateTime));
    }

    [Fact]
    public void Metadata_Properties_ShouldHaveCorrectValues()
    {
        // Assert
        Assert.Equal("Výrobní příkazy", _tile.Title);
        Assert.Equal("Počet výrobních příkazů vyžadujících manuální zásah", _tile.Description);
        Assert.Equal(TileSize.Small, _tile.Size);
        Assert.Equal(TileCategory.Manufacture, _tile.Category);
        Assert.True(_tile.DefaultEnabled);
        Assert.True(_tile.AutoShow);
        Assert.Equal(typeof(object), _tile.ComponentType);
        Assert.Empty(_tile.RequiredPermissions);
    }

    [Fact]
    public async Task LoadDataAsync_WithNoOrders_ShouldReturnZeroCount()
    {
        // Arrange
        var orders = new List<ManufactureOrder>();
        _mockRepository.Setup(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        Assert.NotNull(result);
        
        // Verify repository was called with correct parameters
        _mockRepository.Verify(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WithOrdersButNoManualAction_ShouldReturnZeroCount()
    {
        // Arrange
        var orders = new List<ManufactureOrder>();  // Repository returns empty list when manualActionRequired=true
        _mockRepository.Setup(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        Assert.NotNull(result);
        
        // Verify repository was called with correct parameters
        _mockRepository.Verify(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WithManualActionRequiredOrders_ShouldReturnCorrectCount()
    {
        // Arrange
        var orders = new List<ManufactureOrder>
        {
            new() { Id = 1, ManualActionRequired = true },
            new() { Id = 3, ManualActionRequired = true },
            new() { Id = 4, ManualActionRequired = true }
        };
        _mockRepository.Setup(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(orders);

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        Assert.NotNull(result);
        
        // Verify repository was called with correct parameters
        _mockRepository.Verify(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoadDataAsync_WhenRepositoryThrows_ShouldThrowException()
    {
        // Arrange
        var expectedException = new InvalidOperationException("Database error");
        _mockRepository.Setup(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => _tile.LoadDataAsync());
    }

    [Fact]
    public async Task LoadDataAsync_ShouldPassCancellationTokenToRepository()
    {
        // Arrange
        var cancellationToken = new CancellationToken();
        var orders = new List<ManufactureOrder>();
        _mockRepository.Setup(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            cancellationToken))
            .ReturnsAsync(orders);

        // Act
        await _tile.LoadDataAsync(cancellationToken: cancellationToken);

        // Assert
        _mockRepository.Verify(x => x.GetOrdersAsync(
            It.IsAny<ManufactureOrderState?>(), 
            It.IsAny<DateOnly?>(), 
            It.IsAny<DateOnly?>(),
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            It.IsAny<string?>(), 
            true, 
            cancellationToken), Times.Once);
    }
}