using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.UseCases.CreateManufactureOrder;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Users;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class CreateManufactureOrderHandlerSinglePhaseTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<IProductNameFormatter> _productNameFormatterMock;
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly CreateManufactureOrderHandler _handler;

    public CreateManufactureOrderHandlerSinglePhaseTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _productNameFormatterMock = new Mock<IProductNameFormatter>();
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();
        _timeProviderMock = new Mock<TimeProvider>();

        _handler = new CreateManufactureOrderHandler(
            _repositoryMock.Object,
            _productNameFormatterMock.Object,
            _catalogRepositoryMock.Object,
            _currentUserServiceMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_SinglePhaseManufacturing_ShouldCreateSemiProductFromMainProduct()
    {
        // Arrange
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            ManufactureType = ManufactureType.SinglePhase,
            NewBatchSize = 100,
            ScaleFactor = 1.0,
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD001",
                    ProductName = "Final Product",
                    PlannedQuantity = 50
                }
            }
        };

        var currentUser = new CurrentUser("testuser", "Test User", "test@example.com", true);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(currentUser);

        var currentTime = new DateTime(2023, 10, 20);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(currentTime));

        _repositoryMock.Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("MO-2023-001");

        var catalogProduct = new CatalogAggregate
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Properties = new CatalogProperties { ExpirationMonths = 12 }
        };
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("TEST001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogProduct);

        _productNameFormatterMock.Setup(x => x.ShortProductName("Final Product"))
            .Returns("Final Product");

        var createdOrder = new ManufactureOrder { Id = 1, OrderNumber = "MO-2023-001" };
        _repositoryMock.Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Id);
        Assert.Equal("MO-2023-001", result.OrderNumber);

        _repositoryMock.Verify(x => x.AddOrderAsync(It.Is<ManufactureOrder>(o =>
            o.ManufactureType == ManufactureType.SinglePhase &&
            o.SemiProduct != null &&
            o.SemiProduct.ProductCode == "PROD001" &&
            o.SemiProduct.ProductName == "Final Product" &&
            o.Products.Count == 1 &&
            o.Products[0].SemiProductCode == "PROD001"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_MultiPhaseManufacturing_ShouldCreateSemiProduct()
    {
        // Arrange
        var request = new CreateManufactureOrderRequest
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            ManufactureType = ManufactureType.MultiPhase,
            NewBatchSize = 100,
            ScaleFactor = 1.0,
            PlannedDate = DateOnly.FromDateTime(DateTime.Today),
            Products = new List<CreateManufactureOrderProductRequest>
            {
                new()
                {
                    ProductCode = "PROD001",
                    ProductName = "Final Product",
                    PlannedQuantity = 50
                }
            }
        };

        var currentUser = new CurrentUser("testuser", "Test User", "test@example.com", true);
        _currentUserServiceMock.Setup(x => x.GetCurrentUser()).Returns(currentUser);

        var currentTime = new DateTime(2023, 10, 20);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(currentTime));

        _repositoryMock.Setup(x => x.GenerateOrderNumberAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("MO-2023-001");

        var catalogProduct = new CatalogAggregate
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Properties = new CatalogProperties { ExpirationMonths = 12 }
        };
        _catalogRepositoryMock.Setup(x => x.GetByIdAsync("TEST001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogProduct);

        _productNameFormatterMock.Setup(x => x.ShortProductName("Test Product"))
            .Returns("Test Product");

        var createdOrder = new ManufactureOrder { Id = 1, OrderNumber = "MO-2023-001" };
        _repositoryMock.Setup(x => x.AddOrderAsync(It.IsAny<ManufactureOrder>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdOrder);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.Id);
        Assert.Equal("MO-2023-001", result.OrderNumber);

        _repositoryMock.Verify(x => x.AddOrderAsync(It.Is<ManufactureOrder>(o =>
            o.ManufactureType == ManufactureType.MultiPhase &&
            o.SemiProduct != null &&
            o.Products.Count == 1 &&
            o.Products[0].SemiProductCode == "TEST001"
        ), It.IsAny<CancellationToken>()), Times.Once);
    }
}